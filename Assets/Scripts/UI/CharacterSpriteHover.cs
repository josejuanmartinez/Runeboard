using System.Collections;
using UnityEngine;

public class CharacterSpriteHover : MonoBehaviour
{
    public Hex hex;
    [Tooltip("Seconds the cursor must stay on a character's sprite, uninterrupted, before its card preview appears.")]
    [SerializeField] private float cardPreviewHoverDelay = 5f;
    
    private SelectedCharacterIcon selectedIcon;
    private Board board;
    private bool isPreviewing;
    private Character previewedCharacter;
    private Hex previewedHex;
    private Coroutine cardPreviewCoroutine;

    // OnMouseEnter/Exit are physics-raycast events that Unity can silently fail to
    // pair up (pointer teleports on alt-tab/focus loss, this object gets repositioned
    // or its content swapped without ever being disabled, etc.), which could leave a
    // hover state stuck on with no matching exit. isHovered + the pointer-overlap
    // check in Update() self-heal that within a frame instead of trusting the event pairing.
    private bool isHovered;
    private BoxCollider2D hoverCollider;

    private void Awake()
    {
        board = Board.Instance;
        selectedIcon = FindFirstObjectByType<SelectedCharacterIcon>();
        // OnMouseEnter/Exit only fire on the GameObject that owns the collider, so this
        // script (and hoverCollider) must stay on the HexCharacterLayer root, not the
        // "character" child sprite.
        hoverCollider = GetComponent<BoxCollider2D>();
    }

