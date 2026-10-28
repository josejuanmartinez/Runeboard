using System;
using System.Collections.Generic;
using UnityEngine;
using Cell = CityMeshBuilder.Cell;

// Hand-made 3D settlements, one per PC painting (Assets/Art/Hexes/PCs): each painting has a
// recipe, Resources/Cities/<key>.json (key = the art name in lower case, letters and digits only,
// e.g. "Helm Deep" → helmdeep.json), describing the same place in the procedural toolkit's parts —
// its buildings, towers, walls, trees and rocks, where they stand, how big, and in what colours and
// surfaces. City3D builds a settlement from its recipe when there is one (CityLayout falls back to
// the generic generator when there is not).
//
// Units are hex widths. A building stands at `at` = [x, y]: a screen offset from the hex centre
// (x right, y up the screen = farther back). Its parts are placed in the building's own frame:
// pos = [x, y, z] with y up (height) and z back (away from the viewer; on screen a depth z shows
// foreshortened, about 0.42 z up the screen). See Resources/Cities/README.md for the full format.
public static class CityRecipes
{
    [Serializable]
    public class Recipe
    {
        public string name;          // the painting's name, as in Assets/Art/Hexes/PCs
        public string description;   // what the painting shows (kept for whoever edits the recipe)
        public string style;         // CityStyles.Style: Mannish, Elven, Dwarven, Hobbit, Orcish, Southron, Northman
        public string[] colors;      // per cell, "#RRGGBB" or "" to keep the style's
        public string[] surfaces;    // per cell, a CityTextures.Kind name or "" to keep the style's
        public string ground;        // "water": the town's base is water (Lake-town, island holds); default land
        public Building[] buildings;
    }

    [Serializable]
    public class Building
    {
        public string note;          // what it is in the painting
        public float[] at;           // [x, y] screen offset from the hex centre
        public float yaw;            // degrees
        public Part[] parts;
    }

    [Serializable]
    public class Part
    {
        public string shape;         // box, gable, pyramid, cone, cylinder, dome, battlements, wallBattlements, flag, tree, rock, peak, reeds, wall
        public float[] pos;          // [x, y, z] in the building's frame (y up, z back)
        public float[] size;         // box: [width, height, depth]; gable: [width, rise, depth]
        public float radius;
        public float height;
        public int sides;            // polygon sides for cone/cylinder/dome/peak (0 = a sensible default for the size)
        public float taper;          // cylinder: top radius / bottom radius (0 = straight); peak: shoulder
                                     // fullness (0 = 0.35, a near cone; ~1.2 keeps a rock's bulk)
        public float twist;          // turns the polygon (0.125 squares a 4-sided one to the view)
        public float yaw;            // extra degrees for this part, about its own pos
        public string cell;          // wall, roof, wood, stone, trim, dark, accent, nation, foliage
        public string top;           // box/cylinder: the cap's cell (default: same as cell); peak: the snow cell
                                     // (none = bare rock); reeds: the bulrush heads' cell (none = plain blades)
        public float snow;           // peak: snowline as a fraction of the height (0 = 0.6)
        public int count;            // battlements: merlons around the ring; reeds: blades in the clump (0 = 7)
        public float length;         // wallBattlements: run length; peak: ridge length along x (stretches the footprint)
        public float thickness;      // wallBattlements / wall
        public string kind;          // tree: pine, broadleaf, poplar, dead (a bare snag; its cell defaults to wood)
        public int seed;             // tree / rock / peak / reeds variation
        public float[] to;           // wall: end point [x, z] in the building's frame; starts at pos
    }

    private static readonly Dictionary<string, Recipe> loaded = new();

    public static string Key(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    public static Recipe Find(params string[] names)
    {
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            // Sprites cut from a sheet carry a "_0" suffix.
            string trimmed = System.Text.RegularExpressions.Regex.Replace(name, @"_\d+$", "");
            foreach (string candidate in new[] { trimmed, name })
            {
                string key = Key(candidate);
                if (key.Length == 0) continue;
                if (loaded.TryGetValue(key, out Recipe cached)) { if (cached != null) return cached; continue; }
                TextAsset asset = Resources.Load<TextAsset>("Cities/" + key);
                Recipe recipe = null;
                if (asset != null)
                {
                    try { recipe = JsonUtility.FromJson<Recipe>(asset.text); }
                    catch (Exception e) { Debug.LogWarning($"[City3D] Bad recipe Cities/{key}.json: {e.Message}"); }
                }
                loaded[key] = recipe;
                if (recipe != null) return recipe;
            }
        }
        return null;
    }

