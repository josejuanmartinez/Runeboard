using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A soft border in the owner's true nation colour around the land each settlement you
/// have discovered commands: one hex around camps and villages, two around towns, major towns and cities.
/// A hex in reach of several settlements belongs to the nearest (the larger one on a tie), so
/// neighbouring realms meet along a shared line, each border drawn just inside its own land.
/// The outline follows the hex edges, just inside them, with every corner rounded off, drawn with
/// TerritoryBorder.shader so it lies on the ground behind every miniature and town. It is rebuilt
/// only when a settlement is discovered, grows, shrinks or changes hands.
/// </summary>
public class TerritoryBorders : MonoBehaviour
{
    private const float RefreshInterval = 1f;
    private const float Inset = 0.06f;       // of a hex width, from the edge into the land
    private const float CornerRadius = 0.12f; // of a hex width
    private const int CornerSteps = 6;        // points per rounded corner
    private const float Width = 0.022f;      // of a hex width
    private const float Opacity = 0.6f;
    private const int SortingOrder = 1;      // on the Hexes layer, over the terrain (depth keeps it under units)

    private static TerritoryBorders instance;
    private readonly List<LineRenderer> lines = new();
    private Material material;
    private string key;
    private float nextRefresh;

    public static void Ensure()
    {
        if (instance != null) return;
        instance = new GameObject("TerritoryBorders").AddComponent<TerritoryBorders>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        Board board = Board.Instance;
        if (board == null || board.hexes == null || Game.Instance == null || !Game.Instance.started) { Clear(); key = null; return; }
        var claims = Claim(board, out string wanted);
        if (wanted == key) return;
        key = wanted;
        Rebuild(board, claims);
    }

    private void OnDestroy()
    {
        Clear();
        if (material != null) Destroy(material);
        if (instance == this) instance = null;
    }

    // ---------------------------------------------------------------- land

    private static int Reach(PC pc) => pc.citySize <= PCSizeEnum.village ? 1 : 2;

    // Each hex in reach of a known, owned settlement, with the leader whose land it is.
    private static Dictionary<Vector2Int, Leader> Claim(Board board, out string signature)
    {
        var best = new Dictionary<Vector2Int, (Leader owner, int distance, int size)>();
        var sig = new System.Text.StringBuilder();
        foreach (Hex hex in board.hexes.Values)
        {
            // Only settlements drawn on your map (Hex.ShouldShowPcVisual): no border round an empty hex.
            PC pc = hex != null && hex.ShouldShowPcVisual() ? hex.GetPCData() : null;
            if (pc?.owner == null || pc.owner.killed) continue;
            sig.Append(hex.v2).Append(pc.citySize).Append(ColorUtility.ToHtmlStringRGB(pc.owner.nationColor)).Append(';');
            foreach ((Vector2Int at, int distance) in Around(board, hex.v2, Reach(pc)))
            {
                if (best.TryGetValue(at, out var held) && (held.distance < distance || (held.distance == distance && held.size >= (int)pc.citySize))) continue;
                best[at] = (pc.owner, distance, (int)pc.citySize);
            }
        }
        signature = sig.ToString();
        var claims = new Dictionary<Vector2Int, Leader>(best.Count);
        foreach (var pair in best) claims[pair.Key] = pair.Value.owner;
        return claims;
    }

    private static Vector2Int[] Neighbours(Board board, Vector2Int v) => (v.x & 1) == 0 ? board.evenRowNeighbors : board.oddRowNeighbors;

    // Hexes within `radius` steps (breadth first over the board's own neighbour rule).
    private static IEnumerable<(Vector2Int, int)> Around(Board board, Vector2Int centre, int radius)
    {
        var seen = new HashSet<Vector2Int> { centre };
        var frontier = new List<Vector2Int> { centre };
        yield return (centre, 0);
        for (int step = 1; step <= radius; step++)
        {
            var next = new List<Vector2Int>();
            foreach (Vector2Int v in frontier)
                foreach (Vector2Int d in Neighbours(board, v))
                {
                    Vector2Int n = v + d;
                    if (!board.hexes.ContainsKey(n) || !seen.Add(n)) continue;
                    next.Add(n);
                }
            foreach (Vector2Int n in next) yield return (n, step);
            frontier = next;
        }
    }

    // ---------------------------------------------------------------- outlines

