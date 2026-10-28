using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Weapon = Character3DWeapons.Weapon;

/// <summary>
/// A test bench for the 3D miniatures' weapons and fighting moves, from the command line:
///   Runeboard.exe -figurebench &lt;outDir&gt;
/// Once the title is up, hides the UI, stands a grid of models (one row per rig, one column per
/// loadout) in front of a camera of its own, and writes screenshots: idle, then two moments of the
/// attack swing. Quits when done.
/// </summary>
public static class FigureBench
{
    public static bool Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-figurebench");
        if (i < 0 || i + 1 >= args.Length) return;
        Active = true;
        var runner = new GameObject("FigureBench").AddComponent<FigureBenchRunner>();
        runner.OutDir = args[i + 1];
        UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);
    }
}

public class FigureBenchRunner : MonoBehaviour
{
    public string OutDir;

    private static readonly string[] Rigs = { "Common", "Orc", "Dwarf", "Hobbit", "Elf", "Nazgul", "Aragorn", "Easterling", "Troll", "Gandalf" };
    private static readonly Character3DWeapons.Loadout[] Loadouts =
    {
        new(Weapon.Sword), new(Weapon.Sword, shield: true), new(Weapon.Axe), new(Weapon.Scimitar),
        new(Weapon.MorningStar), new(Weapon.Spear), new(Weapon.Staff),
    };

    private const float Spacing = 1.3f;
    private static readonly Vector3 Origin = new(8000f, 8000f, 0f);

    private IEnumerator Start()
    {
        Directory.CreateDirectory(OutDir);
        float deadline = Time.realtimeSinceStartup + 300f;
        while (FindAnyObjectByType<StartScreenController>() is not StartScreenController s || !s.IsShowing)
        {
            if (Time.realtimeSinceStartup > deadline) { Debug.LogError("[FigureBench] no title screen"); Application.Quit(); yield break; }
            yield return null;
        }
        yield return new WaitForSecondsRealtime(2f);
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
        if (TitleDiorama.Instance != null) Destroy(TitleDiorama.Instance.gameObject);

        var camGo = new GameObject("FigureBenchCamera");
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.depth = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.32f, 0.33f, 0.35f);
        float width = Loadouts.Length * Spacing, height = Rigs.Length * Spacing;
        cam.orthographicSize = height * 0.5f + 0.4f;
        cam.transform.position = Origin + new Vector3(width * 0.5f - Spacing * 0.5f, -height * 0.5f + Spacing * 0.5f + 0.35f, -20f);
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 60f;

