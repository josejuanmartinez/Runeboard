using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

public static class AITurnController
{
    private static bool leaderTurnInProgress;
    public static Leader CurrentExecutingLeader { get; private set; }

    public static IEnumerator ExecuteLeaderTurn(Leader leader, bool presentChosenCards = false, bool skipAlreadyActionedCharacters = false)
    {
        if (leader == null) yield break;

        // Playable and alignment-matched NPL turns can be scheduled in the same window, but
        // ActionsManager caches mutable action instances. Time-slice whole leader turns so those
        // shared instances are never driven by two coroutines at once.
        while (leaderTurnInProgress) yield return null;
        leaderTurnInProgress = true;
        CurrentExecutingLeader = leader;

        try
        {
            Game game = Game.Instance;
            game?.RefreshPlayerControlState();

            ActionsManager actionsManager = ActionsManager.Instance;
            if (actionsManager == null)
            {
                Debug.LogWarning("AI could not find ActionsManager. Skipping AI turn.");
                yield break;
            }

        Task economyCardsTask = ConsumeAiResourceCardsAsync(leader, actionsManager, presentChosenCards);
        while (!economyCardsTask.IsCompleted) yield return null;
        if (economyCardsTask.IsFaulted && economyCardsTask.Exception != null)
        {
            Debug.LogException(economyCardsTask.Exception);
        }

        foreach (Character character in leader.controlledCharacters.Where(c => c != null && !c.killed))
        {
            if (skipAlreadyActionedCharacters && character.hasActionedThisTurn) continue;
            game?.RefreshPlayerControlState();
            // HTN strategy is decided per character, from that character's own local situation
            // (its own UtilityAIContext) — not once for the whole nation from an arbitrary
            // "first" character. A leader-wide gate (e.g. Economic.Critical) still ends up
            // uniform across characters for free, since that predicate doesn't depend on
            // position; a position-dependent one (Militaristic.Danger, an opportunity nearby)
            // now only biases the character it's actually about.
            CharacterBlackboard blackboard = CharacterBlackboardStore.GetOrCreate(leader, character);
            (string activeHtnTaskId, IReadOnlyList<string> preferredParameters, Hex activeHtnTargetHex) = AdvanceHtnStrategy(leader, character, blackboard);

            Task task = ExecuteCharacterAsync(leader, character, actionsManager, activeHtnTaskId, preferredParameters, activeHtnTargetHex, presentChosenCards);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted && task.Exception != null)
            {
                Debug.LogException(task.Exception);
            }
        }

            if (!IsBackgroundAiDuringHumanTurn(leader)) actionsManager.Hide();
        }
        finally
        {
            CurrentExecutingLeader = null;
            leaderTurnInProgress = false;
        }
    }

    // Once per character-turn: re-evaluate that character's own persistent HTN strategy
    // (priority-interrupt scan, failure check, completion-driven advancement — see
    // HTNPlanner), using a UtilityAIContext built from THIS character's own position, and
    // return the taskId + preferred utility parameters its currently-active primitive task
    // biases scoring toward (null/empty for a neutral, no-bias fallback task).
    // Public so AIBlackboardDebugPanel can force a fresh evaluation for a character the AI
    // turn loop hasn't reached yet (e.g. inspecting mid-round, before its turn comes up).
    public static (string taskId, IReadOnlyList<string> preferredParameters, Hex targetHex) AdvanceHtnStrategy(Leader leader, Character character, CharacterBlackboard blackboard)
    {
        UtilityAIContext.PrecomputedData? precomputed = UtilityAIContextCacheManager.Instance != null
            ? UtilityAIContextCacheManager.Instance.GetCached(leader, character)
            : null;
        UtilityAIContext sensingContext = new(leader, character, new List<CharacterAction>(), null, precomputed);

        HTNCompoundTask strategyRoot = AIStrategyLibrary.GetStrategyFor(leader);
        List<HTNStackFrame> previousStack = blackboard.ActiveStack;
        List<HTNStackFrame> newStack = HTNPlanner.AdvanceStack(previousStack, strategyRoot, sensingContext, blackboard);

        bool changed = !StacksEqual(previousStack, newStack);
        blackboard.ActiveStack = newStack;
        blackboard.TurnsOnCurrentTask = changed ? 0 : blackboard.TurnsOnCurrentTask + 1;

        HTNPrimitiveTask activePrimitive = HTNPlanner.ResolveActivePrimitive(newStack, strategyRoot);

        // The specific hex behind whichever preferred parameter actually resolves to one —
        // takes the first that does, since a leaf's PreferredParameters are usually either all
        // about the same location (e.g. attack's MilitaryEdge + EnemyPressure both point at the
        // same enemy hex) or, when they're not location-based at all (Economic, generic
        // fallback), none of them resolve and the target stays null.
        Hex targetHex = activePrimitive?.PreferredParameters?
            .Select(sensingContext.GetTargetHexForParameter)
            .FirstOrDefault(h => h != null);
        blackboard.TargetHex = targetHex;

        string stackDescription = string.Join(">", newStack.Select(f => $"{f.MethodTaskId}[{f.SubtaskIndex}]"));
        string preferredParamsDescription = activePrimitive?.PreferredParameters is { Count: > 0 } ? string.Join(",", activePrimitive.PreferredParameters) : "(none)";
        Debug.Log($"[HTN] {leader.characterName}/{character.characterName} difficulty={AIDifficultySettings.CurrentDifficulty} turnsOnTask={blackboard.TurnsOnCurrentTask} stack={stackDescription} preferredParams={preferredParamsDescription} targetHex={targetHex?.GetHoverV2()}");

        return (activePrimitive?.TaskId, activePrimitive?.PreferredParameters, targetHex);
    }

    private static bool StacksEqual(List<HTNStackFrame> a, List<HTNStackFrame> b)
    {
        if (a == null || b == null) return a == b;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].MethodTaskId != b[i].MethodTaskId || a[i].SubtaskIndex != b[i].SubtaskIndex) return false;
        }
        return true;
    }

    private static async Task ConsumeAiResourceCardsAsync(Leader leader, ActionsManager actionsManager, bool presentChosenCards)
    {
        if (leader == null || actionsManager == null) return;

        DeckManager deckManager = DeckManager.Instance != null
            ? DeckManager.Instance
            : DeckManager.Instance;
        if (deckManager == null || !deckManager.HasDeckFor(leader)) return;

        Character actor = leader.controlledCharacters.FirstOrDefault(c => c != null && !c.killed);
        if (actor == null) return;

        List<CardData> resourceCards = deckManager.GetFullDeck(leader)
            .Where(card => card != null && (card.GetCardType() == CardTypeEnum.Land || card.GetCardType() == CardTypeEnum.PC))
            .ToList();

        foreach (CardData card in resourceCards)
        {
            if (card == null) continue;
            if (!card.EvaluatePlayability(actor)) continue;
            if (!deckManager.TryConsumeCard(leader, card.name, out CardData consumedCard)) continue;
            if (presentChosenCards) await PresentChosenCardAsync(leader, actor, consumedCard ?? card);
            bool succeeded = await ExecuteCardEffectForAiAsync(consumedCard, actor, actionsManager);
            if (succeeded)
            {
                deckManager.ApplyMapRevealForPlayedCard(leader, consumedCard);
            }
        }
    }

    private static async Task<bool> ExecuteCardEffectForAiAsync(CardData card, Character actor, ActionsManager actionsManager)
    {
        if (card == null || actor == null || actionsManager == null) return false;

        string actionRef = NormalizeActionRef(card.GetActionRef());
        if (string.IsNullOrWhiteSpace(actionRef)) return false;

        CharacterAction action = ResolveActionByRef(actionRef, actionsManager);
        if (action == null) return false;

        action.Initialize(actor, card, condition: null, effect: null, asyncEffect: null);
        action.difficulty = Mathf.Max(0, card.difficulty);

        bool ok = true;
        if (action.effect != null) ok = action.effect(actor);
        if (ok && action.asyncEffect != null) ok = await action.asyncEffect(actor);
        return ok;
    }

    // Public: also used by UtilityAIContext.HasEligibleCard, which needs the same actionRef ->
    // CharacterAction resolution to check role eligibility across the leader's full deck.
    public static CharacterAction ResolveActionByRef(string actionRef, ActionsManager actionsManager = null)
    {
        string normalizedActionRef = NormalizeActionRef(actionRef);
        if (string.IsNullOrWhiteSpace(normalizedActionRef)) return null;

        if (actionsManager == null)
        {
            actionsManager = ActionsManager.Instance;
        }

        return actionsManager != null ? actionsManager.ResolveActionByRef(normalizedActionRef) : null;
    }

    public static string NormalizeActionRef(string actionRef)
    {
        if (string.IsNullOrWhiteSpace(actionRef)) return string.Empty;

        string normalized = actionRef.Trim();
        if (normalized.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - 3).Trim();
        }

        int lastDotIndex = normalized.LastIndexOf('.');
        if (lastDotIndex >= 0 && lastDotIndex < normalized.Length - 1)
        {
            normalized = normalized.Substring(lastDotIndex + 1).Trim();
        }

        return normalized;
    }

    private static bool ActionTypeMatchesRef(System.Type candidateType, string normalizedActionRef)
    {
        if (candidateType == null || string.IsNullOrWhiteSpace(normalizedActionRef)) return false;

        if (string.Equals(candidateType.Name, normalizedActionRef, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(candidateType.FullName)
            && string.Equals(candidateType.FullName, normalizedActionRef, System.StringComparison.OrdinalIgnoreCase);
    }

    private static Type ResolveActionType(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return null;

        Type direct = Type.GetType(className, false, true);
        if (direct != null) return direct;

        foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            Type candidate = assembly.GetType(className, false, true);
            if (candidate != null) return candidate;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            candidate = types.FirstOrDefault(t =>
                string.Equals(t.Name, className, System.StringComparison.OrdinalIgnoreCase));
            if (candidate != null) return candidate;
        }

        return null;
    }

    // Plays up to AIDifficulty cards for this character this turn, each the single best-scoring
    // card across the leader's entire deck (never just the hand — see DeckManager.GetFullDeck).
    // Re-scores from scratch each pick since executing a card changes affordability/available
    // cards for the next one. Stops early on an empty candidate pool or a failed execution;
    // falls back to a single Pass if literally nothing was played.
    private static async Task ExecuteCharacterAsync(Leader leader, Character character, ActionsManager actionsManager, string activeHtnTaskId, IReadOnlyList<string> preferredParameters, Hex activeHtnTargetHex, bool presentChosenCards)
    {
        // This flag drives only the human one-action-per-character flow. AI characters are
        // governed exclusively by picksRemaining below (the configured difficulty limit).
        character.hasActionedThisTurn = false;

        // Coarse per-character phase timing — logged once at the end of this method. Not a
        // profiler, just enough to tell (from the console) which phase a slow AI turn is
        // actually spending its time in, since that's otherwise invisible from outside Unity.
        System.Diagnostics.Stopwatch totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        double moveMs = 0, scoreMs = 0, executeMs = 0;

        DeckManager deckManager = DeckManager.Instance != null
            ? DeckManager.Instance
            : DeckManager.Instance;

        // If the active strategy has a specific place for this character and it isn't already
        // there, travel first — scoring/acting from the wrong hex this turn would just be
        // evaluating the wrong situation (e.g. FortifyPC needs to already be at the PC in
        // danger). Already at the target (or no specific target this turn) skips straight to
        // acting, same as before.
        bool movedFirst = false;
        if (activeHtnTargetHex != null && character.hex != activeHtnTargetHex)
        {
            System.Diagnostics.Stopwatch moveStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await MoveCharacterTowardsHexAsync(character, activeHtnTargetHex);
            moveMs += moveStopwatch.Elapsed.TotalMilliseconds;
            movedFirst = true;
        }

        // Moving invalidates UtilityAIContextCacheManager's cached proximity data (built from
        // this character's pre-move position) — rebuild fresh rather than score off a stale hex.
        // Also covers a cold cache (GetCached returns null before the manager has warmed up):
        // guaranteeing a non-null value here, once, means ScoreFullDeck's own fallback build
        // never has to re-run the O(board-hexes) scan on every one of the picks below.
        UtilityAIContext.PrecomputedData? precomputed = movedFirst
            ? UtilityAIContextDataBuilder.Build(leader, character)
            : (UtilityAIContextCacheManager.Instance != null ? UtilityAIContextCacheManager.Instance.GetCached(leader, character) : null);
        precomputed ??= UtilityAIContextDataBuilder.Build(leader, character);

        int picksRemaining = (int)AIDifficultySettings.CurrentDifficulty;
        HashSet<CardData> playedThisCharacter = new();
        UtilityAIContext lastContext = null;

        // Materialized once per character-turn — the leader's deck composition doesn't change
        // across picks within this loop (only playedThisCharacter grows), so there's no need
        // for DeckManager to reallocate its drawPile+hand concat list on every pick.
        IReadOnlyList<CardData> fullDeck = deckManager?.GetFullDeck(leader);

        while (picksRemaining > 0 && deckManager != null && deckManager.HasDeckFor(leader))
        {
            System.Diagnostics.Stopwatch scoreStopwatch = System.Diagnostics.Stopwatch.StartNew();
            List<(CardData card, float score)> scoredCards = await ScoreFullDeckAsync(leader, character, actionsManager, deckManager, precomputed, playedThisCharacter, preferredParameters, fullDeck);
            scoreMs += scoreStopwatch.Elapsed.TotalMilliseconds;
            // Pick randomly among whichever card(s) tied for the top score instead of always
            // favoring whichever happened to come first in the full-deck scan.
            CardData chosenCard = UtilityAI.PickRandomAmongTopScored(scoredCards, scored => scored.score).card;
            if (chosenCard == null) break;

            if (presentChosenCards) await PresentChosenCardAsync(leader, character, chosenCard);
            System.Diagnostics.Stopwatch executeStopwatch = System.Diagnostics.Stopwatch.StartNew();
            UtilityAIContext context = await ExecuteChosenCardAsync(leader, character, actionsManager, precomputed, chosenCard, activeHtnTaskId, activeHtnTargetHex);
            executeMs += executeStopwatch.Elapsed.TotalMilliseconds;
            Game.Instance?.RefreshPlayerControlState();
            character.hasActionedThisTurn = false;
            lastContext = context;

            bool shouldLog = context.LastChosenAction == null || context.LastChosenAction.LastExecutionSucceeded;
            if (shouldLog) AIActionLogger.Log(context.BuildLogEntry());

            if (context.LastChosenAction == null || !context.LastChosenAction.LastExecutionSucceeded) break;

            playedThisCharacter.Add(chosenCard);
            picksRemaining--;
        }

        if (lastContext == null)
        {
            lastContext = new UtilityAIContext(leader, character, new List<CharacterAction>(), null, precomputed) { ActiveHtnTaskId = activeHtnTaskId, ActiveHtnTargetHex = activeHtnTargetHex };
            await lastContext.PassAsync();
            AIActionLogger.Log(lastContext.BuildLogEntry());
        }

        // Already moved toward the active target before acting above — don't also chase
        // afterward (movement allowance is spent anyway; MoveCharacterTowardsHexAsync would
        // just no-op). Only the "already at target / no specific target" path still falls
        // through to the old post-action opportunistic chase.
        if (!movedFirst)
        {
            System.Diagnostics.Stopwatch chaseStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await MoveTowardsTargetAsync(lastContext);
            moveMs += chaseStopwatch.Elapsed.TotalMilliseconds;
        }
        if (!IsBackgroundAiDuringHumanTurn(leader)) actionsManager.Hide();

        double totalMs = totalStopwatch.Elapsed.TotalMilliseconds;
        if (totalMs >= 500)
        {
            Debug.Log($"[AITiming] {leader.characterName}/{character.characterName} total={totalMs:F0}ms score={scoreMs:F0}ms execute={executeMs:F0}ms move={moveMs:F0}ms");
        }
    }

    private static async Task PresentChosenCardAsync(Leader leader, Character character, CardData card)
    {
        if (leader == null || character == null || card == null) return;

        Game game = Game.Instance;
        if (game == null || game.player != leader || !game.PlayerAutoplayEnabled) return;

        Board board = UtilityAIContext.GetSharedBoard();
        if (board != null && character.hex != null && !character.hex.IsHidden())
        {
            board.SelectCharacter(character, true, 0.45f, 0f);
        }

        CardCenterPreview preview = CardCenterPreview.Instance != null
            ? CardCenterPreview.Instance
            : UnityEngine.Object.FindFirstObjectByType<CardCenterPreview>();
        await CenterDisplayLock.WaitAsync();
        try
        {
            if (!game.PlayerAutoplayEnabled) return;
            preview?.ShowPreview(card, speedMultiplier: 1.35f, hoverDriven: false);
            await Task.Delay(1200);
        }
        finally
        {
            preview?.HidePreview();
            CenterDisplayLock.Release();
        }
    }

    // Re-resolves and re-Initializes the action fresh right before executing — the action
    // instance returned by ResolveActionByRef is a shared singleton per action *type*
    // (ActionsManager caches one CharacterAction component per class), so it must never be
    // reused across candidates without a fresh Initialize immediately before each use.
    // Public: also reused by AIBlackboardDebugPanel's "Play Card" cheat-engine control to
    // force-play an operator-chosen card outside the normal AI pick loop, through the exact
    // same execution path (deck consumption, map reveal, environmental bookkeeping) a real AI
    // pick takes.
    public static async Task<UtilityAIContext> ExecuteChosenCardAsync(Leader leader, Character character, ActionsManager actionsManager, UtilityAIContext.PrecomputedData? precomputed, CardData chosenCard, string activeHtnTaskId, Hex activeHtnTargetHex)
    {
        if (chosenCard != null)
        {
            string actionRef = NormalizeActionRef(chosenCard.GetActionRef());
            CharacterAction action = ResolveActionByRef(actionRef, actionsManager);
            if (action != null)
            {
                action.Initialize(character, chosenCard);
                Dictionary<CharacterAction, CardData> actionCards = new() { [action] = chosenCard };
                UtilityAIContext context = new(leader, character, new List<CharacterAction> { action }, actionCards, precomputed) { ActiveHtnTaskId = activeHtnTaskId, ActiveHtnTargetHex = activeHtnTargetHex };
                bool executed = await context.TryExecuteChosenActionAsync(action);
                if (!executed) await context.PassAsync();
                return context;
            }
        }

        UtilityAIContext passContext = new(leader, character, new List<CharacterAction>(), null, precomputed) { ActiveHtnTaskId = activeHtnTaskId, ActiveHtnTargetHex = activeHtnTargetHex };
        await passContext.PassAsync();
        return passContext;
    }

    // Scores every playable card in the leader's full deck (not just the drawn hand) for this
    // character — one candidate at a time, each freshly Initialized immediately before it's
    // scored via a throwaway single-card UtilityAIContext, since scoring depends on the shared
    // action singleton's current card/character state. preferredParameters lets the HTN layer
    // tilt the ranking toward its currently-active strategy's specific situation, purely via
    // whether a card's own utilityParameters overlap it (see UtilityAIContext.ScoreAction) —
    // without restricting which cards are eligible (see AIDifficulty loop in
    // ExecuteLeaderTurn). Public: also reused read-only by AIBlackboardDebugPanel for its live
    // "cards that would be suitable" preview — scoring itself never executes/mutates anything,
    // so calling it outside a real AI turn is safe.
    // fullDeck: pass the leader's already-materialized full deck to avoid DeckManager
    // reallocating its drawPile+hand concat list on every pick of the same character-turn
    // (see ExecuteCharacterAsync, which fetches it once before its picksRemaining loop).
    // Falls back to a fresh fetch for callers scoring a single one-off snapshot (e.g.
    // AIBlackboardDebugPanel's live preview).
    public static List<(CardData card, float score)> ScoreFullDeck(Leader leader, Character character, ActionsManager actionsManager, DeckManager deckManager, UtilityAIContext.PrecomputedData? precomputed, HashSet<CardData> excluded, IReadOnlyList<string> preferredParameters, IReadOnlyList<CardData> fullDeck = null)
    {
        List<(CardData card, float score)> scored = new();
        if (leader == null || character == null || actionsManager == null || deckManager == null) return scored;

        // A cache entry may not be ready yet during the human player's movement. Without this
        // fallback, every temporary per-card UtilityAIContext rebuilds the entire world-state
        // snapshot synchronously, making the game appear frozen before suggestions open.
        precomputed ??= UtilityAIContextDataBuilder.Build(leader, character);

        foreach (CardData card in fullDeck ?? deckManager.GetFullDeck(leader))
        {
            if (card == null || card.IsEncounterCard()) continue;
            if (excluded != null && excluded.Contains(card)) continue;
            (CardData card, float score)? cardScore = ScoreCard(
                leader, character, card, actionsManager, precomputed, preferredParameters, requirePlayable: true);
            if (cardScore.HasValue) scored.Add(cardScore.Value);
        }

        return scored;
    }

    // Per-frame time budget for ScoreFullDeckAsync below — same frame-budget-and-yield
    // technique UtilityAIContextCacheManager.PrecomputeLoop uses for the player-facing
    // recommendation cache, applied here to the AI's own live turn scoring.
    private const float ScoringFrameBudgetMs = 6f;

    // Async twin of ScoreFullDeck used by the live AI turn loop (ExecuteCharacterAsync).
    // Scoring a full deck (~100-150 cards) synchronously in one call blocks the main thread
    // for its entire duration — spread across 4 picks per character and every character/leader
    // in play, that reads as a freeze even once the per-card cost itself is cheap. Yielding
    // every ScoringFrameBudgetMs lets Unity render a frame in between chunks instead.
    public static async Task<List<(CardData card, float score)>> ScoreFullDeckAsync(Leader leader, Character character, ActionsManager actionsManager, DeckManager deckManager, UtilityAIContext.PrecomputedData? precomputed, HashSet<CardData> excluded, IReadOnlyList<string> preferredParameters, IReadOnlyList<CardData> fullDeck)
    {
        List<(CardData card, float score)> scored = new();
        if (leader == null || character == null || actionsManager == null || deckManager == null) return scored;

        precomputed ??= UtilityAIContextDataBuilder.Build(leader, character);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        foreach (CardData card in fullDeck ?? deckManager.GetFullDeck(leader))
        {
            if (card == null || card.IsEncounterCard()) continue;
            if (excluded != null && excluded.Contains(card)) continue;
            (CardData card, float score)? cardScore = ScoreCard(
                leader, character, card, actionsManager, precomputed, preferredParameters, requirePlayable: true);
            if (cardScore.HasValue) scored.Add(cardScore.Value);

            if (stopwatch.Elapsed.TotalMilliseconds >= ScoringFrameBudgetMs)
            {
                await Task.Yield();
                stopwatch.Restart();
            }
        }

        return scored;
    }

    // Scores one card without executing it. The turn-start recommendation cache calls this
    // with requirePlayable=false so location-sensitive cards are still ranked before movement;
    // DeckManager applies the exact current playability check immediately before presentation.
    public static (CardData card, float score)? ScoreCard(
        Leader leader,
        Character character,
        CardData card,
        ActionsManager actionsManager,
        UtilityAIContext.PrecomputedData? precomputed,
        IReadOnlyList<string> preferredParameters,
        bool requirePlayable)
    {
        if (leader == null || character == null || card == null || actionsManager == null || card.IsEncounterCard()) return null;

        string actionRef = NormalizeActionRef(card.GetActionRef());
        if (string.IsNullOrWhiteSpace(actionRef)) return null;

        CharacterAction action = ResolveActionByRef(actionRef, actionsManager);
        if (action == null) return null;
        action.Initialize(character, card);

        if (requirePlayable && !card.EvaluatePlayability(character, null, _ => action.FulfillsConditions())) return null;

        Dictionary<CharacterAction, CardData> actionCards = new() { [action] = card };
        UtilityAIContext scoringContext = new(
            leader,
            character,
            new List<CharacterAction> { action },
            actionCards,
            precomputed,
            captureExecutionSnapshot: false);
        return (card, scoringContext.ScoreAction(action, preferredParameters));
    }

    private static async Task MoveTowardsTargetAsync(UtilityAIContext context)
    {
        if (context == null || context.Character == null) return;
        await MoveCharacterTowardsHexAsync(context.Character, context.GetPreferredMovementTarget());
    }

    // Shared movement mechanic: paths toward target and moves as far as the character's
    // remaining allowance permits (may not arrive in one call). No-ops if already there, out of
    // movement, or missing a target — same guards regardless of whether this is called before
    // acting (see ExecuteCharacterAsync) or after (the old, still-used fallback-chase path).
    private static async Task MoveCharacterTowardsHexAsync(Character character, Hex target)
    {
        if (character == null) return;
        if (character.moved >= character.GetMaxMovement()) return;
        if (target == null || target == character.hex) return;

        Board board = UtilityAIContext.GetSharedBoard();
        if (board == null) return;

        // DrawPathBetweenHexes runs a full A* pathfind purely to draw the path line + per-hex
        // movement-cost bubbles — Board.Move (below) pathfinds the exact same route again for
        // the actual move. That's a genuine double pathfinding cost, and HexPathRenderer's A*
        // is an unindexed linear-scan implementation (List.Contains/Remove per node, no
        // priority queue), so it's not cheap to begin with. Only worth paying for when a human
        // is actually watching this character move — during another leader's turn (the vast
        // majority of AI movement) nothing here is ever rendered.
        Game game = Game.Instance;
        bool showToPlayer = game != null && game.IsPlayerCurrentlyPlaying() && game.player == character.GetOwner();
        if (showToPlayer)
        {
            HexPathRenderer pathRenderer = UnityEngine.Object.FindFirstObjectByType<HexPathRenderer>();
            pathRenderer?.DrawPathBetweenHexes(character.hex.v2, target.v2, character);
        }

        board.Move(character, target.v2);

        // Wait until movement finishes
        int safety = 0;
        while (board.moving && safety < 200)
        {
            await Task.Delay(50);
            safety++;
        }
    }

    private static bool IsBackgroundAiDuringHumanTurn(Leader leader)
    {
        Game game = Game.Instance;
        return game != null && game.currentlyPlaying == game.player && leader != null && leader != game.player;
    }
}
