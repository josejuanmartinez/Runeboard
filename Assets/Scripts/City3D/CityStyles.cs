using System.Collections.Generic;
using UnityEngine;
using Kind = CityTextures.Kind;

// The look of a procedural settlement by its owner's people (see CityLayout): which buildings it
// is made of, and for each of CityMeshBuilder's eight surface cells (wall, roof, wood, stone,
// trim, dark, accent, nation) a weathered colour and a surface (CityTextures) — half-timbered
// limewash and clay tiles for the towns of men, pale dressed stone and green-bronze slate for
// the elves, dark fieldstone for dwarves, turf over cream walls for hobbits, tarred planks and
// rusted roofs for orcs, sandstone for the south, thatch and logs for the northmen. Drawn with the
// units' own shader, so towns take the same key light and cast shadows as the miniatures.
public static class CityStyles
{
    public enum Style { Mannish, Elven, Dwarven, Hobbit, Orcish, Southron, Northman }

    public static Style For(RacesEnum race) => race switch
    {
        RacesEnum.Elf => Style.Elven,
        RacesEnum.Dwarf => Style.Dwarven,
        RacesEnum.Hobbit => Style.Hobbit,
        RacesEnum.Orc or RacesEnum.Goblin or RacesEnum.Troll or RacesEnum.Nazgul or RacesEnum.Undead
            or RacesEnum.Balrog or RacesEnum.Spider or RacesEnum.Dragon => Style.Orcish,
        RacesEnum.Southron or RacesEnum.Easterling => Style.Southron,
        RacesEnum.Beorning or RacesEnum.Wildman or RacesEnum.Ent or RacesEnum.Beast => Style.Northman,
        _ => Style.Mannish,
    };

    private sealed class Spec
    {
        public Kind[] Kinds;
        public Color[] Colours;
    }

    private static readonly Dictionary<Style, Spec> Specs = new()
    {
        //                              wall              roof           wood          stone            trim          dark          accent
        { Style.Mannish, new Spec {
            Kinds = new[] { Kind.HalfTimber, Kind.Tiles, Kind.Planks, Kind.Ashlar, Kind.Ashlar, Kind.Planks, Kind.Plain },
            Colours = new[] { C(0.76f, 0.70f, 0.58f), C(0.50f, 0.30f, 0.22f), C(0.36f, 0.26f, 0.17f), C(0.58f, 0.55f, 0.49f), C(0.68f, 0.64f, 0.56f), C(0.17f, 0.12f, 0.09f), C(0.60f, 0.48f, 0.27f) } } },
        { Style.Elven, new Spec {
            Kinds = new[] { Kind.Ashlar, Kind.Slate, Kind.Planks, Kind.Ashlar, Kind.Plain, Kind.Planks, Kind.Plain },
            Colours = new[] { C(0.74f, 0.70f, 0.60f), C(0.30f, 0.38f, 0.31f), C(0.46f, 0.35f, 0.23f), C(0.66f, 0.63f, 0.55f), C(0.72f, 0.60f, 0.38f), C(0.15f, 0.12f, 0.10f), C(0.66f, 0.54f, 0.30f) } } },
        { Style.Dwarven, new Spec {
            Kinds = new[] { Kind.Fieldstone, Kind.Slate, Kind.Planks, Kind.Ashlar, Kind.Plain, Kind.Planks, Kind.Plain },
            Colours = new[] { C(0.44f, 0.41f, 0.37f), C(0.27f, 0.27f, 0.29f), C(0.32f, 0.22f, 0.14f), C(0.40f, 0.38f, 0.35f), C(0.60f, 0.46f, 0.24f), C(0.08f, 0.07f, 0.06f), C(0.78f, 0.44f, 0.15f) } } },
        { Style.Hobbit, new Spec {
            Kinds = new[] { Kind.Plaster, Kind.Turf, Kind.Planks, Kind.Fieldstone, Kind.Plaster, Kind.Planks, Kind.Planks },
            Colours = new[] { C(0.74f, 0.66f, 0.50f), C(0.34f, 0.44f, 0.22f), C(0.42f, 0.28f, 0.17f), C(0.55f, 0.52f, 0.45f), C(0.70f, 0.62f, 0.45f), C(0.15f, 0.10f, 0.07f), C(0.26f, 0.38f, 0.24f) } } },
        { Style.Orcish, new Spec {
            Kinds = new[] { Kind.Planks, Kind.Slate, Kind.Planks, Kind.Fieldstone, Kind.Plain, Kind.Planks, Kind.Plain },
            Colours = new[] { C(0.27f, 0.21f, 0.16f), C(0.33f, 0.20f, 0.14f), C(0.23f, 0.16f, 0.11f), C(0.26f, 0.24f, 0.23f), C(0.40f, 0.13f, 0.08f), C(0.05f, 0.04f, 0.04f), C(0.90f, 0.42f, 0.12f) } } },
        { Style.Southron, new Spec {
            Kinds = new[] { Kind.Plaster, Kind.Tiles, Kind.Planks, Kind.Ashlar, Kind.Plaster, Kind.Planks, Kind.Plain },
            Colours = new[] { C(0.76f, 0.63f, 0.46f), C(0.58f, 0.34f, 0.20f), C(0.42f, 0.28f, 0.17f), C(0.68f, 0.56f, 0.40f), C(0.80f, 0.72f, 0.56f), C(0.14f, 0.10f, 0.08f), C(0.27f, 0.42f, 0.46f) } } },
        { Style.Northman, new Spec {
            Kinds = new[] { Kind.Planks, Kind.Thatch, Kind.Planks, Kind.Fieldstone, Kind.Plain, Kind.Planks, Kind.Plain },
            Colours = new[] { C(0.44f, 0.31f, 0.19f), C(0.58f, 0.48f, 0.29f), C(0.37f, 0.25f, 0.15f), C(0.49f, 0.47f, 0.43f), C(0.66f, 0.53f, 0.28f), C(0.12f, 0.09f, 0.07f), C(0.55f, 0.22f, 0.15f) } } },
    };

