// Auto-generated wrapper actions for new deck cards

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using static CustomDeckExpansionActionHelpers;

public static class CustomDeckExpansionActionHelpers
{
    public static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }
}


public class KindleDawnfire : Dawn { }
public class WardAgainsttheEye : VisionsOfTolEressea { }
public class StewardsMuster : RalliedMen { }
public class HallowtheRoad : GoingOnAnAdventure { }
public class OathoftheWestfold : FirstLightOnTheThirdDay { }
public class MorgulTithe : ReachOfBaradUngol { }
public class EyesTribute : WhatDoesMordorCommand { }
public class FurnacesofIsengard : ChoppingTheTrees { }
public class FalseParley : RestlessEast { }
public class StormfromOrthanc : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;

            List<Character> allies = character.hex.characters
                .Where(ch => ch != null && !ch.killed && (ch.GetOwner() == character.GetOwner() || (character.GetAlignment() != AlignmentEnum.neutral && ch.GetAlignment() == character.GetAlignment())))
                .Distinct()
                .ToList();

            if (allies.Count == 0) return false;

            for (int i = 0; i < allies.Count; i++)
            {
                allies[i].hasActionedThisTurn = false;
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Storm from Orthanc drives {allies.Count} friendly unit(s) in this hex to act again.", Color.white);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.characters != null
                && character.hex.characters.Any(ch => ch != null && !ch.killed && (ch.GetOwner() == character.GetOwner() || (character.GetAlignment() != AlignmentEnum.neutral && ch.GetAlignment() == character.GetAlignment())));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class OrthancsSurveillanceAction : EventAction
{
    private const int Radius = 4;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            List<Hex> revealed = new();
            foreach (Hex hex in character.hex.GetHexesInRadius(Radius))
            {
                if (hex == null) continue;
                bool enemyPc = hex.GetPC() != null && hex.GetPC().owner != owner;
                bool enemyArmy = hex.armies != null && hex.armies.Any(a => a != null && !a.killed && a.commander != null && a.commander.GetOwner() != owner);
                if (!enemyPc && !enemyArmy) continue;

                hex.RevealArea(0, true, owner);
                revealed.Add(hex);
            }

            if (revealed.Count == 0) return false;
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Orthanc's Surveillance reveals {revealed.Count} enemy PC/army hex(es) in radius {Radius}.", Color.white);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            return character.hex.GetHexesInRadius(Radius).Any(hex => hex != null
                && ((hex.GetPC() != null && hex.GetPC().owner != owner)
                    || (hex.armies != null && hex.armies.Any(a => a != null && !a.killed && a.commander != null && a.commander.GetOwner() != owner))));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class EnginesFromIsengardAction : EventAction
{
    private const int Radius = 2;
    private const int BonusProc = 25;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            List<Army> armies = character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.armies != null)
                .SelectMany(h => h.armies)
                .Where(a => a != null && !a.killed && a.commander != null && a.commander.GetOwner() == owner)
                .Distinct()
                .ToList();

            if (armies.Count == 0) return false;

            for (int i = 0; i < armies.Count; i++)
            {
                armies[i].specialAbilityProcChance = Mathf.Clamp(armies[i].specialAbilityProcChance + BonusProc, 1, 100);
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Engines from Isengard drives {armies.Count} allied arm(ies) to +{BonusProc}% proc chance this turn.", Color.white);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            return character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.armies != null)
                .SelectMany(h => h.armies)
                .Any(a => a != null && !a.killed && a.commander != null && a.commander.GetOwner() == owner);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class PalantirOfOrthancAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Game game = Game.Instance;
            DeckManager deckManager = DeckManager.Instance;
            Board board = Board.Instance;
            if (game == null || deckManager == null || board == null || game.player == null) return false;
            if (character.GetOwner() != game.player || !deckManager.HasDeckFor(game.player)) return false;

            CardData peek = deckManager.GetDrawPile(game.player).Take(3).FirstOrDefault(card => card != null);
            if (peek == null) return false;
            if (!deckManager.TryAddCardToHand(game.player, peek)) return false;

            Leader owner = character.GetOwner();
            Hex nearestEnemyPc = board.GetHexes()
                .Where(h => h != null && h.GetPC() != null && h.GetPC().owner != owner)
                .OrderBy(h => Vector2.Distance(character.hex.v2, h.v2))
                .FirstOrDefault();

            if (nearestEnemyPc != null)
            {
                nearestEnemyPc.RevealArea(0, true, owner);
                MessageDisplayNoUI.ShowMessage(character.hex, character, $"Palantír of Orthanc secures {peek.name} and reveals {nearestEnemyPc.GetPC().pcName}.", Color.white);
            }
            else
            {
                MessageDisplayNoUI.ShowMessage(character.hex, character, $"Palantír of Orthanc secures {peek.name} from the top of your deck.", Color.white);
            }

            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            Game game = Game.Instance;
            DeckManager deckManager = DeckManager.Instance;
            return character != null && game != null && deckManager != null && game.player != null
                && character.GetOwner() == game.player
                && deckManager.HasDeckFor(game.player)
                && deckManager.GetDrawPile(game.player).Take(3).Any(card => card != null);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}
public class ThroughMirkwoodShadowsAction : EventAction
{
    private const int RevealCount = 5;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Leader owner = character.GetOwner();
            Board board = Board.Instance;
            if (owner == null || board == null || board.hexes == null) return false;

            var forestHexes = board.hexes.Values
                .Where(h => h != null && h.terrainType == TerrainEnum.forest && !h.IsScoutedBy(owner))
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(RevealCount)
                .ToList();

            if (forestHexes.Count == 0) return false;

            owner.AddTemporarySeenHexes(forestHexes);

            if (owner == Game.Instance?.player)
            {
                owner.RefreshVisibleHexesImmediate();
            }

            for (int i = 0; i < forestHexes.Count; i++)
            {
                forestHexes[i]?.RefreshVisibilityRendering();
            }

            forestHexes[0]?.LookAt();
            MessageDisplayNoUI.ShowMessage(
                character.hex,
                character,
                $"Through Mirkwood Shadows: {forestHexes.Count} unseen forest hex(es) are revealed for 1 turn.",
                new UnityEngine.Color(0.38f, 0.62f, 0.42f));

            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null) return false;

            Leader owner = character.GetOwner();
            Board board = Board.Instance;
            if (owner == null || board == null || board.hexes == null) return false;

            return board.hexes.Values.Any(h => h != null && h.terrainType == TerrainEnum.forest && !h.IsScoutedBy(owner));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class EreborBeckonsAction : EventAction
{
    private const int Duration = 2;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            List<Character> allies = character.hex.characters
                .Where(ch => ch != null && !ch.killed && ch.GetAlignment() == character.GetAlignment()
                    && (ch.race == RacesEnum.Hobbit || ch.race == RacesEnum.Dwarf))
                .Distinct()
                .ToList();

            if (allies.Count == 0) return false;

            for (int i = 0; i < allies.Count; i++)
            {
                allies[i].ApplyStatusEffect(StatusEffectEnum.Haste, Duration);
            }

            MessageDisplayNoUI.ShowMessage(
                character.hex,
                character,
                $"Erebor Beckons: {allies.Count} Hobbit/Dwarf unit(s) in this hex gain Haste for {Duration} turns.",
                new UnityEngine.Color(0.78f, 0.66f, 0.32f));

            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;

            return character.hex.characters.Any(ch => ch != null && !ch.killed && ch.GetAlignment() == character.GetAlignment()
                && (ch.race == RacesEnum.Hobbit || ch.race == RacesEnum.Dwarf));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class UnderMountainBannersAction : EventAction
{
    private const int Radius = 1;
    private const int Duration = 1;

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            List<Character> targets = character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.characters != null)
                .SelectMany(h => h.characters)
                .Where(ch => ch != null && !ch.killed && ch.IsArmyCommander() && ch.race == RacesEnum.Dwarf && IsAllied(character, ch))
                .Distinct()
                .ToList();

            if (targets.Count == 0) return false;

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].ApplyStatusEffect(StatusEffectEnum.Strengthened, Duration);
            }

            MessageDisplayNoUI.ShowMessage(
                character.hex,
                character,
                $"Under Mountain Banners: {targets.Count} allied Dwarf commander(s) gain Strengthened for {Duration} turn.",
                new UnityEngine.Color(0.74f, 0.68f, 0.4f));

            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.hex == null) return false;

            return character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.characters != null)
                .SelectMany(h => h.characters)
                .Any(ch => ch != null && !ch.killed && ch.IsArmyCommander() && ch.race == RacesEnum.Dwarf && IsAllied(character, ch));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class HiddenTreasureAction : EventAction
{
    private const int GoldAmount = 5;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Leader owner = character.GetOwner();
            if (owner == null) return false;

            owner.AddGold(GoldAmount);
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Hidden Treasure yields +{GoldAmount} <sprite name=\"gold\">.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.GetOwner() != null;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class TrollsHoardGrantArtifactAction : EventAction
{
    private static HashSet<string> GetOwnedOrHiddenObjectNames(Board board)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        if (board != null && board.hexes != null)
        {
            foreach (Hex hex in board.hexes.Values)
            {
                if (hex?.hiddenObjects == null) continue;
                foreach (CardData obj in hex.hiddenObjects)
                {
                    if (obj != null && !string.IsNullOrWhiteSpace(obj.name))
                    {
                        names.Add(obj.name);
                    }
                }
            }
        }

        foreach (Leader leader in UnityEngine.Object.FindObjectsByType<Leader>(FindObjectsSortMode.None))
        {
            if (leader?.controlledCharacters == null) continue;
            foreach (Character ch in leader.controlledCharacters)
            {
                if (ch?.objects == null) continue;
                foreach (CardData obj in ch.objects)
                {
                    if (obj != null && !string.IsNullOrWhiteSpace(obj.name))
                    {
                        names.Add(obj.name);
                    }
                }
            }
        }

        return names;
    }

    private static List<CardData> GetUnusedObjectCards(HashSet<string> unavailableNames)
    {
        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        List<CardData> all = deckManager?.GetAllObjectCardClones() ?? new List<CardData>();
        return all.Where(o => o != null && (unavailableNames == null || !unavailableNames.Contains(o.name))).ToList();
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
            if (character.objects.Count >= Character.MAX_OBJECTS) return false;

            Board board = Board.Instance;

            HashSet<string> unavailable = GetOwnedOrHiddenObjectNames(board);
            foreach (CardData owned in character.objects)
            {
                if (owned != null && !string.IsNullOrWhiteSpace(owned.name)) unavailable.Add(owned.name);
            }

            List<CardData> candidates = GetUnusedObjectCards(unavailable);

            if (candidates.Count == 0) return false;

            CardData chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            if (chosen == null) return false;

            character.objects.Add(chosen);
            Character.RefreshArtifactPcVisibilityForHex(character.hex);
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Troll's Hoard yields {chosen.name}.", Color.yellow);
            Sounds.Instance?.PlayArtifactFound();
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.objects.Count >= Character.MAX_OBJECTS) return false;

            Board board = Board.Instance;

            HashSet<string> unavailable = GetOwnedOrHiddenObjectNames(board);
            foreach (CardData owned in character.objects)
            {
                if (owned != null && !string.IsNullOrWhiteSpace(owned.name)) unavailable.Add(owned.name);
            }

            return GetUnusedObjectCards(unavailable).Count > 0;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class VeinOfTrueSilverAction : EventAction
{
    private const int MithrilAmount = 3;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Leader owner = character.GetOwner();
            Game game = Game.Instance;
            DeckManager deckManager = DeckManager.Instance;
            if (owner == null || game == null || deckManager == null) return false;

            PlayableLeader player = game.player;
            if (player == null || owner != player || !deckManager.HasDeckFor(player)) return false;

            var fullDeck = deckManager.GetFullDeck(player);
            CardData discardTarget = fullDeck.FirstOrDefault(card => card != null && !card.IsEncounterCard() && !string.Equals(card.name, "Vein of True Silver", StringComparison.OrdinalIgnoreCase));
            if (discardTarget == null) return false;

            CardData balrog = deckManager.FindCardByNameForLeader(player, "Balrog");
            if (balrog == null || !balrog.IsEncounterCard()) return false;

            owner.AddMithril(MithrilAmount);
            if (!deckManager.TryDiscardCard(player, discardTarget.name, out _)) return false;
            if (!deckManager.TryAddCardToHand(player, balrog)) return false;

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Vein of True Silver yields +{MithrilAmount} <sprite name=\"mithril\">, but the Balrog enters your hand.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null) return false;

            Leader owner = character.GetOwner();
            Game game = Game.Instance;
            DeckManager deckManager = DeckManager.Instance;
            if (owner == null || game == null || deckManager == null || game.player == null) return false;
            if (owner != game.player || !deckManager.HasDeckFor(game.player)) return false;

            bool hasReplaceableCard = deckManager.GetFullDeck(game.player)
                .Any(card => card != null && !card.IsEncounterCard() && !string.Equals(card.name, "Vein of True Silver", StringComparison.OrdinalIgnoreCase));
            CardData balrog = deckManager.FindCardByNameForLeader(game.player, "Balrog");
            return hasReplaceableCard && balrog != null && balrog.IsEncounterCard();
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class SealTheLowerGatesAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;

            List<Character> enemies = character.hex.characters
                .Where(ch => ch != null && !ch.killed && ch.GetAlignment() != character.GetAlignment())
                .Distinct()
                .ToList();

            if (enemies.Count == 0) return false;

            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].ApplyStatusEffect(StatusEffectEnum.Halted, 1);
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Seal the Lower Gates halts {enemies.Count} enemy unit(s) in this hex.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.characters != null
                && character.hex.characters.Any(ch => ch != null && !ch.killed && ch.GetAlignment() != character.GetAlignment());
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class HallOfOathsAction : EventAction
{
    private const int LoyaltyGain = 15;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;
            PC pc = character.hex.GetPC();
            if (pc == null) return false;
            pc.IncreaseLoyalty(LoyaltyGain, character);
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Hall of Oaths grants {pc.pcName} +{LoyaltyGain} loyalty.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.GetPC() != null && character.hex.GetPC().loyalty < 100;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class AshenBreathRepaidAction : EventAction
{
    private const int Radius = 1;

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            List<Character> allies = character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.characters != null)
                .SelectMany(h => h.characters)
                .Where(ch => ch != null && !ch.killed && ch.race == RacesEnum.Dwarf && IsAllied(character, ch))
                .Distinct()
                .ToList();

            if (allies.Count == 0) return false;

            int burningCleared = 0;
            for (int i = 0; i < allies.Count; i++)
            {
                if (allies[i].HasStatusEffect(StatusEffectEnum.Burning))
                {
                    allies[i].ClearStatusEffect(StatusEffectEnum.Burning);
                    burningCleared++;
                }
                allies[i].ApplyStatusEffect(StatusEffectEnum.Strengthened, 1);
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Ashen Breath Repaid clears Burning from {burningCleared} allied Dwarf unit(s) and strengthens {allies.Count}.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.characters != null)
                .SelectMany(h => h.characters)
                .Any(ch => ch != null && !ch.killed && ch.race == RacesEnum.Dwarf && IsAllied(character, ch));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class ForgefireKeptAction : EventAction
{
    private const int Duration = 2;

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            Character target = character.hex.characters
                .Where(ch => ch != null && !ch.killed && ch.race == RacesEnum.Dwarf && IsAllied(character, ch))
                .OrderByDescending(ch => ch.IsArmyCommander() ? 1 : 0)
                .ThenByDescending(ch => ch.GetCommander() + ch.GetAgent() + ch.GetEmmissary() + ch.GetMage())
                .FirstOrDefault();

            if (target == null) return false;

            target.ApplyStatusEffect(StatusEffectEnum.Fortified, Duration);
            owner.AddIron(1);
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Forgefire Kept fortifies {target.characterName} for {Duration} turns and yields +1 <sprite name=\"iron\">.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.characters != null && character.GetOwner() != null
                && character.hex.characters.Any(ch => ch != null && !ch.killed && ch.race == RacesEnum.Dwarf && IsAllied(character, ch));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class WallOfOakAndIronAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;

            List<Character> allies = character.hex.characters
                .Where(ch => ch != null && !ch.killed && ch.race == RacesEnum.Dwarf && (ch.GetOwner() == character.GetOwner() || (character.GetAlignment() != AlignmentEnum.neutral && ch.GetAlignment() == character.GetAlignment())))
                .Distinct()
                .ToList();

            if (allies.Count == 0) return false;

            for (int i = 0; i < allies.Count; i++)
            {
                allies[i].ApplyStatusEffect(StatusEffectEnum.Fortified, 1);
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Wall of Oak and Iron fortifies {allies.Count} allied Dwarf unit(s).", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.characters != null
                && character.hex.characters.Any(ch => ch != null && !ch.killed && ch.race == RacesEnum.Dwarf && (ch.GetOwner() == character.GetOwner() || (character.GetAlignment() != AlignmentEnum.neutral && ch.GetAlignment() == character.GetAlignment())));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class PitchFromTheMurderHolesAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;

            List<Character> enemies = character.hex.characters
                .Where(ch => ch != null && !ch.killed && ch.GetAlignment() != character.GetAlignment())
                .Distinct()
                .ToList();

            if (enemies.Count == 0) return false;

            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].ApplyStatusEffect(StatusEffectEnum.Burning, 1);
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Pitch from the Murder-Holes sets {enemies.Count} enemy unit(s) Burning.", Color.red);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.characters != null
                && character.hex.characters.Any(ch => ch != null && !ch.killed && ch.GetAlignment() != character.GetAlignment());
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class RaiseTheInnerBastionAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            PC pc = character.hex.GetPC();
            if (pc == null || pc.owner != character.GetOwner() || pc.fortSize >= FortSizeEnum.citadel) return false;
            pc.IncreaseFort();
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Raise the Inner Bastion increases the fortifications of {pc.pcName} by 1 level.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.GetPC() != null
                && character.hex.GetPC().owner == character.GetOwner()
                && character.hex.GetPC().fortSize < FortSizeEnum.citadel;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class PalantirGlimpseAction : EventAction
{
    private const int RevealCount = 10;

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
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

            Leader owner = character.GetOwner();
            Board board = Board.Instance;
            if (owner == null || board == null) return false;

            List<Hex> chosen = board.GetHexes()
                .Where(hex => hex != null && hex.terrainType == TerrainEnum.mountains)
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(RevealCount)
                .ToList();

            if (chosen.Count == 0) return false;

            owner.AddTemporarySeenHexes(chosen, 1);
            if (owner == Game.Instance?.player)
            {
                owner.RefreshVisibleHexesImmediate();
            }

            for (int i = 0; i < chosen.Count; i++)
            {
                chosen[i]?.RefreshVisibilityRendering();
            }

            chosen[0]?.LookAt();
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Signal Fires of Anorien reveals {chosen.Count} mountain hex(es) for 1 turn.", Color.magenta);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            Board board = Board.Instance;
            return character != null && character.GetOwner() != null && board != null && board.GetHexes().Any(hex => hex != null && hex.terrainType == TerrainEnum.mountains);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class WordsOfWardingAction : EventAction
{
    private const int Radius = 2;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            List<Hex> area = character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null)
                .Distinct()
                .ToList();

            if (area.Count == 0) return false;

            for (int i = 0; i < area.Count; i++)
            {
                area[i].RevealMapOnlyArea(0, false, false);
            }

            if (character.GetOwner() == Game.Instance?.player)
            {
                MinimapManager.RefreshMinimap();
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Wardens of the Rammas: {area.Count} hex(es) around this place are revealed as unseen for 1 turn.", new Color(0.82f, 0.76f, 0.6f));
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null
                && character.hex.GetHexesInRadius(Radius).Any(h => h != null);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class TheHiddenScriptAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Game game = Game.Instance;
            DeckManager deckManager = DeckManager.Instance;
            Board board = Board.Instance;
            if (game == null || deckManager == null || board == null) return false;

            Hex artifactHex = board.GetHexes().FirstOrDefault(h => h != null && h.hiddenObjects != null && h.hiddenObjects.Count > 0);
            if (artifactHex == null) return false;

            artifactHex.RevealArtifact();

            MessageDisplayNoUI.ShowMessage(character.hex, character, "The Hidden Script reveals an artifact site.", Color.magenta);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            Board board = Board.Instance;
            return character != null && board != null && board.GetHexes().Any(h => h != null && h.hiddenObjects != null && h.hiddenObjects.Count > 0);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class FarSpeakingThoughtAction : EventAction
{
    private const int MaxTargets = 2;
    private const int Radius = 3;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            Leader owner = character.GetOwner();
            if (owner == null) return false;

            List<Character> targets = character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.characters != null)
                .SelectMany(h => h.characters)
                .Where(ch => ch != null && !ch.killed && !IsAllied(character, ch))
                .OrderByDescending(ch => ch.HasStatusEffect(StatusEffectEnum.Hidden) ? 1 : 0)
                .ThenBy(_ => UnityEngine.Random.value)
                .Take(MaxTargets)
                .ToList();

            if (targets.Count == 0) return false;

            HashSet<Hex> revealed = new();
            int hiddenCleared = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].HasStatusEffect(StatusEffectEnum.Hidden))
                {
                    targets[i].ClearStatusEffect(StatusEffectEnum.Hidden);
                    hiddenCleared++;
                }
                if (targets[i].hex != null && revealed.Add(targets[i].hex))
                {
                    targets[i].hex.RevealArea(0, true, owner);
                }
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Far-Speaking Thought reveals {revealed.Count} enemy hex(es) and strips Hidden from {hiddenCleared} target(s).", Color.magenta);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.characters != null)
                .SelectMany(h => h.characters)
                .Any(ch => ch != null && !ch.killed && !IsAllied(character, ch));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class CounselByFirelightAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.GetOwner() == null) return false;

            character.Hide(2);

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Counsel by Firelight finds a quieter path: {character.characterName} is Hidden (2).", Color.magenta);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.GetOwner() != null && !character.IsHidden();
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class KingUnderTheMountainAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;

            Character target = character.hex.characters
                .Where(ch => ch != null && !ch.killed && ch.race == RacesEnum.Dwarf && IsAllied(character, ch))
                .OrderByDescending(ch => ch.hasActionedThisTurn ? 1 : 0)
                .ThenByDescending(ch => ch.GetCommander() + ch.GetAgent() + ch.GetEmmissary() + ch.GetMage())
                .FirstOrDefault();
            if (target == null) return false;

            target.hasActionedThisTurn = false;
            target.moved = 0;
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"King Under the Mountain readies {target.characterName} for one more labor this turn.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.characters != null
                && character.hex.characters.Any(ch => ch != null && !ch.killed && ch.race == RacesEnum.Dwarf && IsAllied(character, ch));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class IllNewsBeforeDawnAction : EventAction
{
    private const int Radius = 5;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            var nearby = character.hex.GetHexesInRadius(Radius).Where(h => h != null).ToList();
            Hex targetHex = nearby.FirstOrDefault(h => h.GetPC() != null && h.GetPC().owner != owner)
                ?? nearby.FirstOrDefault(h => h.characters != null && h.characters.Any(ch => ch != null && !ch.killed && !IsAllied(character, ch) && ch.IsArmyCommander()));
            if (targetHex == null) return false;

            targetHex.RevealArea(0, true, owner);
            if (targetHex.GetPC() != null && targetHex.GetPC().loyalty > 0)
            {
                targetHex.GetPC().DecreaseLoyalty(10, character);
                MessageDisplayNoUI.ShowMessage(character.hex, character, $"Ill News Before Dawn reveals {targetHex.GetPC().pcName} and lowers its loyalty.", Color.white);
            }
            else
            {
                MessageDisplayNoUI.ShowMessage(character.hex, character, $"Ill News Before Dawn exposes enemy forces before dawn.", Color.white);
            }
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.GetHexesInRadius(Radius).Any(h => h != null && ((h.GetPC() != null && h.GetPC().owner != character.GetOwner()) || (h.characters != null && h.characters.Any(ch => ch != null && !ch.killed && !IsAllied(character, ch) && ch.IsArmyCommander()))));
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class RidersSentInHasteAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            Board board = Board.Instance;
            if (board == null) return false;

            Hex farthestOwnedPcHex = board.GetHexes()
                .Where(h => h != null && h.GetPC() != null && h.GetPC().owner == character.GetOwner())
                .OrderByDescending(h => Vector2.Distance(character.hex.v2, h.v2))
                .FirstOrDefault();
            if (farthestOwnedPcHex == null) return false;

            board.MoveCharacterOneHex(character, character.hex, farthestOwnedPcHex, true, false);

            PC pc = farthestOwnedPcHex.GetPC();
            if (pc != null)
            {
                pc.IncreaseLoyalty(5, character);
                MessageDisplayNoUI.ShowMessage(farthestOwnedPcHex, character, $"Riders Sent in Haste carries {character.characterName} to {pc.pcName}, where loyalty rises by 5.", Color.white);
            }

            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            Board board = Board.Instance;
            return character != null && character.hex != null && board != null
                && board.GetHexes().Any(h => h != null && h.GetPC() != null && h.GetPC().owner == character.GetOwner());
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class CouncilInAShutteredHallAction : EventAction
{
    private const int Radius = 2;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Game game = Game.Instance;
            DeckManager deckManager = DeckManager.Instance;
            if (game == null || deckManager == null || game.player == null) return false;
            if (character.GetOwner() != game.player || !deckManager.HasDeckFor(game.player)) return false;

            CardData peek = deckManager.GetDrawPile(game.player).Take(3).FirstOrDefault(card => card != null);
            if (peek == null) return false;
            if (!deckManager.TryAddCardToHand(game.player, peek)) return false;

            PC pc = character.hex.GetPC();
            bool grantedLoyalty = false;
            if (pc != null && pc.owner == character.GetOwner() && character.hex.GetHexesInRadius(Radius).Any(h => h != null && h.characters != null && h.characters.Any(ch => ch != null && !ch.killed && !IsAllied(character, ch))))
            {
                pc.IncreaseLoyalty(5, character);
                grantedLoyalty = true;
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Council in a Shuttered Hall secures {peek.name}{(grantedLoyalty ? " and hardens local loyalty." : ".")}", Color.white);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            Game game = Game.Instance;
            DeckManager deckManager = DeckManager.Instance;
            return character != null && game != null && deckManager != null && game.player != null && character.GetOwner() == game.player
                && deckManager.HasDeckFor(game.player)
                && deckManager.GetDrawPile(game.player).Take(3).Any(card => card != null);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class WhiteTowerArsenalAction : EventAction
{
    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    private static List<Character> GetTargets(Character character)
    {
        if (character == null || character.hex == null || character.hex.characters == null) return new List<Character>();
        return character.hex.characters
            .Where(ch => ch != null && !ch.killed && ch.IsArmyCommander() && IsAllied(character, ch) && ch.GetArmy() != null)
            .Distinct()
            .ToList();
    }

    private static int GrantShielded(Army army)
    {
        if (army == null || army.troopAbilityGroups == null) return 0;

        int shieldedTroops = 0;
        foreach (ArmyTroopAbilityGroup group in army.troopAbilityGroups)
        {
            if (group == null || group.amount <= 0) continue;
            group.abilities ??= new List<ArmySpecialAbilityEnum>();
            if (group.abilities.Contains(ArmySpecialAbilityEnum.Shielded)) continue;
            group.abilities.Add(ArmySpecialAbilityEnum.Shielded);
            shieldedTroops += group.amount;
        }

        return shieldedTroops;
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

            List<Character> targets = GetTargets(character);
            if (targets.Count == 0) return false;

            int fortifiedTargets = 0;
            int shieldedTroops = 0;
            foreach (Character target in targets)
            {
                target.ApplyStatusEffect(StatusEffectEnum.Fortified, 1);
                fortifiedTargets++;
                shieldedTroops += GrantShielded(target.GetArmy());
                target.GetArmy()?.commander?.hex?.RedrawArmies();
            }

            character.hex?.RedrawCharacters();
            character.hex?.RedrawArmies();
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"White Tower Arsenal arms {fortifiedTargets} allied army commander(s) with Fortified and {shieldedTroops} shielded troop(s).", Color.cyan);
            return fortifiedTargets > 0;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return GetTargets(character).Count > 0;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class OsgiliathQuartermastersAction : EventAction
{
    private const int Radius = 2;

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    private static List<Character> GetTargets(Character character)
    {
        if (character == null || character.hex == null) return new List<Character>();

        return character.hex.GetHexesInRadius(Radius)
            .Where(h => h != null && h.characters != null)
            .SelectMany(h => h.characters)
            .Where(ch => ch != null && !ch.killed && ch.IsArmyCommander() && IsAllied(character, ch) && ch.GetArmy() != null)
            .Distinct()
            .ToList();
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

            List<Character> targets = GetTargets(character);
            if (targets.Count == 0) return false;

            Character target = targets
                .OrderByDescending(ch => ch.GetCommander() + ch.GetEmmissary())
                .ThenByDescending(ch => ch.GetArmy() != null ? ch.GetArmy().GetSize() : 0)
                .FirstOrDefault();
            if (target == null || target.GetArmy() == null) return false;

            Leader owner = target.GetOwner();
            owner?.AddTimber(1, false);
            owner?.AddIron(1, false);
            owner?.AddGold(1, false);
            target.GetArmy().AddXp(1, "Quartermasters");
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Osgiliath Quartermasters replenish {target.characterName}: +1 timber, +1 iron, +1 gold, and +1 army XP.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return GetTargets(character).Count > 0;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class RammasEchorAction : EventAction
{
    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;

            List<Character> allies = character.hex.characters
                .Where(ch => ch != null && !ch.killed && IsAllied(character, ch))
                .Distinct()
                .ToList();
            List<Character> enemies = character.hex.characters
                .Where(ch => ch != null && !ch.killed && !IsAllied(character, ch))
                .Distinct()
                .ToList();

            if (allies.Count == 0 && enemies.Count == 0) return false;

            for (int i = 0; i < allies.Count; i++)
            {
                allies[i].ApplyStatusEffect(StatusEffectEnum.Fortified, 1);
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].ApplyStatusEffect(StatusEffectEnum.Halted, 1);
            }

            character.hex.RedrawCharacters();
            character.hex.RedrawArmies();
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Rammas Echor braces the wall: {allies.Count} allied unit(s) gain Fortified and {enemies.Count} enemy unit(s) are Halted.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.characters != null
                && character.hex.characters.Any(ch => ch != null && !ch.killed);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class DolAmrothShipyardAction : EventAction
{
    private static bool IsSeaAdjacent(Hex hex)
    {
        if (hex == null) return false;
        return hex.GetHexesInRadius(1)
            .Any(h => h != null && h != hex && (h.terrainType == TerrainEnum.shore || h.terrainType == TerrainEnum.shallowWater || h.IsWaterTerrain()));
    }

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    private static List<Character> GetTargets(Character character)
    {
        if (character == null || character.hex == null || character.hex.characters == null) return new List<Character>();
        return character.hex.characters
            .Where(ch => ch != null && !ch.killed && ch.IsArmyCommander() && IsAllied(character, ch) && ch.GetArmy() != null)
            .Distinct()
            .ToList();
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || !IsSeaAdjacent(character.hex)) return false;

            Character target = GetTargets(character)
                .OrderByDescending(ch => ch.GetCommander() + ch.GetEmmissary())
                .ThenByDescending(ch => ch.GetArmy() != null ? ch.GetArmy().GetSize() : 0)
                .FirstOrDefault();
            if (target == null || target.GetArmy() == null) return false;

            target.GetArmy().Recruit(TroopsTypeEnum.ws, 1);
            target.GetOwner()?.AddTimber(1, false);
            target.hex?.RedrawCharacters();
            target.hex?.RedrawArmies();
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Dol Amroth Shipyard launches a warship for {target.characterName} and adds 1 timber to the stores.", Color.cyan);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && IsSeaAdjacent(character.hex) && GetTargets(character).Count > 0;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class GondorMusteringAction : EventAction
{
    private const int Radius = 2;

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    private static List<Character> GetTargets(Character character)
    {
        if (character == null || character.hex == null) return new List<Character>();
        return character.hex.GetHexesInRadius(Radius)
            .Where(h => h != null && h.characters != null)
            .SelectMany(h => h.characters)
            .Where(ch => ch != null && !ch.killed && ch.IsArmyCommander() && IsAllied(character, ch) && ch.GetArmy() != null)
            .Distinct()
            .ToList();
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

            Character target = GetTargets(character)
                .OrderByDescending(ch => ch.GetCommander() + ch.GetEmmissary())
                .ThenByDescending(ch => ch.GetArmy() != null ? ch.GetArmy().GetSize() : 0)
                .FirstOrDefault();
            if (target == null || target.GetArmy() == null) return false;

            target.GetArmy().Recruit(TroopsTypeEnum.li, 1);
            target.ApplyStatusEffect(StatusEffectEnum.Encouraged, 1);
            target.hex?.RedrawCharacters();
            target.hex?.RedrawArmies();
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Gondor Mustering adds 1 Light Infantry and Courage to {target.characterName}.", Color.green);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return GetTargets(character).Count > 0;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class PelargirShipCaptainAction : EventAction
{
    private static bool IsSeaAdjacent(Hex hex)
    {
        if (hex == null) return false;
        return hex.GetHexesInRadius(1)
            .Any(h => h != null && h != hex && (h.terrainType == TerrainEnum.shore || h.terrainType == TerrainEnum.shallowWater || h.IsWaterTerrain()));
    }

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    private static List<Character> GetTargets(Character character)
    {
        if (character == null || character.hex == null || character.hex.characters == null) return new List<Character>();
        return character.hex.characters
            .Where(ch => ch != null && !ch.killed && ch.IsArmyCommander() && IsAllied(character, ch) && ch.GetArmy() != null)
            .Distinct()
            .ToList();
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || !IsSeaAdjacent(character.hex)) return false;

            Character target = GetTargets(character)
                .OrderByDescending(ch => (ch.GetCommander() + ch.GetEmmissary(), ch.GetArmy() != null ? ch.GetArmy().GetSize() : 0))
                .FirstOrDefault();
            if (target == null || target.GetArmy() == null) return false;

            target.GetArmy().Recruit(TroopsTypeEnum.ws, 1);
            target.ApplyStatusEffect(StatusEffectEnum.Haste, 1);
            target.hex?.RedrawCharacters();
            target.hex?.RedrawArmies();
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Pelargir Ship Captain adds 1 Warship and Haste to {target.characterName}.", Color.cyan);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && IsSeaAdjacent(character.hex) && GetTargets(character).Count > 0;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}


public class WisdomOfTheAges : EventAction
{
    private const int HealAmount = 20;

    private static bool IsAllied(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (target.GetOwner() == source.GetOwner()) return true;
        return source.GetAlignment() != AlignmentEnum.neutral
            && target.GetAlignment() == source.GetAlignment()
            && target.GetAlignment() != AlignmentEnum.neutral;
    }

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;
            return character.hex.characters.Any(ch => ch != null && !ch.killed && IsAllied(character, ch));
        };

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;

            List<Character> allies = character.hex.characters
                .Where(ch => ch != null && !ch.killed && IsAllied(character, ch))
                .Distinct()
                .ToList();
            if (allies.Count == 0) return false;

            Character target = allies.OrderByDescending(x => 100 - x.health).FirstOrDefault();
            if (target == null) return false;

            target.Heal(HealAmount);
            string bonusText = "";
            if (target.GetMage() > 0 && target.GetOwner() != null)
            {
                target.GetOwner().AddGold(1);
                bonusText = " and +1 <sprite name=\"gold\">";
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Wisdom of the Ages heals {target.characterName} for {HealAmount} HP{bonusText}.", Color.green);
            return true;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class RingsOfLore : EventAction
{
    private static readonly string[] LoreBearers = { "Galadriel", "Elrond", "Gandalf" };

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Leader owner = character.GetOwner();
            Board board = Board.Instance;
            if (owner == null || board == null || board.hexes == null) return false;

            List<Character> loreBearers = UnityEngine.Object.FindObjectsByType<Character>(FindObjectsSortMode.None)
                .Where(ch => ch != null && !ch.killed && LoreBearers.Contains(ch.characterName))
                .Distinct()
                .ToList();

            List<Hex> targetHexes = loreBearers
                .Select(ch => ch.hex)
                .Where(h => h != null && !h.IsScoutedBy(owner))
                .Distinct()
                .ToList();

            if (targetHexes.Count == 0)
            {
                MessageDisplayNoUI.ShowMessage(
                    character.hex,
                    character,
                    "Rings of Lore: no lore-bearers (Galadriel, Elrond, or Gandalf) are hidden from view.",
                    new UnityEngine.Color(0.55f, 0.42f, 0.72f));
                return true;
            }

            owner.AddTemporarySeenHexes(targetHexes);

            if (owner == Game.Instance?.player)
            {
                owner.RefreshVisibleHexesImmediate();
            }

            for (int i = 0; i < targetHexes.Count; i++)
            {
                targetHexes[i]?.RefreshVisibilityRendering();
            }

            targetHexes[0]?.LookAt();
            MessageDisplayNoUI.ShowMessage(
                character.hex,
                character,
                $"Rings of Lore reveals {targetHexes.Count} hex(es) where lore-bearers stand.",
                new UnityEngine.Color(0.55f, 0.42f, 0.72f));

            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null) return false;

            Leader owner = character.GetOwner();
            Board board = Board.Instance;
            if (owner == null || board == null || board.hexes == null) return false;

            return true;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class VoiceOfAuthority : EventAction
{
    private const int LoyaltyGain = 15;
    private const int GoldGain = 1;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;

            PC pc = character.hex.GetPC();
            if (pc == null) return false;

            pc.IncreaseLoyalty(LoyaltyGain, character);
            character.GetOwner()?.AddGold(GoldGain);
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Voice of Authority grants {pc.pcName} +{LoyaltyGain} loyalty and +{GoldGain} <sprite name=\"gold\">.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null && character.hex.GetPC() != null && character.hex.GetPC().loyalty < 100;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class CounselOfTheWise : EventAction
{
    private static readonly ProducesEnum[] ResourceOptions =
    {
        ProducesEnum.gold, ProducesEnum.timber, ProducesEnum.iron, ProducesEnum.leather, ProducesEnum.mounts
    };
    private const int ResourceAmount = 3;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            Leader owner = character?.GetOwner();
            if (owner == null) return false;

            List<ProducesEnum> sampled = ResourceOptions.OrderBy(_ => UnityEngine.Random.value).Take(3).ToList();
            ProducesEnum chosen = sampled.OrderBy(r => owner.GetResourceAmount(r)).First();

            owner.AddResource(chosen, ResourceAmount);
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Counsel of the Wise foresees scarcity: +{ResourceAmount} {chosen}.", Color.white);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character?.GetOwner() != null;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class TheOldMillAction : EventAction
{
    private const int LoyaltyLoss = 10;
    private const int ResourceDrain = 3;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            PC pc = character.hex.GetPC();
            string loyaltyMsg = "";
            string drainMsg = "";

            if (pc != null)
            {
                if (pc.loyalty > 0)
                {
                    pc.DecreaseLoyalty(LoyaltyLoss, character);
                    loyaltyMsg = $" {pc.pcName} loses {LoyaltyLoss} loyalty.";
                }

                Leader pcOwner = pc.owner;
                if (pcOwner != null && pcOwner != owner)
                {
                    int d = ResourceDrain;
                    pcOwner.RemoveLeather(Mathf.Min(d, pcOwner.leatherAmount), false);
                    pcOwner.RemoveMounts(Mathf.Min(d, pcOwner.mountsAmount), false);
                    pcOwner.RemoveTimber(Mathf.Min(d, pcOwner.timberAmount), false);
                    pcOwner.RemoveIron(Mathf.Min(d, pcOwner.ironAmount), false);
                    pcOwner.RemoveSteel(Mathf.Min(d, pcOwner.steelAmount), false);
                    pcOwner.RemoveMithril(Mathf.Min(d, pcOwner.mithrilAmount), false);
                    pcOwner.RemoveGold(Mathf.Min(d, pcOwner.goldAmount), false);
                    drainMsg = $" {pcOwner.characterName} loses up to {d} of each resource.";
                }
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character,
                $"Destroy the Mill.{loyaltyMsg}{drainMsg}",
                Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class TheBattleOfBywaterAction : EventAction
{
    private const int Radius = 3;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            List<Army> targets = character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.armies != null)
                .SelectMany(h => h.armies)
                .Where(a => a != null && !a.killed && a.commander != null && a.commander.GetOwner() != owner && a.li > 0)
                .Distinct()
                .ToList();

            if (targets.Count == 0) return false;

            foreach (Army target in targets)
            {
                Hex targetHex = target.commander?.hex;
                target.Killed(owner);
                targetHex?.RedrawArmies();
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"The Battle of Bywater routs {targets.Count} enemy light infantry force(s) in radius {Radius}.", Color.green);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            return character.hex.GetHexesInRadius(Radius)
                .Where(h => h != null && h.armies != null)
                .SelectMany(h => h.armies)
                .Any(a => a != null && !a.killed && a.commander != null && a.commander.GetOwner() != owner && a.li > 0);
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class ImprisonmentAction : EventAction
{
    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;
            Leader owner = character.GetOwner();

            Character target = character.hex.characters
                .Where(ch => ch != null && !ch.killed && ch.GetOwner() != owner
                    && !(ch is Leader) && !ch.IsKidnapped() && !ch.IsArmyCommander())
                .OrderBy(ch => ch.GetTotalSkillLevel())
                .FirstOrDefault();

            if (target == null) return false;

            Leader originalOwner = target.GetOwner();
            if (originalOwner == null) return false;

            Hex previousHex = target.hex;
            if (previousHex != null && previousHex.characters.Contains(target))
                previousHex.characters.Remove(target);

            if (character.kidnappedCharacters == null) character.kidnappedCharacters = new System.Collections.Generic.List<Character.KidnappedCharacterRecord>();
            target.kidnappedBy = character;
            target.kidnappedOriginalOwner = originalOwner;
            target.hex = character.hex;
            target.hasActionedThisTurn = true;
            target.moved = target.GetMaxMovement();
            if (target.hex != null && !target.hex.characters.Contains(target))
                target.hex.characters.Add(target);

            character.kidnappedCharacters.Add(new Character.KidnappedCharacterRecord { character = target, originalOwner = originalOwner });

            previousHex?.RedrawCharacters();
            character.RefreshKidnappedCharactersPosition();

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Imprisonment: {target.characterName} dragged to the Lockholes!", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;
            Leader owner = character.GetOwner();

            return character.hex.characters.Any(ch => ch != null && !ch.killed && ch.GetOwner() != owner
                && !(ch is Leader) && !ch.IsKidnapped() && !ch.IsArmyCommander());
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class IndustrializationAction : EventAction
{
    private const int Radius = 5;
    private const int IronGain = 5;
    private const int SteelGain = 5;
    private const int LoyaltyLoss = 15;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            owner.AddIron(IronGain, false);
            owner.AddSteel(SteelGain, false);

            int pcCount = 0;
            foreach (Hex h in character.hex.GetHexesInRadius(Radius))
            {
                PC pc = h?.GetPC();
                if (pc == null || pc.loyalty <= 0) continue;
                pc.DecreaseLoyalty(LoyaltyLoss, character);
                pcCount++;
            }

            MessageDisplayNoUI.ShowMessage(character.hex, character,
                $"Industrialization yields +{IronGain} <sprite name=\"iron\">, +{SteelGain} <sprite name=\"steel\"> and lowers {pcCount} PC(s) loyalty by {LoyaltyLoss}.",
                Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null && character.hex != null;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class LothosPurseAction : EventAction
{
    private const int GoldGain = 15;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            owner.AddGold(GoldGain);
            owner.leatherAmount = 0;
            owner.timberAmount = 0;

            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Lotho's Purse: +{GoldGain} <sprite name=\"gold\">, leather and timber plundered to 0.", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class PipeweedMonopolyAction : EventAction
{
    private const int GoldGain = 5;
    private static readonly string[] DrawCardNames = { "Lured by Halflings' Leaf", "TheLureOfTheSenses" };

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null) return false;

            Game game = Game.Instance;
            DeckManager deckManager = DeckManager.Instance;
            Leader owner = character.GetOwner();
            if (owner == null) return false;

            owner.AddGold(GoldGain);

            int drawn = 0;
            if (game != null && deckManager != null && owner is PlayableLeader playerLeader && playerLeader == game.player)
            {
                foreach (string cardName in DrawCardNames)
                {
                    CardData card = deckManager.FindCardByNameForLeader(playerLeader, cardName);
                    if (card != null && deckManager.TryAddCardToHand(playerLeader, card)) drawn++;
                }
            }

            string drawnText = drawn > 0 ? $" and draws {drawn} card(s)." : ".";
            MessageDisplayNoUI.ShowMessage(character.hex, character, $"Pipe-weed Monopoly: +{GoldGain} <sprite name=\"gold\">{drawnText}", Color.yellow);
            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            return character != null;
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}

public class GrimasKnifeAction : EventAction
{
    private const int WoundDamage = 50;

    public override void Initialize(Character c, Func<Character, bool> condition = null, Func<Character, bool> effect = null, Func<Character, System.Threading.Tasks.Task<bool>> asyncEffect = null)
    {
        var originalEffect = effect;
        var originalCondition = condition;
        var originalAsyncEffect = asyncEffect;

        effect = (character) =>
        {
            if (originalEffect != null && !originalEffect(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;
            Leader owner = character.GetOwner();

            Character target = character.hex.characters
                .Where(ch => ch != null && !ch.killed && ch.GetOwner() != owner && !(ch is Leader) && !ch.IsArmyCommander())
                .OrderBy(ch => ch.GetTotalSkillLevel())
                .FirstOrDefault();

            if (target == null) return false;

            int roll = UnityEngine.Random.Range(0, 100);

            if (roll < 25)
            {
                MessageDisplayNoUI.ShowMessage(character.hex, character,
                    $"Grima's Knife: rolled {roll} — the blade turns! {character.characterName} is assassinated!", Color.red);
                character.Killed(target.GetOwner());
            }
            else if (roll < 50)
            {
                MessageDisplayNoUI.ShowMessage(character.hex, character,
                    $"Grima's Knife: rolled {roll} — the blade slips! {character.characterName} is wounded.", Color.red);
                character.Wounded(target.GetOwner(), WoundDamage);
                character.hex?.RedrawCharacters();
            }
            else if (roll < 75)
            {
                MessageDisplayNoUI.ShowMessage(character.hex, character,
                    $"Grima's Knife: rolled {roll} — a glancing blow! {target.characterName} is wounded.", Color.yellow);
                target.Wounded(owner, WoundDamage);
                target.hex?.RedrawCharacters();
            }
            else
            {
                MessageDisplayNoUI.ShowMessage(character.hex, character,
                    $"Grima's Knife: rolled {roll} — a killing stroke! {target.characterName} is slain!", Color.green);
                target.Killed(owner);
            }

            return true;
        };

        condition = (character) =>
        {
            if (originalCondition != null && !originalCondition(character)) return false;
            if (character == null || character.hex == null || character.hex.characters == null) return false;
            Leader owner = character.GetOwner();
            return character.hex.characters.Any(ch => ch != null && !ch.killed && ch.GetOwner() != owner
                && !(ch is Leader) && !ch.IsArmyCommander());
        };

        asyncEffect = async (character) =>
        {
            if (originalAsyncEffect != null && !await originalAsyncEffect(character)) return false;
            return true;
        };

        base.Initialize(c, condition, effect, asyncEffect);
    }
}
