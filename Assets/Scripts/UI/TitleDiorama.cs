using System.Collections;
using System.Collections.Generic;
using RetroLOTR.Scenarios;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The title screen's tabletop: a slice of the real 2950 map — the Gondor/Mordor front, from
/// Pelargir and Minas Tirith across the Anduin to Minas Morgul, Cirith Ungol and Barad-dûr — laid
/// on a wooden table and filmed by the board camera's view, drifting slowly over it. Everything on
/// it is the game's own look: the scenario's terrain tiles under the seamless blend
/// (HexSeamlessTerrain), the settlements as City3D miniatures, and the characters standing there in
/// 2950 as live Character3D models on their nation-coloured bases, armies with a few troopers
/// behind them. The map's rim dissolves into the table as it does into fog.
///
/// Built far from where the real board will be generated and destroyed before that starts
/// (StartScreenController.Hide). Ready is set once the terrain, towns and table are up, so the
/// title can crossfade from its painted backdrop; the miniatures fade in as their models load.
/// </summary>
public sealed class TitleDiorama : MonoBehaviour
{
    private const string ScenarioName = "2950";
    private const int RowMin = 35, RowMax = 48, ColMin = 36, ColMax = 52;
    private const string Idle = "RB Ready Idle";

    // Far from the real board (which is built from the origin toward +x / -y).
    private static readonly Vector3 Origin = new(-4000f, 4000f, 0f);

    public static TitleDiorama Instance { get; private set; }
    public static Camera Camera => Instance != null ? Instance.cam : null;

    // The camera is the board camera's own (BoardNavigator.ApplyRig at BoardLook.DefaultZoom with
    // BoardLookSettings' tilt and field of view): the 3D units' depth slabs, the towns and the tile
    // blend are all tuned for that view, so the title only pans it.
    [Header("Camera (live)")]
    [Tooltip("How close the shot is (the board camera's orthographic size; the game's default is 6.2).")]
    public float zoom = 3.2f;
    [Header("Miniatures (live)")]
    // As Character3DView places a unit: the character layer sits at (0.058, -0.058) in the hex, its
    // sprite at scale 0.2, a figure ~2.85 sprite units tall standing ~1.7 below the sprite's centre.
    private static readonly Vector3 FeetOffset = new(0.058f, -0.058f - 0.2f * 1.696f, 0f);
    private const float FigureHeight = 0.2f * 2.85f;
    [Tooltip("The lantern on the figures: key light, and sky / ground fill (the board's own light is cool and dim for its far-off view).")]
    public Color lanternKey = new(0.55f, 0.45f, 0.32f);
    public Color lanternSky = new(0.88f, 0.85f, 0.86f);
    public Color lanternGround = new(0.74f, 0.63f, 0.50f);
    [Tooltip("Warm tint on figures and bases; above 1 brightens past the baked texture.")]
    public Color figureTint = new(1.3f, 1.2f, 1.05f);
    [Tooltip("No two figures closer than this (world units; a hex's neighbours are 1.13–1.5 away). 0 = every hex keeps its figure, as on the board.")]
    public float minSpacing = 0f;

    public bool Ready { get; private set; }

    private Camera cam;
    private Board board;
    private Vector3 center;
    private int sortingLayer;
    private readonly List<Figure> figures = new();

    // ── Who stands where ────────────────────────────────────────────────────────────────────

