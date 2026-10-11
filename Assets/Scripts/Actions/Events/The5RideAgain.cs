using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class The5RideAgain : EventAction
{
    private static readonly string[] IstariNames = { "Alatar", "Pallando", "Gandalf", "Radagast", "Saruman" };

    private static void MoveCharacterToHex(Character character, Hex targetHex)
    {
        if (character == null || targetHex == null || character.hex == targetHex) return;

        Hex previousHex = character.hex;
        if (previousHex != null)
        {
            if (previousHex.characters.Contains(character)) previousHex.characters.Remove(character);
            if (character.IsArmyCommander() && previousHex.armies != null && character.GetArmy() != null && previousHex.armies.Contains(character.GetArmy()))
                previousHex.armies.Remove(character.GetArmy());
            previousHex.RedrawCharacters();
            previousHex.RedrawArmies();
            Character.RefreshArtifactPcVisibilityForHex(previousHex);
        }

        if (!targetHex.characters.Contains(character)) targetHex.characters.Add(character);
        if (character.IsArmyCommander() && targetHex.armies != null && character.GetArmy() != null && !targetHex.armies.Contains(character.GetArmy()))
            targetHex.armies.Add(character.GetArmy());

        character.hex = targetHex;
        character.RefreshKidnappedCharactersPosition();
        Character.RefreshArtifactPcVisibilityForHex(targetHex);

        targetHex.RedrawCharacters();
        targetHex.RedrawArmies();

        if (character.GetOwner() == Game.Instance?.player)
        {
            targetHex.RevealArea(1, true);
        }
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Board board = Board.Instance;
            if (board == null || board.hexes == null) return false;

            Hex orthancHex = board.hexes.Values
                .FirstOrDefault(h => h != null && h.GetPC() != null && h.GetPC().pcName == "Orthanc");

            if (orthancHex == null) return false;

            List<Character> allCharacters = UnityEngine.Object.FindObjectsByType<Character>()
                .Where(ch => ch != null && !ch.killed)
                .ToList();

            List<Character> targets = allCharacters
                .Where(ch => IstariNames.Contains(ch.characterName) && ch.hex != null)
                .Distinct()
                .ToList();

            if (targets.Count == 0) return false;

            int movedCount = 0;
            foreach (Character target in targets)
            {
                if (target.hex == orthancHex) continue;
                MoveCharacterToHex(target, orthancHex);
                movedCount++;
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"The 5 ride again: {movedCount} Istari summoned to Orthanc.", new Color(0.8f, 0.7f, 0.4f));
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null) return false;

            Board board = Board.Instance;
            if (board == null || board.hexes == null) return false;

            bool hasOrthanc = board.hexes.Values.Any(h => h != null && h.GetPC() != null && h.GetPC().pcName == "Orthanc");
            if (!hasOrthanc) return false;

            return UnityEngine.Object.FindObjectsByType<Character>()
                .Any(ch => ch != null && !ch.killed && IstariNames.Contains(ch.characterName) && ch.hex != null);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}
