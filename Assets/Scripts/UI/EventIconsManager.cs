using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EventIconsManager : MonoBehaviour
{
    public GameObject eventIcon;
    public GridLayoutGroup gridLayout;
    private readonly List<EventIcon> activeIcons = new();

    private void Awake()
    {
        if (gridLayout == null)
        {
            gridLayout = GetComponentInChildren<GridLayoutGroup>(true);
        }
    }

    public static EventIconsManager FindManager()
    {
        return FindAnyObjectByType<EventIconsManager>();
    }

    public static void ShowHexAnchoredMessage(EventIconType type, Hex targetHex, Hex fallbackHex, string message, Color color)
    {
        bool isRevealed = targetHex != null && targetHex.IsHexRevealed();
        Hex immediateHex = isRevealed ? targetHex : fallbackHex;
        if (immediateHex == null) return;

        MessageDisplayNoUI.ShowAnchoredMessage(immediateHex, message, color, true);
    }

    public EventIcon AddEventIcon(EventIconType type, bool discardable, Action onOpen)
        => AddEventIcon(type, discardable, onOpen, null, null);

    public EventIcon AddEventIcon(EventIconType type, bool discardable, Action onOpen, Action onRemove, Sprite characterPortrait = null)
    {
        if (eventIcon == null)
        {
            Debug.LogWarning("EventIconsManager has no eventIcon prefab assigned.");
            return null;
        }

        EnsureVisible();
        Transform parent = gridLayout != null ? gridLayout.transform : transform;
        GameObject iconInstance = Instantiate(eventIcon, parent);
        iconInstance.transform.SetAsLastSibling();
        iconInstance.transform.localScale = Vector3.zero;

        CanvasGroup canvasGroup = iconInstance.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = iconInstance.AddComponent<CanvasGroup>();
        }
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        Image rootImage = iconInstance.GetComponent<Image>();
        if (rootImage == null)
        {
            rootImage = iconInstance.AddComponent<Image>();
            rootImage.color = new Color(1f, 1f, 1f, 0.001f);
            rootImage.raycastTarget = true;
        }
        else
        {
            rootImage.raycastTarget = true;
        }

        Button button = iconInstance.GetComponent<Button>();
        if (button == null)
        {
            button = iconInstance.AddComponent<Button>();
        }
        button.targetGraphic = rootImage;

        EventIcon icon = iconInstance.GetComponent<EventIcon>();
        if (icon == null)
        {
            icon = iconInstance.AddComponent<EventIcon>();
        }

        Action removal = onRemove ?? (() => RemoveIcon(icon));
        Sprite portrait = characterPortrait ?? ResolveActiveLeaderPortrait();
        icon.Configure(type, discardable, onOpen, removal, portrait);
        activeIcons.Add(icon);
        RefreshLayout(parent as RectTransform);
        StartCoroutine(AnimateIconIn(iconInstance.transform, canvasGroup));
        return icon;
    }

    private IEnumerator AnimateIconIn(Transform iconTransform, CanvasGroup cg)
    {
        float duration = 0.28f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (iconTransform == null) yield break;
            float p = elapsed / duration;
            float eased = 1f + 2.70158f * Mathf.Pow(p - 1f, 3f) + 1.70158f * Mathf.Pow(p - 1f, 2f);
            iconTransform.localScale = Vector3.LerpUnclamped(Vector3.zero, Vector3.one, eased);
            if (cg != null) cg.alpha = Mathf.Clamp01(p * 3f);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (iconTransform != null) iconTransform.localScale = Vector3.one;
        if (cg != null) cg.alpha = 1f;
    }

    private void RemoveIcon(EventIcon icon)
    {
        activeIcons.Remove(icon);
        Transform parent = gridLayout != null ? gridLayout.transform : transform;
        RefreshLayout(parent as RectTransform);
    }

    private void EnsureVisible()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (gridLayout != null && !gridLayout.gameObject.activeSelf) gridLayout.gameObject.SetActive(true);

        ScrollRect scrollRect = GetComponentInParent<ScrollRect>(true);
        if (scrollRect != null && !scrollRect.gameObject.activeSelf)
        {
            scrollRect.gameObject.SetActive(true);
        }

        Canvas canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null && !canvas.gameObject.activeSelf)
        {
            canvas.gameObject.SetActive(true);
        }
    }

    private static void RefreshLayout(RectTransform layoutRect)
    {
        Canvas.ForceUpdateCanvases();
        if (layoutRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRect);
            RectTransform parentRect = layoutRect.parent as RectTransform;
            if (parentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }
        }

        EventSystem current = EventSystem.current;
        if (current != null)
        {
            current.UpdateModules();
        }
    }

    private static Sprite ResolveActiveLeaderPortrait()
    {
        SelectedCharacterIcon selectedIcon = FindAnyObjectByType<SelectedCharacterIcon>();
        if (selectedIcon != null)
        {
            if (selectedIcon.icon != null && selectedIcon.icon.sprite != null)
                return selectedIcon.icon.sprite;
        }

        Game game = Game.Instance;
        Leader leader = null;
        if (game != null)
            leader = game.currentlyPlaying ?? game.player;

        if (leader == null || string.IsNullOrWhiteSpace(leader.characterName)) return null;

        Illustrations illustrations = FindAnyObjectByType<Illustrations>();
        if (illustrations == null) return null;

        return illustrations.GetIllustrationByName(leader.characterName, false)
            ?? illustrations.GetIllustrationByName(leader.characterName);
    }
}