        // Models load through Addressables: ask for all, then wait for them.
        var models = new List<(Character3DModel model, int row)>();
        for (int r = 0; r < Rigs.Length; r++) Character3DModel.RequestPrefab(Rigs[r]);
        deadline = Time.realtimeSinceStartup + 120f;
        for (int r = 0; r < Rigs.Length; r++)
        {
            GameObject prefab;
            while ((prefab = Character3DModel.RequestPrefab(Rigs[r])) == null && !Character3DModel.IsMissing(Rigs[r]) && Time.realtimeSinceStartup < deadline) yield return null;
            if (prefab == null) { Debug.LogWarning($"[FigureBench] no model {Rigs[r]}"); continue; }
            for (int c = 0; c < Loadouts.Length; c++)
            {
                Character3DModel model = Character3DModel.Create(prefab, Rigs[r], transform);
                Transform t = model.transform;
                t.position = Origin + new Vector3(c * Spacing, -r * Spacing - 0.5f, 0f);
                // Three-quarters toward the camera, weapon hand forward.
                t.rotation = Quaternion.Euler(0f, 200f, 0f);
                t.localScale = Vector3.one * (1f / model.ModelHeight);
                model.Animation.IdleVariety = false;
                model.Animation.SetBaseLoop("RB Ready Idle", 0f);
                model.Arm(Loadouts[c]);
                models.Add((model, r));
            }
        }
        yield return new WaitForSecondsRealtime(4f);
        yield return Rows(cam, "a_idle");
        foreach ((Character3DModel model, int _) in models) model.Animation.PlayOnce("Standing Melee Attack Downward", 0.1f, 0.25f);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Rows(cam, "b_windup");
        yield return new WaitForSecondsRealtime(0.8f);
        yield return Rows(cam, "c_strike");
        foreach ((Character3DModel model, int _) in models)
        {
            model.Animation.PlayOnce("Standing Melee Attack Downward", 0.1f, 1f);
            model.transform.rotation = Quaternion.Euler(0f, 110f, 0f);
        }
        yield return new WaitForSecondsRealtime(2.5f);
        yield return Rows(cam, "d_side");
        foreach ((Character3DModel model, int _) in models)
        {
            model.Animation.SetBaseLoop("Standing Walk Forward", 0.1f);
            model.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
        }
        yield return new WaitForSecondsRealtime(1.5f);
        yield return Rows(cam, "e_walk");
        yield return ClipShowdown(cam);
        Debug.Log("[FigureBench] done");
        Application.Quit();
    }

    // Candidate attack clips side by side on armed figures, filmed through the swing.
    private static readonly string[] Clips =
    {
        "RB Walk", "RB Ready Idle", "RB Slash", "RB Slash", "RB Slash", "RB Slash",
    };
    private static readonly (string rig, Character3DWeapons.Loadout loadout)[] Fighters =
    {
        ("Common", new(Weapon.Sword, shield: true)), ("Orc", new(Weapon.Scimitar)), ("Dwarf", new(Weapon.Axe, shield: true)), ("Elf", new(Weapon.Spear)),
    };

    private IEnumerator ClipShowdown(Camera cam)
    {
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        Vector3 origin = Origin + new Vector3(0f, -40f, 0f);
        var models = new List<(Character3DModel model, int clip)>();
        for (int r = 0; r < Fighters.Length; r++)
        {
            GameObject prefab = Character3DModel.RequestPrefab(Fighters[r].rig);
            if (prefab == null) continue;
            for (int c = 0; c < Clips.Length; c++)
            {
                Character3DModel model = Character3DModel.Create(prefab, Fighters[r].rig, transform);
                model.transform.position = origin + new Vector3(c * Spacing, -r * Spacing - 0.5f, 0f);
                model.transform.rotation = Quaternion.Euler(0f, 210f, 0f);
                model.transform.localScale = Vector3.one * (1f / model.ModelHeight);
                model.Animation.IdleVariety = false;
                model.Animation.SetBaseLoop("standing idle", 0f);
                model.Arm(Fighters[r].loadout);
                models.Add((model, c));
            }
        }
        yield return new WaitForSecondsRealtime(2f);
        foreach ((Character3DModel model, int clip) in models)
        {
            if (Clips[clip].Contains("Walk") || Clips[clip].Contains("idle") || Clips[clip].Contains("Idle")) model.Animation.SetBaseLoop(Clips[clip], 0f);
            else if (Clips[clip] == "RB Slash") model.Swing(0.35f);
            else model.Animation.PlayOnce(Clips[clip], 0.1f, 0.35f);
        }
        float width = Clips.Length * Spacing;
        cam.orthographicSize = Fighters.Length * Spacing * 0.5f;
        cam.transform.position = origin + new Vector3(width * 0.5f - Spacing * 0.5f, -Fighters.Length * Spacing * 0.5f + Spacing * 0.5f, -20f);
        for (int k = 0; k < 8; k++)
        {
            yield return Shot($"f_clips_{k}");
            yield return new WaitForSecondsRealtime(0.45f);
        }
        // From the side, for the walks and the line of the blow.
        foreach ((Character3DModel model, int clip) in models)
        {
            model.transform.rotation = Quaternion.Euler(0f, 270f, 0f);
            if (Clips[clip] == "RB Slash") model.Swing(0.35f);
            else if (!Clips[clip].Contains("Walk") && !Clips[clip].Contains("dle")) model.Animation.PlayOnce(Clips[clip], 0.1f, 0.35f);
        }
        for (int k = 0; k < 6; k++)
        {
            yield return Shot($"g_side_{k}");
            yield return new WaitForSecondsRealtime(0.45f);
        }
    }

    // One shot per rig row, framed close: the whole row of loadouts at a readable size.
    private IEnumerator Rows(Camera cam, string phase)
    {
        float width = Loadouts.Length * Spacing;
        for (int r = 0; r < Rigs.Length; r++)
        {
            cam.orthographicSize = Spacing * 0.5f;
            cam.transform.position = Origin + new Vector3(width * 0.5f - Spacing * 0.5f, -r * Spacing, -20f);
            yield return Shot($"{phase}_{r:00}_{Rigs[r]}");
        }
    }

    private IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        string path = Path.Combine(OutDir, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        yield return null;
        yield return null;
        Debug.Log($"[FigureBench] shot {path}");
    }
}

