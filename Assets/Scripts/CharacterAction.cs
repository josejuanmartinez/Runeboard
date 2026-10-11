using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class CharacterAction
{
    [Header("IDs")]
    public string actionName;
    public string description;
    [HideInInspector] public Character character;
    [HideInInspector] public CardData card;

    [Header("Failure rate (0-100) %")]
    public int difficulty = 0;
    [Header("Required skill")]
    public int commanderSkillRequired;
    public int agentSkillRequired;
    public int emissarySkillRequired;
    public int mageSkillRequired;

    [Header("Cost")]
    public int leatherCost;
    public int mountsCost;
    public int timberCost;
    public int ironCost;
    public int steelCost;
    public int mithrilCost;
    public int goldCost;
    public bool isBuyCaravans;
    public bool isSellCaravans;

    [Header("Granted")]
    public int leatherGranted;
    public int mountsGranted;
    public int timberGranted;
    public int ironGranted;
    public int steelGranted;
    public int mithrilGranted;
    public int goldGranted;

    [Header("XP Chance (0-100) %")]
    public int commanderXP;
    public int agentXP;
    public int emmissaryXP;
    public int mageXP;

    [Header("XP Rewarded")]
    public int reward = 1;

    [Header("Availability")]
    // Function delegate that returns a bool to determine if action is available
    public Func<Character, bool> condition;

    [Header("Effect")]
    // Function delegate that returns a bool to determine if action is available
    public Func<Character, bool> effect;
    // Optional async effect for actions that require awaiting UI/input
    public Func<Character, Task<bool>> asyncEffect;

    public bool LastExecutionSucceeded { get; private set; }

    private Game game;
    private StoresManager cachedStoresManager;
    private static int defaultActionNameFrame = -1;
    private static string cachedDefaultActionName;

    protected virtual bool ConsumesAction => true;

    // Called every turn by EnvironmentalCardManager while this card is the active environmental card.
    public virtual void ApplyOngoingEffect() { }

    // Which alignment played the active environmental card (reads the live ActiveCard).
    protected static AlignmentEnum GetCasterAlignment()
    {
        int? a = EnvironmentalCardManager.Instance?.ActiveCard?.alignment;
        return a switch { 0 => AlignmentEnum.freePeople, 1 => AlignmentEnum.darkServants, _ => AlignmentEnum.neutral };
    }

    protected static T FindAnyObjectByType<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindAnyObjectByType<T>();
    }

    private Game GetGame()
    {
        if (game == null) game = Game.Instance;
        return game;
    }

    private void RefreshVictoryPoints()
    {
        Game g = GetGame();
        VictoryPoints.RecalculateAndAssign(g);
    }

    // Passive/automatic grants (see MaterialRetrieval) fire constantly as AI leaders re-enter
    // their own PCs/regions every turn — a generic "AI did something visible" floating text +
    // event icon for each of these is noise, not information. Rumours are still recorded above
    // regardless, only the player-visible notification is opted out of.
    protected virtual bool ShowsVisibleAiActionNotification => true;

    private static string GetDefaultActionName()
    {
        if (defaultActionNameFrame == Time.frameCount)
        {
            return cachedDefaultActionName;
        }

        defaultActionNameFrame = Time.frameCount;
        ActionsManager actionsManager = ActionsManager.Instance;
        cachedDefaultActionName = actionsManager != null && actionsManager.DEFAULT != null
            ? actionsManager.DEFAULT.actionName
            : null;
        return cachedDefaultActionName;
    }

    public virtual void Initialize(Character character, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, Task<bool>> asyncEffect = null)
    {
        try
        {
            var originalCondition = condition;
            this.character = character;

            string defaultActionName = GetDefaultActionName();

            this.condition = (character) => { return
                character != null && !character.killed
                // The per-character action flag belongs to the human turn flow. AI turns are
                // limited by AIDifficultySettings in AITurnController instead, so an AI card
                // must never become unavailable merely because an earlier order was executed.
                && (!character.isPlayerControlled
                    || character.CanPlayerActNow()
                    || (!string.IsNullOrWhiteSpace(defaultActionName) && this.actionName == defaultActionName))
                && (originalCondition == null || originalCondition(character));
            };
            this.effect = effect;
            this.asyncEffect = asyncEffect;
        }
        catch (Exception e)
        {
            Debug.LogError($"Unable to Initialize CharacterAction {actionName}");
            Debug.LogError(e.ToString());
        }
    }

    public virtual void Initialize(Character character, CardData card, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, Task<bool>> asyncEffect = null)
    {
        try
        {
            this.character = character;
            this.card = card;

            if (card != null)
            {
                this.actionName = card.name;
                this.description = card.GetDescriptionBody();
                this.difficulty = card.difficulty;
                this.commanderSkillRequired = card.commanderSkillRequired;
                this.agentSkillRequired = card.agentSkillRequired;
                this.emissarySkillRequired = card.emissarySkillRequired;
                this.mageSkillRequired = card.mageSkillRequired;
                this.leatherCost = card.leatherRequired;
                this.mountsCost = card.mountsRequired;
                this.timberCost = card.timberRequired;
                this.ironCost = card.ironRequired;
                this.steelCost = card.steelRequired;
                this.mithrilCost = card.mithrilRequired;
                this.goldCost = card.goldRequired;
                this.leatherGranted = card.leatherGranted;
                this.mountsGranted = card.mountsGranted;
                this.timberGranted = card.timberGranted;
                this.ironGranted = card.ironGranted;
                this.steelGranted = card.steelGranted;
                this.mithrilGranted = card.mithrilGranted;
                this.goldGranted = card.goldGranted;
            }

            Initialize(character, condition, effect, asyncEffect);
        }
        catch (Exception e)
        {
            Debug.LogError($"Unable to Initialize CharacterAction {actionName}");
            Debug.LogError(e.ToString());
        }
    }

    public bool FulfillsConditions()
    {
        return condition != null && condition(character);
    }

    public virtual bool IsRoleEligible(Character character)
    {
        return true;
    }

    public virtual bool ShouldShowWhenUnavailable()
    {
        return true;
    }

    public void UpdateHoverText(bool isAvailable)
    {
    }

    public void Reset()
    {
        character = null;
        card = null;
        condition = null;
        effect = null;
        asyncEffect = null;
    }

    public bool ResourcesAvailable()
    {
        return true;
    }

    public void Fail(bool isAI)
    {
        if (this is Spell)
        {
            ApplySpellFailurePenalty(isAI);
        }
        if (!isAI)
        {
            Sounds.Instance?.PlayActionFail();
        }
        if (isAI && PlayerCanSeeHex(character != null ? character.hex : null))
        {
            BoardNavigator.Instance?.EnqueueEnemyFocus(character.hex, character.GetOwner());
        }
        if (!isAI && game != null) game.PointToCharacterWithMissingActions();
        if (!isAI) FindAnyObjectByType<Layout>().GetSelectedCharacterIcon().Refresh(character);
        if (!isAI) Card.RequestInteractionRefreshAll();
        return;
    }

    // Chance for a card that leans on a skill requirement to also sharpen that skill on
    // successful play. One independent roll per required skill (almost always just one).
    // Riskier (higher-difficulty) cards pay off more: 5% at difficulty 0 up to 15% at 100.
    private const int SkillLevelUpChanceBasePercent = 5;
    private const int SkillLevelUpChanceMaxPercent = 15;

    private int GetSkillLevelUpChancePercent()
    {
        return Mathf.Clamp(
            SkillLevelUpChanceBasePercent + Mathf.RoundToInt(difficulty * 0.10f),
            SkillLevelUpChanceBasePercent,
            SkillLevelUpChanceMaxPercent);
    }

    private void RollForSkillLevelUp()
    {
        if (character == null || character.killed) return;
        int chance = GetSkillLevelUpChancePercent();
        if (commanderSkillRequired > 0 && UnityEngine.Random.Range(0, 100) < chance)
            character.AddCommander(1);
        if (agentSkillRequired > 0 && UnityEngine.Random.Range(0, 100) < chance)
            character.AddAgent(1);
        if (emissarySkillRequired > 0 && UnityEngine.Random.Range(0, 100) < chance)
            character.AddEmmissary(1);
        if (mageSkillRequired > 0 && UnityEngine.Random.Range(0, 100) < chance)
            character.AddMage(1);
    }

    // Each skill level above what the card requires shaves this much off the failure chance,
    // so seasoned characters are the reliable ones (shown on the card hover as base success).
    public const int SkillMasteryPercentPerLevel = 5;

    public int GetSkillMastery(Character c)
    {
        if (c == null) return 0;
        int best = 0;
        if (commanderSkillRequired > 0) best = Mathf.Max(best, c.GetCommander() - commanderSkillRequired);
        if (agentSkillRequired > 0) best = Mathf.Max(best, c.GetAgent() - agentSkillRequired);
        if (emissarySkillRequired > 0) best = Mathf.Max(best, c.GetEmmissary() - emissarySkillRequired);
        if (mageSkillRequired > 0) best = Mathf.Max(best, c.GetMage() - mageSkillRequired);
        return Mathf.Max(0, best);
    }

    public async Task Execute()
    {
        // Snapshotted once: CharacterAction instances are cached/shared per class by
        // ActionsManager (see ResolveActionByRef), and Card.RequestInteractionRefreshAll()
        // below re-Initializes every active hand card's action (possibly this very
        // instance, for a different character) as a side effect of refreshing hand UI.
        // Reading the field again after that point could silently swap to the wrong
        // character mid-execution — the local keeps this call consistent throughout.
        Character character = this.character;
        bool isAI = !character.isPlayerControlled;
        LastExecutionSucceeded = false;
        try
        {
            if (!isAI)
            {
                Sounds.Instance?.PlayActionExecute();
            }
            Hex actionHex = character.hex;
            ActionCostSnapshot caravanSnapshot = (isBuyCaravans || isSellCaravans)
                ? CalculateCostSnapshot(GetResourceCostProfile(character.GetOwner()))
                : default;
            // Only human-controlled characters use the one-action-per-turn flag. AI order
            // count is enforced by AITurnController according to the selected difficulty.
            if (!isAI && ConsumesAction)
            {
                character.hasActionedThisTurn = true;
                (character.GetOwner() as PlayableLeader)?.SpendCommandPoint();
            }

            bool failed = false;
            int effectiveDifficulty = difficulty;
            // Penalties (guard defenses, AI acting on an unscouted hex) are applied first, so
            // artifact/temporary reductions below have something real to offset. Reducing before
            // the penalties were added made those reductions a no-op whenever base difficulty
            // was already 0 (e.g. Find Artifact) — exactly the case where a guarded hex most
            // needed them to matter. See balance review, 2026-08-07.
            if (isAI && ShouldApplyUnscoutedPenalty(character))
            {
                effectiveDifficulty = Mathf.Min(100, effectiveDifficulty + 25);
            }
            effectiveDifficulty = Mathf.Min(100, effectiveDifficulty + GetGuardPenalty(character, actionHex));
            if (character != null)
            {
                int artifactReduction = character.GetArtifactActionDifficultyReduction(GetType().Name);
                int temporaryReduction = character.GetTemporaryActionDifficultyReduction(GetType().Name, actionHex);
                effectiveDifficulty = Mathf.Max(0, effectiveDifficulty - artifactReduction - temporaryReduction);
                if (character.HasStatusEffect(StatusEffectEnum.ArcaneInsight) && GetType().Name == nameof(FindArtifact))
                {
                    effectiveDifficulty = Mathf.Max(0, effectiveDifficulty - 50);
                }
                effectiveDifficulty = Mathf.Max(0, effectiveDifficulty - GetSkillMastery(character) * SkillMasteryPercentPerLevel);
            }
            bool failedByChance = UnityEngine.Random.Range(0, 100) < effectiveDifficulty;
            character?.ConsumeTemporaryActionDifficultyReduction(GetType().Name, actionHex);

            if (failedByChance)
            {
                Fail(isAI);
                return;
            }

            if (effect != null) failed = !effect(character);

            if (!failed && asyncEffect != null) failed = !await asyncEffect(character);

            if (failed)
            {
                Fail(isAI);
                return;
            }

            RollForSkillLevelUp();

            if (!isAI && ConsumesAction && character.HasStatusEffect(StatusEffectEnum.Hope) && UnityEngine.Random.Range(0, 100) < 25)
            {
                character.hasActionedThisTurn = false;
                MessageDisplayNoUI.ShowMessage(character.hex, character, "Hope preserves the action.", Color.green);
            }

            // Card-authored status effects are no longer part of exposed card design.
            // Status effects remain internal gameplay state only.

            string message = actionName;
            string tradeMessage = BuildCaravanTransactionMessage(caravanSnapshot);
            string rumourMessage = !string.IsNullOrWhiteSpace(tradeMessage)
                ? tradeMessage
                : $"succeeds on {message}";
            bool isDoubledByPlayer = game != null
                && game.player != null
                && character.doubledBy != null
                && character.doubledBy.Contains(game.player);
            if (isAI)
            {
                // Always record AI actions privately; if doubled by the player, also promote to public immediately
                Rumour rumour = new Rumour() { leader = character.GetOwner(), character = character, characterName = character.characterName, rumour = rumourMessage, v2 = character.hex.v2 };
                RumoursManager.AddRumour(rumour, false);
                // Exact trade quantities and proceeds are always private intelligence. Even a
                // doubled character does not turn a nation's market ledger into public news.
                if (isDoubledByPlayer && string.IsNullOrWhiteSpace(tradeMessage))
                {
                    RumoursManager.PromoteRumourToPublic(rumour);
                }
                if (ShowsVisibleAiActionNotification && string.IsNullOrWhiteSpace(tradeMessage) && PlayerCanSeeHex(character.hex))
                {
                    MessageDisplayNoUI.ShowMessage(character.hex, character, message, Color.yellow);
                    BoardNavigator.Instance?.EnqueueEnemyFocus(character.hex, character.GetOwner());
                }
            }
            else if (!string.IsNullOrWhiteSpace(tradeMessage))
            {
                // The acting player sees their own ledger entry, while the underlying rumour
                // remains private and can never be exposed as public market news.
                Rumour tradeRumour = new Rumour { leader = character.GetOwner(), character = character, characterName = character.characterName, rumour = tradeMessage, v2 = character.hex.v2 };
                RumoursManager.AddRumour(tradeRumour, false, logToWidget: false);
                LogManager.Log(LogCategory.Rumour, character.GetOwner()?.characterName, character.characterName, tradeMessage);
            }
            if (!isAI) FindAnyObjectByType<Layout>().GetSelectedCharacterIcon().Refresh(character);
            if (!isAI) Card.RequestInteractionRefreshAll();

            StoresManager storesManager = GetStoresManager();
            if (!isAI && storesManager != null) storesManager.RefreshStores();

            if (!isAI && game != null)
            {
                game.PointToCharacterWithMissingActions();
            }

            RefreshVictoryPoints();
            LastExecutionSucceeded = true;
            if (!isAI)
            {
                Sounds.Instance?.PlayActionSuccess(actionName);
                Sounds.Instance?.PlayVoiceForAction(character, actionName);
            }
            return;
        }
        catch (Exception e)
        {
            Debug.Log(e);
            if (!isAI) FindAnyObjectByType<Layout>().GetSelectedCharacterIcon().Refresh(character);
            if (!isAI) Card.RequestInteractionRefreshAll();

            // Debug.LogError($"{character.characterName} was unable to Execute action {actionName} {actionId} {actionInitials} {e}");
            if (!isAI) character.hasActionedThisTurn = true;
            return;
        }
    }

    // Wrapper so Unity UI Buttons (which require void return) can trigger this async action
    public async void ExecuteFromButton()
    {
        await Execute();
    }

    protected bool IsCharacterKnownAtHex(Character actor, Character target)
    {
        if (actor == null || target == null || actor.hex == null) return false;
        if (target.IsArmyCommander()) return true;
        Leader actorOwner = actor.GetOwner();
        if (actorOwner == null) return false;

        if (target.GetOwner() == actorOwner) return true;

        AlignmentEnum actorAlignment = actor.GetAlignment();
        if (actorAlignment != AlignmentEnum.neutral && target.GetAlignment() == actorAlignment) return true;
        if (target.HasStatusEffect(StatusEffectEnum.Hidden)) return false;

        return ShouldIgnoreScouting(actorOwner) || actor.hex.IsScoutedBy(actorOwner);
    }

    private int GetGuardPenalty(Character actor, Hex hex)
    {
        if (actor == null || hex == null) return 0;
        Leader actorOwner = actor.GetOwner();
        int penalty = 0;

        PC pc = hex.GetPC();
        if (pc != null && pc.guardLevel > 0 && pc.owner != null && pc.owner != actorOwner)
            penalty = Mathf.Max(penalty, pc.guardLevel * 10);

        if (hex.characters != null)
        {
            foreach (Character ch in hex.characters)
            {
                if (ch == null || ch.killed || ch == actor) continue;
                if (ch.GetOwner() == actorOwner) continue;
                if (ch.guardLevel > 0)
                    penalty = Mathf.Max(penalty, ch.guardLevel * 10);
            }
        }

        return penalty;
    }

    private bool ShouldApplyUnscoutedPenalty(Character actor)
    {
        if (actor == null || actor.hex == null) return false;
        Leader owner = actor.GetOwner();
        if (owner == null) return false;
        if (!ShouldIgnoreScouting(owner)) return false;
        return !actor.hex.IsScoutedBy(owner);
    }

    private bool ShouldIgnoreScouting(Leader leader)
    {
        if (leader == null) return false;
        if (leader is NonPlayableLeader) return true;
        Game g = GetGame();
        if (leader is PlayableLeader pl && g != null && g.player != pl) return true;
        return false;
    }

    protected Character FindEnemyCharacterTargetAtHex(Character c)
    {
        Character target = FindEnemyNonNeutralCharactersAtHexNoLeader(c);
        if (target) return target;
        target = FindEnemyCharactersAtHexNoLeaders(c);
        if (target) return target;
        target = FindEnemyNonNeutralCharactersAtHex(c);
        if (target) return target;
        target = FindEnemyCharacterAtHex(c);
        return target;
    }
    protected Character FindEnemyNonNeutralCharactersAtHexNoLeader(Character c)
    {
        // Always prioritize free people or dark servants  (but leaders will be difficult as they will be guarded)
        return c.hex.characters.Find(
            x => IsCharacterKnownAtHex(c, x) &&
            !x.IsHidden() &&
            x.GetOwner() != c.GetOwner() &&
            x.GetAlignment() != c.GetAlignment() &&
            x.GetAlignment() != AlignmentEnum.neutral &&
            x is not PlayableLeader
        );
    }
    protected Character FindEnemyCharactersAtHexNoLeaders(Character c)
    {
        // Always prioritize free people or dark servants  (but leaders will be difficult as they will be guarded)
        return c.hex.characters.Find(
            x => IsCharacterKnownAtHex(c, x) &&
            !x.IsHidden() &&
            x.GetOwner() != c.GetOwner() &&
            (x.GetAlignment() == AlignmentEnum.neutral || x.GetAlignment() != c.GetAlignment()) && (x is not PlayableLeader)
        );
    }

    protected Character FindEnemyNonNeutralCharactersAtHex(Character c)
    {
        // Always prioritize free people or dark servants  (but leaders will be difficult as they will be guarded)
        return c.hex.characters.Find(
            x => IsCharacterKnownAtHex(c, x) &&
            !x.IsHidden() &&
            x.GetOwner() != c.GetOwner() &&
            x.GetAlignment() != c.GetAlignment() &&
            x.GetAlignment() != AlignmentEnum.neutral
        );
    }

    protected Character FindEnemyCharacterAtHex(Character c)
    {
        // Always prioritize free people or dark servants  (but leaders will be difficult as they will be guarded)
        return c.hex.characters.Find(
            x => IsCharacterKnownAtHex(c, x) &&
            !x.IsHidden() &&
            x.GetOwner() != c.GetOwner() &&
            (x.GetAlignment() == AlignmentEnum.neutral || x.GetAlignment() != c.GetAlignment())
        );
    }
    protected List<Character> FindEnemyCharactersAtHex(Character c)
    {
        // Always prioritize free people or dark servants  (but leaders will be difficult as they will be guarded)
        return c.hex.characters.FindAll(
            x => IsCharacterKnownAtHex(c, x) &&
            !x.IsHidden() &&
            x.GetOwner() != c.GetOwner() &&
            (x.GetAlignment() == AlignmentEnum.neutral || x.GetAlignment() != c.GetAlignment())
        );
    }
    protected List<Character> FindEnemyCharactersNotArmyCommandersAtHex(Character c)
    {
        // Always prioritize free people or dark servants  (but leaders will be difficult as they will be guarded)
        return c.hex.characters.FindAll(
            x => IsCharacterKnownAtHex(c, x) &&
            !x.IsHidden() &&
            !x.IsArmyCommander() && x.GetOwner() != c.GetOwner() &&
            (x.GetAlignment() == AlignmentEnum.neutral || x.GetAlignment() != c.GetAlignment())
        );
    }

    protected Army FindEnemyArmyNotNeutralAtHex(Character c)
    {
        Character commander = c.hex.characters.Find(
            x => IsCharacterKnownAtHex(c, x) &&
            x.IsArmyCommander() && x.GetOwner() != c.GetOwner() &&
            (x.GetAlignment() != AlignmentEnum.neutral && x.GetAlignment() != c.GetAlignment())
        );
        if (commander != null && commander.GetArmy() != null) return commander.GetArmy();
        return null;
    }
    protected Army FindEnemyArmyAtHex(Character c)
    {
        Character commander = c.hex.characters.Find(
            x => IsCharacterKnownAtHex(c, x) &&
            x.IsArmyCommander() && x.GetOwner() != c.GetOwner() &&
            (x.GetAlignment() != c.GetAlignment())
        );
        if (commander != null && commander.GetArmy() != null) return commander.GetArmy();
        return null;
    }

    protected Army FindFriendlyArmyAtHex(Character c)
    {
        Character commander = c.hex.characters.Find(
            x => IsCharacterKnownAtHex(c, x) &&
            x.IsArmyCommander() && (x.GetOwner() == c.GetOwner() || (x.GetAlignment() == c.GetAlignment() && x.GetAlignment() != AlignmentEnum.neutral))
        );
        if (commander != null && commander.GetArmy() != null) return commander.GetArmy();
        return null;
    }

    protected PC FindEnemyTargetPCInRange(List<Hex> hexes, Character character)
    {
        // Get potential targets from each category and track their priority
        List<TargetScore> allCandidates = new List<TargetScore>();

        // Category 1: Non-neutral PCs that aren't capitals (highest priority)
        PC target1 = FindNonNeutralEnemyTargetPCNoCapitalInRange(hexes, character);
        if (target1 != null)
        {
            float distance = Vector2.Distance(character.hex.v2, target1.hex.v2);
            float defense = target1.GetDefense();
            float score = distance + defense;
            allCandidates.Add(new TargetScore
            {
                Target = target1,
                TotalScore = score,
                CategoryPriority = 1 // Highest priority
            });
        }

        // Category 2: Any PC that isn't a capital
        PC target2 = FindEnemyTargetPCNoCapitalsInRange(hexes, character);
        if (target2 != null)
        {
            float distance = Vector2.Distance(character.hex.v2, target2.hex.v2);
            float defense = target2.GetDefense();
            float score = distance + defense;
            allCandidates.Add(new TargetScore
            {
                Target = target2,
                TotalScore = score,
                CategoryPriority = 2
            });
        }

        // Category 3: Non-neutral PCs
        PC target3 = FindEnemyTargetNonNeutralPCInRange(hexes, character);
        if (target3 != null)
        {
            float distance = Vector2.Distance(character.hex.v2, target3.hex.v2);
            float defense = target3.GetDefense();
            float score = distance + defense;
            allCandidates.Add(new TargetScore
            {
                Target = target3,
                TotalScore = score,
                CategoryPriority = 3
            });
        }

        // Category 4: Any PC
        PC target4 = FindEnemyTargetPCNoRestrictionsInRange(hexes, character);
        if (target4 != null)
        {
            float distance = Vector2.Distance(character.hex.v2, target4.hex.v2);
            float defense = target4.GetDefense();
            float score = distance + defense;
            allCandidates.Add(new TargetScore
            {
                Target = target4,
                TotalScore = score,
                CategoryPriority = 4 // Lowest priority
            });
        }

        // Return null if no targets found
        if (allCandidates.Count == 0)
        {
            return null;
        }

        // Sort by score (lowest is best)
        allCandidates = allCandidates.OrderBy(c => c.TotalScore).ToList();

        // Get best candidate
        TargetScore bestCandidate = allCandidates[0];
        float bestScore = bestCandidate.TotalScore;

        // Check if any scores are within 10% of the best score
        // If so, use category priority as a tiebreaker
        List<TargetScore> closeScoreCandidates = new List<TargetScore>();
        closeScoreCandidates.Add(bestCandidate);

        for (int i = 1; i < allCandidates.Count; i++)
        {
            float scoreDifference = allCandidates[i].TotalScore - bestScore;
            float percentDifference = bestScore > 0 ? (scoreDifference / bestScore) * 100 : 100;

            // If score is within 10% of best score, add to close candidates
            if (percentDifference <= 10)
            {
                closeScoreCandidates.Add(allCandidates[i]);
            }
        }

        // If we have multiple candidates with close scores, sort by category priority
        if (closeScoreCandidates.Count > 1)
        {
            return closeScoreCandidates.OrderBy(c => c.CategoryPriority).First().Target;
        }

        // Otherwise, return the candidate with the best score
        return bestCandidate.Target;
    }

    // Helper class to track potential targets with their scores
    private class TargetScore
    {
        public PC Target { get; set; }
        public float DistanceScore { get; set; }
        public float DefenseScore { get; set; }
        public float TotalScore { get; set; }
        public int CategoryPriority { get; set; } // 1-4, lower is higher priority
    }

    private PC FindNonNeutralEnemyTargetPCNoCapitalInRange(List<Hex> hexes, Character character)
    {
        // Prioritize non-neutral PCs that aren't capitals
        List<TargetScore> potentialTargets = new List<TargetScore>();

        foreach (Hex hex in hexes)
        {
            PC pc = hex.GetPC();
            if (pc != null &&
                pc.owner != null &&
                pc.owner != character.GetOwner() &&
                pc.owner.GetAlignment() != character.GetAlignment() &&
                pc.owner.GetAlignment() != AlignmentEnum.neutral &&
                !pc.isCapital)
            {
                // Calculate scores (lower is better)
                float distance = Vector2.Distance(character.hex.v2, hex.v2);
                float distanceScore = distance; // Lower distance = better score
                float defenseScore = pc.GetDefense(); // Lower defense = better score

                // Combine scores (lower total = better target)
                float totalScore = distanceScore + defenseScore;

                potentialTargets.Add(new TargetScore
                {
                    Target = pc,
                    DistanceScore = distanceScore,
                    DefenseScore = defenseScore,
                    TotalScore = totalScore
                });
            }
        }

        // Return the target with the lowest total score (best target)
        if (potentialTargets.Count > 0)
        {
            return potentialTargets.OrderBy(t => t.TotalScore).First().Target;
        }

        return null;
    }

    private PC FindEnemyTargetPCNoCapitalsInRange(List<Hex> hexes, Character character)
    {
        // Find any PC that isn't a capital, including neutral ones
        List<TargetScore> potentialTargets = new List<TargetScore>();

        foreach (Hex hex in hexes)
        {
            PC pc = hex.GetPC();
            if (pc != null &&
                pc.owner != null &&
                pc.owner != character.GetOwner() &&
                (pc.owner.GetAlignment() == AlignmentEnum.neutral || pc.owner.GetAlignment() != character.GetAlignment()) &&
                !pc.isCapital)
            {
                // Calculate scores (lower is better)
                float distance = Vector2.Distance(character.hex.v2, hex.v2);
                float distanceScore = distance; // Lower distance = better score
                float defenseScore = pc.GetDefense(); // Lower defense = better score

                // Combine scores (lower total = better target)
                float totalScore = distanceScore + defenseScore;

                potentialTargets.Add(new TargetScore
                {
                    Target = pc,
                    DistanceScore = distanceScore,
                    DefenseScore = defenseScore,
                    TotalScore = totalScore
                });
            }
        }

        // Return the target with the lowest total score (best target)
        if (potentialTargets.Count > 0)
        {
            return potentialTargets.OrderBy(t => t.TotalScore).First().Target;
        }

        return null;
    }

    private PC FindEnemyTargetNonNeutralPCInRange(List<Hex> hexes, Character character)
    {
        // Prioritize non-neutral PCs, including capitals
        List<TargetScore> potentialTargets = new List<TargetScore>();

        foreach (Hex hex in hexes)
        {
            PC pc = hex.GetPC();
            if (pc != null &&
                pc.owner != null &&
                pc.owner != character.GetOwner() &&
                pc.owner.GetAlignment() != character.GetAlignment() &&
                pc.owner.GetAlignment() != AlignmentEnum.neutral)
            {
                // Calculate scores (lower is better)
                float distance = Vector2.Distance(character.hex.v2, hex.v2);
                float distanceScore = distance; // Lower distance = better score
                float defenseScore = pc.GetDefense(); // Lower defense = better score

                // Combine scores (lower total = better target)
                float totalScore = distanceScore + defenseScore;

                potentialTargets.Add(new TargetScore
                {
                    Target = pc,
                    DistanceScore = distanceScore,
                    DefenseScore = defenseScore,
                    TotalScore = totalScore
                });
            }
        }

        // Return the target with the lowest total score (best target)
        if (potentialTargets.Count > 0)
        {
            return potentialTargets.OrderBy(t => t.TotalScore).First().Target;
        }

        return null;
    }

    private PC FindEnemyTargetPCNoRestrictionsInRange(List<Hex> hexes, Character character)
    {
        // Find any PC, including neutral ones and capitals
        List<TargetScore> potentialTargets = new List<TargetScore>();

        foreach (Hex hex in hexes)
        {
            PC pc = hex.GetPC();
            if (pc != null &&
                pc.owner != null &&
                pc.owner != character.GetOwner() &&
                (pc.owner.GetAlignment() == AlignmentEnum.neutral || pc.owner.GetAlignment() != character.GetAlignment()))
            {
                // Calculate scores (lower is better)
                float distance = Vector2.Distance(character.hex.v2, hex.v2);
                float distanceScore = distance; // Lower distance = better score
                float defenseScore = pc.GetDefense(); // Lower defense = better score

                // Combine scores (lower total = better target)
                float totalScore = distanceScore + defenseScore;

                potentialTargets.Add(new TargetScore
                {
                    Target = pc,
                    DistanceScore = distanceScore,
                    DefenseScore = defenseScore,
                    TotalScore = totalScore
                });
            }
        }

        // Return the target with the lowest total score (best target)
        if (potentialTargets.Count > 0)
        {
            return potentialTargets.OrderBy(t => t.TotalScore).First().Target;
        }

        return null;
    }

    protected Army FindTargetEnemyArmyInRange(List<Hex> hexes, Character character)
    {
        // Get potential targets from each category
        List<ArmyTargetScore> allCandidates = new List<ArmyTargetScore>();

        // Category 1: Non-neutral Armies not in PCs (highest priority)
        Army target1 = FindTargetNonNeutralArmyNotInPCInRange(hexes, character);
        if (target1 != null)
        {
            float distance = Vector2.Distance(character.hex.v2, target1.commander.hex.v2);
            float strength = CalculateArmyStrength(target1);
            float score = distance + strength;
            allCandidates.Add(new ArmyTargetScore
            {
                Target = target1,
                TotalScore = score,
                CategoryPriority = 1 // Highest priority
            });
        }

        // Category 2: Any Army not in PC
        Army target2 = FindTargetAnyArmyNotInPCInRange(hexes, character);
        if (target2 != null)
        {
            float distance = Vector2.Distance(character.hex.v2, target2.commander.hex.v2);
            float strength = CalculateArmyStrength(target2);
            float score = distance + strength;
            allCandidates.Add(new ArmyTargetScore
            {
                Target = target2,
                TotalScore = score,
                CategoryPriority = 2
            });
        }

        // Category 3: Non-neutral Army (including in PCs)
        Army target3 = FindTargetNonNeutralArmyInRange(hexes, character);
        if (target3 != null)
        {
            float distance = Vector2.Distance(character.hex.v2, target3.commander.hex.v2);
            float strength = CalculateArmyStrength(target3);
            float score = distance + strength;
            allCandidates.Add(new ArmyTargetScore
            {
                Target = target3,
                TotalScore = score,
                CategoryPriority = 3
            });
        }

        // Category 4: Any Army
        Army target4 = FindTargetAnyEnemyArmyInRange(hexes, character);
        if (target4 != null)
        {
            float distance = Vector2.Distance(character.hex.v2, target4.commander.hex.v2);
            float strength = CalculateArmyStrength(target4);
            float score = distance + strength;
            allCandidates.Add(new ArmyTargetScore
            {
                Target = target4,
                TotalScore = score,
                CategoryPriority = 4 // Lowest priority
            });
        }

        // Return null if no targets found
        if (allCandidates.Count == 0)
        {
            return null;
        }

        // Sort by score (lowest is best)
        allCandidates = allCandidates.OrderBy(c => c.TotalScore).ToList();

        // Get best candidate
        ArmyTargetScore bestCandidate = allCandidates[0];
        float bestScore = bestCandidate.TotalScore;

        // Check if any scores are within 10% of the best score
        // If so, use category priority as a tiebreaker
        List<ArmyTargetScore> closeScoreCandidates = new List<ArmyTargetScore>();
        closeScoreCandidates.Add(bestCandidate);

        for (int i = 1; i < allCandidates.Count; i++)
        {
            float scoreDifference = allCandidates[i].TotalScore - bestScore;
            float percentDifference = bestScore > 0 ? (scoreDifference / bestScore) * 100 : 100;

            // If score is within 10% of best score, add to close candidates
            if (percentDifference <= 10)
            {
                closeScoreCandidates.Add(allCandidates[i]);
            }
        }

        // If we have multiple candidates with close scores, sort by category priority
        if (closeScoreCandidates.Count > 1)
        {
            return closeScoreCandidates.OrderBy(c => c.CategoryPriority).First().Target;
        }

        // Otherwise, return the candidate with the best score
        return bestCandidate.Target;
    }

    // Helper class to track potential army targets with their scores
    private class ArmyTargetScore
    {
        public Army Target { get; set; }
        public float DistanceScore { get; set; }
        public float StrengthScore { get; set; }
        public float TotalScore { get; set; }
        public int CategoryPriority { get; set; } // 1-4, lower is higher priority
    }

    // Helper method to calculate army strength (lower is better for attacker)
    private float CalculateArmyStrength(Army army)
    {
        return army.GetDefence();
    }

    private Army FindTargetNonNeutralArmyNotInPCInRange(List<Hex> hexes, Character character)
    {
        // Prioritize non-neutral Armies that aren't in PCs
        List<ArmyTargetScore> potentialTargets = new List<ArmyTargetScore>();

        foreach (Hex hex in hexes)
        {
            if (hex.armies != null && hex.armies.Count > 0 && hex.GetPC() == null)
            {
                foreach (Army army in hex.armies)
                {
                    if (army.commander != null &&
                        army.commander.GetOwner() != character.GetOwner() &&
                        army.commander.GetOwner().GetAlignment() != character.GetAlignment() &&
                        army.commander.GetOwner().GetAlignment() != AlignmentEnum.neutral)
                    {
                        // Calculate scores (lower is better)
                        float distance = Vector2.Distance(character.hex.v2, hex.v2);
                        float strengthScore = CalculateArmyStrength(army);

                        // Combine scores (lower total = better target)
                        float totalScore = distance + strengthScore;

                        potentialTargets.Add(new ArmyTargetScore
                        {
                            Target = army,
                            DistanceScore = distance,
                            StrengthScore = strengthScore,
                            TotalScore = totalScore
                        });
                    }
                }
            }
        }

        // Return the target with the lowest total score (best target)
        if (potentialTargets.Count > 0)
        {
            return potentialTargets.OrderBy(t => t.TotalScore).First().Target;
        }

        return null;
    }

    private Army FindTargetAnyArmyNotInPCInRange(List<Hex> hexes, Character character)
    {
        // Find any Army that isn't in a PC, including neutral ones
        List<ArmyTargetScore> potentialTargets = new List<ArmyTargetScore>();

        foreach (Hex hex in hexes)
        {
            if (hex.armies != null && hex.armies.Count > 0 && hex.GetPC() == null)
            {
                foreach (Army army in hex.armies)
                {
                    if (army.commander != null &&
                        army.commander.GetOwner() != character.GetOwner())
                    {
                        // Calculate scores (lower is better)
                        float distance = Vector2.Distance(character.hex.v2, hex.v2);
                        float strengthScore = CalculateArmyStrength(army);

                        // Combine scores (lower total = better target)
                        float totalScore = distance + strengthScore;

                        potentialTargets.Add(new ArmyTargetScore
                        {
                            Target = army,
                            DistanceScore = distance,
                            StrengthScore = strengthScore,
                            TotalScore = totalScore
                        });
                    }
                }
            }
        }

        // Return the target with the lowest total score (best target)
        if (potentialTargets.Count > 0)
        {
            return potentialTargets.OrderBy(t => t.TotalScore).First().Target;
        }

        return null;
    }

    private Army FindTargetNonNeutralArmyInRange(List<Hex> hexes, Character character)
    {
        // Prioritize non-neutral Armies, including those in PCs
        List<ArmyTargetScore> potentialTargets = new List<ArmyTargetScore>();

        foreach (Hex hex in hexes)
        {
            if (hex.armies != null && hex.armies.Count > 0)
            {
                foreach (Army army in hex.armies)
                {
                    if (army.commander != null &&
                        army.commander.GetOwner() != character.GetOwner() &&
                        army.commander.GetOwner().GetAlignment() != character.GetAlignment() &&
                        army.commander.GetOwner().GetAlignment() != AlignmentEnum.neutral)
                    {
                        // Calculate scores (lower is better)
                        float distance = Vector2.Distance(character.hex.v2, hex.v2);
                        float strengthScore = CalculateArmyStrength(army);

                        // Combine scores (lower total = better target)
                        float totalScore = distance + strengthScore;

                        potentialTargets.Add(new ArmyTargetScore
                        {
                            Target = army,
                            DistanceScore = distance,
                            StrengthScore = strengthScore,
                            TotalScore = totalScore
                        });
                    }
                }
            }
        }

        // Return the target with the lowest total score (best target)
        if (potentialTargets.Count > 0)
        {
            return potentialTargets.OrderBy(t => t.TotalScore).First().Target;
        }

        return null;
    }

    private Army FindTargetAnyEnemyArmyInRange(List<Hex> hexes, Character character)
    {
        // Find any Army, including neutral ones and those in PCs
        List<ArmyTargetScore> potentialTargets = new List<ArmyTargetScore>();

        foreach (Hex hex in hexes)
        {
            if (hex.armies != null && hex.armies.Count > 0)
            {
                foreach (Army army in hex.armies)
                {
                    if (army.commander != null &&
                        army.commander.GetOwner() != character.GetOwner())
                    {
                        // Calculate scores (lower is better)
                        float distance = Vector2.Distance(character.hex.v2, hex.v2);
                        float strengthScore = CalculateArmyStrength(army);

                        // Combine scores (lower total = better target)
                        float totalScore = distance + strengthScore;

                        potentialTargets.Add(new ArmyTargetScore
                        {
                            Target = army,
                            DistanceScore = distance,
                            StrengthScore = strengthScore,
                            TotalScore = totalScore
                        });
                    }
                }
            }
        }

        // Return the target with the lowest total score (best target)
        if (potentialTargets.Count > 0)
        {
            return potentialTargets.OrderBy(t => t.TotalScore).First().Target;
        }

        return null;
    }

    public int GetGoldCost()
    {
        Leader owner = character != null ? character.GetOwner() : null;
        ResourceCostProfile cost = GetResourceCostProfile(owner);
        return cost.gold
        + cost.iron * StoresManager.IronSellValue
        + cost.mounts * StoresManager.MountsSellValue
        + cost.timber * StoresManager.TimberSellValue
        + cost.mithril * StoresManager.MithrilSellValue
        + cost.leather * StoresManager.LeatherSellValue;
    }

    protected virtual string GetDescription()
    {
        return string.IsNullOrWhiteSpace(description) ? string.Empty : description;
    }

    public string GetDescriptionForCard()
    {
        string desc = GetDescription();
        if (!isBuyCaravans && !isSellCaravans) return desc;

        string caravanDetail = BuildCaravanDetailText();
        if (string.IsNullOrWhiteSpace(caravanDetail)) return desc;
        if (string.IsNullOrWhiteSpace(desc)) return caravanDetail;
        return $"{desc}<br><size=80%>{caravanDetail}</size>";
    }

    private string BuildCostText()
    {
        if (isBuyCaravans) return BuildBuyCaravanCostText();
        if (isSellCaravans) return BuildSellCaravanCostText();
        return BuildStandardCostText();
    }

    private string BuildStandardCostText()
    {
        Leader owner = character != null ? character.GetOwner() : null;
        ResourceCostProfile cost = GetResourceCostProfile(owner);
        List<string> costs = new();
        if (cost.leather > 0) costs.Add($"<color=red>-<sprite name=\"leather\">[{cost.leather}]</color>");
        if (cost.timber > 0) costs.Add($"<color=red>-<sprite name=\"timber\">[{cost.timber}]</color>");
        if (cost.mounts > 0) costs.Add($"<color=red>-<sprite name=\"mounts\">[{cost.mounts}]</color>");
        if (cost.iron > 0) costs.Add($"<color=red>-<sprite name=\"iron\">[{cost.iron}]</color>");
        if (cost.steel > 0) costs.Add($"<color=red>-<sprite name=\"steel\">[{cost.steel}]</color>");
        if (cost.mithril > 0) costs.Add($"<color=red>-<sprite name=\"mithril\">[{cost.mithril}]</color>");
        if (cost.gold > 0) costs.Add($"<color=red>-<sprite name=\"gold\">[{cost.gold}]</color>");
        return string.Join(" ", costs);
    }

    private IEnumerable<(int amount, string sprite, ProducesEnum produce)> GetResourceCosts(ResourceCostProfile costProfile)
    {
        if (costProfile.leather > 0) yield return (costProfile.leather, "leather", ProducesEnum.leather);
        if (costProfile.timber > 0) yield return (costProfile.timber, "timber", ProducesEnum.timber);
        if (costProfile.mounts > 0) yield return (costProfile.mounts, "mounts", ProducesEnum.mounts);
        if (costProfile.iron > 0) yield return (costProfile.iron, "iron", ProducesEnum.iron);
        if (costProfile.steel > 0) yield return (costProfile.steel, "steel", ProducesEnum.steel);
        if (costProfile.mithril > 0) yield return (costProfile.mithril, "mithril", ProducesEnum.mithril);
    }

    private struct ResourceCostProfile
    {
        public int leather;
        public int timber;
        public int mounts;
        public int iron;
        public int steel;
        public int mithril;
        public int gold;
    }

    private struct ActionCostSnapshot
    {
        public bool isCaravan;
        public int goldDelta; // positive means gold gained, negative means gold spent
        public List<(ProducesEnum produce, int amount)> resourceCosts;
        public bool hasRequiredStock;
    }

    private ActionCostSnapshot CalculateCostSnapshot(ResourceCostProfile costProfile)
    {
        ActionCostSnapshot snapshot = new()
        {
            isCaravan = isBuyCaravans || isSellCaravans,
            goldDelta = 0,
            resourceCosts = new List<(ProducesEnum produce, int amount)>(),
            hasRequiredStock = true
        };

        if (!snapshot.isCaravan) return snapshot;

        StoresManager stores = GetStoresManager();
        if (stores == null)
        {
            snapshot.hasRequiredStock = false;
            return snapshot;
        }

        // gold requirement is always treated as a spend (negative delta)
        snapshot.goldDelta -= costProfile.gold;

        foreach (var cost in GetResourceCosts(costProfile))
        {
            snapshot.resourceCosts.Add((cost.produce, cost.amount));
            if (isBuyCaravans)
            {
                if (!stores.HasStock(cost.produce, cost.amount)) snapshot.hasRequiredStock = false;
                snapshot.goldDelta -= stores.GetBuyPrice(cost.produce, cost.amount);
            }
            else if (isSellCaravans)
            {
                snapshot.goldDelta += stores.GetSellPrice(cost.produce, cost.amount);
            }
        }

        return snapshot;
    }

    private string BuildBuyCaravanCostText()
    {
        StoresManager stores = GetStoresManager();
        if (stores == null) return BuildStandardCostText();

        Leader owner = character != null ? character.GetOwner() : null;
        ResourceCostProfile costProfile = GetResourceCostProfile(owner);
        int totalGold = costProfile.gold;
        foreach (var cost in GetResourceCosts(costProfile))
        {
            totalGold += stores.GetBuyPrice(cost.produce, cost.amount);
        }

        if (totalGold <= 0) return string.Empty;
        return $"<color=red>-<sprite name=\"gold\">[{totalGold}]</color>";
    }

    private string BuildSellCaravanCostText()
    {
        StoresManager stores = GetStoresManager();
        if (stores == null) return BuildStandardCostText();

        Leader owner = character != null ? character.GetOwner() : null;
        ResourceCostProfile costProfile = GetResourceCostProfile(owner);
        List<string> parts = new();
        int totalGold = costProfile.gold;

        foreach (var cost in GetResourceCosts(costProfile))
        {
            parts.Add($"<color=red>-<sprite name=\"{cost.sprite}\">{cost.sprite}[{cost.amount}]</color>");
            totalGold += stores.GetSellPrice(cost.produce, cost.amount);
        }

        if (totalGold > 0)
        {
            parts.Add($"<color=green>+<sprite name=\"gold\">[{totalGold}]</color>");
        }

        return string.Join(" ", parts);
    }

    protected virtual string BuildHoverText()
    {
        string strippedTitle = ActionNameUtils.StripShortcut(actionName);
        string title = string.IsNullOrWhiteSpace(strippedTitle) ? actionName : strippedTitle;

        List<string> parts = new() { title };
        string desc = GetDescription();
        if (!string.IsNullOrWhiteSpace(desc)) parts.Add($"<br><size=80%>{desc}</size>");

        string detail = BuildCaravanDetailText();
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = BuildStandardDetailText();
        }
        if (!string.IsNullOrWhiteSpace(detail)) parts.Add($"<br><size=80%>{detail}</size>");

        return string.Join("", parts);
    }

    private string BuildUnavailableHoverText()
    {
        string baseText = BuildHoverText();
        string missing = BuildMissingRequirementsText();
        if (string.IsNullOrWhiteSpace(missing)) return baseText;
        return $"{baseText}<br><size=80%><color=red>{missing}</color></size>";
    }

    private string BuildMissingRequirementsText()
    {
        if (character == null) return "No character selected.";

        List<string> parts = new();
        Leader owner = character.GetOwner();
        ResourceCostProfile costProfile = GetResourceCostProfile(owner);

        if (character.killed) parts.Add("Character is dead.");
        string defaultActionName = GetDefaultActionName();
        if (character.isPlayerControlled
            && !character.CanPlayerActNow()
            && (string.IsNullOrWhiteSpace(defaultActionName) || actionName != defaultActionName))
        {
            parts.Add(character.actionBlockedThisTurn ? "Cannot act this turn." : "No command points left this turn.");
        }

        if (owner != null)
        {
            if (isBuyCaravans || isSellCaravans)
            {
                ActionCostSnapshot snapshot = CalculateCostSnapshot(costProfile);
                if (!snapshot.hasRequiredStock)
                {
                    parts.Add("Market lacks required stock.");
                }

                if (snapshot.goldDelta < 0 && owner.goldAmount < -snapshot.goldDelta)
                {
                    parts.Add($"Need <sprite name=\"gold\">[{Mathf.Abs(snapshot.goldDelta)}] (have {owner.goldAmount})");
                }

                if (isSellCaravans)
                {
                    foreach (var cost in snapshot.resourceCosts)
                    {
                        int available = cost.produce switch
                        {
                            ProducesEnum.leather => owner.leatherAmount,
                            ProducesEnum.timber => owner.timberAmount,
                            ProducesEnum.mounts => owner.mountsAmount,
                            ProducesEnum.iron => owner.ironAmount,
                            ProducesEnum.steel => owner.steelAmount,
                            ProducesEnum.mithril => owner.mithrilAmount,
                            ProducesEnum.gold => owner.goldAmount,
                            _ => 0
                        };
                        if (available < cost.amount)
                        {
                            parts.Add($"Need <sprite name=\"{cost.produce}\">{cost.produce}[{cost.amount}] (have {available})");
                        }
                    }
                }
            }
            else
            {
                if (costProfile.leather > 0 && owner.leatherAmount < costProfile.leather)
                    parts.Add($"Need <sprite name=\"leather\">[{costProfile.leather}] (have {owner.leatherAmount})");
                if (costProfile.timber > 0 && owner.timberAmount < costProfile.timber)
                    parts.Add($"Need <sprite name=\"timber\">[{costProfile.timber}] (have {owner.timberAmount})");
                if (costProfile.mounts > 0 && owner.mountsAmount < costProfile.mounts)
                    parts.Add($"Need <sprite name=\"mounts\">[{costProfile.mounts}] (have {owner.mountsAmount})");
                if (costProfile.iron > 0 && owner.ironAmount < costProfile.iron)
                    parts.Add($"Need <sprite name=\"iron\">[{costProfile.iron}] (have {owner.ironAmount})");
                if (costProfile.steel > 0 && owner.steelAmount < costProfile.steel)
                    parts.Add($"Need <sprite name=\"steel\">[{costProfile.steel}] (have {owner.steelAmount})");
                if (costProfile.mithril > 0 && owner.mithrilAmount < costProfile.mithril)
                    parts.Add($"Need <sprite name=\"mithril\">[{costProfile.mithril}] (have {owner.mithrilAmount})");
                if (costProfile.gold > 0 && owner.goldAmount < costProfile.gold)
                    parts.Add($"Need <sprite name=\"gold\">[{costProfile.gold}] (have {owner.goldAmount})");
            }
        }

        if (parts.Count == 0 && !FulfillsConditions())
        {
            parts.Add("No valid targets or requirements not met.");
        }

        return string.Join("<br>", parts);
    }

    private string BuildCaravanDetailText()
    {
        Leader owner = character != null ? character.GetOwner() : null;
        ResourceCostProfile costProfile = GetResourceCostProfile(owner);
        ActionCostSnapshot snapshot = CalculateCostSnapshot(costProfile);
        if (!snapshot.isCaravan) return string.Empty;

        List<string> parts = new();
        if (isBuyCaravans)
        {
            if (snapshot.goldDelta < 0)
            {
                parts.Add($"<color=red>-<sprite name=\"gold\">[{Mathf.Abs(snapshot.goldDelta)}]</color>");
            }
        }
        else if (isSellCaravans)
        {
            foreach (var cost in snapshot.resourceCosts)
            {
                parts.Add($"<color=red>-<sprite name=\"{cost.produce}\">{cost.produce}[{cost.amount}]</color>");
            }

            if (snapshot.goldDelta > 0)
            {
                parts.Add($"<color=green>+<sprite name=\"gold\">[{snapshot.goldDelta}]</color>");
            }
            else if (snapshot.goldDelta < 0)
            {
                parts.Add($"<color=red>-<sprite name=\"gold\">[{Mathf.Abs(snapshot.goldDelta)}]</color>");
            }
        }

        return string.Join(" ", parts);
    }

    private string BuildStandardDetailText()
    {
        Leader owner = character != null ? character.GetOwner() : null;
        ResourceCostProfile costProfile = GetResourceCostProfile(owner);
        List<string> parts = new();

        foreach (var cost in GetResourceCosts(costProfile))
        {
            parts.Add($"<color=red>-<sprite name=\"{cost.sprite}\">{cost.sprite}[{cost.amount}]</color>");
        }

        if (costProfile.gold > 0)
        {
            parts.Add($"<color=red>-<sprite name=\"gold\">[{costProfile.gold}]</color>");
        }

        return string.Join(" ", parts);
    }

    private void ApplyCaravanPayments(ActionCostSnapshot snapshot)
    {
        if (!snapshot.isCaravan) return;
        if (character == null || character.GetOwner() == null) return;

        Leader owner = character.GetOwner();

        if (isSellCaravans)
        {
            foreach (var cost in snapshot.resourceCosts)
            {
                switch (cost.produce)
                {
                    case ProducesEnum.leather:
                        owner.RemoveLeather(cost.amount);
                        break;
                    case ProducesEnum.timber:
                        owner.RemoveTimber(cost.amount);
                        break;
                    case ProducesEnum.mounts:
                        owner.RemoveMounts(cost.amount);
                        break;
                    case ProducesEnum.iron:
                        owner.RemoveIron(cost.amount);
                        break;
                    case ProducesEnum.steel:
                        owner.RemoveSteel(cost.amount);
                        break;
                    case ProducesEnum.mithril:
                        owner.RemoveMithril(cost.amount);
                        break;
                    case ProducesEnum.gold:
                        owner.RemoveGold(cost.amount);
                        break;
                }
            }
        }

        if (snapshot.goldDelta < 0)
        {
            owner.RemoveGold(-snapshot.goldDelta);
        }
        else if (snapshot.goldDelta > 0)
        {
            owner.AddGold(snapshot.goldDelta);
        }
    }

    private string BuildCaravanTransactionMessage(ActionCostSnapshot snapshot)
    {
        if (!snapshot.isCaravan || snapshot.resourceCosts == null || snapshot.resourceCosts.Count == 0) return null;

        string resources = string.Join(", ", snapshot.resourceCosts.Select(cost =>
            $"{cost.amount} <sprite name=\"{cost.produce}\">{cost.produce}"));
        int gold = Mathf.Abs(snapshot.goldDelta);
        return isBuyCaravans
            ? $"bought {resources} for {gold} <sprite name=\"gold\">gold"
            : $"sold {resources} for {gold} <sprite name=\"gold\">gold";
    }

    private void ApplySpellFailurePenalty(bool isAI)
    {
        if (character == null || character.killed) return;
        int damage = UnityEngine.Random.Range(1, 6);
        character.health = Mathf.Max(0, character.health - damage);
        Sounds.Instance?.PlayVoicePain(character);
        if (!isAI)
        {
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"-{damage} <sprite name=\"health\">health", Color.red);
        }
        if (character.health < 1)
        {
            character.Killed(null);
        }
        else
        {
            character.RefreshSelectedCharacterIconIfSelected();
            CharacterIcons.RefreshForHumanPlayerCharacter(character);
        }
    }

    private StoresManager GetStoresManager()
    {
        if (cachedStoresManager != null) return cachedStoresManager;
        cachedStoresManager = FindAnyObjectByType<StoresManager>();
        return cachedStoresManager;
    }

    private bool PlayerCanSeeHex(Hex hex)
    {
        if (hex == null) return false;
        Game g = GetGame();
        if (g == null || g.player == null) return false;
        return g.player.visibleHexes.Contains(hex) && hex.IsHexSeen();
    }

    private ResourceCostProfile GetResourceCostProfile(Leader owner)
    {
        if (card != null)
        {
            return new ResourceCostProfile
            {
                leather = card.leatherRequired,
                timber = card.timberRequired,
                mounts = card.mountsRequired,
                iron = card.ironRequired,
                steel = card.steelRequired,
                mithril = card.mithrilRequired,
                gold = card.GetTotalGoldCost()
            };
        }

        return new ResourceCostProfile
        {
            leather = leatherCost,
            timber = timberCost,
            mounts = mountsCost,
            iron = ironCost,
            steel = steelCost,
            mithril = mithrilCost,
            gold = goldCost
        };
    }
}
