using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Settlements as procedural 3D miniatures instead of their painted PC tiles
/// (BoardLookSettings.cities3D; turning it off brings the tiles back). A poller follows every
/// hex's PC art: wherever the tile would show, a City3D built from the settlement's data
/// (CityLayout: size, fortification, capital, owner's people, landmark) stands on the hex instead,
/// and the tile is skipped by rendering. The city is rebuilt when any of that changes (a captured
/// town takes its new owner's style and colours), and follows the tile's fade under the fog.
/// </summary>
public class City3D : MonoBehaviour
{
    private const float RefreshInterval = 0.5f;

    private static City3D manager;
    private static readonly Dictionary<Hex, City3D> byHex = new();
    private static readonly List<Hex> scratch = new();
    private float nextRefresh;

    // Per city.
    private Hex hex;
    private string key;
    private readonly List<Mesh> meshes = new();
    public Color Tint { get; private set; } = Color.white;
    public int SortingLayerId { get; private set; }
    public int SortingOrder { get; private set; }

    public static void Ensure()
    {
        if (manager != null) return;
        manager = new GameObject("City3D").AddComponent<City3D>();
    }

    // ── Manager ─────────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (this != manager || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    private static void Refresh()
    {
        bool enabled = BoardLookSettings.Instance.cities3D && Character3DSettings.Instance != null && Character3DSettings.Instance.shader != null;
        scratch.Clear();
        foreach (KeyValuePair<Hex, City3D> pair in byHex)
            if (!enabled || pair.Key == null || pair.Value == null || KeyFor(pair.Key) == null) scratch.Add(pair.Key);
        foreach (Hex gone in scratch) Remove(gone);

        Board board = Board.Instance;
        if (!enabled || board == null || board.hexes == null) return;
        foreach (Hex hex in board.hexes.Values)
        {
            if (hex == null) continue;
            string wanted = KeyFor(hex);
            if (wanted == null) continue;
            if (byHex.TryGetValue(hex, out City3D city) && city != null && city.key == wanted) continue;
            Remove(hex);
            byHex[hex] = Build(hex, wanted);
        }
    }

    // What the city looks like, or null where no PC tile would show.
    private static string KeyFor(Hex hex)
    {
        SpriteRenderer tile = hex.pcTexture;
        if (tile == null || tile.sprite == null || !tile.gameObject.activeInHierarchy) return null;
        PC pc = hex.GetPCData();
        if (pc == null) return null;
        Leader owner = pc.owner;
        string nation = owner != null ? ColorUtility.ToHtmlStringRGB(owner.nationColor) : "-";
        return $"{pc.pcName}|{pc.citySize}|{pc.fortSize}|{pc.isCapital}|{(owner != null ? owner.race : RacesEnum.Common)}|{nation}|{Mathf.RoundToInt(BoardNavigator.CurrentTilt)}|{BoardLookSettings.Instance.cityPaletteMatch:F2}|{BoardLookSettings.Instance.cityBrightness:F2}|{BoardLookSettings.Instance.citySaturation:F2}|{BoardLookSettings.Instance.cityScale:F2}";
    }

    private static void Remove(Hex hex)
    {
        if (hex == null) { byHex.Remove(hex); return; }
        if (byHex.TryGetValue(hex, out City3D city) && city != null) Destroy(city.gameObject);
        byHex.Remove(hex);
        if (hex.pcTexture != null) hex.pcTexture.forceRenderingOff = false;
    }

    private static City3D Build(Hex hex, string key)
    {
        PC pc = hex.GetPCData();
        float hexWidth = Board.Instance.hexSize.x * BoardLookSettings.Instance.cityScale;
        CityLayout.Result layout = CityLayout.Make(pc, hexWidth, hex.pcTexture.sprite != null ? hex.pcTexture.sprite.name : null);
        City3D city = Assemble(layout, pc.pcName, pc.owner != null ? pc.owner.nationColor : Color.clear, hexWidth, hex.transform,
            hex.terrainTexture != null ? hex.terrainTexture.sprite : null);
        city.hex = hex;
        city.key = key;

        hex.pcTexture.forceRenderingOff = true;
        city.LateUpdate();
        return city;
    }

    /// <summary>A settlement standing on its own, off the board (TitleDiorama): the same
    /// buildings as on a hex, drawn at full opacity in the default sort order.</summary>
    public static City3D BuildStandalone(string pcName, RacesEnum race, PCSizeEnum citySize, FortSizeEnum fortSize, bool isCapital, Color nation, float hexWidth, Transform parent, Sprite ground = null)
    {
        CityLayout.Result layout = CityLayout.Make(pcName, race, citySize, fortSize, isCapital, hexWidth);
        return Assemble(layout, pcName, nation, hexWidth, parent, ground);
    }

    /// <summary>Draw order for a standalone settlement (a board one follows its PC tile).</summary>
    public void SetSorting(int layerId, int order)
    {
        SortingLayerId = layerId;
        SortingOrder = order;
    }

