using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class TransferArtifact : CharacterAction
{
    override public void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;
        effect = (c) => {
            return originalEffect == null || originalEffect(c); 
        };
        condition = (c) => {
            if (originalCondition != null && !originalCondition(c)) return false;
            if( c == null || c.killed) return false;
            bool hasFriendlyTarget = c.hex != null && c.hex.GetFriendlyCharacters(c.GetOwner()).Any(x => x != c);
            if (!hasFriendlyTarget) return false;
            return c.objects.Find(x => x.transferable) != null;
        };
        async System.Threading.Tasks.Task<bool> transferAsync(Character c)
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(c)) return false;
            List<Character> characters = c.hex.GetFriendlyCharacters(c.GetOwner()).Where(x => x != c).ToList();
            List<CardData> transferableArtifacts = c.objects.Where(x => x.transferable).ToList();
            if(characters.Count < 1 || transferableArtifacts.Count < 1) return false;
            bool isAI = !c.isPlayerControlled;
            Character character = null;
            CardData artifact = null;
            if(!isAI)
            {
                string targetArtifact = await SelectionDialog.AskImmediate(
                    "Select artifact",
                    "Ok",
                    "Cancel",
                    transferableArtifacts.Select(x => x.name).ToList(),
                    null,
                    isAI,
                    SelectionDialog.Instance != null ? SelectionDialog.Instance.GetCharacterIllustration(c) : null,
                    EventIconType.MultiChoice,
                    actionName,
                    SelectionDialog.CardIconNames(transferableArtifacts));
                artifact = transferableArtifacts.Find(x => x.name == targetArtifact);
                if (artifact == null) return false;

                string targetCharacter = await SelectionDialog.AskImmediate(
                    "Select friendly character",
                    "Ok",
                    "Cancel",
                    characters.Select(x => x.characterName).ToList(),
                    null,
                    isAI,
                    SelectionDialog.Instance != null ? SelectionDialog.Instance.GetCharacterIllustration(c) : null,
                    EventIconType.MultiChoice,
                    actionName,
                    SelectionDialog.CharacterIconNames(characters));
                character = c.hex.characters.Find(x => x.characterName == targetCharacter);
                if (character == null) return false;

            }
            else
            {
                float bestScore = -1f;
                foreach (CardData art in transferableArtifacts)
                {
                    foreach (Character target in characters)
                    {
                        float score = 0f;
                        // Prefer helping characters that currently lack the boosted skill
                        if (art.commanderBonus > 0) score += art.commanderBonus * 2 + Mathf.Max(0, 5 - target.GetCommander());
                        if (art.agentBonus > 0) score += art.agentBonus * 2 + Mathf.Max(0, 5 - target.GetAgent());
                        if (art.emmissaryBonus > 0) score += art.emmissaryBonus * 2 + Mathf.Max(0, 5 - target.GetEmmissary());
                        if (art.mageBonus > 0) score += art.mageBonus * 2 + Mathf.Max(0, 5 - target.GetMage());

                        // Prefer giving combat bonuses to army commanders
                        if (art.GetAttackBonus() > 0 && target.IsArmyCommander()) score += art.GetAttackBonus() * 3;
                        if (art.GetDefenseBonus() > 0 && target.IsArmyCommander()) score += art.GetDefenseBonus() * 2;

                        // Small penalty if target already excels at the boosted area
                        if (art.commanderBonus > 0 && target.GetCommander() > 3) score -= 2f;
                        if (art.agentBonus > 0 && target.GetAgent() > 3) score -= 2f;
                        if (art.emmissaryBonus > 0 && target.GetEmmissary() > 3) score -= 2f;
                        if (art.mageBonus > 0 && target.GetMage() > 3) score -= 2f;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            artifact = art;
                            character = target;
                        }
                    }
                }

                if (bestScore <= 0f) return false;
            }
            
            if (character == null || artifact == null) return false;
            if (character.objects.Count >= Character.MAX_OBJECTS)
            {
                await ConfirmationDialog.AskOk($"{character.characterName} can't hold more objects");
                return false;
            }

            c.objects.Remove(artifact);
            character.objects.Add(artifact);
            Character.RefreshArtifactPcVisibilityForHex(c.hex);

            MessageDisplayNoUI.ShowMessage(c.hex, c, $"{c.characterName}'s {artifact.name} transferred to {character.characterName}", Color.green);

            if (!isAI)
            {
                var layout = FindAnyObjectByType<Layout>();
                layout.GetSelectedCharacterIcon().Refresh(c);
                layout.GetSelectedCharacterIcon().Refresh(character);
            }
            return true;
        }
        base.Initialize(c, condition, effect, transferAsync);
    }
}
