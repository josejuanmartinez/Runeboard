using System.Collections.Generic;
using UnityEngine;

// Low-poly building blocks for the procedural settlements (see CityLayout): boxes, gable roofs,
// cylinders, cones, domes and battlements, flat-shaded (every face has its own vertices) so the
// models read as cut and painted miniatures under the board's two-tone key light. Each face
// belongs to a surface cell (one submesh and material per cell, see CityStyles) and gets planar
// UVs at a fixed world scale (UvScale repeats per unit): u along the face, v up it, so stone
// courses, timbering and roof tiles keep their size on every building. Coordinates: +X right, +Y up,
// +Z away from the viewer, origin on the ground; Rotate/Translate place a part before it is added.
public class CityMeshBuilder
{
    public enum Cell { Wall, Roof, Wood, Stone, Trim, Dark, Accent, Nation, Foliage }
    public const int CellCount = 9;

    private readonly List<Vector3> vertices = new();
    private readonly List<Vector3> normals = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<int>[] triangles = NewCells();

    private static List<int>[] NewCells()
    {
        var cells = new List<int>[CellCount];
        for (int i = 0; i < CellCount; i++) cells[i] = new List<int>();
        return cells;
    }

    /// <summary>Texture repeats per world unit.</summary>
    public float UvScale = 1f;

    private Vector3 offset;
    private Quaternion rotation = Quaternion.identity;

    public float Height { get; private set; }
    public float Extent { get; private set; } // largest horizontal reach from the origin

    public bool IsEmpty => vertices.Count == 0;

    // Places the next parts (until changed again): rotated by yaw degrees about the origin, then moved.
    public CityMeshBuilder At(Vector3 position, float yaw = 0f)
    {
        offset = position;
        rotation = Quaternion.Euler(0f, yaw, 0f);
        return this;
    }

    public Mesh Build(string name)
    {
        var mesh = new Mesh { name = name };
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = CellCount;
        for (int i = 0; i < CellCount; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── Faces ───────────────────────────────────────────────────────────────────────────────

    private Vector3 P(Vector3 local) => offset + rotation * local;


    // One flat triangle, wound so it faces `outward` (Unity treats clockwise as the front face).
    private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Cell cell)
    {
        a = P(a); b = P(b); c = P(c);
        outward = rotation * outward;
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-12f) return;
        if (Vector3.Dot(n, outward) < 0f) { (b, c) = (c, b); n = -n; }
        n.Normalize();
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        normals.Add(n); normals.Add(n); normals.Add(n);
        // Planar mapping: u runs along the face, v up it (up the slope on a roof; across a flat top).
        Vector3 u = Vector3.Cross(Vector3.up, n);
        u = u.sqrMagnitude < 0.01f ? Vector3.right : u.normalized;
        Vector3 v = Vector3.Cross(n, u).normalized;
        uvs.Add(new Vector2(Vector3.Dot(a, u), Vector3.Dot(a, v)) * UvScale);
        uvs.Add(new Vector2(Vector3.Dot(b, u), Vector3.Dot(b, v)) * UvScale);
        uvs.Add(new Vector2(Vector3.Dot(c, u), Vector3.Dot(c, v)) * UvScale);
        List<int> cellTriangles = triangles[(int)cell];
        cellTriangles.Add(start); cellTriangles.Add(start + 1); cellTriangles.Add(start + 2);
        Track(a); Track(b); Track(c);
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Cell cell)
    {
        Tri(a, b, c, outward, cell);
        Tri(a, c, d, outward, cell);
    }

    private void Track(Vector3 p)
    {
        Height = Mathf.Max(Height, p.y);
        Extent = Mathf.Max(Extent, new Vector2(p.x, p.z).magnitude);
    }

    // ── Parts ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A box standing on y0 (size = width x, height y, depth z), centred on (x, z).</summary>
    public void Box(Vector3 bottomCentre, Vector3 size, Cell cell, Cell? top = null)
    {
        float hx = size.x * 0.5f, hz = size.z * 0.5f;
        Vector3 c = bottomCentre;
        Vector3 v000 = c + new Vector3(-hx, 0, -hz), v100 = c + new Vector3(hx, 0, -hz);
        Vector3 v001 = c + new Vector3(-hx, 0, hz), v101 = c + new Vector3(hx, 0, hz);
        Vector3 v010 = v000 + Vector3.up * size.y, v110 = v100 + Vector3.up * size.y;
        Vector3 v011 = v001 + Vector3.up * size.y, v111 = v101 + Vector3.up * size.y;
        Quad(v000, v100, v110, v010, Vector3.back, cell);
        Quad(v001, v101, v111, v011, Vector3.forward, cell);
        Quad(v000, v001, v011, v010, Vector3.left, cell);
        Quad(v100, v101, v111, v110, Vector3.right, cell);
        Quad(v010, v110, v111, v011, Vector3.up, top ?? cell);
    }

