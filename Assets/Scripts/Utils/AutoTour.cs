using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using RetroLOTR.Scenarios;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Hands-free visual/regression tour, enabled from the command line:
///   Runeboard.exe -autotour &lt;outDir&gt; [-autotour-leader Gandalf] [-autotour-turns 2]
/// Starts the 2950 campaign as the given leader with no menus, intro or tutorial, stages a fixed
/// set of scenes (HUD, card preview, zoomed-out map, AI turns, save/load, end screen), writes a
/// screenshot of each to outDir plus "[AutoTour]" timing lines to the player log, then quits.
/// Audio is muted so it can run in the background while someone else uses the machine.
/// </summary>
public static class AutoTour
{
    public static bool Active { get; private set; }
    public static string OutDir { get; private set; }
    public static string Leader { get; private set; } = "Gandalf";
    public static int Turns { get; private set; } = 2;
    /// <summary>-autotour-title: photograph the title/menu screens instead of playing.</summary>
    public static bool TitleOnly { get; private set; }
    /// <summary>-autotour-film: slow camera moves and no screenshots, for recording a trailer.</summary>
    public static bool Film { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-autotour") { Active = true; OutDir = args[i + 1]; }
            else if (args[i] == "-autotour-leader") Leader = args[i + 1];
            else if (args[i] == "-autotour-turns" && int.TryParse(args[i + 1], out int t)) Turns = t;
        }
        if (!Active) return;

        Directory.CreateDirectory(OutDir);
        TitleOnly = System.Array.IndexOf(args, "-autotour-title") >= 0;
        Film = System.Array.IndexOf(args, "-autotour-film") >= 0;
        if (TitleOnly)
        {
            AudioListener.volume = 0f;
            var titleRunner = new GameObject("AutoTourRunner").AddComponent<AutoTourRunner>();
            UnityEngine.Object.DontDestroyOnLoad(titleRunner.gameObject);
            Log("started title tour");
            return;
        }
        GameConfig.ScenarioToLoad = "2950";
        GameConfig.ScenarioChosen = true;
        GameConfig.SkipIntro = true;
        AudioListener.volume = 0f;
        var runner = new GameObject("AutoTourRunner").AddComponent<AutoTourRunner>();
        UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);
        Log($"started out={OutDir} leader={Leader} turns={Turns}");
    }

    public static void Log(string message) => Debug.Log($"[AutoTour] {message}");
}

public class AutoTourRunner : MonoBehaviour
{
    private int shot;

