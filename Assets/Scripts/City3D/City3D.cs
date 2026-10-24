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
        Board board = Board.Instance;
        Character3DSettings unitSettings = Character3DSettings.Instance;
        float hexWidth = board.hexSize.x * BoardLookSettings.Instance.cityScale;

        var root = new GameObject($"City3D {pc.pcName}");
        root.transform.SetParent(hex.transform, false);
        root.transform.position = hex.transform.position;
        root.transform.rotation = Character3DLighting.BoardFrame(unitSettings);
        var city = root.AddComponent<City3D>();
        city.hex = hex;
        city.key = key;

        CityLayout.Result layout = CityLayout.Make(pc, hexWidth, hex.pcTexture.sprite != null ? hex.pcTexture.sprite.name : null);
        Material[] materials = CityStyles.MaterialsFor(layout.Style, layout.Palette, pc.owner != null ? pc.owner.nationColor : Color.clear, unitSettings.shader, layout.Surfaces);
        // A stone course or roof-tile row is the same size on every building: the textures repeat
        // every tenth of a hex width.
        float uvScale = 1f / (0.1f * hexWidth);
        var shades = new System.Random(pc.pcName.GetHashCode());
        // Screen offset → the board frame's ground plane: depth is foreshortened by the pitch, and a
        // tilted camera (BoardNavigator) sees board distances shortened by cos(tilt) as well.
        float depthScale = Mathf.Cos(BoardNavigator.CurrentTilt * Mathf.Deg2Rad) / Mathf.Sin(Mathf.Max(5f, unitSettings.pitch) * Mathf.Deg2Rad);
        int index = 0;
        foreach (CityLayout.Plan plan in layout.Plans)
        {
            var builder = new CityMeshBuilder { UvScale = uvScale };
            plan.Build(builder);
            if (builder.IsEmpty) continue;
            Mesh mesh = builder.Build($"{pc.pcName} {index++}");
            city.meshes.Add(mesh);

            var go = new GameObject("Building", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(plan.Screen.x, 0f, plan.Screen.y * depthScale);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Each building weathered a little differently.
            float shade = 0.86f + 0.18f * (float)shades.NextDouble();
            float warmth = ((float)shades.NextDouble() - 0.5f) * 0.06f;
            var tint = new Color(shade * (1f + warmth), shade, shade * (1f - warmth), 1f);
            go.AddComponent<CityBuilding>().Init(city, renderer, builder.Height, builder.Extent, tint);
        }

        hex.pcTexture.forceRenderingOff = true;
        city.LateUpdate();
        return city;
    }

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
    protected override float SortY => transform.position.y + 10000f;
    private float height, extent;
    private Color shade = Color.white;

    public void Init(City3D owner, MeshRenderer renderer, float buildingHeight, float buildingExtent, Color buildingShade)
    {
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
