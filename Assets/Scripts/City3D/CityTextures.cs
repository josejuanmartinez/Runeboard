using System.Collections.Generic;
using UnityEngine;

// Tiling surface textures for the procedural settlements (see CityStyles), generated once per
// kind and colour: limewashed plaster, half-timbering, cut stone, fieldstone, clay roof tiles,
// thatch, slate, turf, wooden planks, cloth. Weathered and uneven on purpose — the board is a
// medieval world, and flat clean colours made the towns look newly built. Each is 64×64, tiles
// seamlessly (all noise is periodic), and is mapped by CityMeshBuilder's planar UVs at a fixed
// world scale, u along a face and v up it (up the slope, on a roof).
public static class CityTextures
{
    public enum Kind { Plaster, HalfTimber, Ashlar, Fieldstone, Tiles, Thatch, Slate, Turf, Planks, Plain, Cloth, Leaves }

    private const int Size = 64;
    private static readonly Dictionary<string, Texture2D> cache = new();
    private static readonly Color Timber = new(0.22f, 0.15f, 0.1f);

    public static Texture2D Get(Kind kind, Color colour)
    {
        string key = kind + ColorUtility.ToHtmlStringRGB(colour);
        if (cache.TryGetValue(key, out Texture2D tex) && tex != null) return tex;
        var px = new Color[Size * Size];
        int seed = (int)kind * 7919 + key.GetHashCode();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Color c = Sample(kind, colour, x, y, seed);
                c.a = 1f;
                px[y * Size + x] = c;
            }
        tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2, name = $"City {key}",
        };
        tex.SetPixels(px);
        tex.Apply(true, true);
        cache[key] = tex;
        return tex;
    }

    private static Color Sample(Kind kind, Color c, int x, int y, int seed)
    {
        float grain = Fbm(x, y, 8, seed) - 0.5f;   // fine, tileable mottling
        float blotch = Fbm(x, y, 2, seed + 3) - 0.5f; // large stains
        switch (kind)
        {
            case Kind.Plaster:
                // Limewash: mottled, with grime gathering low on the wall.
                return c * (1f + grain * 0.14f + blotch * 0.12f) * Mathf.Lerp(0.82f, 1f, y / (float)Size);

            case Kind.HalfTimber:
            {
                // Posts every half tile, a sill and a rail, and a brace in every other panel.
                int px = x % 32, py = y % 32;
                bool post = px < 4;
                bool rail = py < 3 || (py > 29);
                bool brace = (x / 32) % 2 == 0 && Mathf.Abs((px - 4) * (26f / 28f) - (py - 3)) < 2.2f && py > 2 && py < 30;
                if (post || rail || brace) return Timber * (1f + grain * 0.3f);
                return c * (1f + grain * 0.12f + blotch * 0.1f) * Mathf.Lerp(0.85f, 1f, py / 32f);
            }

            case Kind.Ashlar:
                return Blocks(c, x, y, 16, 32, 0.1f, grain, seed);

            case Kind.Fieldstone:
                return Blocks(c, x, y, 11, 16, 0.2f, grain, seed);

            case Kind.Tiles:
            {
                // Rows of overlapping clay tiles: a dark lip under each row, a gap between tiles,
                // each tile its own fired shade.
                int row = y / 8, py = y % 8;
                int tx = (x + (row % 2) * 4) % Size;
                int col = tx / 8, px = tx % 8;
                float shade = 0.82f + 0.3f * Hash(col & 7, row & 7, seed);
                float lip = py < 2 ? 0.6f : Mathf.Lerp(0.85f, 1.05f, py / 8f);
                float edge = px == 0 ? 0.7f : 1f;
                float round = 1f - 0.18f * Mathf.Abs(px - 4f) / 4f; // barrel-curved tiles
                return c * shade * lip * edge * round * (1f + grain * 0.1f);
            }

            case Kind.Thatch:
            case Kind.Turf:
            {
                // Straw: fine vertical streaks, laid in courses with a darker shadowed lip.
                float streak = Hash(x, 0, seed) * 0.5f + Fbm(x * 4, y, 4, seed + 9) * 0.5f;
                float course = Mathf.Lerp(0.7f, 1.05f, (y % 16) / 16f);
                Color tone = c * (0.75f + streak * 0.45f) * course;
                if (kind == Kind.Turf)
                    tone = Color.Lerp(tone, c * 1.35f, Mathf.Clamp01(grain * 3f)); // grassy flecks
                return tone;
            }

            case Kind.Slate:
            {
                int row = y / 8, py = y % 8;
                int sx = (x + (row % 2) * 8) % Size;
                int col = sx / 16, px = sx % 16;
                float h = Hash(col & 7, row, seed);
                Color tint = Color.Lerp(c, new Color(c.r * 0.9f, c.g * 0.98f, c.b * 1.12f), h); // blue-green variety
                float lip = py < 2 ? 0.55f : 1f;
                float gap = px == 0 ? 0.6f : 1f;
                return tint * (0.85f + h * 0.25f) * lip * gap * (1f + grain * 0.12f);
            }

            case Kind.Planks:
            {
                int plank = x / 8, px = x % 8;
                float tone = 0.8f + 0.35f * Hash(plank, 3, seed);
                float gap = px == 0 ? 0.45f : 1f;
                float wood = 0.92f + 0.12f * Mathf.Sin((y + Hash(plank, 7, seed) * 40f) * 0.5f + grain * 6f);
                return c * tone * gap * wood;
            }

            case Kind.Leaves:
            {
                // Clumps of leaves: light tops and dark hollows, speckled.
                float clump = Fbm(x, y, 4, seed + 17);
                float speck = Hash(x, y, seed) > 0.85f ? 1.15f : 1f;
                return c * Mathf.Lerp(0.6f, 1.2f, clump) * speck;
            }

            case Kind.Cloth:
                return c * (1f + ((x + y) % 2 == 0 ? 0.04f : -0.04f) + grain * 0.1f);

            default:
                return c * (1f + grain * 0.1f + blotch * 0.06f);
        }
    }

    // Courses of cut stones: mortar lines, each block its own shade, worn edges.
    private static Color Blocks(Color c, int x, int y, int rowHeight, int blockWidth, float variety, float grain, int seed)
    {
        int rows = Size / rowHeight;
        int row = Mathf.Min(y / rowHeight, rows - 1);
        int height = row == rows - 1 ? Size - row * rowHeight : rowHeight;
        int py = y - row * rowHeight;
        int shift = (row % 2) * (blockWidth / 2);
        int bx = (x + shift) % Size;
        int col = bx / blockWidth, px = bx % blockWidth;

        if (py == 0 || px == 0) return c * 0.55f; // mortar
        float shade = 1f - variety + 2f * variety * Hash(col, row, seed);
        float edge = (py == 1 || px == 1) ? 1.08f : (py == height - 1 || px == blockWidth - 1) ? 0.85f : 1f;
        return c * shade * edge * (1f + grain * 0.15f);
    }

    // ── Periodic noise (tiles over the 64-pixel texture) ────────────────────────────────────

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 2147483;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }
    }

    private static float ValueNoise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int Wrap(int v) => ((v % period) + period) % period;
        float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
        float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // Fractal noise with `cells` lattice cells across the texture at the lowest octave.
    private static float Fbm(int x, int y, int cells, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int octave = 0; octave < 3; octave++)
        {
            int period = cells << octave;
            float scale = period / (float)Size;
            sum += amp * ValueNoise(x * scale, y * scale, period, seed + octave * 31);
            norm += amp;
            amp *= 0.5f;
        }
        return sum / norm;
    }
}
