using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime port of the Scenario Creator's SeamlessBlend preview (PLAN_ingame_seamless_hexes.md):
/// drives RetroLOTR/HexSeamlessBlend on every hex's terrain SpriteRenderer so tile rims cross-fade
/// into their neighbors and the overdraw feather hides the seams.
///
/// Hexes call MarkDirty when their terrain art or fog reveal state changes; the affected hex and
/// its 6 neighbors are batched in a set and rebuilt once per frame in LateUpdate (never per frame
/// per hex). All per-hex data (including the tile-local geometry — tile art is trimmed to
/// slightly different sizes per sprite, so offsets/aspect are per hex, not global) travels in a
/// MaterialPropertyBlock over one shared material.
/// </summary>
public class HexSeamlessTerrain : MonoBehaviour
{
    private static HexSeamlessTerrain instance;
    private static Material material;
    private static Material skinTerrainSource;
    private static Material skinGridSource;
    private static Board cachedBoard;
    private static MaterialPropertyBlock mpb;
    private static readonly HashSet<Hex> dirty = new();
    private static readonly List<Hex> flushBuffer = new();

    private static readonly int SpriteUVId = Shader.PropertyToID("_SpriteUV");
    private static readonly int CellCenterId = Shader.PropertyToID("_CellCenter");
    private static readonly int AspectYId = Shader.PropertyToID("_AspectY");
    private static readonly int GridOnId = Shader.PropertyToID("_GridOn");
    private static readonly int GridColorId = Shader.PropertyToID("_GridColor");
    private static readonly int GridIntensityId = Shader.PropertyToID("_GridIntensity");
    private static readonly int GridWidthId = Shader.PropertyToID("_GridWidth");
    private static readonly int GridGlowWidthId = Shader.PropertyToID("_GridGlowWidth");
    private static readonly int GridHueScaleId = Shader.PropertyToID("_GridHueScale");
    private static readonly int[] NeighborTexIds = BuildPropertyIds("_NeighborTex");
    private static readonly int[] NeighborUVIds = BuildPropertyIds("_NeighborUV");
    private static readonly int[] NeighborOffsetIds = BuildPropertyIds("_NeighborOffset");
    private static readonly int[] NeighborValidIds = BuildPropertyIds("_NeighborValid");

    private static int[] BuildPropertyIds(string prefix)
    {
        int[] ids = new int[6];
        for (int d = 0; d < 6; d++) ids[d] = Shader.PropertyToID(prefix + d);
        return ids;
    }

