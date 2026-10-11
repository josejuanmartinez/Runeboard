using System.Collections.Generic;
using System;
using UnityEngine;
using System.Collections;
using Random = UnityEngine.Random;
using UnityEngine.Pool;

#region Small helper to time-slice work per frame

/// <summary>
/// Simple per-frame time budget helper. Call Spent() inside hot loops and yield when true.
/// </summary>
public struct FrameBudget
{
    private float _start;
    private readonly float _maxSeconds; // e.g., 0.002f = 2ms
    private readonly Func<float> _maxSecondsProvider;

    public FrameBudget(float maxSeconds)
    {
        _start = Time.realtimeSinceStartup;
        _maxSeconds = maxSeconds;
        _maxSecondsProvider = null;
    }

    public FrameBudget(Func<float> maxSecondsProvider)
    {
        _start = Time.realtimeSinceStartup;
        _maxSeconds = 0f;
        _maxSecondsProvider = maxSecondsProvider;
    }

    private float CurrentBudget => _maxSecondsProvider != null ? Mathf.Max(0f, _maxSecondsProvider()) : _maxSeconds;

    public bool Spent()
    {
        return Time.realtimeSinceStartup - _start >= CurrentBudget;
    }

    public void Reset()
    {
        _start = Time.realtimeSinceStartup;
    }
}

#endregion

[RequireComponent(typeof(Board))]
public class BoardGenerator : MonoBehaviour
{
    public Board board;

    // Chance parameters
    [Header("Generation Parameters")]
    [Range(1, 5)] public int minIslands = 1;
    [Range(1, 5)] public int maxIslands = 5;
    [Range(0.1f, 0.3f)] public float waterPercentage = 0.15f;
    [Range(0.1f, 1f)] public float deepWaterProportion = 0.85f;
    [Range(0.1f, 0.3f)] public float desertPercentage = 0.1395f;
    [Range(0.1f, 0.9f)] public float grasslandsProbability = 0.5f; // Chance of plains becoming grasslands
    [Range(0.1f, 0.5f)] public float coastDepth = 0.15f;
    [Range(0.5f, 1.0f)] public float shoreProbability = 0.85f; // Chance a coastal tile is shore (lakes and rivers excluded)

    [Header("Mountain Parameters")]
    [Range(1, 5)] public int mountainChainCount = 3;
    [Range(0.5f, 2.0f)] public float mountainChainLengthMultiplier = 1.5f;

    [Header("Forest Parameters")]
    [Range(1, 3)] public int majorForestCount = 2;
    [Range(2, 6)] public int minorForestCount = 5;

    [Header("Swamp Parameters")]
    [Range(1, 3)] public int swampCount = 2;
    [Range(4, 7)] public int swampSize = 4;

    [Header("Wastelands Parameters")]
    [Range(1, 2)] public int majorWastelandCount = 1;
    [Range(0, 3)] public int minorWastelandCount = 2;

    [Header("Snow Parameters")]
    [Tooltip("Fraction of the map covered by snow on randomly generated boards (0 = none).")]
    [Range(0f, 0.07f)] public float snowPercentage = 0f;

    [Header("River and Lakes Parameters")]
    [Range(2, 5)] public int rivers = 2;
    [Range(2, 5)] public int lakes = 2;

    // Array to store the terrain types
    private TerrainEnum[,] terrainGrid;
    private int seaBorder;
    private Dictionary<Vector2Int, Hex> hexes;
    private ObjectPool<GameObject> hexPool;
    private Dictionary<TerrainEnum, Color> terrainColors;

    // Hexes adjacent to rivers (shallowWater cells that are part of rivers)
    [System.NonSerialized] public HashSet<Vector2Int> riverCoastHexes = new HashSet<Vector2Int>();

    // Hexes adjacent to lakes (shallowWater clusters generated as lakes)
    [System.NonSerialized] public HashSet<Vector2Int> lakeCoastHexes = new HashSet<Vector2Int>();

    // Internally used to mark which shallowWater are rivers vs lakes
    private HashSet<Vector2Int> riverCells = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> lakeCells = new HashSet<Vector2Int>();

    // Delegate for progress reporting
    public delegate void GenerationProgressDelegate(float progress, string stage);
    public event GenerationProgressDelegate OnGenerationProgress;

    // For tracking progress
    private float totalSteps = 11; // Total generation steps
    private float currentStep = 0;

    private float GetStepProgress(float stepProgress)
    {
        // If we're at the last step or beyond, just return 1.0f
        if (currentStep >= totalSteps) return 1.0f; 
        
        float totalWeight = currentStep * 0.1f;

        return Mathf.Clamp01(totalWeight);
    }

