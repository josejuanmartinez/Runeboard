using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The body of a city banner (HexNameplates): a long hexagonal ribbon — pointed ends, like the
/// city banners of Civilization — in the owner's nation colour with a soft vertical sheen, a thin
/// rim, a drop shadow, and a small diamond crest on top for capitals. Pure mesh, crisp at any scale.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CityBannerGraphic : MaskableGraphic
{
    public Color fillTop = new(0.35f, 0.3f, 0.24f, 0.96f);
    public Color fillBottom = new(0.16f, 0.13f, 0.1f, 0.96f);
    public Color rim = new(0.86f, 0.7f, 0.4f, 0.95f);
    public Color shadow = new(0f, 0f, 0f, 0.45f);
    [Min(0)] public float rimWidth = 1.6f;
    [Min(0)] public float point = 11f;   // how far the pointed ends reach in
    public bool crest;                   // capital: a diamond on the top edge

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float p = Mathf.Min(point, r.width * 0.25f);
        Vector2[] outline =
        {
            new(r.xMin, r.center.y), new(r.xMin + p, r.yMax), new(r.xMax - p, r.yMax),
            new(r.xMax, r.center.y), new(r.xMax - p, r.yMin), new(r.xMin + p, r.yMin),
        };

        // Shadow, nudged down-right.
        AddFan(vh, Offset(outline, new Vector2(1.5f, -2.5f)), _ => shadow);
        // Body with a vertical gradient (lighter at the top, like a lacquered banner).
        AddFan(vh, outline, v => Color.Lerp(fillBottom, fillTop, Mathf.InverseLerp(r.yMin, r.yMax, v.y)));
        // A brighter sheen along the upper third.
        Vector2[] sheen =
        {
            new(r.xMin + p * 0.66f, r.yMax - r.height * 0.33f), new(r.xMin + p, r.yMax - 1f),
            new(r.xMax - p, r.yMax - 1f), new(r.xMax - p * 0.66f, r.yMax - r.height * 0.33f),
        };
        Color sheenTop = new(1f, 1f, 1f, 0.16f), sheenBottom = new(1f, 1f, 1f, 0f);
        AddFan(vh, sheen, v => Color.Lerp(sheenBottom, sheenTop, Mathf.InverseLerp(r.yMax - r.height * 0.33f, r.yMax, v.y)));

        float w = UiPixel.AtLeastOnePixel(this, rimWidth);
        if (w > 0f)
            for (int i = 0; i < outline.Length; i++)
                AddLine(vh, outline[i], outline[(i + 1) % outline.Length], w, rim);

        if (crest)
        {
            float s = Mathf.Max(5f, r.height * 0.22f);
            Vector2 c = new(r.center.x, r.yMax);
            Vector2[] diamond = { c + new Vector2(0f, s), c + new Vector2(s * 0.75f, 0f), c + new Vector2(0f, -s), c + new Vector2(-s * 0.75f, 0f) };
            AddFan(vh, diamond, _ => rim);
        }
    }

    private static Vector2[] Offset(Vector2[] points, Vector2 by)
    {
        var moved = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++) moved[i] = points[i] + by;
        return moved;
    }

    // Convex polygon as a triangle fan around its centroid.
    private void AddFan(VertexHelper vh, Vector2[] points, System.Func<Vector2, Color> colorAt)
    {
        Vector2 centre = Vector2.zero;
        foreach (Vector2 v in points) centre += v;
        centre /= points.Length;
        int start = vh.currentVertCount;
        vh.AddVert(centre, colorAt(centre) * color, Vector4.zero);
        foreach (Vector2 v in points) vh.AddVert(v, colorAt(v) * color, Vector4.zero);
        for (int i = 0; i < points.Length; i++)
            vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points.Length);
    }

    private void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
    {
        Vector2 n = Vector2.Perpendicular((b - a).normalized) * (width * 0.5f);
        int start = vh.currentVertCount;
        Color tinted = c * color;
        vh.AddVert(a - n, tinted, Vector4.zero);
        vh.AddVert(a + n, tinted, Vector4.zero);
        vh.AddVert(b + n, tinted, Vector4.zero);
        vh.AddVert(b - n, tinted, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