    private void Rebuild(Board board, Dictionary<Vector2Int, Leader> claims)
    {
        Clear();
        if (claims.Count == 0 || !Offsets(board, out Vector3[][] offsets)) return;
        if (material == null)
        {
            Material source = Resources.Load<Material>("Materials/TerritoryBorder");
            material = source != null ? new Material(source) : new Material(Shader.Find("Sprites/Default"));
        }
        float hexWidth = Mathf.Max(0.01f, board.hexSize.x);
        float width = hexWidth * Width;

        // Every edge where a leader's land meets anyone else's (or none), counter-clockwise
        // around its own hex, keyed by its exact corners so edges chain into closed loops.
        var edgesByLeader = new Dictionary<Leader, Dictionary<Vector2Int, Edge>>();
        foreach (var pair in claims)
        {
            if (!board.hexes.TryGetValue(pair.Key, out Hex hex) || hex == null) continue;
            Vector3 c = hex.transform.position;
            Vector2Int[] dirs = Neighbours(board, pair.Key);
            Vector3[] off = offsets[pair.Key.x & 1];
            for (int i = 0; i < 6; i++)
            {
                Vector2Int n = pair.Key + dirs[i];
                if (claims.TryGetValue(n, out Leader other) && other == pair.Value) continue;
                Vector3 a = c + (off[(i + 5) % 6] + off[i]) / 3f;
                Vector3 b = c + (off[i] + off[(i + 1) % 6]) / 3f;
                if (!edgesByLeader.TryGetValue(pair.Value, out var edges)) edgesByLeader[pair.Value] = edges = new();
                edges[Snap(a, width)] = new Edge { end = Snap(b, width), from = a, to = b, centre = c };
            }
        }

        foreach (var pair in edgesByLeader)
        {
            Color colour = pair.Key.nationColor;
            colour.a = Opacity;
            Dictionary<Vector2Int, Edge> edges = pair.Value;
            while (edges.Count > 0)
            {
                var loop = new List<Edge>();
                Vector2Int start = default;
                foreach (Vector2Int k in edges.Keys) { start = k; break; }
                Vector2Int at = start;
                while (edges.TryGetValue(at, out Edge e))
                {
                    edges.Remove(at);
                    loop.Add(e);
                    at = e.end;
                }
                if (loop.Count >= 3) AddLine(Round(Inside(loop, hexWidth * Inset), hexWidth * CornerRadius), colour, width);
            }
        }
    }

    private struct Edge
    {
        public Vector2Int end;
        public Vector3 from, to, centre;
    }

    // The world offset to each neighbour, per row parity, taken from any hex that has all six:
    // the corners between two neighbours are then the centroid of the three centres, which
    // holds for however the grid is stretched.
    private static bool Offsets(Board board, out Vector3[][] offsets)
    {
        offsets = new Vector3[2][];
        foreach (var pair in board.hexes)
        {
            int parity = pair.Key.x & 1;
            if (offsets[parity] != null || pair.Value == null) continue;
            Vector2Int[] dirs = Neighbours(board, pair.Key);
            var off = new Vector3[6];
            bool complete = true;
            for (int i = 0; i < 6 && complete; i++)
            {
                if (board.hexes.TryGetValue(pair.Key + dirs[i], out Hex n) && n != null) off[i] = n.transform.position - pair.Value.transform.position;
                else complete = false;
            }
            if (complete) offsets[parity] = off;
            if (offsets[0] != null && offsets[1] != null) return true;
        }
        return false;
    }

    private static Vector2Int Snap(Vector3 p, float grain) => new(Mathf.RoundToInt(p.x / grain), Mathf.RoundToInt(p.y / grain));

    // The loop's corners with every edge moved `inset` toward its own hex's centre: each corner
    // is where two neighbouring moved edges cross.
    private static List<Vector3> Inside(List<Edge> loop, float inset)
    {
        var shifted = new (Vector2 p, Vector2 d)[loop.Count];
        for (int i = 0; i < loop.Count; i++)
        {
            Edge e = loop[i];
            Vector2 a = e.from, b = e.to, d = (b - a).normalized;
            Vector2 n = new(-d.y, d.x);
            if (Vector2.Dot((Vector2)e.centre - a, n) < 0f) n = -n;
            shifted[i] = (a + n * inset, d);
        }
        var corners = new List<Vector3>(loop.Count);
        float z = loop[0].from.z;
        for (int i = 0; i < loop.Count; i++)
        {
            var (p1, d1) = shifted[(i + loop.Count - 1) % loop.Count];
            var (p2, d2) = shifted[i];
            float cross = d1.x * d2.y - d1.y * d2.x;
            Vector2 corner = Mathf.Abs(cross) < 1e-5f ? p2 : p1 + d1 * (((p2.x - p1.x) * d2.y - (p2.y - p1.y) * d2.x) / cross);
            corners.Add(new Vector3(corner.x, corner.y, z));
        }
        return corners;
    }

    // Straight runs between the corners, each corner replaced by a short curve.
    private static List<Vector3> Round(List<Vector3> corners, float radius)
    {
        var points = new List<Vector3>(corners.Count * (CornerSteps + 1));
        for (int i = 0; i < corners.Count; i++)
        {
            Vector3 prev = corners[(i + corners.Count - 1) % corners.Count], at = corners[i], next = corners[(i + 1) % corners.Count];
            float t = Mathf.Min(radius, Vector3.Distance(prev, at) * 0.5f, Vector3.Distance(at, next) * 0.5f);
            Vector3 p0 = at + (prev - at).normalized * t, p2 = at + (next - at).normalized * t;
            for (int k = 0; k <= CornerSteps; k++)
            {
                float u = k / (float)CornerSteps;
                points.Add((1 - u) * (1 - u) * p0 + 2 * (1 - u) * u * at + u * u * p2);
            }
        }
        return points;
    }

    private void AddLine(List<Vector3> points, Color colour, float width)
    {
        var go = new GameObject("Border");
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.loop = true;
        line.alignment = LineAlignment.TransformZ;
        line.widthMultiplier = width;
        line.numCornerVertices = 2;
        line.startColor = line.endColor = colour;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingLayerName = "Hexes";
        line.sortingOrder = SortingOrder;
        line.positionCount = points.Count;
        line.SetPositions(points.ToArray());
        lines.Add(line);
    }

    private void Clear()
    {
        foreach (LineRenderer line in lines) if (line != null) Destroy(line.gameObject);
        lines.Clear();
    }
}