    /// <summary>The plans, colours and surfaces of a recipe, at hex width S.</summary>
    public static CityLayout.Result Build(Recipe recipe, float s)
    {
        var result = new CityLayout.Result();
        result.Style = Enum.TryParse(recipe.style, true, out CityStyles.Style style) ? style : CityStyles.Style.Mannish;

        var overrides = new Color[CityMeshBuilder.CellCount];
        var surfaces = new CityTextures.Kind?[CityMeshBuilder.CellCount];
        for (int i = 0; i < CityMeshBuilder.CellCount; i++)
        {
            if (recipe.colors != null && i < recipe.colors.Length && ColorUtility.TryParseHtmlString(recipe.colors[i], out Color c)) overrides[i] = c;
            if (recipe.surfaces != null && i < recipe.surfaces.Length && Enum.TryParse(recipe.surfaces[i], true, out CityTextures.Kind kind)) surfaces[i] = kind;
        }
        result.Palette = CityStyles.Palette(result.Style, overrides);
        result.Surfaces = surfaces;
        result.WaterGround = string.Equals(recipe.ground, "water", StringComparison.OrdinalIgnoreCase);
        bool water = result.WaterGround;
        // A blue accent is the recipe's water: its flat pools and streams take the painted water.
        Color accent = result.Palette[(int)Cell.Accent];
        bool accentWater = accent.b > accent.r + 0.05f && accent.b >= accent.g - 0.03f;

        if (recipe.buildings == null) return result;
        int seedBase = Key(recipe.name).GetHashCode();
        for (int bi = 0; bi < recipe.buildings.Length; bi++)
        {
            Building building = recipe.buildings[bi];
            if (building?.parts == null || building.parts.Length == 0) continue;
            Vector2 at = building.at != null && building.at.Length >= 2 ? new Vector2(building.at[0], building.at[1]) : Vector2.zero;
            int seed = seedBase + bi * 101;
            result.Plans.Add(new CityLayout.Plan
            {
                Screen = at * s,
                Build = b => { foreach (Part part in building.parts) Draw(b, part, building.yaw, s, seed, water, accentWater); },
            });
        }
        return result;
    }

    // A flat footprint in a ground colour (water, lava, a path, a lawn) — in hex widths. Thin
    // upright details (windows and doors, a few thousandths deep) never qualify.
    private static bool IsGround(float height, float x, float z, Cell cell) =>
        height <= 0.015f && Mathf.Min(x, z) >= 0.012f && Mathf.Max(x, z) >= 0.03f
        && cell is Cell.Accent or Cell.Trim or Cell.Stone or Cell.Foliage;

