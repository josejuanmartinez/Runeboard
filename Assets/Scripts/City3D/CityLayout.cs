using System.Collections.Generic;
using UnityEngine;
using Cell = CityMeshBuilder.Cell;

// Plans a settlement's buildings from its game data (see City3D): how many houses by its size
// (camp → city), walls, towers and a keep by its fortification (tower → citadel), a spire for a
// capital, all in its owner's style (CityStyles) — plus hand-shaped landmarks for a few famous
// places (Orthanc, Barad-dûr, Minas Tirith, Minas Morgul, Edoras, Dol Guldur). The layout is
// seeded by the settlement's name, so a town always looks the same.
//
// Positions are screen offsets from the hex centre (world units); the middle of the hex is kept
// clear for the units that stand there, and only low buildings go in front of it so they never
// hide a miniature. Each plan is one building (its own mesh and depth slab, so units sort
// against each building separately). Sizes are in units of the hex width.
public static class CityLayout
{
    public struct Plan
    {
        public Vector2 Screen;                       // offset from the hex centre, world units
        public System.Action<CityMeshBuilder> Build; // draws the building around its own origin
    }

    public sealed class Result
    {
        public readonly List<Plan> Plans = new();
        public Color[] Palette;
        public CityStyles.Style Style;
        public CityTextures.Kind?[] Surfaces; // per cell, null = the style's own
        public bool WaterGround;              // the base is water (CityRecipes "ground": "water")
    }

    // Footprint, as an ellipse in screen space (fractions of the hex width), and the clear middle.
    private const float FootX = 0.37f, FootY = 0.25f;
    private const float ClearX = 0.16f, ClearY = 0.11f;

    private static readonly Color Keep = new(0, 0, 0, 0); // palette override: leave the style colour

    public static Result Make(PC pc, float hexWidth, string artName = null) =>
        Make(pc.pcName, pc.owner != null ? pc.owner.race : RacesEnum.Common, pc.citySize, pc.fortSize, pc.isCapital, hexWidth, artName);

    // The same plan from plain values, for a settlement that is not on the board (TitleDiorama).
    public static Result Make(string pcName, RacesEnum race, PCSizeEnum citySize, FortSizeEnum fortSize, bool isCapital, float hexWidth, string artName = null)
    {
        // A hand-made recipe for this painting, when there is one (CityRecipes).
        CityRecipes.Recipe recipe = CityRecipes.Find(artName, pcName);
        if (recipe != null) return CityRecipes.Build(recipe, hexWidth);

        var result = new Result();
        var rng = new System.Random(Seed(pcName));
        CityStyles.Style style = CityStyles.For(race);
        var ctx = new Context(result, rng, style, hexWidth);

        string key = Normalize(pcName);
        Color[] overrides = null;
        bool landmark = true;
        switch (key)
        {
            case "orthanc": overrides = Orthanc(ctx); break;
            case "baraddur": overrides = BaradDur(ctx); break;
            case "minastirith": overrides = MinasTirith(ctx); break;
            case "minasmorgul": overrides = MinasMorgul(ctx); break;
            case "edoras": overrides = Edoras(ctx); break;
            case "dolguldur": overrides = DolGuldur(ctx); break;
            default: landmark = false; break;
        }
        if (landmark)
        {
            result.Palette = CityStyles.Palette(ctx.Style, overrides);
            result.Style = ctx.Style;
            return result;
        }

        result.Palette = CityStyles.Palette(style);
        result.Style = style;
        Fortifications(ctx, fortSize);
        if (isCapital) ctx.Add(new Vector2(0.06f, 0.2f), 0.06f, b => Spire(b, ctx));
        int houses = citySize switch
        {
            PCSizeEnum.camp => 4,
            PCSizeEnum.village => 5,
            PCSizeEnum.town => 8,
            PCSizeEnum.majorTown => 11,
            _ => 15,
        };
        bool camp = citySize == PCSizeEnum.camp;
        for (int i = 0; i < houses; i++)
        {
            if (!ctx.TryPlace(0.075f, out Vector2 at)) break;
            float yaw = (float)(rng.NextDouble() * 30.0 - 15.0);
            int variant = rng.Next(4);
            ctx.Add(at, 0f, b => { b.At(Vector3.zero, yaw); if (camp) Tent(b, ctx); else House(b, ctx, variant); });
        }
        return result;
    }