    // Peoples and colours for the leaders of this corner of the map (anyone else: Common, and a
    // colour from the game's nation palette). Races pick models, troopers and town styles.
    private static readonly Dictionary<string, RacesEnum> Races = new()
    {
        { "Denethor", RacesEnum.Dunedain }, { "Boromir", RacesEnum.Dunedain }, { "Faramir", RacesEnum.Dunedain },
        { "Beregond", RacesEnum.Dunedain }, { "Ioreth", RacesEnum.Common }, { "Eomer", RacesEnum.Common },
        { "Gandalf the White", RacesEnum.Maia }, { "Gandalf", RacesEnum.Maia }, { "GhanBuriGhan", RacesEnum.Wildman },
        { "Murazor", RacesEnum.Nazgul }, { "Hoarmurath", RacesEnum.Nazgul }, { "Ren the Unclean", RacesEnum.Nazgul },
        { "Dendra Dwar", RacesEnum.Nazgul }, { "Gorbag", RacesEnum.Orc }, { "Gothmog", RacesEnum.Orc },
        { "Mauhur", RacesEnum.Orc }, { "Shelob", RacesEnum.Spider }, { "Mouth of Sauron", RacesEnum.Common },
        { "Sauron", RacesEnum.Orc }, { "Huz", RacesEnum.Easterling }, { "Khamul", RacesEnum.Nazgul },
    };

    private static readonly HashSet<string> Shadow = new()
    {
        "Murazor", "Hoarmurath", "Ren the Unclean", "Dendra Dwar", "Sauron", "Shelob", "Huz", "Khamul", "Akhorahil",
    };

    private static readonly Dictionary<string, Color> NationColours = new()
    {
        { "Denethor", new Color(0.10f, 0.25f, 0.50f) }, { "Gandalf", new Color(0.55f, 0.40f, 0.05f) },
        { "Faramir", new Color(0.10f, 0.40f, 0.20f) }, { "GhanBuriGhan", new Color(0.35f, 0.40f, 0.08f) },
        { "Theoden", new Color(0.60f, 0.30f, 0.05f) }, { "Murazor", new Color(0.55f, 0.10f, 0.10f) },
        { "Hoarmurath", new Color(0.35f, 0.15f, 0.45f) }, { "Ren the Unclean", new Color(0.35f, 0.08f, 0.12f) },
        { "Dendra Dwar", new Color(0.28f, 0.10f, 0.35f) }, { "Sauron", new Color(0.45f, 0.18f, 0.28f) },
        { "Shelob", new Color(0.22f, 0.22f, 0.45f) }, { "Huz", new Color(0.55f, 0.25f, 0.15f) },
        { "Khamul", new Color(0.15f, 0.40f, 0.42f) },
    };

    // Names on the map with no figure of their own.
    private static readonly HashSet<string> Skip = new() { "The Dark Eye", "Urzahil", "Ethacali" };

    private sealed class Figure
    {
        public string Name;
        public RacesEnum Race;
        public Color Nation;
        public Vector3 Position;    // world, feet
        public float Yaw;
        public string Folder;
        public Character3DModel Model;
        public float BornAt;
        public bool Done;           // model (and regiment) in place, or no model at all
        public bool Failed;
        public bool Walker;         // walks to RouteEnd and back
        public Vector3 RouteEnd;
        public bool Moving;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────────────────

    /// <summary>Starts building the diorama; null when the scene lacks what it needs.</summary>
    public static TitleDiorama Create()
    {
        if (Instance != null) return Instance;
        Board board = Board.Instance;
        if (board == null || board.hexPrefab == null || Character3DSettings.Instance == null) return null;
        var go = new GameObject("TitleDiorama");
        go.transform.position = Origin;
        var diorama = go.AddComponent<TitleDiorama>();
        diorama.board = board;
        return diorama;
    }

    private void Awake()
    {
        Instance = this;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    // Character3DModel publishes the board's light every frame; the title camera re-lights the
    // figures with the lantern just before it draws.
    private void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (!Ready || camera != cam) return;
        Character3DSettings settings = Character3DSettings.Instance;
        // A lantern's restless flame.
        float flame = 0.88f + 0.12f * Mathf.PerlinNoise(Time.unscaledTime * 2.3f, 0.7f);
        if (settings != null) Character3DLighting.Publish(settings, lanternKey * flame, lanternSky, lanternGround);
    }

    private void OnDestroy()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (Instance == this) Instance = null;
        BoardNavigator.TitleTilt = 0f;
        TabletopLens.CloseUp = 0f;
        if (cam != null) Destroy(cam.gameObject);
    }