    private static City3D Assemble(CityLayout.Result layout, string pcName, Color nation, float hexWidth, Transform parent, Sprite ground = null)
    {
        Character3DSettings unitSettings = Character3DSettings.Instance;

        var root = new GameObject($"City3D {pcName}");
        root.transform.SetParent(parent, false);
        root.transform.position = parent.position;
        root.transform.rotation = Character3DLighting.BoardFrame(unitSettings);
        var city = root.AddComponent<City3D>();

        Material[] materials = CityStyles.MaterialsFor(layout.Style, layout.Palette, nation, unitSettings.shader, layout.Surfaces);
        // A stone course or roof-tile row is the same size on every building: the textures repeat
        // every tenth of a hex width.
        float uvScale = 1f / (0.1f * hexWidth);
        var shades = new System.Random(pcName.GetHashCode());
        // Screen offset → the board frame's ground plane: depth is foreshortened by the pitch, and a
        // tilted camera (BoardNavigator) sees board distances shortened by cos(tilt) as well.
        float depthScale = Mathf.Cos(BoardNavigator.CurrentTilt * Mathf.Deg2Rad) / Mathf.Sin(Mathf.Max(5f, unitSettings.pitch) * Mathf.Deg2Rad);
        // The settlement stands on a miniature base like its units' (Character3DBase), tilted to
        // the board as theirs are: a sloped rim and side band in the owner's colour around a top
        // laid with its own hex's painting (painted water for Lake-town and the island holds).
        // Everything it builds is fitted onto the base: nothing spills over onto the board.
        float baseRadius = BaseRadius * hexWidth, baseHeight = BaseHeight * hexWidth;
        BuildBase(city, root.transform, pcName, baseRadius, baseHeight, nation, layout.WaterGround ? null : ground, layout.WaterGround, unitSettings.shader);

        var built = new List<(CityLayout.Plan plan, CityMeshBuilder builder, Mesh mesh)>();
        float reachX = 0f, reachY = 0f;
        foreach (CityLayout.Plan plan in layout.Plans)
        {
            var builder = new CityMeshBuilder { UvScale = uvScale };
            plan.Build(builder);
            if (builder.IsEmpty) continue;
            Mesh mesh = builder.Build($"{pcName} {built.Count}");
            built.Add((plan, builder, mesh));
            // How far out on the board it reaches, across and up the board (building depth shows
            // on the board divided by the depth scale).
            Bounds b = mesh.bounds;
            reachX = Mathf.Max(reachX, Mathf.Abs(plan.Screen.x + b.min.x), Mathf.Abs(plan.Screen.x + b.max.x));
            reachY = Mathf.Max(reachY, Mathf.Abs(plan.Screen.y * depthScale + b.min.z), Mathf.Abs(plan.Screen.y * depthScale + b.max.z));
        }
        // The base is round in the figures' frame, as a unit's is; the town's layout was drawn for
        // the hex's screen depth, so its rows are pulled in (spacing only, not the buildings).
        float fit = reachX > 0f ? Mathf.Clamp(baseRadius * 0.96f / reachX, 0.35f, 1f) : 1f;
        float rows = reachY * fit > baseRadius * 0.96f ? baseRadius * 0.96f / (reachY * fit) : 1f;
        var content = new GameObject("Content").transform;
        content.SetParent(root.transform, false);
        content.localPosition = new Vector3(0f, baseHeight, 0f);
        content.localScale = Vector3.one * fit;

        foreach ((CityLayout.Plan plan, CityMeshBuilder builder, Mesh mesh) in built)
        {
            city.meshes.Add(mesh);

            var go = new GameObject("Building", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(content, false);
            go.transform.localPosition = new Vector3(plan.Screen.x, 0f, plan.Screen.y * depthScale * rows);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Each building weathered a little differently.
            float shade = 0.86f + 0.18f * (float)shades.NextDouble();
            float warmth = ((float)shades.NextDouble() - 0.5f) * 0.06f;
            var tint = new Color(shade * (1f + warmth), shade, shade * (1f - warmth), 1f);
            go.AddComponent<CityBuilding>().Init(city, renderer, builder.Height * fit, builder.Extent * fit, tint);
        }
        return city;
    }

    // The base, in hex widths: round on the board, a little inside the hex, low.
    private const float BaseRadius = 0.43f;
    private const float BaseHeight = 0.03f;

    // The units' base at a settlement's size, a little lower: the rim and band in the owner's
    // colour, the top laid with the middle of the hex's painted tile (or painted water).
    private static void BuildBase(City3D city, Transform root, string pcName, float radius, float height, Color nation, Sprite ground, bool water, Shader shader)
    {
        Texture topTexture;
        Rect uvRect;
        if (water || ground == null || ground.texture == null)
        {
            topTexture = water ? CityTextures.PaintedWater() : null;
            if (topTexture == null) topTexture = CityTextures.Get(CityTextures.Kind.Turf, new Color(0.40f, 0.38f, 0.26f));
            uvRect = new Rect(0f, 0f, 1f, 1f);
        }
        else
        {
            topTexture = ground.texture;
            Rect r = ground.textureRect;
            // The middle of the tile: its painting, clear of the hex's transparent corners.
            float w = r.width * 0.55f, hgt = r.height * 0.42f;
            uvRect = new Rect((r.center.x - w * 0.5f) / topTexture.width, (r.center.y - hgt * 0.5f) / topTexture.height, w / topTexture.width, hgt / topTexture.height);
        }
        Mesh mesh = Character3DBase.BuildSplit(p =>
        {
            float u = p.x / Character3DBase.TopFraction * 0.5f + 0.5f, v = p.y / Character3DBase.TopFraction * 0.5f + 0.5f;
            return new Vector2(uvRect.x + u * uvRect.width, uvRect.y + v * uvRect.height);
        }, $"{pcName} base");
        city.meshes.Add(mesh);
        if (!groundMaterials.TryGetValue(topTexture, out Material topMaterial) || topMaterial == null)
        {
            topMaterial = new Material(shader) { name = $"City ground {topTexture.name}", mainTexture = topTexture };
            groundMaterials[topTexture] = topMaterial;
        }

        var go = new GameObject("Base", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, height, 0f);
        go.transform.localScale = new Vector3(radius, height / Character3DBase.Thickness, radius);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { Character3DBase.MaterialFor(nation, shader, bronzeRim: true), topMaterial };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.AddComponent<CityBuilding>().Init(city, renderer, height, radius, Color.white, ground: true);
    }

    private static readonly Dictionary<Texture, Material> groundMaterials = new();


    // ── City ────────────────────────────────────────────────────────────────────────────────

    // Follows the PC tile it replaces: its fade (reveal pulse, unseen dimming) and its draw order.
    private void LateUpdate()
    {
        if (hex == null || hex.pcTexture == null) return;
        SpriteRenderer tile = hex.pcTexture;
        Tint = new Color(1f, 1f, 1f, tile.color.a);
        transform.rotation = Character3DLighting.BoardFrame(Character3DSettings.Instance);
        SortingLayerId = tile.sortingLayerID;
        SortingOrder = tile.sortingOrder;
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
        meshes.Clear();
        if (this == manager)
        {
            manager = null;
            foreach (Hex h in new List<Hex>(byHex.Keys)) Remove(h);
        }
    }
}

/// <summary>One building of a City3D: drawn with the units' shader and its own depth slab, so a
/// miniature in the town stands in front of the houses behind it and behind the ones in front.</summary>
[DefaultExecutionOrder(200)]
public class CityBuilding : DepthSlabbed
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int OriginId = Shader.PropertyToID("_C3D_Origin");
    private static readonly int UpId = Shader.PropertyToID("_C3D_Up");
    private static readonly int HeightId = Shader.PropertyToID("_C3D_Height");
    private static readonly int RimColorId = Shader.PropertyToID("_C3D_RimColor");
    private static readonly int DepthBaseId = Shader.PropertyToID("_DepthBase");
    private static readonly int DepthSlabWidthId = Shader.PropertyToID("_DepthSlabWidth");
    private static readonly int DepthSlabScaleId = Shader.PropertyToID("_DepthSlabScale");
    private static MaterialPropertyBlock block;