    /// <summary>A gable roof on a w x d footprint at height y, ridge along X, with a little overhang.</summary>
    public void GableRoof(Vector3 bottomCentre, float width, float depth, float height, Cell roof, Cell gable)
    {
        float hx = width * 0.5f + 0.006f, hz = depth * 0.5f + 0.008f;
        Vector3 c = bottomCentre;
        Vector3 fl = c + new Vector3(-hx, 0, -hz), fr = c + new Vector3(hx, 0, -hz);
        Vector3 bl = c + new Vector3(-hx, 0, hz), br = c + new Vector3(hx, 0, hz);
        Vector3 rl = c + new Vector3(-hx, height, 0), rr = c + new Vector3(hx, height, 0);
        Quad(fl, fr, rr, rl, new Vector3(0, hz, -height), roof);
        Quad(bl, br, rr, rl, new Vector3(0, hz, height), roof);
        Tri(fl, bl, rl, Vector3.left, gable);
        Tri(fr, br, rr, Vector3.right, gable);
    }

    /// <summary>A pyramid (n = 4) or cone (n > 4) standing on y, apex at y + height.</summary>
    public void Cone(Vector3 bottomCentre, float radius, float height, int sides, Cell cell, float twist = 0f)
    {
        Vector3 apex = bottomCentre + Vector3.up * height;
        for (int i = 0; i < sides; i++)
        {
            Vector3 a = Ring(bottomCentre, radius, i, sides, twist);
            Vector3 b = Ring(bottomCentre, radius, i + 1, sides, twist);
            Vector3 mid = (a + b) * 0.5f - bottomCentre;
            Tri(a, b, apex, new Vector3(mid.x, radius * 0.5f, mid.z), cell);
        }
    }

    /// <summary>A prism with n sides (a cylinder for n ≥ 8) standing on y; tapers to topScale.</summary>
    public void Cylinder(Vector3 bottomCentre, float radius, float height, int sides, Cell cell, Cell? top = null, float topScale = 1f, float twist = 0f)
    {
        Vector3 topCentre = bottomCentre + Vector3.up * height;
        for (int i = 0; i < sides; i++)
        {
            Vector3 a = Ring(bottomCentre, radius, i, sides, twist), b = Ring(bottomCentre, radius, i + 1, sides, twist);
            Vector3 c = Ring(topCentre, radius * topScale, i + 1, sides, twist), d = Ring(topCentre, radius * topScale, i, sides, twist);
            Vector3 mid = (a + b) * 0.5f - bottomCentre;
            Quad(a, b, c, d, new Vector3(mid.x, 0, mid.z), cell);
            if (top.HasValue) Tri(topCentre, d, c, Vector3.up, top.Value);
        }
    }

