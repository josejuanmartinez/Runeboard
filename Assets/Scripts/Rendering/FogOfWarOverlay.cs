using UnityEngine;

/// <summary>
/// Draws the fog of war as one soft, drifting cloud layer over the whole board (shader
/// Runeboard/FogOfWar), instead of a per-hex on/off state. A small visibility mask (a few texels
/// per world unit) is painted from every hex's state and blurred, so the boundary of sight
/// feathers across tiles and moves with the clouds:
///   in sight → clear · known but unseen → thin veil · never explored → full cloud.
/// Land in sight also gets slow, drifting cloud shadows (BoardLookSettings).
/// Sits on the Hexes sorting layer at order 0: above all terrain/PC art, below units and labels.
/// </summary>
public class FogOfWarOverlay : MonoBehaviour
{
    private const float TexelsPerUnit = 5f;
    private const float Margin = 6f;
    private const float RefreshInterval = 0.1f;
    private const float RevealRadius = 0.62f; // world units, ≈ one hex
    private const byte Unexplored = 255;
    private const byte Remembered = 120;
    private const byte InSight = 0;

    private static FogOfWarOverlay instance;

    private Board board;
    private Texture2D mask;
    private byte[] pixels;
    private byte[] scratch;
    private byte[] water;   // 255 on water hexes (shallow or deep), blurred: where the shader draws glints
    private byte[] packed;  // RG16 upload: R = visibility, G = water
    private int width, height;
    private Vector2 origin, size;
    private float nextRefresh;
    private int lastSignature = int.MinValue;
    private Material material;

    // Held off while the leader selection shows the open board behind its carousel.
    private static bool hidden;

    public static void SetHidden(bool value)
    {
        hidden = value;
        if (instance != null && instance.TryGetComponent(out SpriteRenderer sr)) sr.enabled = !value;
    }

    public static void Ensure()
    {
        if (instance != null) return;
        Board board = Board.Instance;
        if (board == null || board.hexes == null || board.hexes.Count == 0) return;
        Material template = Resources.Load<Material>("Materials/FogOfWarOverlay");
        if (template == null) { Debug.LogWarning("[FogOfWar] material missing"); return; }
        var go = new GameObject("FogOfWarOverlay");
        instance = go.AddComponent<FogOfWarOverlay>();
        instance.Build(board, template);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (mask != null) Destroy(mask);
        if (material != null) Destroy(material);
    }

    private void Build(Board owner, Material template)
    {
        board = owner;
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        foreach (Hex hex in board.hexes.Values)
        {
            if (hex == null) continue;
            Vector2 p = hex.transform.position;
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        origin = min - Vector2.one * Margin;
        size = (max - min) + Vector2.one * Margin * 2f;
        width = Mathf.CeilToInt(size.x * TexelsPerUnit);
        height = Mathf.CeilToInt(size.y * TexelsPerUnit);
        pixels = new byte[width * height];
        scratch = new byte[width * height];
        water = new byte[width * height];
        packed = new byte[width * height * 2];
        mask = new Texture2D(width, height, TextureFormat.RG16, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FogOfWarMask" };

        material = new Material(template);
        material.SetTexture("_Mask", mask);
        material.SetVector("_MaskRect", new Vector4(origin.x, origin.y, size.x, size.y));
        BoardLookSettings look = BoardLookSettings.Instance;
        material.SetFloat("_CloudShadow", look.cloudShadow);
        material.SetFloat("_CloudShadowScale", look.cloudShadowScale);
        material.SetVector("_CloudShadowDrift", look.cloudShadowDrift);
        material.SetFloat("_WaterShimmer", look.waterShimmer);
        material.SetFloat("_EdgeGlow", look.fogEdgeGlow);

        // A unit white sprite stretched over the board's bounds.
        var tex = Texture2D.whiteTexture;
        Sprite quad = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        var sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = quad;
        sr.sharedMaterial = material;
        sr.sortingLayerName = "Hexes";
        sr.sortingOrder = 0;
        sr.enabled = !hidden;
        transform.position = new Vector3(origin.x + size.x * 0.5f, origin.y + size.y * 0.5f, 0f);
        transform.localScale = new Vector3(size.x, size.y, 1f);

        Repaint();
        Upload(snap: true);
    }

    private void LateUpdate()
    {
        if (board == null) return;
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + RefreshInterval;
            int signature = ComputeSignature();
            if (signature != lastSignature) { Repaint(); fading = true; }
        }
        if (fading) fading = !Upload(snap: false);
    }

    // The mask on screen eases toward the freshly painted one, so newly seen land clears in one
    // smooth fade (and fog rolls back in the same way) instead of snapping a moment later.
    private const float FadeSeconds = 0.5f;
    private float[] shown;
    private bool fading;

    // Moves the shown mask a step toward the target (or straight onto it) and uploads it; true once there.
    private bool Upload(bool snap)
    {
        shown ??= new float[pixels.Length];
        float step = snap ? 256f : 255f * Time.unscaledDeltaTime / FadeSeconds;
        bool arrived = true;
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = Mathf.MoveTowards(shown[i], pixels[i], step);
            shown[i] = v;
            if (v != pixels[i]) arrived = false;
            packed[i * 2] = (byte)Mathf.RoundToInt(v);
            packed[i * 2 + 1] = water[i];
        }
        mask.SetPixelData(packed, 0);
        mask.Apply(false, false);
        return arrived;
    }

