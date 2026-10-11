using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public static class EncounterResolver
{
    public static async Task<bool> ResolveAsync(CardData encounterCard, Character actor)
    {
        if (encounterCard == null || actor == null || actor.killed) return false;

        List<EncounterOptionData> options = BuildSelectableOptions(encounterCard);
        if (options.Count == 0) return false;

        bool isAi = !actor.isPlayerControlled;
        Sprite portrait = await ResolvePortraitAsync(encounterCard);
        string prompt = BuildPrompt(encounterCard);
        List<string> optionLabels = options.Select(GetOptionLabel).ToList();
        List<string> optionDescriptions = options.Select(GetOptionDescription).ToList();
        List<string> optionIcons = options.Select(GetPrimarySkillIconName).ToList();
        string selection = await AskEncounterSelectionAsync(
            encounterCard.name,
            prompt,
            optionLabels,
            optionDescriptions,
            optionIcons,
            isAi,
            portrait);
        if (string.IsNullOrWhiteSpace(selection)) return false;

        EncounterOptionData chosenOption = options.FirstOrDefault(option => string.Equals(GetOptionLabel(option), selection, StringComparison.Ordinal));
        if (chosenOption == null) return false;

        EncounterOutcomeData outcome = ResolveOutcome(chosenOption, actor);
        if (outcome == null) return false;

        ApplyOutcome(actor, outcome);
        return true;
    }

    private static List<EncounterOptionData> BuildSelectableOptions(CardData encounterCard)
    {
        List<EncounterOptionData> options = new();
        if (encounterCard.encounterOptions != null)
        {
            options.AddRange(encounterCard.encounterOptions.Where(option => option != null).Take(3));
        }
        if (encounterCard.fleeOption != null)
        {
            options.Add(encounterCard.fleeOption);
        }
        return options;
    }

    private static Task<string> AskEncounterSelectionAsync(
        string title,
        string prompt,
        List<string> optionLabels,
        List<string> optionDescriptions,
        List<string> optionIcons,
        bool isAi,
        Sprite portrait)
    {
        return SelectionDialog.Ask(
            prompt,
            "Choose",
            string.Empty,
            optionLabels,
            optionDescriptions,
            isAi,
            portrait,
            EventIconType.Encounter,
            title,
            optionIcons);
    }

    private static string BuildPrompt(CardData encounterCard)
    {
        if (!string.IsNullOrWhiteSpace(encounterCard.historyText))
        {
            return encounterCard.historyText.Trim();
        }

        string prompt = encounterCard.GetDescriptionBody();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = encounterCard.quote;
        }

        return string.IsNullOrWhiteSpace(prompt)
            ? $"How will you answer {encounterCard.name}?"
            : prompt.Trim();
    }

    private static string GetOptionLabel(EncounterOptionData option)
    {
        if (option == null) return string.Empty;

        if (!string.IsNullOrWhiteSpace(option.label))
        {
            return option.label.Trim();
        }

        if (string.IsNullOrWhiteSpace(option.description))
        {
            return string.Empty;
        }

        string text = option.description.Trim();
        const int maxLength = 40;
        return text.Length <= maxLength ? text : $"{text[..(maxLength - 3)].TrimEnd()}...";
    }

    // Options often gate their best outcome on more than one skill at once (see the
    // outcome data), but the option button can only show a single class icon - so we
    // pick whichever skill has the highest requirement anywhere among the option's
    // outcomes, with Commander/Agent/Emmissary/Mage as the tie-break order.
    private static readonly (string SpriteName, Func<EncounterOutcomeData, int> GetRequirement)[] SkillIcons =
    {
        ("commander", o => o.minCommander),
        ("agent", o => o.minAgent),
        ("emmissary", o => o.minEmmissary),
        ("mage", o => o.minMage),
    };

    private static string GetPrimarySkillIconName(EncounterOptionData option)
    {
        if (option?.outcomes == null || option.outcomes.Count == 0) return null;

        string bestSpriteName = null;
        int bestRequirement = 0;
        foreach ((string spriteName, Func<EncounterOutcomeData, int> getRequirement) in SkillIcons)
        {
            int requirement = option.outcomes.Max(outcome => outcome == null ? 0 : getRequirement(outcome));
            if (requirement > bestRequirement)
            {
                bestRequirement = requirement;
                bestSpriteName = spriteName;
            }
        }

        return bestSpriteName;
    }

    private static string GetOptionDescription(EncounterOptionData option)
    {
        return option == null || string.IsNullOrWhiteSpace(option.description)
            ? string.Empty
            : option.description.Trim();
    }

    private static EncounterOutcomeData ResolveOutcome(EncounterOptionData option, Character actor)
    {
        if (option?.outcomes == null || actor == null) return null;
        return option.outcomes.FirstOrDefault(outcome => MeetsConditions(outcome, actor));
    }

    private static bool MeetsConditions(EncounterOutcomeData outcome, Character actor)
    {
        if (outcome == null || actor == null) return false;
        if (!string.IsNullOrWhiteSpace(outcome.requiredAlignment)
            && !string.Equals(outcome.requiredAlignment, "any", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(actor.GetAlignment().ToString(), outcome.requiredAlignment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (actor.GetCommander() < Mathf.Max(0, outcome.minCommander)) return false;
        if (actor.GetAgent() < Mathf.Max(0, outcome.minAgent)) return false;
        if (actor.GetEmmissary() < Mathf.Max(0, outcome.minEmmissary)) return false;
        if (actor.GetMage() < Mathf.Max(0, outcome.minMage)) return false;
        if (actor.health < Mathf.Max(0, outcome.minHealth)) return false;
        if (outcome.maxHealth >= 0 && actor.health > outcome.maxHealth) return false;
        return true;
    }

    private static void ApplyOutcome(Character actor, EncounterOutcomeData outcome)
    {
        if (actor == null || outcome == null) return;

        Leader owner = actor.GetOwner();
        if (outcome.healthDelta > 0)
        {
            actor.Heal(outcome.healthDelta);
        }
        else if (outcome.healthDelta < 0)
        {
            actor.Wounded(null, -outcome.healthDelta);
        }

        if (owner != null)
        {
            ApplyResourceDelta(owner, outcome.goldDelta, amount => owner.AddGold(amount, false), amount => owner.RemoveGold(amount, false));
            ApplyResourceDelta(owner, outcome.leatherDelta, amount => owner.AddLeather(amount, false), amount => owner.RemoveLeather(amount, false));
            ApplyResourceDelta(owner, outcome.timberDelta, amount => owner.AddTimber(amount, false), amount => owner.RemoveTimber(amount, false));
            ApplyResourceDelta(owner, outcome.mountsDelta, amount => owner.AddMounts(amount, false), amount => owner.RemoveMounts(amount, false));
            ApplyResourceDelta(owner, outcome.ironDelta, amount => owner.AddIron(amount, false), amount => owner.RemoveIron(amount, false));
            ApplyResourceDelta(owner, outcome.steelDelta, amount => owner.AddSteel(amount, false), amount => owner.RemoveSteel(amount, false));
            ApplyResourceDelta(owner, outcome.mithrilDelta, amount => owner.AddMithril(amount, false), amount => owner.RemoveMithril(amount, false));
        }

        if (outcome.statuses != null)
        {
            foreach (EncounterStatusEffectData status in outcome.statuses)
            {
                if (status == null || string.IsNullOrWhiteSpace(status.statusId)) continue;
                if (!Enum.TryParse(status.statusId, true, out StatusEffectEnum parsed)) continue;
                actor.ApplyStatusEffect(parsed, Mathf.Max(1, status.turns));
            }
        }

        if (!string.IsNullOrWhiteSpace(outcome.resultText))
        {
            MessageDisplayNoUI.ShowMessage(actor.hex, actor, outcome.resultText, ResolveOutcomeColor(outcome));
        }
    }

    private static void ApplyResourceDelta(Leader owner, int delta, Action<int> add, Action<int> remove)
    {
        if (owner == null || delta == 0) return;
        if (delta > 0)
        {
            add?.Invoke(delta);
            return;
        }
        remove?.Invoke(-delta);
    }

    private static Color ResolveOutcomeColor(EncounterOutcomeData outcome)
    {
        if (outcome == null) return Color.white;
        if (outcome.healthDelta < 0 || outcome.goldDelta < 0 || outcome.leatherDelta < 0 || outcome.timberDelta < 0
            || outcome.mountsDelta < 0 || outcome.ironDelta < 0 || outcome.steelDelta < 0 || outcome.mithrilDelta < 0)
        {
            return Color.red;
        }
        if (outcome.healthDelta > 0 || outcome.goldDelta > 0 || outcome.leatherDelta > 0 || outcome.timberDelta > 0
            || outcome.mountsDelta > 0 || outcome.ironDelta > 0 || outcome.steelDelta > 0 || outcome.mithrilDelta > 0)
        {
            return Color.green;
        }
        return Color.yellow;
    }

    private static async Task<Sprite> ResolvePortraitAsync(CardData encounterCard)
    {
        if (encounterCard == null) return null;
        Illustrations illustrations = UnityEngine.Object.FindAnyObjectByType<Illustrations>();
        if (illustrations == null) return null;

        List<string> candidateNames = BuildPortraitLookupNames(encounterCard);
        Sprite portrait = TryResolvePortrait(illustrations, candidateNames);
        if (portrait != null) return portrait;

        const int maxWaitFrames = 120;
        for (int i = 0; i < maxWaitFrames; i++)
        {
            await Task.Yield();
            portrait = TryResolvePortrait(illustrations, candidateNames);
            if (portrait != null) return portrait;
        }

        Debug.LogWarning($"Encounter portrait not found or not loaded in time for '{encounterCard.name}'. Tried keys: {string.Join(", ", candidateNames)}");
        return null;
    }

    private static Sprite TryResolvePortrait(Illustrations illustrations, List<string> candidateNames)
    {
        if (illustrations == null || candidateNames == null) return null;

        for (int i = 0; i < candidateNames.Count; i++)
        {
            string candidate = candidateNames[i];
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            Sprite sprite = illustrations.GetIllustrationByName(candidate, false);
            if (sprite != null) return sprite;
        }

        return null;
    }

    private static List<string> BuildPortraitLookupNames(CardData encounterCard)
    {
        List<string> candidates = new();
        AddCandidate(candidates, encounterCard?.portraitName);
        AddCandidate(candidates, encounterCard?.spriteName);
        AddCandidate(candidates, encounterCard?.name);
        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string value)
    {
        if (candidates == null || string.IsNullOrWhiteSpace(value)) return;
        if (!candidates.Contains(value))
        {
            candidates.Add(value);
        }
    }
}
