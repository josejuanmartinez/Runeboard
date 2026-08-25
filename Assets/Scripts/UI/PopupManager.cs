using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class PopupManager : MonoBehaviour
{
    public static PopupManager Instance { get; private set; }

    [Header("References")]
    public GameObject container;
    public Image actor1;
    public Image actor2;
    public GameObject leftArrow;
    public GameObject rightArrow;
    public TextMeshProUGUI textWidget;
    public TextMeshProUGUI titleWidget;
    public TypewriterEffect typeWriterEffect;
    public int referenceHeight = 600;

    private readonly List<PopupData> queue = new();
    private int currentIndex = -1;
    private RectTransform rectTransform;
    private GameObject visibilityRoot;
    private CanvasGroup visibilityCanvasGroup;
    private Vector2 initialSize;
    public static bool IsShowing { get; private set; }
    // private Videos videos;
    private Coroutine waitForMessagesRoutine;
    // private Coroutine actorPlaybackSequenceRoutine;
    // private Coroutine popupDisplayRoutine;
    // private int actorPlaybackToken;
    // private int popupDisplayToken;

    private struct PopupData
    {
        public string title;
        public Sprite spriteActor1;
        public Sprite spriteActor2;
        public string text;
        public bool typeWrite;
        public int restrictHeight;
        public Action onClose;
    }

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // optional: persists across scenes

        rectTransform = container != null ? container.GetComponent<RectTransform>() : null;

        // `container` is the framed popup panel, while its direct child of PopupManager is the
        // full-screen Content canvas (including the dark backdrop). Keep that whole UI branch
        // active and hide/show it with one CanvasGroup. A CanvasGroup on only the inner panel
        // cannot revive an inactive Content parent and also leaves the backdrop visible.
        visibilityRoot = ResolveVisibilityRoot();
        visibilityCanvasGroup = visibilityRoot != null ? visibilityRoot.GetComponent<CanvasGroup>() : null;
        if (visibilityRoot != null && visibilityCanvasGroup == null)
        {
            visibilityCanvasGroup = visibilityRoot.AddComponent<CanvasGroup>();
        }
        SetContainerVisible(false);
        if (visibilityRoot != null && !visibilityRoot.activeSelf)
        {
            visibilityRoot.SetActive(true);
        }
        initialSize = rectTransform != null ? rectTransform.sizeDelta : Vector2.zero;
        IsShowing = false;
        SetContainerVisible(false);
    }

    public void Initialize(string title, Sprite spriteActor1, Sprite spriteActor2, string text, bool typeWrite, int restrictHeight = 0, Action onClose = null)
        => InitializeInternal(title, spriteActor1, spriteActor2, text, typeWrite, restrictHeight, onClose, false);

    private void InitializeInternal(string title, Sprite spriteActor1, Sprite spriteActor2, string text, bool typeWrite, int restrictHeight = 0, Action onClose = null, bool immediate = false)
    {
        Game.Instance?.NotifyStartupPopupShown();

        queue.Add(new PopupData
        {
            title = title,
            spriteActor1 = spriteActor1,
            spriteActor2 = spriteActor2,
            text = text,
            typeWrite = typeWrite,
            restrictHeight = restrictHeight,
            onClose = onClose
        });

        if (currentIndex == -1)
        {
            if (!immediate && ShouldDelayPopup())
            {
                StartWaitForMessages();
            }
            else
            {
                ShowEntry(0);
            }
        }
        else
        {
            UpdateArrows(); // refresh navigation availability when adding while already showing
        }
    }

    public void Hide()
    {
        // Video popup flow disabled for now; static portraits only.
        // popupDisplayToken++;
        // StopPopupDisplayRoutine();
        // actorPlaybackToken++;
        // StopActorPlaybackSequence();
        Action onClose = null;
        if (currentIndex >= 0 && currentIndex < queue.Count)
        {
            onClose = queue[currentIndex].onClose;
        }

        FindFirstObjectByType<Sounds>().StopAllSounds();
        Music.Instance?.StopEventMusic();
        SetContainerVisible(false);
        queue.Clear();
        currentIndex = -1;
        IsShowing = false;

        if (rectTransform != null)
        {
            rectTransform.sizeDelta = initialSize;
        }

        if (typeWriterEffect != null)
        {
            typeWriterEffect.enabled = false;
            typeWriterEffect.fullText = "";
        }

        SetActorVisuals(actor1, null);
        SetActorVisuals(actor2, null);

        ActionsManager actionsManager = ActionsManager.Instance;
        if (actionsManager != null)
        {
            actionsManager.RefreshInteractableState();
        }

        Game.Instance?.NotifyStartupPopupClosed();
        onClose?.Invoke();
    }

    public static void Show(string title, Sprite spriteActor1, Sprite spriteActor2, string text, bool typeWrite, int restrictHeight = 0, Action onClose = null)
    {
        if (Instance == null) return;
        Instance.Initialize(title, spriteActor1, spriteActor2, text, typeWrite, restrictHeight, onClose);
    }

    public static void ShowImmediate(string title, Sprite spriteActor1, Sprite spriteActor2, string text, bool typeWrite, int restrictHeight = 0, Action onClose = null)
    {
        if (Instance == null) return;
        Instance.InitializeInternal(title, spriteActor1, spriteActor2, text, typeWrite, restrictHeight, onClose, true);
    }

    public static void HidePopup()
        => Instance.Hide();

    public static void CloseAll()
    {
        if (Instance == null) return;
        Instance.Hide();
    }

    public void ShowPrevious()
    {
        if (queue.Count == 0 || currentIndex <= 0)
            return;

        ShowEntry(currentIndex - 1);
    }

    public void ShowNext()
    {
        if (queue.Count == 0 || currentIndex >= queue.Count - 1)
            return;

        ShowEntry(currentIndex + 1);
    }

    public static void ShowPreviousPopup()
        => Instance.ShowPrevious();

    public static void ShowNextPopup()
        => Instance.ShowNext();

    private void ShowEntry(int index)
    {
        if (index < 0 || index >= queue.Count)
            return;

        currentIndex = index;
        PopupData data = queue[currentIndex];

        bool hasActor2 = data.spriteActor2 != null;
        if (actor2 != null)
        {
            actor2.gameObject.SetActive(hasActor2);
        }
        
        SetActorVisuals(actor1, data.spriteActor1);
        SetActorVisuals(actor2, data.spriteActor2);
        if (!ShowContainer())
        {
            // A modal that cannot render must not retain IsShowing/startup input locks.
            Hide();
            return;
        }
        Sounds.Instance?.PlayMessage();
        Music.Instance?.PlayEventMusic();
        ApplyPopupTextAndTitle(data);

        if (rectTransform != null)
        {
            Vector2 size = initialSize;

            if (data.restrictHeight > 0)
            {
                size.y = referenceHeight - data.restrictHeight;
            }

            rectTransform.sizeDelta = size;
        }

        UpdateArrows();
    }

    private bool ShouldDelayPopup()
    {
        bool focusPending = BoardNavigator.Instance != null && BoardNavigator.Instance.HasPendingFocus();
        return MessageDisplay.IsDisplaying() || MessageDisplayNoUI.IsBusy() || focusPending || VideoPopupManager.IsShowing;
    }

    private void StartWaitForMessages()
    {
        if (waitForMessagesRoutine != null) return;
        waitForMessagesRoutine = StartCoroutine(WaitForMessages());
    }

    private IEnumerator WaitForMessages()
    {
        while (ShouldDelayPopup())
        {
            yield return null;
        }
        waitForMessagesRoutine = null;
        if (currentIndex == -1 && queue.Count > 0)
        {
            ShowEntry(0);
        }
        else
        {
            UpdateArrows();
        }
    }

    private void UpdateArrows()
    {
        bool hasQueue = queue.Count > 1;

        if (leftArrow != null) leftArrow.SetActive(hasQueue && currentIndex > 0);
        if (rightArrow != null) rightArrow.SetActive(hasQueue && currentIndex < queue.Count - 1);
    }

    private bool ShowContainer()
    {
        SetContainerVisible(true);
        if (visibilityRoot == null || !visibilityRoot.activeInHierarchy || container == null || !container.activeInHierarchy)
        {
            Debug.LogError("PopupManager cannot show its CanvasGroup visibility root because it is inactive in the hierarchy; cancelling the popup to avoid an invisible input lock.");
            IsShowing = false;
            return false;
        }
        IsShowing = true;
        return true;
    }

    private void SetContainerVisible(bool visible)
    {
        if (visibilityRoot == null) return;
        // The popup hierarchy stays active after Awake. Visibility and input are controlled
        // exclusively by CanvasGroup so hiding it cannot leave a background intercepting input.
        if (visibilityCanvasGroup != null)
        {
            visibilityCanvasGroup.alpha = visible ? 1f : 0f;
            visibilityCanvasGroup.interactable = visible;
            visibilityCanvasGroup.blocksRaycasts = visible;
        }
    }

    private GameObject ResolveVisibilityRoot()
    {
        if (container == null) return null;

        Transform candidate = container.transform;
        while (candidate.parent != null && candidate.parent != transform)
        {
            candidate = candidate.parent;
        }

        return candidate.gameObject;
    }

    private void ApplyPopupTextAndTitle(PopupData data)
    {
        if (typeWriterEffect != null)
        {
            if (data.typeWrite)
            {
                typeWriterEffect.enabled = true;
                typeWriterEffect.fullText = data.text;
                textWidget.text = "";
            }
            else
            {
                typeWriterEffect.enabled = false;
                typeWriterEffect.fullText = "";
                textWidget.text = data.text;
            }
        }
        else
        {
            textWidget.text = data.text;
        }

        titleWidget.text = data.title;

        if (typeWriterEffect != null && data.typeWrite)
        {
            // Do not activate popup ancestors here. TypewriterEffect falls back to rendering the
            // complete text if its Content is inactive; CanvasGroup alone controls popup visibility.
            typeWriterEffect.StartWriting();
        }
    }

    private static void SetActorVisuals(Image image, Sprite fallbackSprite)
    {
        if (image != null)
        {
            image.enabled = true;
            image.sprite = fallbackSprite;
        }
    }
}