    private City3D city;
    private MeshRenderer meshRenderer;

    // Behind every unit, as the painted PC tiles were: a miniature standing in a dense town is
    // never hidden by its houses. Buildings still sort among themselves by board Y.
    // A base lies under all of its town's buildings.
    protected override float SortY => transform.position.y + (ground ? 20000f : 10000f);
    private bool ground;
    private float height, extent;
    private Color shade = Color.white;

    public void Init(City3D owner, MeshRenderer renderer, float buildingHeight, float buildingExtent, Color buildingShade, bool ground = false)
    {
        this.ground = ground;
        city = owner;
        meshRenderer = renderer;
        height = Mathf.Max(buildingHeight, 1e-4f);
        extent = buildingExtent;
        shade = buildingShade;
    }

    private void LateUpdate()
    {
        if (city == null || meshRenderer == null) return;
        if (meshRenderer.sortingLayerID != city.SortingLayerId) meshRenderer.sortingLayerID = city.SortingLayerId;
        if (meshRenderer.sortingOrder != city.SortingOrder) meshRenderer.sortingOrder = city.SortingOrder;
        block ??= new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(block);
        block.SetColor(ColorId, city.Tint * shade);
        block.SetVector(OriginId, transform.position);
        block.SetVector(UpId, transform.up);
        block.SetFloat(HeightId, height);
        block.SetColor(RimColorId, Color.clear);
        block.SetFloat(DepthBaseId, SlabBase);
        block.SetFloat(DepthSlabWidthId, SlabWidth * 0.9f);
        block.SetFloat(DepthSlabScaleId, 0.5f / (height + extent));
        meshRenderer.SetPropertyBlock(block);
    }
}