    /// <summary>A low-poly dome (half ellipsoid) standing on y.</summary>
    public void Dome(Vector3 bottomCentre, float radius, float height, int sides, int rings, Cell cell)
    {
        for (int r = 0; r < rings; r++)
        {
            float a0 = r / (float)rings * Mathf.PI * 0.5f, a1 = (r + 1) / (float)rings * Mathf.PI * 0.5f;
            float r0 = Mathf.Cos(a0) * radius, r1 = Mathf.Cos(a1) * radius;
            Vector3 c0 = bottomCentre + Vector3.up * (Mathf.Sin(a0) * height);
            Vector3 c1 = bottomCentre + Vector3.up * (Mathf.Sin(a1) * height);
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = Ring(c0, r0, i, sides), b = Ring(c0, r0, i + 1, sides);
                Vector3 c = Ring(c1, r1, i + 1, sides), d = Ring(c1, r1, i, sides);
                Vector3 outward = ((a + b + c + d) * 0.25f - bottomCentre);
                if (r == rings - 1) Tri(a, b, c, outward, cell);
                else Quad(a, b, c, d, outward, cell);
            }
        }
    }

    /// <summary>Merlons around the top rim of a round tower.</summary>
    public void RoundBattlements(Vector3 topCentre, float radius, int count, float size, Cell cell)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (i + 0.5f) / count * Mathf.PI * 2f;
            var p = topCentre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Box(p, new Vector3(size, size * 1.1f, size), cell);
        }
    }

    /// <summary>Merlons along a straight wall top running along X.</summary>
    public void WallBattlements(Vector3 topCentre, float length, float thickness, float size, Cell cell)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(length / (size * 2f)));
        for (int i = 0; i < count; i++)
        {
            float x = -length * 0.5f + (i + 0.5f) * length / count;
            Box(topCentre + new Vector3(x, 0f, 0f), new Vector3(size, size * 1.1f, thickness), cell);
        }
    }

    /// <summary>A pole with a small flag in the nation colour.</summary>
    public void Flag(Vector3 bottom, float height, float size)
    {
        Box(bottom, new Vector3(size * 0.12f, height, size * 0.12f), Cell.Dark);
        Box(bottom + new Vector3(size * 0.5f, height - size * 0.7f, 0f), new Vector3(size, size * 0.65f, size * 0.06f), Cell.Nation);
    }

    public enum TreeKind { Pine, Broadleaf, Poplar }

    /// <summary>A small tree: a trunk and low-poly foliage (stacked cones, leafy blobs, or a tall
    /// slender crown), standing on y, `height` tall overall, foliage `radius` wide.</summary>
    public void Tree(Vector3 bottom, float height, float radius, TreeKind kind, int seed, Cell foliage = Cell.Foliage)
    {
        float trunkHeight = height * (kind == TreeKind.Pine ? 0.25f : 0.38f);
        Cylinder(bottom, radius * 0.16f, trunkHeight, 5, Cell.Wood);
        switch (kind)
        {
            case TreeKind.Pine:
                for (int i = 0; i < 3; i++)
                {
                    float y = trunkHeight * 0.7f + (height - trunkHeight) * i * 0.27f;
                    float r = radius * (1f - i * 0.25f);
                    Cone(bottom + Vector3.up * y, r, (height - y) * 0.75f + radius * 0.2f, 7, foliage, Jitter(seed, i) * 0.1f);
                }
                break;
            case TreeKind.Poplar:
                Dome(bottom + Vector3.up * trunkHeight, radius * 0.6f, height - trunkHeight, 7, 3, foliage);
                Dome(bottom + Vector3.up * (trunkHeight + 0.02f * height), radius * 0.6f, -(height * 0.12f), 7, 2, foliage);
                break;
            default:
                // Three leafy blobs: one on top, two to the sides.
                for (int i = 0; i < 3; i++)
                {
                    float a = (Jitter(seed, i) + i / 3f) * Mathf.PI * 2f;
                    Vector3 c = bottom + Vector3.up * (trunkHeight + (i == 0 ? radius * 0.35f : 0f))
                        + (i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.45f);
                    float r = radius * (i == 0 ? 0.75f : 0.6f);
                    Dome(c, r, r * 0.9f, 7, 2, foliage);
                    Dome(c, r, -r * 0.5f, 7, 1, foliage);
                }
                break;
        }
    }

    /// <summary>A rough boulder, crag or cliff: a lumpy low-poly mound, `radius` wide, `height` tall.</summary>
    public void Rock(Vector3 bottom, float radius, float height, int seed, Cell cell = Cell.Stone)
    {
        const int sides = 8, rings = 3;
        Vector3 Point(int ring, int i)
        {
            float t = ring / (float)rings;
            float a = (i % sides) / (float)sides * Mathf.PI * 2f;
            float r = radius * Mathf.Cos(t * Mathf.PI * 0.5f) * (0.75f + 0.5f * Jitter(seed, (i % sides) * 7 + ring));
            float y = height * Mathf.Sin(t * Mathf.PI * 0.5f) * (ring == 0 ? 0f : 0.8f + 0.4f * Jitter(seed + 5, (i % sides) + ring * 11));
            return bottom + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }
        Vector3 top = bottom + Vector3.up * height;
        for (int ring = 0; ring < rings; ring++)
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = Point(ring, i), b = Point(ring, i + 1);
                Vector3 outward = (a + b) * 0.5f - bottom;
                if (ring == rings - 1) { Tri(a, b, top, outward + Vector3.up * height, cell); continue; }
                Vector3 c = Point(ring + 1, i + 1), d = Point(ring + 1, i);
                Quad(a, b, c, d, outward, cell);
            }
    }

    private static float Jitter(int seed, int i)
    {
        unchecked
        {
            int h = seed * 73856093 ^ i * 19349663;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }
    }

    private static Vector3 Ring(Vector3 centre, float radius, int i, int sides, float twist = 0f)
    {
        float a = (i / (float)sides + twist) * Mathf.PI * 2f;
        return centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
    }
}
