using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Heal: FreeNeutralSpell
{
    override public void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (c) => true;
        condition = (c) => {
            if (originalCondition != null && !originalCondition(c)) return false;
            return c.hex != null && c.hex.characters.Any(x =>
                (x.health < 100 || x.HasStatusEffect(StatusEffectEnum.Poisoned)) &&
                (x.GetOwner() == c.GetOwner() || (x.GetAlignment() == c.GetAlignment() && x.GetAlignment() != AlignmentEnum.neutral)));
        };
        async System.Threading.Tasks.Task<bool> healAsync(Character c)
        {
            if (originalEffect != null && !originalEffect(c)) return false;
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;

            List<Character> healTargets = c.hex.characters.Where(x =>
                (x.health < 100 || x.HasStatusEffect(StatusEffectEnum.Poisoned)) &&
                (x.GetOwner() == c.GetOwner() || (x.GetAlignment() == c.GetAlignment() && x.GetAlignment() != AlignmentEnum.neutral))
            ).ToList();
            if (healTargets.Count < 1) return false;

            bool isAI = !c.isPlayerControlled;
            Character target = null;
            if (!isAI)
            {
                string targetCharacter = await SelectionDialog.AskImmediate(
                    "Select character to heal",
                    "Ok",
                    "Cancel",
                    healTargets.Select(x => x.characterName).ToList(),
                    null,
                    isAI,
                    SelectionDialog.Instance != null ? SelectionDialog.Instance.GetCharacterIllustration(c) : null,
                    EventIconType.MultiChoice,
                    actionName,
                    SelectionDialog.CharacterIconNames(healTargets));
                if (string.IsNullOrEmpty(targetCharacter)) return false;
                target = healTargets.Find(x => x.characterName == targetCharacter);
            }
            else
            {
                target = healTargets.First();
            }

            if (target == null) return false;

            int health = UnityEngine.Random.Range(0, 10) * c.GetMage();
            health = Math.Max(0, ApplySpellEffectMultiplier(c, health));
            Character selectedCharacter = Board.Instance.selectedCharacter;
            target.Heal(health);
            if (selectedCharacter == target) FindAnyObjectByType<Layout>().GetSelectedCharacterIcon().Refresh(target);

            return true;
        }
        base.Initialize(c, condition, effect, healAsync);
    }
}