    private void Awake()
    {
        Application.backgroundLoadingPriority = ThreadPriority.Low;

        if (board == null) board = GetComponent<Board>();

        if (board != null && board.hexPrefab != null)
        {
            hexPool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    GameObject go = Instantiate(board.hexPrefab);
                    prefabInstantiateTicks += sw.ElapsedTicks;
                    prefabInstantiateCount++;
                    return go;
                },
                actionOnGet: (obj) => obj.SetActive(true),
                actionOnRelease: (obj) => obj.SetActive(false),
                actionOnDestroy: (obj) => Destroy(obj),
                defaultCapacity: 1000,
                maxSize: 10000
            );
        }
        else
        {
            Debug.LogError("Board or hexPrefab is not assigned in BoardGenerator!");
        }
    }

    private static long prefabInstantiateTicks;
    private static int prefabInstantiateCount;

    // One dial for every time-sliced part of loading — procedural terrain generation,
    // hex-clone integration ("Conjuring Tiles"), and hex placement ("Configuring Board").
    // All three are the same trade-off (main-thread time per frame vs. intro-video
    // smoothness), so they used to be 3 separate pairs of fields (6 total) with no
    // indication of which phase each one touched. Now it's one pair: tight while the
    // video plays, released to the fast value once the video ends/is skipped/is absent
    // (same pattern as before, see SetVideoPlaying below).
    [Header("Loading speed vs. intro-video smoothness (ms of main-thread time per frame)")]
    [Tooltip("Applies to ALL time-sliced loading: terrain generation, hex cloning, hex placement. Budget WHILE THE INTRO VIDEO PLAYS (clamped 10-200ms). Lower = smoother video, slower load. Higher = faster load, choppier video.")]
    [SerializeField] private float loadBudgetWhileVideoMs = 30f;
    [Tooltip("Same scope as above. Budget once nothing needs to stay smooth (video finished/skipped/absent). 100 ≈ 10 fps, 200 ≈ 5 fps — do not use 1000.")]
    [SerializeField] private float loadBudgetAfterVideoMs = 200f;

    [Header("Hex cloning batch size")]
    [Tooltip("Hexes per async clone batch (clamped 256-2048). Bigger = fewer scheduling stalls between batches = faster.")]
    [SerializeField] private int hexCloneBatchSize = 1024;

    // Clamped accessors so no Inspector combination can freeze input or starve the load.
    private int CloneBatchSize => Mathf.Clamp(hexCloneBatchSize, 256, 2048);
    private float LoadBudgetMs => Mathf.Clamp(loadBudgetWhileVideoMs, 10f, 200f);
    private float LoadBudgetSeconds => LoadBudgetMs / 1000f;

    // Freshly cloned hexes are parked far off-camera until the placement loop
    // positions them; keeps pending clones from being rendered. They stay ACTIVE in
    // the parked queue: deactivating/reactivating a full hex hierarchy per batch
    // caused seconds-long freezes back when the prefab was heavy.
    private static readonly Vector3 HexParkPosition = new(100000f, 100000f, 0f);
    private readonly Queue<GameObject> parkedHexes = new();

    private int AvailableHexes => hexPool.CountInactive + parkedHexes.Count;

    /// <summary>
    /// Call this when your video starts/stops to tighten/relax budgets and loader priority.
    /// </summary>
    public void SetVideoPlaying(bool playing)
    {
        if(!playing)
        {
            loadBudgetWhileVideoMs = loadBudgetAfterVideoMs;
            Application.backgroundLoadingPriority = ThreadPriority.Normal;

            // Push the released budget immediately in case a clone batch is already
            // mid-flight — the video can finish or be skipped while cloning is still
            // running, and AsyncInstantiateOperation reads this per integration slice.
            AsyncInstantiateOperation.SetIntegrationTimeMS(LoadBudgetMs);
        }
    }

    public void Initialize(Board board)
    {
        if (board == null)
        {
            Debug.LogError("Board is null in BoardGenerator.Initialize!");
            return;
        }

        this.board = board;

        if (board.colors == null)
        {
            Debug.LogError("Board colors are not assigned!");
            return;
        }

        CacheTerrainAssets();
    }

    // Lets the Board hand over an externally built grid (authored scenarios) so
    // InstantiateHexesCoroutine can run without GenerateTerrainCoroutine.
    public void SetTerrainGrid(TerrainEnum[,] grid)
    {
        terrainGrid = grid;
    }

    private void CacheTerrainAssets()
    {
        if (board == null || board.colors == null)
        {
            Debug.LogError("Cannot cache terrain assets: Board or colors are null!");
            return;
        }

        terrainColors = new Dictionary<TerrainEnum, Color>();

        foreach (TerrainEnum terrain in Enum.GetValues(typeof(TerrainEnum)))
        {
            try
            {
                var colorFieldInfo = typeof(Colors).GetField(terrain.ToString());
                if (colorFieldInfo != null)
                {
                    terrainColors[terrain] = (Color)colorFieldInfo.GetValue(board.colors);
                }
                else
                {
                    Debug.LogWarning($"Color field not found for terrain type: {terrain}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error caching assets for terrain {terrain}: {e.Message}");
            }
        }
    }

    // Main entry point - now returns a coroutine
    public IEnumerator GenerateTerrainCoroutine(Action<TerrainEnum[,]> onComplete)
    {
        currentStep = 0;

        // Initialize the terrain grid with plains as default
        terrainGrid = new TerrainEnum[board.GetHeight(), board.GetWidth()];
        yield return StartCoroutine(InitializeGridCoroutine());

        // Convert some plains to grasslands
        yield return StartCoroutine(ConvertPlainsToGrasslandsCoroutine());

        // Determine the sea border (0=North, 1=East, 2=South, 3=West)
        seaBorder = Random.Range(0, 4);

        // Generate water (sea) with more natural coastlines
        yield return StartCoroutine(GenerateWaterWithNaturalCoastlineCoroutine(seaBorder));

        // Determine desert border (different from sea border)
        int desertBorder;
        do
        {
            desertBorder = Random.Range(0, 4);
        } while (desertBorder == seaBorder);

        // Generate desert along the chosen border
        yield return StartCoroutine(GenerateDesertCoroutine(desertBorder));

        // Generate mountain chains
        yield return StartCoroutine(GenerateMountainChainsCoroutine());

        // Generate rivers and lakes (uses shallowWater only, 1-hex wide rivers, 3–6-hex lakes)
        yield return StartCoroutine(GenerateRiversAndLakesCoroutine());

        // Identify which land hexes are coastal to rivers/lakes
        yield return StartCoroutine(ComputeRiverAndLakeCoastsCoroutine());

        // Generate forests
        yield return StartCoroutine(GenerateForestsCoroutine());

        // Generate swamps
        yield return StartCoroutine(GenerateSwampsCoroutine());

        // Generate wastelands
        yield return StartCoroutine(GenerateWastelandsCoroutine());

        // Generate snow patches (random boards only; scaled by the snowPercentage slider)
        yield return StartCoroutine(GenerateSnowCoroutine());

        // Apply shore terrain to areas adjacent to shallow water
        yield return StartCoroutine(ApplyShoresCoroutine());

        OnGenerationProgress?.Invoke(1.0f, "Complete");
        onComplete?.Invoke(terrainGrid);
    }

    public IEnumerator InstantiateHexesCoroutine(Action<Dictionary<Vector2Int, Hex>> onComplete)
    {
        int height = board.GetHeight();
        int width = board.GetWidth();
        int totalHexes = height * width;

        hexes = new Dictionary<Vector2Int, Hex>(totalHexes);

        // Clear existing children safely and time-sliced
        yield return StartCoroutine(ClearChildrenCoroutine());

        // Safety: make sure terrainGrid exists and matches board dims
        if (terrainGrid == null ||
            terrainGrid.GetLength(0) != height ||
            terrainGrid.GetLength(1) != width)
        {
            Debug.LogError("InstantiateHexesCoroutine: terrainGrid is null or has wrong size.");
            onComplete?.Invoke(hexes);
            yield break;
        }

        var grid = terrainGrid;
        var colors = terrainColors;
        var parentTransform = transform;

        // Clone everything the board needs on demand, in small async batches
        // (worker-thread cloning, bounded main-thread integration). The hex prefab
        // is slim enough since the HexParts split that no prewarming is needed.
        Application.backgroundLoadingPriority = ThreadPriority.High;
        AsyncInstantiateOperation.SetIntegrationTimeMS(LoadBudgetMs);

        if (AvailableHexes < totalHexes)
        {
            var cloneTimer = System.Diagnostics.Stopwatch.StartNew();
            int toCreate = totalHexes - AvailableHexes;
            while (AvailableHexes < totalHexes)
            {
                int batch = Mathf.Min(CloneBatchSize, totalHexes - AvailableHexes);
                var op = InstantiateAsync(board.hexPrefab, batch, HexParkPosition, Quaternion.identity);
                while (!op.isDone)
                {
                    // Progress must move every frame or the load reads as frozen:
                    // blend the in-flight batch's own progress into the pool count.
                    float inFlight = AvailableHexes + op.progress * batch;
                    OnGenerationProgress?.Invoke(0.6f * inFlight / totalHexes, "Conjuring tiles");
                    yield return null;
                }
                foreach (GameObject hexGo in op.Result)
                {
                    if (hexGo != null) parkedHexes.Enqueue(hexGo);
                }
            }
            Debug.Log($"[ScenarioLoad] async clone of {toCreate} hexes finished in {cloneTimer.ElapsedMilliseconds} ms (available {AvailableHexes}).");
        }

        var budget = new FrameBudget(() => LoadBudgetSeconds);
        int hexesProcessed = 0;

        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        var stepTimer = new System.Diagnostics.Stopwatch();
        long poolTicks = 0;
        long terrainTicks = 0;
        // Diagnostic-only: the per-hex work above (poolTicks/terrainTicks) rarely accounts for
        // the whole loop's wall-clock time — these isolate the other two things that run per
        // iteration, so a slow run tells us whether it's Unity waiting on other work between
        // frames (yieldTicks) or the progress-bar callback itself (progressTicks) that's slow.
        long yieldTicks = 0;
        int yieldCount = 0;
        long progressTicks = 0;
        Debug.Log($"[ScenarioLoad] placing {totalHexes} hexes (budget/frame: {LoadBudgetMs:0.#} ms)...");

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                stepTimer.Restart();
                // Parked clones are already active — using them skips the costly
                // full-hierarchy SetActive(true) the pool would pay.
                GameObject hexGo = parkedHexes.Count > 0 ? parkedHexes.Dequeue() : hexPool.Get();
                var hex = hexGo.GetComponent<Hex>();
                hex.Initialize(row, col);

                var tr = hexGo.transform;
                tr.SetParent(parentTransform, false);
                tr.position = GetPosition(row, col);
                hexGo.name = $"{row},{col}";
                poolTicks += stepTimer.ElapsedTicks;

                var terrainType = grid[row, col];
                var color = colors[terrainType];

                stepTimer.Restart();
                hex.SetTerrain(terrainType, null, color);
                terrainTicks += stepTimer.ElapsedTicks;
                hexes[new Vector2Int(row, col)] = hex;

                hexesProcessed++;

                if (hexesProcessed % 1000 == 0)
                {
                    Debug.Log($"[ScenarioLoad] {hexesProcessed}/{totalHexes} hexes in {totalTimer.ElapsedMilliseconds} ms "
                        + $"(pool+setup {poolTicks / System.TimeSpan.TicksPerMillisecond} ms, terrain {terrainTicks / System.TimeSpan.TicksPerMillisecond} ms, "
                        + $"raw Instantiate lifetime {prefabInstantiateTicks / System.TimeSpan.TicksPerMillisecond} ms / {prefabInstantiateCount})");
                }

                // Progress updates once per actual yield, not per hex: Unity never renders
                // mid-burst, so updating the progress bar more often than that just overwrites
                // itself before a frame ever shows it — wasted Slider/TMP-text work for nothing.
                if (budget.Spent())
                {
                    stepTimer.Restart();
                    OnGenerationProgress?.Invoke(Mathf.Min((float)hexesProcessed / totalHexes, 1.0f), "Configuring Board");
                    progressTicks += stepTimer.ElapsedTicks;

                    // Reset AFTER the wait, not before: resetting first counted the frame-wait
                    // itself against the next burst's budget, so a slow frame (video decode,
                    // low fps, etc.) could exhaust the whole budget before a single new hex was
                    // processed — starving throughput down to a handful of hexes per yield.
                    stepTimer.Restart();
                    yield return null;
                    yieldTicks += stepTimer.ElapsedTicks;
                    yieldCount++;
                    budget.Reset();
                }
            }
        }

        Debug.Log($"[ScenarioLoad] all {hexesProcessed} hexes instantiated in {totalTimer.ElapsedMilliseconds} ms "
            + $"(pool+setup {poolTicks / System.TimeSpan.TicksPerMillisecond} ms, terrain {terrainTicks / System.TimeSpan.TicksPerMillisecond} ms, "
            + $"progress-callback {progressTicks / System.TimeSpan.TicksPerMillisecond} ms, "
            + $"yield-wait {yieldTicks / System.TimeSpan.TicksPerMillisecond} ms across {yieldCount} yields "
            + $"[avg {(yieldCount > 0 ? yieldTicks / System.TimeSpan.TicksPerMillisecond / yieldCount : 0)} ms/yield])");
        OnGenerationProgress?.Invoke(1.0f, "Game ready!");
        onComplete?.Invoke(hexes);

        // Safety net: return any surplus parked clones to the pool a few per
        // frame — never bulk-deactivate. (Normally empty now that hexes are
        // cloned on demand for exactly the board size.)
        if (parkedHexes.Count > 0)
        {
            var drainBudget = new FrameBudget(() => 0.02f);
            while (parkedHexes.Count > 0)
            {
                hexPool.Release(parkedHexes.Dequeue());
                if (drainBudget.Spent())
                {
                    drainBudget.Reset();
                    yield return null;
                }
            }
        }
    }


    private IEnumerator ClearChildrenCoroutine()
    {
        // Release pooled children if they belong to the pool, otherwise Destroy().
        // Time-sliced to avoid spikes.
        var budget = new FrameBudget(() => LoadBudgetSeconds);

        // Snapshot because we're modifying the hierarchy
        int count = transform.childCount;
        if (count == 0) yield break;

        var toClear = new List<GameObject>(count);
        for (int i = 0; i < count; i++)
            toClear.Add(transform.GetChild(i).gameObject);

        foreach (var go in toClear)
        {
            // If this object came from our pool, release; otherwise, Destroy().
            // (If you're *sure* all children are pooled, you can always Release.)
            hexPool.Release(go);

            if (budget.Spent())
            {
                yield return null;
                budget.Reset();
            }
        }
    }

    private Vector3 GetPosition(int row, int col)
    {
        /*float hexWidth = board.hexSize.x * board.hexPrefab.transform.localScale.x;
        float hexHeight = board.hexSize.y * board.hexPrefab.transform.localScale.y;

        float xOffset = col * hexWidth;  // Default spacing
        float yOffset = row * hexHeight * 0.5f;  // Proper vertical stacking

        // Shift odd rows to interlock with the previous row
        if (row % 2 == 1)
        {
            xOffset += hexWidth * 0.5f; // Half of a hex board.GetWidth()
        }*/

        float xOffset = col * board.hexSize.x;
        if(row % 2 == 1) xOffset += board.hexSize.x * 0.5f; // Half of a hex board.GetWidth()
        // Row 0 is the NORTH edge (the convention the terrain generator and the Scenario Creator
        // both use), so rows grow downward in world space — otherwise authored maps render
        // vertically mirrored versus the editor that authored them.
        float yOffset = -row * board.hexSize.y;

        return new Vector3(xOffset, yOffset, 0);
    }

    private IEnumerator InitializeGridCoroutine()
    {
        int totalCells = board.GetHeight() * board.GetWidth();
        int cellsPerBatchLocal = 1000;
        int cellsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        for (int row = 0; row < board.GetHeight(); row++)
        {
            for (int col = 0; col < board.GetWidth(); col++)
            {
                terrainGrid[row, col] = TerrainEnum.plains;

                cellsProcessed++;

                // Yield by batch OR when time budget is spent
                if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                {
                    float progress = (float)cellsProcessed / totalCells;
                    float stepProgress = GetStepProgress(progress);
                    OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Configuring Terrain");
                    yield return null;
                    budget.Reset();
                }
            }
        }

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Terrain ready");
        currentStep++;
    }

    private IEnumerator ConvertPlainsToGrasslandsCoroutine()
    {
        int totalCells = board.GetHeight() * board.GetWidth();
        int cellsPerBatchLocal = 500;
        int cellsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        for (int row = 0; row < board.GetHeight(); row++)
        {
            for (int col = 0; col < board.GetWidth(); col++)
            {
                if (terrainGrid[row, col] == TerrainEnum.plains && Random.value < grasslandsProbability)
                {
                    terrainGrid[row, col] = TerrainEnum.grasslands;
                }

                cellsProcessed++;

                if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                {
                    float progress = (float)cellsProcessed / totalCells;
                    float stepProgress = GetStepProgress(progress);
                    OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Grasslands");
                    yield return null;
                    budget.Reset();
                }
            }
        }

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Grasslands Created");
        currentStep++;
    }

    private IEnumerator GenerateWaterWithNaturalCoastlineCoroutine(int seaBorder)
    {
        int waterWidth = Mathf.FloorToInt(board.GetWidth() * waterPercentage);
        int waterHeight = Mathf.FloorToInt(board.GetHeight() * waterPercentage);
        int coastlineDepth = Mathf.FloorToInt(Mathf.Min(board.GetWidth(), board.GetHeight()) * coastDepth);

        // Create a noise-based coastline
        int seedX = Random.Range(0, 10000);
        int seedY = Random.Range(0, 10000);

        // Process the main coastline generation in batches
        yield return StartCoroutine(GenerateCoastlineCoroutine(seaBorder, waterWidth, waterHeight, coastlineDepth, seedX, seedY));

        // Create water "fingers" extending inland
        yield return StartCoroutine(GenerateWaterFingersCoroutine(seaBorder, waterWidth, waterHeight, coastlineDepth));

        // Create islands
        yield return StartCoroutine(GenerateIslandsCoroutine(seaBorder, waterWidth, waterHeight));

        OnGenerationProgress?.Invoke(GetStepProgress(1.0f), "Water Generation Complete");
        currentStep++;
    }

    private IEnumerator GenerateCoastlineCoroutine(int seaBorder, int waterWidth, int waterHeight, int coastlineDepth, int seedX, int seedY)
    {
        int totalCells;
        int cellsPerBatchLocal = 300;
        int cellsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        switch (seaBorder)
        {
            case 0: // North border
                totalCells = waterHeight * board.GetWidth();

                for (int row = 0; row < waterHeight; row++)
                {
                    for (int col = 0; col < board.GetWidth(); col++)
                    {
                        float noise = Mathf.PerlinNoise((col + seedX) * 0.1f, seedY * 0.1f);
                        int noiseOffset = Mathf.FloorToInt(noise * coastlineDepth);

                        int deepWaterBoundary = Mathf.FloorToInt(waterHeight * deepWaterProportion);

                        if (row < deepWaterBoundary - noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.deepWater;
                        }
                        else if (row < waterHeight + noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.shallowWater;
                        }

                        cellsProcessed++;
                        if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                        {
                            float progress = (float)cellsProcessed / totalCells;
                            OnGenerationProgress?.Invoke(GetStepProgress(progress * 0.4f), "Creating Coastline");
                            yield return null;
                            budget.Reset();
                        }
                    }
                }
                break;

            case 1: // East border
                totalCells = board.GetHeight() * waterWidth;

                for (int row = 0; row < board.GetHeight(); row++)
                {
                    for (int col = board.GetWidth() - waterWidth; col < board.GetWidth(); col++)
                    {
                        float noise = Mathf.PerlinNoise(seedX * 0.1f, (row + seedY) * 0.1f);
                        int noiseOffset = Mathf.FloorToInt(noise * coastlineDepth);

                        int deepWaterStart = board.GetWidth() - Mathf.FloorToInt(waterWidth * deepWaterProportion);

                        if (col > deepWaterStart + noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.deepWater;
                        }
                        else if (col > board.GetWidth() - waterWidth - noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.shallowWater;
                        }

                        cellsProcessed++;
                        if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                        {
                            float progress = (float)cellsProcessed / totalCells;
                            OnGenerationProgress?.Invoke(GetStepProgress(progress * 0.4f), "Creating Coastline");
                            yield return null;
                            budget.Reset();
                        }
                    }
                }
                break;

            case 2: // South border
                totalCells = waterHeight * board.GetWidth();

                for (int row = board.GetHeight() - waterHeight; row < board.GetHeight(); row++)
                {
                    for (int col = 0; col < board.GetWidth(); col++)
                    {
                        float noise = Mathf.PerlinNoise((col + seedX) * 0.1f, seedY * 0.1f);
                        int noiseOffset = Mathf.FloorToInt(noise * coastlineDepth);

                        int deepWaterStart = board.GetHeight() - Mathf.FloorToInt(waterHeight * deepWaterProportion);

                        if (row > deepWaterStart + noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.deepWater;
                        }
                        else if (row > board.GetHeight() - waterHeight - noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.shallowWater;
                        }

                        cellsProcessed++;
                        if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                        {
                            float progress = (float)cellsProcessed / totalCells;
                            OnGenerationProgress?.Invoke(GetStepProgress(progress * 0.4f), "Creating Coastline");
                            yield return null;
                            budget.Reset();
                        }
                    }
                }
                break;

            case 3: // West border
                totalCells = board.GetHeight() * waterWidth;

                for (int row = 0; row < board.GetHeight(); row++)
                {
                    for (int col = 0; col < waterWidth; col++)
                    {
                        float noise = Mathf.PerlinNoise(seedX * 0.1f, (row + seedY) * 0.1f);
                        int noiseOffset = Mathf.FloorToInt(noise * coastlineDepth);

                        int deepWaterBoundary = Mathf.FloorToInt(waterWidth * deepWaterProportion);

                        if (col < deepWaterBoundary - noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.deepWater;
                        }
                        else if (col < waterWidth + noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.shallowWater;
                        }

                        cellsProcessed++;
                        if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                        {
                            float progress = (float)cellsProcessed / totalCells;
                            OnGenerationProgress?.Invoke(GetStepProgress(progress * 0.4f), "Creating Coastline");
                            yield return null;
                            budget.Reset();
                        }
                    }
                }
                break;
        }
    }

    private IEnumerator GenerateWaterFingersCoroutine(int seaBorder, int waterWidth, int waterHeight, int coastlineDepth)
    {
        int numFingers = Random.Range(2, 5);
        int totalFingers = numFingers;
        int fingersProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        switch (seaBorder)
        {
            case 0: // North border - fingers extend south
                for (int i = 0; i < numFingers; i++)
                {
                    int startCol = Random.Range(0, board.GetWidth());
                    int fingerLength = Random.Range(coastlineDepth, coastlineDepth * 2);
                    int fingerWidth = Random.Range(3, 6);
                    int cellsPerFinger = fingerLength * fingerWidth;
                    int cellsProcessed = 0;

                    for (int row = waterHeight; row < waterHeight + fingerLength && row < board.GetHeight(); row++)
                    {
                        for (int colOffset = -fingerWidth / 2; colOffset <= fingerWidth / 2; colOffset++)
                        {
                            int col = startCol + colOffset;
                            if (col >= 0 && col < board.GetWidth())
                            {
                                float taperChance = (float)(row - waterHeight) / fingerLength;
                                if (Random.value > taperChance * 0.8f)
                                {
                                    if (row < waterHeight + fingerLength / 3)
                                    {
                                        terrainGrid[row, col] = TerrainEnum.shallowWater;
                                    }
                                    else
                                    {
                                        terrainGrid[row, col] = Random.value < 0.7f ?
                                            TerrainEnum.shallowWater : TerrainEnum.shore;
                                    }
                                }
                            }

                            cellsProcessed++;
                        }

                        if ((row % 3 == 0) || budget.Spent())
                        {
                            float fingerProgress = (float)cellsProcessed / cellsPerFinger;
                            float overallProgress = (fingersProcessed + fingerProgress) / totalFingers;
                            OnGenerationProgress?.Invoke(GetStepProgress(0.4f + overallProgress * 0.3f), "Creating Water Features");
                            yield return null;
                            budget.Reset();
                        }
                    }

                    fingersProcessed++;
                }
                break;

            case 1: // East border - fingers extend west
                for (int i = 0; i < numFingers; i++)
                {
                    int startRow = Random.Range(0, board.GetHeight());
                    int fingerLength = Random.Range(coastlineDepth, coastlineDepth * 2);
                    int fingerWidth = Random.Range(3, 6);
                    int cellsPerFinger = fingerLength * fingerWidth;
                    int cellsProcessed = 0;

                    for (int col = board.GetWidth() - waterWidth - 1; col >= board.GetWidth() - waterWidth - fingerLength && col >= 0; col--)
                    {
                        for (int rowOffset = -fingerWidth / 2; rowOffset <= fingerWidth / 2; rowOffset++)
                        {
                            int row = startRow + rowOffset;
                            if (row >= 0 && row < board.GetHeight())
                            {
                                float taperChance = (float)(board.GetWidth() - waterWidth - col) / fingerLength;
                                if (Random.value > taperChance * 0.8f)
                                {
                                    if (col > board.GetWidth() - waterWidth - fingerLength / 3)
                                    {
                                        terrainGrid[row, col] = TerrainEnum.shallowWater;
                                    }
                                    else
                                    {
                                        terrainGrid[row, col] = Random.value < 0.7f ?
                                            TerrainEnum.shallowWater : TerrainEnum.shore;
                                    }
                                }
                            }

                            cellsProcessed++;
                        }

                        if (((board.GetWidth() - waterWidth - 1 - col) % 3 == 0) || budget.Spent())
                        {
                            float fingerProgress = (float)cellsProcessed / cellsPerFinger;
                            float overallProgress = (fingersProcessed + fingerProgress) / totalFingers;
                            OnGenerationProgress?.Invoke(GetStepProgress(0.4f + overallProgress * 0.3f), "Creating Water Features");
                            yield return null;
                            budget.Reset();
                        }
                    }

                    fingersProcessed++;
                }
                break;

            case 2: // South border - fingers extend north
                for (int i = 0; i < numFingers; i++)
                {
                    int startCol = Random.Range(0, board.GetWidth());
                    int fingerLength = Random.Range(coastlineDepth, coastlineDepth * 2);
                    int fingerWidth = Random.Range(3, 6);
                    int cellsPerFinger = fingerLength * fingerWidth;
                    int cellsProcessed = 0;

                    for (int row = board.GetHeight() - waterHeight - 1; row >= board.GetHeight() - waterHeight - fingerLength && row >= 0; row--)
                    {
                        for (int colOffset = -fingerWidth / 2; colOffset <= fingerWidth / 2; colOffset++)
                        {
                            int col = startCol + colOffset;
                            if (col >= 0 && col < board.GetWidth())
                            {
                                float taperChance = (float)(board.GetHeight() - waterHeight - row) / fingerLength;
                                if (Random.value > taperChance * 0.8f)
                                {
                                    if (row > board.GetHeight() - waterHeight - fingerLength / 3)
                                    {
                                        terrainGrid[row, col] = TerrainEnum.shallowWater;
                                    }
                                    else
                                    {
                                        terrainGrid[row, col] = Random.value < 0.7f ?
                                            TerrainEnum.shallowWater : TerrainEnum.shore;
                                    }
                                }
                            }

                            cellsProcessed++;
                        }

                        if (((board.GetHeight() - waterHeight - 1 - row) % 3 == 0) || budget.Spent())
                        {
                            float fingerProgress = (float)cellsProcessed / cellsPerFinger;
                            float overallProgress = (fingersProcessed + fingerProgress) / totalFingers;
                            OnGenerationProgress?.Invoke(GetStepProgress(0.4f + overallProgress * 0.3f), "Creating Water Features");
                            yield return null;
                            budget.Reset();
                        }
                    }

                    fingersProcessed++;
                }
                break;

            case 3: // West border - fingers extend east
                for (int i = 0; i < numFingers; i++)
                {
                    int startRow = Random.Range(0, board.GetHeight());
                    int fingerLength = Random.Range(coastlineDepth, coastlineDepth * 2);
                    int fingerWidth = Random.Range(3, 6);
                    int cellsPerFinger = fingerLength * fingerWidth;
                    int cellsProcessed = 0;

                    for (int col = waterWidth; col < waterWidth + fingerLength && col < board.GetWidth(); col++)
                    {
                        for (int rowOffset = -fingerWidth / 2; rowOffset <= fingerWidth / 2; rowOffset++)
                        {
                            int row = startRow + rowOffset;
                            if (row >= 0 && row < board.GetHeight())
                            {
                                float taperChance = (float)(col - waterWidth) / fingerLength;
                                if (Random.value > taperChance * 0.8f)
                                {
                                    if (col < waterWidth + fingerLength / 3)
                                    {
                                        terrainGrid[row, col] = TerrainEnum.shallowWater;
                                    }
                                    else
                                    {
                                        terrainGrid[row, col] = Random.value < 0.7f ?
                                            TerrainEnum.shallowWater : TerrainEnum.shore;
                                    }
                                }
                            }

                            cellsProcessed++;
                        }

                        if (((col - waterWidth) % 3 == 0) || budget.Spent())
                        {
                            float fingerProgress = (float)cellsProcessed / cellsPerFinger;
                            float overallProgress = (fingersProcessed + fingerProgress) / totalFingers;
                            OnGenerationProgress?.Invoke(GetStepProgress(0.4f + overallProgress * 0.3f), "Creating Water Features");
                            yield return null;
                            budget.Reset();
                        }
                    }

                    fingersProcessed++;
                }
                break;
        }
    }

    private IEnumerator GenerateIslandsCoroutine(int seaBorder, int waterWidth, int waterHeight)
    {
        int numIslands = Random.Range(minIslands, maxIslands);
        int totalIslands = numIslands;
        int islandsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        for (int i = 0; i < numIslands; i++)
        {
            int islandRow, islandCol;

            // Determine island position based on the sea border
            switch (seaBorder)
            {
                case 0: // North
                    islandRow = Random.Range(0, waterHeight);
                    islandCol = Random.Range(0, board.GetWidth());
                    break;
                case 1: // East
                    islandRow = Random.Range(0, board.GetHeight());
                    islandCol = Random.Range(board.GetWidth() - waterWidth, board.GetWidth());
                    break;
                case 2: // South
                    islandRow = Random.Range(board.GetHeight() - waterHeight, board.GetHeight());
                    islandCol = Random.Range(0, board.GetWidth());
                    break;
                case 3: // West
                    islandRow = Random.Range(0, board.GetHeight());
                    islandCol = Random.Range(0, waterWidth);
                    break;
                default:
                    islandRow = Random.Range(0, board.GetHeight());
                    islandCol = Random.Range(0, board.GetWidth());
                    break;
            }

            if (terrainGrid[islandRow, islandCol] == TerrainEnum.deepWater ||
                terrainGrid[islandRow, islandCol] == TerrainEnum.shallowWater)
            {
                int islandSize = Random.Range(3, 8);
                Queue<Vector2Int> islandCells = new Queue<Vector2Int>();
                islandCells.Enqueue(new Vector2Int(islandRow, islandCol));

                terrainGrid[islandRow, islandCol] = TerrainEnum.shore;

                int cellsProcessed = 1;
                int iterations = 0;
                int maxIterations = islandSize * 4; // Prevent infinite loops

                while (islandCells.Count > 0 && cellsProcessed < islandSize && iterations < maxIterations)
                {
                    Vector2Int current = islandCells.Dequeue();

                    Vector2Int[] neighbors = (current.x % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                    foreach (Vector2Int dir in neighbors)
                    {
                        int newRow = current.x + dir.x;
                        int newCol = current.y + dir.y;

                        if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth() &&
                            (terrainGrid[newRow, newCol] == TerrainEnum.deepWater ||
                             terrainGrid[newRow, newCol] == TerrainEnum.shallowWater) &&
                            Random.value < 0.6f)
                        {
                            terrainGrid[newRow, newCol] = TerrainEnum.shore;
                            islandCells.Enqueue(new Vector2Int(newRow, newCol));
                            cellsProcessed++;
                            if (cellsProcessed >= islandSize)
                                break;
                        }
                    }

                    iterations++;

                    if ((iterations % 5 == 0) || budget.Spent())
                    {
                        yield return null;
                        budget.Reset();
                    }
                }

                // Add some shallow water around the island
                foreach (Vector2Int cell in GetNeighbors(islandRow, islandCol))
                {
                    if (terrainGrid[cell.x, cell.y] == TerrainEnum.deepWater)
                    {
                        terrainGrid[cell.x, cell.y] = TerrainEnum.shallowWater;
                    }
                }
            }

            islandsProcessed++;
            float islandProgress = (float)islandsProcessed / totalIslands;
            OnGenerationProgress?.Invoke(GetStepProgress(0.7f + islandProgress * 0.3f), "Creating Islands");

            // Yield per island if budget spent
            if (budget.Spent())
            {
                yield return null;
                budget.Reset();
            }
        }
    }

    private IEnumerator GenerateMountainChainsCoroutine()
    {
        int maxChainLength = Mathf.FloorToInt(Mathf.Max(board.GetWidth(), board.GetHeight()) * 0.4f * mountainChainLengthMultiplier);
        int totalChains = mountainChainCount;
        int chainsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        for (int i = 0; i < mountainChainCount; i++)
        {
            int startRow, startCol;
            bool validStart = false;
            int attempts = 0;

            do
            {
                startRow = Random.Range(0, board.GetHeight());
                startCol = Random.Range(0, board.GetWidth());
                if (terrainGrid[startRow, startCol] == TerrainEnum.plains)
                {
                    validStart = true;
                }
                attempts++;
            } while (!validStart && attempts < 50);

            if (!validStart) continue;

            bool xDominant = Random.value > 0.5f;

            int chainLength = Random.Range(maxChainLength / 2, maxChainLength);

            Vector2Int current = new Vector2Int(startRow, startCol);
            terrainGrid[current.x, current.y] = TerrainEnum.mountains;

            yield return null; // small yield after setup

            for (int j = 0; j < chainLength; j++)
            {
                Vector2Int[] neighbors = (current.x % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                Vector2Int nextDir;
                if (xDominant)
                {
                    nextDir = neighbors[Random.value > 0.7f ? Random.Range(0, 6) : Random.Range(1, 4) % 6];
                }
                else
                {
                    nextDir = neighbors[Random.value > 0.7f ? Random.Range(0, 6) : Random.Range(0, 3) * 2];
                }

                Vector2Int next = current + nextDir;

                if (next.x >= 0 && next.x < board.GetHeight() && next.y >= 0 && next.y < board.GetWidth() &&
                    terrainGrid[next.x, next.y] != TerrainEnum.deepWater &&
                    terrainGrid[next.x, next.y] != TerrainEnum.shallowWater)
                {
                    current = next;
                    terrainGrid[current.x, current.y] = TerrainEnum.mountains;
                }

                if ((j % 10 == 0) || budget.Spent())
                {
                    float chainProgress = (float)j / chainLength;
                    float overallProgress = (chainsProcessed + chainProgress) / totalChains;
                    float stepProgress = GetStepProgress(overallProgress);
                    OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Mountains");
                    yield return null;
                    budget.Reset();
                }
            }

            chainsProcessed++;
        }

        // Add hills around mountains
        yield return StartCoroutine(AddHillsAroundMountainsCoroutine());

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Mountains Created");
        currentStep++;
    }

    private IEnumerator GenerateLakesCoroutine()
    {
        int lakesCreated = 0;
        int attempts = 0;
        var budget = new FrameBudget(() => LoadBudgetSeconds);

        while (lakesCreated < lakes && attempts < lakes * 20)
        {
            attempts++;

            int row = Random.Range(0, board.GetHeight());
            int col = Random.Range(0, board.GetWidth());

            if (!CanPlaceLakeCenter(row, col))
                continue;

            int targetSize = Random.Range(4, 8); // 5–10 inclusive

            // Collect potential cells in radius 1–2 to get a roundish cluster
            int radius = 1;
            List<Vector2Int> candidates = GetNeighborsInRadius(row, col, radius);
            List<Vector2Int> validNeighbors = new List<Vector2Int>();

            foreach (var c in candidates)
            {
                if (CanPlaceLakeCell(c.x, c.y))
                    validNeighbors.Add(c);
            }

            // Need at least 2 neighbors: center + 2 = 3 tiles minimum
            if (validNeighbors.Count < 2)
                continue;

            ShuffleList(validNeighbors);

            Vector2Int center = new Vector2Int(row, col);
            lakeCells.Add(center);

            int extra = Mathf.Min(targetSize - 1, validNeighbors.Count);
            for (int i = 0; i < extra; i++)
            {
                lakeCells.Add(validNeighbors[i]);
            }

            // Paint all selected cells as shallowWater
            foreach (var c in lakeCells)
            {
                terrainGrid[c.x, c.y] = TerrainEnum.shallowWater;
            }

            lakesCreated++;

            if (budget.Spent())
            {
                float stepProgress = GetStepProgress((float)lakesCreated / Mathf.Max(1, lakes));
                OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Lakes");
                yield return null;
                budget.Reset();
            }
        }
    }

    private IEnumerator GenerateRiversCoroutine()
    {
        var budget = new FrameBudget(() => LoadBudgetSeconds);

        // 1. Collect possible starting cells: land adjacent to mountains
        List<Vector2Int> potentialStarts = new List<Vector2Int>();

        for (int row = 0; row < board.GetHeight(); row++)
        {
            for (int col = 0; col < board.GetWidth(); col++)
            {
                if (terrainGrid[row, col] != TerrainEnum.mountains)
                    continue;

                Vector2Int[] neighbors = (row % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                foreach (Vector2Int dir in neighbors)
                {
                    int nr = row + dir.x;
                    int nc = col + dir.y;
                    if (!IsInside(nr, nc)) continue;

                    if (IsLandForRiver(nr, nc))
                        potentialStarts.Add(new Vector2Int(nr, nc));
                }
            }
        }

        // No suitable sources, nothing to do
        if (potentialStarts.Count == 0)
            yield break;

        int riversCreated = 0;
        int safety = 0;

        while (riversCreated < rivers && safety < rivers * 4)
        {
            safety++;

            Vector2Int start = potentialStarts[Random.Range(0, potentialStarts.Count)];
            if (!IsLandForRiver(start.x, start.y))
                continue;

            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            Vector2Int current = start;

            // Reasonable upper bound: map perimeter-ish
            int maxLength = board.GetWidth() + board.GetHeight();

            for (int step = 0; step < maxLength; step++)
            {
                if (!IsInside(current.x, current.y))
                    break;

                TerrainEnum t = terrainGrid[current.x, current.y];

                if (t == TerrainEnum.deepWater)
                    break; // reached sea
                if (t == TerrainEnum.shallowWater && step > 0)
                    break; // reached another river / lake / ocean

                // 1-wide river: mark current cell as shallowWater
                terrainGrid[current.x, current.y] = TerrainEnum.shallowWater;
                riverCells.Add(current);  // track river water
                visited.Add(current);

                Vector2Int[] neighbors = (current.x % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                List<Vector2Int> candidates = new List<Vector2Int>();
                foreach (Vector2Int dir in neighbors)
                {
                    int nr = current.x + dir.x;
                    int nc = current.y + dir.y;
                    Vector2Int next = new Vector2Int(nr, nc);

                    if (!IsInside(nr, nc))
                        continue;
                    if (visited.Contains(next))
                        continue;

                    TerrainEnum nt = terrainGrid[nr, nc];

                    // Don't climb mountains; rivers go around them
                    if (nt == TerrainEnum.mountains)
                        continue;

                    candidates.Add(next);
                }

                if (candidates.Count == 0)
                    break;

                // Bias: choose neighbors that move generally toward the seaBorder
                List<Vector2Int> forward = new List<Vector2Int>();
                foreach (var c in candidates)
                {
                    switch (seaBorder)
                    {
                        case 0: // sea to the north – flow northward (row decreasing)
                            if (c.x < current.x) forward.Add(c);
                            break;
                        case 1: // sea to the east – flow eastward (col increasing)
                            if (c.y > current.y) forward.Add(c);
                            break;
                        case 2: // sea to the south – flow southward (row increasing)
                            if (c.x > current.x) forward.Add(c);
                            break;
                        case 3: // sea to the west – flow westward (col decreasing)
                            if (c.y < current.y) forward.Add(c);
                            break;
                    }
                }

                List<Vector2Int> choicePool = forward.Count > 0 ? forward : candidates;
                current = choicePool[Random.Range(0, choicePool.Count)];

                if ((step % 8 == 0) || budget.Spent())
                {
                    float riverProgress = (float)riversCreated / Mathf.Max(1, rivers);
                    float stepProgress = GetStepProgress(riverProgress);
                    OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Rivers");
                    yield return null;
                    budget.Reset();
                }
            }

            riversCreated++;
        }
    }

    private IEnumerator ComputeRiverAndLakeCoastsCoroutine()
    {
        riverCoastHexes.Clear();
        lakeCoastHexes.Clear();

        int height = board.GetHeight();
        int width = board.GetWidth();

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                Vector2Int pos = new Vector2Int(row, col);
                TerrainEnum t = terrainGrid[row, col];

                // Only land can be a coast
                if (t == TerrainEnum.deepWater || t == TerrainEnum.shallowWater)
                    continue;

                Vector2Int[] neighbors = (row % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                bool riverAdj = false;
                bool lakeAdj = false;

                foreach (var dir in neighbors)
                {
                    int nr = row + dir.x;
                    int nc = col + dir.y;
                    Vector2Int nPos = new Vector2Int(nr, nc);

                    if (!IsInside(nr, nc))
                        continue;

                    if (riverCells.Contains(nPos))
                        riverAdj = true;

                    if (lakeCells.Contains(nPos))
                        lakeAdj = true;
                }

                if (riverAdj)
                    riverCoastHexes.Add(pos);

                if (lakeAdj)
                    lakeCoastHexes.Add(pos);

                if (budget.Spent())
                {
                    yield return null;
                    budget.Reset();
                }
            }
        }

        yield break;
    }

    private IEnumerator GenerateRiversAndLakesCoroutine()
    {
        // Lakes first, then rivers
        yield return StartCoroutine(GenerateLakesCoroutine());
        yield return StartCoroutine(GenerateRiversCoroutine());

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Rivers & Lakes Created");
        currentStep++;
    }

    private IEnumerator AddHillsAroundMountainsCoroutine()
    {
        TerrainEnum[,] terrainCopy = new TerrainEnum[board.GetHeight(), board.GetWidth()];
        Array.Copy(terrainGrid, terrainCopy, terrainGrid.Length);

        int totalCells = board.GetHeight() * board.GetWidth();
        int cellsPerBatchLocal = 300;
        int cellsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        for (int row = 0; row < board.GetHeight(); row++)
        {
            for (int col = 0; col < board.GetWidth(); col++)
            {
                if (terrainCopy[row, col] == TerrainEnum.mountains)
                {
                    Vector2Int[] neighbors = (row % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                    foreach (Vector2Int dir in neighbors)
                    {
                        int newRow = row + dir.x;
                        int newCol = col + dir.y;

                        if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth())
                        {
                            if (terrainGrid[newRow, newCol] == TerrainEnum.plains && Random.value < 0.8f)
                            {
                                terrainGrid[newRow, newCol] = TerrainEnum.hills;
                            }
                        }
                    }
                }

                cellsProcessed++;
                if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                {
                    float progress = (float)cellsProcessed / totalCells;
                    float stepProgress = GetStepProgress(progress);
                    OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Hills");
                    yield return null;
                    budget.Reset();
                }
            }
        }
    }

    private IEnumerator GenerateForestsCoroutine()
    {
        for (int i = 0; i < majorForestCount; i++)
        {
            int forestSize = Mathf.FloorToInt(board.GetWidth() * board.GetHeight() * 0.08f); // 8%
            yield return StartCoroutine(GenerateForestPatchCoroutine(forestSize, "Major Forest"));
        }

        for (int i = 0; i < minorForestCount; i++)
        {
            int forestSize = Mathf.FloorToInt(board.GetWidth() * board.GetHeight() * 0.02f); // 2%
            yield return StartCoroutine(GenerateForestPatchCoroutine(forestSize, "Minor Forest"));
        }

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Forests Created");
        currentStep++;
    }

    private IEnumerator GenerateForestPatchCoroutine(int forestSize, string forestType)
    {
        int startRow, startCol;
        bool validStart = false;
        int attempts = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        do
        {
            startRow = Random.Range(0, board.GetHeight());
            startCol = Random.Range(0, board.GetWidth());
            if (terrainGrid[startRow, startCol] != TerrainEnum.deepWater &&
                terrainGrid[startRow, startCol] != TerrainEnum.shallowWater &&
                terrainGrid[startRow, startCol] != TerrainEnum.mountains)
            {
                validStart = true;
            }
            attempts++;
        } while (!validStart && attempts < 50);

        if (!validStart) yield break;

        Queue<Vector2Int> tilesToProcess = new Queue<Vector2Int>();
        tilesToProcess.Enqueue(new Vector2Int(startRow, startCol));
        terrainGrid[startRow, startCol] = TerrainEnum.forest;

        int tilesProcessed = 1;
        int iterations = 0;
        int maxIterations = forestSize * 3;

        while (tilesToProcess.Count > 0 && tilesProcessed < forestSize && iterations < maxIterations)
        {
            Vector2Int current = tilesToProcess.Dequeue();

            Vector2Int[] neighbors = (current.x % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

            // shuffle neighbors
            for (int i = 0; i < neighbors.Length; i++)
            {
                int j = Random.Range(i, neighbors.Length);
                (neighbors[i], neighbors[j]) = (neighbors[j], neighbors[i]);
            }

            foreach (Vector2Int dir in neighbors)
            {
                int newRow = current.x + dir.x;
                int newCol = current.y + dir.y;

                if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth())
                {
                    if (terrainGrid[newRow, newCol] == TerrainEnum.plains)
                    {
                        float distanceRatio = (float)tilesProcessed / forestSize;
                        if (Random.value > distanceRatio)
                        {
                            terrainGrid[newRow, newCol] = TerrainEnum.forest;
                            tilesToProcess.Enqueue(new Vector2Int(newRow, newCol));
                            tilesProcessed++;
                            if (tilesProcessed >= forestSize)
                                break;
                        }
                    }
                }
            }

            iterations++;

            if ((iterations % 20 == 0) || budget.Spent())
            {
                float forestProgress = (float)tilesProcessed / forestSize;
                float stepProgress = GetStepProgress(forestProgress);
                OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), $"Creating {forestType}");
                yield return null;
                budget.Reset();
            }
        }
    }

    private IEnumerator GenerateSwampsCoroutine()
    {
        int totalSwamps = swampCount;
        int swampsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        for (int i = 0; i < swampCount; i++)
        {
            int startRow, startCol;
            bool validStart = false;
            int attempts = 0;
            bool nearWater = false;

            do
            {
                startRow = Random.Range(0, board.GetHeight());
                startCol = Random.Range(0, board.GetWidth());

                if (terrainGrid[startRow, startCol] != TerrainEnum.plains)
                {
                    attempts++;
                    continue;
                }

                validStart = true;

                Vector2Int[] neighbors = (startRow % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;
                foreach (Vector2Int dir in neighbors)
                {
                    int newRow = startRow + dir.x;
                    int newCol = startCol + dir.y;

                    if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth() &&
                        (terrainGrid[newRow, newCol] == TerrainEnum.shallowWater))
                    {
                        nearWater = true;
                        break;
                    }
                }

                attempts++;

            } while ((!validStart || !nearWater) && attempts < 50);

            if (!validStart)
            {
                attempts = 0;
                do
                {
                    startRow = Random.Range(0, board.GetHeight());
                    startCol = Random.Range(0, board.GetWidth());
                    validStart = terrainGrid[startRow, startCol] == TerrainEnum.plains;
                    attempts++;
                } while (!validStart && attempts < 20);

                if (!validStart) continue;
            }

            terrainGrid[startRow, startCol] = TerrainEnum.swamp;

            Vector2Int[] swampNeighbors = (startRow % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

            for (int n = 0; n < swampNeighbors.Length; n++)
            {
                int j = Random.Range(n, swampNeighbors.Length);
                (swampNeighbors[n], swampNeighbors[j]) = (swampNeighbors[j], swampNeighbors[n]);
            }

            int swampTiles = 1;
            foreach (Vector2Int dir in swampNeighbors)
            {
                if (swampTiles >= swampSize)
                    break;

                int newRow = startRow + dir.x;
                int newCol = startCol + dir.y;

                if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth() &&
                    (terrainGrid[newRow, newCol] == TerrainEnum.plains || terrainGrid[newRow, newCol] == TerrainEnum.forest))
                {
                    terrainGrid[newRow, newCol] = TerrainEnum.swamp;
                    swampTiles++;
                }
            }

            swampsProcessed++;
            float swampProgress = (float)swampsProcessed / totalSwamps;
            float stepProgress = GetStepProgress(swampProgress);
            OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Swamps");

            if (budget.Spent())
            {
                yield return null;
                budget.Reset();
            }
        }

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Swamps Created");
        currentStep++;
    }

    private IEnumerator GenerateWastelandsCoroutine()
    {
        for (int i = 0; i < majorWastelandCount; i++)
        {
            int wastelandSize = Mathf.FloorToInt(board.GetWidth() * board.GetHeight() * 0.07f);
            yield return StartCoroutine(GenerateWastelandPatchCoroutine(wastelandSize, true, "Major Wasteland"));
        }

        for (int i = 0; i < minorWastelandCount; i++)
        {
            int wastelandSize = Mathf.FloorToInt(board.GetWidth() * board.GetHeight() * 0.02f);
            yield return StartCoroutine(GenerateWastelandPatchCoroutine(wastelandSize, false, "Minor Wasteland"));
        }

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Wastelands Created");
        currentStep++;
    }

    private IEnumerator GenerateWastelandPatchCoroutine(int wastelandSize, bool isMajor, string wastelandType)
    {
        int startRow, startCol;
        bool validStart = false;
        int attempts = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        if (isMajor)
        {
            do
            {
                startRow = Random.Range(board.GetHeight() / 4, 3 * board.GetHeight() / 4);
                startCol = Random.Range(board.GetWidth() / 4, 3 * board.GetWidth() / 4);
                validStart = (terrainGrid[startRow, startCol] == TerrainEnum.plains ||
                              terrainGrid[startRow, startCol] == TerrainEnum.grasslands);
                attempts++;
            } while (!validStart && attempts < 50);
        }
        else
        {
            do
            {
                startRow = Random.Range(0, board.GetHeight());
                startCol = Random.Range(0, board.GetWidth());

                validStart = (terrainGrid[startRow, startCol] == TerrainEnum.plains ||
                              terrainGrid[startRow, startCol] == TerrainEnum.grasslands);

                if (validStart)
                {
                    foreach (Vector2Int neighbor in GetNeighborsInRadius(startRow, startCol, 4))
                    {
                        if (terrainGrid[neighbor.x, neighbor.y] == TerrainEnum.wastelands)
                        {
                            validStart = false;
                            break;
                        }
                    }
                }

                attempts++;
            } while (!validStart && attempts < 50);
        }

        if (!validStart) yield break;

        terrainGrid[startRow, startCol] = TerrainEnum.wastelands;

        Queue<Vector2Int> expansionCells = new Queue<Vector2Int>();
        expansionCells.Enqueue(new Vector2Int(startRow, startCol));

        int cellsProcessed = 1;
        float noiseScale = 0.15f;
        int noiseSeed = Random.Range(0, 10000);
        int iterations = 0;
        int maxIterations = wastelandSize * 3;

        while (expansionCells.Count > 0 && cellsProcessed < wastelandSize && iterations < maxIterations)
        {
            Vector2Int current = expansionCells.Dequeue();

            Vector2Int[] neighbors = (current.x % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

            for (int n = 0; n < neighbors.Length; n++)
            {
                int j = Random.Range(n, neighbors.Length);
                (neighbors[n], neighbors[j]) = (neighbors[j], neighbors[n]);
            }

            foreach (Vector2Int dir in neighbors)
            {
                int newRow = current.x + dir.x;
                int newCol = current.y + dir.y;

                if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth())
                {
                    if ((terrainGrid[newRow, newCol] == TerrainEnum.plains ||
                         terrainGrid[newRow, newCol] == TerrainEnum.grasslands) &&
                        terrainGrid[newRow, newCol] != TerrainEnum.wastelands)
                    {
                        float noise = Mathf.PerlinNoise((newCol + noiseSeed) * noiseScale, (newRow + noiseSeed) * noiseScale);
                        float expandChance = isMajor ?
                            (0.8f - (float)cellsProcessed / wastelandSize * 0.5f) :
                            0.7f;

                        expandChance *= (0.5f + noise * 0.5f);

                        if (Random.value < expandChance)
                        {
                            terrainGrid[newRow, newCol] = TerrainEnum.wastelands;
                            expansionCells.Enqueue(new Vector2Int(newRow, newCol));
                            cellsProcessed++;
                            if (cellsProcessed >= wastelandSize)
                                break;
                        }
                    }
                }
            }

            iterations++;

            if ((iterations % 20 == 0) || budget.Spent())
            {
                float wastelandProgress = (float)cellsProcessed / wastelandSize;
                float stepProgress = GetStepProgress(wastelandProgress);
                OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), $"Creating {wastelandType}");
                yield return null;
                budget.Reset();
            }
        }
    }

    // Snow only appears on randomly generated boards (authored scenarios paint it directly).
    // Grows roundish patches on cold inland terrain until snowPercentage of the map is covered.
    private IEnumerator GenerateSnowCoroutine()
    {
        int totalCells = board.GetHeight() * board.GetWidth();
        int targetSnow = Mathf.FloorToInt(totalCells * snowPercentage);

        if (targetSnow <= 0)
        {
            OnGenerationProgress?.Invoke(currentStep / totalSteps, "Snow Created");
            currentStep++;
            yield break;
        }

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        int snowPlaced = 0;
        int attempts = 0;
        int maxAttempts = targetSnow * 6 + 50;
        float noiseScale = 0.15f;
        int noiseSeed = Random.Range(0, 10000);

        while (snowPlaced < targetSnow && attempts < maxAttempts)
        {
            attempts++;

            int startRow = Random.Range(0, board.GetHeight());
            int startCol = Random.Range(0, board.GetWidth());

            if (!CanPlaceSnow(startRow, startCol))
                continue;

            // Prefer cold seeds: snow gathers in the far north (row 0 = north) or around mountains.
            if (terrainGrid[startRow, startCol] != TerrainEnum.mountains)
            {
                bool nearMountain = false;
                foreach (Vector2Int n in GetNeighborsInRadius(startRow, startCol, 2))
                {
                    if (terrainGrid[n.x, n.y] == TerrainEnum.mountains)
                    {
                        nearMountain = true;
                        break;
                    }
                }

                // Northness: 1 at the northern edge (row 0), 0 at the southern edge.
                float northness = 1f - (float)startRow / Mathf.Max(1, board.GetHeight() - 1);
                // Always seed next to peaks; otherwise the further north, the more likely.
                // A small floor lets the odd snow field appear in the milder south.
                float acceptChance = nearMountain ? 1f : Mathf.Max(0.05f, northness);
                if (Random.value > acceptChance)
                    continue;
            }

            int patchSize = Random.Range(3, 8);

            Queue<Vector2Int> expansionCells = new Queue<Vector2Int>();
            expansionCells.Enqueue(new Vector2Int(startRow, startCol));
            terrainGrid[startRow, startCol] = TerrainEnum.snow;
            snowPlaced++;

            int cellsInPatch = 1;
            int iterations = 0;
            int maxIterations = patchSize * 3;

            while (expansionCells.Count > 0 && cellsInPatch < patchSize && snowPlaced < targetSnow && iterations < maxIterations)
            {
                Vector2Int current = expansionCells.Dequeue();

                Vector2Int[] neighbors = (current.x % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                for (int n = 0; n < neighbors.Length; n++)
                {
                    int j = Random.Range(n, neighbors.Length);
                    (neighbors[n], neighbors[j]) = (neighbors[j], neighbors[n]);
                }

                foreach (Vector2Int dir in neighbors)
                {
                    int newRow = current.x + dir.x;
                    int newCol = current.y + dir.y;

                    if (!IsInside(newRow, newCol) || !CanPlaceSnow(newRow, newCol))
                        continue;

                    float noise = Mathf.PerlinNoise((newCol + noiseSeed) * noiseScale, (newRow + noiseSeed) * noiseScale);
                    float expandChance = (0.8f - (float)cellsInPatch / patchSize * 0.5f) * (0.5f + noise * 0.5f);

                    if (Random.value < expandChance)
                    {
                        terrainGrid[newRow, newCol] = TerrainEnum.snow;
                        expansionCells.Enqueue(new Vector2Int(newRow, newCol));
                        cellsInPatch++;
                        snowPlaced++;
                        if (cellsInPatch >= patchSize || snowPlaced >= targetSnow)
                            break;
                    }
                }

                iterations++;

                if ((iterations % 20 == 0) || budget.Spent())
                {
                    float stepProgress = GetStepProgress((float)snowPlaced / targetSnow);
                    OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Snow");
                    yield return null;
                    budget.Reset();
                }
            }

            if (budget.Spent())
            {
                yield return null;
                budget.Reset();
            }
        }

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Snow Created");
        currentStep++;
    }

    // Snow can cover cold inland terrain, but not water, coast, desert or swamp.
    private bool CanPlaceSnow(int row, int col)
    {
        if (!IsInside(row, col)) return false;

        TerrainEnum t = terrainGrid[row, col];
        return t == TerrainEnum.plains ||
               t == TerrainEnum.grasslands ||
               t == TerrainEnum.hills ||
               t == TerrainEnum.mountains ||
               t == TerrainEnum.forest;
    }

    private IEnumerator ApplyShoresCoroutine()
    {
        TerrainEnum[,] terrainCopy = new TerrainEnum[board.GetHeight(), board.GetWidth()];
        Array.Copy(terrainGrid, terrainCopy, terrainGrid.Length);

        int totalCells = board.GetHeight() * board.GetWidth();
        int cellsPerBatchLocal = 300;
        int cellsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        for (int row = 0; row < board.GetHeight(); row++)
        {
            for (int col = 0; col < board.GetWidth(); col++)
            {
                if (terrainCopy[row, col] != TerrainEnum.deepWater &&
                    terrainCopy[row, col] != TerrainEnum.shallowWater)
                {
                    bool adjacentToWater = false;

                    Vector2Int[] neighbors = (row % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                    foreach (Vector2Int dir in neighbors)
                    {
                        int newRow = row + dir.x;
                        int newCol = col + dir.y;

                        if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth())
                        {
                            if (terrainCopy[newRow, newCol] == TerrainEnum.shallowWater)
                            {
                                Vector2Int waterPos = new Vector2Int(newRow, newCol);

                                // Only mark as "sea coast" if this shallowWater is NOT river or lake
                                if (!riverCells.Contains(waterPos) && !lakeCells.Contains(waterPos) && !riverCoastHexes.Contains(waterPos) && !lakeCoastHexes.Contains(waterPos))
                                {
                                    adjacentToWater = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (adjacentToWater &&
                        terrainCopy[row, col] != TerrainEnum.mountains &&
                        terrainCopy[row, col] != TerrainEnum.hills &&
                        terrainCopy[row, col] != TerrainEnum.swamp &&
                        terrainCopy[row, col] != TerrainEnum.snow)
                    {
                        if (Random.value < shoreProbability)
                        {
                            terrainGrid[row, col] = TerrainEnum.shore;
                        }
                    }
                }

                cellsProcessed++;
                if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                {
                    float progress = (float)cellsProcessed / totalCells;
                    float stepProgress = GetStepProgress(progress);
                    OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Shores");
                    yield return null;
                    budget.Reset();
                }
            }
        }

        currentStep++;
        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Shores Created");
    }

    private IEnumerator GenerateDesertCoroutine(int desertBorder)
    {
        int desertWidth = Mathf.FloorToInt(board.GetWidth() * desertPercentage);
        int desertHeight = Mathf.FloorToInt(board.GetHeight() * desertPercentage);
        int desertDepth = Mathf.FloorToInt(Mathf.Min(board.GetWidth(), board.GetHeight()) * 0.12f);

        int seedX = Random.Range(0, 10000);
        int seedY = Random.Range(0, 10000);

        int cellsPerBatchLocal = 300;
        int totalCells;
        int cellsProcessed = 0;

        var budget = new FrameBudget(() => LoadBudgetSeconds);

        switch (desertBorder)
        {
            case 0: // North border
                totalCells = desertHeight * board.GetWidth();

                for (int row = 0; row < desertHeight; row++)
                {
                    for (int col = 0; col < board.GetWidth(); col++)
                    {
                        if (terrainGrid[row, col] == TerrainEnum.deepWater ||
                            terrainGrid[row, col] == TerrainEnum.shallowWater ||
                            terrainGrid[row, col] == TerrainEnum.mountains ||
                            terrainGrid[row, col] == TerrainEnum.hills)
                            continue;

                        float noise = Mathf.PerlinNoise((col + seedX) * 0.1f, seedY * 0.1f);
                        int noiseOffset = Mathf.FloorToInt(noise * desertDepth);

                        if (row < desertHeight - noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.desert;
                        }

                        cellsProcessed++;
                        if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                        {
                            float progress = (float)cellsProcessed / totalCells;
                            float stepProgress = GetStepProgress(progress);
                            OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Desert");
                            yield return null;
                            budget.Reset();
                        }
                    }
                }
                break;

            case 1: // East border
                totalCells = board.GetHeight() * desertWidth;

                for (int row = 0; row < board.GetHeight(); row++)
                {
                    for (int col = board.GetWidth() - desertWidth; col < board.GetWidth(); col++)
                    {
                        if (terrainGrid[row, col] == TerrainEnum.deepWater ||
                            terrainGrid[row, col] == TerrainEnum.shallowWater ||
                            terrainGrid[row, col] == TerrainEnum.mountains ||
                            terrainGrid[row, col] == TerrainEnum.hills)
                            continue;

                        float noise = Mathf.PerlinNoise(seedX * 0.1f, (row + seedY) * 0.1f);
                        int noiseOffset = Mathf.FloorToInt(noise * desertDepth);

                        if (col > board.GetWidth() - desertWidth + noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.desert;
                        }

                        cellsProcessed++;
                        if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                        {
                            float progress = (float)cellsProcessed / totalCells;
                            float stepProgress = GetStepProgress(progress);
                            OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Desert");
                            yield return null;
                            budget.Reset();
                        }
                    }
                }
                break;

            case 2: // South border
                totalCells = desertHeight * board.GetWidth();

                for (int row = board.GetHeight() - desertHeight; row < board.GetHeight(); row++)
                {
                    for (int col = 0; col < board.GetWidth(); col++)
                    {
                        if (terrainGrid[row, col] == TerrainEnum.deepWater ||
                            terrainGrid[row, col] == TerrainEnum.shallowWater ||
                            terrainGrid[row, col] == TerrainEnum.mountains ||
                            terrainGrid[row, col] == TerrainEnum.hills)
                            continue;

                        float noise = Mathf.PerlinNoise((col + seedX) * 0.1f, seedY * 0.1f);
                        int noiseOffset = Mathf.FloorToInt(noise * desertDepth);

                        if (row > board.GetHeight() - desertHeight + noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.desert;
                        }

                        cellsProcessed++;
                        if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                        {
                            float progress = (float)cellsProcessed / totalCells;
                            float stepProgress = GetStepProgress(progress);
                            OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Desert");
                            yield return null;
                            budget.Reset();
                        }
                    }
                }
                break;

            case 3: // West border
                totalCells = board.GetHeight() * desertWidth;

                for (int row = 0; row < board.GetHeight(); row++)
                {
                    for (int col = 0; col < desertWidth; col++)
                    {
                        if (terrainGrid[row, col] == TerrainEnum.deepWater ||
                            terrainGrid[row, col] == TerrainEnum.shallowWater ||
                            terrainGrid[row, col] == TerrainEnum.mountains ||
                            terrainGrid[row, col] == TerrainEnum.hills)
                            continue;

                        float noise = Mathf.PerlinNoise(seedX * 0.1f, (row + seedY) * 0.1f);
                        int noiseOffset = Mathf.FloorToInt(noise * desertDepth);

                        if (col < desertWidth - noiseOffset)
                        {
                            terrainGrid[row, col] = TerrainEnum.desert;
                        }

                        cellsProcessed++;
                        if (cellsProcessed % cellsPerBatchLocal == 0 || budget.Spent())
                        {
                            float progress = (float)cellsProcessed / totalCells;
                            float stepProgress = GetStepProgress(progress);
                            OnGenerationProgress?.Invoke(Mathf.Min(stepProgress, (currentStep + 1) / totalSteps), "Creating Desert");
                            yield return null;
                            budget.Reset();
                        }
                    }
                }
                break;
        }

        OnGenerationProgress?.Invoke(currentStep / totalSteps, "Desert Created");
        currentStep++;
    }

    private bool IsInside(int row, int col)
    {
        return row >= 0 && row < board.GetHeight() && col >= 0 && col < board.GetWidth();
    }

    private bool IsLandForRiver(int row, int col)
    {
        TerrainEnum t = terrainGrid[row, col];

        // Rivers cannot start on or flow over deep water, other shallow water, or mountains
        return t != TerrainEnum.deepWater &&
               t != TerrainEnum.shallowWater &&
               t != TerrainEnum.mountains;
    }

    private bool CanPlaceLakeCenter(int row, int col)
    {
        if (!IsInside(row, col)) return false;

        TerrainEnum t = terrainGrid[row, col];

        // Don’t start lakes on existing water or very steep terrain
        if (t == TerrainEnum.deepWater ||
            t == TerrainEnum.shallowWater ||
            t == TerrainEnum.mountains ||
            t == TerrainEnum.hills)
            return false;

        return true;
    }

    private bool CanPlaceLakeCell(int row, int col)
    {
        if (!CanPlaceLakeCenter(row, col))
            return false;

        // Avoid lakes that directly touch deep ocean
        foreach (var n in GetNeighbors(row, col))
        {
            if (terrainGrid[n.x, n.y] == TerrainEnum.deepWater)
                return false;
        }

        return true;
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }


    // A utility method to get all neighbors of a given hex
    private List<Vector2Int> GetNeighbors(int row, int col)
    {
        Vector2Int[] neighbors = (row % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;
        List<Vector2Int> result = new List<Vector2Int>();

        foreach (Vector2Int dir in neighbors)
        {
            int newRow = row + dir.x;
            int newCol = col + dir.y;

            if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth())
            {
                result.Add(new Vector2Int(newRow, newCol));
            }
        }

        return result;
    }

    // Get all neighbors within a certain radius (measured in hex distance)
    private List<Vector2Int> GetNeighborsInRadius(int row, int col, int radius)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2IntWithDistance> queue = new Queue<Vector2IntWithDistance>();

        Vector2Int center = new Vector2Int(row, col);
        queue.Enqueue(new Vector2IntWithDistance(center, 0));
        visited.Add(center);

        while (queue.Count > 0)
        {
            Vector2IntWithDistance current = queue.Dequeue();

            if (current.distance > 0)
            {
                result.Add(current.position);
            }

            if (current.distance >= radius)
                continue;

            Vector2Int[] neighbors = (current.position.x % 2 == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

            foreach (Vector2Int dir in neighbors)
            {
                int newRow = current.position.x + dir.x;
                int newCol = current.position.y + dir.y;
                Vector2Int next = new Vector2Int(newRow, newCol);

                if (newRow >= 0 && newRow < board.GetHeight() && newCol >= 0 && newCol < board.GetWidth() && !visited.Contains(next))
                {
                    visited.Add(next);
                    queue.Enqueue(new Vector2IntWithDistance(next, current.distance + 1));
                }
            }
        }

        return result;
    }

    // Helper struct for tracking distance in the neighbor radius search
    private struct Vector2IntWithDistance
    {
        public Vector2Int position;
        public int distance;

        public Vector2IntWithDistance(Vector2Int position, int distance)
        {
            this.position = position;
            this.distance = distance;
        }
    }
}