    /// <summary>
    /// Queues this hex AND its 6 neighbors for a MaterialPropertyBlock rebuild at the end of the
    /// frame (their rims blend toward this hex's art / reveal state too).
    /// </summary>
    public static void MarkDirty(Hex hex)
    {
        if (hex == null) return;
        EnsureInstance();
        dirty.Add(hex);

        Board board = GetBoard();
        if (board == null || board.hexes == null) return;
        Vector2Int[] neighbors = ((hex.v2.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;
        for (int d = 0; d < 6; d++)
        {
            if (board.hexes.TryGetValue(hex.v2 + neighbors[d], out Hex neighbor) && neighbor != null)
                dirty.Add(neighbor);
        }
    }

    /// <summary>Toggles the neon seam grid on the shared runtime material (default off).</summary>
    public static void SetGridEnabled(bool enabled)
    {
        EnsureInstance();
        if (!EnsureMaterial()) return;
        ApplyGridLookOverrides();
        material.SetFloat(GridOnId, enabled ? 1f : 0f);
    }

    /// <summary>
    /// Per-skin override for which material renders hex terrain (e.g. HexSeamlessBlendGame vs. a
    /// different skin's material) and which asset drives the grid look (e.g. HexNeonGrid). Pass
    /// null for either to fall back to Board.hexSeamlessBlendMaterial / Board.hexGridMaterial.
    /// Rebuilds every existing hex so the change is visible immediately.
    /// </summary>
    public static void SetSkinMaterials(Material terrainSource, Material gridSource)
    {
        skinTerrainSource = terrainSource;
        skinGridSource = gridSource;

        if (material != null)
        {
            Destroy(material);
            material = null;
        }

        EnsureInstance();
        if (!EnsureMaterial()) return;
        ApplyGridLookOverrides();

        Board board = GetBoard();
        if (board == null || board.hexes == null) return;
        foreach (Hex hex in board.hexes.Values) MarkDirty(hex);
    }

    // Grid look (color/intensity/width/glow/hue) is authored on its own dedicated asset
    // (Board.hexGridMaterial, or a per-skin override — see SetSkinMaterials) rather than
    // hexSeamlessBlendMaterial, so tuning it never means touching the terrain-blend asset. Re-read
    // every frame (see LateUpdate) rather than only on toggle, so live edits to that asset's
    // Inspector values (a common way to tune it while watching the board) show up immediately
    // instead of needing the grid re-toggled or Play mode restarted.
    private static void ApplyGridLookOverrides()
    {
        Board board = GetBoard();
        Material gridLook = skinGridSource != null ? skinGridSource : (board != null ? board.hexGridMaterial : null);
        if (gridLook == null) return;

        material.SetColor(GridColorId, gridLook.GetColor(GridColorId));
        material.SetFloat(GridIntensityId, gridLook.GetFloat(GridIntensityId));
        material.SetFloat(GridWidthId, gridLook.GetFloat(GridWidthId));
        material.SetFloat(GridGlowWidthId, gridLook.GetFloat(GridGlowWidthId));
        material.SetFloat(GridHueScaleId, gridLook.GetFloat(GridHueScaleId));
    }

    private void LateUpdate()
    {
        if (material != null)
        {
            ApplyGridLookOverrides();
            material.SetFloat(ReliefId, BoardLookSettings.Instance.terrainRelief);
        }
        ApplyPcLook();
        Flush();
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        GameObject go = new("HexSeamlessTerrain");
        instance = go.AddComponent<HexSeamlessTerrain>();
    }

    private static bool EnsureMaterial()
    {
        if (material != null) return true;
        Board board = GetBoard();
        Material source = skinTerrainSource != null ? skinTerrainSource
            : (board != null ? board.hexSeamlessBlendMaterial : null);
        if (source == null)
        {
            Debug.LogError("HexSeamlessTerrain: Board.hexSeamlessBlendMaterial is not assigned.");
            return false;
        }
        // Runtime copy: play-mode tweaks (grid toggle) must never dirty the asset.
        material = new Material(source);
        ApplyCommandLineOverrides(material);
        return true;
    }

    // Look tuning without a rebuild: -hexmat "_BlendBand=0.45,_EdgeTrim=0.1"
    private static void ApplyCommandLineOverrides(Material target)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-hexmat");
        if (i < 0 || i + 1 >= args.Length) return;
        foreach (string pair in args[i + 1].Split(','))
        {
            string[] kv = pair.Split('=');
            if (kv.Length == 2 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && target.HasProperty(kv[0].Trim()))
                target.SetFloat(kv[0].Trim(), v);
        }
    }

    private static Board GetBoard()
    {
        if (cachedBoard == null) cachedBoard = Board.Instance;
        return cachedBoard;
    }

    private static void Flush()
    {
        if (dirty.Count == 0) return;
        Board board = GetBoard();
        if (board == null || board.hexes == null) { dirty.Clear(); return; }
        if (!EnsureMaterial()) { dirty.Clear(); return; }

        flushBuffer.Clear();
        flushBuffer.AddRange(dirty);
        dirty.Clear();
        for (int i = 0; i < flushBuffer.Count; i++) Rebuild(flushBuffer[i], board);
        flushBuffer.Clear();
    }

    // ── Settlement art (RetroLOTR/PCArt) ─────────────────────────────────────────────────────
    // The PC sprite on top of the terrain gets its own material: graded toward the blended terrain
    // and with its lower ground rim dissolved into it (see PCArt.shader). Look values come from
    // BoardLookSettings and are re-read every frame, so they can be tuned live.
    private static Material pcMaterial;
    private static readonly int HexCenterId = Shader.PropertyToID("_HexCenter");
    private static readonly int ReliefId = Shader.PropertyToID("_Relief");
    private static readonly int HexStepId = Shader.PropertyToID("_HexStep");

    private static bool EnsurePcMaterial()
    {
        if (pcMaterial != null) return true;
        Shader shader = BoardLookSettings.Instance.pcArtShader;
        if (shader == null) return false;
        pcMaterial = new Material(shader) { name = "PCArt (runtime)" };
        ApplyPcLook();
        return true;
    }

    private static void ApplyPcLook()
    {
        if (pcMaterial == null) return;
        BoardLookSettings look = BoardLookSettings.Instance;
        pcMaterial.SetFloat("_Saturation", look.pcSaturation);
        pcMaterial.SetFloat("_Contrast", look.pcContrast);
        pcMaterial.SetFloat("_Brightness", look.pcBrightness);
        pcMaterial.SetFloat("_RimStart", look.pcRimStart);
    }

    private static void RebuildPc(Hex hex, Board board)
    {
        SpriteRenderer renderer = hex.pcTexture;
        if (renderer == null || !EnsurePcMaterial()) return;
        if (renderer.sharedMaterial != pcMaterial) renderer.sharedMaterial = pcMaterial;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(mpb);
        mpb.SetVector(HexCenterId, hex.transform.position);
        mpb.SetVector(HexStepId, new Vector4(board.hexSize.x, board.hexSize.y));
        renderer.SetPropertyBlock(mpb);
    }

    private static void Rebuild(Hex hex, Board board)
    {
        if (hex == null) return;
        RebuildPc(hex, board);
        SpriteRenderer renderer = hex.terrainTexture;
        if (renderer == null) return;

        Sprite sprite = renderer.sprite;
        Vector2 drawnSize = hex.GetTerrainDrawnWorldSize();
        if (sprite == null || sprite.texture == null || drawnSize.x <= 0f || drawnSize.y <= 0f) return;

        if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;

        if (mpb == null) mpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(mpb);

        mpb.SetVector(SpriteUVId, NormalizedRect(sprite));

        // The shader works in a tile-local frame where 1 unit = THIS hex's drawn quad width.
        // Tile art is trimmed per sprite, so the frame scale is per hex. Offsets mirror
        // BoardGenerator.GetPosition: one column of spacing is hexSize.x, odd rows shift
        // +0.5 * hexSize.x, and +row is -y in the world (row 0 = north/top, matching the
        // Scenario Creator). Direction order follows the table index of
        // Board.evenRowNeighbors/oddRowNeighbors (both tables resolve to the same six world
        // directions): 0=(row+1) SE, 1=E, 2=(row-1) NE, 3=NW, 4=W, 5=SW.
        float width = drawnSize.x;
        float colX = board.hexSize.x / width;
        float rowY = board.hexSize.y / width;
        mpb.SetFloat(AspectYId, drawnSize.y / width);
        mpb.SetVector(NeighborOffsetIds[0], new Vector4(colX * 0.5f, -rowY));  // SE (row+1)
        mpb.SetVector(NeighborOffsetIds[1], new Vector4(colX, 0f));            // E
        mpb.SetVector(NeighborOffsetIds[2], new Vector4(colX * 0.5f, rowY));   // NE (row-1)
        mpb.SetVector(NeighborOffsetIds[3], new Vector4(-colX * 0.5f, rowY));  // NW (row-1)
        mpb.SetVector(NeighborOffsetIds[4], new Vector4(-colX, 0f));           // W
        mpb.SetVector(NeighborOffsetIds[5], new Vector4(-colX * 0.5f, -rowY)); // SW (row+1)

        // Map-space center in tile-width units, y-up — only feeds the grid's rainbow hue, so any
        // shared origin works.
        Vector3 position = hex.transform.position;
        mpb.SetVector(CellCenterId, new Vector4(position.x / width, position.y / width));

        Vector2Int[] neighbors = ((hex.v2.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;
        for (int d = 0; d < 6; d++)
        {
            // Tri-state: 1 = rendered neighbor (blend + feather), 0 = no neighbor / no art (crisp
            // edge), -1 = fog-of-war neighbor (fade our own rim into the fog; never sample the
            // hidden art — that would preview unscouted terrain).
            float valid = 0f;
            if (board.hexes.TryGetValue(hex.v2 + neighbors[d], out Hex neighbor) &&
                neighbor != null && neighbor.terrainTexture != null)
            {
                if (!neighbor.IsHexRevealed() && !BoardLook.ShroudActive)
                {
                    valid = -1f;
                }
                else
                {
                    Sprite neighborSprite = neighbor.terrainTexture.sprite;
                    if (neighborSprite != null && neighborSprite.texture != null)
                    {
                        valid = 1f;
                        mpb.SetTexture(NeighborTexIds[d], neighborSprite.texture);
                        mpb.SetVector(NeighborUVIds[d], NormalizedRect(neighborSprite));
                    }
                }
            }
            mpb.SetFloat(NeighborValidIds[d], valid);
        }

        renderer.SetPropertyBlock(mpb);
    }

    private static Vector4 NormalizedRect(Sprite sprite)
    {
        Texture texture = sprite.texture;
        Rect rect = sprite.rect;
        return new Vector4(rect.x / texture.width, rect.y / texture.height,
                           rect.width / texture.width, rect.height / texture.height);
    }
}
