using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public enum MovementType
{
    ArmyCommander,
    ArmyCommanderCavalryOnly,
    Character
}

[RequireComponent(typeof(Board), typeof(LineRenderer))]
public class HexPathRenderer : MonoBehaviour
{
    public static int MAX_REACHABLE_HEXES = 631; // MAX_MOVEMENT OF 14, hex, supposing all cost 1 => 1 + 6 * (n(n+1)/2)

    // How many spline points to generate between each pair of hex centers. The flow animation
    // (HexPathFlow shader) relies on the LineRenderer's Tile texture mode, which restarts badly
    // if segments are too coarse, so this needs to stay reasonably high for a fluid look.
    [SerializeField] private int splineSegmentsPerHex = 8;

    // How long each opportunity-hint route stays on screen before the cycle moves on to the
    // next candidate hex.
    [SerializeField] private float opportunityHintDwellSeconds = 1.6f;

    // Distinct material for the opportunity/encounter hint route so it reads as a separate,
    // ambient cue rather than the actual movement plan (which uses the main LineRenderer's own
    // material). Falls back to the movement path's material if left unassigned.
    [SerializeField] private Material hintPathMaterial;

    [Header("Opportunity Hint Marker")]
    [SerializeField] private TMP_FontAsset hintMarkerFont;
    [SerializeField] private float hintMarkerFontSize = 3f;
    [SerializeField] private float hintMarkerPulseSeconds = 0.6f;
    [SerializeField] private Vector3 hintMarkerLocalOffset = new(0f, 0f, -0.1f);

    private LineRenderer lineRenderer;
    private LineRenderer hintLineRenderer;
    private Coroutine opportunityHintCoroutine;
    private TextMeshPro hintMarkerText;
    private Coroutine hintMarkerPulseCoroutine;
    private Board board;
    private Dictionary<(Character, int), HashSet<Vector2Int>> rangeCache = new();
    private Dictionary<Vector2Int, bool> waterTerrainCache = new();
    private Dictionary<(Vector2Int, Character), float> terrainCostCache = new();

    void Start()
    {
        board = GetComponent<Board>();
        lineRenderer = GetComponent<LineRenderer>();
        if (board == null)
        {
            Debug.LogError("Board component not found!");
        }
    }
    void Update()
    {
        // Check right mouse button state and update it in the OnHoverTile class
        OnHoverTile.UpdateMouseState(Input.GetMouseButton(1));
    }

    public void DrawPathBetweenHexes(Vector2Int from, Vector2Int to, Character character)
    {
        // Round the input coordinates to ensure they match actual hex positions
        Vector2Int fromRounded = new (Mathf.RoundToInt(from.x), Mathf.RoundToInt(from.y));
        Vector2Int toRounded = new (Mathf.RoundToInt(to.x), Mathf.RoundToInt(to.y));

        // Find the path using A* algorithm
        List<Vector2Int> path = FindPath(fromRounded, toRounded, character);

        lineRenderer.positionCount = 0;
        lineRenderer.SetPositions(new Vector3[] { });

        // Check if path exists
        if (path == null || path.Count == 0) return;

        int movementLeft = character.GetMovementLeft();
        bool wasInWater = IsWaterTerrain(from);
        var hexCenters = new List<Vector3>(path.Count);

        // Convert hex coordinates to world positions
        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int hexPos = path[i];
            bool isInWater = IsWaterTerrain(hexPos);
            if (board.hexes.TryGetValue(hexPos, out Hex hexObj))
            {
                // Get the world position of the hex center
                Vector3 worldPos = hexObj.transform.position;
                hexCenters.Add(worldPos);
                if(i != 0)
                {
                    if(wasInWater != isInWater)
                    {
                        movementLeft -= movementLeft;
                    } else
                    {
                        movementLeft -= hexObj.GetTerrainCost(character);
                    }
                }

                hexObj.ShowMovementLeft(movementLeft, character);
            }
            else
            {
                Debug.LogError($"Hex at position {hexPos} not found in board.hexes dictionary!");
                HidePath();
            }
        }

