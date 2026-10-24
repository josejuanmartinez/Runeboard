using System.Collections.Generic;
using UnityEngine;

// Low-poly building blocks for the procedural settlements (see CityLayout): boxes, gable roofs,
// cylinders, cones, domes, battlements, trees, rocks, mountain peaks and reeds, flat-shaded (every face has its own vertices) so the
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

    public enum TreeKind { Pine, Broadleaf, Poplar, Dead }

    /// <summary>A small tree: a trunk and low-poly foliage (stacked cones, leafy blobs, or a tall
    /// slender crown), standing on y, `height` tall overall, foliage `radius` wide. A dead tree is a
    /// bare snag: a tapering trunk with a few crooked bare branches (in `foliage`, e.g. wood).</summary>
    public void Tree(Vector3 bottom, float height, float radius, TreeKind kind, int seed, Cell foliage = Cell.Foliage)
    {
        if (kind == TreeKind.Dead)
        {
            float trunk = Mathf.Max(radius * 0.22f, height * 0.045f);
            Vector3 lean = new Vector3(Jitter(seed, 1) - 0.5f, 0f, Jitter(seed, 2) - 0.5f) * height * 0.18f;
            Spike(bottom, bottom + lean + Vector3.up * height, trunk, 5, foliage, Jitter(seed, 3));
            int branches = 2 + Mathf.FloorToInt(Jitter(seed, 4) * 1.99f);
            for (int i = 0; i < branches; i++)
            {
                float t = 0.4f + 0.35f * (i + Jitter(seed, 10 + i)) / branches;
                float a = (i / (float)branches + Jitter(seed, 20 + i) * 0.3f) * Mathf.PI * 2f;
                Vector3 foot = bottom + lean * t + Vector3.up * (height * t);
                Vector3 tip = foot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * (0.7f + 0.5f * Jitter(seed, 30 + i))
                    + Vector3.up * height * (0.18f + 0.15f * Jitter(seed, 40 + i));
                Spike(foot, tip, trunk * 0.55f * (1f - t * 0.5f), 4, foliage, Jitter(seed, 50 + i));
            }
            return;
        }
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
        const int sides = 11, rings = 4;
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

    /// <summary>A mountain peak: a many-sided cone in `rings` bands whose vertices are pushed out along
    /// a few seeded ridges (spurs) and jittered, so the flanks read as faceted rock rather than a smooth
    /// cone; the summit leans a little off centre. `stretch` (≥ 1) draws the footprint out along X into
    /// a ridge, and `fullness` swells the shoulders (0 = a near cone; about 1.2 keeps the bulk of a
    /// rock of the same size, for mountains with buildings on their slopes). Faces above `snowline`
    /// (a fraction of the height; ragged, and lower along the ridges) take the `snow` cell; pass no
    /// snow cell for bare rock.</summary>
    public void Peak(Vector3 bottom, float radius, float height, int sides, int seed, Cell cell,
        Cell? snow = null, float snowline = 0.6f, float stretch = 1f, float fullness = 0.35f)
    {
        const int rings = 5;
        sides = Mathf.Max(sides, 6);
        // Three to five ridges at seeded bearings; a vertex near one is pushed out (and kept higher).
        int ridgeCount = 3 + Mathf.FloorToInt(Jitter(seed, 900) * 2.99f);
        var ridges = new float[ridgeCount];
        for (int k = 0; k < ridgeCount; k++) ridges[k] = (k + 0.35f * Jitter(seed, 910 + k)) / ridgeCount * Mathf.PI * 2f;
        float Ridge(float a)
        {
            float best = 0f;
            foreach (float r in ridges) best = Mathf.Max(best, Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a - r)), 6f));
            return best;
        }
        Vector3 apex = bottom + new Vector3((Jitter(seed, 920) - 0.5f) * 0.16f * radius * stretch, height,
            (Jitter(seed, 921) - 0.5f) * 0.16f * radius);
        var points = new Vector3[rings, sides];
        var ridgeness = new float[rings, sides];
        for (int ring = 0; ring < rings; ring++)
        {
            float t = ring / (float)rings;
            // A cone with fuller shoulders, so the base carries some mass.
            float profile = Mathf.Pow(1f - t, 1.1f) * (1f + fullness * t);
            for (int i = 0; i < sides; i++)
            {
                float a = (i + (Jitter(seed, i * 13 + ring) - 0.5f) * 0.35f) / sides * Mathf.PI * 2f;
                float ridge = Ridge(a);
                float r = radius * profile * (0.72f + 0.42f * ridge + 0.16f * Jitter(seed + 3, i * 7 + ring));
                float y = ring == 0 ? 0f : height * t * (0.9f + 0.2f * Jitter(seed + 7, i + ring * 17) + 0.08f * ridge);
                // Draw the band toward the summit's lean as it rises.
                Vector3 lean = (apex - bottom - Vector3.up * height) * t;
                points[ring, i] = bottom + lean + new Vector3(Mathf.Cos(a) * r * stretch, y, Mathf.Sin(a) * r);
                ridgeness[ring, i] = ridge;
            }
        }
        Cell Face(float y, float ridge, int i, int ring)
        {
            if (!snow.HasValue) return cell;
            float line = snowline - 0.3f * ridge + (Jitter(seed + 11, i * 5 + ring) - 0.5f) * 0.14f;
            return (y - bottom.y) / height > line ? snow.Value : cell;
        }
        for (int ring = 0; ring < rings; ring++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Vector3 a = points[ring, i], b = points[ring, j];
                Vector3 outward = (a + b) * 0.5f - bottom; outward.y = 0f;
                float ridge = (ridgeness[ring, i] + ridgeness[ring, j]) * 0.5f;
                if (ring == rings - 1)
                {
                    Tri(a, b, apex, outward + Vector3.up * radius, Face((a.y + b.y + apex.y) / 3f, ridge, i, ring));
                    continue;
                }
                Vector3 c = points[ring + 1, j], d = points[ring + 1, i];
                // Split each band quad into two separately coloured triangles so the snow edge is ragged.
                Tri(a, b, c, outward, Face((a.y + b.y + c.y) / 3f, ridge, i, ring));
                Tri(a, c, d, outward, Face((a.y + c.y + d.y) / 3f, ridge, i + 101, ring));
            }
    }

    /// <summary>A clump of reeds or rushes: `count` thin blades leaning out from a spot `radius` wide,
    /// up to `height` tall. With a `head` cell, the taller blades carry a bulrush head near the tip.</summary>
    public void Reeds(Vector3 bottom, float radius, float height, int count, int seed, Cell cell, Cell? head = null)
    {
        float blade = Mathf.Max(radius * 0.2f, height * 0.045f);
        for (int i = 0; i < count; i++)
        {
            float a = (i / (float)count + Jitter(seed, i) * 0.4f) * Mathf.PI * 2f;
            float d = radius * (i == 0 ? 0f : 0.35f + 0.65f * Jitter(seed, i + 40));
            Vector3 foot = bottom + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            float h = height * (0.6f + 0.4f * Jitter(seed, i + 80));
            // Lean outward from the clump's centre, a little more the farther out the blade stands.
            float lean = h * (0.12f + 0.25f * Jitter(seed, i + 120));
            Vector3 tip = foot + new Vector3(Mathf.Cos(a) * lean, h, Mathf.Sin(a) * lean);
            Spike(foot, tip, blade * (0.8f + 0.4f * Jitter(seed, i + 160)), 3, cell, Jitter(seed, i + 200));
            if (head.HasValue && h > height * 0.75f)
            {
                Vector3 h0 = Vector3.Lerp(foot, tip, 0.62f), h1 = Vector3.Lerp(foot, tip, 0.86f);
                Spindle(h0, h1, blade * 1.6f, 4, head.Value);
            }
        }
    }

    // A tapering spike from `foot` to `tip` (any direction), `sides`-sided, `radius` at the foot.
    private void Spike(Vector3 foot, Vector3 tip, float radius, int sides, Cell cell, float turn = 0f)
    {
        Frame(tip - foot, out Vector3 u, out Vector3 w);
        for (int i = 0; i < sides; i++)
        {
            float a0 = (i / (float)sides + turn) * Mathf.PI * 2f, a1 = ((i + 1) / (float)sides + turn) * Mathf.PI * 2f;
            Vector3 a = foot + (u * Mathf.Cos(a0) + w * Mathf.Sin(a0)) * radius;
            Vector3 b = foot + (u * Mathf.Cos(a1) + w * Mathf.Sin(a1)) * radius;
            Tri(a, b, tip, (a + b) * 0.5f - foot, cell);
        }
    }

    // A closed spindle (two cones base to base) from p0 to p1, `radius` at the middle.
    private void Spindle(Vector3 p0, Vector3 p1, float radius, int sides, Cell cell)
    {
        Frame(p1 - p0, out Vector3 u, out Vector3 w);
        Vector3 mid = (p0 + p1) * 0.5f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
            Vector3 a = mid + (u * Mathf.Cos(a0) + w * Mathf.Sin(a0)) * radius;
            Vector3 b = mid + (u * Mathf.Cos(a1) + w * Mathf.Sin(a1)) * radius;
            Tri(a, b, p1, (a + b) * 0.5f - mid, cell);
            Tri(a, b, p0, (a + b) * 0.5f - mid, cell);
        }
    }

    // Two unit vectors square to `axis` (and to each other).
    private static void Frame(Vector3 axis, out Vector3 u, out Vector3 w)
    {
        axis.Normalize();
        u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        w = Vector3.Cross(axis, u).normalized;
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
