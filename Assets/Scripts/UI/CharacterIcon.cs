using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class CharacterIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public CanvasGroup deadCanvasGroup;
    public Image image;
    public Image healthBar;
    protected Character character;
    protected Board board;
    protected SelectedCharacterIcon selectedCharacterIcon;

    

    public virtual void Initialize(Character character)
    {
        board = Board.Instance;
        selectedCharacterIcon = FindAnyObjectByType<SelectedCharacterIcon>();
        SetCharacter(character);
    }

    public void OnClick()
    {
        if (character == null || character.killed) return;

        if (board == null) board = Board.Instance;
        if (board != null)
        {
            Sounds.Instance?.PlayUiClick();
            board.SelectCharacter(character);
        }
        else
        {
            if (selectedCharacterIcon == null) selectedCharacterIcon = FindAnyObjectByType<SelectedCharacterIcon>();
            selectedCharacterIcon?.Refresh(character);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (BoardNavigator.IsNavigationInputLocked()) return;
        if (character == null || character.killed) return;
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
    }

    public void OnPointerExit(PointerEventData eventData)
    {
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

    public void Refresh(Character updatedCharacter)
    {
        SetCharacter(updatedCharacter);
    }

    public Character GetCharacter()
    {
        return character;
    }

    protected void SetCharacter(Character newCharacter)
    {
        character = newCharacter;
        if (character != null)
        {
            if (!string.IsNullOrWhiteSpace(character.characterName))
            {
                gameObject.name = character.characterName;
            }

            var illustrations = FindAnyObjectByType<Illustrations>();
            if (illustrations != null)
            {
                string lookupName = !string.IsNullOrWhiteSpace(character.illustrationName) ? character.illustrationName : character.characterName;
                image.sprite = illustrations.GetIllustrationByName(lookupName);
            }
            if (healthBar != null) healthBar.fillAmount = character.killed ? 0f : Mathf.Clamp01(character.health / 100f);
        }

        RefreshDeathState();
    }

    private void RefreshDeathState()
    {
        if (deadCanvasGroup == null) return;
        float targetAlpha = character != null && character.killed ? 1f : 0f;
        if (!Mathf.Approximately(deadCanvasGroup.alpha, targetAlpha))
        {
            deadCanvasGroup.alpha = targetAlpha;
        }
    }
}