    private IEnumerator Start()
    {
        BoardNavigator.TitleTilt = Mathf.Max(BoardLookSettings.Instance.cameraTilt, 0.01f);
        ScenarioData data = ScenarioLoader.Load(ScenarioName);
        Hex hexTemplate = board.hexPrefab.GetComponent<Hex>();
        HexTextureMapping mapping = board.hexPrefab.GetComponent<HexTextureMapping>();
        if (mapping == null) mapping = FindAnyObjectByType<HexTextureMapping>();
        if (data == null || hexTemplate == null || hexTemplate.terrainTexture == null || mapping == null)
        {
            Debug.LogWarning("TitleDiorama: scenario, hex prefab or terrain art missing; keeping the painted title.");
            Destroy(gameObject);
            yield break;
        }
        sortingLayer = hexTemplate.terrainTexture.sortingLayerID;

        center = HexPosition((RowMin + RowMax) / 2, (ColMin + ColMax) / 2);
        BuildCamera();
        PlaceCamera();
        BuildTable();
        yield return null;

        Dictionary<Vector2Int, Sprite> art = BuildTerrain(data, hexTemplate.terrainTexture, mapping);
        yield return null;
        BuildTowns(data, art);
        PlanFigures(data);
        AddWalker();
        yield return null;
        BuildAtmosphere(data, art);

        // One frame on screen before the title fades over to it.
        yield return null;
        Ready = true;
    }

    private void Update()
    {
        if (!Ready) return;
        foreach (Figure figure in figures)
            if (!figure.Done) TrySpawn(figure);
    }

    // ── The errand rider ────────────────────────────────────────────────────────────────────

    // The errand rider walks his road, pausing at each end to look about.
    private void Walk(Figure figure)
    {
        const float speed = 0.22f, pause = 3f;
        float distance = Vector3.Distance(figure.Position, figure.RouteEnd);
        float walk = distance / speed, cycle = 2f * (walk + pause);
        float t = Mathf.Repeat(Time.unscaledTime - figure.BornAt, cycle);
        bool outbound = t < walk + pause;
        float leg = outbound ? t : t - walk - pause;
        bool moving = leg < walk;
        float along = moving ? leg / walk : 1f;
        if (!outbound) along = 1f - along;
        Vector3 at = Vector3.Lerp(figure.Position, figure.RouteEnd, along);
        float east = Mathf.Sign(figure.RouteEnd.x - figure.Position.x);
        figure.Yaw = moving ? (outbound ? 100f : -100f) * east : (outbound ? 160f : -160f) * east;
        if (moving != figure.Moving)
        {
            figure.Moving = moving;
            figure.Model.Animation.SetBaseLoop(moving ? "RB Walk" : Idle, 0.25f);
        }
        figure.Model.transform.position = at + FeetOffset;
    }