    // ── Placement ───────────────────────────────────────────────────────────────────────────

    private sealed class Context
    {
        public readonly Result Result;
        public readonly System.Random Rng;
        public CityStyles.Style Style;
        public readonly float S; // hex width: every size below is a fraction of it
        private readonly List<(Vector2 at, float radius)> taken = new();

        public Context(Result result, System.Random rng, CityStyles.Style style, float s)
        {
            Result = result; Rng = rng; Style = style; S = s;
        }

        // at and radius in hex widths; reserves the spot so houses keep clear of it.
        public void Add(Vector2 at, float radius, System.Action<CityMeshBuilder> build)
        {
            taken.Add((at, radius));
            Result.Plans.Add(new Plan { Screen = at * S, Build = build });
        }

        // A free spot inside the footprint, outside the clear middle, for a building of this radius.
        public bool TryPlace(float radius, out Vector2 at)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                double angle = Rng.NextDouble() * System.Math.PI * 2.0;
                double r = 0.45 + Rng.NextDouble() * 0.5;
                at = new Vector2((float)(System.Math.Cos(angle) * r) * FootX, (float)(System.Math.Sin(angle) * r) * FootY);
                Vector2 c = new(at.x / ClearX, at.y / ClearY);
                if (c.sqrMagnitude < 1f) continue;
                bool free = true;
                foreach ((Vector2 p, float pr) in taken)
                {
                    // Overlap on screen: vertical distances count double (the board is foreshortened).
                    Vector2 d = at - p;
                    d.y *= 2f;
                    if (d.magnitude < radius + pr) { free = false; break; }
                }
                if (!free) continue;
                taken.Add((at, radius));
                return true;
            }
            at = default;
            return false;
        }
    }

    // A point on the footprint ellipse: angle in degrees (90 = back, 270 = front), r normalized.
    private static Vector2 OnFoot(float angle, float r)
    {
        float a = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(a) * FootX * r, Mathf.Sin(a) * FootY * r);
    }

    // ── Fortifications ──────────────────────────────────────────────────────────────────────

    private static void Fortifications(Context ctx, FortSizeEnum fort)
    {
        switch (fort)
        {
            case FortSizeEnum.tower:
                ctx.Add(OnFoot(140f, 0.8f), 0.06f, b => Tower(b, ctx, 1f));
                break;
            case FortSizeEnum.fort:
                Walls(ctx, 0.75f);
                ctx.Add(OnFoot(145f, 0.97f), 0.05f, b => Tower(b, ctx, 0.9f));
                ctx.Add(OnFoot(35f, 0.97f), 0.05f, b => Tower(b, ctx, 0.9f));
                break;
            case FortSizeEnum.keep:
                Walls(ctx, 0.9f);
                ctx.Add(OnFoot(90f, 0.6f), 0.1f, b => KeepTower(b, ctx, 1f));
                ctx.Add(OnFoot(150f, 0.97f), 0.05f, b => Tower(b, ctx, 1f));
                ctx.Add(OnFoot(30f, 0.97f), 0.05f, b => Tower(b, ctx, 1f));
                break;
            case FortSizeEnum.fortress:
                Walls(ctx, 1f);
                ctx.Add(OnFoot(90f, 0.6f), 0.11f, b => KeepTower(b, ctx, 1.15f));
                foreach (float a in new[] { 30f, 150f, 210f, 330f })
                    ctx.Add(OnFoot(a, 0.97f), 0.05f, b => Tower(b, ctx, a > 180f ? 0.8f : 1.1f));
                break;
            case FortSizeEnum.citadel:
                Walls(ctx, 1.2f);
                ctx.Add(OnFoot(90f, 0.55f), 0.12f, b => KeepTower(b, ctx, 1.4f));
                foreach (float a in new[] { 20f, 65f, 115f, 160f, 210f, 330f })
                    ctx.Add(OnFoot(a, 0.97f), 0.05f, b => Tower(b, ctx, a > 180f ? 0.85f : 1.25f));
                break;
        }
    }

    // A ring of wall segments round the footprint with a gate gap at the front; front walls low.
    private static void Walls(Context ctx, float height, float radius = 0.97f)
    {
        const float step = 30f;
        for (float a0 = 0f; a0 < 360f; a0 += step)
        {
            float a1 = a0 + step;
            float mid = (a0 + a1) * 0.5f;
            if (mid > 255f && mid < 285f) continue; // the gate
            Vector2 p0 = OnFoot(a0, radius), p1 = OnFoot(a1, radius);
            Vector2 at = (p0 + p1) * 0.5f;
            bool front = at.y < 0f;
            float h = (front ? 0.04f : 0.065f) * height;
            ctx.Add(at, 0f, b => Wall(b, ctx, p0, p1, at, h));
        }
    }

    // ── Buildings (sizes in hex widths × S) ─────────────────────────────────────────────────

    private static float SinPitch => Mathf.Sin(Mathf.Max(5f, Character3DSettings.Instance != null ? Character3DSettings.Instance.pitch : 25f) * Mathf.Deg2Rad);

    // Screen offset → board-frame ground (the board is seen at an angle: depth is foreshortened).
    private static Vector3 Ground(Vector2 screen) => new(screen.x, 0f, screen.y / SinPitch);

    private static void Wall(CityMeshBuilder b, Context ctx, Vector2 p0, Vector2 p1, Vector2 at, float height)
    {
        float s = ctx.S;
        Vector3 g0 = Ground(p0 * s) - Ground(at * s), g1 = Ground(p1 * s) - Ground(at * s);
        Vector3 d = g1 - g0;
        float yaw = -Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        float length = d.magnitude + 0.02f * s;
        float thick = 0.025f * s;
        Cell cell = ctx.Style is CityStyles.Style.Northman or CityStyles.Style.Hobbit ? Cell.Wood : Cell.Stone;
        b.At(Vector3.zero, yaw);
        b.Box(Vector3.zero, new Vector3(length, height * s, thick), cell);
        if (cell == Cell.Stone) b.WallBattlements(new Vector3(0f, height * s, 0f), length, thick, 0.014f * s, cell);
        else for (float x = -length * 0.5f; x <= length * 0.5f; x += 0.022f * s)
                b.Cone(new Vector3(x, height * s, 0f), 0.009f * s, 0.018f * s, 4, Cell.Wood);
    }

    private static void Tower(CityMeshBuilder b, Context ctx, float scale)
    {
        float s = ctx.S * scale;
        switch (ctx.Style)
        {
            case CityStyles.Style.Elven:
                b.Cylinder(Vector3.zero, 0.032f * s, 0.34f * s, 10, Cell.Wall, null, 0.85f);
                b.Cone(new Vector3(0f, 0.34f * s, 0f), 0.04f * s, 0.16f * s, 10, Cell.Roof);
                b.Flag(new Vector3(0f, 0.5f * s, 0f), 0.06f * s, 0.035f * s);
                break;
            case CityStyles.Style.Dwarven:
                b.Cylinder(Vector3.zero, 0.055f * s, 0.18f * s, 4, Cell.Stone, Cell.Stone, 0.9f, 0.125f);
                b.RoundBattlements(new Vector3(0f, 0.18f * s, 0f), 0.04f * s, 6, 0.016f * s, Cell.Stone);
                b.Box(new Vector3(0f, 0.08f * s, -0.04f * s), new Vector3(0.02f * s, 0.03f * s, 0.01f * s), Cell.Accent);
                break;
            case CityStyles.Style.Orcish:
                b.Cylinder(Vector3.zero, 0.05f * s, 0.3f * s, 4, Cell.Stone, Cell.Dark, 0.65f, 0.125f);
                for (int i = 0; i < 4; i++)
                {
                    float a = (i / 4f + 0.125f) * Mathf.PI * 2f;
                    b.Cone(new Vector3(Mathf.Cos(a) * 0.03f * s, 0.3f * s, Mathf.Sin(a) * 0.03f * s), 0.009f * s, 0.07f * s, 4, Cell.Dark);
                }
                b.Box(new Vector3(0f, 0.2f * s, -0.03f * s), new Vector3(0.018f * s, 0.022f * s, 0.01f * s), Cell.Accent);
                break;
            case CityStyles.Style.Southron:
                b.Cylinder(Vector3.zero, 0.042f * s, 0.26f * s, 4, Cell.Stone, Cell.Trim, 1f, 0.125f);
                b.Dome(new Vector3(0f, 0.26f * s, 0f), 0.034f * s, 0.04f * s, 8, 3, Cell.Roof);
                break;
            case CityStyles.Style.Northman:
                b.Cylinder(Vector3.zero, 0.04f * s, 0.22f * s, 4, Cell.Wood, Cell.Wood, 0.9f, 0.125f);
                b.Cone(new Vector3(0f, 0.22f * s, 0f), 0.05f * s, 0.07f * s, 4, Cell.Roof, 0.125f);
                b.Flag(new Vector3(0f, 0.29f * s, 0f), 0.05f * s, 0.03f * s);
                break;
            default:
                b.Cylinder(Vector3.zero, 0.042f * s, 0.26f * s, 10, Cell.Stone, Cell.Stone);
                b.RoundBattlements(new Vector3(0f, 0.26f * s, 0f), 0.036f * s, 7, 0.014f * s, Cell.Stone);
                b.Box(new Vector3(0f, 0.15f * s, -0.04f * s), new Vector3(0.012f * s, 0.025f * s, 0.008f * s), Cell.Dark);
                b.Flag(new Vector3(0f, 0.27f * s, 0f), 0.07f * s, 0.035f * s);
                break;
        }
    }

    private static void KeepTower(CityMeshBuilder b, Context ctx, float scale)
    {
        float s = ctx.S * scale;
        float w = 0.15f * s, d = 0.11f * s, h = 0.2f * s;
        Cell body = ctx.Style is CityStyles.Style.Elven or CityStyles.Style.Southron ? Cell.Wall
            : ctx.Style == CityStyles.Style.Northman ? Cell.Wood : Cell.Stone;
        b.Box(Vector3.zero, new Vector3(w, h, d), body);
        // Corner turrets.
        foreach (Vector2 c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
        {
            var at = new Vector3(c.x * w * 0.5f, 0f, c.y * d * 0.5f);
            b.Cylinder(at, 0.022f * s, h * 1.18f, 8, body, body);
            if (ctx.Style is CityStyles.Style.Elven or CityStyles.Style.Mannish)
                b.Cone(at + Vector3.up * h * 1.18f, 0.027f * s, 0.06f * s, 8, Cell.Roof);
        }
        // Roof: battlements, or a gable for the halls of men and northmen.
        if (ctx.Style is CityStyles.Style.Northman or CityStyles.Style.Mannish)
            b.GableRoof(new Vector3(0f, h, 0f), w, d, 0.08f * s, Cell.Roof, body);
        else
            b.WallBattlements(new Vector3(0f, h, -d * 0.5f), w, 0.012f * s, 0.016f * s, body);
        // Door and windows on the front.
        b.Box(new Vector3(0f, 0f, -d * 0.5f - 0.002f), new Vector3(0.03f * s, 0.05f * s, 0.006f * s), Cell.Dark);
        b.Box(new Vector3(-w * 0.25f, h * 0.6f, -d * 0.5f - 0.002f), new Vector3(0.014f * s, 0.022f * s, 0.006f * s), Cell.Dark);
        b.Box(new Vector3(w * 0.25f, h * 0.6f, -d * 0.5f - 0.002f), new Vector3(0.014f * s, 0.022f * s, 0.006f * s), Cell.Dark);
        b.Flag(new Vector3(0f, h + (ctx.Style is CityStyles.Style.Northman or CityStyles.Style.Mannish ? 0.08f * s : 0f), 0f), 0.08f * s, 0.04f * s);
    }

    private static void Spire(CityMeshBuilder b, Context ctx)
    {
        float s = ctx.S;
        Cell body = ctx.Style == CityStyles.Style.Orcish ? Cell.Stone : Cell.Trim;
        b.Cylinder(Vector3.zero, 0.03f * s, 0.42f * s, 8, body, null, 0.7f);
        b.Cone(new Vector3(0f, 0.42f * s, 0f), 0.025f * s, 0.14f * s, 8, ctx.Style == CityStyles.Style.Orcish ? Cell.Dark : Cell.Roof);
        b.Flag(new Vector3(0f, 0.56f * s, 0f), 0.05f * s, 0.035f * s);
    }

    private static void Tent(CityMeshBuilder b, Context ctx)
    {
        float s = ctx.S;
        Cell cloth = ctx.Style == CityStyles.Style.Orcish ? Cell.Wood : Cell.Trim;
        b.Cone(Vector3.zero, 0.045f * s, 0.075f * s, 6, cloth);
        b.Box(new Vector3(0f, 0f, -0.035f * s), new Vector3(0.016f * s, 0.03f * s, 0.006f * s), Cell.Dark);
    }

    private static void House(CityMeshBuilder b, Context ctx, int variant)
    {
        float s = ctx.S;
        switch (ctx.Style)
        {
            case CityStyles.Style.Hobbit:
            {
                float r = (0.05f + variant * 0.005f) * s;
                b.Dome(Vector3.zero, r, r * 0.8f, 10, 3, Cell.Roof);
                b.Cylinder(new Vector3(0f, 0f, -r * 0.92f), 0.016f * s, 0.032f * s, 8, variant % 2 == 0 ? Cell.Accent : Cell.Nation, Cell.Wood);
                b.Box(new Vector3(-r * 0.55f, 0.012f * s, -r * 0.75f), new Vector3(0.012f * s, 0.012f * s, 0.006f * s), Cell.Trim);
                if (variant > 1) b.Cylinder(new Vector3(r * 0.4f, r * 0.5f, r * 0.2f), 0.006f * s, 0.035f * s, 6, Cell.Stone, Cell.Dark);
                break;
            }
            case CityStyles.Style.Southron:
            {
                float w = 0.085f * s, d = 0.075f * s, h = (0.06f + variant * 0.008f) * s;
                b.Box(Vector3.zero, new Vector3(w, h, d), Cell.Wall, Cell.Trim);
                b.WallBattlements(new Vector3(0f, h, -d * 0.5f), w, 0.006f * s, 0.008f * s, Cell.Wall);
                if (variant == 3) b.Dome(new Vector3(0f, h, 0f), 0.03f * s, 0.035f * s, 8, 3, Cell.Roof);
                Openings(b, s, w, d, h);
                break;
            }
            case CityStyles.Style.Dwarven:
            {
                float w = 0.1f * s, d = 0.085f * s, h = 0.055f * s;
                b.Box(Vector3.zero, new Vector3(w, h, d), Cell.Wall, Cell.Roof);
                b.Box(new Vector3(0f, h, 0f), new Vector3(w * 0.7f, 0.012f * s, d * 0.7f), Cell.Roof);
                b.Box(new Vector3(w * 0.3f, h, d * 0.2f), new Vector3(0.012f * s, 0.035f * s, 0.012f * s), Cell.Dark);
                Openings(b, s, w, d, h);
                break;
            }
            case CityStyles.Style.Orcish:
            {
                float w = 0.08f * s, d = 0.07f * s, h = 0.05f * s;
                b.Box(Vector3.zero, new Vector3(w, h, d), Cell.Wood);
                b.Cone(new Vector3(0f, h, 0f), w * 0.72f, 0.05f * s, 4, Cell.Roof, 0.125f);
                b.Cone(new Vector3(0f, h + 0.045f * s, 0f), 0.006f * s, 0.035f * s, 4, Cell.Dark);
                b.Box(new Vector3(0f, 0f, -d * 0.5f - 0.002f), new Vector3(0.016f * s, 0.028f * s, 0.005f * s), Cell.Dark);
                break;
            }
            case CityStyles.Style.Northman:
            {
                float w = 0.13f * s, d = 0.065f * s, h = 0.045f * s;
                b.Box(Vector3.zero, new Vector3(w, h, d), Cell.Wood);
                b.GableRoof(new Vector3(0f, h, 0f), w, d, 0.055f * s, Cell.Roof, Cell.Wood);
                b.Box(new Vector3(0f, 0f, -d * 0.5f - 0.002f), new Vector3(0.018f * s, 0.032f * s, 0.005f * s), Cell.Dark);
                break;
            }
            case CityStyles.Style.Elven:
            {
                float w = 0.075f * s, d = 0.065f * s, h = 0.08f * s;
                b.Box(Vector3.zero, new Vector3(w, h, d), Cell.Wall);
                b.GableRoof(new Vector3(0f, h, 0f), w, d, 0.07f * s, Cell.Roof, Cell.Wall);
                if (variant == 0) b.Cylinder(new Vector3(w * 0.5f, 0f, 0f), 0.014f * s, 0.15f * s, 8, Cell.Wall);
                if (variant == 0) b.Cone(new Vector3(w * 0.5f, 0.15f * s, 0f), 0.018f * s, 0.06f * s, 8, Cell.Roof);
                Openings(b, s, w, d, h);
                break;
            }
            default:
            {
                float w = (0.09f + variant * 0.01f) * s, d = 0.07f * s, h = (0.06f + (variant % 2) * 0.015f) * s;
                b.Box(Vector3.zero, new Vector3(w, h, d), Cell.Wall);
                // Timber frame: a dark beam under the roof line.
                b.Box(new Vector3(0f, h - 0.006f * s, 0f), new Vector3(w + 0.002f, 0.006f * s, d + 0.002f), Cell.Wood);
                b.GableRoof(new Vector3(0f, h, 0f), w, d, 0.05f * s, Cell.Roof, Cell.Wall);
                if (variant == 2) b.Cylinder(new Vector3(-w * 0.3f, h, d * 0.15f), 0.008f * s, 0.05f * s, 6, Cell.Stone, Cell.Dark);
                Openings(b, s, w, d, h);
                break;
            }
        }
    }

    // A door and a window or two on the front face.
    private static void Openings(CityMeshBuilder b, float s, float w, float d, float h)
    {
        float z = -d * 0.5f - 0.002f;
        b.Box(new Vector3(-w * 0.18f, 0f, z), new Vector3(0.016f * s, 0.03f * s, 0.005f * s), Cell.Dark);
        if (h > 0.05f * s) b.Box(new Vector3(w * 0.22f, h * 0.45f, z), new Vector3(0.013f * s, 0.015f * s, 0.005f * s), Cell.Dark);
    }

    // ── Landmarks ───────────────────────────────────────────────────────────────────────────

    private static Color[] Orthanc(Context ctx)
    {
        ctx.Style = CityStyles.Style.Mannish;
        float s = ctx.S;
        ctx.Add(new Vector2(0f, 0.1f), 0.12f, b =>
        {
            // Four fused black piers rising to four horns.
            for (int i = 0; i < 4; i++)
            {
                float a = (i / 4f + 0.125f) * Mathf.PI * 2f;
                var at = new Vector3(Mathf.Cos(a) * 0.025f * s, 0f, Mathf.Sin(a) * 0.025f * s);
                b.Cylinder(at, 0.035f * s, 0.62f * s, 6, Cell.Stone, Cell.Dark, 0.75f);
                b.Cone(at * 1.4f + Vector3.up * 0.62f * s, 0.014f * s, 0.09f * s, 4, Cell.Stone);
            }
            b.Box(new Vector3(0f, 0f, -0.05f * s), new Vector3(0.025f * s, 0.05f * s, 0.01f * s), Cell.Dark);
        });
        Walls(ctx, 0.6f); // the Ring of Isengard
        return new[] { Keep, Keep, Keep, new Color(0.13f, 0.13f, 0.15f), Keep, new Color(0.03f, 0.03f, 0.04f) };
    }

    private static Color[] BaradDur(Context ctx)
    {
        ctx.Style = CityStyles.Style.Orcish;
        float s = ctx.S;
        ctx.Add(new Vector2(0f, 0.12f), 0.14f, b =>
        {
            b.Cylinder(Vector3.zero, 0.13f * s, 0.2f * s, 6, Cell.Stone, Cell.Dark, 0.8f);
            b.Cylinder(new Vector3(0f, 0.2f * s, 0f), 0.09f * s, 0.22f * s, 6, Cell.Stone, Cell.Dark, 0.75f);
            b.Cylinder(new Vector3(0f, 0.42f * s, 0f), 0.06f * s, 0.24f * s, 6, Cell.Stone, Cell.Dark, 0.7f);
            // The two horns and the Eye between them.
            b.Cone(new Vector3(-0.035f * s, 0.66f * s, 0f), 0.016f * s, 0.12f * s, 4, Cell.Dark);
            b.Cone(new Vector3(0.035f * s, 0.66f * s, 0f), 0.016f * s, 0.12f * s, 4, Cell.Dark);
            b.Box(new Vector3(0f, 0.7f * s, -0.004f * s), new Vector3(0.03f * s, 0.035f * s, 0.012f * s), Cell.Accent);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                b.Cone(new Vector3(Mathf.Cos(a) * 0.1f * s, 0.2f * s, Mathf.Sin(a) * 0.1f * s), 0.012f * s, 0.07f * s, 4, Cell.Dark);
            }
        });
        Walls(ctx, 1.1f);
        return new[] { Keep, Keep, Keep, new Color(0.16f, 0.15f, 0.15f), Keep, new Color(0.03f, 0.02f, 0.02f), new Color(1f, 0.55f, 0.12f) };
    }

    private static Color[] MinasTirith(Context ctx)
    {
        ctx.Style = CityStyles.Style.Mannish;
        float s = ctx.S;
        ctx.Add(new Vector2(0f, 0.06f), 0.3f, b =>
        {
            // Seven circles of the city, rising toward the Citadel and the White Tower.
            float[] radii = { 0.3f, 0.25f, 0.2f, 0.16f, 0.12f, 0.09f, 0.065f };
            float y = 0f;
            for (int i = 0; i < radii.Length; i++)
            {
                float h = 0.045f * s;
                b.Cylinder(new Vector3(0f, y, 0.02f * i * s), radii[i] * s, h, 16, Cell.Stone, Cell.Trim);
                b.RoundBattlements(new Vector3(0f, y + h, 0.02f * i * s), radii[i] * s * 0.97f, 12 + (6 - i) * 2, 0.012f * s, Cell.Stone);
                y += h;
            }
            var top = new Vector3(0f, y, 0.12f * s);
            b.Cylinder(top, 0.025f * s, 0.3f * s, 10, Cell.Trim, Cell.Trim, 0.85f);
            b.Cone(top + Vector3.up * 0.3f * s, 0.024f * s, 0.06f * s, 10, Cell.Trim);
            b.Flag(top + Vector3.up * 0.36f * s, 0.06f * s, 0.035f * s);
        });
        return new[] { Keep, new Color(0.42f, 0.44f, 0.46f), Keep, new Color(0.78f, 0.76f, 0.7f), new Color(0.84f, 0.82f, 0.76f) };
    }

    private static Color[] MinasMorgul(Context ctx)
    {
        ctx.Style = CityStyles.Style.Orcish;
        float s = ctx.S;
        ctx.Add(new Vector2(0f, 0.1f), 0.1f, b =>
        {
            b.Cylinder(Vector3.zero, 0.06f * s, 0.2f * s, 8, Cell.Stone, Cell.Stone, 0.85f);
            b.Cylinder(new Vector3(0f, 0.2f * s, 0f), 0.035f * s, 0.35f * s, 8, Cell.Trim, Cell.Trim, 0.8f);
            // The turning top, faintly lit.
            b.Cylinder(new Vector3(0f, 0.55f * s, 0f), 0.045f * s, 0.05f * s, 8, Cell.Accent, Cell.Accent, 1f, 0.06f);
            b.Cone(new Vector3(0f, 0.6f * s, 0f), 0.04f * s, 0.08f * s, 8, Cell.Stone);
        });
        Walls(ctx, 0.9f);
        return new[] { Keep, Keep, Keep, new Color(0.42f, 0.47f, 0.43f), new Color(0.56f, 0.62f, 0.56f), Keep, new Color(0.6f, 0.95f, 0.65f) };
    }

    private static Color[] Edoras(Context ctx)
    {
        ctx.Style = CityStyles.Style.Northman;
        float s = ctx.S;
        ctx.Add(new Vector2(0f, 0.05f), 0.25f, b =>
        {
            // The green hill with Meduseld, the golden hall, on its crown.
            b.Dome(Vector3.zero, 0.24f * s, 0.12f * s, 12, 3, Cell.Accent);
            var top = new Vector3(0f, 0.115f * s, 0.06f * s);
            b.Box(top, new Vector3(0.16f * s, 0.05f * s, 0.07f * s), Cell.Wood);
            b.GableRoof(top + Vector3.up * 0.05f * s, 0.16f * s, 0.07f * s, 0.06f * s, Cell.Roof, Cell.Wood);
            b.Flag(top + new Vector3(0.09f * s, 0f, 0f), 0.13f * s, 0.035f * s);
        });
        Walls(ctx, 0.7f);
        for (int i = 0; i < 4; i++)
        {
            if (!ctx.TryPlace(0.075f, out Vector2 at)) break;
            ctx.Add(at, 0f, b => House(b, ctx, 1));
        }
        return new[] { Keep, new Color(0.85f, 0.68f, 0.25f), Keep, Keep, Keep, Keep, new Color(0.42f, 0.52f, 0.28f) };
    }

    private static Color[] DolGuldur(Context ctx)
    {
        ctx.Style = CityStyles.Style.Orcish;
        float s = ctx.S;
        ctx.Add(new Vector2(0f, 0.08f), 0.2f, b =>
        {
            b.Dome(Vector3.zero, 0.2f * s, 0.08f * s, 10, 3, Cell.Wood);
            // Broken towers of different heights on the hill of sorcery.
            float[] heights = { 0.38f, 0.24f, 0.3f, 0.16f };
            for (int i = 0; i < heights.Length; i++)
            {
                float a = (i / 4f + 0.1f) * Mathf.PI * 2f;
                var at = new Vector3(Mathf.Cos(a) * 0.08f * s, 0.05f * s, Mathf.Sin(a) * 0.06f * s + 0.03f * s);
                b.Cylinder(at, 0.03f * s, heights[i] * s, 6, Cell.Stone, Cell.Dark, 0.8f);
                b.Cone(at + Vector3.up * heights[i] * s, 0.012f * s, 0.05f * s, 4, Cell.Dark, 0.1f);
            }
        });
        return new[] { Keep, Keep, new Color(0.2f, 0.22f, 0.18f), new Color(0.2f, 0.2f, 0.22f), Keep, Keep, Keep };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static int Seed(string name)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in name ?? "") h = h * 31 + c;
            return h;
        }
    }
}
