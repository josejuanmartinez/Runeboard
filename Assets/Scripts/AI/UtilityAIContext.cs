using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class UtilityAIContext
{
    // Board is instantiated once at game start and lives for the whole session. Resolving it
    // via FindAnyObjectByType inside this constructor used to happen once per scored card —
    // thousands of scene-wide lookups per AI turn (same anti-pattern as the earlier hex-load
    // freeze, see Hex.cs's sharedBoard). Cache it statically instead; Unity's overloaded
    // null-check re-resolves it if the scene reloads and the old reference is destroyed.
    private static Board sharedBoard;
    public static Board GetSharedBoard()
    {
        if (sharedBoard == null) sharedBoard = Board.Instance;
        return sharedBoard;
    }

    private readonly Board board;
    private readonly List<AIScoredAction> scoredActions = new();
    private readonly List<ArtifactTransferCandidate> artifactTransferCandidates = new();
    private readonly HashSet<string> scoredActionKeys = new();
    private readonly Dictionary<CharacterAction, CardData> actionCardsByAction = new();
    private readonly PrecomputedData? _precomputed;
    private ResourceSnapshot preSnapshot;
    private Dictionary<PlayableLeader, int> preVictoryPoints;

    public Leader Leader { get; }
    public Character Character { get; }
    public List<CharacterAction> AvailableActions { get; }
    public EconomyStatus EconomyStatus { get; }

    private EnemyTarget closestEnemy;
    private EnemyTarget closestNonNeutralEnemy;
    private float nearestUnrevealedNpcDistance = float.MaxValue;
    private Hex nearestUnrevealedNpcHex = null;
    private float nearestEnemyCharacterDistance = float.MaxValue;
    private Hex nearestEnemyCharacterHex = null;
    private float nearestEnemyPcOpportunityDistance = float.MaxValue;
    private Hex nearestEnemyPcOpportunityHex = null;
    private float nearestOwnPcLoyaltyRiskDistance = float.MaxValue;
    private Hex nearestOwnPcLoyaltyRiskHex = null;
    private float nearestEnemyPcVulnerabilityDistance = float.MaxValue;
    private Hex nearestEnemyPcVulnerabilityHex = null;
    private float nearestHighValueEnemyCharacterDistance = float.MaxValue;
    private Hex nearestHighValueEnemyCharacterHex = null;
    private float nearestOwnPcFortificationNeedDistance = float.MaxValue;
    private Hex nearestOwnPcFortificationNeedHex = null;
    private float nearestNplRecruitmentDistance = float.MaxValue;
    private Hex nearestNplRecruitmentHex = null;
    private bool needsIndirectApproach = false;
    private float liquidWealth = 0f;
    private float nationPercentageArtifacts = 0;
    private int hiddenArtifactsRemaining;
    private float duelAdvantage = 0f;
    private float songDuelAdvantage = 0f;
    private int unrecruitedSameAlignmentNplCount = 0;
    private float agentRoleStrength = 0f;
    private float mageRoleStrength = 0f;
    private float emissaryRoleStrength = 0f;
    private int unrevealedLandHexCount = 0;
    private Hex nearestUnrevealedLandHex = null;
    public CharacterAction LastChosenAction { get; private set; }

    // Set by AITurnController after construction, from the HTN's currently-active
    // PrimitiveTask.TaskId, purely for AIActionLogger traceability (see BuildLogEntry).
    public string ActiveHtnTaskId { get; set; }

    // Set by AITurnController from CharacterBlackboard.TargetHex — the specific hex behind
    // whichever situational parameter the active primitive task prefers (see
    // GetTargetHexForParameter). GetPreferredMovementTarget() reads this first, so a character
    // actually travels toward and acts on the location that triggered its current strategy
    // rather than only ever acting on wherever it happens to already be standing.
    public Hex ActiveHtnTargetHex { get; set; }

    public UtilityAIContext(Leader leader, Character character, List<CharacterAction> availableActions, Dictionary<CharacterAction, CardData> actionCards = null, PrecomputedData? precomputed = null, bool captureExecutionSnapshot = true)
    {
        Leader = leader;
        Character = character;
        AvailableActions = availableActions ?? new List<CharacterAction>();
        if (actionCards != null)
        {
            actionCardsByAction = actionCards
                .Where(pair => pair.Key != null && pair.Value != null)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        board = GetSharedBoard();

        _precomputed = precomputed;
        ApplyPrecomputedData(precomputed ?? UtilityAIContextDataBuilder.Build(leader, character));
        EconomyStatus = EvaluateEconomy();
        if (captureExecutionSnapshot)
        {
            preSnapshot = CaptureSnapshot();
            preVictoryPoints = CaptureVictoryPointsSnapshot();
        }
    }

    public async Task<bool> TryExecuteBestAvailableActionAsync()
    {
        ResetScoringData();
        // Score every action first (ScoreAction also records it for logging), then pick
        // randomly among whichever tied for the top score instead of always favoring
        // whichever happened to come first in AvailableActions.
        List<(CharacterAction action, float score)> scored = AvailableActions
            .Select(a => (action: a, score: ScoreAction(a)))
            .ToList();
        CharacterAction action = UtilityAI.PickRandomAmongTopScored(scored, s => s.score).action;

        if (action == null) return false;

        return await TryExecuteChosenActionAsync(action);
    }

    // Shared tail of TryExecuteBestAvailableActionAsync, also used by AITurnController's
    // full-deck difficulty loop once an action has already been chosen.
    public async Task<bool> TryExecuteChosenActionAsync(CharacterAction action)
    {
        if (action == null) return false;
        if (!PrepareActionForExecution(action)) return false;
        RecordAction(action);
        await action.Execute();
        return true;
    }

    public async Task<bool> PassAsync()
    {
        RecordAction(null);
        await Character.Pass();
        return true;
    }

    private void ApplyPrecomputedData(PrecomputedData data)
    {
        liquidWealth = data.LiquidWealth;
        nationPercentageArtifacts = data.NationPercentageArtifacts;
        closestEnemy = data.ClosestEnemy;
        closestNonNeutralEnemy = data.ClosestNonNeutralEnemy;
        nearestUnrevealedNpcDistance = data.NearestUnrevealedNpcDistance;
        nearestUnrevealedNpcHex = data.NearestUnrevealedNpcHex;
        nearestEnemyCharacterDistance = data.NearestEnemyCharacterDistance;
        nearestEnemyCharacterHex = data.NearestEnemyCharacterHex;
        nearestEnemyPcOpportunityDistance = data.NearestEnemyPcOpportunityDistance;
        nearestEnemyPcOpportunityHex = data.NearestEnemyPcOpportunityHex;
        nearestOwnPcLoyaltyRiskDistance = data.NearestOwnPcLoyaltyRiskDistance;
        nearestOwnPcLoyaltyRiskHex = data.NearestOwnPcLoyaltyRiskHex;
        nearestEnemyPcVulnerabilityDistance = data.NearestEnemyPcVulnerabilityDistance;
        nearestEnemyPcVulnerabilityHex = data.NearestEnemyPcVulnerabilityHex;
        nearestHighValueEnemyCharacterDistance = data.NearestHighValueEnemyCharacterDistance;
        nearestHighValueEnemyCharacterHex = data.NearestHighValueEnemyCharacterHex;
        nearestOwnPcFortificationNeedDistance = data.NearestOwnPcFortificationNeedDistance;
        nearestOwnPcFortificationNeedHex = data.NearestOwnPcFortificationNeedHex;
        nearestNplRecruitmentDistance = data.NearestNplRecruitmentDistance;
        nearestNplRecruitmentHex = data.NearestNplRecruitmentHex;
        needsIndirectApproach = data.NeedsIndirectApproach;
        hiddenArtifactsRemaining = data.HiddenArtifactsRemaining;
        duelAdvantage = data.DuelAdvantage;
        songDuelAdvantage = data.SongDuelAdvantage;
        unrecruitedSameAlignmentNplCount = data.UnrecruitedSameAlignmentNplCount;
        agentRoleStrength = data.AgentRoleStrength;
        mageRoleStrength = data.MageRoleStrength;
        emissaryRoleStrength = data.EmissaryRoleStrength;
        unrevealedLandHexCount = data.UnrevealedLandHexCount;
        nearestUnrevealedLandHex = data.NearestUnrevealedLandHex;

        if (data.ArtifactTransferCandidates != null && data.ArtifactTransferCandidates.Count > 0)
        {
            artifactTransferCandidates.Clear();
            artifactTransferCandidates.AddRange(data.ArtifactTransferCandidates);
        }
    }

    // preferredParameters: the active HTN leaf's PreferredParameters, if any — a card gets
    // HTNBiasBonus once per parameter it shares with that list. This is the only mechanism
    // tying a card to "what the active strategy is about"; there is no separate flat bonus.
    public float ScoreAction(CharacterAction action, IReadOnlyList<string> preferredParameters = null)
    {
        float score = 0f;
        ActionScoreFlags scoreFlags = UtilityAI.GetActionScoreFlags(action);

        // User-authored flat priority adjustment for this specific action.
        score += UtilityAI.GetActionScoreBonus(action);

        // Environmental cards get an automatic, non-authored penalty (unlike every other
        // consideration below, which only applies when a card opts into it via
        // utilityParameters) — every environmental card should be discouraged from frequent
        // play regardless of how its individual CardParameterProfile happens to be authored.
        if (actionCardsByAction.TryGetValue(action, out CardData scoredCard) && scoredCard != null
            && scoredCard.GetCardType() == CardTypeEnum.Environmental)
        {
            score += GetEnvironmentalPenaltyScore();
        }

        // A card can opt into any named UtilityAI parameter explicitly. This is
        // deliberately data-driven: there are no action-name special cases or
        // hidden per-card calculations here.
        if (!scoreFlags.ignoreSituation)
        {
            foreach (ActionUtilityParameterModifier modifier in UtilityAI.GetActionUtilityParameters(action))
            {
                score += GetUtilityParameter(modifier.parameter) * modifier.multiplier + modifier.bonus;

                // Bonus when this card's own authored parameter matches what the active HTN
                // leaf's situation is actually about (HTNPrimitiveTask.PreferredParameters) —
                // this is the sole mechanism biasing scoring toward the active branch.
                if (preferredParameters != null && preferredParameters.Contains(modifier.parameter, StringComparer.OrdinalIgnoreCase))
                {
                    score += UtilityAI.GetWeight(UtilityAI.Keys.HTNBiasBonus);
                }
            }
        }

        RecordScoredAction(action, score);
        return score;
    }

    private float GetDistanceScore(bool allowNeutral)
    {
        EnemyTarget target = allowNeutral ? closestEnemy : closestNonNeutralEnemy;
        if (target.Hex == null && allowNeutral) target = closestNonNeutralEnemy;
        if (target.Hex == null) target = closestEnemy;

        if (target.Hex == null) return 0f;

        float effectiveDistance = target.Distance + (target.IsNeutral ? UtilityAI.GetWeight(UtilityAI.Keys.NeutralTargetExtraDistance) : 0f);
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.EnemyProximityMax) - effectiveDistance);
    }

    private int ResolveCardDifficulty(CharacterAction action)
    {
        if (action == null) return 0;
        if (actionCardsByAction.TryGetValue(action, out CardData card) && card != null)
        {
            return Mathf.Max(0, card.difficulty);
        }
        return 0;
    }

    private bool PrepareActionForExecution(CharacterAction action)
    {
        if (action == null || Leader == null) return false;

        if (!actionCardsByAction.TryGetValue(action, out CardData card) || card == null)
        {
            return false;
        }

        // Defensive re-check: EvaluatePlayability's Environmental branch (the shared playability
        // boundary ScoreCard filters candidates through) already excludes a second environmental
        // card once one's been played this turn, but AvailableActions in the legacy
        // TryExecuteBestAvailableActionAsync path isn't guaranteed pre-filtered the same way.
        bool isEnvironmental = card.GetCardType() == CardTypeEnum.Environmental;
        EnvironmentalCardManager environment = isEnvironmental ? EnvironmentalCardManager.GetOrCreate() : null;
        int currentTurn = Game.Instance?.turn ?? 0;
        if (isEnvironmental && !environment.CanNationPlay(Leader, currentTurn, out _))
        {
            return false;
        }

        DeckManager deckManager = DeckManager.Instance != null
            ? DeckManager.Instance
            : DeckManager.Instance;
        if (deckManager == null) return false;

        if (!deckManager.TryConsumeActionCardFromFullDeck(Leader, action.GetType().Name, card, out CardData consumedCard))
        {
            return false;
        }

        consumedCard ??= card;
        action.card = consumedCard;
        action.difficulty = Mathf.Max(0, consumedCard.difficulty);
        deckManager.ApplyMapRevealForPlayedCard(Leader, consumedCard);
        // AI card execution otherwise never routes through EnvironmentalCardManager at all —
        // the resolved action's own Execute() only runs its immediate effect, not the board's
        // ongoing-effect/token machinery (see Card.HandleEnvironmentalCardPlayed for the human
        // equivalent of these two calls).
        if (isEnvironmental)
        {
            if (!environment.TrySetActiveCard(consumedCard, Leader, out _)) return false;
        }

        return true;
    }

    private float GetMilitaryEdgeScore()
    {
        if (!Character.IsArmyCommander() || Character.GetArmy() == null)
            return UtilityAI.GetWeight(UtilityAI.Keys.NoArmyPenalty);

        float myStrength = Character.GetArmy().GetOffence();
        EnemyTarget target = closestEnemy.Hex != null ? closestEnemy : closestNonNeutralEnemy;
        if (target.Hex == null || target.Strength <= 0) return 0f;

        float strengthDiff = myStrength - target.Strength;
        float distancePenalty = target.Distance > 1f ? UtilityAI.GetWeight(UtilityAI.Keys.FarTargetPenalty) : 0f;

        if (strengthDiff < 0)
        {
            // needsIndirectApproach is decided once, authoritatively, in
            // UtilityAIContextDataBuilder (against OutmatchedStrengthRatio) — not re-derived
            // here against a different, undocumented threshold (this used to set it on any
            // deficit at all, vs. the builder's ratio-buffered definition).
            return Mathf.Max(-10f, strengthDiff / 10f - distancePenalty);
        }

        return Mathf.Clamp(strengthDiff / 20f, -5f, 8f) - distancePenalty;
    }

    // Reusable win-probability proxy for Militaristic.OffenseWinRatioReady's hard gate — a
    // straightforward ratio (not the clamped/penalized delta GetMilitaryEdgeScore uses for
    // continuous scoring), so "1.0 = evenly matched" reads naturally against
    // Militaristic.MinWinRatioToAttack. 0 means "don't attack" (no army, or no target found)
    // rather than an undefined/negative signal.
    public float GetArmyWinRatio()
    {
        if (!Character.IsArmyCommander() || Character.GetArmy() == null) return 0f;
        EnemyTarget target = closestNonNeutralEnemy.Hex != null ? closestNonNeutralEnemy : closestEnemy;
        if (target.Hex == null) return 0f;
        float myStrength = Character.GetArmy().GetOffence();
        return myStrength / Mathf.Max(1f, target.Strength);
    }

    private float GetDiplomaticScore()
    {
        if (nearestUnrevealedNpcDistance == float.MaxValue) return 0f;

        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.NpcProximityMax) - nearestUnrevealedNpcDistance);
    }

    private float GetNearbyEnemyCharacterScore()
    {
        if (nearestEnemyCharacterDistance == float.MaxValue) return 0f;
        // Closer enemy characters make intelligence more valuable
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.EnemyCharacterProximityMax) - nearestEnemyCharacterDistance);
    }

    // Target-quality signals: "is there a specific good target nearby right now", computed once
    // in UtilityAIContextDataBuilder against an authored qualifying threshold (loyalty/defense/
    // skill), then faded by proximity here exactly like every other *ProximityMax term in this file.
    private float GetEnemyPcOpportunityScore()
    {
        if (nearestEnemyPcOpportunityDistance == float.MaxValue) return 0f;
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticEnemyPcOpportunityProximityMax) - nearestEnemyPcOpportunityDistance);
    }

    private float GetOwnPcLoyaltyRiskScore()
    {
        if (nearestOwnPcLoyaltyRiskDistance == float.MaxValue) return 0f;
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticOwnPcLoyaltyRiskProximityMax) - nearestOwnPcLoyaltyRiskDistance);
    }

    private float GetEnemyPcVulnerabilityScore()
    {
        if (nearestEnemyPcVulnerabilityDistance == float.MaxValue) return 0f;
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.IntelligenceEnemyPcVulnerabilityProximityMax) - nearestEnemyPcVulnerabilityDistance);
    }

    private float GetHighValueEnemyCharacterScore()
    {
        if (nearestHighValueEnemyCharacterDistance == float.MaxValue) return 0f;
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.IntelligenceHighValueEnemyCharacterProximityMax) - nearestHighValueEnemyCharacterDistance);
    }

    private float GetOwnPcFortificationNeedScore()
    {
        if (nearestOwnPcFortificationNeedDistance == float.MaxValue) return 0f;
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.MilitaristicOwnPcFortificationProximityMax) - nearestOwnPcFortificationNeedDistance);
    }

    private float GetNplRecruitmentScore()
    {
        if (nearestNplRecruitmentDistance == float.MaxValue) return 0f;
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticNplRecruitmentProximityMax) - nearestNplRecruitmentDistance);
    }

    // The nearest non-neutral enemy PC/army is within Militaristic's proximity window
    // (Targeting.EnemyProximityMax) — proximity alone, independent of who would win a fight.
    // Read by HTNRegistry's Militaristic.EnemyNear/Danger predicates.
    public bool IsEnemyNear => GetDistanceScore(false) > 0f;

    // The nearest enemy target outguns this leader's army by UtilityAIContextDataBuilder's
    // OutmatchedStrengthRatio (0 strength while leading no army, so "no army" is always
    // outmatched if any enemy exists) — computed once, authoritatively, at context build time.
    // Read by HTNRegistry's Militaristic.Danger predicate and already feeds the Intelligence/
    // Diplomatic "indirect approach" bonuses below.
    public bool IsOutmatched => needsIndirectApproach;

    // Tightest-radius, highest-priority danger tier: outmatched AND the enemy is right on top
    // of this character (a raw distance check, not the faded proximity score IsEnemyNear
    // reads) — read by HTNRegistry's Global.ImmediateDanger predicate.
    public bool IsImmediateDanger
    {
        get
        {
            EnemyTarget target = closestNonNeutralEnemy.Hex != null ? closestNonNeutralEnemy : closestEnemy;
            return IsOutmatched && target.Hex != null
                && target.Distance <= UtilityAI.GetWeight(UtilityAI.Keys.ImmediateDangerDistance);
        }
    }

    // Raw distances behind the Diplomacy near/mid banding predicates (HTNRegistry's
    // Diplomatic.NplsNear/MidReady, EnemyPcOpportunityNear/MidReady) — the underlying fields
    // are already populated by UtilityAIContextDataBuilder.CacheNpcTargets/CacheEnemyTargets
    // for the existing continuous proximity scores; these just expose the raw number.
    public float NearestNplRecruitmentDistance => nearestNplRecruitmentDistance;
    public float NearestEnemyPcOpportunityDistance => nearestEnemyPcOpportunityDistance;

    // World-state-only appeal of pursuing each named category of response right now,
    // independent of any specific card — the same situational terms ScoreAction adds per-card
    // above, minus the handful tied to a concrete action (Intelligence's Scout Area bonus,
    // Artifacts's artifact-transfer search — both added separately in ScoreAction). This is what
    // HTNRegistry's Viable predicates read: the HTN's strategy choice is driven by literally
    // the same weights and formula the Utility scorer uses to pick cards, not a separate
    // hand-coded sensing layer. All terms degrade to 0 (or a fixed penalty, e.g. NoArmyPenalty)
    // with no valid target, so no separate "HasTarget" boolean is needed — a low/negative
    // viability already means "no." There is deliberately no Economic case here (and never
    // was) — Economic's situational term is EconomyStatus (a liquid-wealth tier), not a
    // viability aggregate; see EvaluateEconomy.
    public float GetMilitaristicViability() => GetDistanceScore(false) + GetMilitaryEdgeScore();

    public float GetIntelligenceViability()
    {
        float viability = GetNearbyEnemyCharacterScore() + GetDistanceScore(true);
        if (needsIndirectApproach) viability += UtilityAI.GetWeight(UtilityAI.Keys.IntelligenceOutmatchedBonus);
        viability += GetDistanceScore(true) * UtilityAI.GetWeight(UtilityAI.Keys.IntelligenceEnemyPressureWeight);
        viability += agentRoleStrength * UtilityAI.GetWeight(UtilityAI.Keys.IntelligenceAgentStrengthWeight);
        return viability;
    }

    public float GetArtifactsViability()
    {
        return (1f - nationPercentageArtifacts) * UtilityAI.GetWeight(UtilityAI.Keys.ArtifactScarcityWeight)
            + GetDistanceScore(true)
            + hiddenArtifactsRemaining * UtilityAI.GetWeight(UtilityAI.Keys.ArtifactsHiddenArtifactsWeight)
            + mageRoleStrength * UtilityAI.GetWeight(UtilityAI.Keys.ArtifactsMageStrengthWeight);
    }

    public float GetDiplomaticViability()
    {
        float viability = GetDiplomaticScore() + GetDistanceScore(true);
        if (needsIndirectApproach) viability += UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticOutmatchedBonus);
        viability += GetDistanceScore(true) * UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticEnemyPressureWeight);
        viability += emissaryRoleStrength * UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticEmissaryStrengthWeight);
        return viability;
    }

    public float GetLogisticsViability() => GetLogisticsProximityScore();

    public float GetDisruptionViability() => GetDistanceScore(true);

    // Direct, named utility observations. HTN predicates and card profiles both
    // call this same public API, so the inspector can always show exactly what
    // value caused a branch or score contribution.
    public float GetUtilityParameter(string parameter)
    {
        return parameter switch
        {
            UtilityAIParameters.MilitaristicEnemyPressure => GetDistanceScore(false),
            UtilityAIParameters.MilitaristicMilitaryEdge => GetMilitaryEdgeScore(),
            UtilityAIParameters.EconomicLiquidWealth => liquidWealth,
            UtilityAIParameters.DiplomaticIndirectSafety => needsIndirectApproach ? UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticOutmatchedBonus) : 0f,
            UtilityAIParameters.IntelligenceEnemyCharacter => GetNearbyEnemyCharacterScore(),
            UtilityAIParameters.IntelligenceIndirectSafety => needsIndirectApproach ? UtilityAI.GetWeight(UtilityAI.Keys.IntelligenceOutmatchedBonus) : 0f,
            UtilityAIParameters.ArtifactsArtifactScarcity => (1f - nationPercentageArtifacts) * UtilityAI.GetWeight(UtilityAI.Keys.ArtifactScarcityWeight),
            UtilityAIParameters.ArtifactsArtifactTransfer => GetArtifactTransferScore(),
            UtilityAIParameters.ArtifactsEnemyPressure => GetDistanceScore(true),
            UtilityAIParameters.ArtifactsHiddenArtifacts => hiddenArtifactsRemaining,
            UtilityAIParameters.ArtifactsMageStrength => mageRoleStrength,
            UtilityAIParameters.DiplomaticEnemyPressure => GetDistanceScore(true),
            UtilityAIParameters.DiplomaticEmissaryStrength => emissaryRoleStrength,
            UtilityAIParameters.IntelligenceEnemyPressure => GetDistanceScore(true),
            UtilityAIParameters.IntelligenceAgentStrength => agentRoleStrength,
            UtilityAIParameters.IntelligenceExplorationNeed => unrevealedLandHexCount,
            UtilityAIParameters.LogisticsReachNpc => GetLogisticsProximityScore(nearestUnrevealedNpcHex),
            UtilityAIParameters.LogisticsInterceptEnemy => GetLogisticsProximityScore(closestNonNeutralEnemy.Hex ?? closestEnemy.Hex),
            UtilityAIParameters.LogisticsReachEnemyCharacter => GetLogisticsProximityScore(nearestEnemyCharacterHex),
            UtilityAIParameters.LogisticsHealingNeed => GetHealingNeedScore(),
            UtilityAIParameters.DisruptionEnemyPressure => GetDistanceScore(true),
            UtilityAIParameters.DiplomaticEnemyPcOpportunity => GetEnemyPcOpportunityScore(),
            UtilityAIParameters.DiplomaticOwnPcLoyaltyRisk => GetOwnPcLoyaltyRiskScore(),
            UtilityAIParameters.IntelligenceEnemyPcVulnerability => GetEnemyPcVulnerabilityScore(),
            UtilityAIParameters.IntelligenceHighValueEnemyCharacter => GetHighValueEnemyCharacterScore(),
            UtilityAIParameters.MilitaristicOwnPcFortificationNeed => GetOwnPcFortificationNeedScore(),
            UtilityAIParameters.DiplomaticNplRecruitment => GetNplRecruitmentScore(),
            // Deliberately the same formula as MilitaristicOwnPcFortificationNeed above — see
            // the constant's doc comment in UtilityAI.cs.
            UtilityAIParameters.MilitaristicOwnPcDefenderNeed => GetOwnPcFortificationNeedScore(),
            // Signed win-probability margins (self score minus best eligible opponent's, see
            // UtilityAIContextDataBuilder.CacheDuelSignal/CacheSongDuelSignal) — negative when
            // this character would likely lose, so a bad matchup suppresses the card's own
            // score instead of merely failing to help it.
            UtilityAIParameters.MilitaristicDuelAdvantage => duelAdvantage,
            UtilityAIParameters.MilitaristicSongDuelAdvantage => songDuelAdvantage,
            // Board-wide, not proximity-based — see DiplomaticNplRecruitment above for "is there
            // one specific eligible target nearby right now".
            UtilityAIParameters.DiplomaticNplScarcity => Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticLowNplsCountAtMost) - unrecruitedSameAlignmentNplCount),
            UtilityAIParameters.EconomicMithrilInsufficient => GetResourceInsufficientScore(ProducesEnum.mithril, UtilityAI.Keys.EconomicMithrilInsufficientBelow),
            UtilityAIParameters.EconomicMithrilSurplus => GetResourceSurplusScore(ProducesEnum.mithril, UtilityAI.Keys.EconomicMithrilSurplusAbove),
            UtilityAIParameters.EconomicSteelInsufficient => GetResourceInsufficientScore(ProducesEnum.steel, UtilityAI.Keys.EconomicSteelInsufficientBelow),
            UtilityAIParameters.EconomicSteelSurplus => GetResourceSurplusScore(ProducesEnum.steel, UtilityAI.Keys.EconomicSteelSurplusAbove),
            UtilityAIParameters.EconomicIronInsufficient => GetResourceInsufficientScore(ProducesEnum.iron, UtilityAI.Keys.EconomicIronInsufficientBelow),
            UtilityAIParameters.EconomicIronSurplus => GetResourceSurplusScore(ProducesEnum.iron, UtilityAI.Keys.EconomicIronSurplusAbove),
            UtilityAIParameters.EconomicMountsInsufficient => GetResourceInsufficientScore(ProducesEnum.mounts, UtilityAI.Keys.EconomicMountsInsufficientBelow),
            UtilityAIParameters.EconomicMountsSurplus => GetResourceSurplusScore(ProducesEnum.mounts, UtilityAI.Keys.EconomicMountsSurplusAbove),
            UtilityAIParameters.EconomicTimberInsufficient => GetResourceInsufficientScore(ProducesEnum.timber, UtilityAI.Keys.EconomicTimberInsufficientBelow),
            UtilityAIParameters.EconomicTimberSurplus => GetResourceSurplusScore(ProducesEnum.timber, UtilityAI.Keys.EconomicTimberSurplusAbove),
            UtilityAIParameters.EconomicLeatherInsufficient => GetResourceInsufficientScore(ProducesEnum.leather, UtilityAI.Keys.EconomicLeatherInsufficientBelow),
            UtilityAIParameters.EconomicLeatherSurplus => GetResourceSurplusScore(ProducesEnum.leather, UtilityAI.Keys.EconomicLeatherSurplusAbove),
            // Gold is the trade currency, not a card-cost material — it stays on the old flat
            // liquid-wealth threshold rather than the deck-share deviation the six tradeable
            // materials use above.
            UtilityAIParameters.EconomicGoldInsufficient => Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.EconomyCriticalBelow) - (Leader?.GetResourceAmount(ProducesEnum.gold) ?? 0)),
            UtilityAIParameters.EconomicGoldSurplus => Mathf.Max(0f, (Leader?.GetResourceAmount(ProducesEnum.gold) ?? 0) - UtilityAI.GetWeight(UtilityAI.Keys.EconomyStableBelow)),
            _ => 0f
        };
    }

    // The specific hex behind a given situational parameter's score, where one exists — lets
    // AITurnController.AdvanceHtnStrategy turn "this parameter is what's driving the active
    // task" into an actual travel destination (stored on CharacterBlackboard.TargetHex),
    // instead of the parameter's score being a number with no location attached. Parameters
    // with no "go there" concept (nation-wide Economic stockpile signals; Artifacts/Logistics
    // signals that resolve wherever the character already is) correctly return null —
    // GetPreferredMovementTarget falls back to its own generic chase-logic in that case.
    public Hex GetTargetHexForParameter(string parameter)
    {
        return parameter switch
        {
            UtilityAIParameters.MilitaristicEnemyPressure => closestNonNeutralEnemy.Hex ?? closestEnemy.Hex,
            UtilityAIParameters.MilitaristicMilitaryEdge => closestNonNeutralEnemy.Hex ?? closestEnemy.Hex,
            UtilityAIParameters.MilitaristicOwnPcFortificationNeed => nearestOwnPcFortificationNeedHex,
            UtilityAIParameters.MilitaristicOwnPcDefenderNeed => nearestOwnPcFortificationNeedHex,
            UtilityAIParameters.DiplomaticEnemyPressure => closestNonNeutralEnemy.Hex ?? closestEnemy.Hex,
            UtilityAIParameters.DiplomaticEnemyPcOpportunity => nearestEnemyPcOpportunityHex,
            UtilityAIParameters.DiplomaticOwnPcLoyaltyRisk => nearestOwnPcLoyaltyRiskHex,
            UtilityAIParameters.DiplomaticNplRecruitment => nearestNplRecruitmentHex,
            UtilityAIParameters.IntelligenceEnemyCharacter => nearestEnemyCharacterHex,
            UtilityAIParameters.IntelligenceEnemyPressure => closestNonNeutralEnemy.Hex ?? closestEnemy.Hex,
            UtilityAIParameters.IntelligenceEnemyPcVulnerability => nearestEnemyPcVulnerabilityHex,
            UtilityAIParameters.IntelligenceHighValueEnemyCharacter => nearestHighValueEnemyCharacterHex,
            UtilityAIParameters.IntelligenceExplorationNeed => nearestUnrevealedLandHex,
            UtilityAIParameters.ArtifactsEnemyPressure => closestNonNeutralEnemy.Hex ?? closestEnemy.Hex,
            UtilityAIParameters.LogisticsReachNpc => nearestUnrevealedNpcHex,
            UtilityAIParameters.LogisticsInterceptEnemy => closestNonNeutralEnemy.Hex ?? closestEnemy.Hex,
            UtilityAIParameters.LogisticsReachEnemyCharacter => nearestEnemyCharacterHex,
            UtilityAIParameters.DisruptionEnemyPressure => closestEnemy.Hex ?? closestNonNeutralEnemy.Hex,
            // Board-wide "few NPLs left to recruit" has no location of its own — the nearest
            // eligible capital (already cached for DiplomaticNplRecruitment) is the natural
            // place to actually go act on it.
            UtilityAIParameters.DiplomaticNplScarcity => nearestNplRecruitmentHex,
            // These previously had no case at all, so a character whose active HTN leaf was
            // duelLeaf/songDuelLeaf (see HTNStrategyBuilder) had no travel destination and could
            // only ever duel if already standing on the right hex by coincidence — resolve the
            // same target Duel.PickBestTarget/BattleOfSongs would themselves pick.
            UtilityAIParameters.MilitaristicDuelAdvantage => GetDuelTargetHex(),
            UtilityAIParameters.MilitaristicSongDuelAdvantage => GetSongDuelTargetHex(),
            _ => null
        };
    }

    // Same target resolution as UtilityAIContextDataBuilder.CacheDuelSignal (kept in sync
    // deliberately — see that method's comment), just returning the target's hex instead of
    // the advantage score.
    private Hex GetDuelTargetHex()
    {
        if (Character == null || Character.IsRefusingDuels()) return null;

        ActionsManager actionsManager = ActionsManager.Instance;
        if (AITurnController.ResolveActionByRef("Duel", actionsManager) is not Duel duelAction) return null;

        duelAction.Initialize(Character);
        List<Character> candidates = duelAction.GetEligibleTargets(Character);
        if (candidates.Count == 0) return null;

        Character target = candidates.OrderByDescending(x => Duel.EstimateDuelScore(x, null)).First();
        return target?.hex;
    }

    // Same target resolution as UtilityAIContextDataBuilder.CacheSongDuelSignal — see GetDuelTargetHex.
    private Hex GetSongDuelTargetHex()
    {
        if (Character == null || Character.GetMage() < 1) return null;

        ActionsManager actionsManager = ActionsManager.Instance;
        if (AITurnController.ResolveActionByRef("BattleOfSongs", actionsManager) is not BattleOfSongs songAction) return null;

        songAction.Initialize(Character);
        List<Character> candidates = songAction.GetEligibleMageTargets(Character);
        if (candidates.Count == 0) return null;

        Character target = candidates.OrderByDescending(x => BattleOfSongs.EstimateSongScore(x)).First();
        return target?.hex;
    }

    // Does this character have at least one card, anywhere in the leader's full deck, whose
    // own utilityParameters profile shares at least one parameter with the given list, and
    // whose action is role-eligible for this character right now (CharacterAction.IsRoleEligible
    // — Commander/Agent/Emmissary/Mage skill gates, a stable trait, not momentary
    // affordability)? Read by HTNPlanner.Decompose to skip a branch that would only ever waste
    // its bias on a character who can never act on it — deliberately role-only, not full
    // CardData.EvaluatePlayability, since a temporarily-poor but otherwise-capable character
    // should still be considered eligible here.
    public bool HasEligibleCard(IReadOnlyList<string> preferredParameters)
    {
        if (Leader == null || Character == null || preferredParameters == null || preferredParameters.Count == 0) return false;

        DeckManager deckManager = DeckManager.Instance != null
            ? DeckManager.Instance
            : DeckManager.Instance;
        ActionsManager actionsManager = ActionsManager.Instance;
        if (deckManager == null || actionsManager == null) return false;

        foreach (CardData card in deckManager.GetFullDeck(Leader))
        {
            if (card == null || card.IsEncounterCard()) continue;
            if (!UtilityAI.TryGetProfile(card, out CardParameterProfile profile) || profile.utilityParameters == null) continue;
            if (!profile.utilityParameters.Any(p => preferredParameters.Contains(p.parameter, StringComparer.OrdinalIgnoreCase))) continue;

            string actionRef = AITurnController.NormalizeActionRef(card.GetActionRef());
            if (string.IsNullOrWhiteSpace(actionRef)) continue;

            CharacterAction action = AITurnController.ResolveActionByRef(actionRef, actionsManager);
            if (action == null) continue;

            action.Initialize(Character, card);
            if (action.IsRoleEligible(Character)) return true;
        }

        return false;
    }

    private static readonly ProducesEnum[] TradeableMaterials =
    {
        ProducesEnum.leather, ProducesEnum.mounts, ProducesEnum.timber,
        ProducesEnum.iron, ProducesEnum.steel, ProducesEnum.mithril
    };

    // How much of this leader's own stockpile (across the six tradeable materials) this one
    // resource makes up right now — the "actual" half of the insufficient/surplus comparison.
    // Zero stockpile reports every material's share as 0, which correctly reads as "insufficient
    // in everything the deck needs" rather than an undefined 0/0.
    private float GetOwnResourceShare(ProducesEnum resource)
    {
        if (Leader == null) return 0f;
        float total = 0f;
        foreach (ProducesEnum material in TradeableMaterials) total += Leader.GetResourceAmount(material);
        return total > 0f ? Leader.GetResourceAmount(resource) / total : 0f;
    }

    // What share of this resource the leader's own deck actually needs, based on how much of
    // it the leader's cards collectively cost to play — see NationBlackboard
    // (DeckManager.BuildDeckStateForLeader snapshots it once per leader at deck-build time).
    // Read from this character's own blackboard copy first (the normal path — see
    // CharacterBlackboardStore.GetOrCreate); falls back to the nation-level store directly for
    // the rare case this is queried before any blackboard exists for this character yet.
    private float GetDeckTargetShare(ProducesEnum resource)
    {
        IReadOnlyDictionary<ProducesEnum, float> share = (Leader != null && Character != null
            && CharacterBlackboardStore.TryGet(Leader, Character, out CharacterBlackboard blackboard) && blackboard.DeckResourceShare != null)
            ? blackboard.DeckResourceShare
            : NationBlackboard.GetDeckResourceShare(Leader);
        return share.TryGetValue(resource, out float value) ? value : 1f / TradeableMaterials.Length;
    }

    // "Insufficient"/"surplus" are now relative to what the leader's own deck actually needs,
    // not a flat unit threshold identical for every leader — a mithril-heavy deck reads as
    // insufficient at a much higher stockpile than a mithril-light one would. The per-material
    // weight key (still authored per-material in UtilityAI.json/the widget) now scales how
    // strongly a percentage-point deviation from the deck's target share drives Buy/Sell bias,
    // rather than marking an absolute unit floor/ceiling.
    private float GetResourceInsufficientScore(ProducesEnum resource, string scaleKey)
    {
        if (Leader == null) return 0f;
        float deviation = GetDeckTargetShare(resource) - GetOwnResourceShare(resource);
        return Mathf.Max(0f, deviation) * 100f * UtilityAI.GetWeight(scaleKey);
    }

    private float GetResourceSurplusScore(ProducesEnum resource, string scaleKey)
    {
        if (Leader == null) return 0f;
        float deviation = GetOwnResourceShare(resource) - GetDeckTargetShare(resource);
        return Mathf.Max(0f, deviation) * 100f * UtilityAI.GetWeight(scaleKey);
    }

    private void RecordAction(CharacterAction action)
    {
        LastChosenAction = action;
    }

    public Hex GetPreferredMovementTarget()
    {
        // Authoritative: the specific hex behind whichever situational parameter the active
        // HTN task actually prefers (see GetTargetHexForParameter), resolved once in
        // AITurnController.AdvanceHtnStrategy and carried on the blackboard/this context. This
        // is what makes the active strategy (danger, an opportunity, a target to intercept)
        // something the character actually travels to and acts on, rather than only ever
        // acting on whatever hex it happens to already be standing on.
        if (ActiveHtnTargetHex != null) return ActiveHtnTargetHex;

        // Fallback for tasks with no specific location (Economic recovery, the generic
        // fallback branch) — opportunistic chase, same as before this existed. Every hex here
        // is already computed board-wide by UtilityAIContextDataBuilder for scoring purposes;
        // originally only 5 of these ~10 cached candidates were ever consulted, so a character
        // whose HTN leaf had no specific target and whose top 5 candidates were all empty just
        // sat still with real, known opportunities elsewhere on the board. Priority: own PC
        // needing defense -> unrevealed NPC PCs -> strongest non-neutral enemy -> any enemy ->
        // nearest enemy character -> enemy PC ripe for influence -> own PC at loyalty risk ->
        // vulnerable enemy PC -> high-value enemy character -> NPL capital worth recruiting.
        if (nearestOwnPcFortificationNeedHex != null) return nearestOwnPcFortificationNeedHex;
        if (nearestUnrevealedNpcHex != null) return nearestUnrevealedNpcHex;
        if (closestNonNeutralEnemy.Hex != null) return closestNonNeutralEnemy.Hex;
        if (closestEnemy.Hex != null) return closestEnemy.Hex;
        if (nearestEnemyCharacterHex != null) return nearestEnemyCharacterHex;
        if (nearestEnemyPcOpportunityHex != null) return nearestEnemyPcOpportunityHex;
        if (nearestOwnPcLoyaltyRiskHex != null) return nearestOwnPcLoyaltyRiskHex;
        if (nearestEnemyPcVulnerabilityHex != null) return nearestEnemyPcVulnerabilityHex;
        if (nearestHighValueEnemyCharacterHex != null) return nearestHighValueEnemyCharacterHex;
        if (nearestNplRecruitmentHex != null) return nearestNplRecruitmentHex;
        if (nearestUnrevealedLandHex != null) return nearestUnrevealedLandHex;
        return null;
    }

    private float GetLogisticsProximityScore()
    {
        return GetLogisticsProximityScore(GetPreferredMovementTarget());
    }

    private float GetLogisticsProximityScore(Hex target)
    {
        if (target == null || Character == null || Character.hex == null) return 0f;

        float distance = Vector2.Distance(Character.hex.v2, target.v2);
        // Reward being close to the intended destination; closer hexes give larger boosts
        return Mathf.Max(0f, UtilityAI.GetWeight(UtilityAI.Keys.LogisticsProximityMax)
            - distance * UtilityAI.GetWeight(UtilityAI.Keys.LogisticsDistancePenaltyPerHex));
    }

    // Applied automatically (see ScoreAction) to every Environmental-type card, not opted into
    // via a card's utilityParameters like every other consideration. Full-strength penalty the
    // turn immediately after this leader last played one; decays linearly to 0 once
    // EnvironmentalPenaltyDecayTurns turns have passed. Before ever playing one,
    // Leader.lastEnvironmentalCardPlayedTurn defaults far enough in the past that turnsSince
    // already exceeds the decay window, so no penalty applies.
    private float GetEnvironmentalPenaltyScore()
    {
        if (Leader == null) return 0f;

        int currentTurn = Game.Instance?.turn ?? 0;
        int turnsSince = currentTurn - Leader.lastEnvironmentalCardPlayedTurn;
        float decayTurns = Mathf.Max(1f, UtilityAI.GetWeight(UtilityAI.Keys.EnvironmentalPenaltyDecayTurns));
        if (turnsSince >= decayTurns) return 0f;

        float remainingStrength = 1f - turnsSince / decayTurns;
        return UtilityAI.GetWeight(UtilityAI.Keys.EnvironmentalPenalty) * Mathf.Clamp01(remainingStrength);
    }

    // Live count of wounded allies (including self) sharing this character's hex — the same
    // "no board scan needed, just look at what's already known" shape as GetSpellOpportunityScore
    // above. A raw count, like Artifacts.HiddenArtifacts, so card profiles choose their own scale.
    private float GetHealingNeedScore()
    {
        if (Character == null || Character.hex == null || Character.hex.characters == null) return 0f;

        float healthBelow = UtilityAI.GetWeight(UtilityAI.Keys.LogisticsHealingNeedHealthBelow);
        Leader owner = Character.GetOwner();
        AlignmentEnum alignment = Character.GetAlignment();
        return Character.hex.characters.Count(c => c != null && !c.killed && c.health < healthBelow
            && (c.GetOwner() == owner || (c.GetAlignment() == alignment && alignment != AlignmentEnum.neutral)));
    }

    private float GetArtifactTransferScore()
    {
        if (Character == null || Character.hex == null) return 0f;

        // If we have precomputed candidates, reuse them and simply adjust by availability
        if (_precomputed.HasValue && _precomputed.Value.ArtifactTransferCandidates != null && _precomputed.Value.ArtifactTransferCandidates.Count > 0)
        {
            artifactTransferCandidates.Clear();
            artifactTransferCandidates.AddRange(_precomputed.Value.ArtifactTransferCandidates);
            bool canTransferCached = AvailableActions.Any(a => a is TransferArtifact);
            return canTransferCached ? Mathf.Max(0f, _precomputed.Value.BestArtifactTransferScore) : 0f;
        }

        bool canTransfer = AvailableActions.Any(a => a is TransferArtifact);
        if (!canTransfer) return 0f;

        List<CardData> transferable = Character.objects.Where(a => a != null && a.transferable).ToList();
        if (transferable.Count == 0) return 0f;

        if (board == null || board.hexes == null) return 0f;

        List<Character> friendlies = board.hexes.Values
            .SelectMany(h => h.characters)
            .Where(ch => ch != null && ch.hex != null && ch != Character &&
                         (ch.GetOwner() == Character.GetOwner() ||
                          (ch.GetAlignment() == Character.GetAlignment() && ch.GetAlignment() != AlignmentEnum.neutral)))
            .ToList();
        if (friendlies.Count == 0) return 0f;

        artifactTransferCandidates.Clear();
        float bestScore = 0f;
        foreach (CardData art in transferable)
        {
            foreach (Character target in friendlies)
            {
                float score = 0f;
                float distance = Character.hex != null && target.hex != null
                    ? Vector2.Distance(Character.hex.v2, target.hex.v2)
                    : float.MaxValue;

                // Skill boosts help low-skill targets more
                score += art.commanderBonus > 0 ? art.commanderBonus * 2f + Mathf.Max(0, 5 - target.GetCommander()) : 0f;
                score += art.agentBonus > 0 ? art.agentBonus * 2f + Mathf.Max(0, 5 - target.GetAgent()) : 0f;
                score += art.emmissaryBonus > 0 ? art.emmissaryBonus * 2f + Mathf.Max(0, 5 - target.GetEmmissary()) : 0f;
                score += art.mageBonus > 0 ? art.mageBonus * 2f + Mathf.Max(0, 5 - target.GetMage()) : 0f;

                // Combat bonuses are more valuable on army commanders
                if (target.IsArmyCommander())
                {
                    score += art.GetAttackBonus() * 3f;
                    score += art.GetDefenseBonus() * 2f;
                }

                // Small penalty if target already excels in the boosted area
                if (art.commanderBonus > 0 && target.GetCommander() > 3) score -= 2f;
                if (art.agentBonus > 0 && target.GetAgent() > 3) score -= 2f;
                if (art.emmissaryBonus > 0 && target.GetEmmissary() > 3) score -= 2f;
                if (art.mageBonus > 0 && target.GetMage() > 3) score -= 2f;

                // Distance penalty so nearer recipients are favored
                if (distance < float.MaxValue)
                {
                    score -= distance * 2f;
                }
                else
                {
                    score -= 5f;
                }

                artifactTransferCandidates.Add(new ArtifactTransferCandidate(art.name, target.characterName, score, distance));
                bestScore = Mathf.Max(bestScore, score);
            }
        }

        // Reward scenarios where at least one good transfer exists
        return Mathf.Max(0f, bestScore / 3f);
    }

    private void RecordScoredAction(CharacterAction action, float score)
    {
        if (action == null) return;
        string key = action.actionName;
        if (scoredActionKeys.Contains(key)) return;
        scoredActionKeys.Add(key);
        float targetDistance = -1f;
        Hex preferred = GetPreferredMovementTarget();
        if (preferred != null && Character != null && Character.hex != null)
        {
            targetDistance = Vector2.Distance(Character.hex.v2, preferred.v2);
        }
        scoredActions.Add(new AIScoredAction(action.actionName, score, targetDistance));
    }

    private void ResetScoringData()
    {
        scoredActions.Clear();
        scoredActionKeys.Clear();
        artifactTransferCandidates.Clear();
    }

    public AIActionLogEntry BuildLogEntry()
    {
        // Refresh enemy target cache after action for post-state measurements
        CacheEnemyTargets();
        ResourceSnapshot post = CaptureSnapshot();
        Hex preferred = GetPreferredMovementTarget();
        Leader owner = Character != null ? Character.GetOwner() : null;
        Army army = Character != null ? Character.GetArmy() : null;
        TargetInfo targetInfo = GetTargetInfo(preferred);
        // Victory points are a PlayableLeader-only competitive-win concept — NPLs don't have
        // an entry in the snapshot, so VP-delta reporting is skipped (zeroed) for their turns.
        Dictionary<PlayableLeader, int> postVictoryPoints = CaptureVictoryPointsSnapshot();
        int preVpSelf = Leader is PlayableLeader vpSelfPre ? GetVictoryPoints(preVictoryPoints, vpSelfPre) : 0;
        int postVpSelf = Leader is PlayableLeader vpSelfPost ? GetVictoryPoints(postVictoryPoints, vpSelfPost) : 0;
        return new AIActionLogEntry
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            turn = Game.Instance?.turn ?? -1,
            leaderName = Leader?.characterName,
            leaderAlignment = Leader?.GetAlignment().ToString(),
            characterName = Character?.characterName,
            characterAlignment = Character?.GetAlignment().ToString(),
            armyCommander = Character != null && Character.IsArmyCommander(),
            commander = Character?.GetCommander() ?? 0,
            agent = Character?.GetAgent() ?? 0,
            emmissary = Character?.GetEmmissary() ?? 0,
            mage = Character?.GetMage() ?? 0,
            armyOffence = army != null ? army.GetOffence() : 0,
            armyDefence = army != null ? army.GetDefence() : 0,
            health = Character?.health ?? 0,
            preCommander = preSnapshot.commander,
            preAgent = preSnapshot.agent,
            preEmmissary = preSnapshot.emmissary,
            preMage = preSnapshot.mage,
            preArmyOffence = preSnapshot.armyOffence,
            preArmyDefence = preSnapshot.armyDefence,
            preHealth = preSnapshot.health,
            commanderDelta = (Character?.GetCommander() ?? 0) - preSnapshot.commander,
            agentDelta = (Character?.GetAgent() ?? 0) - preSnapshot.agent,
            emmissaryDelta = (Character?.GetEmmissary() ?? 0) - preSnapshot.emmissary,
            mageDelta = (Character?.GetMage() ?? 0) - preSnapshot.mage,
            armyOffenceDelta = (army != null ? army.GetOffence() : 0) - preSnapshot.armyOffence,
            armyDefenceDelta = (army != null ? army.GetDefence() : 0) - preSnapshot.armyDefence,
            healthDelta = (Character?.health ?? 0) - preSnapshot.health,
            goldBuffer = owner != null ? owner.goldAmount : 0,
            goldPerTurn = 0,
            leather = owner != null ? owner.leatherAmount : 0,
            timber = owner != null ? owner.timberAmount : 0,
            iron = owner != null ? owner.ironAmount : 0,
            mounts = owner != null ? owner.mountsAmount : 0,
            mithril = owner != null ? owner.mithrilAmount : 0,
            leatherPerTurn = owner != null ? owner.GetLeatherPerTurn() : 0,
            timberPerTurn = owner != null ? owner.GetTimberPerTurn() : 0,
            ironPerTurn = owner != null ? owner.GetIronPerTurn() : 0,
            mountsPerTurn = owner != null ? owner.GetMountsPerTurn() : 0,
            mithrilPerTurn = owner != null ? owner.GetMithrilPerTurn() : 0,
            preGoldBuffer = preSnapshot.gold,
            preGoldPerTurn = preSnapshot.goldPerTurn,
            preLeather = preSnapshot.leather,
            preTimber = preSnapshot.timber,
            preIron = preSnapshot.iron,
            preMounts = preSnapshot.mounts,
            preMithril = preSnapshot.mithril,
            preLeatherPerTurn = preSnapshot.leatherPerTurn,
            preTimberPerTurn = preSnapshot.timberPerTurn,
            preIronPerTurn = preSnapshot.ironPerTurn,
            preMountsPerTurn = preSnapshot.mountsPerTurn,
            preMithrilPerTurn = preSnapshot.mithrilPerTurn,
            goldDelta = (owner != null ? owner.goldAmount : 0) - preSnapshot.gold,
            leatherDelta = (owner != null ? owner.leatherAmount : 0) - preSnapshot.leather,
            timberDelta = (owner != null ? owner.timberAmount : 0) - preSnapshot.timber,
            ironDelta = (owner != null ? owner.ironAmount : 0) - preSnapshot.iron,
            mountsDelta = (owner != null ? owner.mountsAmount : 0) - preSnapshot.mounts,
            mithrilDelta = (owner != null ? owner.mithrilAmount : 0) - preSnapshot.mithril,
            goldPerTurnDelta = 0 - preSnapshot.goldPerTurn,
            leatherPerTurnDelta = (owner != null ? owner.GetLeatherPerTurn() : 0) - preSnapshot.leatherPerTurn,
            timberPerTurnDelta = (owner != null ? owner.GetTimberPerTurn() : 0) - preSnapshot.timberPerTurn,
            ironPerTurnDelta = (owner != null ? owner.GetIronPerTurn() : 0) - preSnapshot.ironPerTurn,
            mountsPerTurnDelta = (owner != null ? owner.GetMountsPerTurn() : 0) - preSnapshot.mountsPerTurn,
            mithrilPerTurnDelta = (owner != null ? owner.GetMithrilPerTurn() : 0) - preSnapshot.mithrilPerTurn,
            economyStatus = EconomyStatus.ToString(),
            needsIndirect = needsIndirectApproach,
            nationArtifactsShare = nationPercentageArtifacts,
            nearestNpcDistance = nearestUnrevealedNpcDistance,
            nearestEnemyCharacterDistance = nearestEnemyCharacterDistance,
            nearestEnemyStrength = closestEnemy.Strength,
            nearestNonNeutralStrength = closestNonNeutralEnemy.Strength,
            preNearestEnemyStrength = preSnapshot.nearestEnemyStrength,
            preNearestNonNeutralStrength = preSnapshot.nearestNonNeutralStrength,
            nearestEnemyStrengthDelta = closestEnemy.Strength - preSnapshot.nearestEnemyStrength,
            nearestNonNeutralStrengthDelta = closestNonNeutralEnemy.Strength - preSnapshot.nearestNonNeutralStrength,
            targetOwnerName = targetInfo.name,
            targetOwnerAlignment = targetInfo.alignment,
            targetOwnerType = targetInfo.type,
            preferredTargetType = preferred != null ? preferred.GetPC() != null ? "PC" : "Hex" : "None",
            preferredTarget = preferred != null ? preferred.v2 : Vector2Int.one * -1,
            preferredTargetDistance = preferred != null && Character != null && Character.hex != null ? Vector2.Distance(Character.hex.v2, preferred.v2) : -1f,
            actionName = LastChosenAction != null ? LastChosenAction.actionName : Pass.ActionRef,
            activeHtnTaskId = ActiveHtnTaskId,
            actionDifficulty = LastChosenAction != null ? ResolveCardDifficulty(LastChosenAction) : 0,
            actionGoldCost = LastChosenAction != null ? LastChosenAction.GetGoldCost() : 0,
            scoredActions = scoredActions.Select(sa => $"{sa.actionName}|{sa.score:0.00}|{sa.targetDistance:0.00}").ToList(),
            artifactTransferCandidates = artifactTransferCandidates.Select(c => $"{c.artifactName}->{c.targetName}|{c.score:0.00}|{c.distance:0.00}").ToList(),
            victoryPointsSelfBefore = preVpSelf,
            victoryPointsSelfAfter = postVpSelf,
            victoryPointsSelfDelta = postVpSelf - preVpSelf,
            victoryPointsOpponentDeltas = Leader is PlayableLeader vpSelf
                ? BuildOpponentVpDeltas(postVictoryPoints, preVictoryPoints, vpSelf)
                : new List<string>()
        };
    }

    private EconomyStatus EvaluateEconomy()
    {
        if (Leader == null) return EconomyStatus.Stable;
        return UtilityAI.EvaluateEconomyStatus(liquidWealth);
    }

    // Real liquid wealth: stored gold + every held resource valued at its current market
    // sell price (StoresManager — a shared, supply-driven market, so this number moves with
    // the market too, not just with what the leader holds). This game has no passive income
    // of any kind (Leader.GetXPerTurn() are all hardcoded-0 stubs, never overridden anywhere;
    // NewTurn() never grants gold or resources) — there is nothing else to measure.
    public static float CalculateLiquidWealth(Leader leader, StoresManager stores)
    {
        if (leader == null) return 0f;
        float wealth = leader.goldAmount;
        if (stores == null) return wealth;

        wealth += leader.leatherAmount * stores.GetSellPricePerUnit(ProducesEnum.leather);
        wealth += leader.mountsAmount * stores.GetSellPricePerUnit(ProducesEnum.mounts);
        wealth += leader.timberAmount * stores.GetSellPricePerUnit(ProducesEnum.timber);
        wealth += leader.ironAmount * stores.GetSellPricePerUnit(ProducesEnum.iron);
        wealth += leader.steelAmount * stores.GetSellPricePerUnit(ProducesEnum.steel);
        wealth += leader.mithrilAmount * stores.GetSellPricePerUnit(ProducesEnum.mithril);
        return wealth;
    }

    private void CacheEnemyTargets()
    {
        closestEnemy = new EnemyTarget(null, float.MaxValue, false, 0f);
        closestNonNeutralEnemy = new EnemyTarget(null, float.MaxValue, false, 0f);

        if (board == null || Character == null || Character.hex == null) return;

        IEnumerable<Hex> hexes = board.hexes != null ? board.hexes.Values : Enumerable.Empty<Hex>();

        float myStrength = Character.IsArmyCommander() && Character.GetArmy() != null ? Character.GetArmy().GetOffence() : 0f;

        foreach (Hex hex in hexes)
        {
            bool hasEnemyCharacter = hex.characters.Any(c => c != null && c.GetOwner() != null && IsEnemy(c.GetOwner()));
            Leader enemyLeader = GetEnemyLeaderOnHex(hex);
            if (enemyLeader == null) continue;

            bool isNeutral = enemyLeader.GetAlignment() == AlignmentEnum.neutral;
            float distance = Vector2.Distance(Character.hex.v2, hex.v2);
            float distanceScore = distance + (isNeutral ? 2f : 0f);
            float strength = EstimateEnemyStrength(hex);

            if (distanceScore < closestEnemy.Score)
            {
                closestEnemy = new EnemyTarget(hex, distance, isNeutral, strength);
            }

            if (!isNeutral && distance < closestNonNeutralEnemy.Distance)
            {
                closestNonNeutralEnemy = new EnemyTarget(hex, distance, isNeutral, strength);
            }

            if (hasEnemyCharacter && distance < nearestEnemyCharacterDistance)
            {
                nearestEnemyCharacterDistance = distance;
                nearestEnemyCharacterHex = hex;
            }
        }

        EnemyTarget best = closestNonNeutralEnemy.Hex != null ? closestNonNeutralEnemy : closestEnemy;
        float outmatchedRatio = UtilityAI.GetWeight(UtilityAI.Keys.OutmatchedStrengthRatio);
        if (best.Hex != null && best.Strength > myStrength * outmatchedRatio) needsIndirectApproach = true;
    }

    private void CacheNpcTargets()
    {
        if (board == null || Character == null || Character.hex == null) return;

        foreach (Hex hex in board.hexes.Values)
        {
            PC pc = hex.GetPC();
            if (pc == null) continue;
            if (pc.owner is not NonPlayableLeader npc) continue;
            if (npc.IsRevealedToLeader(Game.Instance.currentlyPlaying)) continue;

            float distance = Vector2.Distance(Character.hex.v2, hex.v2);
            if (distance < nearestUnrevealedNpcDistance)
            {
                nearestUnrevealedNpcDistance = distance;
                nearestUnrevealedNpcHex = hex;
            }
        }
    }

    private Leader GetEnemyLeaderOnHex(Hex hex)
    {
        if (hex == null) return null;

        PC pc = hex.GetPC();
        if (pc != null && pc.owner != null && IsEnemy(pc.owner)) return pc.owner;

        Character enemyCharacter = hex.characters.FirstOrDefault(c => c != null && c.GetOwner() != null && IsEnemy(c.GetOwner()));
        if (enemyCharacter != null) return enemyCharacter.GetOwner();

        return null;
    }

    private float EstimateEnemyStrength(Hex hex)
    {
        if (hex == null) return 0f;

        int strength = 0;
        PC pc = hex.GetPC();
        if (pc != null && pc.owner != null && IsEnemy(pc.owner))
        {
            strength = Mathf.Max(strength, pc.GetDefense());
        }

        if (hex.armies != null)
        {
            foreach (Army army in hex.armies)
            {
                if (army == null || army.commander == null) continue;
                if (army.commander.GetOwner() == null) continue;
                if (!IsEnemy(army.commander.GetOwner())) continue;
                strength = Mathf.Max(strength, army.GetDefence());
            }
        }

        return strength;
    }

    private float CalculateNationArtifacts()
    {
        if (Leader == null) return 0;
        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        int catalogCount = deckManager?.GetObjectCardCount() ?? 0;
        return Leader.controlledCharacters.Sum(ch => ch != null ? ch.objects.Count * 1f : 0f) / Math.Max(1f, catalogCount * 1f);
    }

    private bool IsEnemy(Leader other)
    {
        if (other == null || Leader == null) return false;
        if (other == Leader) return false;

        AlignmentEnum myAlignment = Leader.GetAlignment();
        AlignmentEnum otherAlignment = other.GetAlignment();

        if (myAlignment == otherAlignment && myAlignment != AlignmentEnum.neutral) return false;

        // Anything that is not aligned with us is an enemy; neutral is lowest priority but still an enemy.
        return otherAlignment != myAlignment || otherAlignment == AlignmentEnum.neutral;
    }

    private static bool NameContains(string source, string needle)
    {
        return source != null && source.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public struct PrecomputedData
    {
        public EnemyTarget ClosestEnemy;
        public EnemyTarget ClosestNonNeutralEnemy;
        public float NearestUnrevealedNpcDistance;
        public Hex NearestUnrevealedNpcHex;
        public float NearestEnemyCharacterDistance;
        public Hex NearestEnemyCharacterHex;
        public float NearestEnemyPcOpportunityDistance;
        public Hex NearestEnemyPcOpportunityHex;
        public float NearestOwnPcLoyaltyRiskDistance;
        public Hex NearestOwnPcLoyaltyRiskHex;
        public float NearestEnemyPcVulnerabilityDistance;
        public Hex NearestEnemyPcVulnerabilityHex;
        public float NearestHighValueEnemyCharacterDistance;
        public Hex NearestHighValueEnemyCharacterHex;
        public float NearestOwnPcFortificationNeedDistance;
        public Hex NearestOwnPcFortificationNeedHex;
        public float NearestNplRecruitmentDistance;
        public Hex NearestNplRecruitmentHex;
        public bool NeedsIndirectApproach;
        public float LiquidWealth;
        public float NationPercentageArtifacts;
        public int HiddenArtifactsRemaining;
        public List<ArtifactTransferCandidate> ArtifactTransferCandidates;
        public float BestArtifactTransferScore;
        public float DuelAdvantage;
        public float SongDuelAdvantage;
        public int UnrecruitedSameAlignmentNplCount;
        public int UnrevealedLandHexCount;
        public Hex NearestUnrevealedLandHex;

        // Sum of the leader's controlled characters' Agent/Mage/Emissary skill, computed once
        // per character-turn (see UtilityAIContextDataBuilder.Build) rather than re-summed via
        // LINQ by every one of the ~100+ per-card UtilityAIContext instances scored per pick.
        public float AgentRoleStrength;
        public float MageRoleStrength;
        public float EmissaryRoleStrength;
    }

    public readonly struct EnemyTarget
    {
        public Hex Hex { get; }
        public float Distance { get; }
        public bool IsNeutral { get; }
        public float Strength { get; }
        public float Score => Distance + (IsNeutral ? 2f : 0f);

        public EnemyTarget(Hex hex, float distance, bool isNeutral, float strength)
        {
            Hex = hex;
            Distance = distance;
            IsNeutral = isNeutral;
            Strength = strength;
        }
    }

    private readonly struct AIScoredAction
    {
        public readonly string actionName;
        public readonly float score;
        public readonly float targetDistance;

        public AIScoredAction(string actionName, float score, float targetDistance)
        {
            this.actionName = actionName;
            this.score = score;
            this.targetDistance = targetDistance;
        }
    }

    public readonly struct ArtifactTransferCandidate
    {
        public readonly string artifactName;
        public readonly string targetName;
        public readonly float score;
        public readonly float distance;

        public ArtifactTransferCandidate(string artifactName, string targetName, float score, float distance)
        {
            this.artifactName = artifactName;
            this.targetName = targetName;
            this.score = score;
            this.distance = distance;
        }
    }

    private TargetInfo GetTargetInfo(Hex targetHex)
    {
        if (targetHex == null) return new TargetInfo(null, null, null);
        Leader rawLeader = GetEnemyLeaderOnHex(targetHex);
        if (rawLeader == null) return new TargetInfo(null, null, null);

        // If NPC has joined, prefer the joined owner
        Leader effective = rawLeader;
        if (rawLeader is NonPlayableLeader npc && npc.joined && npc.GetOwner() != null)
        {
            effective = npc.GetOwner();
        }

        string type = effective is NonPlayableLeader ? "NonPlayableLeader" : "Leader";
        return new TargetInfo(effective.characterName, effective.GetAlignment().ToString(), type);
    }

    private readonly struct TargetInfo
    {
        public readonly string name;
        public readonly string alignment;
        public readonly string type;

        public TargetInfo(string name, string alignment, string type)
        {
            this.name = name;
            this.alignment = alignment;
            this.type = type;
        }
    }

    private ResourceSnapshot CaptureSnapshot()
    {
        Leader owner = Character != null ? Character.GetOwner() : null;
        Army army = Character != null ? Character.GetArmy() : null;
        return new ResourceSnapshot
        {
            gold = owner != null ? owner.goldAmount : 0,
            goldPerTurn = 0,
            leather = owner != null ? owner.leatherAmount : 0,
            timber = owner != null ? owner.timberAmount : 0,
            iron = owner != null ? owner.ironAmount : 0,
            mounts = owner != null ? owner.mountsAmount : 0,
            mithril = owner != null ? owner.mithrilAmount : 0,
            leatherPerTurn = owner != null ? owner.GetLeatherPerTurn() : 0,
            timberPerTurn = owner != null ? owner.GetTimberPerTurn() : 0,
            ironPerTurn = owner != null ? owner.GetIronPerTurn() : 0,
            mountsPerTurn = owner != null ? owner.GetMountsPerTurn() : 0,
            mithrilPerTurn = owner != null ? owner.GetMithrilPerTurn() : 0,
            armyOffence = army != null ? army.GetOffence() : 0,
            armyDefence = army != null ? army.GetDefence() : 0,
            commander = Character?.GetCommander() ?? 0,
            agent = Character?.GetAgent() ?? 0,
            emmissary = Character?.GetEmmissary() ?? 0,
            mage = Character?.GetMage() ?? 0,
            health = Character?.health ?? 0,
            nearestEnemyStrength = closestEnemy.Strength,
            nearestNonNeutralStrength = closestNonNeutralEnemy.Strength
        };
    }

    private struct ResourceSnapshot
    {
        public int gold;
        public int goldPerTurn;
        public int leather;
        public int timber;
        public int iron;
        public int mounts;
        public int mithril;
        public int leatherPerTurn;
        public int timberPerTurn;
        public int ironPerTurn;
        public int mountsPerTurn;
        public int mithrilPerTurn;
        public int armyOffence;
        public int armyDefence;
        public int commander;
        public int agent;
        public int emmissary;
        public int mage;
        public int health;
        public float nearestEnemyStrength;
        public float nearestNonNeutralStrength;
    }

    private Dictionary<PlayableLeader, int> CaptureVictoryPointsSnapshot()
    {
        Game game = Game.Instance;
        if (game == null) return new();
        return VictoryPoints.CalculateForAll(game)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.RelativeScore);
    }

    private static int GetVictoryPoints(Dictionary<PlayableLeader, int> snapshot, PlayableLeader leader)
    {
        if (snapshot == null || leader == null) return 0;
        return snapshot.TryGetValue(leader, out int value) ? value : 0;
    }

    private static List<string> BuildOpponentVpDeltas(Dictionary<PlayableLeader, int> post, Dictionary<PlayableLeader, int> pre, PlayableLeader self)
    {
        List<string> result = new();
        if (post == null) return result;
        foreach (var kvp in post)
        {
            PlayableLeader leader = kvp.Key;
            if (leader == null || leader == self) continue;
            int before = pre != null && pre.TryGetValue(leader, out int v) ? v : 0;
            int delta = kvp.Value - before;
            result.Add($"{leader.characterName}|{delta}");
        }
        return result;
    }
}
