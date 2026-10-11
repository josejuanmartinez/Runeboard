using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

public class CharacterIcons : MonoBehaviour
{
    public GameObject characterIconPrefab;
    public List<CharacterIcon> characterIcons = new();
    public Transform gridLayout;

    public void BuildIconsForPlayer(PlayableLeader player)
    {
        ClearIcons();
        if (player == null || player.controlledCharacters == null) return;

        Transform parent = gridLayout != null ? gridLayout : transform;
        foreach (Character character in player.controlledCharacters)
        {
            if (character == null) continue;

            GameObject iconGO = Instantiate(characterIconPrefab, parent);
            iconGO.name = character.characterName;

            CharacterIcon icon = iconGO.GetComponent<CharacterIcon>();
            if (icon != null)
            {
                icon.Initialize(character);
                characterIcons.Add(icon);
            }
        }
    }

    public bool RefreshIcon(Character character)
    {
        if (character == null) return false;
        CharacterIcon icon = characterIcons.Find(x => x != null && x.GetCharacter() == character);
        if (icon != null)
        {
            icon.Refresh(character);
            return true;
        }
        return false;
    }

    public static void RefreshForHumanPlayerOf(Leader leader)
    {
        Game game = Game.Instance;
        if (game == null || game.player == null || leader == null) return;

        PlayableLeader humanPlayer = game.player;
        Leader owningLeader = leader.GetOwner();
        if (owningLeader != humanPlayer && leader != humanPlayer) return;

        CharacterIcons icons = FindAnyObjectByType<CharacterIcons>();
        icons?.BuildIconsForPlayer(humanPlayer);
    }

    public static void RefreshForHumanPlayerCharacter(Character character)
    {
        if (character == null) return;
        Game game = Game.Instance;
        if (game == null || game.player == null) return;
        Leader owner = character.GetOwner();
        if (owner != game.player) return;

        CharacterIcons icons = FindAnyObjectByType<CharacterIcons>();
        if (icons == null) return;

        if (!icons.RefreshIcon(character))
        {
            icons.BuildIconsForPlayer(game.player);
        }
    }

    public void BuildIconsForPlayerExcluding(PlayableLeader player, Character exclude)
    {
        ClearIcons();
        if (player == null || player.controlledCharacters == null) return;

        Transform parent = gridLayout != null ? gridLayout : transform;
        foreach (Character character in player.controlledCharacters)
        {
            if (character == null || character == exclude) continue;

            GameObject iconGO = Instantiate(characterIconPrefab, parent);
            iconGO.name = character.characterName;

            CharacterIcon icon = iconGO.GetComponent<CharacterIcon>();
            if (icon != null)
            {
                icon.Initialize(character);
                characterIcons.Add(icon);
            }
        }
    }

    public void Clear()
    {
        ClearIcons();
    }

    private void ClearIcons()
    {
        Transform parent = gridLayout != null ? gridLayout : transform;
        foreach (Transform child in parent)
        {
            Destroy(child.gameObject);
        }

        characterIcons.Clear();
    }
}
