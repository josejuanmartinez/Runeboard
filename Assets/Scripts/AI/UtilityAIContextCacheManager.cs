using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Diagnostics;

public class UtilityAIContextCacheManager : MonoBehaviour
{
    public static UtilityAIContextCacheManager Instance { get; private set; }

    private readonly Dictionary<int, UtilityAIContext.PrecomputedData> cache = new();
    private readonly Dictionary<int, List<(CardData card, float score)>> recommendationCache = new();
    private readonly Queue<(Leader leader, Character character)> workQueue = new();
    private readonly Queue<RecommendationWorkItem> recommendationQueue = new();
    private bool recommendationQueueBuilt;
    private Coroutine precomputeRoutine;
    private Game game;
    private bool rebuildRequested = false;
    private int currentQueueTotal = 0;
    private int currentQueueProcessed = 0;
    private bool queueCompletionLogged = true;
    private Stopwatch queueStopwatch = new();
    private string lastQueueDetail = string.Empty;

    private readonly struct RecommendationWorkItem
    {
        public readonly Leader leader;
        public readonly Character character;
        public readonly CardData card;
        public readonly UtilityAIContext.PrecomputedData precomputed;
        public readonly IReadOnlyList<string> preferredParameters;

        public RecommendationWorkItem(Leader leader, Character character, CardData card, UtilityAIContext.PrecomputedData precomputed, IReadOnlyList<string> preferredParameters)
        {
            this.leader = leader;
            this.character = character;
            this.card = card;
            this.precomputed = precomputed;
            this.preferredParameters = preferredParameters;
        }
    }

    [SerializeField] private float playerFrameBudgetMs = 3f;
    [SerializeField] private float aiFrameBudgetMs = 6f;
    [SerializeField] private int minimumPerFrame = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void BeginPlayerTurnPrecompute(Game contextGame)
    {
        game = contextGame != null ? contextGame : Game.Instance;
        cache.Clear();
        recommendationCache.Clear();
        recommendationQueue.Clear();
        recommendationQueueBuilt = false;
        rebuildRequested = true;
        EnsureRoutine();
    }

    public UtilityAIContext.PrecomputedData? GetCached(Leader leader, Character character)
    {
        if (leader == null || character == null) return null;
        int key = BuildKey(leader, character);
        if (cache.TryGetValue(key, out UtilityAIContext.PrecomputedData data)) return data;
        return null;
    }

    public bool PlayerRecommendationsReady => recommendationQueueBuilt
        && workQueue.Count == 0
        && recommendationQueue.Count == 0;

    public bool TryGetCachedCardSuggestions(Leader leader, Character character, out IReadOnlyList<(CardData card, float score)> suggestions)
    {
        suggestions = null;
        if (leader == null || character == null) return false;
        if (!recommendationCache.TryGetValue(BuildKey(leader, character), out List<(CardData card, float score)> cached)) return false;
        suggestions = cached;
        return PlayerRecommendationsReady;
    }

    public void ClearCache()
    {
        cache.Clear();
        recommendationCache.Clear();
        recommendationQueue.Clear();
        recommendationQueueBuilt = false;
    }

    private void EnsureRoutine()
    {
        if (precomputeRoutine == null)
        {
            precomputeRoutine = StartCoroutine(PrecomputeLoop());
        }
    }

