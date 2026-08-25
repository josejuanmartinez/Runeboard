using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

[Serializable]
public class CardsManifest
{
    public int deckCount = 0;
    public List<DeckManifestEntry> decks = new();
}

[Serializable]
public class DeckManifestEntry
{
    public string deckId;
    public string nation;
    public string thematic;
    public int alignment;
    public string resourcePath;
    public int cardCount;
    public bool sharedToAll;
    public string parentDeckId;
    public bool isBaseDeck;
    public string deckSpriteName;
    // Orthogonal to sharedToAll: sharedToAll means "not tied to one nation", excluded means
    // "this deck's cards are world content (artifacts, encounters), never part of any leader's
    // own drawable pool" — see GetSharedDecks(). A deck can be both (objects_shared) or neither.
    public bool excluded;
}

[Serializable]
public class DeckData
{
    public string deckId;
    public string nation;
    public int alignment;
    public List<CardData> cards = new();
}

public enum SituationCardOfferSource
{
    Situation,
    AI
}

public sealed class SituationCardOffer
{
    public CardData Card { get; }
    public SituationCardOfferSource Source { get; }
    public bool IsPlayable { get; }

    public SituationCardOffer(CardData card, SituationCardOfferSource source, bool isPlayable)
    {
        Card = card;
        Source = source;
        IsPlayable = isPlayable;
    }
}

[Serializable]
public class CardPlayabilityResult
{
    public bool isPlayable;
    public bool failsLevelRequirements;
    public bool failsResourceRequirements;
    public bool failsActionConditions;
    public bool failsAlreadyActioned;
    public bool failsCardHistoryRequirements;
    public bool failsStartingCityRequirement;
    public string cardHistoryReason;
    public string startingCityReason;

    public void Reset()
    {
        isPlayable = false;
        failsLevelRequirements = false;
        failsResourceRequirements = false;
        failsActionConditions = false;
        failsAlreadyActioned = false;
        failsCardHistoryRequirements = false;
        failsStartingCityRequirement = false;
        cardHistoryReason = null;
        startingCityReason = null;
    }
}

[Serializable]
public class EncounterStatusEffectData
{
    public string statusId;
    public int turns = 1;
}

[Serializable]
public class EncounterOutcomeData
{
    public string outcomeId;
    public string resultText;
    public string requiredAlignment = string.Empty;
    public int minCommander;
    public int minAgent;
    public int minEmmissary;
    public int minMage;
    public int minHealth;
    public int maxHealth = -1;
    public int healthDelta;
    public int goldDelta;
    public int leatherDelta;
    public int timberDelta;
    public int mountsDelta;
    public int ironDelta;
    public int steelDelta;
    public int mithrilDelta;
    public List<EncounterStatusEffectData> statuses = new();
}

[Serializable]
public class EncounterOptionData
{
    public string optionId;
    public string label;
    public string description;
    public List<EncounterOutcomeData> outcomes = new();
}

[Serializable]
public class CardData
{
    public int cardId;
    public string name;
    public string quote;
    public string actionEffect;
    public string type;
    public List<string> tags = new();
    public string deckId;
    public int alignment;
    public string actionClassName;
    public string action;
    public string spriteName;
    public string region;
    public string description;
    public string requirementsText;
    public string historyText;
    public string statusEffect;
    public int procChance;
    public string portraitName;
    public string characterGroup;
    public string referenceDeckId;
    public int referenceCardId;
    public List<EncounterOptionData> encounterOptions = new();
    public EncounterOptionData fleeOption;
    public int commander;
    public int agent;
    public int emmissary;
    public int mage;
    public RacesEnum race;
    public SexEnum sex = SexEnum.Male;
    public List<string> artifacts = new();
    public TroopsTypeEnum troopType;
    public List<ArmySpecialAbilityEnum> specialAbilities = new();

    // Card-owned requirements (migrated from Actions.json)
    public int commanderSkillRequired;
    public int agentSkillRequired;
    public int emissarySkillRequired;
    public int mageSkillRequired;
    public int difficulty;
    public int leatherRequired;
    public int mountsRequired;
    public int timberRequired;
    public int ironRequired;
    public int steelRequired;
    public int mithrilRequired;
    public int goldRequired;
    public int jokerRequired;

    public int leatherGranted;
    public int mountsGranted;
    public int timberGranted;
    public int ironGranted;
    public int steelGranted;
    public int mithrilGranted;
    public int goldGranted;

    public string startingPC = string.Empty;
    public InspireEffectData inspireEffectData;
    public string deckSpriteName;
    public string situation = string.Empty;
    public string situation2 = string.Empty;
    // PC cards only: the founded PC marks its hex as an entrance to the Underground.
    public bool isUnderground;

    // Object card fields (bonuses/effects granted while a character carries this object).
    // Migrated 1:1 from the retired Artifact class — name/spriteName double as
    // artifactName/spriteString. No alignment restriction: every object is usable by any
    // leader (Artifact's per-item alignment field was deliberately dropped, not carried over).
    public bool hidden;
    // How many instances of this Object exist in the world's random hidden-object scatter pool
    // (Board.SpawnArtifacts via GetAllObjectCardClones). A unique legendary item (Andúril, Narya)
    // should stay at the default 1; a common item that narratively grows in multiple places
    // (Athelas) can be set higher so it's not a one-shot find.
    public int copies = 1;
    public int commanderBonus;
    public int agentBonus;
    public int emmissaryBonus;
    public int mageBonus;
    public string passiveEffectId = "";
    public int passiveEffectValue;
    public bool transferable = true;
    public int healPerTurn;
    public int movementBonus;
    public bool ignoreTerrainMovementPenalty;
    public bool grantsHasteAtSea;
    public int autoScoutRadius;
    public int detectionEvasion;
    // Combat-relevant effects (attack/defense/vs-race/vs-troop-type/army bonuses) — a closed,
    // dropdown-only enum list (see ObjectCombatEffect.cs) rather than free-typed strings/ints,
    // so a card can't accidentally ship an unbalanced raw number. Non-combat Object fields
    // (above/below) don't feed Duel.cs or Army.cs and stay as plain fields.
    public System.Collections.Generic.List<ObjectCombatEffect> combatEffects = new();
    public int recruitBonusMenAtArms;
    public int scryAreaBonus;
    public int scryObjectBonus;
    public string negativeStatusImmunity;
    public int negativeStatusDurationReduction;
    public int negativeStatusDamageReduction;
    public int positiveStatusDurationBonus;
    public int positiveStatusEffectBonus;
    public bool grantsEnvironmentalImmunity;

    // Object card typed getters — ported from the retired Artifact class, same field names,
    // same clamping/matching semantics. Only meaningful when GetCardType() == Object, but
    // harmless (return 0/false) to call on any card since the backing fields default that way.
    public int GetHealPerTurn() => Mathf.Max(0, healPerTurn);
    public int GetMovementBonus() => Mathf.Max(0, movementBonus);
    public bool GetIgnoreTerrainMovementPenalty() => ignoreTerrainMovementPenalty;
    public int GetAutoScoutRadius() => Mathf.Max(0, autoScoutRadius);
    public int GetDetectionEvasion() => Mathf.Max(0, detectionEvasion);