    private bool IsStillUnderPointer()
    {
        if (hoverCollider == null || !hoverCollider.enabled) return false;
        Camera cam = Camera.main;
        if (cam == null) return false;
        float depth = cam.WorldToScreenPoint(hoverCollider.bounds.center).z;
        Vector3 mouseWorld = cam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, depth));
        return hoverCollider.OverlapPoint(mouseWorld);
    }

    private void OnMouseEnter()
    {
        if (hex == null || hex.characterSpriteRenderer == null) return;
        if (hex.characterSpriteRenderer.sprite == null) return;
        if (!hex.TryGetKnownCharacterForIcon(out Character character)) return;
        if (BoardNavigator.IsPointerOverVisibleUIElement()) return;
        Sounds.Instance?.PlayUiHover();
        if (BoardNavigator.IsNavigationInputLocked()) return;

        // Every known character loses the unhovered dim tint while hovered, regardless of
        // nation. Keep the clickable cursor restricted to selectable (player-controlled)
        // characters so previewing another nation does not imply it can be selected.
        hex.SetCharacterHovered(true);
        if (character.isPlayerControlled)
        {
            hex.GetCharacterAnimationController()?.SetHoverCursor(true);
        }
        hex.Hover();
        isHovered = true;

        board ??= Board.Instance;
        bool isSelected = board != null && board.selectedCharacter == character;
        bool isScouted = hex.IsScouted();

        // Set before the branches below so it's committed as soon as the visual hover
        // (scale + hex.Hover()) is, even if an early return skips the preview/icon work —
        // otherwise ClearPreview's UI-covers-sprite watchdog in Update() never armed for
        // this hover at all.
        isPreviewing = true;
        previewedCharacter = character;
        previewedHex = hex;

        // The already-selected character's info sits permanently in SelectedCharacterIcon
        // already, so skip the transient hover-text overwrite for it — but the card preview
        // below is independent of that panel and should still show on hover either way.
        if (!isSelected)
        {
            if (!hex.TryGetPreviewTextForCharacter(character, out string hoverText)) return;
            if (selectedIcon == null)
            {
                Layout layout = FindFirstObjectByType<Layout>();
                selectedIcon = layout != null ? layout.GetSelectedCharacterIcon() : null;
            }
            if (selectedIcon == null) return;

            selectedIcon.RefreshHoverPreview(character, hoverText, isScouted, isScouted);
        }

        if (cardPreviewCoroutine != null) StopCoroutine(cardPreviewCoroutine);
        cardPreviewCoroutine = StartCoroutine(ShowCardPreviewAfterDelay(character, isScouted));
    }

    // Only pops the card preview after the cursor has sat on this character's sprite,
    // uninterrupted, for cardPreviewHoverDelay seconds — OnMouseExit/OnDisable (via
    // ClearPreview) cancel this if the cursor leaves first.
    private IEnumerator ShowCardPreviewAfterDelay(Character character, bool includeArmyCards)
    {
        yield return new WaitForSeconds(cardPreviewHoverDelay);
        cardPreviewCoroutine = null;
        CardCenterPreview.Instance?.ShowPreviewForCharacter(character, includeArmyCards: includeArmyCards);
    }

    private void Update()
    {
        // Self-heals any missed OnMouseExit (pointer teleport on alt-tab/focus loss, this
        // object's content/position changing without ever being disabled, etc.) by checking
        // the pointer against our own collider directly, rather than trusting Unity's
        // enter/exit event pairing to always fire correctly.
        if (isHovered && !IsStillUnderPointer())
        {
            HandlePointerLeft();
            return;
        }

        if (!isPreviewing)
        {
            return;
        }

        // A UI panel (e.g. SelectedCharacterIcon opened by a click) can appear over this sprite
        // after the hover already started, without the mouse ever moving — OnMouseEnter alone
        // wouldn't catch that, so keep checking every frame while the preview is up. Same
        // teardown as OnMouseExit, since as far as this sprite is concerned the pointer left.
        if (BoardNavigator.IsPointerOverVisibleUIElement())
        {
            HandlePointerLeft();
            return;
        }

        ValidatePreviewStillValid();
    }

    // Character selection only happens by clicking directly on a character's own
    // sprite (this collider) — clicking elsewhere on the hex does nothing (the hex
    // tile itself has no click handler). Only selectable (yours) characters respond.
    private void OnMouseDown()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (BoardNavigator.IsPointerOverVisibleUIElement()) return;
        if (hex == null || !hex.TryGetKnownCharacterForIcon(out Character character)) return;
        if (BoardNavigator.IsNavigationInputLocked() || PopupManager.IsShowing || !character.isPlayerControlled)
        {
            Sounds.Instance?.PlayNegative();
            return;
        }

        board ??= Board.Instance;
        if (board == null) return;

        Sounds.Instance?.PlayUiClick();
        // SelectHex itself now drives the situation-card bloom for whichever character ends up
        // selected (restore-if-dismissed, or a fresh check) — see
        // SituationCardsUI.RefreshBloomForCharacterSelection.
        board.SelectHex(hex.v2, characterToSelect: character);
    }

    private void OnMouseExit()
    {
        HandlePointerLeft();
    }

    private void OnDisable()
    {
        HandlePointerLeft();
    }

    private void HandlePointerLeft()
    {
        isHovered = false;
        if (hex != null)
        {
            hex.SetCharacterHovered(false);
            hex.GetCharacterAnimationController()?.SetHoverCursor(false);
        }
        ClearPreview();
    }

    private void ValidatePreviewStillValid()
    {
        if (previewedHex == null || previewedHex.characterSpriteRenderer == null)
        {
            ClearPreview();
            return;
        }

        if (previewedCharacter == null || previewedCharacter.hex != previewedHex)
        {
            ClearPreview();
            return;
        }

        if (previewedHex.characterSpriteRenderer.sprite == null ||
            !previewedHex.TryGetKnownCharacterForIcon(out Character currentCharacter) ||
            currentCharacter != previewedCharacter)
        {
            ClearPreview();
        }
    }

    private void ClearPreview()
    {
        // Only tear down/restore when THIS component actually put up a preview — otherwise
        // every mouse-exit (including ones where OnMouseEnter no-opped, e.g. hovering the
        // already-selected character's own sprite) redundantly re-touches the shared
        // SelectedCharacterIcon. (selectedIcon is populated lazily and almost never null,
        // so the old `selectedIcon == null` half of this guard essentially never fired.)
        if (!isPreviewing)
        {
            return;
        }

        if (cardPreviewCoroutine != null) { StopCoroutine(cardPreviewCoroutine); cardPreviewCoroutine = null; }

        isPreviewing = false;
        previewedCharacter = null;
        previewedHex = null;
        CardCenterPreview.Instance?.HidePreview();

        if (selectedIcon == null)
        {
            Layout layout = FindFirstObjectByType<Layout>();
            selectedIcon = layout != null ? layout.GetSelectedCharacterIcon() : null;
        }
        if (selectedIcon == null) return;

        board ??= Board.Instance;
        if (board != null && board.selectedCharacter != null)
        {
            selectedIcon.Refresh(board.selectedCharacter);
        }
        else
        {
            selectedIcon.Hide();
        }
    }
}