        DrawFluidPath(hexCenters);
    }

    // Resamples the straight hex-center-to-hex-center path into a smooth Catmull-Rom spline so
    // the movement path reads as a fluid, curved trail rather than sharp straight segments.
    private void DrawFluidPath(List<Vector3> hexCenters)
    {
        if (hexCenters.Count == 0)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        if (hexCenters.Count == 1)
        {
            lineRenderer.positionCount = 1;
            lineRenderer.SetPosition(0, hexCenters[0]);
            return;
        }

        List<Vector3> curve = BuildCatmullRomSpline(hexCenters, splineSegmentsPerHex);
        lineRenderer.positionCount = curve.Count;
        lineRenderer.SetPositions(curve.ToArray());
    }

    private static List<Vector3> BuildCatmullRomSpline(List<Vector3> points, int segmentsPerSpan)
    {
        var result = new List<Vector3>();
        int last = points.Count - 1;

        for (int i = 0; i < last; i++)
        {
            Vector3 p0 = points[Mathf.Max(i - 1, 0)];
            Vector3 p1 = points[i];
            Vector3 p2 = points[i + 1];
            Vector3 p3 = points[Mathf.Min(i + 2, last)];

            // Skip step 0 past the first span so the shared point between spans isn't duplicated.
            int startStep = i == 0 ? 0 : 1;
            for (int s = startStep; s <= segmentsPerSpan; s++)
            {
                float t = s / (float)segmentsPerSpan;
                result.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return result;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    public List<Vector2Int> FindPath(Vector2Int startPos, Vector2Int goalPos, Character character)
    {
        int movementLeft = character.GetMovementLeft();
        if (movementLeft < 1) return new List<Vector2Int> { };
        // If start and goal are the same, return just that position
        if (startPos == goalPos) return new List<Vector2Int> { startPos };

        var openSet = new List<Vector2Int>();
        var closedSet = new HashSet<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float>();
        var fScore = new Dictionary<Vector2Int, float>();
        var hasTransition = new Dictionary<Vector2Int, bool>();

        // Initialize the starting position
        openSet.Add(startPos);
        gScore[startPos] = 0;
        fScore[startPos] = HexDistance(startPos, goalPos);
        hasTransition[startPos] = false;

        // Track the best path so far
        Vector2Int bestEnd = startPos;
        float bestDistanceToGoal = HexDistance(startPos, goalPos);
        bool foundTransitionPath = false;
        bool foundNonTransitionPath = false;

        while (openSet.Count > 0)
        {
            // Find hex with lowest fScore in openSet
            Vector2Int current = openSet[0];
            float lowestFScore = float.MaxValue;
            if (fScore.TryGetValue(current, out float currentF)) lowestFScore = currentF;

            for (int i = 1; i < openSet.Count; i++)
            {
                if (fScore.TryGetValue(openSet[i], out float f) && f < lowestFScore)
                {
                    current = openSet[i];
                    lowestFScore = f;
                }
            }

            // Check if we've reached the goal
            if (current == goalPos)
            {
                return ReconstructPath(cameFrom, current, startPos);
            }

            // Check if this could be a better endpoint
            float distanceToGoal = HexDistance(current, goalPos);
            bool nodeHasTransition = hasTransition[current];

            // Update best path logic:
            // 1. If we haven't found any transition path yet, update bestEnd
            // 2. If this is a non-transition path and we don't have one yet, prefer it
            // 3. If types match (both transition or both non-transition), prefer closer to goal
            if ((!foundTransitionPath && !foundNonTransitionPath) ||
                (!nodeHasTransition && !foundNonTransitionPath) ||
                (nodeHasTransition == foundTransitionPath && nodeHasTransition == foundNonTransitionPath && distanceToGoal < bestDistanceToGoal))
            {
                bestEnd = current;
                bestDistanceToGoal = distanceToGoal;

                if (nodeHasTransition)
                    foundTransitionPath = true;
                else
                    foundNonTransitionPath = true;
            }

            openSet.Remove(current);
            closedSet.Add(current);

            // Get all neighbors of the current hex
            foreach (var neighbor in GetNeighbors(current))
            {
                if (closedSet.Contains(neighbor) || !board.hexes.ContainsKey(neighbor)) continue;

                // Check terrain transition (land to water or water to land)
                bool isCurrentWater = IsWaterTerrain(current);
                bool isNeighborWater = IsWaterTerrain(neighbor);
                bool isTerrainTransition = isCurrentWater != isNeighborWater;

                // Block land-to-water movement for armies without enough warships (ws must be >= non-ws)
                if (!isCurrentWater && isNeighborWater)
                {
                    Army army = character.GetArmy();
                    if (army != null && army.ws < army.GetSize(true)) continue;
                }

                // Calculate movement cost
                float terrainCost = GetTerrainCost(neighbor, character);
                float tentativeGScore = gScore[current] + terrainCost;

                // Check if this would exceed movement
                bool isFirstStep = current == startPos;
                if (tentativeGScore > movementLeft && !isFirstStep) continue;
                if (tentativeGScore > movementLeft && isFirstStep)
                {
                    // Allow one costly step by spending all remaining movement
                    tentativeGScore = movementLeft;
                }

                // Special case: Allow transition if neighbor is the goal position
                bool isGoalPosition = neighbor == goalPos;

                // If this is a transition hex and NOT the goal, mark it as an endpoint candidate but don't extend the path
                if (isTerrainTransition && !isGoalPosition)
                {
                    // Update the neighbor's path information
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;

                    // Track it as a potential best endpoint
                    float distanceToGoalNeighbor = HexDistance(neighbor, goalPos);
                    if (!foundTransitionPath || distanceToGoalNeighbor < bestDistanceToGoal)
                    {
                        bestEnd = neighbor;
                        bestDistanceToGoal = distanceToGoalNeighbor;
                        foundTransitionPath = true;
                    }

                    // Don't add to openSet - this prevents the path from extending beyond this hex
                    continue;
                }

                // For non-transition hexes or if it's the goal position, continue as normal
                if (!openSet.Contains(neighbor))
                {
                    openSet.Add(neighbor);
                }
                else if (gScore.TryGetValue(neighbor, out float neighborG) && tentativeGScore >= neighborG)
                {
                    continue;
                }

                // Update path information
                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeGScore;
                fScore[neighbor] = tentativeGScore + HexDistance(neighbor, goalPos);
                hasTransition[neighbor] = hasTransition[current] || isTerrainTransition;
            }
        }

        // If we couldn't reach the goal but found a valid path
        if (bestEnd != startPos)
        {
            return ReconstructPath(cameFrom, bestEnd, startPos);
        }

        // No path found
        return new List<Vector2Int> { };
    }

    // Helper method to check if a hex is water terrain
    private bool IsWaterTerrain(Vector2Int position)
    {
        if (waterTerrainCache.TryGetValue(position, out bool isWater))
        {
            return isWater;
        }

        if (board.hexes.TryGetValue(position, out var hex))
        {
            isWater = hex.IsWaterTerrain();
            waterTerrainCache[position] = isWater;
            return isWater;
        }
        return false;
    }

    private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current, Vector2Int startPos)
    {
        var path = new List<Vector2Int> { current };

        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }

        // Make sure the starting position is included
        if (path.Count == 0 || path[0] != startPos)
        {
            path.Insert(0, startPos);
        }

        return path;
    }

    private List<Vector2Int> GetNeighbors(Vector2Int hexPos)
    {
        var neighbors = new List<Vector2Int>();
        Vector2Int[] directionsToCheck;

        // Round to ensure we're using integer coordinates for lookup
        int x = Mathf.RoundToInt(hexPos.x);
        int y = Mathf.RoundToInt(hexPos.y);

        // Determine if we're in an even or odd row
        // We need to check X (not Y) to determine whether to use odd or even directions
        if (x % 2 == 0)
        {
            directionsToCheck = board.evenRowNeighbors;
        }
        else
        {
            directionsToCheck = board.oddRowNeighbors;
        }

        // Add all six neighbors
        foreach (var dir in directionsToCheck)
        {
            Vector2Int neighborPos = new Vector2Int(x + dir.x, y + dir.y);

            // Only add if the hex exists on the board
            if (board.hexes.ContainsKey(neighborPos)) neighbors.Add(neighborPos);
        }

        return neighbors;
    }

    // Get the terrain cost for a hex
    private float GetTerrainCost(Vector2Int hexPos, Character character)
    {
        var cacheKey = (hexPos, character);
        if (terrainCostCache.TryGetValue(cacheKey, out float cost))
        {
            return cost;
        }

        if (board.hexes.TryGetValue(hexPos, out Hex hex))
        {
            cost = hex.GetTerrainCost(character);
            terrainCostCache[cacheKey] = cost;
            return cost;
        }

        return 1f; // Default cost if terrain can't be determined
    }

    // Calculate the hex distance (in a hex grid)
    private float HexDistance(Vector2Int a, Vector2Int b)
    {
        // Convert to cube coordinates for easier distance calculation
        Vector3 aCube = OffsetToCube(a);
        Vector3 bCube = OffsetToCube(b);

        // The distance in a hex grid is the maximum component distance
        return Mathf.Max(
            Mathf.Abs(aCube.x - bCube.x),
            Mathf.Abs(aCube.y - bCube.y),
            Mathf.Abs(aCube.z - bCube.z)
        );
    }

    private Vector3 OffsetToCube(Vector2Int hex)
    {
        int x = Mathf.RoundToInt(hex.x);
        int y = Mathf.RoundToInt(hex.y);

        // For this specific coordinate system with the given direction vectors
        // We can use the standard odd-offset conversion, but adjusted for your grid orientation
        float q = (x - (x & 1)) / 2 + y;
        float r = x;
        float s = -q - r;

        return new Vector3(q, r, s);
    }

    public void HidePath()
    {
        FindObjectsByType<MovementCostManager>().ToList().ForEach((x) => x.Hide());
        lineRenderer.SetPositions(new Vector3[] { });
        lineRenderer.positionCount = 0;
    }

    // Cycles a fluid route (same spline + flow shader as the interactive movement path, but on
    // a separate LineRenderer so it never fights the drag/hover preview) from the character's
    // hex to each hex in turn, showing exactly one at a time so several candidates don't tangle
    // into overlapping lines. Used for ambient "you could reach an opportunity card here" cues,
    // not an actual movement plan - so unlike DrawPathBetweenHexes it never shows the per-hex
    // movement-cost bubbles.
    public void StartOpportunityHintCycle(Character character, List<Vector2Int> targetHexes)
    {
        StopOpportunityHintCycle();
        if (character == null || character.hex == null || targetHexes == null || targetHexes.Count == 0) return;

        opportunityHintCoroutine = StartCoroutine(OpportunityHintCycleRoutine(character, targetHexes));
    }

    public void StopOpportunityHintCycle()
    {
        if (opportunityHintCoroutine != null)
        {
            StopCoroutine(opportunityHintCoroutine);
            opportunityHintCoroutine = null;
        }
        HideOpportunityHintPath();
    }

    // One-shot version of the opportunity-hint cycle for a single hex that auto-stops after
    // durationSeconds - used to point at a specific hex (e.g. an encounter card's target hex)
    // rather than looping through several candidates forever. Guards against stomping a newer
    // cycle: if something else (re-)started the hint cycle before this timeout fires, the stale
    // timeout is a no-op instead of cutting the newer cycle short.
    public void PulseHintPath(Character character, Vector2Int targetHex, float durationSeconds)
    {
        StartOpportunityHintCycle(character, new List<Vector2Int> { targetHex });
        StartCoroutine(StopHintCycleAfter(opportunityHintCoroutine, durationSeconds));
    }

    private IEnumerator StopHintCycleAfter(Coroutine cycleToStop, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (opportunityHintCoroutine == cycleToStop)
        {
            StopOpportunityHintCycle();
        }
    }

    private IEnumerator OpportunityHintCycleRoutine(Character character, List<Vector2Int> targetHexes)
    {
        var wait = new WaitForSeconds(opportunityHintDwellSeconds);
        while (true)
        {
            for (int i = 0; i < targetHexes.Count; i++)
            {
                if (character == null || character.hex == null) yield break;
                ShowOpportunityHintPath(character.hex.v2, targetHexes[i], character);
                yield return wait;
            }
        }
    }

    private void ShowOpportunityHintPath(Vector2Int from, Vector2Int to, Character character)
    {
        EnsureHintLineRenderer();

        List<Vector2Int> path = FindPath(from, to, character);
        if (path == null || path.Count == 0)
        {
            hintLineRenderer.positionCount = 0;
            HideHintMarker();
            return;
        }

        var hexCenters = new List<Vector3>(path.Count);
        foreach (Vector2Int hexPos in path)
        {
            if (board.hexes.TryGetValue(hexPos, out Hex hexObj))
            {
                hexCenters.Add(hexObj.transform.position);
            }
        }

        if (hexCenters.Count == 0)
        {
            hintLineRenderer.positionCount = 0;
            HideHintMarker();
            return;
        }

        if (hexCenters.Count == 1)
        {
            hintLineRenderer.positionCount = 1;
            hintLineRenderer.SetPosition(0, hexCenters[0]);
            ShowHintMarkerAt(hexCenters[0]);
            return;
        }

        List<Vector3> curve = BuildCatmullRomSpline(hexCenters, splineSegmentsPerHex);
        hintLineRenderer.positionCount = curve.Count;
        hintLineRenderer.SetPositions(curve.ToArray());
        ShowHintMarkerAt(hexCenters[hexCenters.Count - 1]);
    }

    private void HideOpportunityHintPath()
    {
        if (hintLineRenderer != null) hintLineRenderer.positionCount = 0;
        HideHintMarker();
    }

    // Animated "?" cue (Tiny5 font, pulsing white/black) marking the end hex of whichever route
    // is currently being pulsed - the opportunity-hint cycle or the encounter-card one-shot
    // pulse both funnel through ShowOpportunityHintPath, so both get the marker for free.
    private void ShowHintMarkerAt(Vector3 endHexWorldPosition)
    {
        EnsureHintMarker();
        hintMarkerText.transform.position = endHexWorldPosition + hintMarkerLocalOffset;
        if (!hintMarkerText.gameObject.activeSelf) hintMarkerText.gameObject.SetActive(true);
        if (hintMarkerPulseCoroutine == null)
        {
            hintMarkerPulseCoroutine = StartCoroutine(PulseHintMarkerRoutine());
        }
    }

    private void HideHintMarker()
    {
        if (hintMarkerPulseCoroutine != null)
        {
            StopCoroutine(hintMarkerPulseCoroutine);
            hintMarkerPulseCoroutine = null;
        }
        if (hintMarkerText != null) hintMarkerText.gameObject.SetActive(false);
    }

    private IEnumerator PulseHintMarkerRoutine()
    {
        while (true)
        {
            yield return LerpMarkerColor(Color.white, Color.black, hintMarkerPulseSeconds);
            yield return LerpMarkerColor(Color.black, Color.white, hintMarkerPulseSeconds);
        }
    }

    private IEnumerator LerpMarkerColor(Color from, Color to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            hintMarkerText.color = Color.Lerp(from, to, t / duration);
            yield return null;
        }
        hintMarkerText.color = to;
    }

    private void EnsureHintMarker()
    {
        if (hintMarkerText != null) return;

        GameObject markerObj = new("OpportunityHintMarker");
        markerObj.transform.SetParent(transform, false);
        hintMarkerText = markerObj.AddComponent<TextMeshPro>();
        hintMarkerText.text = "?";
        hintMarkerText.font = hintMarkerFont;
        hintMarkerText.fontSize = hintMarkerFontSize;
        hintMarkerText.alignment = TextAlignmentOptions.Center;
        hintMarkerText.color = Color.white;

        MeshRenderer markerRenderer = markerObj.GetComponent<MeshRenderer>();
        if (markerRenderer != null)
        {
            markerRenderer.sortingLayerID = hintLineRenderer.sortingLayerID;
            markerRenderer.sortingOrder = hintLineRenderer.sortingOrder + 1;
        }

        markerObj.SetActive(false);
    }

    // Mirrors the authored movement-path LineRenderer's look (material, width, gradient,
    // sorting) onto a second LineRenderer dedicated to the opportunity-hint cue, built lazily
    // so boards that never hint an opportunity never pay for it.
    private void EnsureHintLineRenderer()
    {
        if (hintLineRenderer != null) return;

        GameObject hintObj = new("OpportunityHintPath");
        hintObj.transform.SetParent(transform, false);
        hintLineRenderer = hintObj.AddComponent<LineRenderer>();

        hintLineRenderer.sharedMaterial = hintPathMaterial != null ? hintPathMaterial : lineRenderer.sharedMaterial;
        hintLineRenderer.widthCurve = lineRenderer.widthCurve;
        hintLineRenderer.widthMultiplier = lineRenderer.widthMultiplier;
        hintLineRenderer.colorGradient = lineRenderer.colorGradient;
        hintLineRenderer.numCornerVertices = lineRenderer.numCornerVertices;
        hintLineRenderer.numCapVertices = lineRenderer.numCapVertices;
        hintLineRenderer.alignment = lineRenderer.alignment;
        hintLineRenderer.textureMode = lineRenderer.textureMode;
        hintLineRenderer.textureScale = lineRenderer.textureScale;
        hintLineRenderer.useWorldSpace = true;
        hintLineRenderer.sortingLayerID = lineRenderer.sortingLayerID;
        hintLineRenderer.sortingOrder = lineRenderer.sortingOrder;
        hintLineRenderer.positionCount = 0;
    }

    public HashSet<Vector2Int> FindAllHexesV2InRange(Character character)
        => FindAllHexesV2InRange(character, character.GetMaxMovement());

    public HashSet<Vector2Int> FindAllHexesV2InRange(Character character, int movementBudget)
    {
        Vector2Int startPos = character.hex.v2;
        int maxMovement = movementBudget;

        // Check cache first
        var cacheKey = (character, maxMovement);
        if (rangeCache.TryGetValue(cacheKey, out var cachedRange))
        {
            return cachedRange;
        }

        var reachableHexes = new HashSet<Vector2Int>();
        var openSet = new List<Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float>();

        openSet.Add(startPos);
        reachableHexes.Add(startPos);
        gScore[startPos] = 0;

        while (openSet.Count > 0)
        {
            // Find node with lowest cost
            Vector2Int current = openSet[0];
            float lowestGScore = gScore[current];
            int currentIndex = 0;

            for (int i = 1; i < openSet.Count; i++)
            {
                if (gScore[openSet[i]] < lowestGScore)
                {
                    lowestGScore = gScore[openSet[i]];
                    current = openSet[i];
                    currentIndex = i;
                }
            }

            // Remove current hex efficiently
            openSet[currentIndex] = openSet[openSet.Count - 1];
            openSet.RemoveAt(openSet.Count - 1);

            // Get neighbors
            var neighbors = GetNeighbors(current);
            foreach (var neighbor in neighbors)
            {
                if (!board.hexes.ContainsKey(neighbor)) continue;

                bool isCurrentWater = IsWaterTerrain(current);
                bool isNeighborWater = IsWaterTerrain(neighbor);
                bool isTerrainTransition = isCurrentWater != isNeighborWater;

                float terrainCost = GetTerrainCost(neighbor, character);
                float tentativeGScore = gScore[current] + terrainCost;

                if (tentativeGScore > maxMovement) continue;
                bool isFirstStep = current == startPos;
                if (tentativeGScore > maxMovement && !isFirstStep) continue;
                if (tentativeGScore > maxMovement && isFirstStep)
                {
                    tentativeGScore = maxMovement;
                }

                if (isTerrainTransition)
                {
                    if (!reachableHexes.Contains(neighbor))
                    {
                        reachableHexes.Add(neighbor);
                        gScore[neighbor] = tentativeGScore;
                    }
                    continue;
                }

                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    gScore[neighbor] = tentativeGScore;

                    if (!reachableHexes.Contains(neighbor))
                    {
                        reachableHexes.Add(neighbor);
                        openSet.Add(neighbor);
                    }
                }
            }
        }

        // Cache the result
        rangeCache[cacheKey] = reachableHexes;
        return reachableHexes;
    }

    public List<Hex> FindAllHexesInRange(Character character)
        => ResolveHexes(FindAllHexesV2InRange(character));

    public List<Hex> FindAllHexesInRange(Character character, int movementBudget)
        => ResolveHexes(FindAllHexesV2InRange(character, movementBudget));

    // Movement-remaining-aware variant of FindAllHexesInRange, used for the selected-character
    // opportunity-card hex hint (Part 6): highlights where the character could still move to
    // and play an opportunity card THIS turn, not a fresh-turn full-movement range.
    public List<Hex> FindAllHexesInRemainingRange(Character character)
        => FindAllHexesInRange(character, character.GetMovementLeft());

    private List<Hex> ResolveHexes(HashSet<Vector2Int> v2Hexes)
    {
        var result = new List<Hex>(v2Hexes.Count);
        foreach (var v2 in v2Hexes)
        {
            if (board.hexes.TryGetValue(v2, out var hex))
            {
                result.Add(hex);
            }
        }
        return result;
    }

    public void ClearCache()
    {
        rangeCache.Clear();
        waterTerrainCache.Clear();
        terrainCostCache.Clear();
    }

    public float GetPathCost(Vector2Int startHex, Vector2Int endHex, Character character)
    {
        // Find the path first
        List<Vector2Int> path = FindPath(startHex, endHex, character);

        // If there's no valid path, return an impossible cost (or -1 if you prefer)
        if (path == null || path.Count < 2)
        {
            return -1f;
        }

        float totalCost = 0f;

        // Sum terrain costs between each hex in the path
        for (int i = 1; i < path.Count; i++)
        {
            Vector2Int currentHex = path[i];
            totalCost += GetTerrainCost(currentHex, character);
        }

        return totalCost;
    }
}