    // Sum rather than "first match wins" — a card's combatEffects list can carry more than one
    // entry of the same type (e.g. two different vs-race bonuses), unlike the old single-value
    // flat fields it replaced.
    public int GetAttackBonus() =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.AttackBonus).Sum(e => e.Value) ?? 0;

    public int GetDefenseBonus() =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.DefenseBonus).Sum(e => e.Value) ?? 0;

    public int GetAttackBonusVsRace(RacesEnum race) =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.AttackBonusVsRace && e.targetRace == race)
            .Sum(e => e.Value) ?? 0;

    public int GetAttackBonusVsTroopType(TroopsTypeEnum troopType) =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.AttackBonusVsTroopType && e.targetTroopType == troopType)
            .Sum(e => e.Value) ?? 0;

    public int GetDefenseBonusVsRace(RacesEnum race) =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.DefenseBonusVsRace && e.targetRace == race)
            .Sum(e => e.Value) ?? 0;

    public int GetDefenseBonusVsTroopType(TroopsTypeEnum troopType) =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.DefenseBonusVsTroopType && e.targetTroopType == troopType)
            .Sum(e => e.Value) ?? 0;

    public int GetRecruitBonusMenAtArms() => Mathf.Max(0, recruitBonusMenAtArms);
    public int GetScryAreaBonus() => Mathf.Max(0, scryAreaBonus);
    public int GetScryObjectBonus() => Mathf.Max(0, scryObjectBonus);

    public bool GetNegativeStatusImmunity(StatusEffectEnum effect)
    {
        return TryGetNegativeStatusImmunity(out StatusEffectEnum immunity) && immunity == effect;
    }

    // Enum.TryParse accepts numeric strings (for example "0" => Halted). Card JSON should store
    // the named value, so reject numeric serialization debris instead of treating it as a real
    // immunity or generating a broken TMP sprite tag such as <sprite name="0">.
    private bool TryGetNegativeStatusImmunity(out StatusEffectEnum effect)
    {
        effect = default;
        if (string.IsNullOrWhiteSpace(negativeStatusImmunity) ||
            int.TryParse(negativeStatusImmunity, out _))
            return false;

        return Enum.TryParse(negativeStatusImmunity, true, out effect)
            && Enum.IsDefined(typeof(StatusEffectEnum), effect);
    }

    public int GetNegativeStatusDurationReduction() => Mathf.Max(0, negativeStatusDurationReduction);
    public int GetNegativeStatusDamageReduction() => Mathf.Max(0, negativeStatusDamageReduction);
    public int GetPositiveStatusDurationBonus() => Mathf.Max(0, positiveStatusDurationBonus);
    public int GetPositiveStatusEffectBonus() => Mathf.Max(0, positiveStatusEffectBonus);

    // scryObjectBonus doubles as "Find Object" action-difficulty reduction, matching the
    // old Artifact.GetActionDifficultyReduction's hardcoded FindArtifact tie-in.
    public int GetActionDifficultyReduction(string actionClassName)
    {
        if (scryObjectBonus > 0 && string.Equals(actionClassName, FindArtifact.ActionRef, StringComparison.OrdinalIgnoreCase))
            return scryObjectBonus;
        return 0;
    }

    public int GetArmyAttackStrengthBonus() =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.ArmyAttackBonus).Sum(e => e.Value) ?? 0;

    public int GetArmyDefenseStrengthBonus() =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.ArmyDefenseBonus).Sum(e => e.Value) ?? 0;

    public int GetEnemyArmyDefensePenaltySameHex() =>
        combatEffects?.Where(e => e != null && e.type == ObjectCombatEffectTypeEnum.EnemyArmyDefensePenaltySameHex).Sum(e => e.Value) ?? 0;
    public bool GrantsEnvironmentalImmunity() => grantsEnvironmentalImmunity;
    public bool GrantsHasteAtSea() => grantsHasteAtSea;

    // Ported from Artifact.GetSpriteString()/GetHoverText() for the object-icon UI
    // (ArtifactRenderer) — same fallback sprite and same mechanical-detail summary line.
    public string GetSpriteString() => !string.IsNullOrEmpty(spriteName) ? spriteName : "artifact";

    public string GetHoverText()
    {
        var sb = new System.Collections.Generic.List<string> { $"<sprite name=\"{GetSpriteString()}\">{name}" };
        System.Collections.Generic.List<string> details = BuildObjectMechanicalDetails();
        if (details.Count > 0) sb.Add($"<br>{string.Join(", ", details)}");
        return string.Join("", sb);
    }

    private System.Collections.Generic.List<string> BuildObjectMechanicalDetails()
    {
        var details = new System.Collections.Generic.List<string>();
        if (commanderBonus > 0) details.Add($"+{commanderBonus}<sprite name=\"commander\">");
        if (agentBonus > 0) details.Add($"+{agentBonus}<sprite name=\"agent\">");
        if (emmissaryBonus > 0) details.Add($"+{emmissaryBonus}<sprite name=\"emmissary\">");
        if (mageBonus > 0) details.Add($"+{mageBonus}<sprite name=\"mage\">");
        if (combatEffects != null)
        {
            foreach (ObjectCombatEffect effect in combatEffects)
            {
                if (effect == null) continue;
                switch (effect.type)
                {
                    case ObjectCombatEffectTypeEnum.AttackBonus:
                        details.Add($"+{effect.Value} attack");
                        break;
                    case ObjectCombatEffectTypeEnum.DefenseBonus:
                        details.Add($"+{effect.Value} defense");
                        break;
                    case ObjectCombatEffectTypeEnum.AttackBonusVsRace:
                        details.Add($"+{effect.Value} attack vs {effect.targetRace}");
                        break;
                    case ObjectCombatEffectTypeEnum.AttackBonusVsTroopType:
                        details.Add($"+{effect.Value} attack vs {effect.targetTroopType}");
                        break;
                    case ObjectCombatEffectTypeEnum.DefenseBonusVsRace:
                        details.Add($"+{effect.Value} defense vs {effect.targetRace}");
                        break;
                    case ObjectCombatEffectTypeEnum.DefenseBonusVsTroopType:
                        details.Add($"+{effect.Value} defense vs {effect.targetTroopType}");
                        break;
                    case ObjectCombatEffectTypeEnum.ArmyAttackBonus:
                        details.Add($"+{effect.Value} army attack");
                        break;
                    case ObjectCombatEffectTypeEnum.ArmyDefenseBonus:
                        details.Add($"+{effect.Value} army defense");
                        break;
                    case ObjectCombatEffectTypeEnum.EnemyArmyDefensePenaltySameHex:
                        details.Add($"-{effect.Value} enemy army defense in same hex");
                        break;
                }
            }
        }

        if (healPerTurn > 0) details.Add($"heals {healPerTurn} each turn");
        if (movementBonus > 0) details.Add($"+{movementBonus} movement");
        if (ignoreTerrainMovementPenalty) details.Add("ignores terrain movement penalties");
        if (grantsHasteAtSea) details.Add("grants Haste at sea");
        if (autoScoutRadius > 0) details.Add($"auto-scouts radius {autoScoutRadius}");
        if (detectionEvasion > 0) details.Add($"+{detectionEvasion * 10}% harder to detect");

        if (recruitBonusMenAtArms > 0) details.Add($"+{recruitBonusMenAtArms} men-at-arms recruited");
        if (scryAreaBonus > 0) details.Add($"+{scryAreaBonus} Scry Area range");
        if (scryObjectBonus > 0) details.Add($"+{scryObjectBonus} Find Object");

        if (TryGetNegativeStatusImmunity(out StatusEffectEnum immunity))
            details.Add($"immune to <sprite name=\"{immunity.ToString().ToLowerInvariant()}\">{immunity}");
        if (negativeStatusDurationReduction > 0) details.Add($"-{negativeStatusDurationReduction} negative status duration");
        if (negativeStatusDamageReduction > 0) details.Add($"-{negativeStatusDamageReduction} negative status damage");
        if (positiveStatusDurationBonus > 0) details.Add($"+{positiveStatusDurationBonus} positive status duration");
        if (positiveStatusEffectBonus > 0) details.Add($"+{positiveStatusEffectBonus} positive status healing");

        if (grantsEnvironmentalImmunity) details.Add("immune to negative environmental cards");
        if (!transferable) details.Add("non-transferable");
        return details;
    }

    // Independent copy — e.g. handing a template Object card to a character as an owned
    // instance. Delegates to DeckManager's field-complete clone (used everywhere else a card
    // needs duplicating) rather than a second, divergence-prone copy of the field list.
    public CardData Clone() => DeckManager.CloneCard(this);

    public CardSituationEnum GetSituation()
        => Enum.TryParse(situation, true, out CardSituationEnum s) ? s : CardSituationEnum.None;

    public CardSituationEnum GetSecondarySituation()
        => Enum.TryParse(situation2, true, out CardSituationEnum s) ? s : CardSituationEnum.None;

    public bool MatchesAnySituation(ICollection<CardSituationEnum> activeSituations)
    {
        if (activeSituations == null || activeSituations.Count == 0) return false;
        CardSituationEnum primary = GetSituation();
        CardSituationEnum secondary = GetSecondarySituation();
        return (primary != CardSituationEnum.None && activeSituations.Contains(primary))
            || (secondary != CardSituationEnum.None && activeSituations.Contains(secondary));
    }

    [NonSerialized] public bool isPlayable;
    [NonSerialized] public CardPlayabilityResult playability = new CardPlayabilityResult();
    [NonSerialized] public Hex encounterTargetHex;
    [NonSerialized] public bool encounterRevealed;
    [NonSerialized] public bool hasShownHandAnimation;

    public CardTypeEnum GetCardType()
    {
        return CardTypeParser.Parse(type);
    }

    public int GetCharacterPointTotal()
    {
        if (GetCardType() != CardTypeEnum.Character) return 0;
        return Mathf.Max(0, commander) + Mathf.Max(0, agent) + Mathf.Max(0, emmissary) + Mathf.Max(0, mage);
    }

    public int GetAdditionalGoldCost()
    {
        return GetCardType() == CardTypeEnum.Character ? GetCharacterPointTotal() * 5 : 0;
    }

    public int GetTotalGoldCost()
    {
        if (GetCardType() == CardTypeEnum.Character)
        {
            return GetAdditionalGoldCost();
        }

        return Mathf.Max(0, goldRequired) + GetAdditionalGoldCost();
    }

    public bool IsEventCard()
    {
        return GetCardType() == CardTypeEnum.Event;
    }

    public bool IsEncounterCard()
    {
        return GetCardType() == CardTypeEnum.Encounter;
    }

    public bool HasTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tags == null) return false;
        return tags.Any(t => string.Equals(t?.Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public bool HasAnyTag(params string[] queryTags)
    {
        if (queryTags == null || queryTags.Length == 0) return false;
        return queryTags.Any(HasTag);
    }

    public string GetActionRef()
    {
        return !string.IsNullOrWhiteSpace(action) ? action : actionClassName;
    }

    public string GetRenderedDescription(bool includeFoundingText = false)
    {
        string body = GetDescriptionBody(includeFoundingText);
        string quoteBlock = GetQuoteBlock();

        if (string.IsNullOrWhiteSpace(body))
        {
            return quoteBlock;
        }

        if (string.IsNullOrWhiteSpace(quoteBlock))
        {
            return body;
        }

        return $"{body}\n\n{quoteBlock}";
    }

    public string GetDescriptionBody(bool includeFoundingText = false)
    {
        CardTypeEnum cardType = GetCardType();
        string body = cardType switch
        {
            CardTypeEnum.Character => GetCharacterDescription(),
            CardTypeEnum.Army => GetArmyDescription(),
            CardTypeEnum.Land => GetLandDescription(),
            CardTypeEnum.PC => PcDescriptionBuilder.BuildBody(this, includeFoundingText),
            CardTypeEnum.Event or CardTypeEnum.Action or CardTypeEnum.Spell or CardTypeEnum.Environmental => GetActionEffectText(),
            CardTypeEnum.Encounter => !string.IsNullOrWhiteSpace(description) ? description.Trim() : string.Empty,
            CardTypeEnum.Object => GetObjectDescription(),
            _ => string.Empty
        };

        if (cardType == CardTypeEnum.Character && !string.IsNullOrWhiteSpace(actionEffect))
        {
            string effect = actionEffect.Trim();
            body = string.IsNullOrWhiteSpace(body) ? effect : $"{body}\n\n{effect}";
        }

        return cardType == CardTypeEnum.Action
            || cardType == CardTypeEnum.Event
            || cardType == CardTypeEnum.Spell
            || cardType == CardTypeEnum.Environmental
            || cardType == CardTypeEnum.Land
            || cardType == CardTypeEnum.PC
                ? AppendCardStatusText(body)
                : body;
    }

    public string GetQuoteBlock()
    {
        if (string.IsNullOrWhiteSpace(quote)) return string.Empty;

        string text = Regex.Replace(quote.Trim(), "<[^>]+>", string.Empty).Trim();
        if (text.StartsWith("\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal) && text.Length >= 2)
        {
            text = text.Substring(1, text.Length - 2).Trim();
        }

        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return $"<align=\"center\"><color=#d3d3d388><i>\"{text}\"</i></color></align>";
    }

    public string GetArmyDescription()
    {
        if (GetCardType() != CardTypeEnum.Army) return string.Empty;
        return GetArmySummary();
    }

    public string GetActionEffectText()
    {
        return string.IsNullOrWhiteSpace(actionEffect) ? string.Empty : actionEffect.Trim();
    }

    // Flavor text (authored `description`) followed by the same mechanical-effect summary
    // used in the carried-object hover tooltip (BuildObjectMechanicalDetails/GetHoverText),
    // reformatted one-per-line for the larger card face instead of comma-joined.
    public string GetObjectDescription()
    {
        if (GetCardType() != CardTypeEnum.Object) return string.Empty;

        string flavor = !string.IsNullOrWhiteSpace(description) ? description.Trim() : string.Empty;
        List<string> details = BuildObjectMechanicalDetails();
        string effectsBlock = details.Count > 0 ? string.Join("\n", details.Select(d => $"• {d}")) : string.Empty;

        if (string.IsNullOrWhiteSpace(flavor)) return effectsBlock;
        if (string.IsNullOrWhiteSpace(effectsBlock)) return flavor;
        return $"{flavor}\n\n{effectsBlock}";
    }

    private string AppendCardStatusText(string body)
    {
        // Status effects are internal gameplay state and should not be exposed in card authoring/display.
        // Army proc configuration is driven by specialAbilities + procChance instead.
        return body;
    }

    private string GetArmySummary()
    {
        if (GetCardType() != CardTypeEnum.Army) return string.Empty;

        string raceLabel = FormatRaceLabel(race);
        string troopLabel = GetDefaultTroopName(troopType);
        if (string.IsNullOrWhiteSpace(troopLabel))
        {
            troopLabel = !string.IsNullOrWhiteSpace(name) ? name : string.Empty;
        }

        string spriteTag = $"<sprite name=\"{troopType.ToString().ToLowerInvariant()}\">";
        List<string> abilities = GetArmyAbilityLabels();

        if (string.IsNullOrWhiteSpace(troopLabel))
        {
            if (abilities.Count > 0)
            {
                return !string.IsNullOrWhiteSpace(raceLabel)
                    ? $"{raceLabel}. {string.Join(". ", abilities)}."
                    : string.Join(". ", abilities);
            }

            return raceLabel;
        }

        string baseText = string.IsNullOrWhiteSpace(raceLabel)
            ? $"{troopLabel} {spriteTag}."
            : $"{raceLabel}. {troopLabel} {spriteTag}.";
        return abilities.Count > 0
            ? $"{baseText} {string.Join(". ", abilities)}."
            : baseText;
    }

    private string GetLandDescription()
    {
        if (GetCardType() != CardTypeEnum.Land) return string.Empty;

        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(region))
        {
            parts.Add($"{PcDescriptionBuilder.FormatDisplayRegionName(region)}.");
        }

        List<string> grants = new();
        if (leatherGranted > 0) grants.Add($"{leatherGranted}<sprite name=\"leather\">");
        if (timberGranted > 0) grants.Add($"{timberGranted}<sprite name=\"timber\">");
        if (mountsGranted > 0) grants.Add($"{mountsGranted}<sprite name=\"mounts\">");
        if (ironGranted > 0) grants.Add($"{ironGranted}<sprite name=\"iron\">");
        if (steelGranted > 0) grants.Add($"{steelGranted}<sprite name=\"steel\">");
        if (mithrilGranted > 0) grants.Add($"{mithrilGranted}<sprite name=\"mithril\">");
        if (goldGranted > 0) grants.Add($"{goldGranted}<sprite name=\"gold\">");
        if (grants.Count > 0)
        {
            parts.Add(string.Join(string.Empty, grants));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add($"Reveals hexes and allows founding PCs originally from {PcDescriptionBuilder.FormatDisplayRegionName(name)}.");
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public string GetCharacterDescription()
    {
        if (GetCardType() != CardTypeEnum.Character) return string.Empty;

        List<string> lines = new();

        if (!string.IsNullOrWhiteSpace(startingPC))
        {
            lines.Add($"Starts at {startingPC}.");
        }

        List<string> classParts = new();
        AppendCharacterLevel(classParts, "commander", commander);
        AppendCharacterLevel(classParts, "agent", agent);
        AppendCharacterLevel(classParts, "emmissary", emmissary);
        AppendCharacterLevel(classParts, "mage", mage);
        if (classParts.Count > 0)
        {
            lines.Add(string.Join(" ", classParts));
        }

        return lines.Count > 0 ? string.Join(" ", lines) : string.Empty;
    }

    public bool EvaluatePlayability(Character selectedCharacter, Func<Character, bool> resourceCheck = null, Func<Character, bool> conditionCheck = null)
    {
        playability ??= new CardPlayabilityResult();
        playability.Reset();

        // Object cards are lookup-only data records (bonuses/effects an object grants while
        // carried) — never drawn, drafted, or played as an action.
        if (GetCardType() == CardTypeEnum.Object)
        {
            isPlayable = false;
            playability.isPlayable = false;
            return false;
        }

        // Human characters get one card action per turn. Keep this at the shared CardData
        // playability boundary so every presentation path (hand, bloom, and situation cards)
        // disables the complete card set consistently after the selected character acts.
        // AI characters deliberately bypass this gate; AITurnController limits their orders
        // according to difficulty instead.
        if (selectedCharacter != null
            && selectedCharacter.isPlayerControlled
            && selectedCharacter.hasActionedThisTurn)
        {
            playability.failsActionConditions = true;
            playability.failsAlreadyActioned = true;
            isPlayable = false;
            playability.isPlayable = false;
            return false;
        }

        if (GetCardType() == CardTypeEnum.Encounter)
        {
            bool atTargetHex = selectedCharacter != null &&
                               (encounterTargetHex == null || selectedCharacter.hex == encounterTargetHex);
            playability.failsActionConditions = !atTargetHex;
            isPlayable = atTargetHex;
            playability.isPlayable = isPlayable;
            return isPlayable;
        }

        // Environmental cards are global effects. They do not require an acting character;
        // only their owning leader's resource check (when supplied by Card) can block them.
        // At most one may be played per leader per turn — the board only has a single active
        // environment slot (EnvironmentalCardManager.ActiveCard) and a second play would just
        // silently discard the first card's effect.
        if (GetCardType() == CardTypeEnum.Environmental)
        {
            bool environmentalResourcesOk = resourceCheck == null || resourceCheck(selectedCharacter);
            bool environmentalConditionsOk = conditionCheck == null || conditionCheck(selectedCharacter);
            Leader environmentalOwner = selectedCharacter?.GetOwner() ?? Game.Instance?.currentlyPlaying;
            int currentTurn = Game.Instance?.turn ?? 0;
            bool environmentalTurnLimitOk = environmentalOwner == null
                || EnvironmentalCardManager.GetOrCreate().CanNationPlay(environmentalOwner, currentTurn, out _);
            playability.failsResourceRequirements = !environmentalResourcesOk;
            playability.failsActionConditions = !environmentalConditionsOk || !environmentalTurnLimitOk;
            isPlayable = environmentalResourcesOk && environmentalConditionsOk && environmentalTurnLimitOk;
            playability.isPlayable = isPlayable;
            return isPlayable;
        }

        if (GetCardType() == CardTypeEnum.Character || GetCardType() == CardTypeEnum.Army)
        {
            bool cardResourcesOk = resourceCheck != null
                ? resourceCheck(selectedCharacter)
                : selectedCharacter != null && MeetsResourceRequirements(selectedCharacter.GetOwner());
            bool cardConditionsOk = conditionCheck == null || conditionCheck(selectedCharacter);

            playability.failsResourceRequirements = !cardResourcesOk;
            playability.failsActionConditions = !cardConditionsOk;

            bool startingCityOk = true;
            if (GetCardType() == CardTypeEnum.Character && !string.IsNullOrWhiteSpace(startingPC))
            {
                Hex hex = selectedCharacter?.hex;
                PC pc = hex?.GetPCData();
                startingCityOk = pc != null && CardNameUtility.Equals(pc.pcName, startingPC);
                if (!startingCityOk)
                {
                    playability.failsStartingCityRequirement = true;
                    playability.startingCityReason = $"Must be played at {startingPC}.";
                }
            }

            isPlayable = cardResourcesOk && cardConditionsOk && startingCityOk;
            playability.isPlayable = isPlayable;
            return isPlayable;
        }

        if (selectedCharacter == null)
        {
            playability.failsActionConditions = true;
            isPlayable = false;
            return false;
        }

        bool spellArcaneOverride = GetCardType() == CardTypeEnum.Spell
            && selectedCharacter.HasStatusEffect(StatusEffectEnum.ArcaneInsight);

        bool levelsOk = selectedCharacter.GetCommander() >= commanderSkillRequired
            && selectedCharacter.GetAgent() >= agentSkillRequired
            && selectedCharacter.GetEmmissary() >= emissarySkillRequired
            && (selectedCharacter.GetMage() >= mageSkillRequired || spellArcaneOverride);

        bool resourcesOk = resourceCheck != null
            ? resourceCheck(selectedCharacter)
            : MeetsResourceRequirements(selectedCharacter.GetOwner());
        bool conditionsOk = conditionCheck == null || conditionCheck(selectedCharacter);
        bool cardHistoryOk = MeetsCardHistoryRequirements(selectedCharacter.GetOwner(), out string cardHistoryReason);

        playability.failsLevelRequirements = !levelsOk;
        playability.failsResourceRequirements = !resourcesOk;
        playability.failsActionConditions = !conditionsOk;
        playability.failsCardHistoryRequirements = !cardHistoryOk;
        playability.cardHistoryReason = cardHistoryReason;

        isPlayable = levelsOk && resourcesOk && conditionsOk && cardHistoryOk;
        playability.isPlayable = isPlayable;
        return isPlayable;
    }

    public bool MeetsResourceRequirements(Leader owner)
    {
        if (owner == null) return false;
        if (leatherRequired > 0 && owner.leatherAmount < leatherRequired) return false;
        if (timberRequired > 0 && owner.timberAmount < timberRequired) return false;
        if (mountsRequired > 0 && owner.mountsAmount < mountsRequired) return false;
        if (ironRequired > 0 && owner.ironAmount < ironRequired) return false;
        if (steelRequired > 0 && owner.steelAmount < steelRequired) return false;
        if (mithrilRequired > 0 && owner.mithrilAmount < mithrilRequired) return false;
        if (GetTotalGoldCost() > 0 && owner.goldAmount < GetTotalGoldCost()) return false;
        return true;
    }

    public bool MeetsCardHistoryRequirements(Leader owner, out string reason)
    {
        reason = null;
        if (owner is not PlayableLeader playableLeader) return true;

        if (GetCardType() == CardTypeEnum.Land && playableLeader.HasPlayedLandThisTurn())
        {
            reason = "Only one land card can be played each turn.";
            return false;
        }

        if (GetCardType() != CardTypeEnum.PC) return true;
        if (string.IsNullOrWhiteSpace(region)) return true;
        if (IsRegionDiscovered(region, owner)) return true;

        reason = $"{region} not discovered yet.";
        return false;
    }

    private bool IsRegionDiscovered(string region, Leader owner)
    {
        if (string.IsNullOrWhiteSpace(region) || owner == null) return true;

        Game game = UnityEngine.Object.FindFirstObjectByType<Game>();
        Board board = game?.board != null ? game.board : Board.Instance;
        if (board == null) return false;

        DeckManager deckManager = UnityEngine.Object.FindFirstObjectByType<DeckManager>();
        string normalizedTarget = NormalizeRegion(region);

        foreach (Hex hex in board.hexes.Values)
        {
            if (hex == null) continue;

            string hexRegion = hex.GetLandRegion();
            if (string.IsNullOrWhiteSpace(hexRegion))
            {
                PC pc = hex.GetPCData();
                if (pc != null && deckManager != null)
                {
                    hexRegion = deckManager.ResolveRegionForPc(pc);
                }
            }

            if (string.IsNullOrWhiteSpace(hexRegion)) continue;
            if (!string.Equals(NormalizeRegion(hexRegion), normalizedTarget, StringComparison.Ordinal)) continue;

            if (hex.IsHexRevealed() || hex.IsScoutedBy(owner)) return true;
        }

        return false;
    }

    private static string NormalizeRegion(string s)
        => string.IsNullOrWhiteSpace(s) ? string.Empty : new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string GetDefaultTroopName(TroopsTypeEnum troopType)
    {
        return troopType switch
        {
            TroopsTypeEnum.ma => "Men-at-arms",
            TroopsTypeEnum.ar => "Archers",
            TroopsTypeEnum.li => "Light Infantry",
            TroopsTypeEnum.hi => "Heavy Infantry",
            TroopsTypeEnum.lc => "Light Cavalry",
            TroopsTypeEnum.hc => "Heavy Cavalry",
            TroopsTypeEnum.ca => "Catapults",
            TroopsTypeEnum.ws => "Warships",
            _ => string.Empty
        };
    }

    private static string FormatRaceLabel(RacesEnum value)
    {
        string raw = value.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string formatted = raw.Trim().ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(formatted);
    }

    private List<string> GetArmyAbilityLabels()
    {
        if (specialAbilities == null || specialAbilities.Count == 0) return new List<string>();

        int chance = Mathf.Clamp(procChance <= 0 ? 100 : procChance, 1, 100);
        var labels = new List<string>();
        foreach (ArmySpecialAbilityEnum ability in specialAbilities)
        {
            string label = FormatArmyAbilityLabel(ability);
            if (!string.IsNullOrWhiteSpace(label))
                labels.Add($"{label} {chance}%");
        }
        return labels;
    }

    private static string FormatArmyAbilityLabel(ArmySpecialAbilityEnum ability)
    {
        string abilityName = ability switch
        {
            ArmySpecialAbilityEnum.Longrange => "Long range",
            ArmySpecialAbilityEnum.ShortRange => "Short range",
            ArmySpecialAbilityEnum.RefusingDuels => "Refusing duels",
            ArmySpecialAbilityEnum.ArcaneInsight => "Arcane insight",
            ArmySpecialAbilityEnum.DuelSupremacy => "Duel supremacy",
            ArmySpecialAbilityEnum.MorgulTouch => "Morgul touch",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                Regex.Replace(ability.ToString(), "([a-z])([A-Z])", "$1 $2").ToLowerInvariant())
        };

        string spriteName = ability switch
        {
            ArmySpecialAbilityEnum.Longrange => "longrange",
            ArmySpecialAbilityEnum.ShortRange => "shortrange",
            ArmySpecialAbilityEnum.ArcaneInsight => "arcaneinsight",
            ArmySpecialAbilityEnum.RefusingDuels => "refusingduels",
            ArmySpecialAbilityEnum.DuelSupremacy => "duelsupremacy",
            ArmySpecialAbilityEnum.MorgulTouch => "morgultouch",
            _ => ability.ToString().ToLowerInvariant()
        };

        return $"{abilityName} <sprite name=\"{spriteName}\">";
    }

    private static void AppendCharacterLevel(List<string> parts, string spriteName, int required)
    {
        if (parts == null || string.IsNullOrWhiteSpace(spriteName) || required <= 0) return;
        parts.Add($"{required}<sprite name=\"{spriteName}\">");
    }
}

public class DeckManager : MonoBehaviour
{
    private enum BalancedDeckBucket
    {
        Army,
        Event,
        Environmental,
        PC,
        Land,
        Encounter,
        Character,
        ActionSpell,
        Misc
    }

    private static readonly BalancedDeckBucket[] BalancedDrawPattern =
    {
        BalancedDeckBucket.Army,
        BalancedDeckBucket.Army,
        BalancedDeckBucket.Event,
        BalancedDeckBucket.Event,
        BalancedDeckBucket.Event,
        BalancedDeckBucket.Environmental,
        BalancedDeckBucket.PC,
        BalancedDeckBucket.Land,
        BalancedDeckBucket.Land,
        BalancedDeckBucket.Land,
        BalancedDeckBucket.Encounter,
        BalancedDeckBucket.Character,
        BalancedDeckBucket.ActionSpell,
        BalancedDeckBucket.ActionSpell,
        BalancedDeckBucket.ActionSpell,
        BalancedDeckBucket.ActionSpell
    };

    private class PlayerDeckState
    {
        public string deckId;
        // Every card the leader currently holds or could still play this game. There used to be
        // a separate hand/drawPile split (a fixed-size "hand" you drew into, gated behind a
        // handSize cap) but nothing ever rendered it as a UI tray — every consumer (AI scoring,
        // human card play, opportunity cards) already treated the two lists as one combined pool.
        // Merged into a single list to drop the replenish/guarantee machinery that only existed
        // to keep that now-invisible split populated.
        public readonly List<CardData> drawPile = new();
        public readonly List<CardData> discardPile = new();
        public readonly List<CardData> situationPool = new List<CardData>();
    }

    public static DeckManager Instance { get; private set; }

    [Header("References")]
    [FormerlySerializedAs("cardCameObject")]
    [SerializeField] GameObject cardCameObject;
    [SerializeField] GameObject tokenCardTemplate;
    [SerializeField] CardBloomWheel cardBloomWheel;

    [Header("Config")]
    [SerializeField] private bool initializeOnStart = true;
    [SerializeField] private string cardsManifestResourcePath = "Cards";
    [SerializeField] private int handSize = 6;
    [Header("Debug")]
    [SerializeField] private bool logInitialization = true;

    public List<CardData> cards = new();

    [Header("Status Effects")]
    [SerializeField] private List<string> availableStatusEffectIds = Enum.GetNames(typeof(StatusEffectEnum)).ToList();

    [Header("Inspector (Runtime)")]
    [SerializeField] private List<DeckData> inspectorDecks = new();

    private readonly Dictionary<string, DeckManifestEntry> deckManifestById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeckData> loadedDecksById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Leader, PlayerDeckState> playerDecks = new();

    private bool loaded;
    public bool IsLoaded => loaded;

    public IReadOnlyList<string> AvailableStatusEffectIds => availableStatusEffectIds;

    private void OnValidate()
    {
        availableStatusEffectIds = Enum.GetNames(typeof(StatusEffectEnum)).ToList();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private IEnumerator Start()
    {
        if (!initializeOnStart) yield break;

        // Let the active startup canvas render once before parsing the full card catalog.
        // InitializeFromResources is synchronous, so doing it in the first Start frame made
        // the application look frozen before Unity had presented any feedback at all.
        yield return null;

        if (!loaded) InitializeFromResources();

        Game game = FindFirstObjectByType<Game>();
        if (game != null && game.started)
        {
            InitializeHandsForCurrentGame();
        }
    }

    public bool InitializeFromResources()
    {
        // Several runtime systems need card lookups and call this defensively. Reloading an
        // already-loaded catalog used to clear playerDecks while leaving the rendered hand in
        // place, producing bloom tokens that could never be consumed.
        if (loaded) return true;

        loaded = false;
        cards.Clear();
        inspectorDecks.Clear();
        deckManifestById.Clear();
        loadedDecksById.Clear();
        playerDecks.Clear();

        TextAsset manifestAsset = Resources.Load<TextAsset>(cardsManifestResourcePath);
        if (manifestAsset == null)
        {
            Debug.LogWarning($"DeckManager: Could not load cards manifest from Resources/{cardsManifestResourcePath}.json");
            return false;
        }

        CardsManifest manifest = JsonUtility.FromJson<CardsManifest>(manifestAsset.text);
        if (manifest == null || manifest.decks == null || manifest.decks.Count == 0)
        {
            Debug.LogWarning("DeckManager: Cards manifest is empty or malformed.");
            return false;
        }

        foreach (DeckManifestEntry entry in manifest.decks)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.deckId)) continue;
            deckManifestById[entry.deckId] = entry;
        }

        foreach (DeckManifestEntry entry in deckManifestById.Values)
        {
            if (string.IsNullOrWhiteSpace(entry.resourcePath)) continue;

            TextAsset deckAsset = Resources.Load<TextAsset>(entry.resourcePath);
            if (deckAsset == null)
            {
                Debug.LogWarning($"DeckManager: Could not load deck file Resources/{entry.resourcePath}.json");
                continue;
            }

            DeckData deckData = JsonUtility.FromJson<DeckData>(deckAsset.text);
            if (deckData == null || string.IsNullOrWhiteSpace(deckData.deckId))
            {
                Debug.LogWarning($"DeckManager: Deck file at {entry.resourcePath} is empty or malformed.");
                continue;
            }

            if (deckData.cards == null) deckData.cards = new();
            foreach (CardData card in deckData.cards)
            {
                if (card == null) continue;
                card.deckId = deckData.deckId;
                card.alignment = deckData.alignment;
                card.deckSpriteName = entry.deckSpriteName;
            }

            loadedDecksById[deckData.deckId] = deckData;
            cards.AddRange(deckData.cards);
        }

        InjectMissingStartingPcAndLandReferences();
        ResolveCardReferences();
        cards.Clear();
        foreach (DeckManifestEntry entry in deckManifestById.Values)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.deckId)) continue;
            if (!loadedDecksById.TryGetValue(entry.deckId, out DeckData deckData) || deckData?.cards == null) continue;
            cards.AddRange(deckData.cards);
        }

        loaded = loadedDecksById.Count > 0;
        RefreshInspectorDecks();

        if (logInitialization)
        {
            Debug.Log($"DeckManager: Loaded {loadedDecksById.Count} decks, {cards.Count} cards, handSize={handSize}.");
        }

        return loaded;
    }

    public bool InitializeHandsForCurrentGame()
    {
        if (!loaded && !InitializeFromResources()) return false;

        Game game = FindFirstObjectByType<Game>();
        if (game == null)
        {
            Debug.LogWarning("DeckManager: Game not found; cannot initialize player hands.");
            return false;
        }

        List<PlayableLeader> leaders = new();
        if (game.player != null) leaders.Add(game.player);
        if (game.competitors != null) leaders.AddRange(game.competitors.Where(x => x != null));

        InitializeHands(leaders);
        InitializeNonPlayableLeaderHands(game.npcs);
        return true;
    }

    public void InitializeHands(IEnumerable<PlayableLeader> leaders)
    {
        playerDecks.Clear();
        if (leaders == null) return;

        foreach (PlayableLeader leader in leaders.Distinct())
        {
            if (leader == null) continue;
            PlayerDeckState state = BuildDeckStateForLeader(leader);
            if (state != null)
            {
                playerDecks[leader] = state;
            }
        }
    }

    // Called after InitializeHands (which clears playerDecks) — never clears the dict itself,
    // so it's safe to add NPL entries alongside the PlayableLeader ones already populated.
    public void InitializeNonPlayableLeaderHands(IEnumerable<NonPlayableLeader> leaders)
    {
        if (leaders == null) return;

        foreach (NonPlayableLeader leader in leaders.Distinct())
        {
            if (leader == null) continue;
            PlayerDeckState state = BuildDeckStateForLeader(leader);
            if (state != null)
            {
                playerDecks[leader] = state;
            }
        }
    }

    // Every card this leader currently holds or could still play this game — the live,
    // per-instance pool both AI full-deck scoring (AITurnController) and human card play
    // operate on. Deliberately excludes discardPile (already played, not consumable again until
    // a reshuffle moves it back into drawPile) and situationPool (a separate non-drawable
    // opportunity-card pool).
    public IReadOnlyList<CardData> GetFullDeck(Leader leader)
    {
        if (leader == null || !playerDecks.TryGetValue(leader, out PlayerDeckState state)) return Array.Empty<CardData>();
        // Snapshot rather than the live list — callers (AI scoring) hold this across yields
        // while consumption elsewhere can mutate state.drawPile concurrently.
        return state.drawPile.ToList();
    }

    // Score for ranking player-facing Opportunity Cards. Deliberately not a reuse of
    // This is the authored-situation source's own ranking, separate from the HTN/Utility AI
    // ranking mixed in by GetSituationCardOffers below. Its situational bonus reuses the
    // Situations tab's authored order as a score gradient instead of the old
    // hard gate/first-match-wins order.
    private static float ScoreOpportunityCard(CardData card, Character character, List<CardSituationEnum> activeSituations)
    {
        float score = character.GetCommander() * (card.commanderSkillRequired > 0 ? 0.5f : 0f)
               + character.GetAgent() * (card.agentSkillRequired > 0 ? 0.5f : 0f)
               + character.GetEmmissary() * (card.emissarySkillRequired > 0 ? 0.5f : 0f)
               + character.GetMage() * (card.mageSkillRequired > 0 ? 0.5f : 0f);

        int primaryRank = activeSituations.IndexOf(card.GetSituation());
        int secondaryRank = activeSituations.IndexOf(card.GetSecondarySituation());
        int rank = primaryRank < 0 ? secondaryRank
            : secondaryRank < 0 ? primaryRank
            : Mathf.Min(primaryRank, secondaryRank);
        if (rank >= 0)
        {
            score += Mathf.Max(0f, 10f - rank);
        }

        return score;
    }

    // Returns up to handSize affordable opportunity cards, ranked by ScoreOpportunityCard
    // across the whole eligible pool together (character-at-PC + situation-matching +
    // Spell/Event), rather than the old one-match-per-situation loop — this is what lets
    // multiple cards from the same situation compete on relevance instead of the same single
    // card always winning. IsEligibleOpportunityCard's hard filters (Event cap, Spell mage-rank
    // gate) still apply while filling the result.
    public List<CardData> GetSituationCards(PlayableLeader leader, Character character, Hex hex)
    {
        var result = new List<CardData>();
        if (leader == null || character == null || hex == null) return result;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return result;

        int maxCards = GetHandSize();
        List<CardSituationEnum> activeSituations = SituationEvaluator.GetActiveSituations(character, hex);

        // Character cards are world opportunities at their authored starting PC. PC ownership
        // is deliberately irrelevant: reaching the place is enough, provided the character card
        // belongs to this leader's configured deck and its normal costs can be paid.
        PC currentPc = hex.GetPCData();

        List<CardData> candidates = state.situationPool.Where(c => c != null && (
            (currentPc != null && c.GetCardType() == CardTypeEnum.Character && !string.IsNullOrWhiteSpace(c.startingPC) && CardNameUtility.Equals(c.startingPC, currentPc.pcName))
            || c.MatchesAnySituation(activeSituations)
            || c.GetCardType() == CardTypeEnum.Spell
            || c.GetCardType() == CardTypeEnum.Event
        )).ToList();

        List<CardData> ranked = candidates
            .Where(c => c.EvaluatePlayability(character))
            .OrderByDescending(c => ScoreOpportunityCard(c, character, activeSituations))
            .ToList();

        bool eventIncluded = false;
        foreach (CardData c in ranked)
        {
            if (result.Count >= maxCards) break;
            if (!IsEligibleOpportunityCard(c, character, eventIncluded)) continue;

            result.Add(c);
            if (c.GetCardType() == CardTypeEnum.Event) eventIncluded = true;
        }

        return result;
    }

    // Builds the human-facing opportunity offer from two independent recommendation sources:
    // up to three cards matched by the authored Situations priority, and up to two cards ranked
    // by the same HTN/blackboard Utility AI used for automated leaders. Either source may fill
    // vacancies left by the other, up to the configured hand size.
    public List<SituationCardOffer> GetSituationCardOffers(PlayableLeader leader, Character character, Hex hex)
    {
        List<SituationCardOffer> result = new();
        if (leader == null || character == null || hex == null) return result;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return result;

        int maxCards = GetHandSize();
        if (maxCards <= 0) return result;

        ActionsManager actionsManager = FindFirstObjectByType<ActionsManager>();
        List<CardSituationEnum> activeSituations = SituationEvaluator.GetActiveSituations(character, hex);
        PC currentPc = hex.GetPCData();

        List<CardData> matchedSituationCards = state.situationPool
            .Where(c => c != null && (
                (currentPc != null && c.GetCardType() == CardTypeEnum.Character
                    && !string.IsNullOrWhiteSpace(c.startingPC)
                    && CardNameUtility.Equals(c.startingPC, currentPc.pcName))
                || c.MatchesAnySituation(activeSituations)
                || c.GetCardType() == CardTypeEnum.Spell
                || c.GetCardType() == CardTypeEnum.Event))
            .OrderByDescending(c => ScoreOpportunityCard(c, character, activeSituations))
            .ToList();

        List<CardData> playableSituationCards = matchedSituationCards
            .Where(c => IsFullyPlayableOpportunityCard(c, character, actionsManager))
            .ToList();

        List<CardData> aiCards = new();
        UtilityAIContextCacheManager cacheManager = UtilityAIContextCacheManager.Instance;
        if (actionsManager != null
            && cacheManager != null
            && cacheManager.TryGetCachedCardSuggestions(leader, character, out IReadOnlyList<(CardData card, float score)> cachedSuggestions))
        {
            foreach ((CardData card, float _) in cachedSuggestions)
            {
                if (!IsFullyPlayableOpportunityCard(card, character, actionsManager)) continue;
                aiCards.Add(card);
                // A few extras allow de-duplication against the authored Situation source
                // without evaluating the remainder of the deck at presentation time.
                if (aiCards.Count >= maxCards + 3) break;
            }
        }

        HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
        AddOffers(playableSituationCards, SituationCardOfferSource.Situation, Mathf.Min(3, maxCards), result, selected);
        AddOffers(aiCards, SituationCardOfferSource.AI, Mathf.Min(2, maxCards - result.Count), result, selected);

        // Preserve the intended 3+2 split first, then let either list fill unused capacity.
        AddOffers(playableSituationCards, SituationCardOfferSource.Situation, maxCards - result.Count, result, selected);
        AddOffers(aiCards, SituationCardOfferSource.AI, maxCards - result.Count, result, selected);

        // With no legal play at all, expose the best authored situational match as a visible
        // explanation of the missed opportunity. SituationCardsUI renders it red and disables it.
        if (result.Count == 0 && matchedSituationCards.Count > 0)
        {
            result.Add(new SituationCardOffer(matchedSituationCards[0], SituationCardOfferSource.Situation, false));
        }

        return result;
    }

    private static void AddOffers(
        IEnumerable<CardData> candidates,
        SituationCardOfferSource source,
        int count,
        List<SituationCardOffer> destination,
        HashSet<string> selected)
    {
        if (candidates == null || count <= 0) return;
        foreach (CardData card in candidates)
        {
            if (card == null || !selected.Add(GetOfferCardKey(card))) continue;
            destination.Add(new SituationCardOffer(card, source, true));
            if (--count <= 0) break;
        }
    }

    private static string GetOfferCardKey(CardData card)
    {
        if (card == null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(card.deckId) && card.cardId > 0)
            return $"{card.deckId.Trim()}::{card.cardId}";
        return card.name?.Trim() ?? string.Empty;
    }

    private static bool IsFullyPlayableOpportunityCard(CardData card, Character character, ActionsManager actionsManager)
    {
        if (card == null || character == null) return false;

        string actionRef = card.GetActionRef();
        if (string.IsNullOrWhiteSpace(actionRef)) return card.EvaluatePlayability(character);
        if (actionsManager == null) return false;

        CharacterAction action = actionsManager.ResolveActionByRef(actionRef, card);
        if (action == null) return false;
        action.Initialize(character, card);
        return card.EvaluatePlayability(character, null, _ => action.FulfillsConditions());
    }

    // A Spell opportunity requires an actual mage rank (ArcaneInsight grants one via
    // Character.GetMage()'s status-effect bonus), and at most one Event card may appear at
    // once — events are rarer, higher-impact interrupts than routine situational offers.
    private static bool IsEligibleOpportunityCard(CardData card, Character character, bool eventAlreadyIncluded)
    {
        if (card == null || !card.EvaluatePlayability(character)) return false;
        if (card.GetCardType() == CardTypeEnum.Event && eventAlreadyIncluded) return false;
        if (card.GetCardType() == CardTypeEnum.Spell
            && character.GetMage() <= 0
            && !character.HasStatusEffect(StatusEffectEnum.ArcaneInsight))
            return false;
        return true;
    }

    // Existence-only check used to drive hex hints/'?' markers: true whenever an opportunity
    // card matches this hex's active situations or founding PC, regardless of whether its
    // requirements (level, resources, mage rank, etc.) are currently satisfied — the hint
    // should point at the opportunity even when the player can't act on it yet.
    public bool HasOpportunityCardsAtHex(PlayableLeader leader, Character character, Hex hex)
    {
        if (leader == null || character == null || hex == null) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        PC currentPc = hex.GetPCData();
        if (currentPc != null && state.situationPool.Any(c =>
            c != null
            && c.GetCardType() == CardTypeEnum.Character
            && !string.IsNullOrWhiteSpace(c.startingPC)
            && CardNameUtility.Equals(c.startingPC, currentPc.pcName)))
        {
            return true;
        }

        List<CardSituationEnum> activeSituations = SituationEvaluator.GetActiveSituations(character, hex);
        if (activeSituations.Count == 0) return false;

        return state.situationPool.Any(c => c != null && c.MatchesAnySituation(activeSituations));
    }

    public bool TryPayOpportunityCardCosts(PlayableLeader leader, CardData card)
    {
        if (leader == null || card == null) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;
        if (!state.situationPool.Any(candidate => candidate != null
            && candidate.cardId == card.cardId
            && string.Equals(candidate.deckId, card.deckId, StringComparison.OrdinalIgnoreCase))) return false;
        if (!card.MeetsResourceRequirements(leader)) return false;

        ApplyCardCosts(leader, card);
        return true;
    }

    public IReadOnlyList<CardData> GetDrawPile(PlayableLeader leader)
    {
        if (leader == null) return Array.Empty<CardData>();
        return playerDecks.TryGetValue(leader, out PlayerDeckState state) ? state.drawPile : Array.Empty<CardData>();
    }

    public int GetHandSize()
    {
        return handSize;
    }

    public bool TryPlayCard(PlayableLeader leader, string cardName, out CardData card)
    {
        card = null;
        if (leader == null || string.IsNullOrWhiteSpace(cardName)) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        int index = state.drawPile.FindIndex(x => x != null && CardNameUtility.Equals(x.name, cardName));
        if (index < 0) return false;

        card = state.drawPile[index];
        state.drawPile.RemoveAt(index);
        state.discardPile.Add(card);
        return true;
    }

    public bool TryConsumeCard(Leader leader, string cardName, out CardData consumedCard)
    {
        consumedCard = null;
        if (leader == null || string.IsNullOrWhiteSpace(cardName)) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        int index = state.drawPile.FindIndex(x => x != null && CardNameUtility.Equals(x.name, cardName));
        if (index < 0) return false;

        consumedCard = state.drawPile[index];
        if (consumedCard == null) return false;
        if (!consumedCard.MeetsResourceRequirements(leader))
        {
            consumedCard = null;
            return false;
        }

        state.drawPile.RemoveAt(index);
        state.discardPile.Add(consumedCard);
        ApplyCardCosts(leader, consumedCard);
        return true;
    }

    public bool TryDiscardCard(PlayableLeader leader, string cardName, out CardData discardedCard)
    {
        discardedCard = null;
        if (leader == null || string.IsNullOrWhiteSpace(cardName)) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        int index = state.drawPile.FindIndex(x => x != null && CardNameUtility.Equals(x.name, cardName));
        if (index < 0) return false;

        discardedCard = state.drawPile[index];
        if (discardedCard == null || discardedCard.IsEncounterCard()) return false;

        state.drawPile.RemoveAt(index);
        state.discardPile.Add(discardedCard);
        return true;
    }

    public bool TryAddCardToHand(PlayableLeader leader, CardData card)
    {
        if (leader == null || card == null) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        state.drawPile.Add(CloneCard(card));
        return true;
    }

    public bool HasDeckFor(Leader leader)
    {
        return leader != null && playerDecks.ContainsKey(leader);
    }

    public bool HasActionCardInDeck(Leader leader, string actionClassName)
    {
        if (leader == null) return true;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        bool Matches(CardData card)
        {
            if (card == null) return false;
            if (!IsConsumableEffectCard(card)) return false;
            string cardRef = card.GetActionRef();
            return !string.IsNullOrWhiteSpace(actionClassName) &&
                string.Equals(cardRef, actionClassName, StringComparison.OrdinalIgnoreCase);
        }

        return state.drawPile.Any(Matches)
            || state.discardPile.Any(Matches);
    }

    public bool HasActionCardInHand(Leader leader, string actionClassName, Character selectedCharacter = null, Func<Character, bool> resourceCheck = null, Func<Character, bool> conditionCheck = null)
    {
        if (leader == null) return true;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;
        return FindMatchingActionCardIndex(state.drawPile, actionClassName, selectedCharacter, resourceCheck, conditionCheck) >= 0;
    }

    public bool TryGetActionCardInHand(Leader leader, string actionClassName, out CardData card, Character selectedCharacter = null, Func<Character, bool> resourceCheck = null, Func<Character, bool> conditionCheck = null)
    {
        card = null;
        if (leader == null) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        int index = FindMatchingActionCardIndex(state.drawPile, actionClassName, selectedCharacter, resourceCheck, conditionCheck);
        if (index < 0) return false;
        card = state.drawPile[index];
        return card != null;
    }

    public int GetActionCardDifficulty(Leader leader, string actionClassName, Character selectedCharacter = null)
    {
        if (TryGetActionCardInHand(leader, actionClassName, out CardData card, selectedCharacter))
        {
            return card != null ? Mathf.Max(0, card.difficulty) : 0;
        }
        return 0;
    }

    public bool TryConsumeActionCard(Leader leader, string actionClassName, out CardData consumedCard, string preferredCardName = null)
    {
        consumedCard = null;
        if (leader == null) return true;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        return TryConsumeActionCardFromDeck(leader, state, actionClassName, preferredCardName, out consumedCard);
    }

    public bool TryConsumeActionCardFromFullDeck(Leader leader, string actionClassName, CardData preferredCard, out CardData consumedCard)
    {
        consumedCard = null;
        if (leader == null || !playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;

        return TryConsumeActionCardFromDeck(leader, state, actionClassName, preferredCard != null ? preferredCard.name : null, out consumedCard);
    }

    private bool TryConsumeActionCardFromDeck(Leader playableLeader, PlayerDeckState state, string actionClassName, string preferredCardName, out CardData consumedCard)
    {
        consumedCard = null;
        List<CardData> sourceList = state.drawPile;

        int index = -1;
        if (!string.IsNullOrWhiteSpace(preferredCardName))
        {
            index = sourceList.FindIndex(card =>
                card != null
                && string.Equals(card.name, preferredCardName, StringComparison.OrdinalIgnoreCase)
                && MatchesActionCard(card, actionClassName));
        }
        if (index < 0)
        {
            index = FindMatchingActionCardIndex(sourceList, actionClassName);
        }
        if (index < 0)
        {
            Debug.LogWarning($"[DeckManager] TryConsumeActionCard: no card matching actionRef='{actionClassName}'" +
                (string.IsNullOrWhiteSpace(preferredCardName) ? string.Empty : $" preferredCardName='{preferredCardName}'") +
                $" found in deck of {sourceList.Count} card(s) for '{playableLeader?.characterName}'." +
                " Check the card is actually in that leader's deck, and that IsConsumableEffectCard() covers its CardTypeEnum.");
            return false;
        }

        consumedCard = sourceList[index];
        if (consumedCard == null) return false;
        if (consumedCard.GetCardType() == CardTypeEnum.Land && playableLeader.HasPlayedLandThisTurn())
        {
            Debug.LogWarning($"[DeckManager] TryConsumeActionCard: '{consumedCard.name}' rejected — a Land card was already played this turn.");
            consumedCard = null;
            return false;
        }
        if (!consumedCard.MeetsResourceRequirements(playableLeader))
        {
            Debug.LogWarning($"[DeckManager] TryConsumeActionCard: '{consumedCard.name}' rejected — MeetsResourceRequirements=false for '{playableLeader?.characterName}'.");
            consumedCard = null;
            return false;
        }

        sourceList.RemoveAt(index);
        state.discardPile.Add(consumedCard);
        ApplyCardCosts(playableLeader, consumedCard);
        return true;
    }

    public void ApplyMapRevealForPlayedCard(Leader leader, CardData card)
    {
        if (leader == null || card == null) return;

        Game game = FindFirstObjectByType<Game>();
        if (game == null || game.player != leader || !game.IsPlayerCurrentlyPlaying()) return;

        Board board = game.board != null ? game.board : Board.Instance;
        if (board == null || board.hexes == null || board.hexes.Count == 0) return;
        board.nationSpawner?.EnsureLandRegionsAssigned();

        List<Hex> revealedPcHexes = null;
        string revealMessage = null;
        string discoveryRegion = null;
        switch (card.GetCardType())
        {
            case CardTypeEnum.Land:
                revealedPcHexes = RevealRegion(board, card.name, leader, maxHexes: 1);
                revealMessage = $"The lands of {FormatDisplayName(card.name)} were revealed";
                discoveryRegion = card.name;
                break;
            case CardTypeEnum.PC:
                string pcRegion = !string.IsNullOrWhiteSpace(card.region) ? card.region : card.name;
                revealedPcHexes = RevealRegion(board, pcRegion, leader);
                revealMessage = $"The lands of {FormatDisplayName(pcRegion)} were revealed";
                discoveryRegion = pcRegion;
                break;
        }

        MinimapManager.RefreshMinimap();
        if (!string.IsNullOrWhiteSpace(discoveryRegion) && (revealedPcHexes == null || revealedPcHexes.Count == 0))
        {
            Debug.LogWarning($"DeckManager: No hexes matched reveal region '{discoveryRegion}'.");
        }

        if (leader is PlayableLeader pl && revealedPcHexes != null && revealedPcHexes.Count > 0
            && !string.IsNullOrWhiteSpace(discoveryRegion) && pl.TryDiscoverRegion(discoveryRegion))
        {
            NotifyRegionDiscovered(discoveryRegion, ChooseFocusHex(revealedPcHexes));
        }

        QueueRevealMessages(revealedPcHexes, revealMessage);
    }

    public bool TryReturnActionCardToHand(Leader leader, string actionClassName)
    {
        if (leader == null) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;
        if (state.discardPile == null || state.discardPile.Count == 0) return false;

        int discardIndex = -1;
        for (int i = state.discardPile.Count - 1; i >= 0; i--)
        {
            CardData card = state.discardPile[i];
            if (!MatchesActionCard(card, actionClassName)) continue;
            discardIndex = i;
            break;
        }

        if (discardIndex < 0) return false;

        CardData returnedCard = state.discardPile[discardIndex];
        state.discardPile.RemoveAt(discardIndex);
        state.drawPile.Add(returnedCard);
        return true;
    }

    public bool TryReturnCardToHand(Leader leader, string cardName)
    {
        if (leader == null) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;
        if (state.discardPile == null || state.discardPile.Count == 0) return false;

        int discardIndex = state.discardPile.FindLastIndex(card => card != null && string.Equals(card.name, cardName, StringComparison.OrdinalIgnoreCase));
        if (discardIndex < 0) return false;

        CardData returnedCard = state.discardPile[discardIndex];
        state.discardPile.RemoveAt(discardIndex);
        state.drawPile.Add(returnedCard);
        return true;
    }

    // Recycles the discard pile back into the draw pile once it's exhausted, so cards already
    // played eventually become available again instead of the deck shrinking to nothing over a
    // long game. Called once per leader turn (see Leader.RefreshForNewTurn).
    public bool RecycleDiscardPileIfExhausted(PlayableLeader leader)
    {
        if (leader == null) return false;
        if (!playerDecks.TryGetValue(leader, out PlayerDeckState state)) return false;
        if (state.drawPile.Count > 0 || state.discardPile.Count == 0) return false;

        state.drawPile.AddRange(state.discardPile);
        state.discardPile.Clear();
        ApplyBalancedDrawOrdering(state.drawPile);
        return true;
    }

    public static void NotifyEncounterPlaced(Hex targetHex)
    {
        const string text = "An encounter can be investigated";
        // A tray icon rather than MessageDisplayNoUI's hex-anchored floating text: that system
        // marks MessageDisplayNoUI.IsDisplaying while a message is up, which
        // BoardNavigator.IsNavigationInputLocked() treats as a reason to block ALL board input
        // (zoom, character hover, etc.) board-wide until it finishes — fine for a single rare
        // toast, but encounters can appear several at a time as fog clears, which froze input
        // for as long as the queue took to drain. The tray icon doesn't touch that lock.
        EventIconsManager manager = EventIconsManager.FindManager();
        if (manager != null)
        {
            manager.AddEventIcon(
                EventIconType.Encounter,
                discardable: true,
                // Queued (EnqueueFocus) rather than a direct LookAt() call: LookAt() stops and
                // overwrites BoardNavigator's shared lookAtCoroutine unconditionally, which can
                // clobber an in-flight queued pan from an unrelated system (e.g. MessageDisplayNoUI's
                // RequestFocusForMessage, which awaits that same coroutine finishing to release its
                // focus hold) — going through the queue avoids stepping on other consumers.
                onOpen: () => BoardNavigator.Instance?.EnqueueFocus(targetHex, 1f, 0f, true));
        }
        LogManager.Log(LogCategory.Event, Game.Instance?.currentlyPlaying?.characterName, null, text);
    }


    public CardData FindCardByNameForLeader(PlayableLeader leader, string cardName)
    {
        if (leader == null || string.IsNullOrWhiteSpace(cardName)) return null;
        if (!loaded && !InitializeFromResources()) return null;

        string deckId = ResolveDeckIdForLeader(leader);
        foreach (DeckData deckData in GetDeckChain(deckId))
        {
            if (deckData?.cards == null) continue;
            CardData inLeaderDeck = deckData.cards.FirstOrDefault(card =>
                card != null
                && string.Equals(card.name, cardName, StringComparison.OrdinalIgnoreCase));
            if (inLeaderDeck != null) return inLeaderDeck;
        }

        return cards.FirstOrDefault(card =>
            card != null
            && string.Equals(card.name, cardName, StringComparison.OrdinalIgnoreCase));
    }

    // Any-deck, any-type lookup by display name — used by the campaign-selection screen to
    // resolve a scenario's representative card.
    public CardData FindAnyCardByName(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return null;
        if (!loaded && !InitializeFromResources()) return null;
        return cards.FirstOrDefault(card =>
            card != null && string.Equals(card.name, cardName, StringComparison.OrdinalIgnoreCase));
    }

    public CardData FindArmyCardByName(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return null;
        if (!loaded && !InitializeFromResources()) return null;
        return cards.FirstOrDefault(card =>
            card != null
            && card.GetCardType() == CardTypeEnum.Army
            && string.Equals(card.name, cardName, StringComparison.OrdinalIgnoreCase));
    }

    public CardData FindObjectCardByName(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return null;
        if (!loaded && !InitializeFromResources()) return null;
        return cards.FirstOrDefault(card =>
            card != null
            && card.GetCardType() == CardTypeEnum.Object
            && string.Equals(card.name, cardName, StringComparison.OrdinalIgnoreCase));
    }

    // Fresh clones of every Object card in the catalog — the map's random hidden-object pool
    // (Board's placement pass) draws from this instead of the retired ArtifactRepository.
    // Repeated `copies` times per card so a common item (e.g. Athelas) can seed multiple
    // findable instances instead of always being a unique one-of-a-kind pickup.
    public List<CardData> GetAllObjectCardClones()
    {
        if (!loaded && !InitializeFromResources()) return new List<CardData>();
        return cards
            .Where(card => card != null && card.GetCardType() == CardTypeEnum.Object)
            .SelectMany(card => Enumerable.Repeat(card, Math.Max(1, card.copies)))
            .Select(CloneCard)
            .Where(card => card != null)
            .ToList();
    }

    // Fresh clones of every Encounter card in the catalog — Board's world-scatter placement
    // pass (SpawnEncounters) draws from this, mirroring GetAllObjectCardClones above.
    // Encounters are world content, not part of any leader's own drawable pool (see
    // ShouldIncludeCardInDeck), so they're sourced directly from the master catalog rather
    // than through any leader's deck.
    public List<CardData> GetAllEncounterCardClones()
    {
        if (!loaded && !InitializeFromResources()) return new List<CardData>();
        return cards
            .Where(card => card != null && card.GetCardType() == CardTypeEnum.Encounter)
            .SelectMany(card => Enumerable.Repeat(card, Math.Max(1, card.copies)))
            .Select(CloneCard)
            .Where(card => card != null)
            .ToList();
    }

    // Cheap count (no cloning) — AI scarcity scoring divides by this instead of the retired
    // ArtifactRepository.Count.
    public int GetObjectCardCount()
    {
        if (!loaded && !InitializeFromResources()) return 0;
        return cards.Count(card => card != null && card.GetCardType() == CardTypeEnum.Object);
    }

    public static CardData CloneCard(CardData card)
    {
        if (card == null) return null;
        return new CardData
        {
            cardId = card.cardId,
            name = card.name,
            quote = card.quote,
            actionEffect = card.actionEffect,
            description = card.description,
            type = card.type,
            tags = card.tags != null ? new List<string>(card.tags) : new List<string>(),
            deckId = card.deckId,
            alignment = card.alignment,
            actionClassName = card.actionClassName,
            action = card.action,
            commanderSkillRequired = card.commanderSkillRequired,
            agentSkillRequired = card.agentSkillRequired,
            emissarySkillRequired = card.emissarySkillRequired,
            mageSkillRequired = card.mageSkillRequired,
            commander = card.commander,
            agent = card.agent,
            emmissary = card.emmissary,
            mage = card.mage,
            race = card.race,
            artifacts = card.artifacts != null ? new List<string>(card.artifacts) : new List<string>(),
            troopType = card.troopType,
            specialAbilities = card.specialAbilities != null ? new List<ArmySpecialAbilityEnum>(card.specialAbilities) : new List<ArmySpecialAbilityEnum>(),
            spriteName = card.spriteName,
            region = card.region,
            requirementsText = card.requirementsText,
            historyText = card.historyText,
            statusEffect = card.statusEffect,
            procChance = card.procChance,
            portraitName = card.portraitName,
            characterGroup = card.characterGroup,
            referenceDeckId = card.referenceDeckId,
            referenceCardId = card.referenceCardId,
            encounterOptions = card.encounterOptions != null ? CloneEncounterOptions(card.encounterOptions) : new List<EncounterOptionData>(),
            fleeOption = CloneEncounterOption(card.fleeOption),
            difficulty = card.difficulty,
            leatherRequired = card.leatherRequired,
            mountsRequired = card.mountsRequired,
            timberRequired = card.timberRequired,
            ironRequired = card.ironRequired,
            steelRequired = card.steelRequired,
            mithrilRequired = card.mithrilRequired,
            goldRequired = card.goldRequired,
            jokerRequired = card.jokerRequired,
            leatherGranted = card.leatherGranted,
            mountsGranted = card.mountsGranted,
            timberGranted = card.timberGranted,
            ironGranted = card.ironGranted,
            steelGranted = card.steelGranted,
            mithrilGranted = card.mithrilGranted,
            goldGranted = card.goldGranted,
            startingPC = card.startingPC,
            inspireEffectData = card.inspireEffectData,
            deckSpriteName = card.deckSpriteName,
            situation = card.situation,
            situation2 = card.situation2,
            isUnderground = card.isUnderground,
            hidden = card.hidden,
            copies = card.copies,
            commanderBonus = card.commanderBonus,
            agentBonus = card.agentBonus,
            emmissaryBonus = card.emmissaryBonus,
            mageBonus = card.mageBonus,
            passiveEffectId = card.passiveEffectId,
            passiveEffectValue = card.passiveEffectValue,
            transferable = card.transferable,
            healPerTurn = card.healPerTurn,
            movementBonus = card.movementBonus,
            ignoreTerrainMovementPenalty = card.ignoreTerrainMovementPenalty,
            grantsHasteAtSea = card.grantsHasteAtSea,
            autoScoutRadius = card.autoScoutRadius,
            detectionEvasion = card.detectionEvasion,
            combatEffects = CloneCombatEffects(card.combatEffects),
            recruitBonusMenAtArms = card.recruitBonusMenAtArms,
            scryAreaBonus = card.scryAreaBonus,
            scryObjectBonus = card.scryObjectBonus,
            negativeStatusImmunity = card.negativeStatusImmunity,
            negativeStatusDurationReduction = card.negativeStatusDurationReduction,
            negativeStatusDamageReduction = card.negativeStatusDamageReduction,
            positiveStatusDurationBonus = card.positiveStatusDurationBonus,
            positiveStatusEffectBonus = card.positiveStatusEffectBonus,
            grantsEnvironmentalImmunity = card.grantsEnvironmentalImmunity
        };
    }

    private void InjectMissingStartingPcAndLandReferences()
    {
        // Build lookups from all non-reference cards
        Dictionary<string, CardData> pcByName = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CardData> landByRegion = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CardData> allCardsByKey = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckData deck in loadedDecksById.Values)
        {
            if (deck?.cards == null) continue;
            foreach (CardData card in deck.cards)
            {
                if (card == null) continue;
                allCardsByKey[BuildCardReferenceKey(deck.deckId, card.cardId)] = card;
                if (IsReferenceCard(card)) continue;
                if (card.GetCardType() == CardTypeEnum.PC && !string.IsNullOrWhiteSpace(card.name))
                {
                    string key = NormalizeCardName(card.name);
                    if (!pcByName.ContainsKey(key)) pcByName[key] = card;
                }
                else if (card.GetCardType() == CardTypeEnum.Land && !string.IsNullOrWhiteSpace(card.region))
                {
                    string key = NormalizeCardName(card.region);
                    if (!landByRegion.ContainsKey(key)) landByRegion[key] = card;
                }
            }
        }

        foreach (DeckManifestEntry entry in deckManifestById.Values)
        {
            if (string.IsNullOrWhiteSpace(entry.parentDeckId)) continue;
            if (!loadedDecksById.TryGetValue(entry.deckId, out DeckData subdeck) || subdeck?.cards == null) continue;

            HashSet<string> existingPcNames = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> existingLandRegions = new(StringComparer.OrdinalIgnoreCase);

            foreach (CardData card in subdeck.cards)
            {
                if (card == null) continue;
                CardData resolved = IsReferenceCard(card)
                    ? FindCardByKey(allCardsByKey, card.referenceDeckId, card.referenceCardId)
                    : card;
                if (resolved == null) continue;
                if (resolved.GetCardType() == CardTypeEnum.PC && !string.IsNullOrWhiteSpace(resolved.name))
                    existingPcNames.Add(NormalizeCardName(resolved.name));
                else if (resolved.GetCardType() == CardTypeEnum.Land && !string.IsNullOrWhiteSpace(resolved.region))
                    existingLandRegions.Add(NormalizeCardName(resolved.region));
            }

            int n = subdeck.cards.Count;
            for (int i = 0; i < n; i++)
            {
                CardData card = subdeck.cards[i];
                if (card == null) continue;

                CardData effective = IsReferenceCard(card)
                    ? FindCardByKey(allCardsByKey, card.referenceDeckId, card.referenceCardId)
                    : card;
                if (effective == null) continue;
                if (effective.GetCardType() != CardTypeEnum.Character) continue;
                if (string.IsNullOrWhiteSpace(effective.startingPC)) continue;

                string pcKey = NormalizeCardName(effective.startingPC);
                if (existingPcNames.Contains(pcKey)) continue;

                if (!pcByName.TryGetValue(pcKey, out CardData originalPc))
                {
                    Debug.LogWarning($"DeckManager: Character '{card.name}' in '{subdeck.deckId}' has startingPC '{card.startingPC}' but no matching PC card found.");
                    continue;
                }

                InjectReferenceCard(subdeck, originalPc);
                existingPcNames.Add(pcKey);

                if (string.IsNullOrWhiteSpace(originalPc.region)) continue;
                string regionKey = NormalizeCardName(originalPc.region);
                if (existingLandRegions.Contains(regionKey)) continue;
                if (!landByRegion.TryGetValue(regionKey, out CardData originalLand)) continue;

                InjectReferenceCard(subdeck, originalLand);
                existingLandRegions.Add(regionKey);
            }
        }
    }

    private static CardData FindCardByKey(Dictionary<string, CardData> index, string deckId, int cardId)
    {
        if (string.IsNullOrWhiteSpace(deckId) || cardId <= 0) return null;
        return index.TryGetValue(BuildCardReferenceKey(deckId, cardId), out CardData card) ? card : null;
    }

    private static void InjectReferenceCard(DeckData subdeck, CardData original)
    {
        if (subdeck?.cards == null || original == null) return;
        int maxId = subdeck.cards.Where(c => c != null).Select(c => c.cardId).DefaultIfEmpty(0).Max();
        subdeck.cards.Add(new CardData
        {
            cardId = maxId + 1,
            referenceDeckId = original.deckId,
            referenceCardId = original.cardId
        });
        // Debug.Log($"DeckManager: Injected reference to '{original.name}' ({original.type}) from '{original.deckId}' into '{subdeck.deckId}'.");
    }

    private void ResolveCardReferences()
    {
        Dictionary<string, CardData> cardIndex = BuildCardIndex();
        Dictionary<string, CardData> resolvedTemplates = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckData deckData in loadedDecksById.Values)
        {
            if (deckData?.cards == null) continue;

            for (int i = 0; i < deckData.cards.Count; i++)
            {
                CardData card = deckData.cards[i];
                if (!IsReferenceCard(card)) continue;

                CardData template = ResolveReferencedTemplate(card.referenceDeckId, card.referenceCardId, cardIndex, resolvedTemplates, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (template == null)
                {
                    Debug.LogWarning($"DeckManager: Could not resolve reference for card '{card?.name}' in deck '{deckData.deckId}' -> {card.referenceDeckId}:{card.referenceCardId}.");
                    continue;
                }

                CardData resolvedCard = CloneCard(template);
                resolvedCard.cardId = card.cardId;
                resolvedCard.deckId = deckData.deckId;
                resolvedCard.alignment = deckData.alignment;
                resolvedCard.referenceDeckId = card.referenceDeckId;
                resolvedCard.referenceCardId = card.referenceCardId;
                deckData.cards[i] = resolvedCard;
            }
        }
    }

    private Dictionary<string, CardData> BuildCardIndex()
    {
        Dictionary<string, CardData> cardIndex = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckData deckData in loadedDecksById.Values)
        {
            if (deckData?.cards == null || string.IsNullOrWhiteSpace(deckData.deckId)) continue;

            foreach (CardData card in deckData.cards)
            {
                if (card == null) continue;
                cardIndex[BuildCardReferenceKey(deckData.deckId, card.cardId)] = card;
            }
        }

        return cardIndex;
    }

    private static CardData ResolveReferencedTemplate(
        string referenceDeckId,
        int referenceCardId,
        Dictionary<string, CardData> cardIndex,
        Dictionary<string, CardData> resolvedTemplates,
        HashSet<string> resolving)
    {
        if (string.IsNullOrWhiteSpace(referenceDeckId) || referenceCardId <= 0) return null;

        string referenceKey = BuildCardReferenceKey(referenceDeckId, referenceCardId);
        if (resolvedTemplates.TryGetValue(referenceKey, out CardData cachedTemplate))
        {
            return cachedTemplate;
        }

        if (!cardIndex.TryGetValue(referenceKey, out CardData sourceCard) || sourceCard == null)
        {
            return null;
        }

        if (!IsReferenceCard(sourceCard))
        {
            CardData directTemplate = CloneCard(sourceCard);
            resolvedTemplates[referenceKey] = directTemplate;
            return directTemplate;
        }

        if (!resolving.Add(referenceKey))
        {
            return null;
        }

        CardData nestedTemplate = ResolveReferencedTemplate(sourceCard.referenceDeckId, sourceCard.referenceCardId, cardIndex, resolvedTemplates, resolving);
        resolving.Remove(referenceKey);

        if (nestedTemplate == null) return null;

        CardData resolvedTemplate = CloneCard(nestedTemplate);
        resolvedTemplates[referenceKey] = resolvedTemplate;
        return resolvedTemplate;
    }

    private static bool IsReferenceCard(CardData card)
    {
        return card != null && !string.IsNullOrWhiteSpace(card.referenceDeckId) && card.referenceCardId > 0;
    }

    private static string BuildCardReferenceKey(string deckId, int cardId)
    {
        return $"{deckId?.Trim().ToLowerInvariant()}::{cardId}";
    }

    private static int FindMatchingActionCardIndex(List<CardData> cardsList, string actionClassName, Character selectedCharacter = null, Func<Character, bool> resourceCheck = null, Func<Character, bool> conditionCheck = null)
    {
        if (cardsList == null || cardsList.Count == 0) return -1;
        for (int i = 0; i < cardsList.Count; i++)
        {
            CardData card = cardsList[i];
            if (card == null) continue;
            if (!IsConsumableEffectCard(card)) continue;

            bool matches = false;
            if (!string.IsNullOrWhiteSpace(actionClassName) &&
                string.Equals(card.GetActionRef(), actionClassName, StringComparison.OrdinalIgnoreCase))
            {
                matches = true;
            }

            if (!matches) continue;

            if (selectedCharacter == null || card.EvaluatePlayability(selectedCharacter, resourceCheck, conditionCheck))
            {
                return i;
            }
        }
        return -1;
    }

    private static List<ObjectCombatEffect> CloneCombatEffects(List<ObjectCombatEffect> effects)
    {
        if (effects == null) return new List<ObjectCombatEffect>();
        return effects.Where(e => e != null).Select(e => new ObjectCombatEffect
        {
            type = e.type,
            targetRace = e.targetRace,
            targetTroopType = e.targetTroopType,
            magnitude = e.magnitude
        }).ToList();
    }

    private static List<EncounterOptionData> CloneEncounterOptions(List<EncounterOptionData> options)
    {
        if (options == null) return new List<EncounterOptionData>();
        return options.Select(CloneEncounterOption).Where(option => option != null).ToList();
    }

    private static EncounterOptionData CloneEncounterOption(EncounterOptionData option)
    {
        if (option == null) return null;
        return new EncounterOptionData
        {
            optionId = option.optionId,
            label = option.label,
            description = option.description,
            outcomes = option.outcomes != null
                ? option.outcomes.Select(CloneEncounterOutcome).Where(outcome => outcome != null).ToList()
                : new List<EncounterOutcomeData>()
        };
    }

    private static EncounterOutcomeData CloneEncounterOutcome(EncounterOutcomeData outcome)
    {
        if (outcome == null) return null;
        return new EncounterOutcomeData
        {
            outcomeId = outcome.outcomeId,
            resultText = outcome.resultText,
            requiredAlignment = outcome.requiredAlignment,
            minCommander = outcome.minCommander,
            minAgent = outcome.minAgent,
            minEmmissary = outcome.minEmmissary,
            minMage = outcome.minMage,
            minHealth = outcome.minHealth,
            maxHealth = outcome.maxHealth,
            healthDelta = outcome.healthDelta,
            goldDelta = outcome.goldDelta,
            leatherDelta = outcome.leatherDelta,
            timberDelta = outcome.timberDelta,
            mountsDelta = outcome.mountsDelta,
            ironDelta = outcome.ironDelta,
            steelDelta = outcome.steelDelta,
            mithrilDelta = outcome.mithrilDelta,
            statuses = outcome.statuses != null
                ? outcome.statuses.Select(status => new EncounterStatusEffectData
                {
                    statusId = status.statusId,
                    turns = status.turns
                }).ToList()
                : new List<EncounterStatusEffectData>()
        };
    }

    private static bool MatchesActionCard(CardData card, string actionClassName)
    {
        if (!IsConsumableEffectCard(card)) return false;

        return !string.IsNullOrWhiteSpace(actionClassName)
            && string.Equals(card.GetActionRef(), actionClassName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConsumableEffectCard(CardData card)
    {
        if (card == null) return false;

        CardTypeEnum cardType = card.GetCardType();
        bool supportedType = cardType == CardTypeEnum.Action
            || cardType == CardTypeEnum.Event
            || cardType == CardTypeEnum.Encounter
            || cardType == CardTypeEnum.Land
            || cardType == CardTypeEnum.PC
            || cardType == CardTypeEnum.Environmental
            || cardType == CardTypeEnum.Spell
            || cardType == CardTypeEnum.Army
            || cardType == CardTypeEnum.Object;
        if (!supportedType) return false;

        return !string.IsNullOrWhiteSpace(card.GetActionRef());
    }

    private List<Hex> RevealRegion(Board board, string region, Leader owner, int maxHexes = -1)
    {
        List<Hex> candidates = new();
        if (board == null || string.IsNullOrWhiteSpace(region)) return candidates;

        string normalizedRegion = NormalizeCardName(region);
        foreach (Hex hex in board.hexes.Values)
        {
            if (hex == null) continue;

            string hexRegion = hex.GetLandRegion();
            if (string.IsNullOrWhiteSpace(hexRegion))
            {
                PC pc = hex.GetPCData();
                if (pc != null) hexRegion = ResolveRegionForPc(pc);
            }

            if (string.IsNullOrWhiteSpace(hexRegion)) continue;
            if (!string.Equals(NormalizeCardName(hexRegion), normalizedRegion, StringComparison.Ordinal)) continue;

            candidates.Add(hex);
        }

        if (maxHexes > 0 && candidates.Count > maxHexes)
        {
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }
            candidates = candidates.Take(maxHexes).ToList();
        }

        foreach (Hex hex in candidates) hex.RevealMapOnlyArea(1, false, false);
        return candidates;
    }

    private List<Hex> RevealPcOnMapOnly(Board board, string pcName)
    {
        List<Hex> revealedHexes = new();
        if (board == null || string.IsNullOrWhiteSpace(pcName)) return revealedHexes;

        string normalizedPcName = NormalizeCardName(pcName);
        foreach (Hex hex in board.hexes.Values)
        {
            PC pc = hex?.GetPCData();
            if (pc == null) continue;
            if (!string.Equals(NormalizeCardName(pc.pcName), normalizedPcName, StringComparison.Ordinal)) continue;

            hex.RevealMapOnlyArea(1, false, false);
            revealedHexes.Add(hex);
            return revealedHexes;
        }

        return revealedHexes;
    }

    public string ResolveRegionForPc(PC pc)
    {
        if (pc == null) return null;

        LeaderBiomeConfig ownerBiome = pc.owner != null ? pc.owner.GetBiome() : null;
        if (ownerBiome != null
            && !string.IsNullOrWhiteSpace(ownerBiome.startingCityName)
            && string.Equals(NormalizeCardName(ownerBiome.startingCityName), NormalizeCardName(pc.pcName), StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(ownerBiome.noScenarioStart.startingCityRegion))
        {
            return ownerBiome.noScenarioStart.startingCityRegion;
        }

        string normalizedPcName = NormalizeCardName(pc.pcName);
        CardData pcCard = cards.FirstOrDefault(card =>
            card != null
            && card.GetCardType() == CardTypeEnum.PC
            && !string.IsNullOrWhiteSpace(card.region)
            && string.Equals(NormalizeCardName(card.name), normalizedPcName, StringComparison.Ordinal));

        return pcCard?.region;
    }

    // Forward lookup (PC name -> its CardData), the inverse of ResolveRegionForPc above.
    // Used by the auto-grant triggers (Board.TriggerOwnPcGrantIfStandingOnOne) to resolve
    // which card's *Granted fields to reapply for an already-founded PC.
    public CardData FindPcCardByPcName(string pcName)
    {
        if (string.IsNullOrWhiteSpace(pcName)) return null;
        string normalizedPcName = NormalizeCardName(pcName);
        return cards.FirstOrDefault(card =>
            card != null
            && card.GetCardType() == CardTypeEnum.PC
            && string.Equals(NormalizeCardName(card.name), normalizedPcName, StringComparison.Ordinal));
    }

    // Region lookup for Land-type cards. Land cards carry region: "" in JSON — their region
    // identity is their *name* instead (confirmed via NationSpawner.cs's land-region
    // assignment pass, which builds the board's region list from Land card names, not the
    // blank region field). hex.GetLandRegion() returns exactly one of those names.
    public CardData FindLandCardByRegion(string region)
    {
        if (string.IsNullOrWhiteSpace(region)) return null;
        string normalizedRegion = NormalizeCardName(region);
        return cards.FirstOrDefault(card =>
            card != null
            && card.GetCardType() == CardTypeEnum.Land
            && string.Equals(NormalizeCardName(card.name), normalizedRegion, StringComparison.Ordinal));
    }

    // Founding-opportunity mechanic (Leader.TryOfferPcFoundingOpportunity): PC-type cards
    // whose region matches the given hex's region and whose PC has not yet been founded by
    // anyone. This grants a new capability (the right to found a PC), so — unlike
    // FindPcCardByPcName/FindLandCardByRegion, which resolve an ALREADY-EXISTING world
    // entity's card data and are fine reading the whole global catalog — it must be scoped
    // to cards actually reachable from this leader's own deck tree (same pool
    // BuildDeckStateForLeader/situationPool draw from), so a leader is never offered a PC
    // that belongs only to another faction's deck.
    public List<CardData> GetUnfoundedOwnRegionPcCards(PlayableLeader leader, Hex hex)
    {
        var result = new List<CardData>();
        string region = hex?.GetLandRegion();
        if (string.IsNullOrWhiteSpace(region) || leader == null) return result;

        string normalizedRegion = NormalizeCardName(region);
        ActionsManager actionsManager = FindFirstObjectByType<ActionsManager>();

        foreach (CardData card in GetLeaderCardPool(leader))
        {
            if (card == null || card.GetCardType() != CardTypeEnum.PC) continue;
            if (string.IsNullOrWhiteSpace(card.region)) continue;
            if (!string.Equals(NormalizeCardName(card.region), normalizedRegion, StringComparison.Ordinal)) continue;

            if (actionsManager?.ResolveActionByRef(card.GetActionRef(), card) is not PCAction pcAction) continue;
            pcAction.Initialize(null, card);
            if (pcAction.IsAlreadyFounded()) continue;

            result.Add(card);
        }

        return result;
    }

    // All cards reachable from a leader's own deck setup (their deck's parent chain plus
    // shared-to-all decks) — mirrors PopulateSituationPool's source enumeration exactly, just
    // without the situation-only filter, so callers can search the leader's full card
    // universe for a specific card TYPE instead of only situation-tagged ones.
    private IEnumerable<CardData> GetLeaderCardPool(PlayableLeader leader)
    {
        string deckId = ResolveDeckIdForLeader(leader);
        if (string.IsNullOrWhiteSpace(deckId)) yield break;

        foreach (DeckData deck in GetDeckTree(deckId).Concat(GetSharedDecks()))
        {
            if (deck?.cards == null) continue;
            foreach (CardData card in deck.cards)
            {
                if (card != null) yield return card;
            }
        }
    }

    private static string NormalizeCardName(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return string.Empty;
        return new string(cardName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    public static void NotifyRegionDiscovered(string regionName, Hex anchorHex)
    {
        if (string.IsNullOrWhiteSpace(regionName)) return;

        FindFirstObjectByType<RegionLabelManager>()?.ShowLabel(regionName.Trim());

        string displayName = FormatDisplayName(regionName);
        string text = $"{displayName} discovered!";
        MessageDisplay.ShowMessage(text, Color.cyan, forceImmediate: true, logToWidget: false);
        LogManager.Log(LogCategory.Event, Game.Instance?.currentlyPlaying?.characterName, null, text);
    }

    private static void QueueRevealMessages(List<Hex> revealedPcHexes, string message)
    {
        if (revealedPcHexes == null || revealedPcHexes.Count == 0) return;

        BoardNavigator navigator = BoardNavigator.Instance != null ? BoardNavigator.Instance : FindFirstObjectByType<BoardNavigator>();
        Hex anchorHex = ChooseFocusHex(revealedPcHexes);
        if (anchorHex == null) return;

        string revealText = string.IsNullOrWhiteSpace(message) ? "The lands were revealed" : message;
        Action showRevealMessage = () => MessageDisplay.ShowMessage(revealText, Color.yellow, forceImmediate: true, logToWidget: false);

        if (navigator != null)
        {
            navigator.EnqueueFocus(anchorHex, 0.5f, 0.18f, true, showRevealMessage);
        }
        else
        {
            showRevealMessage();
        }

        LogManager.Log(LogCategory.Event, Game.Instance?.currentlyPlaying?.characterName, null, revealText);
    }

    private static string FormatDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string spaced = Regex.Replace(value.Trim(), @"(?<!^)([A-Z])", " $1");
        return Regex.Replace(spaced, @"\s+", " ").Trim();
    }

    private static Hex ChooseFocusHex(List<Hex> hexes)
    {
        if (hexes == null || hexes.Count == 0) return null;

        List<Hex> validHexes = hexes.Where(hex => hex != null).ToList();
        if (validHexes.Count == 0) return null;
        if (validHexes.Count == 1) return validHexes[0];

        float averageX = (float)validHexes.Average(hex => hex.v2.x);
        float averageY = (float)validHexes.Average(hex => hex.v2.y);

        Hex bestHex = validHexes[0];
        float bestDistance = float.MaxValue;
        for (int i = 0; i < validHexes.Count; i++)
        {
            Hex hex = validHexes[i];
            float dx = hex.v2.x - averageX;
            float dy = hex.v2.y - averageY;
            float distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestHex = hex;
            }
        }

        return bestHex;
    }

    private static void ApplyCardCosts(Leader owner, CardData card)
    {
        if (owner == null || card == null) return;

        owner.recentlyPlayedCards.Add(card);
        if (owner.recentlyPlayedCards.Count > 2) owner.recentlyPlayedCards.RemoveAt(0);

        if (card.leatherRequired > 0) owner.RemoveLeather(card.leatherRequired, false);
        if (card.timberRequired > 0) owner.RemoveTimber(card.timberRequired, false);
        if (card.mountsRequired > 0) owner.RemoveMounts(card.mountsRequired, false);
        if (card.ironRequired > 0) owner.RemoveIron(card.ironRequired, false);
        if (card.steelRequired > 0) owner.RemoveSteel(card.steelRequired, false);
        if (card.mithrilRequired > 0) owner.RemoveMithril(card.mithrilRequired, false);
        int totalGoldCost = card.GetTotalGoldCost();
        if (totalGoldCost > 0) owner.RemoveGold(totalGoldCost, false);
    }

    private PlayerDeckState BuildDeckStateForLeader(Leader leader)
    {
        string deckId = leader switch
        {
            PlayableLeader pl => ResolveDeckIdForLeader(pl),
            NonPlayableLeader npl => ResolveDeckIdForNonPlayableLeader(npl),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(deckId)) return null;

        bool isVariantSelection = leader is PlayableLeader variantLeader && !string.IsNullOrWhiteSpace(variantLeader.GetSelectedVariantName());
        PlayerDeckState state = new PlayerDeckState
        {
            deckId = deckId
        };

        List<CardData> basePool = new();
        List<CardData> subdeckPool = new();

        if (isVariantSelection)
        {
            List<DeckData> ownedChain = GetDeckChain(deckId).ToList();
            if (ownedChain.Count == 0) return null;

            DeckData leafDeck = ownedChain[ownedChain.Count - 1];
            List<DeckData> ancestorDecks = ownedChain.Take(Mathf.Max(0, ownedChain.Count - 1)).ToList();

            foreach (DeckData ownedDeck in ancestorDecks)
            {
                if (ownedDeck?.cards == null) continue;
                basePool.AddRange(
                    ownedDeck.cards
                        .Where(card => ShouldIncludeCardInDeck(state.deckId, ownedDeck.deckId, card))
                        .Select(CloneCard)
                        .Where(card => card != null));
            }

            if (leafDeck?.cards != null)
            {
                subdeckPool.AddRange(
                    leafDeck.cards
                        .Where(card => ShouldIncludeCardInDeck(state.deckId, leafDeck.deckId, card))
                        .Select(CloneCard)
                        .Where(card => card != null));
            }

            AddMergedPool(state.drawPile, basePool, subdeckPool);
        }
        else
        {
            List<DeckData> expandedDecks = GetDeckTree(deckId).ToList();
            if (expandedDecks.Count == 0) return null;

            DeckData baseDeck = expandedDecks[0];
            List<DeckData> descendantDecks = expandedDecks.Skip(1).ToList();

            if (baseDeck?.cards != null)
            {
                basePool.AddRange(
                    baseDeck.cards
                        .Where(card => ShouldIncludeCardInDeck(state.deckId, baseDeck.deckId, card))
                        .Select(CloneCard)
                        .Where(card => card != null));
            }

            foreach (DeckData descendantDeck in descendantDecks)
            {
                if (descendantDeck?.cards == null) continue;
                subdeckPool.AddRange(
                    descendantDeck.cards
                        .Where(card => ShouldIncludeCardInDeck(state.deckId, descendantDeck.deckId, card))
                        .Select(CloneCard)
                        .Where(card => card != null));
            }

            AddMergedPool(state.drawPile, basePool, subdeckPool);
        }

        List<CardData> sharedPool = new();
        foreach (DeckData sharedDeck in GetSharedDecks())
        {
            if (sharedDeck?.cards == null) continue;
            sharedPool.AddRange(
                sharedDeck.cards
                    .Where(card => ShouldIncludeCardInDeck(state.deckId, sharedDeck.deckId, card))
                    .Select(CloneCard)
                    .Where(card => card != null));
        }
        Shuffle(sharedPool);
        foreach (CardData sharedCard in sharedPool)
        {
            int insertIndex = UnityEngine.Random.Range(0, state.drawPile.Count + 1);
            state.drawPile.Insert(insertIndex, sharedCard);
        }

        ApplyBalancedDrawOrdering(state.drawPile);

        PopulateSituationPool(state, deckId);

        // Snapshot the deck's required-material distribution now, while drawPile still equals
        // the complete composed deck (nothing has been played/discarded yet) — see
        // NationBlackboard. Doing this later (e.g. lazily on first AI turn) would risk
        // computing it from a partial deck if cards had already moved to the discard pile.
        NationBlackboard.SetDeckResourceShare(leader, ComputeDeckResourceShare(state));

        return state;
    }

    // Sums each card's material cost (leatherRequired..mithrilRequired — the 6 tradeable
    // materials; gold is currency, not a card-cost material, so it's excluded) across the
    // leader's full composed deck and normalizes to a 0..1 share per material. Falls back to
    // an even split if the deck has no material costs at all (e.g. a still-empty NPL deck).
    private static IReadOnlyDictionary<ProducesEnum, float> ComputeDeckResourceShare(PlayerDeckState state)
    {
        Dictionary<ProducesEnum, float> totals = new()
        {
            [ProducesEnum.leather] = 0f,
            [ProducesEnum.mounts] = 0f,
            [ProducesEnum.timber] = 0f,
            [ProducesEnum.iron] = 0f,
            [ProducesEnum.steel] = 0f,
            [ProducesEnum.mithril] = 0f,
        };

        foreach (CardData card in state.drawPile)
        {
            if (card == null) continue;
            totals[ProducesEnum.leather] += card.leatherRequired;
            totals[ProducesEnum.mounts] += card.mountsRequired;
            totals[ProducesEnum.timber] += card.timberRequired;
            totals[ProducesEnum.iron] += card.ironRequired;
            totals[ProducesEnum.steel] += card.steelRequired;
            totals[ProducesEnum.mithril] += card.mithrilRequired;
        }

        float sum = totals.Values.Sum();
        Dictionary<ProducesEnum, float> share = new();
        foreach (var kvp in totals) share[kvp.Key] = sum > 0f ? kvp.Value / sum : 1f / totals.Count;
        return share;
    }

    private void PopulateSituationPool(PlayerDeckState state, string deckId)
    {
        state.situationPool.Clear();
        bool selectedLeafDeck = deckManifestById.TryGetValue(deckId, out DeckManifestEntry selectedEntry)
            && !string.IsNullOrWhiteSpace(selectedEntry.parentDeckId);
        IEnumerable<DeckData> ownedDecks = selectedLeafDeck ? GetDeckChain(deckId) : GetDeckTree(deckId);
        IEnumerable<DeckData> allDecks = ownedDecks.Concat(GetSharedDecks());
        foreach (DeckData deck in allDecks)
        {
            if (deck?.cards == null) continue;
            foreach (CardData card in deck.cards)
            {
                if (!ShouldIncludeCardInSituationPool(card)) continue;
                CardData clone = CloneCard(card);
                if (clone != null) state.situationPool.Add(clone);
            }
        }
    }

    private static void AddMergedPool(List<CardData> destination, List<CardData> basePool, List<CardData> leafPool)
    {
        if (destination == null) return;

        List<CardData> merged = new List<CardData>();
        if (basePool != null) merged.AddRange(basePool);
        if (leafPool != null) merged.AddRange(leafPool);
        Shuffle(merged);
        destination.AddRange(merged);
    }

    private static CardData TakeRandomCard(List<CardData> cards)
    {
        if (cards == null || cards.Count == 0) return null;
        int index = UnityEngine.Random.Range(0, cards.Count);
        CardData card = cards[index];
        cards.RemoveAt(index);
        return card;
    }

    private static void ApplyBalancedDrawOrdering(List<CardData> cards)
    {
        if (cards == null || cards.Count < 2) return;

        List<CardData> shuffled = new(cards);
        Shuffle(shuffled);

        Dictionary<BalancedDeckBucket, List<CardData>> buckets = new();
        foreach (BalancedDeckBucket bucket in Enum.GetValues(typeof(BalancedDeckBucket)))
        {
            buckets[bucket] = new List<CardData>();
        }

        foreach (CardData card in shuffled)
        {
            buckets[GetBalancedDeckBucket(card)].Add(card);
        }

        foreach (List<CardData> bucketCards in buckets.Values)
        {
            Shuffle(bucketCards);
        }

        List<CardData> ordered = new(cards.Count);
        while (HasBalancedTargetCards(buckets))
        {
            List<BalancedDeckBucket> cycleSlots = new(BalancedDrawPattern);
            Shuffle(cycleSlots);

            foreach (BalancedDeckBucket preferredBucket in cycleSlots)
            {
                CardData next = TakeBalancedCard(buckets, preferredBucket);
                if (next == null) break;
                ordered.Add(next);
            }
        }

        List<CardData> miscCards = buckets[BalancedDeckBucket.Misc];
        for (int i = 0; i < miscCards.Count; i++)
        {
            int insertIndex = UnityEngine.Random.Range(0, ordered.Count + 1);
            ordered.Insert(insertIndex, miscCards[i]);
        }

        cards.Clear();
        cards.AddRange(ordered);
    }

    private static bool HasBalancedTargetCards(Dictionary<BalancedDeckBucket, List<CardData>> buckets)
    {
        foreach (BalancedDeckBucket bucket in BalancedDrawPattern)
        {
            if (buckets.TryGetValue(bucket, out List<CardData> cards) && cards.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static BalancedDeckBucket GetBalancedDeckBucket(CardData card)
    {
        if (card == null) return BalancedDeckBucket.Misc;

        return card.GetCardType() switch
        {
            CardTypeEnum.Army => BalancedDeckBucket.Army,
            CardTypeEnum.Event => BalancedDeckBucket.Event,
            CardTypeEnum.Environmental => BalancedDeckBucket.Environmental,
            CardTypeEnum.PC => BalancedDeckBucket.PC,
            CardTypeEnum.Land => BalancedDeckBucket.Land,
            CardTypeEnum.Encounter => BalancedDeckBucket.Encounter,
            CardTypeEnum.Character => BalancedDeckBucket.Character,
            CardTypeEnum.Action => BalancedDeckBucket.ActionSpell,
            CardTypeEnum.Spell => BalancedDeckBucket.ActionSpell,
            _ => BalancedDeckBucket.Misc
        };
    }

    private static CardData TakeBalancedCard(Dictionary<BalancedDeckBucket, List<CardData>> buckets, BalancedDeckBucket preferredBucket)
    {
        if (buckets.TryGetValue(preferredBucket, out List<CardData> preferredCards) && preferredCards.Count > 0)
        {
            return TakeRandomCard(preferredCards);
        }

        BalancedDeckBucket? fallbackBucket = null;
        int fallbackCount = 0;
        foreach (BalancedDeckBucket bucket in BalancedDrawPattern.Distinct())
        {
            if (bucket == preferredBucket) continue;
            if (!buckets.TryGetValue(bucket, out List<CardData> cards) || cards.Count <= 0) continue;
            if (cards.Count > fallbackCount)
            {
                fallbackBucket = bucket;
                fallbackCount = cards.Count;
            }
        }

        if (fallbackBucket.HasValue && buckets.TryGetValue(fallbackBucket.Value, out List<CardData> fallbackCards) && fallbackCards.Count > 0)
        {
            return TakeRandomCard(fallbackCards);
        }

        if (buckets.TryGetValue(BalancedDeckBucket.Misc, out List<CardData> miscCards) && miscCards.Count > 0)
        {
            return TakeRandomCard(miscCards);
        }

        return null;
    }

    private IEnumerable<DeckData> GetDeckTree(string deckId)
    {
        if (string.IsNullOrWhiteSpace(deckId)) yield break;

        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> queue = new();
        queue.Enqueue(deckId);

        while (queue.Count > 0)
        {
            string currentDeckId = queue.Dequeue();
            if (string.IsNullOrWhiteSpace(currentDeckId) || !visited.Add(currentDeckId)) continue;
            if (!loadedDecksById.TryGetValue(currentDeckId, out DeckData deckData) || deckData == null) continue;
            yield return deckData;

            foreach (DeckManifestEntry entry in deckManifestById.Values)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.parentDeckId)) continue;
                if (!string.Equals(entry.parentDeckId, currentDeckId, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(entry.deckId)) continue;
                if (entry.sharedToAll) continue;
                queue.Enqueue(entry.deckId);
            }
        }
    }

    private static bool ShouldIncludeCardInDeck(string ownerDeckId, string sourceDeckId, CardData card)
    {
        if (card == null) return false;

        // Encounters are world content now (see Board.SpawnEncounters) — scattered onto the
        // map directly from the master catalog (DeckManager.GetAllEncounterCardClones), not
        // drawn from any leader's own deck. Never part of a drawable pool, same reasoning as
        // Object below.
        if (card.IsEncounterCard()) return false;

        // Object cards are lookup-only data records (see EvaluatePlayability) — never part
        // of a drawable deck/hand pool.
        if (card.GetCardType() == CardTypeEnum.Object) return false;

        // Action cards are drawable like any other card; they are ALSO cloned into
        // the situation pool (see ShouldIncludeCardInSituationPool), so they can
        // surface both from the hand and as opportunity cards.
        return true;
    }

    private static bool ShouldIncludeCardInSituationPool(CardData card)
    {
        if (card == null) return false;
        CardTypeEnum type = card.GetCardType();
        return type == CardTypeEnum.Action
            || type == CardTypeEnum.Spell
            || type == CardTypeEnum.Event
            || (type == CardTypeEnum.Character && !string.IsNullOrWhiteSpace(card.startingPC));
    }

    private IEnumerable<DeckData> GetSharedDecks()
    {
        foreach (DeckManifestEntry entry in deckManifestById.Values)
        {
            if (entry == null || !entry.sharedToAll || entry.excluded) continue;
            if (string.IsNullOrWhiteSpace(entry.deckId)) continue;
            if (loadedDecksById.TryGetValue(entry.deckId, out DeckData deckData) && deckData != null)
            {
                yield return deckData;
            }
        }
    }

    private IEnumerable<DeckData> GetDeckChain(string deckId)
    {
        if (string.IsNullOrWhiteSpace(deckId)) yield break;

        Stack<DeckData> chain = new();
        string currentDeckId = deckId;
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);

        while (!string.IsNullOrWhiteSpace(currentDeckId)
            && visited.Add(currentDeckId)
            && deckManifestById.TryGetValue(currentDeckId, out DeckManifestEntry entry))
        {
            if (loadedDecksById.TryGetValue(currentDeckId, out DeckData deckData) && deckData != null)
            {
                chain.Push(deckData);
            }

            currentDeckId = entry.parentDeckId;
        }

        while (chain.Count > 0)
        {
            yield return chain.Pop();
        }
    }

    private string ResolveDeckIdForLeader(PlayableLeader leader)
    {
        if (leader == null) return null;

        string selectedSubdeckId = leader.GetSelectedSubdeckId();
        if (!string.IsNullOrWhiteSpace(selectedSubdeckId)
            && deckManifestById.ContainsKey(selectedSubdeckId))
        {
            return selectedSubdeckId;
        }

        DeckManifestEntry byNation = deckManifestById.Values.FirstOrDefault(x =>
            !x.sharedToAll &&
            !x.isBaseDeck &&
            !string.IsNullOrWhiteSpace(x?.nation) &&
            string.Equals(x.nation, leader.characterName, StringComparison.OrdinalIgnoreCase));
        if (byNation != null) return byNation.deckId;

        int alignment = (int)leader.alignment;
        DeckManifestEntry byAlignment = deckManifestById.Values.FirstOrDefault(x =>
            x != null
            && !x.sharedToAll
            && x.isBaseDeck
            && x.alignment == alignment);
        return byAlignment?.deckId;
    }

    // NPLs aren't tied to a nation, so they draw from one of three fixed alignment-based decks
    // instead of ResolveDeckIdForLeader's nation/subdeck/base-deck resolution above (that
    // method's own alignment fallback specifically requires isBaseDeck: true, which these
    // NPL decks deliberately aren't — they're not a PlayableLeader "nation base", so a
    // dedicated resolver is simpler than overloading that fallback's meaning).
    private string ResolveDeckIdForNonPlayableLeader(NonPlayableLeader leader)
    {
        if (leader == null) return null;

        string deckId = leader.alignment switch
        {
            AlignmentEnum.freePeople => "nonplayableleader_freepeople",
            AlignmentEnum.darkServants => "nonplayableleader_darkservants",
            AlignmentEnum.neutral => "nonplayableleader_neutral",
            _ => "nonplayableleader_neutral"
        };

        if (!deckManifestById.ContainsKey(deckId))
        {
            Debug.LogWarning($"DeckManager: NPL deck '{deckId}' not found in manifest for {leader.characterName}.");
            return null;
        }
        return deckId;
    }

    private static void Shuffle<T>(List<T> list)
    {
        if (list == null) return;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void RefreshInspectorDecks()
    {
        inspectorDecks = loadedDecksById.Values
            .OrderBy(x => x.nation)
            .ThenBy(x => x.deckId)
            .ToList();
    }


    private GameObject ResolveCardPrefab()
    {
        if (cardCameObject != null)
        {
            if (cardCameObject.activeSelf)
            {
                cardCameObject.SetActive(false);
            }
            return cardCameObject;
        }
        if (cardBloomWheel == null) return null;

        Card existingCard = cardBloomWheel.GetComponentInChildren<Card>(true);
        if (existingCard == null) return null;

        cardCameObject = existingCard.gameObject;
        if (cardCameObject != null)
        {
            cardCameObject.SetActive(false);
        }

        return cardCameObject;
    }

    public GameObject GetCardPrefabTemplate() => ResolveCardPrefab();

    public CardBloomWheel GetCardBloomWheel() => cardBloomWheel;

    public GameObject GetTokenCardPrefabTemplate()
    {
        if (tokenCardTemplate == null) return null;
        if (tokenCardTemplate.activeSelf)
        {
            tokenCardTemplate.SetActive(false);
        }
        return tokenCardTemplate;
    }

    public Vector2 GetCardSize() => new(120f, 170f);
}
