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
            yield return WaitFor(() => FindFirstObjectByType<StartScreenController>() is StartScreenController s && s.isActiveAndEnabled && s.IsShowing, 600f, "title screen");
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("title");
            DumpHud();
            FindFirstObjectByType<StartScreenController>().StartCampaign();
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("campaign_select");
            AutoTour.Log("done");
            Application.Quit();
            yield break;
        }
        yield return WaitFor(() => FindFirstObjectByType<LeaderSelector>() is LeaderSelector ls && ls.SelectionScreenShown, 600f, "leader selection");
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

    // Writes every active RectTransform under each screen-space canvas (path, anchors, position,
    // size, scale and notable components) so HUD layout work can be planned from real numbers.
    private static void DumpHud()
    {
        var sb = new System.Text.StringBuilder();
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            RectTransform root = canvas.transform as RectTransform;
            sb.AppendLine($"# CANVAS {canvas.name} mode={canvas.renderMode} order={canvas.sortingOrder} rect={root.rect.size} scale={canvas.scaleFactor:0.###} ref={(scaler != null ? scaler.referenceResolution.ToString() : "-")} match={(scaler != null ? scaler.matchWidthOrHeight : -1)}");
            DumpNode(root, "", 0, sb);
        }
        File.WriteAllText(Path.Combine(AutoTour.OutDir, "hud_dump.txt"), sb.ToString());
        AutoTour.Log("hud dump written");
    }

    private static void DumpNode(RectTransform rt, string path, int depth, System.Text.StringBuilder sb)
    {
        if (depth > 7 || !rt.gameObject.activeInHierarchy) return;
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