    private IEnumerator PrecomputeLoop()
    {
        Stopwatch stopwatch = new Stopwatch();

        while (true)
        {
            if (game == null) game = Game.Instance;
            if (game == null || !game.started)
            {
                yield return null;
                continue;
            }

            if (rebuildRequested)
            {
                BuildWorkQueue();
                rebuildRequested = false;
            }

            float budgetMs = game.currentlyPlaying == game.player ? playerFrameBudgetMs : aiFrameBudgetMs;

            stopwatch.Restart();
            int processedThisFrame = 0;
            while (workQueue.Count > 0)
            {
                if (processedThisFrame >= minimumPerFrame && stopwatch.Elapsed.TotalMilliseconds >= budgetMs)
                {
                    break;
                }

                (Leader leader, Character character) item = workQueue.Dequeue();
                if (item.leader == null || item.character == null || item.leader.killed || item.character.killed) continue;
                // Clamp per-item build time so a single heavy build cannot stall the frame.
                float perItemBudget = Mathf.Max(0.5f, budgetMs);
                cache[BuildKey(item.leader, item.character)] = UtilityAIContextDataBuilder.Build(item.leader, item.character, perItemBudget);
                processedThisFrame++;
                currentQueueProcessed++;
            }

            if (workQueue.Count == 0 && !recommendationQueueBuilt)
            {
                BuildPlayerRecommendationQueue();
                recommendationQueueBuilt = true;
            }

            ActionsManager recommendationActionsManager = recommendationQueue.Count > 0
                ? ActionsManager.Instance
                : null;
            while (recommendationQueue.Count > 0)
            {
                if (processedThisFrame >= minimumPerFrame && stopwatch.Elapsed.TotalMilliseconds >= budgetMs) break;

                RecommendationWorkItem item = recommendationQueue.Dequeue();
                if (item.leader == null || item.character == null || item.card == null) continue;
                (CardData card, float score)? score = AITurnController.ScoreCard(
                    item.leader,
                    item.character,
                    item.card,
                    recommendationActionsManager,
                    item.precomputed,
                    item.preferredParameters,
                    requirePlayable: false);
                if (score.HasValue)
                {
                    int key = BuildKey(item.leader, item.character);
                    if (!recommendationCache.TryGetValue(key, out List<(CardData card, float score)> scores))
                    {
                        scores = new List<(CardData card, float score)>();
                        recommendationCache[key] = scores;
                    }
                    scores.Add(score.Value);
                }
                processedThisFrame++;
            }

            if (workQueue.Count == 0 && recommendationQueue.Count == 0 && !queueCompletionLogged && currentQueueTotal > 0)
            {
                foreach (List<(CardData card, float score)> scores in recommendationCache.Values)
                    scores.Sort((a, b) => b.score.CompareTo(a.score));
                queueCompletionLogged = true;
                queueStopwatch.Stop();
                UnityEngine.Debug.Log($"[AIContextCache] Completed caching {currentQueueProcessed}/{currentQueueTotal} items in {queueStopwatch.Elapsed.TotalMilliseconds:F1} ms (turn {game?.turn}, active={game?.currentlyPlaying?.characterName ?? "?"}); items: {lastQueueDetail}");
            }

            yield return null;
        }
    }

    private void BuildPlayerRecommendationQueue()
    {
        if (game?.player == null) return;
        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        if (deckManager == null || !deckManager.HasDeckFor(game.player)) return;

        foreach (Character character in game.player.controlledCharacters.Where(c => c != null && !c.killed))
        {
            int key = BuildKey(game.player, character);
            if (!cache.TryGetValue(key, out UtilityAIContext.PrecomputedData precomputed)) continue;

            CharacterBlackboard blackboard = CharacterBlackboardStore.GetOrCreate(game.player, character);
            (_, IReadOnlyList<string> preferredParameters, _) = AITurnController.AdvanceHtnStrategy(game.player, character, blackboard);
            recommendationCache[key] = new List<(CardData card, float score)>();
            foreach (CardData card in deckManager.GetFullDeck(game.player))
            {
                if (card == null || card.IsEncounterCard()) continue;
                recommendationQueue.Enqueue(new RecommendationWorkItem(
                    game.player, character, card, precomputed, preferredParameters));
            }
        }
    }

    private void BuildWorkQueue()
    {
        workQueue.Clear();
        if (game == null) game = Game.Instance;
        if (game == null) return;

        if (game.competitors == null) return;

        List<string> detailItems = new();

        // NPCs first: the human-aligned group can begin as soon as the player window opens, so
        // queuing them last would make them the group most likely to hit a cache miss.
        if (game.npcs != null)
        {
            foreach (NonPlayableLeader leader in game.npcs.Where(c => c != null && !c.killed))
            {
                foreach (Character character in leader.controlledCharacters.Where(c => c != null && !c.killed))
                {
                    workQueue.Enqueue((leader, character));
                    string leaderName = !string.IsNullOrEmpty(leader.characterName) ? leader.characterName : leader.name;
                    string charName = !string.IsNullOrEmpty(character.characterName) ? character.characterName : character.name;
                    detailItems.Add($"{leaderName}/{charName}");
                }
            }
        }

        foreach (PlayableLeader leader in game.competitors.Where(c => c != null && !c.killed))
        {
            foreach (Character character in leader.controlledCharacters.Where(c => c != null && !c.killed))
            {
                workQueue.Enqueue((leader, character));
                string leaderName = !string.IsNullOrEmpty(leader.characterName) ? leader.characterName : leader.name;
                string charName = !string.IsNullOrEmpty(character.characterName) ? character.characterName : character.name;
                detailItems.Add($"{leaderName}/{charName}");
            }
        }

        currentQueueTotal = workQueue.Count;
        currentQueueProcessed = 0;
        queueCompletionLogged = false;
        queueStopwatch.Restart();
        lastQueueDetail = detailItems.Count > 0 ? string.Join(", ", detailItems) : "none";
        UnityEngine.Debug.Log($"[AIContextCache] Queued {currentQueueTotal} items for caching (turn {game?.turn}, active={game?.currentlyPlaying?.characterName ?? "?"}); items: {lastQueueDetail}");
    }

    private int BuildKey(Leader leader, Character character)
    {
        unchecked
        {
            return (leader.GetEntityId().GetHashCode() * 397) ^ character.GetEntityId().GetHashCode();
        }
    }
}
