using System.Collections.Generic;
using UnityEngine;

// When a character is selected, sequentially pulses a fluid HexPathRenderer route from the
// character's hex to each reachable hex that has a playable opportunity (situation) card -
// one route at a time (see HexPathRenderer.StartOpportunityHintCycle) rather than marking every
// candidate hex at once.
public static class OpportunityHexHinter
{
    private static HexPathRenderer activePathRenderer;

    public static bool HintsEnabled { get; private set; } = true;

    // Call with false to suppress opportunity-card movement hints (e.g. while some other
    // sequence owns the board's attention) and with true to resume showing them again
    // immediately for whatever character is currently selected, without waiting on the next
    // selection-change event.
    public static void SetHintsEnabled(bool enabled)
    {
        HintsEnabled = enabled;
        if (enabled)
        {
            Refresh(Board.Instance?.selectedCharacter);
        }
        else
        {
            ClearAll();
        }
    }

    public static void Refresh(Character character)
    {
        ClearAll();
        if (!HintsEnabled) return;
        if (character == null || character.killed || character.hex == null) return;

        Game game = Game.Instance;
        if (game == null || game.player == null || character.GetOwner() != game.player) return;
        // Suppress while an AI is actually making the moves for this character — either
        // another leader's turn (ownership check above already covers that in practice) or
        // the human's own turn being resolved by Autoplay. Hints pulsing toward opportunity
        // hexes while nobody's hand is on the wheel just reads as visual noise.
        if (!game.IsPlayerCurrentlyPlaying() || game.IsPlayerAutoplayEnabledFor(character.GetOwner())) return;
        if (character.GetOwner() is not PlayableLeader leader) return;

        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        HexPathRenderer pathRenderer = Object.FindAnyObjectByType<HexPathRenderer>();
        if (deckManager == null || pathRenderer == null) return;

        var targets = new List<Vector2Int>();
        foreach (Hex hex in pathRenderer.FindAllHexesInRemainingRange(character))
        {
            if (hex == null || hex.v2 == character.hex.v2) continue;
            // Existence-only: hint the hex whenever an opportunity card is there, even if its
            // requirements aren't currently met — the actual card offer (GetSituationCards)
            // is where unmet requirements get filtered out, once the character has arrived.
            if (deckManager.HasOpportunityCardsAtHex(leader, character, hex))
            {
                targets.Add(hex.v2);
            }
        }

        if (targets.Count == 0) return;

        activePathRenderer = pathRenderer;
        pathRenderer.StartOpportunityHintCycle(character, targets);
    }

    public static void ClearAll()
    {
        activePathRenderer?.StopOpportunityHintCycle();
        activePathRenderer = null;
    }
}