    private static void Draw(CityMeshBuilder b, Part p, float buildingYaw, float s, int seed, bool waterGround, bool accentWater)
    {
        // On a water base the water is the base itself: the recipe's own pools and seas go.
        if (waterGround && p.cell != null && p.cell.Equals("accent", StringComparison.OrdinalIgnoreCase)
            && (p.shape == "box" || p.shape == "wall") && (p.size == null || p.size.Length < 2 || p.size[1] <= 0.015f) && p.height <= 0.015f) return;
        Vector3 pos = V3(p.pos) * s;
        Quaternion around = Quaternion.Euler(0f, buildingYaw, 0f);
        b.At(around * pos, buildingYaw + p.yaw);
        Cell cell = ParseCell(p.cell, Cell.Wall);
        Cell top = ParseCell(p.top, cell);
        float radius = p.radius * s, height = p.height * s;
        // Unspecified sides: more for big round shapes (hills, great domes and cones), which show their facets.
        int sides = p.sides > 0 ? p.sides : p.radius >= 0.15f ? 12 : 8;
        switch ((p.shape ?? "").ToLowerInvariant())
        {
            case "box":
            {
                // A flat footprint of water, path or lava is ground, not a slab: draw it as a
                // shaped patch (piers and decks, in wood, stay boxes).
                Vector3 size = V3(p.size);
                if (IsGround(size.y, size.x, size.z, cell))
                    b.Patch(Vector3.zero, size.x * s, size.z * s, size.y * s, seed + p.seed + Mathf.RoundToInt(pos.x * 997f + pos.z * 1499f),
                        accentWater && cell == Cell.Accent ? Cell.Water : cell);
                else
                    b.Box(Vector3.zero, size * s, cell, top);
                break;
            }
            case "gable":
            {
                Vector3 size = V3(p.size) * s;
                b.GableRoof(Vector3.zero, size.x, size.z, size.y, cell, top);
                break;
            }
            case "pyramid":
                b.Cone(Vector3.zero, radius, height, 4, cell, p.twist != 0f ? p.twist : 0.125f);
                break;
            case "cone":
                // Older recipes drew crags and mountains as plain stone cones: give them a peak's ridges.
                if ((cell == Cell.Stone && p.height >= 0.1f) || (p.radius >= 0.2f && cell is Cell.Stone or Cell.Trim))
                    b.Peak(Vector3.zero, radius, height, 11, seed + p.seed + Mathf.RoundToInt(pos.x * 997f), Cell.Stone,
                        cell == Cell.Trim ? Cell.Trim : null, 0.5f, 1f, 0.35f);
                else
                    b.Cone(Vector3.zero, radius, height, sides, cell, p.twist);
                break;
            case "cylinder":
                b.Cylinder(Vector3.zero, radius, height, sides, cell, top, p.taper > 0f ? p.taper : 1f, p.twist);
                break;
            case "dome":
                b.Dome(Vector3.zero, radius, height, Mathf.Max(sides, 6), p.radius >= 0.12f ? 4 : 3, cell);
                break;
            case "battlements":
                b.RoundBattlements(Vector3.zero, radius, Mathf.Max(p.count, 4), (p.thickness > 0f ? p.thickness : 0.014f) * s, cell);
                break;
            case "wallbattlements":
                b.WallBattlements(Vector3.zero, p.length * s, (p.thickness > 0f ? p.thickness : 0.02f) * s, 0.014f * s, cell);
                break;
            case "flag":
                b.Flag(Vector3.zero, height > 0f ? height : 0.07f * s, radius > 0f ? radius : 0.035f * s);
                break;
            case "tree":
            {
                CityMeshBuilder.TreeKind kind = Enum.TryParse(p.kind, true, out CityMeshBuilder.TreeKind k) ? k : CityMeshBuilder.TreeKind.Broadleaf;
                Cell leaves = ParseCell(p.cell, kind == CityMeshBuilder.TreeKind.Dead ? Cell.Wood : Cell.Foliage);
                b.Tree(Vector3.zero, height > 0f ? height : 0.12f * s, radius > 0f ? radius : 0.04f * s, kind, seed + p.seed, leaves);
                break;
            }
            case "rock":
                b.Rock(Vector3.zero, radius, height, seed + p.seed, ParseCell(p.cell, Cell.Stone));
                break;
            case "peak":
            {
                Cell? snow = string.IsNullOrEmpty(p.top) ? null : top;
                float stretch = p.radius > 0f ? Mathf.Max(1f, p.length / (2f * p.radius)) : 1f;
                // Tall peaks stand as a massif (shoulders and boulders at the foot), not a lone cone.
                if (p.height >= 0.25f)
                    b.Massif(Vector3.zero, radius, height, p.sides > 0 ? p.sides : 11, seed + p.seed, ParseCell(p.cell, Cell.Stone),
                        snow, p.snow > 0f ? p.snow : 0.6f, stretch, p.taper > 0f ? p.taper : 0.35f);
                else
                    b.Peak(Vector3.zero, radius, height, p.sides > 0 ? p.sides : 11, seed + p.seed, ParseCell(p.cell, Cell.Stone),
                        snow, p.snow > 0f ? p.snow : 0.6f, stretch, p.taper > 0f ? p.taper : 0.35f);
                break;
            }
            case "reeds":
            {
                Cell? head = string.IsNullOrEmpty(p.top) ? null : top;
                b.Reeds(Vector3.zero, radius > 0f ? radius : 0.03f * s, height > 0f ? height : 0.16f * s, p.count > 0 ? p.count : 7,
                    seed + p.seed, ParseCell(p.cell, Cell.Foliage), head);
                break;
            }
            case "wall":
            {
                // A straight wall from pos to `to` (both on the ground, [x, z]).
                Vector3 from = new(pos.x, pos.y, pos.z);
                Vector3 end = p.to != null && p.to.Length >= 2 ? new Vector3(p.to[0] * s, pos.y, p.to[1] * s) : from;
                Vector3 d = end - from;
                if (d.sqrMagnitude < 1e-8f) break;
                float yaw = buildingYaw - Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
                b.At(around * ((from + end) * 0.5f), yaw);
                float thick = (p.thickness > 0f ? p.thickness : 0.025f) * s;
                // A wall at ground level in a ground colour is a crack or a stream drawn along a line.
                if (IsGround(p.height, d.magnitude / s, thick / s, cell))
                {
                    b.Patch(Vector3.zero, d.magnitude + thick, thick, height, seed + p.seed + Mathf.RoundToInt(pos.x * 997f + pos.z * 1499f),
                        accentWater && cell == Cell.Accent ? Cell.Water : cell);
                    break;
                }
                b.Box(Vector3.zero, new Vector3(d.magnitude + thick, height, thick), cell, top);
                if (p.count > 0) b.WallBattlements(Vector3.up * height, d.magnitude, thick, 0.014f * s, cell);
                break;
            }
            default:
                Debug.LogWarning($"[City3D] Unknown recipe shape '{p.shape}'.");
                break;
        }
    }

    private static Vector3 V3(float[] v) => v == null ? Vector3.zero
        : new Vector3(v.Length > 0 ? v[0] : 0f, v.Length > 1 ? v[1] : 0f, v.Length > 2 ? v[2] : 0f);

    private static Cell ParseCell(string name, Cell fallback) =>
        !string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out Cell cell) ? cell : fallback;
}
