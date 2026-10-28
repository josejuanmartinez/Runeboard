using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bringing a character into a leader's service from their Character card, at the card's home
/// PC. Shared by the human's card play (Card) and the AI's campaign play (CampaignAI), so both
/// recruit the same way.
/// </summary>
public static class CharacterRecruitment
{
    /// <summary>Spawns the card's character for the leader at the hex and announces it; null on failure.</summary>
    public static Character Spawn(Leader leader, Hex hex, CardData card)
    {
        if (leader == null || hex == null || card == null) return null;
        if (!leader.HasCharacterSlot())
        {
            if (leader == Game.Instance?.player) MessageDisplay.ShowMessage("No character slots available.", Color.red);
            return null;
        }

        CharacterInstantiator instantiator = Object.FindAnyObjectByType<CharacterInstantiator>();
        if (instantiator == null)
        {
            Debug.LogWarning($"[Recruit] '{card.name}': no CharacterInstantiator found in scene.");
            return null;
        }

        BiomeConfig config = new()
        {
            characterName = card.name,
            alignment = (AlignmentEnum)card.alignment,
            race = card.race,
            sex = card.sex,
            commander = card.commander,
            agent = card.agent,
            emmissary = card.emmissary,
            mage = card.mage,
            artifacts = card.artifacts != null ? new List<string>(card.artifacts) : new List<string>()
        };

        Character recruited = instantiator.InstantiateCharacter(leader, hex, config);
        if (recruited == null) return null;

        Game game = Game.Instance;
        recruited.startingCharacter = false;
        recruited.characterGroup = card.characterGroup;
        recruited.hasActionedThisTurn = true;
        recruited.isPlayerControlled = game != null && leader == game.player;
        leader.TryConsumeCharacterSlot();
        hex.RedrawCharacters();

        string joinMessage = $"{card.name} has joined {leader.characterName}.";
        MessageDisplayNoUI.ShowMessage(hex, recruited, joinMessage, Color.green, recordRumour: false);
        RumoursManager.AddRumour(new Rumour
        {
            leader = leader,
            character = recruited,
            characterName = card.name,
            rumour = joinMessage,
            v2 = hex.v2
        }, isPublic: false);
        return recruited;
    }
}