    private IEnumerator Start()
    {
        Stopwatch clock = Stopwatch.StartNew();
        if (AutoTour.TitleOnly)
        {
            yield return WaitFor(() => FindAnyObjectByType<StartScreenController>() is StartScreenController s && s.isActiveAndEnabled && s.IsShowing, 600f, "title screen");
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("title");
            DumpHud();
            FindAnyObjectByType<StartScreenController>().StartCampaign();
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("campaign_select");
            AutoTour.Log("done");
            Application.Quit();
            yield break;
        }
        yield return WaitFor(() => FindAnyObjectByType<LeaderSelector>() is LeaderSelector ls && ls.SelectionScreenShown, 600f, "leader selection");
        if (AutoTour.Film) { yield return Film(clock); yield break; }
        yield return new WaitForSecondsRealtime(2f);
        yield return Shot("leader_selection");
        yield return WaitFor(() => Game.Instance != null && Game.Instance.started && Game.Instance.IsPlayerCurrentlyPlaying() && Game.Instance.IsAcceptingPlayerInput, 900f, "first human turn");
        AutoTour.Log($"campaign ready after {clock.Elapsed.TotalSeconds:0.0}s");
        yield return new WaitForSecondsRealtime(3f);
        yield return Shot("turn_start");
        DumpHud();

        if (HandTray.Instance != null && HandTray.Instance.DebugPreview(0))
        {
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("card_preview");
            DumpHud("hud_dump_preview.txt");
            HandTray.Instance.DebugPreview(-1);
            CardCenterPreview.Instance?.HidePreview();
            yield return new WaitForSecondsRealtime(1f);
        }

        BoardNavigator nav = BoardNavigator.Instance;
        if (nav != null)
        {
            float before = Camera.main != null ? Camera.main.orthographicSize : 5f;
            nav.DebugSetZoom(nav.maxZoom);
            yield return new WaitForSecondsRealtime(2f);
            foreach (Camera cam in Camera.allCameras)
                AutoTour.Log($"camera '{cam.name}' ortho={cam.orthographic} size={cam.orthographicSize:0.00} depth={cam.depth} target={(cam.targetTexture != null ? cam.targetTexture.name : "screen")} main={(cam == Camera.main)} navCam={(cam.gameObject == nav.gameObject)} enabled={cam.enabled}");
            AutoTour.Log($"navigator enabled={nav.enabled} active={nav.isActiveAndEnabled} min={nav.minZoom} max={nav.maxZoom}");
            yield return Shot("zoomed_out");
            nav.DebugSetZoom(nav.minZoom);
            Character selected = Board.Instance != null ? Board.Instance.selectedCharacter : null;
            if (selected != null && selected.hex != null) nav.LookAt(selected.hex.transform.position, 0.2f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("closeup");
            // -autotour-hide "nameA;nameB": extra close-ups with each named renderer group hidden, to
            // find which board layer draws a visual artefact.
            string[] cmd = Environment.GetCommandLineArgs();
            int hideAt = Array.IndexOf(cmd, "-autotour-hide");
            if (hideAt >= 0 && hideAt + 1 < cmd.Length)
            {
                var census = new System.Collections.Generic.Dictionary<string, int>();
                foreach (Renderer r in FindObjectsByType<Renderer>())
                    if (r.enabled && r.gameObject.activeInHierarchy)
                        census[r.gameObject.name] = census.TryGetValue(r.gameObject.name, out int c) ? c + 1 : 1;
                foreach (var kv in census) AutoTour.Log($"renderer '{kv.Key}' x{kv.Value}");
                foreach (string group in cmd[hideAt + 1].Split(';'))
                {
                    var names = new System.Collections.Generic.HashSet<string>(group.Split(','));
                    var hidden = new System.Collections.Generic.List<Renderer>();
                    foreach (Renderer r in FindObjectsByType<Renderer>())
                        if (r.enabled && names.Contains(r.gameObject.name)) { r.enabled = false; hidden.Add(r); }
                    AutoTour.Log($"hide '{group}': {hidden.Count} renderers");
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return Shot("closeup_hide_" + group.Replace(',', '+'));
                    foreach (Renderer r in hidden) if (r != null) r.enabled = true;
                }
            }
            nav.DebugSetZoom(before);
            yield return new WaitForSecondsRealtime(1f);
        }

        for (int t = 0; t < AutoTour.Turns; t++)
        {
            Stopwatch turnClock = Stopwatch.StartNew();
            int turnBefore = Game.Instance.turn;
            Game.Instance.EndTurnWithoutPrompt();
            yield return WaitFor(() => Game.Instance.turn > turnBefore && Game.Instance.IsPlayerCurrentlyPlaying() && Game.Instance.IsAcceptingPlayerInput, 600f, "next human turn");
            AutoTour.Log($"round {turnBefore}->{Game.Instance.turn} took {turnClock.Elapsed.TotalSeconds:0.0}s");
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot($"turn_{Game.Instance.turn}");
        }

        CampaignObjectives objectivesForShot = CampaignObjectives.Instance;
        if (objectivesForShot != null && ObjectiveGuide.Instance != null)
        {
            foreach (CampaignObjective o in objectivesForShot.GetObjectives(Game.Instance.player))
            {
                if (objectivesForShot.GetTargetHexes(o).Count == 0) continue;
                ObjectiveGuide.Instance.FocusObjective(o);
                yield return new WaitForSecondsRealtime(1.6f);
                yield return Shot("objective_focus");
                break;
            }
            yield return new WaitForSecondsRealtime(2f);
        }

        if (SaveSystem.Save(out string error)) AutoTour.Log("save ok");
        else AutoTour.Log($"save FAILED: {error}");

        CampaignHud.ShowEndScreen(true, "Auto tour: end screen preview.");
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot("end_screen");

        AutoTour.Log($"done after {clock.Elapsed.TotalSeconds:0.0}s");
        Application.Quit();
    }

    // Film mode: the same campaign start, but with slow camera moves over the realm's towns and
    // characters, card previews and the AI's turns. The game records its own frames to
    // <outDir>/frames (FilmRecorder), never the desktop, so the window may sit behind others.
    // "[AutoTour] film mark <name>" lines give each beat's time in the recording for the edit.
    private IEnumerator Film(Stopwatch clock)
    {
        Application.runInBackground = true;
        StartCoroutine(HideCursor());
        FilmRecorder recorder = gameObject.AddComponent<FilmRecorder>();
        recorder.Begin(Path.Combine(AutoTour.OutDir, "frames"));
        Mark("leader_selection");
        yield return WaitFor(() => Game.Instance != null && Game.Instance.started && Game.Instance.IsPlayerCurrentlyPlaying() && Game.Instance.IsAcceptingPlayerInput, 900f, "first human turn");
        Mark("campaign_ready");
        yield return new WaitForSecondsRealtime(5f);

        BoardNavigator nav = BoardNavigator.Instance;
        PlayableLeader player = Game.Instance.player;
        if (nav != null && player != null)
        {
            float home = Camera.main != null ? Camera.main.orthographicSize : 5f;
            float town = Mathf.Lerp(nav.minZoom, nav.maxZoom, 0.35f);
            float close = nav.minZoom;
            Character first = player.controlledCharacters.Find(c => c != null && !c.killed && c.hex != null);
            PC capital = player.controlledPcs.Find(p => p?.hex != null);
            Vector3 start = first != null ? first.hex.transform.position : capital != null ? capital.hex.transform.position : Vector3.zero;

            Mark("pull_back");
            yield return nav.FilmMove(start, nav.maxZoom, 6f);
            yield return new WaitForSecondsRealtime(1.5f);

            Mark("towns");
            int towns = 0;
            foreach (PC pc in player.controlledPcs)
            {
                if (pc?.hex == null || towns++ >= 4) continue;
                yield return nav.FilmMove(pc.hex.transform.position, town, 3.5f);
                yield return new WaitForSecondsRealtime(2f);
            }

            Mark("characters");
            int shown = 0;
            foreach (Character c in player.controlledCharacters)
            {
                if (c == null || c.killed || c.hex == null || shown++ >= 3) continue;
                yield return nav.FilmMove(c.hex.transform.position, close, 3f);
                yield return new WaitForSecondsRealtime(2.5f);
            }

            yield return nav.FilmMove(start, home, 3f);
        }

        if (HandTray.Instance != null)
        {
            Mark("cards");
            for (int i = 0; i < 2 && HandTray.Instance.DebugPreview(i); i++)
                yield return new WaitForSecondsRealtime(3f);
            HandTray.Instance.DebugPreview(-1);
            CardCenterPreview.Instance?.HidePreview();
            yield return new WaitForSecondsRealtime(1.5f);
        }

        for (int t = 0; t < AutoTour.Turns; t++)
        {
            int turnBefore = Game.Instance.turn;
            Mark($"end_turn_{turnBefore}");
            Game.Instance.EndTurnWithoutPrompt();
            yield return WaitFor(() => Game.Instance.turn > turnBefore && Game.Instance.IsPlayerCurrentlyPlaying() && Game.Instance.IsAcceptingPlayerInput, 600f, "next human turn");
            Mark($"turn_{Game.Instance.turn}");
            yield return new WaitForSecondsRealtime(4f);
        }

        CampaignObjectives objectives = CampaignObjectives.Instance;
        if (objectives != null && ObjectiveGuide.Instance != null)
        {
            foreach (CampaignObjective o in objectives.GetObjectives(Game.Instance.player))
            {
                if (objectives.GetTargetHexes(o).Count == 0) continue;
                Mark("objective");
                ObjectiveGuide.Instance.FocusObjective(o);
                yield return new WaitForSecondsRealtime(5f);
                break;
            }
        }

        yield return recorder.Finish();
        AutoTour.Log($"done after {clock.Elapsed.TotalSeconds:0.0}s");
        Application.Quit();

        void Mark(string beat) => AutoTour.Log($"film mark {beat} t={recorder.Seconds:0.00} zoom={(Camera.main != null ? Camera.main.orthographicSize : -1f):0.00}");
    }

    private static IEnumerator HideCursor()
    {
        while (true)
        {
            Cursor.visible = false;
            yield return null;
        }
    }

    // Writes every active RectTransform under each screen-space canvas (path, anchors, position,
    // size, scale and notable components) so HUD layout work can be planned from real numbers.
    private static void DumpHud() => DumpHud("hud_dump.txt");

    private static void DumpHud(string file)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Canvas canvas in FindObjectsByType<Canvas>())
        {
            if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            RectTransform root = canvas.transform as RectTransform;
            sb.AppendLine($"# CANVAS {canvas.name} mode={canvas.renderMode} order={canvas.sortingOrder} rect={root.rect.size} scale={canvas.scaleFactor:0.###} ref={(scaler != null ? scaler.referenceResolution.ToString() : "-")} match={(scaler != null ? scaler.matchWidthOrHeight : -1)}");
            DumpNode(root, "", 0, sb);
        }
        File.WriteAllText(Path.Combine(AutoTour.OutDir, file), sb.ToString());
        AutoTour.Log("hud dump written");
    }

    private static void DumpNode(RectTransform rt, string path, int depth, System.Text.StringBuilder sb)
    {
        if (depth > 11 || !rt.gameObject.activeInHierarchy) return;
        string p = path.Length == 0 ? rt.name : path + "/" + rt.name;
        string comps = string.Join(",", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(rt.GetComponents<Component>(), c => c != null && !(c is RectTransform) && !(c is CanvasRenderer)), c => c.GetType().Name));
        var tmp = rt.GetComponent<TMPro.TMP_Text>();
        string text = tmp != null ? $" text='{tmp.text.Replace('\n', ' ').Substring(0, Mathf.Min(30, tmp.text.Length))}' font={tmp.font?.name} size={tmp.fontSize}" : "";
        sb.AppendLine($"{new string(' ', depth * 2)}{p} | aMin={rt.anchorMin} aMax={rt.anchorMax} piv={rt.pivot} pos={rt.anchoredPosition} size={rt.sizeDelta} rect={rt.rect.size} scl={rt.localScale.x:0.##} [{comps}]{text}");
        foreach (Transform child in rt)
            if (child is RectTransform c) DumpNode(c, p, depth + 1, sb);
    }

    private IEnumerator WaitFor(Func<bool> condition, float timeoutSeconds, string what)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                AutoTour.Log($"TIMEOUT waiting for {what}");
                yield return Shot($"timeout_{what.Replace(' ', '_')}");
                Application.Quit();
                yield break;
            }
            yield return null;
        }
    }

    private IEnumerator Shot(string name)
    {
        string path = Path.Combine(AutoTour.OutDir, $"{++shot:00}_{name}.png");
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(path);
        yield return null;
        yield return null;
        AutoTour.Log($"shot {path}");
    }
}