    // Foliage for trees (CityMeshBuilder.Tree), by people: darker woods for elves and orcs.
    private static readonly Dictionary<Style, Color> Foliage = new()
    {
        { Style.Mannish, C(0.30f, 0.38f, 0.19f) }, { Style.Elven, C(0.27f, 0.36f, 0.20f) },
        { Style.Dwarven, C(0.24f, 0.31f, 0.20f) }, { Style.Hobbit, C(0.34f, 0.43f, 0.19f) },
        { Style.Orcish, C(0.22f, 0.24f, 0.16f) }, { Style.Southron, C(0.36f, 0.38f, 0.20f) },
        { Style.Northman, C(0.26f, 0.34f, 0.19f) },
    };

    // A style's colours, one per CityMeshBuilder.Cell (the Nation entry is a placeholder: the
    // owner's colour fills it). Overrides (landmarks, recipes) replace cells by index; alpha 0
    // keeps the style's own.
    public static Color[] Palette(Style style, Color[] overrides = null)
    {
        var palette = new Color[CityMeshBuilder.CellCount];
        Specs[style].Colours.CopyTo(palette, 0);
        palette[(int)CityMeshBuilder.Cell.Foliage] = Foliage[style];
        if (overrides != null)
            for (int i = 0; i < overrides.Length && i < palette.Length; i++)
                if (overrides[i].a > 0f) palette[i] = overrides[i];
        return palette;
    }

    private static readonly Dictionary<Texture2D, Material> materials = new();

