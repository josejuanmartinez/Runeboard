using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class BattleOfSongs : CharacterAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.GetMage() < 1) return false;
            return GetEnemyMagesAtHex(character).Count > 0;
        };

        async Task<bool> battleAsync(Character character)
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;

            List<Character> targets = GetEnemyMagesAtHex(character);
            if (targets.Count < 1) return false;

            bool isAI = !character.isPlayerControlled;
            Character target = null;
            if (!isAI)
            {
                string targetName = await SelectionDialog.AskImmediate(
                    "Select enemy mage",
                    "Ok",
                    "Cancel",
                    targets.Select(x => x.characterName).ToList(),
                    null,
                    isAI,
                    SelectionDialog.Instance != null ? SelectionDialog.Instance.GetCharacterIllustration(c) : null,
                    EventIconType.MultiChoice,
                    actionName,
                    SelectionDialog.CharacterIconNames(targets));
                if (string.IsNullOrWhiteSpace(targetName)) return false;
                target = targets.Find(x => x.characterName == targetName);
            }
            else
            {
                target = targets.OrderByDescending(x => EstimateSongScore(x)).FirstOrDefault();
            }

            if (target == null) return false;
            ResolveBattleOfSongs(character, target);
            return true;
        }

        base.Initialize(c, condition, effect, battleAsync);
    }

    private List<Character> GetEnemyMagesAtHex(Character c)
    {
        return FindEnemyCharactersAtHex(c)
            .Where(x => x != null && x.GetMage() >= 1 && !x.IsHidden() && !x.IsRefusingDuels() && !x.IsArmyCommander())
            .ToList();
    }

    // Reused by UtilityAIContextDataBuilder.CacheSongDuelSignal so the AI's sensing layer asks
    // exactly the same "who could I challenge right now" question Initialize's own condition does.
    public List<Character> GetEligibleMageTargets(Character c) => GetEnemyMagesAtHex(c);

    // Reusable win-probability scoring, shared by real resolution (ResolveBattleOfSongs/the
    // AI target pick above) and the AI's sensing layer (UtilityAIContextDataBuilder.
    // CacheSongDuelSignal), so both ask the exact same question.
    public static float EstimateSongScore(Character character)
    {
        if (character == null) return 0f;
        float score = character.GetBaseMage() * 2f
                    + character.GetBaseEmmissary() * 0.5f;
        if (character.objects != null)
            score += character.objects.Sum(a => a != null ? a.GetAttackBonus() + a.GetDefenseBonus() : 0);
        if (character.HasStatusEffect(StatusEffectEnum.Strengthened)) score *= 1.10f;
        if (character.HasStatusEffect(StatusEffectEnum.Fortified)) score *= 1.10f;
        return score;
    }

    private void ResolveBattleOfSongs(Character attacker, Character defender)
    {
        if (attacker == null || defender == null) return;

        bool defenderAutoWins = defender.HasStatusEffect(StatusEffectEnum.DuelSupremacy);
        if (defenderAutoWins) defender.ClearStatusEffect(StatusEffectEnum.DuelSupremacy);

        float attackerScore = EstimateSongScore(attacker);
        float defenderScore = EstimateSongScore(defender);
        bool attackerWins = !defenderAutoWins && attackerScore > defenderScore;
        if (!defenderAutoWins && Mathf.Approximately(attackerScore, defenderScore))
            attackerWins = UnityEngine.Random.Range(0, 2) == 0;

        Character winner = attackerWins ? attacker : defender;
        Character loser  = attackerWins ? defender : attacker;

        float diff = Mathf.Abs(attackerScore - defenderScore);
        int wound = Mathf.Clamp(Mathf.RoundToInt(diff * 10f), 0, 100);
        if (wound == 0) wound = UnityEngine.Random.Range(5, 16);

        int loserHealthBefore = loser.health;
        loser.Wounded(winner.GetOwner(), wound);

        bool shouldShowPopup = attacker.isPlayerControlled || defender.isPlayerControlled
            || PlayerCanSeeHex(attacker.hex);

        string narration = BuildNarration(attacker, defender, winner, loser, wound, attackerScore, defenderScore, loserHealthBefore);

        if (shouldShowPopup)
        {
            PopupManager.ShowImmediate(
                $"Battle of Songs: {attacker.characterName} vs {defender.characterName}",
                FindAnyObjectByType<Illustrations>().GetIllustrationByName(attacker.characterName),
                FindAnyObjectByType<Illustrations>().GetIllustrationByName(defender.characterName),
                narration,
                true);
        }
        else
        {
            MessageDisplayNoUI.ShowMessage(attacker.hex, attacker, $"{winner.characterName} wins the Battle of Songs.", Color.cyan);
        }
    }

    private string BuildNarration(Character attacker, Character defender, Character winner, Character loser, int wound, float attackerScore, float defenderScore, int loserHealthBefore)
    {
        StringBuilder sb = new();
        int template = UnityEngine.Random.Range(0, 4);
        bool fatal = wound >= loserHealthBefore;

        switch (template)
        {
            case 0:
                sb.AppendLine($"{attacker.characterName} raises their voice against {defender.characterName}, weaving power into every word.");
                sb.AppendLine($"{winner.characterName}'s song rises above the other, bending the world to their will.");
                break;
            case 1:
                sb.AppendLine($"The air trembles as {attacker.characterName} and {defender.characterName} clash in a contest of pure magic.");
                sb.AppendLine($"{winner.characterName}'s melody overwhelms the other, shattering their focus.");
                break;
            case 2:
                sb.AppendLine($"{attacker.characterName} pits ancient words of power against {defender.characterName}'s counter-song.");
                sb.AppendLine($"{winner.characterName} finds the crack in the harmony and drives through it.");
                break;
            default:
                sb.AppendLine($"The songs of {attacker.characterName} and {defender.characterName} collide in unseen fire.");
                sb.AppendLine($"{winner.characterName}'s will holds firm while the other falters.");
                break;
        }

        sb.AppendLine($"Song power: {attacker.characterName} {attackerScore:0.0} vs {defender.characterName} {defenderScore:0.0}.");
        sb.AppendLine($"{loser.characterName} suffers {wound} wounds from the clash.");
        if (fatal) sb.AppendLine($"{loser.characterName} is undone by the song.");
        else       sb.AppendLine($"{winner.characterName}'s voice echoes last.");

        return sb.ToString();
    }

    private bool PlayerCanSeeHex(Hex hex)
    {
        if (hex == null) return false;
        Game g = Game.Instance;
        if (g == null || g.player == null) return false;
        return g.player.visibleHexes.Contains(hex) && hex.IsHexSeen();
    }
}
