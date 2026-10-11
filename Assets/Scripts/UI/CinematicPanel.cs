using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Cinematic minimal" HUD panel: a dark smoked-glass fill (translucent, soft vertical falloff),
/// an inset gold-leaf hairline, and small diamond ornaments centred on the top and bottom edges.
/// Resolution independent (pure mesh), so it stays crisp at any HUD scale.
/// </summary>
[AddComponentMenu("UI/Cinematic Panel")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CinematicPanel : MaskableGraphic
{
    public Color fillTop = new(0.035f, 0.040f, 0.045f, 0.72f);
    public Color fillBottom = new(0.020f, 0.022f, 0.026f, 0.82f);
    public Color line = new(0.80f, 0.64f, 0.36f, 0.75f);
    [Min(0)] public float lineWidth = 1.2f;
    [Min(0)] public float inset = 5f;
    [Min(0)] public float ornament = 5f;
    public bool fill = true;
    public bool topOrnament = true;
    public bool bottomOrnament = false;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0 || r.height <= 0) return;

        if (fill)
        {
            int s = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), fillBottom * color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), fillTop * color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), fillTop * color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), fillBottom * color, Vector2.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        if (lineWidth <= 0) return;
        float lw = UiPixel.AtLeastOnePixel(this, lineWidth);
        Rect l = new(r.xMin + inset, r.yMin + inset, r.width - inset * 2f, r.height - inset * 2f);
        Color c = line * color;
        Quad(vh, new Rect(l.xMin, l.yMax - lw, l.width, lw), c);
        Quad(vh, new Rect(l.xMin, l.yMin, l.width, lw), c);
        Quad(vh, new Rect(l.xMin, l.yMin, lw, l.height), c);
        Quad(vh, new Rect(l.xMax - lw, l.yMin, lw, l.height), c);

        // Short corner ticks just outside the hairline.
        float t = Mathf.Min(10f, l.width * 0.1f);
        Quad(vh, new Rect(l.xMin - inset * 0.5f, l.yMax - lw, t, lw), c);
        Quad(vh, new Rect(l.xMax - t + inset * 0.5f, l.yMax - lw, t, lw), c);

        if (ornament > 0f)
        {
            if (topOrnament) Diamond(vh, new Vector2(l.center.x, l.yMax - lw * 0.5f), ornament, c);
            if (bottomOrnament) Diamond(vh, new Vector2(l.center.x, l.yMin + lw * 0.5f), ornament, c);
        }
    }

    private float builtScale;

    // Hairline thickness depends on on-screen scale (hover zoom, resolution), which doesn't dirty the mesh.
    private void LateUpdate()
    {
        if (!Mathf.Approximately(transform.lossyScale.x, builtScale)) { builtScale = transform.lossyScale.x; SetVerticesDirty(); }
    }

    private static void Quad(VertexHelper vh, Rect q, Color c)
    {
        int s = vh.currentVertCount;
        vh.AddVert(new Vector2(q.xMin, q.yMin), c, Vector2.zero);
        vh.AddVert(new Vector2(q.xMin, q.yMax), c, Vector2.zero);
        vh.AddVert(new Vector2(q.xMax, q.yMax), c, Vector2.zero);
        vh.AddVert(new Vector2(q.xMax, q.yMin), c, Vector2.zero);
        vh.AddTriangle(s, s + 1, s + 2);
        vh.AddTriangle(s, s + 2, s + 3);
    }

    private static void Diamond(VertexHelper vh, Vector2 p, float size, Color c)
    {
        int s = vh.currentVertCount;
        vh.AddVert(p + new Vector2(0, size), c, Vector2.zero);
        vh.AddVert(p + new Vector2(size, 0), c, Vector2.zero);
        vh.AddVert(p + new Vector2(0, -size), c, Vector2.zero);
        vh.AddVert(p + new Vector2(-size, 0), c, Vector2.zero);
        vh.AddTriangle(s, s + 1, s + 2);
        vh.AddTriangle(s, s + 2, s + 3);
    }
}

/// <summary>
/// Hairlines thinner than one screen pixel can fall between pixel centres and vanish — and which
/// edge vanishes depends on sub-pixel position, so panels randomly lose a side. A quad at least one
/// pixel thick always covers a row of pixel centres.
/// </summary>
public static class UiPixel
{
    public static float AtLeastOnePixel(Graphic g, float width)
    {
        Canvas canvas = g.canvas;
        if (canvas == null || width <= 0f) return width;
        Canvas root = canvas.rootCanvas;
        float rootScale = root.transform.lossyScale.x;
        if (rootScale <= 0f) return width;
        float pxPerUnit = root.renderMode == RenderMode.WorldSpace
            ? 0f
            : root.scaleFactor * g.transform.lossyScale.x / rootScale;
        return pxPerUnit > 0f ? Mathf.Max(width, 1.05f / pxPerUnit) : width;
    }
}

/// <summary>Generated UI sprites for the cinematic HUD (no art assets needed).</summary>
public static class HudSprites
{
    private static Sprite circle, ring, fadeDown, fadeUp;

    /// <summary>Anti-aliased filled circle (use as a Mask for round portraits / minimap).</summary>
    public static Sprite Circle => circle != null ? circle : circle = Build("HudCircle", 256, (d) => Mathf.Clamp01((0.5f - d) * 256f / 1.5f));

    /// <summary>Thin gold ring with a soft inner glow, to frame round elements.</summary>
    public static Sprite Ring => ring != null ? ring : ring = Build("HudRing", 256, (d) =>
    {
        float edge = Mathf.Clamp01(1f - Mathf.Abs(d - 0.485f) * 256f / 2.2f);
        float glow = Mathf.Clamp01(1f - Mathf.Abs(d - 0.47f) * 22f) * 0.35f;
        return Mathf.Max(edge, glow);
    });

    /// <summary>Vertical black gradient, opaque at the top (screen-edge legibility under the HUD).</summary>
    public static Sprite FadeDown => fadeDown != null ? fadeDown : fadeDown = BuildGradient("HudFadeDown", top: true);
    public static Sprite FadeUp => fadeUp != null ? fadeUp : fadeUp = BuildGradient("HudFadeUp", top: false);

    private static Sprite Build(string name, int size, System.Func<float, float> alphaAtRadius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float a = alphaAtRadius(Mathf.Sqrt(dx * dx + dy * dy));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.name = name;
        return s;
    }

    private static Sprite BuildGradient(string name, bool top)
    {
        const int h = 64;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
        for (int y = 0; y < h; y++)
        {
            float t = (y + 0.5f) / h;
            float a = top ? t : 1f - t;
            tex.SetPixel(0, y, new Color(0f, 0f, 0f, a * a));
        }
        tex.Apply(false, true);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
        s.name = name;
        return s;
    }
}