/// <summary>
/// Records the game's own frames for AutoTour film mode: about 30 times a second it copies the
/// finished frame into a render texture, reads it back asynchronously and writes a JPEG on a
/// worker thread. frames.ffconcat lists every frame with how long it was on screen, so the
/// video keeps real time even when frames are dropped:
///   ffmpeg -f concat -safe 0 -i frames.ffconcat -vsync vfr -r 30 out.mp4
/// </summary>
public class FilmRecorder : MonoBehaviour
{
    private const float Interval = 1f / 30f;
    private const int MaxPending = 6;

    private string dir;
    private RenderTexture target;
    private readonly Stopwatch clock = new();
    private readonly System.Collections.Generic.List<(string file, double time)> frames = new();
    private int pending, dropped;
    private bool recording;

    /// <summary>Seconds since recording began (the time in the finished video).</summary>
    public float Seconds => (float)clock.Elapsed.TotalSeconds;

    public void Begin(string outDir)
    {
        dir = outDir;
        Directory.CreateDirectory(dir);
        recording = true;
        clock.Start();
        StartCoroutine(Capture());
    }

    private IEnumerator Capture()
    {
        var endOfFrame = new WaitForEndOfFrame();
        double next = 0;
        while (recording)
        {
            yield return endOfFrame;
            double now = clock.Elapsed.TotalSeconds;
            if (now < next) continue;
            next = now + Interval;
            if (pending >= MaxPending) { dropped++; continue; }

            if (target == null || target.width != Screen.width || target.height != Screen.height)
            {
                if (target != null) target.Release();
                target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
            string file = $"f{frames.Count:000000}.jpg";
            frames.Add((file, now));
            int w = target.width, h = target.height;
            string path = Path.Combine(dir, file);
            pending++;
            UnityEngine.Rendering.AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError) { pending--; return; }
                byte[] pixels = request.GetData<byte>().ToArray();
                System.Threading.Tasks.Task.Run(() =>
                {
                    byte[] jpg = ImageConversion.EncodeArrayToJPG(pixels, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, 92);
                    File.WriteAllBytes(path, jpg);
                    System.Threading.Interlocked.Decrement(ref pending);
                });
            });
        }
    }

    /// <summary>Stops recording, waits for the last frames to be written and writes the frame list.</summary>
    public IEnumerator Finish()
    {
        recording = false;
        double end = clock.Elapsed.TotalSeconds;
        float deadline = Time.realtimeSinceStartup + 30f;
        while (pending > 0 && Time.realtimeSinceStartup < deadline) yield return null;

        var list = new System.Text.StringBuilder("ffconcat version 1.0\n");
        for (int i = 0; i < frames.Count; i++)
        {
            if (!File.Exists(Path.Combine(dir, frames[i].file))) continue;
            double until = i + 1 < frames.Count ? frames[i + 1].time : end;
            list.Append($"file '{frames[i].file}'\nduration {(until - frames[i].time).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}\n");
        }
        File.WriteAllText(Path.Combine(dir, "frames.ffconcat"), list.ToString());
        AutoTour.Log($"film recorder frames={frames.Count} dropped={dropped} seconds={end:0.0}");
        if (target != null) target.Release();
    }
}
