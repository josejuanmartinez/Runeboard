using System.Collections.Generic;
using UnityEngine;

// The miniature's base under every live 3D unit (see Character3DModel.SetBase): a short bevelled
// disc, so a unit reads as a painted figure standing on the board rather than a model pasted onto
// the map. A grey flocked top inside a wide sloped rim in the owner's nation colour — readable from
// above, where the unit chips under a figure never cover it — over a nation-coloured side band.
// Drawn with the models' own shader (its _MINIATURE_BASE variant, which also lights the top
// face up while the unit is selected or hovered), so it takes the same key light, cast
// shadow, hover tint and fade.
//
// The mesh is unit radius in the model root's frame (+Y = the board frame's up, top face at the
// feet, y = 0). It is drawn at one flat depth just behind its model's slab, so the figure always
// stands in front of it; within the base, triangles are ordered far → near with the top last, and
// painter's order does the rest.
public static class Character3DBase
{
    private const int Segments = 40;
    private const float TopRadius = 0.76f;  // where the nation-coloured rim starts
    private const float BevelDrop = 0.045f;
    public const float Thickness = 0.16f;   // of the radius

    // Palette texture: x 0..15 = flocked top, 16..23 = rim (nation colour, lightened), 24..31 = nation colour.
    private const int TexWidth = 32, TexHeight = 16;
    private const float BevelU = 20f / TexWidth, SideU = 28f / TexWidth;

    private static readonly Color TopColor = new(0.30f, 0.30f, 0.29f);
    private static readonly Color Unowned = new(0.36f, 0.33f, 0.28f);

    private static Mesh mesh;
    private static readonly Dictionary<Color32, Material> materials = new();

    public static Mesh Mesh => mesh != null ? mesh : mesh = BuildMesh();

    public static Material MaterialFor(Color nation, Shader shader)
    {
        Color32 key = nation.a > 0f ? (Color32)nation : (Color32)Unowned;
        if (materials.TryGetValue(key, out Material material) && material != null) return material;
        material = new Material(shader) { name = $"Miniature base {key}", mainTexture = BuildTexture(key) };
        material.EnableKeyword("_MINIATURE_BASE");
        materials[key] = material;
        return material;
    }

    private static Texture2D BuildTexture(Color side)
    {
        var tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "MiniatureBase",
        };
        var px = new Color32[TexWidth * TexHeight];
        var random = new System.Random(7);
        for (int y = 0; y < TexHeight; y++)
            for (int x = 0; x < TexWidth; x++)
            {
                Color c = x < 16 ? TopColor * (0.9f + 0.2f * (float)random.NextDouble())
                    : x < 24 ? Color.Lerp(side, Color.white, 0.12f) // the rim, a touch lighter than the band
                    : side;
                c.a = 1f;
                px[y * TexWidth + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    private static Mesh BuildMesh()
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        // Rings far → near (+Z is away from the viewer), so nearer quads draw over farther ones.
        var order = new List<int>();
        for (int i = 0; i < Segments; i++) order.Add(i);
        order.Sort((a, b) => MidZ(b).CompareTo(MidZ(a)));

        Vector3 bevelNormal = new Vector3(BevelDrop, 1f - TopRadius, 0f).normalized;
        foreach (int i in order)
            AddBand(i, 1f, -BevelDrop, 1f, -Thickness, Vector2.right, SideU, vertices, normals, uvs, triangles);
        foreach (int i in order)
            AddBand(i, TopRadius, 0f, 1f, -BevelDrop, new Vector2(bevelNormal.x, bevelNormal.y), BevelU, vertices, normals, uvs, triangles);

        // Top face last: a fan, with planar UVs into the flock strip.
        int centre = vertices.Count;
        vertices.Add(Vector3.zero);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.25f, 0.5f));
        for (int i = 0; i <= Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            var p = new Vector3(Mathf.Cos(a) * TopRadius, 0f, Mathf.Sin(a) * TopRadius);
            vertices.Add(p);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.25f + p.x * 0.2f, 0.5f + p.z * 0.45f));
            if (i > 0) triangles.AddRange(new[] { centre, centre + i + 1, centre + i });
        }

        var built = new Mesh { name = "MiniatureBase" };
        built.SetVertices(vertices);
        built.SetNormals(normals);
        built.SetUVs(0, uvs);
        built.SetTriangles(triangles, 0);
        built.RecalculateBounds();
        return built;
    }

    private static float MidZ(int segment) => Mathf.Sin((segment + 0.5f) * Mathf.PI * 2f / Segments);

    // One quad of a ring band between (r0, y0) and (r1, y1); the normal is (radial, up) in profile.
    private static void AddBand(int segment, float r0, float y0, float r1, float y1, Vector2 profileNormal, float u,
        List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
    {
        int start = vertices.Count;
        for (int k = 0; k < 2; k++)
        {
            float a = (segment + k) * Mathf.PI * 2f / Segments;
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 normal = (radial * profileNormal.x + Vector3.up * profileNormal.y).normalized;
            vertices.Add(radial * r0 + Vector3.up * y0);
            vertices.Add(radial * r1 + Vector3.up * y1);
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(new Vector2(u, 0.5f));
            uvs.Add(new Vector2(u, 0.5f));
        }
        triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
    }
}
