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
    public bool useFramedLayout;

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

        PopupContinue.Rewire(this, "Hide", Continue);
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

    /// <summary>Continue button: the first press finishes the typewriter, the next one closes.</summary>
    public void Continue()
    {
        if (typeWriterEffect != null && typeWriterEffect.isActiveAndEnabled && typeWriterEffect.IsTyping)
        {
            typeWriterEffect.Complete();
            return;
        }
        Hide();
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

        FindAnyObjectByType<Sounds>().StopAllSounds();
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
        if (useFramedLayout)
        {
            bool first = data.spriteActor1 != null;
            if (actor1 != null) actor1.transform.parent.gameObject.SetActive(first);
            if (actor2 != null) actor2.transform.parent.gameObject.SetActive(hasActor2);
            GridLayoutGroup portraits = actor1 != null ? actor1.GetComponentInParent<GridLayoutGroup>(true) : null;
            if (portraits != null)
            {
                portraits.startCorner = GridLayoutGroup.Corner.UpperLeft;
                portraits.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                portraits.constraintCount = 1;
                portraits.cellSize = new Vector2(204, first && hasActor2 ? 174 : 352);
            }
            var textScroll = textWidget != null ? textWidget.GetComponentInParent<ScrollRect>() : null;
            if (textScroll != null)
            {
                var scrollRect = (RectTransform)textScroll.transform;
                scrollRect.offsetMin = new Vector2(first || hasActor2 ? 281 : 35, scrollRect.offsetMin.y);
            }
        }
        StyleMeeting(data);
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
                // Compact messages still need space for their title, artwork and controls.
                size.y = Mathf.Max(initialSize.y * 0.82f, referenceHeight - data.restrictHeight);
            }

            rectTransform.sizeDelta = size;
        }

        UpdateArrows();
        var scroll = textWidget != null ? textWidget.GetComponentInParent<ScrollRect>() : null;
        if (scroll != null) { Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1f; }
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

    // ── The meeting header ──────────────────────────────────────────────────────────────────
    // The two parties of a popup (a leader and the realm it meets, say) as round, gold-ringed
    // portraits facing each other across a crossed-swords mark at the top, the title centred
    // under them and the text at full width below — instead of two square paintings stacked
    // down the side. A popup with one portrait shows it alone, centred.

    private const float MeetingPortrait = 168f;
    private RectTransform meeting;
    private Image meetingLeft, meetingRight;
    private TextMeshProUGUI meetingMark;
    private Vector2 textOffsetMin, textOffsetMax, titleAnchoredPosition, titleSize;
    private bool meetingLayoutSaved;

    private void StyleMeeting(PopupData data)
    {
        if (container == null || titleWidget == null) return;
        RectTransform box = container.transform as RectTransform;
        var textScroll = textWidget != null ? textWidget.GetComponentInParent<ScrollRect>() : null;
        RectTransform scrollRect = textScroll != null ? (RectTransform)textScroll.transform : null;
        RectTransform title = titleWidget.rectTransform;
        if (!meetingLayoutSaved)
        {
            meetingLayoutSaved = true;
            titleAnchoredPosition = title.anchoredPosition;
            titleSize = title.sizeDelta;
            if (scrollRect != null) { textOffsetMin = scrollRect.offsetMin; textOffsetMax = scrollRect.offsetMax; }
        }

        bool first = data.spriteActor1 != null, second = data.spriteActor2 != null;
        bool any = first || second;
        EnsureMeeting(box);
        meeting.gameObject.SetActive(any);
        // The old portrait column gives way to the header.
        if (actor1 != null) actor1.transform.parent.gameObject.SetActive(false);
        if (actor2 != null) actor2.transform.parent.gameObject.SetActive(false);
        if (!any)
        {
            title.anchoredPosition = titleAnchoredPosition;
            title.sizeDelta = titleSize;
            if (scrollRect != null) { scrollRect.offsetMin = textOffsetMin; scrollRect.offsetMax = textOffsetMax; }
            return;
        }

        bool pair = first && second;
        SetPortrait(meetingLeft, first ? data.spriteActor1 : data.spriteActor2);
        SetPortrait(meetingRight, pair ? data.spriteActor2 : null);
        ((RectTransform)meetingLeft.transform.parent.parent).anchoredPosition = new Vector2(pair ? -150f : 0f, -36f);
        ((RectTransform)meetingRight.transform.parent.parent).anchoredPosition = new Vector2(150f, -36f);
        meetingMark.transform.parent.gameObject.SetActive(pair);

        // The title under the portraits, centred; the text below it, full width.
        title.anchorMin = title.anchorMax = new Vector2(0.5f, 1f);
        title.pivot = new Vector2(0.5f, 1f);
        title.anchoredPosition = new Vector2(0f, -36f - MeetingPortrait - 22f);
        title.sizeDelta = new Vector2(box.rect.width - 120f, 56f);
        titleWidget.alignment = TextAlignmentOptions.Center;
        titleWidget.enableAutoSizing = true;
        titleWidget.fontSizeMax = Mathf.Max(titleWidget.fontSizeMax, 40f);
        titleWidget.fontSizeMin = 24f;
        if (scrollRect != null)
        {
            scrollRect.offsetMin = new Vector2(80f, textOffsetMin.y);
            scrollRect.offsetMax = new Vector2(-80f, -(36f + MeetingPortrait + 22f + 70f));
        }
    }

    private void EnsureMeeting(RectTransform box)
    {
        if (meeting != null) return;
        meeting = new GameObject("Meeting", typeof(RectTransform)).GetComponent<RectTransform>();
        meeting.SetParent(box, false);
        meeting.anchorMin = new Vector2(0f, 1f);
        meeting.anchorMax = new Vector2(1f, 1f);
        meeting.pivot = new Vector2(0.5f, 1f);
        meeting.anchoredPosition = Vector2.zero;
        meeting.sizeDelta = new Vector2(0f, MeetingPortrait + 40f);

        // Crossed swords between the two, on a short gold rule (drawn under the portraits).
        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        rule.rectTransform.SetParent(meeting, false);
        rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        rule.rectTransform.sizeDelta = new Vector2(140f, 1.5f);
        rule.rectTransform.anchoredPosition = new Vector2(0f, -36f - MeetingPortrait * 0.5f);
        rule.color = new Color(UiTheme.Gold.r, UiTheme.Gold.g, UiTheme.Gold.b, 0.6f);
        rule.raycastTarget = false;
        meetingMark = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        meetingMark.rectTransform.SetParent(rule.rectTransform, false);
        meetingMark.rectTransform.sizeDelta = new Vector2(60f, 40f);
        meetingMark.alignment = TextAlignmentOptions.Center;
        meetingMark.fontSize = 30f;
        meetingMark.text = "<sprite name=\"sword\">";
        meetingMark.raycastTarget = false;

        meetingLeft = Portrait(meeting, "Left");
        meetingRight = Portrait(meeting, "Right");
    }

    private static Image Portrait(RectTransform parent, string name)
    {
        var holder = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        holder.SetParent(parent, false);
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 1f);
        holder.pivot = new Vector2(0.5f, 1f);
        holder.sizeDelta = new Vector2(MeetingPortrait, MeetingPortrait);
        var ring = holder.GetComponent<Image>();
        ring.sprite = HudSprites.Circle;
        ring.color = UiTheme.Gold;
        ring.raycastTarget = false;
        var window = new GameObject("Window", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask)).GetComponent<RectTransform>();
        window.SetParent(holder, false);
        window.anchorMin = Vector2.zero;
        window.anchorMax = Vector2.one;
        window.offsetMin = new Vector2(4f, 4f);
        window.offsetMax = new Vector2(-4f, -4f);
        var mask = window.GetComponent<Image>();
        mask.sprite = HudSprites.Circle;
        mask.raycastTarget = false;
        window.GetComponent<Mask>().showMaskGraphic = false;
        var face = new GameObject("Face", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        face.rectTransform.SetParent(window, false);
        face.rectTransform.anchorMin = Vector2.zero;
        face.rectTransform.anchorMax = Vector2.one;
        // A little larger than the window and lifted, so the face sits in the circle.
        face.rectTransform.offsetMin = new Vector2(-12f, -24f);
        face.rectTransform.offsetMax = new Vector2(12f, 6f);
        face.raycastTarget = false;
        foreach (Image part in holder.GetComponentsInChildren<Image>(true)) part.gameObject.AddComponent<ImageUnaffectedBySkin>();
        return face;
    }

    private static void SetPortrait(Image face, Sprite sprite)
    {
        face.transform.parent.parent.gameObject.SetActive(sprite != null);
        face.sprite = sprite;
    }

    private static void SetActorVisuals(Image image, Sprite fallbackSprite)
    {
        if (image != null)
        {
            image.enabled = fallbackSprite != null;
            image.sprite = fallbackSprite;
        }
    }
}