    /// <summary>One material per surface cell (CityMeshBuilder.Cell order): the style's surfaces, cloth in the nation colour, leaves.</summary>
    public static Material[] MaterialsFor(Style style, Color[] palette, Color nation, Shader shader, Kind?[] surfaces = null)
    {
        var kinds = new Kind[Specs[style].Kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
            kinds[i] = surfaces != null && i < surfaces.Length && surfaces[i].HasValue ? surfaces[i].Value : Specs[style].Kinds[i];
        var result = new Material[CityMeshBuilder.CellCount];
        for (int i = 0; i < result.Length; i++)
        {
            Texture2D tex = i < kinds.Length ? CityTextures.Get(kinds[i], Grade(palette[i]))
                : i == (int)CityMeshBuilder.Cell.Foliage ? CityTextures.Get(Kind.Leaves, Grade(palette[i]))
                : CityTextures.Get(Kind.Cloth, nation.a > 0f ? nation * 0.85f : new Color(0.45f, 0.42f, 0.36f));
            if (!materials.TryGetValue(tex, out Material material) || material == null)
            {
                material = new Material(shader) { name = $"City {tex.name}", mainTexture = tex };
                materials[tex] = material;
            }
            result[i] = material;
        }
        return result;
    }

    // ── Matching the painted map ────────────────────────────────────────────────────────────
    // Settlement colours were picked from the paintings' lit faces: brighter and paler than the
    // terrain, and with their own greens. Each one is pulled toward the terrain tiles' own palette
    // (Resources/TerrainPalette.json: k-means clusters of every tile's pixels, in Lab) — a weighted
    // blend of its three nearest terrain colours — so a town's greens are the grass's greens, its
    // stone the hills' stone, its slate the water's blue-grey. BoardLookSettings.cityPaletteMatch
    // sets how far (1 = fully onto the terrain's colours); cityBrightness and citySaturation then
    // fine-tune. Flags (the nation colour) are left as they are.

    [System.Serializable] private class TerrainPaletteFile { public TerrainColor[] colors; }
    [System.Serializable] private class TerrainColor { public float r, g, b, weight; }

    private static Color[] terrainColors;
    private static Vector3[] terrainLab;

    private static bool LoadTerrainPalette()
    {
        if (terrainColors != null) return terrainColors.Length > 0;
        terrainColors = new Color[0];
        TextAsset asset = Resources.Load<TextAsset>("TerrainPalette");
        if (asset == null) return false;
        TerrainPaletteFile file = JsonUtility.FromJson<TerrainPaletteFile>(asset.text);
        if (file?.colors == null) return false;
        terrainColors = new Color[file.colors.Length];
        terrainLab = new Vector3[file.colors.Length];
        for (int i = 0; i < file.colors.Length; i++)
        {
            terrainColors[i] = new Color(file.colors[i].r, file.colors[i].g, file.colors[i].b, 1f);
            terrainLab[i] = Lab(terrainColors[i]);
        }
        return terrainColors.Length > 0;
    }

    private static Color Grade(Color c)
    {
        BoardLookSettings look = BoardLookSettings.Instance;
        if (look.cityPaletteMatch > 0f && LoadTerrainPalette())
        {
            // The three nearest terrain colours, weighted by inverse squared distance.
            Vector3 lab = Lab(c);
            int[] best = { -1, -1, -1 };
            float[] bestD = { float.MaxValue, float.MaxValue, float.MaxValue };
            for (int i = 0; i < terrainLab.Length; i++)
            {
                float d = (terrainLab[i] - lab).sqrMagnitude;
                for (int k = 0; k < 3; k++)
                {
                    if (d >= bestD[k]) continue;
                    for (int m = 2; m > k; m--) { bestD[m] = bestD[m - 1]; best[m] = best[m - 1]; }
                    bestD[k] = d; best[k] = i;
                    break;
                }
            }
            Color target = Color.clear;
            float weights = 0f;
            for (int k = 0; k < 3; k++)
            {
                if (best[k] < 0) continue;
                float w = 1f / (bestD[k] + 1f);
                target += terrainColors[best[k]] * w;
                weights += w;
            }
            if (weights > 0f) c = Color.Lerp(c, target / weights, look.cityPaletteMatch);
        }

        float luma = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        var graded = new Color(luma + (c.r - luma) * look.citySaturation, luma + (c.g - luma) * look.citySaturation, luma + (c.b - luma) * look.citySaturation) * look.cityBrightness;
        return new Color(Mathf.Clamp01(graded.r), Mathf.Clamp01(graded.g), Mathf.Clamp01(graded.b), 1f);
    }

    // CIE Lab (D65) from a gamma-space colour, for perceptual colour distance.
    private static Vector3 Lab(Color c)
    {
        static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
        float r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
        float x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f;
        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        float z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;
        return new Vector3(116f * F(y) - 16f, 500f * (F(x) - F(y)), 200f * (F(y) - F(z)));
    }

    private static Color C(float r, float g, float b) => new(r, g, b, 1f);
}