/// <summary>
/// Every settlement recipe (Resources/Cities) built in a grid and photographed with the board's
/// own top-down view, 20 to a page, for reviewing the 3D towns:
///   Runeboard.exe -townbench &lt;outDir&gt;
/// </summary>
public static class TownBench
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-townbench");
        if (i < 0 || i + 1 >= args.Length) return;
        var runner = new GameObject("TownBench").AddComponent<TownBenchRunner>();
        runner.OutDir = args[i + 1];
        UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);
    }
}

public class TownBenchRunner : MonoBehaviour
{
    public string OutDir;
    private const int Columns = 5, RowsPerPage = 4;
    private static readonly Vector3 Origin = new(-9000f, 9000f, 0f);

    private IEnumerator Start()
    {
        Directory.CreateDirectory(OutDir);
        float deadline = Time.realtimeSinceStartup + 300f;
        while (FindAnyObjectByType<StartScreenController>() is not StartScreenController s || !s.IsShowing)
        {
            if (Time.realtimeSinceStartup > deadline) { Application.Quit(); yield break; }
            yield return null;
        }
        yield return new WaitForSecondsRealtime(2f);
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
        if (TitleDiorama.Instance != null) Destroy(TitleDiorama.Instance.gameObject);
        yield return null;
        BoardNavigator.TitleTilt = 0f;

        var keys = new List<string>();
        foreach (TextAsset asset in Resources.LoadAll<TextAsset>("Cities"))
            if (!asset.name.Equals("README", StringComparison.OrdinalIgnoreCase)) keys.Add(asset.name);
        keys.Sort(StringComparer.Ordinal);

        Board board = Board.Instance;
        float hexWidth = board != null ? board.hexSize.x : 1.09f;
        float cell = hexWidth * 1.35f;
        HexTextureMapping mapping = board != null && board.hexPrefab != null ? board.hexPrefab.GetComponent<HexTextureMapping>() : null;
        Sprite grass = mapping != null ? mapping.GetTerrainBaseSprite(TerrainEnum.grasslands) : null;
        for (int i = 0; i < keys.Count; i++)
        {
            int page = i / (Columns * RowsPerPage), local = i % (Columns * RowsPerPage);
            var anchor = new GameObject(keys[i]).transform;
            anchor.SetParent(transform, false);
            anchor.position = Origin + new Vector3(page * 40f + (local % Columns) * cell, -(local / Columns) * cell, 0f);
            City3D city = City3D.BuildStandalone(keys[i], RacesEnum.Common, PCSizeEnum.town, FortSizeEnum.NONE, false,
                new Color(0.55f, 0.1f, 0.1f), hexWidth, anchor, grass);
            city.SetSorting(0, 2);
        }

        var camGo = new GameObject("TownBenchCamera");
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.depth = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.42f, 0.44f, 0.34f);
        cam.orthographicSize = RowsPerPage * cell * 0.5f;
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 60f;
        int pages = (keys.Count + Columns * RowsPerPage - 1) / (Columns * RowsPerPage);
        for (int p = 0; p < pages; p++)
        {
            cam.transform.position = Origin + new Vector3(p * 40f + (Columns - 1) * cell * 0.5f, -(RowsPerPage - 1) * cell * 0.5f + cell * 0.12f, -20f);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(OutDir, $"towns_{p:00}.png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            int first = p * Columns * RowsPerPage;
            Debug.Log($"[TownBench] {path}: {string.Join(", ", keys.GetRange(first, Mathf.Min(Columns * RowsPerPage, keys.Count - first)))}");
        }
        Debug.Log("[TownBench] done");
        Application.Quit();
    }
}
