using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

public class Character : MonoBehaviour
{
    public static int MAX_RELEVANT_HEXES = Game.MAX_CHARACTERS + Game.MAX_OBJECTS + Game.MAX_PCS;
    public const int MAX_SKILL_LEVEL = 10;
    public const int MAX_OBJECTS = 10;

    [Header("Metadata")]
    public bool startingCharacter;

    [Header("Given name")]
    public string characterName;
    public string illustrationName;
    public string characterGroup;

    // Overridden by PlayableLeader: when characterName has been swapped to a leader variant's
    // name (e.g. "Strider" for "Aragorn"), this returns the original base leader name so sprite
    // lookups can fall back to it before falling back to race. Null for every other Character.
    public virtual string SpriteVariantBaseName => null;
    
    [Header("Allegiance")]
    public AlignmentEnum alignment;
    
    [Header("Owner")]
    public Leader owner;
    
    [Header("Current placement")]
    public Hex hex;

    // Which way this character was last facing (CharacterAnimationController). Kept on the
    // character rather than the controller because each Hex owns its own persistent
    // CharacterAnimationController instance (see Hex.GetCharacterAnimationController) — without
    // this, a character stepping onto a new hex mid-walk would pick up that hex's controller
    // fresh and always reset to Forward instead of keeping the direction it just turned to.
    public CharacterAnimationController.Orientation lastFacingOrientation = CharacterAnimationController.Orientation.Forward;

    [Header("Current character stats")]
    [SerializeField] int commander = 0;
    [SerializeField] int agent = 0;
    [SerializeField] int emmissary = 0;
    [SerializeField] int mage = 0;
    public int health = 100;
    public int moved = 0;
    public bool killed;

    [Header("Turn data")]
    [UnityEngine.Serialization.FormerlySerializedAs("hasActionedThisTurn")]
    [SerializeField] private bool actedThisTurn;
    // Status effects (Blocked, Kidnapped, Fear) that stop a character acting at all this turn.
    // Human actions are otherwise budgeted by the leader's command points, not per character.
    public bool actionBlockedThisTurn;

    /// <summary>
    /// "Has acted at least once this turn". For human characters, clearing it mid-turn is how
    /// cards and effects (Hope, rally events) refresh an action, so it hands back a command point.
    /// </summary>
    public bool hasActionedThisTurn
    {
        get => actedThisTurn;
        set
        {
            if (actedThisTurn && !value && isPlayerControlled && !actionBlockedThisTurn)
                (GetOwner() as PlayableLeader)?.GrantCommandPoint();
            actedThisTurn = value;
        }
    }

    /// <summary>Whether a human character may play another card now (command points left, not status-blocked).</summary>
    public bool CanPlayerActNow()
    {
        if (killed || actionBlockedThisTurn) return false;
        PlayableLeader leader = GetOwner() as PlayableLeader;
        return leader == null || leader.CommandPoints > 0;
    }
    public Hex previousHex;
    public bool isEmbarked;
    public List<Hex> reachableHexes = new();
    public List<Hex> relevantHexes = new();

    [Header("Spionage")]
    public List<Leader> doubledBy = new();
    private Dictionary<Leader, int> doubledByTurns = new();

    [System.Serializable]
    public class KidnappedCharacterRecord
    {
        public Character character;
        public Leader originalOwner;
    }

    [Header("Kidnapping")]
    public List<KidnappedCharacterRecord> kidnappedCharacters = new();
    public Character kidnappedBy;
    public Leader kidnappedOriginalOwner;

    [Header("Double Agent")]
    public Leader doubleAgentOriginalOwner;
    public int doubleAgentTurnsRemaining;

    [Header("Objects")]
    public List<CardData> objects = new();

    [Header("Army")]
    [SerializeField] private Army army = null;

    [Header("AI")]
    public bool isPlayerControlled = true;

    [Header("Army")]
    public RacesEnum race = RacesEnum.Common;
    public SexEnum sex = SexEnum.Male;

    [Header("Statuses")]
    public List<StatusEffectEnum> statusEffects = new();
    private Dictionary<StatusEffectEnum, int> statusEffectTurns = new();
    private bool burningForestTroopLossPending;
    private bool poisonedFearTriggered;
    private int statusMovementBonusThisTurn;
    private string temporaryActionDifficultyReductionClassName;
    private int temporaryActionDifficultyReductionValue;
    private int temporaryActionDifficultyReductionTurns;
    private Hex temporaryActionDifficultyReductionHex;
    public int guardLevel = 0;

    private BiomeConfig characterBiome;

    [Header("Animation")]
    public RuntimeAnimatorController animatorController;

    private Colors colors;
    private bool awaken = false;

    public struct StatusSnapshot
    {
        public Dictionary<StatusEffectEnum, int> statusEffectTurns;
        public int moved;
        public bool hasActionedThisTurn;
        public bool isEmbarked;
        public bool burningForestTroopLossPending;
        public bool poisonedFearTriggered;
        public int statusMovementBonusThisTurn;
        public string temporaryActionDifficultyReductionClassName;
        public int temporaryActionDifficultyReductionValue;
        public int temporaryActionDifficultyReductionTurns;
        public Hex temporaryActionDifficultyReductionHex;
    }

    void Awake()
    {
        army = null;
        doubledBy = new();
        doubledByTurns = new();
        kidnappedCharacters = new();
        kidnappedBy = null;
        kidnappedOriginalOwner = null;
        reachableHexes = new();
        statusEffects = new();
        InitializeStatusEffects();
        burningForestTroopLossPending = false;
        poisonedFearTriggered = false;
        statusMovementBonusThisTurn = 0;
        temporaryActionDifficultyReductionClassName = null;
        temporaryActionDifficultyReductionValue = 0;
        temporaryActionDifficultyReductionTurns = 0;
        temporaryActionDifficultyReductionHex = null;
        killed = false;
        awaken = true;
        colors = FindAnyObjectByType<Colors>();
    }

    private void InitializeStatusEffects()
    {
        statusEffectTurns = new Dictionary<StatusEffectEnum, int>();
        foreach (StatusEffectEnum effect in Enum.GetValues(typeof(StatusEffectEnum)))
        {
            statusEffectTurns[effect] = 0;
        }
    }
    // applyNoScenarioStart only matters for a LeaderBiomeConfig (plain BiomeConfig — e.g. a
    // nation's startingCharacters — never carries a noScenarioStart block and is unaffected):
    // a leader's noScenarioStart.startingArmySize/Card/Warships describe its PROCEDURAL default
    // army, which must never be created on top of an authored scenario's own ScenarioArmy for
    // the same leader (see NationSpawner.BuildScenarioArmy) — that's exactly what orphaned a
    // duplicate Army in a hex's army list previously (Character.CreateArmy overwrites `army`
    // without evicting the old one). Defaults false so every caller must opt in explicitly;
    // only the procedural (non-scenario) leader-placement path in NationSpawner passes true.
    public void InitializeFromBiome(Leader leader, Hex hex, BiomeConfig characterBiome, bool showSpawnMessage = true, bool applyNoScenarioStart = false)
    {
        if (!awaken) Awake();
        this.characterBiome = characterBiome;

        List<CardData> resolvedObjects = new();
        if (characterBiome.artifacts != null)
        {
            DeckManager deckManagerForObjects = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
            foreach (string objectName in characterBiome.artifacts)
            {
                CardData resolved = deckManagerForObjects?.FindObjectCardByName(objectName)?.Clone();
                if (resolved != null) resolvedObjects.Add(resolved);
            }
        }

        bool useCardArmy = !string.IsNullOrEmpty(characterBiome.startingArmyCard);
        Initialize(
            leader,
            characterBiome.alignment,
            hex,
            characterBiome.characterName,
            characterBiome.commander,
            characterBiome.agent,
            characterBiome.emmissary,
            characterBiome.mage,
            characterBiome.race,
            characterBiome.sex,
            resolvedObjects,
            useCardArmy ? 0 : characterBiome.startingArmySize,
            characterBiome.preferedTroopType,
            useCardArmy ? 0 : characterBiome.startingWarships,
            showSpawnMessage);

        if (useCardArmy && (characterBiome.startingArmySize > 0 || characterBiome.startingWarships > 0))
        {
            DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
            CardData card = deckManager?.FindArmyCardByName(characterBiome.startingArmyCard);
            if (card != null)
                CreateArmy(card.troopType, characterBiome.startingArmySize, startingCharacter, characterBiome.startingWarships, card.specialAbilities, showSpawnMessage: showSpawnMessage);
        }

        if (applyNoScenarioStart && characterBiome is LeaderBiomeConfig leaderBiome)
        {
            LeaderNoScenarioStart start = leaderBiome.noScenarioStart;
            if (start.startingArmySize > 0 || start.startingWarships > 0)
            {
                if (!string.IsNullOrEmpty(start.startingArmyCard))
                {
                    DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
                    CardData card = deckManager?.FindArmyCardByName(start.startingArmyCard);
                    if (card != null)
                        CreateArmy(card.troopType, start.startingArmySize, startingCharacter, start.startingWarships, card.specialAbilities, showSpawnMessage: showSpawnMessage);
                }
                else
                {
                    CreateArmy(characterBiome.preferedTroopType, start.startingArmySize, startingCharacter, start.startingWarships, showSpawnMessage: showSpawnMessage);
                }
            }
        }

        // Biome JSON only ever sets a handful of starting stats (and never race for non-playable
        // leaders — see NonPlayableLeaderBiomes.json, which has no "race" field on any of its 53
        // entries). The matching Character card is the authoritative source for race (and
        // commander/agent/emmissary/mage/alignment), the same card PlayableLeader.RefreshStatsFromCard
        // already pulls from for playable leaders — this applies it uniformly to every Character.
        ApplyStatsFromCard(GetCharacterCardData());
    }

