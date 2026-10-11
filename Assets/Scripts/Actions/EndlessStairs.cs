using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Opportunity card offered to any character (not commanding an army) standing on an
// underground hex (see Hex.IsUnderground). Playing it opens a choice of the 3 closest
// other underground locations and teleports the character to the chosen one.
public class EndlessStairs : CharacterAction
{
    private const int MaxDestinations = 3;
    private const string CardArtName = "EndlessStairs";

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

    private static List<Hex> FindClosestUndergroundHexes(Character character)
    {
        Board board = Board.Instance;
        if (board == null || character == null || character.hex == null) return new List<Hex>();

        Hex from = character.hex;
        return board.GetHexes()
            .Where(h => h != null && h != from && h.IsUnderground())
            .OrderBy(h => Vector2.Distance(from.v2, h.v2))
            .Take(MaxDestinations)
            .ToList();
    }

    private static string DescribeDestination(Hex hex)
    {
        PC pc = hex.GetPC();
        return pc != null
            ? $"{pc.pcName} at {hex.GetHoverV2()}"
            : $"Hex {hex.GetHoverV2()}";
    }

    // Illustrations only indexes Assets/Art/Cards/* and Assets/Art/Decks/* art, so a raw
    // terrain display name (e.g. "Mountains") never resolves â€” fall back to the card's own
    // art instead of leaving hexes without a PC with a blank icon.
    private static string GetDestinationIconName(Hex hex)
    {
        PC pc = hex.GetPC();
        return pc != null ? pc.pcName : CardArtName;
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.killed || character.hex == null) return false;
            if (character.IsArmyCommander()) return false;
            if (!character.hex.IsUnderground()) return false;
            return FindClosestUndergroundHexes(character).Count > 0;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            List<Hex> destinations = FindClosestUndergroundHexes(character);
            if (destinations.Count == 0) return false;

            bool isAI = !character.isPlayerControlled;
            Hex target = null;
            if (!isAI)
            {
                List<string> options = destinations.Select(DescribeDestination).ToList();
                List<string> optionIcons = destinations.Select(GetDestinationIconName).ToList();
                Illustrations illustrations = FindAnyObjectByType<Illustrations>();
                Sprite portrait = illustrations != null ? illustrations.GetIllustrationByName(CardArtName, false) : null;
                string selected = await SelectionDialog.AskImmediate(
                    "The Endless Stairs wind down into the deep places of the world. Where do they lead?",
                    "Ok",
                    "Cancel",
                    options,
                    null,
                    isAI,
                    portrait,
                    EventIconType.MultiChoice,
                    "Endless Stairs",
                    optionIcons);
                if (string.IsNullOrWhiteSpace(selected)) return false;
                int index = options.IndexOf(selected);
                if (index < 0) return false;
                target = destinations[index];
            }
            else
            {
                target = destinations[UnityEngine.Random.Range(0, destinations.Count)];
            }

            if (target == null) return false;

            Hex origin = character.hex;
            MoveCharacterToHex(character, target);
            MessageDisplayNoUI.ShowMessage(origin, character, $"{character.characterName} descends the Endless Stairs...", Color.gray);
            MessageDisplayNoUI.ShowMessage(target, character, $"{character.characterName} emerges from the Underground!", Color.gray);

            if (character.GetOwner() == Game.Instance?.player)
            {
                target.LookAt();
            }

            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}