    private static byte StateOf(Hex hex) => !hex.IsHexRevealed() ? Unexplored : hex.IsHexSeen() ? InSight : Remembered;

    private int ComputeSignature()
    {
        unchecked
        {
            int s = 17;
            foreach (Hex hex in board.hexes.Values)
                if (hex != null) s = s * 31 + StateOf(hex) + hex.v2.x * 7 + hex.v2.y + (hex.IsWaterTerrain() ? 13 : 0);
            return s;
        }
    }

    private void Repaint()
    {
        lastSignature = ComputeSignature();
        System.Array.Fill(pixels, Unexplored);
        System.Array.Clear(water, 0, water.Length);
        foreach (Hex hex in board.hexes.Values)
        {
            if (hex == null) continue;
            if (hex.IsWaterTerrain()) Stamp(water, hex, 255, keepLowest: false);
            byte v = StateOf(hex);
            if (v != Unexplored) Stamp(pixels, hex, v, keepLowest: true);
        }
        Blur(pixels, 2);
        Blur(pixels, 2);
        Blur(water, 1);
    }

    // Writes v into a disc of RevealRadius around the hex, keeping the lower (or higher) value.
    private void Stamp(byte[] target, Hex hex, byte v, bool keepLowest)
    {
        int r = Mathf.CeilToInt(RevealRadius * TexelsPerUnit);
        float r2 = RevealRadius * RevealRadius * TexelsPerUnit * TexelsPerUnit;
        Vector2 p = ((Vector2)hex.transform.position - origin) * TexelsPerUnit;
        int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
        for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(height - 1, cy + r); y++)
        {
            int row = y * width;
            float dy = y - p.y;
            for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(width - 1, cx + r); x++)
            {
                float dx = x - p.x;
                if (dx * dx + dy * dy > r2) continue;
                byte current = target[row + x];
                if (keepLowest ? v < current : v > current) target[row + x] = v;
            }
        }
    }

    // Separable box blur (radius in texels) — turns hex-stamped discs into soft fog boundaries.
    private void Blur(byte[] data, int radius)
    {
        int span = radius * 2 + 1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width, sum = 0;
            for (int x = -radius; x <= radius; x++) sum += data[row + Mathf.Clamp(x, 0, width - 1)];
            for (int x = 0; x < width; x++)
            {
                scratch[row + x] = (byte)(sum / span);
                sum += data[row + Mathf.Min(x + radius + 1, width - 1)] - data[row + Mathf.Max(x - radius, 0)];
            }
        }
        for (int x = 0; x < width; x++)
        {
            int sum = 0;
            for (int y = -radius; y <= radius; y++) sum += scratch[Mathf.Clamp(y, 0, height - 1) * width + x];
            for (int y = 0; y < height; y++)
            {
                data[y * width + x] = (byte)(sum / span);
                sum += scratch[Mathf.Min(y + radius + 1, height - 1) * width + x] - scratch[Mathf.Max(y - radius, 0) * width + x];
            }
        }
    }
}