    // The CardData (type "Character") that represents this character in the deck — used both to
    // seed starting skill levels and to render the character's card face in UI (e.g. level-up effects).
    public CardData GetCharacterCardData()
    {
        if (string.IsNullOrWhiteSpace(characterName)) return null;

        DeckManager dm = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        return dm?.cards?.Find(c =>
            string.Equals(c.name, characterName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.type, "Character", StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyStatsFromCard(CardData card)
    {
        if (card == null) return;
        alignment = (AlignmentEnum)card.alignment;
        commander = Mathf.Clamp(card.commander, 0, MAX_SKILL_LEVEL);
        agent = Mathf.Clamp(card.agent, 0, MAX_SKILL_LEVEL);
        emmissary = Mathf.Clamp(card.emmissary, 0, MAX_SKILL_LEVEL);
        mage = Mathf.Clamp(card.mage, 0, MAX_SKILL_LEVEL);
        race = card.race;
        if (!string.IsNullOrWhiteSpace(card.spriteName))
            illustrationName = card.spriteName;
        LoadAnimatorController();
    }

    public void LoadAnimatorController()
    {
        animatorController = CharacterAnimatorControllers.Resolve(characterBiome?.characterSprite, race);
    }

    public RuntimeAnimatorController GetAnimatorController()
    {
        // Controllers load asynchronously via Addressables; retry until the cache is ready.
        if (animatorController == null) LoadAnimatorController();
        return animatorController;
    }

    public void Initialize(
        Leader owner,
        AlignmentEnum alignment, 
        Hex hex, 
        string characterName,
        int commander,
        int agent,
        int emmissary,
        int mage,
        RacesEnum race,
        SexEnum sex,
        List<CardData> objects,
        int startingArmySize = 0,
        TroopsTypeEnum preferedTroopType = TroopsTypeEnum.ma,
        int startingWarships = 0,
        bool showSpawnMessage = true)
    {
        if (!awaken) Awake();

        if (showSpawnMessage)
        {
            string ownerName = "";
            if (owner != null && owner.characterName != null) ownerName = owner.characterName;
            if (ownerName.Trim() == "") ownerName = "themselves";
            MessageDisplayNoUI.ShowMessage(hex, this, $"Character {characterName} starts serving {ownerName}", Color.green);
        }

        this.characterName = characterName;
        this.commander = Mathf.Clamp(commander, 0, MAX_SKILL_LEVEL);
        this.agent = Mathf.Clamp(agent, 0, MAX_SKILL_LEVEL);
        this.emmissary = Mathf.Clamp(emmissary, 0, MAX_SKILL_LEVEL);
        this.mage = Mathf.Clamp(mage, 0, MAX_SKILL_LEVEL);
        this.alignment = alignment;
        this.race = race;
        this.sex = sex;
        this.startingCharacter = true;
        this.objects = objects;
        LoadAnimatorController();

        owner.GetOwner().controlledCharacters.Add(this);
        this.owner = owner.GetOwner();
        actedThisTurn = false; actionBlockedThisTurn = false;
        moved = 0;
        isEmbarked = false;
        army = null;
        this.hex = hex;
        hex.characters.Add(this);

        if (startingArmySize > 0 || startingWarships > 0) CreateArmy(preferedTroopType, startingArmySize, startingCharacter, startingWarships, showSpawnMessage: showSpawnMessage);
        RefreshArtifactPcVisibilityForHex(this.hex);
    }

    public AlignmentEnum GetAlignment()
    {
        if (owner != null) return owner.GetAlignment();
        // A leader has no owner above it; its alignment comes from its biome, not the
        // base 'alignment' field (which defaults to freePeople and is left unset on leaders).
        if (this is Leader self) return self.GetAlignment();
        return alignment;
    }

    public async Task Pass()
    {
        ActionsManager actionsManager = ActionsManager.Instance;
        CharacterAction action = actionsManager?.ResolveActionByRef(global::Pass.ActionRef);
        if (action == null) return;
        action.Initialize(this, condition: null, effect: null, asyncEffect: null);
        await action.Execute();
    }

    public void Halt(int turns = 1)
    {
        int clampedTurns = Mathf.Max(1, turns);
        ApplyStatusEffect(StatusEffectEnum.Halted, clampedTurns);
        if (clampedTurns == 1)
        {
            MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} halted (no movement) for next turn!", Color.red);
        }
        else
        {
            MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} halted (no movement) for {clampedTurns} turns!", Color.red);
        }
    }

    public void Encourage(int turns = 1)
    {
        ApplyStatusEffect(StatusEffectEnum.Encouraged, turns);
    }

    public bool IsEncouraged()
    {
        return HasStatusEffect(StatusEffectEnum.Encouraged);
    }

    public void ClearEncouraged()
    {
        ClearStatusEffect(StatusEffectEnum.Encouraged);
    }

    public bool IsRefusingDuels()
    {
        return HasStatusEffect(StatusEffectEnum.RefusingDuels);
    }

    public void RefuseDuels(int turns = 1)
    {
        ApplyStatusEffect(StatusEffectEnum.RefusingDuels, turns);
    }

    public bool HasDuelSupremacy()
    {
        return HasStatusEffect(StatusEffectEnum.DuelSupremacy);
    }

    public void GainDuelSupremacy(int turns = 1)
    {
        ApplyStatusEffect(StatusEffectEnum.DuelSupremacy, turns);
    }

    public bool IsHidden()
    {
        return HasStatusEffect(StatusEffectEnum.Hidden);
    }

    public void Hide(int turns = 1)
    {
        ApplyStatusEffect(StatusEffectEnum.Hidden, turns);
    }

    public bool HasStatusEffect(StatusEffectEnum effect)
    {
        return GetStatusEffectTurns(effect) > 0;
    }

    public int GetStatusEffectTurns(StatusEffectEnum effect)
    {
        if (statusEffectTurns == null || statusEffectTurns.Count == 0)
        {
            InitializeStatusEffects();
        }

        return statusEffectTurns.TryGetValue(effect, out int turns) ? Mathf.Max(0, turns) : 0;
    }

    public void ApplyStatusEffect(StatusEffectEnum effect, int turns = 1)
    {
        if (statusEffectTurns == null || statusEffectTurns.Count == 0)
        {
            InitializeStatusEffects();
        }

        // Artifact immunity check
        if (IsNegativeStatus(effect) && IsImmuneToStatusEffect(effect))
            return;

        turns = GetNormalizedStatusTurns(effect, turns);

        // Artifact duration modifiers
        if (IsNegativeStatus(effect))
        {
            int reduction = GetTotalNegativeStatusDurationReduction();
            turns = Mathf.Max(1, turns - reduction);
        }
        else if (IsPositiveStatus(effect))
        {
            int bonus = GetTotalPositiveStatusDurationBonus();
            turns += bonus;
        }

        if (IsEncouraged() && IsBlockedByEncouraged(effect)) return;
        if (effect == StatusEffectEnum.Haste && HasStatusEffect(StatusEffectEnum.Frozen)) return;

        int current = GetStatusEffectTurns(effect);
        int updatedTurns = Mathf.Max(current, turns);
        statusEffectTurns[effect] = updatedTurns;

        statusEffects ??= new List<StatusEffectEnum>();
        if (!statusEffects.Contains(effect)) statusEffects.Add(effect);

        if (effect == StatusEffectEnum.Burning)
        {
            ClearStatusEffect(StatusEffectEnum.Frozen);
            if (turns > current) burningForestTroopLossPending = true;
        }
        else if (effect == StatusEffectEnum.Frozen)
        {
            ClearStatusEffect(StatusEffectEnum.Haste);
            ClearStatusEffect(StatusEffectEnum.Burning);
        }
        else if (effect == StatusEffectEnum.Poisoned && turns > current)
        {
            poisonedFearTriggered = false;
        }
        else if (effect == StatusEffectEnum.Encouraged)
        {
            ClearSuppressedStatusesIfEncouraged();
        }

        if (updatedTurns > current && hex != null)
        {
            hex.PlayStatusEffectParticles(effect);
        }
    }

    public void ClearStatusEffect(StatusEffectEnum effect)
    {
        if (statusEffectTurns == null || statusEffectTurns.Count == 0)
        {
            InitializeStatusEffects();
        }

        // Detection evasion: resist attempts to clear Hidden status
        if (effect == StatusEffectEnum.Hidden && HasStatusEffect(StatusEffectEnum.Hidden))
        {
            int evasion = GetTotalDetectionEvasion();
            if (evasion > 0 && UnityEngine.Random.Range(0, 100) < evasion * 10)
            {
                return;
            }
        }

        statusEffectTurns[effect] = 0;
        statusEffects?.Remove(effect);
        ResetStatusSpecialState(effect);
    }

    public void NewTurn()
    {
        Game game = Game.Instance;
        Leader player = game != null ? game.player : null;

        ProcessKidnappedCharacters();
        ProcessDoubleAgent();
        if (killed) return;

        statusMovementBonusThisTurn = 0;

        if (health < 100)
        {
            health = Mathf.Min(100, health + 5);
        }
        if (HasStatusEffect(StatusEffectEnum.Hope) && health < 100)
        {
            int bonus = GetTotalPositiveStatusEffectBonus();
            health = Mathf.Min(100, health + 5 + bonus);
            MessageDisplayNoUI.ShowMessage(hex, this, $"Hope restores +{5 + bonus} health.", Color.green);
        }

        ApplyArtifactPassiveEffects();
        if (killed) return;

        ClearSuppressedStatusesIfEncouraged();

        int blockedTurns = GetStatusEffectTurns(StatusEffectEnum.Blocked);
        bool blocked = blockedTurns > 0;
        bool halted = HasStatusEffect(StatusEffectEnum.Halted);

        if (HasStatusEffect(StatusEffectEnum.Encouraged))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Encouraged steadies the heart.", Color.green);
            if (UnityEngine.Random.Range(0, 100) < 50)
            {
                statusMovementBonusThisTurn += 2;
                MessageDisplayNoUI.ShowMessage(hex, this, "Encouraged grants +2 movement.", Color.green);
            }
        }

        if (blocked)
        {
            moved = 0;
            actedThisTurn = true; actionBlockedThisTurn = true;
            MessageDisplayNoUI.ShowMessage(hex, this, "Blocked: action lost.", Color.red);
        }
        else if (halted)
        {
            moved = GetMaxMovement();
            actedThisTurn = false; actionBlockedThisTurn = false;
            MessageDisplayNoUI.ShowMessage(hex, this, "Halted: movement lost.", Color.yellow);
        }
        else
        {
            moved = 0;
            actedThisTurn = false; actionBlockedThisTurn = false;
        }

        if (IsKidnapped())
        {
            moved = GetMaxMovement();
            actedThisTurn = true; actionBlockedThisTurn = true;
        }

        if (HasStatusEffect(StatusEffectEnum.Frozen))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Frostbitten: -5 movement.", Color.cyan);
        }
        if (HasStatusEffect(StatusEffectEnum.Haste))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Haste grants +5 movement.", Color.green);
        }
        if (HasStatusEffect(StatusEffectEnum.Despair))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, HasStatusEffect(StatusEffectEnum.Hope) ? "Hope holds Despair at bay." : "Despair lowers skill.", Color.magenta);
        }
        if (HasStatusEffect(StatusEffectEnum.ArcaneInsight))
        {
            ProcessArcaneInsight();
        }
        if (HasStatusEffect(StatusEffectEnum.Hidden))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Hidden from enemy actions.", Color.green);
        }
        if (HasStatusEffect(StatusEffectEnum.Strengthened))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Strengthened: attack and duel power increased.", Color.red);
        }
        if (HasStatusEffect(StatusEffectEnum.Fortified))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Fortified: defense and duel power increased.", Color.cyan);
        }
        if (HasStatusEffect(StatusEffectEnum.Bleeding))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Bleeding worsens.", Color.red);
        }

        if (HasStatusEffect(StatusEffectEnum.Fear))
        {
            ProcessFear();
        }

        if (!killed)
        {
            ProcessMorgulTouch();
        }
        if (!killed)
        {
            ProcessBurning();
        }
        if (!killed)
        {
            ProcessPoisoned();
        }
        if (!killed)
        {
            ProcessSunburnt();
        }
        if (killed) return;

        if (blockedTurns > 0)
        {
            blockedTurns = Mathf.Max(0, blockedTurns - 1);
            statusEffectTurns[StatusEffectEnum.Blocked] = blockedTurns;
            if (blockedTurns == 0)
            {
                statusEffects?.Remove(StatusEffectEnum.Blocked);
                ResetStatusSpecialState(StatusEffectEnum.Blocked);
            }
        }

        if (HasStatusEffect(StatusEffectEnum.RefusingDuels))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Refusing duels.", Color.yellow);
        }
        if (HasStatusEffect(StatusEffectEnum.DuelSupremacy))
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Duel Supremacy is ready.", Color.cyan);
        }

        foreach (StatusEffectEnum effect in Enum.GetValues(typeof(StatusEffectEnum)))
        {
            if (effect == StatusEffectEnum.Blocked) continue;
            int turns = GetStatusEffectTurns(effect);
            if (turns <= 0) continue;

            turns = Mathf.Max(0, turns - 1);
            statusEffectTurns[effect] = turns;
            if (turns == 0)
            {
                statusEffects?.Remove(effect);
                ResetStatusSpecialState(effect);
                if (effect == StatusEffectEnum.Bleeding)
                {
                    ProcessBleeding();
                    if (killed) return;
                }
            }
        }
        TickDoubledByTurns();
        StoreReachableHexes();
        StoreRelevantHexes();
        RefreshSelectedCharacterIconIfSelected();
        RefreshActionsIfSelected();
        // Turn-start PC/region resource grants are handled once per leader, after the whole
        // controlledCharacters cascade, so duplicates across characters sharing a PC/region
        // can be deduped — see Leader.NewTurn -> RunTurnStartResourceGrants.
    }

    public StatusSnapshot CaptureStatusSnapshot()
    {
        if (statusEffectTurns == null || statusEffectTurns.Count == 0)
        {
            InitializeStatusEffects();
        }

        return new StatusSnapshot
        {
            statusEffectTurns = new Dictionary<StatusEffectEnum, int>(statusEffectTurns),
            moved = moved,
            hasActionedThisTurn = hasActionedThisTurn,
            isEmbarked = isEmbarked,
            burningForestTroopLossPending = burningForestTroopLossPending,
            poisonedFearTriggered = poisonedFearTriggered,
            statusMovementBonusThisTurn = statusMovementBonusThisTurn,
            temporaryActionDifficultyReductionClassName = temporaryActionDifficultyReductionClassName,
            temporaryActionDifficultyReductionValue = temporaryActionDifficultyReductionValue,
            temporaryActionDifficultyReductionTurns = temporaryActionDifficultyReductionTurns,
            temporaryActionDifficultyReductionHex = temporaryActionDifficultyReductionHex
        };
    }

    public void RestoreStatusSnapshot(StatusSnapshot snapshot)
    {
        InitializeStatusEffects();
        if (snapshot.statusEffectTurns != null)
        {
            foreach (var kv in snapshot.statusEffectTurns)
            {
                statusEffectTurns[kv.Key] = Mathf.Max(0, kv.Value);
            }
        }

        statusEffects = statusEffectTurns.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
        moved = snapshot.moved;
        actedThisTurn = snapshot.hasActionedThisTurn;
        isEmbarked = snapshot.isEmbarked;
        burningForestTroopLossPending = snapshot.burningForestTroopLossPending;
        poisonedFearTriggered = snapshot.poisonedFearTriggered;
        statusMovementBonusThisTurn = snapshot.statusMovementBonusThisTurn;
        temporaryActionDifficultyReductionClassName = snapshot.temporaryActionDifficultyReductionClassName;
        temporaryActionDifficultyReductionValue = snapshot.temporaryActionDifficultyReductionValue;
        temporaryActionDifficultyReductionTurns = snapshot.temporaryActionDifficultyReductionTurns;
        temporaryActionDifficultyReductionHex = snapshot.temporaryActionDifficultyReductionHex;
    }

    public void DisbandArmy(bool showMessage = true)
    {
        if (army == null || !IsArmyCommander()) return;

        if (hex != null)
        {
            if (hex.armies.Contains(army)) hex.armies.Remove(army);
            hex.RedrawCharacters();
            hex.RedrawArmies();
        }

        if (showMessage)
        {
            MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} disbanded their army", Color.yellow);
        }

        army = null;
        RefreshSelectedCharacterIconIfSelected();
    }

    public virtual Leader GetOwner()
    {
        if(killed) return null;
        if (!owner && this is Leader) return this as Leader;
        
        return owner;
    }

    public string GetHoverText(bool withAlignment, bool withCharInfo, bool withLevels, bool withArmy, bool withColor, bool withHealth = true, string namePrefix = null)
    {
        List<string> result = new() { };
        if (withColor) result.Add($"<color={colors.GetHexColorByName(alignment.ToString())}>");
        if (withHealth) result.Add(GetHealthHoverText());
        bool hasArmy = GetArmy() != null;
        string characterSprite = alignment.ToString() + (hasArmy? "" : "Character");
        if(withAlignment) result.Add($"<sprite name=\"{characterSprite}\">");
        if (!string.IsNullOrEmpty(namePrefix)) result.Add(namePrefix);
        result.Add($"{characterName}");
        if (withCharInfo)
        {
            if (commander > 0) result.Add($"<sprite name=\"commander\">{(withLevels ? GetCommander().ToString() : "")}");
            if (agent > 0) result.Add($"<sprite name=\"agent\">{(withLevels ? GetAgent().ToString() : "")}");
            if (emmissary > 0) result.Add($"<sprite name=\"emmissary\">{(withLevels ? GetEmmissary().ToString() : "")}");
            if (mage > 0) result.Add($"<sprite name=\"mage\">{(withLevels ? GetMage().ToString() : "")}");
        }

        if (withArmy && hasArmy) result.Add(GetArmy().GetHoverText());
        if (withColor) result.Add("</color>");
        return string.Join("", result);
    }

    private string GetHealthHoverText()
    {
        const int blocks = 4;
        int filledBlocks = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0, health) / 25f), 0, blocks);
        if (filledBlocks <= 0) return string.Empty;
        return "" + string.Concat(Enumerable.Repeat("<sprite name=\"health\">", filledBlocks));
    }

    public MovementType GetMovementType()
    {
        return army == null ? MovementType.Character : army.GetMovementType();
    }

    public int GetMaxMovement()
    {
        MovementType movementType = army == null ? MovementType.Character : army.GetMovementType();
        bool isInWater = hex != null && hex.IsWaterTerrain();
        int baseMovement = movementType switch
        {
            MovementType.ArmyCommander => AdjustMovementByRace(Game.Instance.armyMovement, isInWater),
            MovementType.ArmyCommanderCavalryOnly => AdjustMovementByRace(Game.Instance.cavalryMovement, isInWater),
            _ => AdjustMovementByRace(Game.Instance.characterMovement, isInWater)
        };

        // Object movement bonuses
        if (objects != null)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                CardData o = objects[i];
                if (o == null) continue;
                baseMovement += o.GetMovementBonus();
                if (isInWater && o.GrantsHasteAtSea())
                    baseMovement += 2;
            }
        }

        if (HasStatusEffect(StatusEffectEnum.Haste))
        {
            baseMovement += 5;
        }

        if (HasStatusEffect(StatusEffectEnum.Frozen))
        {
            int extraPenalty = EnvironmentalCardManager.Instance?.FrozenMovementExtraPenalty ?? 0;
            baseMovement -= 5 + extraPenalty;
        }

        if (HasStatusEffect(StatusEffectEnum.Sunburnt))
        {
            int extraPenalty = EnvironmentalCardManager.Instance?.SunburntMovementExtraPenalty ?? 0;
            baseMovement -= 2 + extraPenalty;
        }

        baseMovement += statusMovementBonusThisTurn;
        return Mathf.Max(0, baseMovement);
    }

    private int AdjustMovementByRace(int baseMovement, bool isInWater)
    {
        if (isInWater) return baseMovement;

        return race switch
        {
            RacesEnum.Hobbit => Mathf.Min(baseMovement, 3),
            RacesEnum.Dwarf => Mathf.Min(baseMovement, 4),
            _ => baseMovement
        };
    }

    public bool IsArmyCommander()
    {
        return army != null && army.commander == this && !killed && army.GetSize() > 0 && !army.killed;
    }

    public int GetMovementLeft()
    {
        return Mathf.Max(0, GetMaxMovement() - moved);
    }

    // showSpawnMessage mirrors Initialize's own flag of the same name: false during world
    // setup (scenario/procedural spawning), true for a live in-game recruit. It's not just
    // cosmetic — MessageDisplayNoUI.ShowMessage runs the "is this enemy spotted" reveal roll
    // for any non-owned character, and at spawn time player.visibleHexes hasn't been populated
    // yet (that only happens once Game.SelectFirstPlayerCharacter runs, after all spawning
    // completes), so a starting army always loses that roll — showing "unspotted enemy" for a
    // leader standing in what will immediately be a fully visible, already-known hex.
    public void CreateArmy(TroopsTypeEnum troopsType, int amount, bool startingArmy, int ws = 0, List<ArmySpecialAbilityEnum> specialAbilities = null, string troopName = null, int specialAbilityProcChance = 100, bool showSpawnMessage = true)
    {
        // A prior army for this character can still be sitting in a hex's army list (its own
        // hex, if this character moved since) — overwriting the `army` field below without
        // evicting it first leaves that old Army orphaned: still fought as a live defender by
        // Army.Attack(), but no longer reachable from character.GetArmy(), so it silently
        // vanishes from every UI that reads the character's current army.
        if (army != null && army.commander != null && army.commander.hex != null)
        {
            army.commander.hex.armies.Remove(army);
        }

        army = new Army(this, troopsType, amount, startingArmy, ws, 25, specialAbilities, troopName, specialAbilityProcChance);
        hex.armies.Add(army);

        if (showSpawnMessage)
            MessageDisplayNoUI.ShowMessage(hex, this,  $"{characterName} just hired an army of <sprite name=\"{troopsType.ToString().ToLower()}\">[{amount}]", Color.green);
        hex.RedrawCharacters();
        hex.RedrawArmies();
        RefreshSelectedCharacterIconIfSelected();
    }

    public Army GetArmy()
    {
        if (!IsArmyCommander()) return null;
        return army;
    }

    private void ProcessKidnappedCharacters()
    {
        if (kidnappedCharacters == null) kidnappedCharacters = new();

        for (int i = kidnappedCharacters.Count - 1; i >= 0; i--)
        {
            KidnappedCharacterRecord record = kidnappedCharacters[i];
            Character prisoner = record != null ? record.character : null;
            if (record == null || prisoner == null || prisoner.killed || prisoner.kidnappedBy != this)
            {
                kidnappedCharacters.RemoveAt(i);
                continue;
            }

            GetOwner()?.AddGold(1, GetOwner() == Game.Instance?.player);

            int kidnapperAgentLevel = Mathf.Max(0, GetAgent());
            int escapeRoll = UnityEngine.Random.Range(0, 10);
            if (escapeRoll >= kidnapperAgentLevel)
            {
                prisoner.ReleaseFromKidnap(true);
            }
        }
    }

    public bool IsKidnapped()
    {
        return kidnappedBy != null && !killed;
    }

    public List<Character> GetActiveCaptives()
    {
        if (kidnappedCharacters == null || kidnappedCharacters.Count < 1) return new List<Character>();

        return kidnappedCharacters
            .Where(x => x != null && x.character != null && !x.character.killed && x.character.kidnappedBy == this)
            .Select(x => x.character)
            .ToList();
    }

    public int GetTotalSkillLevel()
    {
        return Mathf.Max(0, GetCommander())
            + Mathf.Max(0, GetAgent())
            + Mathf.Max(0, GetEmmissary())
            + Mathf.Max(0, GetMage());
    }

    public int GetKidnapRansomValue()
    {
        return Mathf.Max(2, GetTotalSkillLevel());
    }

    public bool CanReleaseCaptive(Character target)
    {
        return target != null
            && !killed
            && !target.killed
            && target.kidnappedBy == this
            && target.hex == hex;
    }

    public bool ReleaseCaptive(Character target, bool escaped = false)
    {
        if (!CanReleaseCaptive(target)) return false;

        target.ReleaseFromKidnap(escaped);
        MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} released {target.characterName}.", Color.yellow);
        RefreshSelectedCharacterIconIfSelected();
        RefreshActionsIfSelected();
        target.RefreshSelectedCharacterIconIfSelected();
        target.RefreshActionsIfSelected();
        return true;
    }

    public bool CanDemandRansom(Character target)
    {
        if (!CanReleaseCaptive(target)) return false;
        Leader targetOwner = target.kidnappedOriginalOwner != null ? target.kidnappedOriginalOwner : target.GetOwner();
        return targetOwner != null && !targetOwner.killed;
    }

    public bool ShouldAcceptRansom(Character target, int ransomCost)
    {
        if (target == null) return false;

        Leader targetOwner = target.kidnappedOriginalOwner != null ? target.kidnappedOriginalOwner : target.GetOwner();
        if (targetOwner == null || targetOwner.killed) return false;
        if (targetOwner.goldAmount < ransomCost) return false;

        int reserveFloor = Mathf.Clamp(ransomCost / 2, 2, 6);
        return targetOwner.goldAmount - ransomCost >= reserveFloor;
    }

    public bool CanKidnap(Character target)
    {
        if (target == null || target == this || killed || target.killed) return false;
        if (GetAgent() < 4) return false;
        if (target.IsArmyCommander()) return false;
        if (target is Leader) return false;
        if (target.GetOwner() == GetOwner()) return false;
        if (target.IsKidnapped()) return false;
        return true;
    }

    public bool CanPossess(Character target)
    {
        if (target == null || target == this || killed || target.killed) return false;
        if (target.IsArmyCommander()) return false;
        if (target is Leader) return false;
        if (target.GetOwner() == GetOwner()) return false;
        if (target.IsKidnapped()) return false;
        return true;
    }

    public bool Kidnap(Character target)
    {
        if (!CanKidnap(target)) return false;
        return CaptureCharacter(target, $"{characterName} kidnapped {target.characterName}!");
    }

    public bool Possess(Character target)
    {
        if (!CanPossess(target)) return false;
        return CaptureCharacter(target, $"{characterName} possessed {target.characterName}!");
    }

    private bool CaptureCharacter(Character target, string message)
    {
        if (kidnappedCharacters == null) kidnappedCharacters = new();

        Leader originalOwner = target.GetOwner();
        if (originalOwner == null) return false;

        if (originalOwner.controlledCharacters.Contains(target)) originalOwner.controlledCharacters.Remove(target);
        Hex previousHex = target.hex;
        if (previousHex != null && previousHex.characters.Contains(target)) previousHex.characters.Remove(target);

        target.kidnappedBy = this;
        target.kidnappedOriginalOwner = originalOwner;
        target.hex = this.hex;
        target.hasActionedThisTurn = true; target.actionBlockedThisTurn = true;
        target.moved = target.GetMaxMovement();
        if (target.hex != null && !target.hex.characters.Contains(target)) target.hex.characters.Add(target);

        kidnappedCharacters.Add(new KidnappedCharacterRecord
        {
            character = target,
            originalOwner = originalOwner
        });

        previousHex?.RedrawCharacters();
        if (target.hex != null && target.hex != previousHex) target.hex.RedrawCharacters();
        RefreshKidnappedCharactersPosition();
        MessageDisplayNoUI.ShowMessage(hex, this, message, Color.green);
        RefreshSelectedCharacterIconIfSelected();
        RefreshActionsIfSelected();
        target.RefreshSelectedCharacterIconIfSelected();
        target.RefreshActionsIfSelected();
        return true;
    }

    public void ReleaseFromKidnap(bool escaped)
    {
        Character kidnapper = kidnappedBy;
        if (kidnapper != null && kidnapper.kidnappedCharacters != null)
        {
            kidnapper.kidnappedCharacters.RemoveAll(x => x == null || x.character == null || x.character == this);
        }

        kidnappedBy = null;
        Leader originalOwner = kidnappedOriginalOwner;
        kidnappedOriginalOwner = null;

        if (originalOwner != null && !originalOwner.controlledCharacters.Contains(this))
        {
            originalOwner.controlledCharacters.Add(this);
        }

        if (escaped && originalOwner != null)
        {
            Board board = Board.Instance;
            Hex capitalHex = board?.GetHexes().Find(x => x.GetPC() != null && x.GetPC().owner == originalOwner && x.GetPC().isCapital);
            if (capitalHex != null && capitalHex != hex)
            {
                if (hex != null) hex.characters.Remove(this);
                hex = capitalHex;
                if (!capitalHex.characters.Contains(this)) capitalHex.characters.Add(this);
                capitalHex.RedrawCharacters();
            }
            moved = 0;
            actedThisTurn = false; actionBlockedThisTurn = false;
        }

        Hex currentHex = hex;
        if (hex != null && !hex.characters.Contains(this))
        {
            hex.characters.Add(this);
        }
        currentHex?.RedrawCharacters();

        if (escaped)
        {
            MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} escaped captivity!", Color.yellow);
        }

        RefreshSelectedCharacterIconIfSelected();
        RefreshActionsIfSelected();
    }

    public void BecomeDoubleAgent(Leader newOwner, int turns)
    {
        if (newOwner == null || turns <= 0 || killed) return;
        Leader currentOwner = GetOwner();
        if (currentOwner == null || currentOwner == newOwner) return;

        if (doubleAgentOriginalOwner == null)
        {
            doubleAgentOriginalOwner = currentOwner;
        }

        currentOwner.controlledCharacters.Remove(this);
        owner = newOwner;
        if (!newOwner.controlledCharacters.Contains(this))
        {
            newOwner.controlledCharacters.Add(this);
        }

        doubleAgentTurnsRemaining = Mathf.Max(doubleAgentTurnsRemaining, turns);

        hex?.RedrawCharacters();
        RefreshSelectedCharacterIconIfSelected();
        RefreshActionsIfSelected();
    }

    private void ProcessDoubleAgent()
    {
        if (doubleAgentTurnsRemaining <= 0) return;

        doubleAgentTurnsRemaining--;
        if (doubleAgentTurnsRemaining > 0) return;

        Leader originalOwner = doubleAgentOriginalOwner;
        doubleAgentOriginalOwner = null;
        if (originalOwner == null || originalOwner.killed) return;

        owner?.controlledCharacters.Remove(this);
        owner = originalOwner;
        if (!originalOwner.controlledCharacters.Contains(this))
        {
            originalOwner.controlledCharacters.Add(this);
        }

        MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} returns to serving {originalOwner.characterName}.", Color.yellow);
        hex?.RedrawCharacters();
        RefreshSelectedCharacterIconIfSelected();
        RefreshActionsIfSelected();
    }

    public void RefreshKidnappedCharactersPosition()
    {
        if (kidnappedCharacters == null || kidnappedCharacters.Count < 1) return;

        HashSet<Hex> redrawHexes = new();
        for (int i = kidnappedCharacters.Count - 1; i >= 0; i--)
        {
            KidnappedCharacterRecord record = kidnappedCharacters[i];
            Character prisoner = record != null ? record.character : null;
            if (record == null || prisoner == null || prisoner.killed || prisoner.kidnappedBy != this)
            {
                kidnappedCharacters.RemoveAt(i);
                continue;
            }

            Hex previousHex = prisoner.hex;
            if (previousHex != null && previousHex != hex && previousHex.characters.Contains(prisoner))
            {
                previousHex.characters.Remove(prisoner);
                redrawHexes.Add(previousHex);
            }

            prisoner.hex = hex;
            if (hex != null && !hex.characters.Contains(prisoner))
            {
                hex.characters.Add(prisoner);
            }

            prisoner.RefreshSelectedCharacterIconIfSelected();
            prisoner.RefreshActionsIfSelected();
        }

        if (hex != null) redrawHexes.Add(hex);
        foreach (Hex redrawHex in redrawHexes)
        {
            redrawHex?.RedrawCharacters();
        }
    }

    virtual public void Killed(Leader killedBy, bool onlyMark=false)
    {
        // First, while the unit still stands on its hex: its 3D model falls where it stood.
        UnitAnimations.Killed(this);
        bool redrawArmies = false;
        if (IsArmyCommander() && !army.killed) {
            army.Killed(killedBy, onlyMark);
            redrawArmies = true;
        }
        if (kidnappedCharacters != null && kidnappedCharacters.Count > 0)
        {
            List<Character> prisoners = kidnappedCharacters
                .Where(x => x != null && x.character != null)
                .Select(x => x.character)
                .ToList();
            foreach (Character prisoner in prisoners)
            {
                prisoner.ReleaseFromKidnap(false);
            }
            kidnappedCharacters.Clear();
        }

        if (IsKidnapped())
        {
            ReleaseFromKidnap(false);
        }

        if(!onlyMark)
        {
            if(GetOwner().controlledCharacters.Contains(this)) GetOwner().controlledCharacters.Remove(this);
            if(hex.characters.Contains(this)) hex.characters.Remove(this);
            DropObjectsToHex();
            RefreshArtifactPcVisibilityForHex(hex);
        }
        health = 0;
        killed = true;
        MessageDisplayNoUI.ShowMessage(hex, this,  $"{characterName} eliminated", Color.red);        
        hex.RedrawCharacters();
        RefreshSelectedCharacterIconIfSelected();
        if(redrawArmies) hex.RedrawArmies();
        Leader owner = GetOwner();
        if (owner != null) CharacterIcons.RefreshForHumanPlayerOf(owner);
    }

    private void DropObjectsToHex()
    {
        if (objects == null || objects.Count == 0) return;
        if (hex == null) return;
        hex.hiddenObjects ??= new System.Collections.Generic.List<CardData>();
        foreach (CardData o in objects)
        {
            if (o != null) hex.hiddenObjects.Add(o);
        }
        objects.Clear();
    }

    public void RefreshSelectedCharacterIconIfSelected()
    {        
        Game game = FindAnyObjectByType<Game>();
        Board board = FindAnyObjectByType<Board>();
        if(game.IsPlayerCurrentlyPlaying() && board.selectedCharacter == this) FindAnyObjectByType<SelectedCharacterIcon>().Refresh(this);
    }
    public void RefreshActionsIfSelected()
    {        
    }

    public void Wounded(Leader woundedBy, int damage)
    {
        UnitAnimations.Wounded(this, damage);
        health -= damage;
        MessageDisplayNoUI.ShowMessage(hex, this,  $"{characterName} wounded by {damage}", Color.red);
        Sounds.Instance?.PlayVoicePain(this);
        RefreshSelectedCharacterIconIfSelected();
        CharacterIcons.RefreshForHumanPlayerCharacter(this);
        if (health < 1) Killed(woundedBy);
    }

    public void Revive(Leader revivedOwner, Hex destinationHex, int revivedHealth = 25)
    {
        Leader previousOwner = owner;
        Hex previousHex = hex;

        if (previousOwner != null && previousOwner.controlledCharacters.Contains(this))
        {
            previousOwner.controlledCharacters.Remove(this);
        }

        if (previousHex != null && previousHex.characters.Contains(this))
        {
            previousHex.characters.Remove(this);
        }

        if (army != null)
        {
            if (previousHex != null && previousHex.armies.Contains(army))
            {
                previousHex.armies.Remove(army);
            }
            army = null;
        }

        owner = revivedOwner;
        killed = false;
        health = Mathf.Clamp(revivedHealth, 1, 100);
        moved = 0;
        actedThisTurn = false; actionBlockedThisTurn = false;
        isEmbarked = false;
        kidnappedBy = null;
        kidnappedOriginalOwner = null;
        kidnappedCharacters ??= new();
        kidnappedCharacters.Clear();

        hex = destinationHex;

        if (owner != null && !owner.controlledCharacters.Contains(this))
        {
            owner.controlledCharacters.Add(this);
        }

        if (destinationHex != null && !destinationHex.characters.Contains(this))
        {
            destinationHex.characters.Add(this);
        }

        RefreshArtifactPcVisibilityForHex(previousHex);
        RefreshArtifactPcVisibilityForHex(destinationHex);
        previousHex?.RedrawCharacters();
        previousHex?.RedrawArmies();
        destinationHex?.RedrawCharacters();
        destinationHex?.RedrawArmies();
        RefreshSelectedCharacterIconIfSelected();
        RefreshActionsIfSelected();
        CharacterIcons.RefreshForHumanPlayerOf(previousOwner);
        CharacterIcons.RefreshForHumanPlayerOf(owner);
    }

    public int GetArtifactActionDifficultyReduction(string actionClassName)
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = objects.Sum(o => o != null ? o.GetActionDifficultyReduction(actionClassName) : 0);
        return Mathf.Min(total, MaxTotalScryObjectBonus);
    }

    public int GetTemporaryActionDifficultyReduction(string actionClassName, Hex currentHex)
    {
        if (temporaryActionDifficultyReductionTurns <= 0) return 0;
        if (string.IsNullOrWhiteSpace(temporaryActionDifficultyReductionClassName)) return 0;
        if (!string.Equals(temporaryActionDifficultyReductionClassName, actionClassName, StringComparison.OrdinalIgnoreCase)) return 0;
        if (temporaryActionDifficultyReductionHex != null && temporaryActionDifficultyReductionHex != currentHex) return 0;
        return Math.Max(0, temporaryActionDifficultyReductionValue);
    }

    public void GrantTemporaryActionDifficultyReduction(string actionClassName, int value, int turns, Hex currentHex)
    {
        temporaryActionDifficultyReductionClassName = actionClassName;
        temporaryActionDifficultyReductionValue = Math.Max(0, value);
        temporaryActionDifficultyReductionTurns = Math.Max(1, turns);
        temporaryActionDifficultyReductionHex = currentHex;
    }

    public void ConsumeTemporaryActionDifficultyReduction(string actionClassName, Hex currentHex)
    {
        if (GetTemporaryActionDifficultyReduction(actionClassName, currentHex) <= 0) return;
        temporaryActionDifficultyReductionClassName = null;
        temporaryActionDifficultyReductionValue = 0;
        temporaryActionDifficultyReductionTurns = 0;
        temporaryActionDifficultyReductionHex = null;
    }

    public bool IsImmuneToNegativeEnvironmentalCards()
    {
        if (objects == null || objects.Count == 0) return false;
        return objects.Any(o => o != null && o.GrantsEnvironmentalImmunity());
    }

    public bool HidesOccupiedPcWithArtifact()
    {
        return false;
    }

    public static void RefreshArtifactPcVisibilityForHex(Hex hex)
    {
        if (hex == null) return;

        PC pc = hex.GetPCData();
        if (pc == null) return;

        bool shouldHide = false;
        if (hex.characters != null)
        {
            for (int i = 0; i < hex.characters.Count; i++)
            {
                Character occupant = hex.characters[i];
                if (occupant != null && !occupant.killed && occupant.HidesOccupiedPcWithArtifact())
                {
                    shouldHide = true;
                    break;
                }
            }
        }

        if (pc.artifactOccupancyHidden == shouldHide) return;

        pc.SetArtifactOccupancyHidden(shouldHide);
        hex.RefreshVisibilityRendering();
    }

    // ---- Object stat helpers ----

    public int GetTotalDetectionEvasion()
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null) total += objects[i].GetDetectionEvasion();
        return total;
    }

    public bool GetIgnoreTerrainMovementPenalty()
    {
        if (objects == null || objects.Count == 0) return false;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null && objects[i].GetIgnoreTerrainMovementPenalty())
                return true;
        return false;
    }

    public int GetTotalNegativeStatusDurationReduction()
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null) total += objects[i].GetNegativeStatusDurationReduction();
        return total;
    }

    public int GetTotalPositiveStatusDurationBonus()
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null) total += objects[i].GetPositiveStatusDurationBonus();
        return total;
    }

    public int GetTotalNegativeStatusDamageReduction()
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null) total += objects[i].GetNegativeStatusDamageReduction();
        return total;
    }

    public int GetTotalPositiveStatusEffectBonus()
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null) total += objects[i].GetPositiveStatusEffectBonus();
        return total;
    }

    public bool IsImmuneToStatusEffect(StatusEffectEnum effect)
    {
        if (objects == null || objects.Count == 0) return false;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null && objects[i].GetNegativeStatusImmunity(effect))
                return true;
        return false;
    }

    public int GetTotalRecruitBonusMenAtArms()
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null) total += objects[i].GetRecruitBonusMenAtArms();
        return total;
    }

    // Hard caps on how much carried objects can stack, independent of how many a character
    // holds (MAX_OBJECTS = 10). Same rationale as Duel.cs's MaxArtifactDuelScore: without a
    // cap, hoarding every scry-granting object (5 cards at scryAreaBonus 2, or 6 cards
    // totaling 80 scryObjectBonus once the Find Artifact difficulty-order bug is fixed) would
    // make the ability trivial rather than a meaningful bonus. Values are roughly "best single
    // item plus one backup" (Vilya's scryObjectBonus of 25 plus a Minor item).
    private const int MaxTotalScryAreaBonus = 6;
    private const int MaxTotalScryObjectBonus = 30;

    public int GetTotalScryAreaBonus()
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null) total += objects[i].GetScryAreaBonus();
        return Mathf.Min(total, MaxTotalScryAreaBonus);
    }

    public int GetTotalScryObjectBonus()
    {
        if (objects == null || objects.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i] != null) total += objects[i].GetScryObjectBonus();
        return Mathf.Min(total, MaxTotalScryObjectBonus);
    }

    private static bool IsNegativeStatus(StatusEffectEnum effect)
    {
        return effect == StatusEffectEnum.Halted
            || effect == StatusEffectEnum.Poisoned
            || effect == StatusEffectEnum.Burning
            || effect == StatusEffectEnum.Frozen
            || effect == StatusEffectEnum.Blocked
            || effect == StatusEffectEnum.Despair
            || effect == StatusEffectEnum.Fear
            || effect == StatusEffectEnum.Bleeding
            || effect == StatusEffectEnum.MorgulTouch
            || effect == StatusEffectEnum.Sunburnt;
    }

    private static bool IsPositiveStatus(StatusEffectEnum effect)
    {
        return effect == StatusEffectEnum.Encouraged
            || effect == StatusEffectEnum.Hope
            || effect == StatusEffectEnum.Haste
            || effect == StatusEffectEnum.Hidden
            || effect == StatusEffectEnum.ArcaneInsight
            || effect == StatusEffectEnum.Strengthened
            || effect == StatusEffectEnum.Fortified
            || effect == StatusEffectEnum.DuelSupremacy
            || effect == StatusEffectEnum.Guarded;
    }
    // -----------------------------------

    private void TickDoubledByTurns()
    {
        if (doubledByTurns == null || doubledByTurns.Count == 0) return;

        List<Leader> keys = doubledByTurns.Keys.ToList();
        for (int i = 0; i < keys.Count; i++)
        {
            Leader spyLeader = keys[i];
            int turns = Mathf.Max(0, doubledByTurns[spyLeader] - 1);
            if (turns <= 0)
            {
                doubledByTurns.Remove(spyLeader);
                doubledBy.Remove(spyLeader);
            }
            else
            {
                doubledByTurns[spyLeader] = turns;
            }
        }
    }

    public void Doubled(Leader doubledBy, int turns = -1)
    {
        if (doubledBy == null) return;
        if (!this.doubledBy.Contains(doubledBy)) this.doubledBy.Add(doubledBy);

        if (turns > 0)
        {
            if (doubledByTurns.TryGetValue(doubledBy, out int existing))
            {
                doubledByTurns[doubledBy] = Mathf.Max(existing, turns);
            }
            else
            {
                doubledByTurns[doubledBy] = turns;
            }
        }
        else
        {
            doubledByTurns.Remove(doubledBy);
        }

        MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} doubled by {doubledBy.characterName}", Color.green);
    }
    public void Undouble(Leader doubledBy)
    {
        this.doubledBy.Remove(doubledBy);
        doubledByTurns.Remove(doubledBy);
        MessageDisplayNoUI.ShowMessage(hex, this,  $"{characterName} undoubled by {doubledBy.characterName}", Color.green);
    }

    public int GetBaseCommander()
    {
        return commander;
    }

    public int GetBaseAgent()
    {
        return agent;
    }

    public int GetBaseEmmissary()
    {
        return emmissary;
    }

    public int GetBaseMage()
    {
        return mage;
    }

    public int GetCommander()
    {
        int total = commander + objects.FindAll(x => x.commanderBonus > 0).Sum(x => x.commanderBonus);
        total = ApplyDespairPenalty(total);
        return Mathf.Min(MAX_SKILL_LEVEL, total);
    }

    public int GetAgent()
    {
        int total = agent + objects.FindAll(x => x.agentBonus > 0).Sum(x => x.agentBonus);
        total = ApplyDespairPenalty(total);
        return Mathf.Min(MAX_SKILL_LEVEL, total);
    }

    public int GetEmmissary()
    {
        int total = emmissary + objects.FindAll(x => x.emmissaryBonus > 0).Sum(x => x.emmissaryBonus);
        total = ApplyDespairPenalty(total);
        return Mathf.Min(MAX_SKILL_LEVEL, total);
    }

    public int GetMage()
    {
        int total = mage + objects.FindAll(x => x.mageBonus > 0).Sum(x => x.mageBonus);
        if (HasStatusEffect(StatusEffectEnum.ArcaneInsight)) total += 1;
        total = ApplyDespairPenalty(total);
        return Mathf.Min(MAX_SKILL_LEVEL, total);
    }

    public void SetCommander(int level)
    {
        commander = Mathf.Clamp(level, 0, MAX_SKILL_LEVEL);
    }

    public void SetAgent(int level)
    {
        agent = Mathf.Clamp(level, 0, MAX_SKILL_LEVEL);
    }

    public void SetEmmissary(int level)
    {
        emmissary = Mathf.Clamp(level, 0, MAX_SKILL_LEVEL);
    }

    public void SetMage(int level)
    {
        mage = Mathf.Clamp(level, 0, MAX_SKILL_LEVEL);
    }


    public void AddCommander(int level)
    {
        int before = commander;
        commander = Mathf.Clamp(commander + level, 0, MAX_SKILL_LEVEL);
        NotifySkillLevelChanged(CharacterSkillEnum.Commander, before, commander);
    }

    public void AddAgent(int level)
    {
        int before = agent;
        agent = Mathf.Clamp(agent + level, 0, MAX_SKILL_LEVEL);
        NotifySkillLevelChanged(CharacterSkillEnum.Agent, before, agent);
    }

    public void AddEmmissary(int level)
    {
        int before = emmissary;
        emmissary = Mathf.Clamp(emmissary + level, 0, MAX_SKILL_LEVEL);
        NotifySkillLevelChanged(CharacterSkillEnum.Emmissary, before, emmissary);
    }

    public void AddMage(int level)
    {
        int before = mage;
        mage = Mathf.Clamp(mage + level, 0, MAX_SKILL_LEVEL);
        NotifySkillLevelChanged(CharacterSkillEnum.Mage, before, mage);
    }

    // Fires the full-screen level-up/level-down presentation. Only shown for the human
    // player's own characters — AI skill changes happen too often to interrupt with an overlay.
    private void NotifySkillLevelChanged(CharacterSkillEnum skill, int previousLevel, int newLevel)
    {
        if (newLevel > previousLevel && !killed) UnitAnimations.LeveledUp(this);
        if (previousLevel == newLevel || killed || !isPlayerControlled) return;
        LevelChangeEffectUI.Show(this, skill, previousLevel, newLevel);
    }

    public void Heal(int health)
    {
        int previousHealth = this.health;
        this.health = Mathf.Min(100, this.health + Mathf.Max(0, health));
        int healedAmount = Mathf.Max(0, this.health - previousHealth);
        bool curedPoison = HasStatusEffect(StatusEffectEnum.Poisoned);
        if (curedPoison)
        {
            ClearStatusEffect(StatusEffectEnum.Poisoned);
        }

        if (healedAmount > 0)
        {
            MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} heals by {healedAmount}", Color.green);
        }
        if (curedPoison)
        {
            MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} is cured of Poison.", Color.green);
        }

        if (healedAmount > 0 || curedPoison)
        {
            RefreshSelectedCharacterIconIfSelected();
            CharacterIcons.RefreshForHumanPlayerCharacter(this);
        }
    }

    private void ApplyArtifactPassiveEffects()
    {
        if (objects == null || objects.Count == 0) return;

        for (int i = 0; i < objects.Count; i++)
        {
            CardData obj = objects[i];
            if (obj == null) continue;

            int heal = obj.GetHealPerTurn();
            if (heal > 0 && this.health < 100)
            {
                int previousHealth = this.health;
                this.health = Mathf.Min(100, this.health + Mathf.Max(0, heal));
                int healedAmount = Mathf.Max(0, this.health - previousHealth);
                if (healedAmount > 0)
                {
                    RefreshSelectedCharacterIconIfSelected();
                    CharacterIcons.RefreshForHumanPlayerCharacter(this);
                }
            }

            int scoutRadius = obj.GetAutoScoutRadius();
            if (scoutRadius > 0 && GetOwner() is PlayableLeader pl && hex != null)
            {
                hex.RevealArea(scoutRadius, false, pl);
            }
        }
    }

    private static int GetNormalizedStatusTurns(StatusEffectEnum effect, int turns)
    {
        turns = Mathf.Max(1, turns);
        return effect switch
        {
            StatusEffectEnum.Burning => Mathf.Max(3, turns),
            StatusEffectEnum.Poisoned => Mathf.Max(5, turns),
            StatusEffectEnum.MorgulTouch => Mathf.Max(5, turns),
            StatusEffectEnum.Bleeding => 1,
            _ => turns
        };
    }

    private static bool IsManRace(RacesEnum race)
    {
        return race == RacesEnum.Common
            || race == RacesEnum.Dunedain
            || race == RacesEnum.Southron
            || race == RacesEnum.Easterling;
    }

    private static bool IsBlockedByEncouraged(StatusEffectEnum effect)
    {
        return effect == StatusEffectEnum.Fear
            || effect == StatusEffectEnum.Despair
            || effect == StatusEffectEnum.Halted;
    }

    private void ClearSuppressedStatusesIfEncouraged()
    {
        if (!HasStatusEffect(StatusEffectEnum.Encouraged)) return;
        ClearStatusEffect(StatusEffectEnum.Fear);
        ClearStatusEffect(StatusEffectEnum.Despair);
        ClearStatusEffect(StatusEffectEnum.Halted);
    }

    private void ApplyStatusMovementPenalty(int amount, string sourceName)
    {
        int penalty = Mathf.Max(0, amount);
        if (penalty <= 0) return;
        moved = Mathf.Min(GetMaxMovement(), moved + penalty);
        MessageDisplayNoUI.ShowMessage(hex, this, $"{sourceName}: -{penalty} movement.", Color.yellow);
    }

    private void ProcessFear()
    {
        bool retreated = false;
        if (previousHex != null && previousHex != hex && UnityEngine.Random.Range(0, 100) < 50)
        {
            Board board = Board.Instance;
            if (board != null)
            {
                Hex currentHex = hex;
                int movedBefore = moved;
                board.MoveCharacterOneHex(this, currentHex, previousHex, true, false, false);
                previousHex = currentHex;
                moved = Mathf.Min(GetMaxMovement(), movedBefore);
                retreated = true;
                MessageDisplayNoUI.ShowMessage(hex, this, "Fear drives a retreat.", Color.magenta);
            }
        }

        if (UnityEngine.Random.Range(0, 100) < 25)
        {
            hasActionedThisTurn = true; actionBlockedThisTurn = true;
            MessageDisplayNoUI.ShowMessage(hex, this, retreated ? "Fear also steals the action." : "Fear steals the action.", Color.red);
        }
        else if (!retreated)
        {
            MessageDisplayNoUI.ShowMessage(hex, this, "Fear stirs, but is resisted.", Color.magenta);
        }
    }

    private void ProcessArcaneInsight()
    {
        MessageDisplayNoUI.ShowMessage(hex, this, "Arcane Insight: can cast Spell cards of any level.", Color.cyan);
    }

    private void ResetStatusSpecialState(StatusEffectEnum effect)
    {
        if (effect == StatusEffectEnum.Burning)
        {
            burningForestTroopLossPending = false;
        }
        else if (effect == StatusEffectEnum.Poisoned)
        {
            poisonedFearTriggered = false;
        }
        else if (effect == StatusEffectEnum.Guarded)
        {
            guardLevel = 0;
        }
    }

    private int ApplyDespairPenalty(int total)
    {
        if (!HasStatusEffect(StatusEffectEnum.Despair) || HasStatusEffect(StatusEffectEnum.Hope)) return total;
        if (total <= 0) return 0;
        if (total > 1) total -= 1;
        return Mathf.Clamp(total, 1, MAX_SKILL_LEVEL);
    }

    private void ProcessBurning()
    {
        if (!HasStatusEffect(StatusEffectEnum.Burning)) return;

        ApplyStatusDamage(5, "Burning");
        if (killed) return;
        ApplyStatusMovementPenalty(2, "Burning");

        if (!IsArmyCommander()) return;
        Army commandedArmy = GetArmy();
        if (commandedArmy == null || commandedArmy.killed || commandedArmy.GetSize(true) < 1) return;

        if (UnityEngine.Random.Range(0, 100) < 25)
        {
            TroopsTypeEnum? lostTroop = commandedArmy.RemoveRandomTroopOfTypes(TroopsTypeEnum.ca, TroopsTypeEnum.ar, TroopsTypeEnum.ws);
            if (lostTroop.HasValue)
            {
                MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName}'s burning army loses 1 <sprite name=\"{lostTroop.Value.ToString().ToLower()}\">.", Color.red);
            }
        }
    }

    private void ProcessPoisoned()
    {
        if (!HasStatusEffect(StatusEffectEnum.Poisoned)) return;

        ApplyStatusDamage(5, "Poisoned");
        if (killed) return;
        ApplyStatusMovementPenalty(1, "Poisoned");

        if (poisonedFearTriggered || GetStatusEffectTurns(StatusEffectEnum.Poisoned) > 3) return;
        ApplyStatusEffect(StatusEffectEnum.Fear, 1);
        poisonedFearTriggered = true;
        MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} succumbs to Poison and gains Fear.", Color.magenta);
    }

    private void ProcessBleeding()
    {
        ApplyStatusDamage(15, "Bleeding");
    }

    private void ProcessSunburnt()
    {
        // The -2 movement is applied passively in GetMaxMovement (mirroring Frozen); here we only
        // take the health toll so it isn't double-counted.
        if (!HasStatusEffect(StatusEffectEnum.Sunburnt)) return;
        int extraDamage = EnvironmentalCardManager.Instance?.SunburntDamageExtraPenalty ?? 0;
        ApplyStatusDamage(10 + extraDamage, "Sunburn");
    }

    private void ProcessMorgulTouch()
    {
        if (!HasStatusEffect(StatusEffectEnum.MorgulTouch)) return;

        health -= 10;
        MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} suffers 10 damage from Morgul Touch.", Color.magenta);
        RefreshSelectedCharacterIconIfSelected();
        CharacterIcons.RefreshForHumanPlayerCharacter(this);
        ApplyStatusMovementPenalty(2, "Morgul Touch");

        if (health > 0) return;

        Leader sauron = FindSauronLeader();
        if (this is Leader || !ConvertToNazgulServant(sauron))
        {
            Killed(this is Leader ? GetOwner() : sauron);
        }
    }

    private void ApplyStatusDamage(int damage, string sourceName)
    {
        int reduction = GetTotalNegativeStatusDamageReduction();
        damage = Mathf.Max(0, damage - reduction);
        health = Mathf.Max(0, health - Mathf.Max(0, damage));
        MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} takes {damage} damage from {sourceName}.", Color.red);
        Sounds.Instance?.PlayVoicePain(this);
        RefreshSelectedCharacterIconIfSelected();
        CharacterIcons.RefreshForHumanPlayerCharacter(this);
        if (health < 1)
        {
            Killed(this is Leader ? GetOwner() : null);
        }
    }

    private Leader FindSauronLeader()
    {
        Leader[] leaders = FindObjectsByType<Leader>();
        for (int i = 0; i < leaders.Length; i++)
        {
            Leader leader = leaders[i];
            if (leader is PlayableLeader && !leader.killed && leader.GetBiome().isMorgulMaster)
            {
                return leader;
            }
        }

        return null;
    }

    private bool ConvertToNazgulServant(Leader sauron)
    {
        if (sauron == null || this is Leader) return false;

        Leader oldOwner = GetOwner();
        if (oldOwner != null && oldOwner.controlledCharacters.Contains(this))
        {
            oldOwner.controlledCharacters.Remove(this);
        }

        if (!sauron.controlledCharacters.Contains(this))
        {
            sauron.controlledCharacters.Add(this);
        }

        owner = sauron;
        alignment = sauron.GetAlignment();
        race = RacesEnum.Nazgul;
        health = 100;
        startingCharacter = false;
        isPlayerControlled = Game.Instance?.player == sauron;
        ClearStatusEffect(StatusEffectEnum.MorgulTouch);

        hex?.RedrawCharacters();
        hex?.RedrawArmies();
        CharacterIcons.RefreshForHumanPlayerOf(oldOwner);
        CharacterIcons.RefreshForHumanPlayerOf(sauron);
        RefreshSelectedCharacterIconIfSelected();
        RefreshActionsIfSelected();
        MessageDisplayNoUI.ShowMessage(hex, this, $"{characterName} becomes a Nazgul and joins Sauron.", Color.magenta);
        return true;
    }

    public List<CardData> GetTransferableObjects()
    {
        return objects.Where(x => x.transferable).ToList();
    }

    public void StoreReachableHexes()
    {
        reachableHexes = FindAnyObjectByType<HexPathRenderer>().FindAllHexesInRange(this);
    }

    public void StoreRelevantHexes()
    {
        Game game = Game.Instance;
        Board board = Board.Instance;
        // Pre-allocate exactly 190 elements for maximum efficiency
        List<Hex> relevantHexes = new(MAX_RELEVANT_HEXES);

        // Use direct access to source collections with index-based insertion
        // var inRangeHexes = hexPathRenderer.FindAllHexesInRange(c);

        var objectHexes = board.hexesWithObjects;
        var characterHexes = board.hexesWithCharacters;
        var pcHexes = board.hexesWithPCs;

        // Add items directly to pre-sized list using index
        //for (int i = 0; i < inRangeHexes.Count && relevantHexes.Count < game.maxRelevantHexes; i++)
        //    relevantHexes.Add(inRangeHexes[i]);

        for (int i = 0; i < objectHexes.Count && relevantHexes.Count < MAX_RELEVANT_HEXES; i++)
            relevantHexes.Add(objectHexes[i]);

        for (int i = 0; i < characterHexes.Count && relevantHexes.Count < MAX_RELEVANT_HEXES; i++)
            relevantHexes.Add(characterHexes[i]);

        for (int i = 0; i < pcHexes.Count && relevantHexes.Count < MAX_RELEVANT_HEXES; i++)
            relevantHexes.Add(pcHexes[i]);

        // Fill remaining slots with null (if any)
        int remainingHexes = MAX_RELEVANT_HEXES - relevantHexes.Count;
        for (int i = 0; i < remainingHexes; i++)
            relevantHexes.Add(null);

        Assert.IsTrue(relevantHexes.Count == MAX_RELEVANT_HEXES, "Relevant hexes list size mismatch!");
        this.relevantHexes = relevantHexes;
    }
}