    private void LateUpdate()
    {
        PlaceCamera();
        Character3DSettings settings = Character3DSettings.Instance;
        foreach (Figure figure in figures)
        {
            Character3DModel model = figure.Model;
            if (model == null) continue;
            Transform root = model.transform;
            root.position = figure.Position + FeetOffset;
            if (figure.Walker && figure.Done) Walk(figure);
            root.rotation = Character3DLighting.BoardFrame(settings) * Quaternion.Euler(0f, figure.Yaw, 0f);
            root.localScale = Vector3.one * (FigureHeight * settings.figureScale / model.ModelHeight);
            float fade = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - figure.BornAt) / 0.8f);
            model.Tint = new Color(figureTint.r, figureTint.g, figureTint.b, fade);
            model.SetBase(settings.showBase ? figure.Nation : null, settings.baseRadius);
        }
    }

    // ── Board geometry ──────────────────────────────────────────────────────────────────────

    // As BoardGenerator.GetPosition, offset to the diorama's origin. Odd rows keep their shift,
    // so the slice blends exactly as it does on the board.
    private Vector3 HexPosition(int row, int col)
    {
        float x = col * board.hexSize.x + ((row & 1) == 1 ? board.hexSize.x * 0.5f : 0f);
        return Origin + new Vector3(x - ColMin * board.hexSize.x, -(row - RowMin) * board.hexSize.y, 0f);
    }

    private static bool InSlice(int row, int col) => row >= RowMin && row <= RowMax && col >= ColMin && col <= ColMax;

    // The outermost ring dissolves into the table; nothing stands on it.
    private static bool OnRim(int row, int col) => row == RowMin || row == RowMax || col == ColMin || col == ColMax;

    private static TerrainEnum TerrainAt(ScenarioData data, int row, int col) => (TerrainEnum)data.terrain[row * data.width + col];

    // ── Atmosphere ──────────────────────────────────────────────────────────────────────────

    private void BuildAtmosphere(ScenarioData data, Dictionary<Vector2Int, Sprite> art)
    {
        var half = new Vector2((ColMax - ColMin) * 0.5f * board.hexSize.x + 1f, (RowMax - RowMin) * 0.5f * board.hexSize.y + 1f);
        TitleAtmosphere atmosphere = TitleAtmosphere.Create(transform, sortingLayer, board.hexSize, center, half);
        foreach (Vector2Int key in art.Keys)
        {
            if (OnRim(key.x, key.y)) continue;
            TerrainEnum terrain = TerrainAt(data, key.x, key.y);
            // The lava fields of Gorgoroth, east of the Ephel Dúath.
            if (terrain == TerrainEnum.wastelands && key.y >= 47) atmosphere.AddLava(HexPosition(key.x, key.y));
        }
        foreach (ScenarioPC pc in data.pcs)
        {
            if (!InSlice(pc.row, pc.col) || pc.isHidden || OnRim(pc.row, pc.col)) continue;
            int plumes = pc.citySize >= (int)PCSizeEnum.city ? 4 : pc.citySize >= (int)PCSizeEnum.town ? 2 : 1;
            atmosphere.AddSmoke(HexPosition(pc.row, pc.col), plumes, Shadow.Contains(pc.ownerLeaderName));
        }
    }

    // ── Camera ──────────────────────────────────────────────────────────────────────────────

    private void BuildCamera()
    {
        var go = new GameObject("TitleDioramaCamera");
        cam = go.AddComponent<Camera>();
        Camera main = Camera.main;
        cam.depth = (main != null ? main.depth : 0f) + 1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.015f, 0.01f, 1f);
        cam.allowHDR = main == null || main.allowHDR;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        UniversalAdditionalCameraData mainData = main != null ? main.GetUniversalAdditionalCameraData() : null;
        if (mainData != null)
        {
            data.antialiasing = mainData.antialiasing;
            data.volumeLayerMask = mainData.volumeLayerMask;
        }
    }

    // The board rig's own view (BoardNavigator.ApplyRig: the campaign's tilt and field of view),
    // held on one shot. The board navigator is off on the title, so the diorama has a camera of
    // its own.
    private void PlaceCamera()
    {
        if (board == null || cam == null) return;
        BoardNavigator.TitleTilt = Mathf.Max(BoardLookSettings.Instance.cameraTilt, 0.01f);
        // One fixed shot of the front, Minas Tirith to Minas Morgul across Osgiliath: a moving
        // camera made the miniatures slide against the painted board.
        float shotZoom = zoom;
        Vector3 target = (HexPosition(43, 41) + HexPosition(43, 45)) * 0.5f;
        // The hand of cards covers the lower part of the screen: look below the place, so it sits
        // in the upper middle.
        Vector3 pivot = target - new Vector3(0f, shotZoom * 0.3f, 0f);
        TabletopLens.CloseUp = 0.6f;

        BoardLookSettings look = BoardLookSettings.Instance;
        float tilt = BoardNavigator.TitleTilt * Mathf.Deg2Rad;
        float fov = Mathf.Clamp(look.cameraFov, 5f, 90f);
        float distance = shotZoom / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        var forward = new Vector3(0f, Mathf.Sin(tilt), Mathf.Cos(tilt));
        var up = new Vector3(0f, Mathf.Cos(tilt), -Mathf.Sin(tilt));
        cam.orthographic = false;
        cam.fieldOfView = fov;
        cam.nearClipPlane = Mathf.Max(0.05f, distance * 0.4f);
        cam.farClipPlane = distance * 4f + 10f;
        cam.transform.SetPositionAndRotation(new Vector3(pivot.x, pivot.y, Origin.z) - forward * distance, Quaternion.LookRotation(forward, up));
    }

    // ── Table ───────────────────────────────────────────────────────────────────────────────

    private void BuildTable()
    {
        Shader shader = BoardLookSettings.Instance.titleTableShader;
        if (shader == null) return;
        var go = new GameObject("Table", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        go.transform.position = center + new Vector3(0f, 0f, 0.05f);
        go.transform.localScale = new Vector3(90f, 70f, 1f);
        var mesh = new Mesh { name = "TitleTable" };
        mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) });
        mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
        mesh.RecalculateBounds();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        var material = new Material(shader) { name = "TitleTable (runtime)" };
        // The lantern stands off the board's top-left corner, as in a candle-lit war room.
        material.SetVector("_LampPos", center + new Vector3(-9f, 6f, 0f));
        material.SetFloat("_LampRadius", 22f);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingLayerID = sortingLayer;
        renderer.sortingOrder = short.MinValue + 1;
    }

    // ── Terrain ─────────────────────────────────────────────────────────────────────────────

    private Dictionary<Vector2Int, Sprite> BuildTerrain(ScenarioData data, SpriteRenderer template, HexTextureMapping mapping)
    {
        var named = new Dictionary<Vector2Int, string>();
        foreach (ScenarioSpriteCell cell in data.terrainSprites)
            if (InSlice(cell.row, cell.col)) named[new Vector2Int(cell.row, cell.col)] = cell.spriteName;

        // Variation picks are random on the board; seeded per cell here so the title never changes.
        var art = new Dictionary<Vector2Int, Sprite>();
        Random.State saved = Random.state;
        for (int row = RowMin; row <= RowMax; row++)
            for (int col = ColMin; col <= ColMax; col++)
            {
                var key = new Vector2Int(row, col);
                Sprite sprite = named.TryGetValue(key, out string spriteName) ? mapping.GetTerrainSpriteByName(spriteName) : null;
                if (sprite == null)
                {
                    Random.InitState(row * 1009 + col);
                    sprite = mapping.GetTerrainBaseSprite(TerrainAt(data, row, col));
                }
                if (sprite != null) art[key] = sprite;
            }
        Random.state = saved;

        // The tile sits where the hex prefab puts it, relative to the hex's centre.
        Vector3 tileOffset = template.transform.position - board.hexPrefab.transform.position;
        var renderers = new Dictionary<Vector2Int, SpriteRenderer>();
        foreach (KeyValuePair<Vector2Int, Sprite> pair in art)
        {
            var go = new GameObject($"Terrain {pair.Key.x},{pair.Key.y}", typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            go.transform.position = HexPosition(pair.Key.x, pair.Key.y) + tileOffset;
            go.transform.localScale = template.transform.lossyScale;
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = pair.Value;
            renderer.sortingLayerID = sortingLayer;
            // As Hex.Initialize: lower rows draw over the ones behind them.
            renderer.sortingOrder = -1 - (RowMax - pair.Key.x) * 2 - (pair.Key.y & 1);
            renderers[pair.Key] = renderer;
        }

        var neighbors = new Sprite[6];
        foreach (KeyValuePair<Vector2Int, SpriteRenderer> pair in renderers)
        {
            Vector2Int[] offsets = (pair.Key.x & 1) == 0 ? board.evenRowNeighbors : board.oddRowNeighbors;
            for (int d = 0; d < 6; d++)
                neighbors[d] = art.TryGetValue(pair.Key + offsets[d], out Sprite sprite) ? sprite : null;
            SpriteRenderer renderer = pair.Value;
            Vector2 drawn = Vector2.Scale(renderer.sprite.bounds.size, renderer.transform.lossyScale);
            HexSeamlessTerrain.ApplyStandalone(renderer, drawn, board.hexSize, neighbors);
        }
        return art;
    }

    // ── Settlements ─────────────────────────────────────────────────────────────────────────

    private void BuildTowns(ScenarioData data, Dictionary<Vector2Int, Sprite> art)
    {
        float hexWidth = board.hexSize.x * BoardLookSettings.Instance.cityScale;
        foreach (ScenarioPC pc in data.pcs)
        {
            if (!InSlice(pc.row, pc.col) || pc.isHidden || !art.ContainsKey(new Vector2Int(pc.row, pc.col))) continue;
            var anchor = new GameObject(pc.pcName).transform;
            anchor.SetParent(transform, false);
            anchor.position = HexPosition(pc.row, pc.col);
            City3D city = City3D.BuildStandalone(pc.pcName, RaceOf(pc.ownerLeaderName), (PCSizeEnum)pc.citySize, (FortSizeEnum)pc.fortSize,
                pc.isCapital, ColourOf(pc.ownerLeaderName), hexWidth, anchor, art[new Vector2Int(pc.row, pc.col)]);
            city.SetSorting(sortingLayer, 2);
        }
    }

    // ── Miniatures ──────────────────────────────────────────────────────────────────────────

    private void PlanFigures(ScenarioData data)
    {
        var byHex = new Dictionary<Vector2Int, List<ScenarioCharacter>>();
        var seen = new HashSet<string>();
        foreach (ScenarioCharacter c in data.characters)
        {
            if (!InSlice(c.row, c.col) || string.IsNullOrWhiteSpace(c.characterName) || Skip.Contains(c.characterName)) continue;
            // One figure per name: Faramir stands at Henneth Annûn, not twice.
            if (!seen.Add(c.characterName)) continue;
            var key = new Vector2Int(c.row, c.col);
            if (!byHex.TryGetValue(key, out List<ScenarioCharacter> list)) byHex[key] = list = new List<ScenarioCharacter>();
            list.Add(c);
        }

        // One stand per hex, as on the board: the leader himself if he is there, else whoever
        // leads the biggest army.
        var stands = new List<(int rank, int army, ScenarioCharacter c, Vector2Int hex)>();
        foreach (KeyValuePair<Vector2Int, List<ScenarioCharacter>> pair in byHex)
        {
            ScenarioCharacter c = null;
            int army = 0, best = -1;
            foreach (ScenarioCharacter candidate in pair.Value)
            {
                int size = ArmySize(candidate);
                army += size;
                int rank = Rank(candidate, size);
                if (rank > best) { best = rank; c = candidate; }
            }
            stands.Add((best, army, c, pair.Key));
        }

        // The table is a diorama, not the whole order of battle: figures this tall standing on
        // neighbouring hexes cover each other, so the lesser of two neighbours gives way.
        stands.Sort((a, b) => b.rank.CompareTo(a.rank));
        var placed = new List<Vector3>();
        foreach ((int _, int army, ScenarioCharacter c, Vector2Int hex) in stands)
        {
            Vector3 at = HexPosition(hex.x, hex.y);
            if (placed.Exists(p => Vector2.Distance(p, at) < minSpacing)) continue;
            placed.Add(at);
            bool shadow = Shadow.Contains(c.ownerLeaderName);
            figures.Add(new Figure
            {
                Name = c.characterName,
                Race = RaceOf(c.characterName),
                Nation = ColourOf(c.ownerLeaderName),
                Position = at,
                // The West faces east across the Anduin, the Shadow west toward it; both a
                // little toward the viewer, as figures posed for the table.
                Yaw = shadow ? -118f : 118f,
            });
        }
    }

    // A rider on the road between Minas Tirith and Osgiliath.
    private void AddWalker()
    {
        figures.Add(new Figure
        {
            Name = "Errand Rider",
            Race = RacesEnum.Dunedain,
            Nation = ColourOf("Denethor"),
            Position = HexPosition(44, 41),
            RouteEnd = HexPosition(44, 43),
            Walker = true,
        });
    }

    // Who keeps the table when neighbours crowd: the leaders themselves, then the names everyone
    // knows, then the biggest armies.
    private static readonly HashSet<string> Famous = new() { "Gandalf the White", "Boromir", "Faramir", "Shelob", "Gothmog" };

    private static int Rank(ScenarioCharacter c, int army) =>
        (c.characterName == c.ownerLeaderName ? 3000000 : Famous.Contains(c.characterName) ? 1000000 : 0) + Mathf.Min(army, 999999);

    private static int ArmySize(ScenarioCharacter c)
    {
        int size = 0;
        if (c.army != null && c.army.stacks != null)
            foreach (ScenarioArmyStack stack in c.army.stacks) size += Mathf.Max(stack.amount, 0);
        return size;
    }

    private void TrySpawn(Figure figure)
    {
        if (figure.Folder == null)
        {
            if (!Character3DModel.TryResolve(figure.Race, out string folder, figure.Name, figure.Name.Split(' ')[0])) return; // catalog not up yet
            figure.Folder = folder;
        }
        if (figure.Model == null)
        {
            GameObject prefab = Character3DModel.RequestPrefab(figure.Folder);
            if (prefab == null)
            {
                if (Character3DModel.IsMissing(figure.Folder)) figure.Done = figure.Failed = true;
                return;
            }
            figure.Model = Character3DModel.Create(prefab, figure.Folder, transform);
            figure.Model.SortingLayerId = sortingLayer;
            figure.Model.SortingOrder = 3;
            figure.Model.Animation.SetBaseLoop(Idle, 0f);
            figure.Model.Arm(LoadoutFor(figure.Race, figure.Name));
            figure.BornAt = Time.unscaledTime;
        }
        figure.Done = true;
    }

    // What a figure holds — Character3DWeapons.For's rules, from its people alone.
    private static Character3DWeapons.Loadout LoadoutFor(RacesEnum race, string name) => race switch
    {
        RacesEnum.Spider => Character3DWeapons.Unarmed,
        RacesEnum.Maia => new Character3DWeapons.Loadout(Character3DWeapons.Weapon.Staff),
        RacesEnum.Nazgul => new Character3DWeapons.Loadout(Character3DWeapons.Weapon.Sword),
        RacesEnum.Orc => new Character3DWeapons.Loadout(name.Length % 2 == 0 ? Character3DWeapons.Weapon.Scimitar : Character3DWeapons.Weapon.MorningStar),
        RacesEnum.Wildman or RacesEnum.Easterling => new Character3DWeapons.Loadout(Character3DWeapons.Weapon.Spear),
        _ => new Character3DWeapons.Loadout(Character3DWeapons.Weapon.Sword, shield: true),
    };

    private static RacesEnum RaceOf(string name) =>
        name != null && Races.TryGetValue(name, out RacesEnum race) ? race : RacesEnum.Common;

    private Color ColourOf(string leader)
    {
        if (string.IsNullOrEmpty(leader)) return Color.clear;
        if (NationColours.TryGetValue(leader, out Color colour)) return colour;
        Colors colors = FindAnyObjectByType<Colors>();
        Color[] palette = colors != null ? colors.nationColors : null;
        if (palette == null || palette.Length == 0) return Color.grey;
        return palette[Mathf.Abs(leader.GetHashCode()) % palette.Length];
    }
}
