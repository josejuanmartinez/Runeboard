using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent dark lacquer, fine brass edges, and clipped corners.</summary>
[AddComponentMenu("UI/Runeboard Panel")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RuneboardPanel : MaskableGraphic
{
    public Color topColor = new(0.095f, 0.105f, 0.095f, 0.97f);
    public Color bottomColor = new(0.025f, 0.035f, 0.033f, 0.97f);
    public Color edgeColor = new(0.60f, 0.46f, 0.24f, 0.7f);
    [Min(0)] public float corner = 7f;
    [Min(0)] public float edgeWidth = 1f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        float cut = Mathf.Min(corner, Mathf.Min(rect.width, rect.height) * 0.25f);
        Vector2[] points = {
            new(rect.xMin + cut, rect.yMin), new(rect.xMax - cut, rect.yMin),
            new(rect.xMax, rect.yMin + cut), new(rect.xMax, rect.yMax - cut),
            new(rect.xMax - cut, rect.yMax), new(rect.xMin + cut, rect.yMax),
            new(rect.xMin, rect.yMax - cut), new(rect.xMin, rect.yMin + cut)
        };
        vh.AddVert(rect.center, Color.Lerp(bottomColor, topColor, 0.5f) * color, Vector2.zero);
        foreach (Vector2 p in points)
            vh.AddVert(p, Color.Lerp(bottomColor, topColor, Mathf.InverseLerp(rect.yMin, rect.yMax, p.y)) * color, Vector2.zero);
        for (int i = 0; i < 8; i++) vh.AddTriangle(0, i + 1, (i + 1) % 8 + 1);
        if (edgeWidth <= 0) return;
        for (int i = 0; i < 8; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % 8];
            Vector2 delta = (b - a).normalized;
            Vector2 inset = new Vector2(-delta.y, delta.x) * edgeWidth;
            int start = vh.currentVertCount;
            Color tint = edgeColor * color;
            vh.AddVert(a, tint, Vector2.zero);
            vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(b + inset, tint, Vector2.zero);
            vh.AddVert(a + inset, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
