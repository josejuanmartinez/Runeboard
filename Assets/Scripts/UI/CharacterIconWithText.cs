using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class CharacterIconWithText: CharacterIcon, IPointerEnterHandler, IPointerExitHandler
{
    public TextMeshProUGUI characterText;

    // Icons rest slightly darkened so the row reads as background; the hovered one
    // brightens to full color (see OnPointerEnter/Exit).
    private static readonly Color IdleTint = new(0.72f, 0.72f, 0.72f, 1f);

    private void OnEnable()
    {
        ApplyHoverTint(false);
    }

    private void ApplyHoverTint(bool hovered)
    {
        if (image != null) image.color = hovered ? Color.white : IdleTint;
    }

    public override void Initialize(Character character)
    {
        base.Initialize(character);
        if (characterText != null && character != null)
            characterText.text = character.characterName;
        ApplyHoverTint(false);
    }

    public void Initialize(Character character, string text)
    {
        base.Initialize(character);
        SetCharacterWithText(character, text);
    }

    new public void OnClick()
    {
        if (character == null || character.killed) return;

        if (board == null) board = Board.Instance;
        if (board != null)
        {
            Sounds.Instance?.PlayUiClick();
            board.SelectCharacter(character);
        }
    }

    new public void OnPointerEnter(PointerEventData eventData)
    {
        if (BoardNavigator.IsNavigationInputLocked()) return;
        if (character == null || character.killed) return;
        ApplyHoverTint(true);
        CursorManager.Instance?.SetClickableCursor();
        board ??= Board.Instance;
        if (board != null && board.selectedCharacter == character) return;
        Sounds.Instance?.PlayUiHover();

        if (selectedCharacterIcon == null)
        {
            Layout layout = FindAnyObjectByType<Layout>();
            selectedCharacterIcon = layout != null ? layout.GetSelectedCharacterIcon() : null;
        }
        if (selectedCharacterIcon == null) return;

        selectedCharacterIcon.RefreshForHover(character);
        CardCenterPreview.Instance?.ShowPreviewForCharacter(character);
    }

    new public void OnPointerExit(PointerEventData eventData)
    {
        ApplyHoverTint(false);
        CursorManager.Instance?.SetDefaultCursor();
        CardCenterPreview.Instance?.HidePreview();
        if (selectedCharacterIcon == null)
        {
            Layout layout = FindAnyObjectByType<Layout>();
            selectedCharacterIcon = layout != null ? layout.GetSelectedCharacterIcon() : null;
        }
        if (selectedCharacterIcon == null) return;

        board ??= Board.Instance;
        if (board != null && board.selectedCharacter != null)
        {
            selectedCharacterIcon.Refresh(board.selectedCharacter);
        }
        else
        {
            selectedCharacterIcon.Hide();
        }
    }

    public void Refresh(Character updatedCharacter, string text = null)
    {
        string label = text ?? (updatedCharacter != null ? updatedCharacter.characterName : string.Empty);
        SetCharacterWithText(updatedCharacter, label);
    }

    private void SetCharacterWithText(Character newCharacter, string text)
    {
        SetCharacter(newCharacter);
        characterText.text = text;
    }
}
