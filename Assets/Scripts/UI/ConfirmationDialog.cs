using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ConfirmationDialog : MonoBehaviour
{
    public static ConfirmationDialog Instance { get; private set; }
    public static bool IsShowing { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject content;
    [SerializeField] private TextMeshProUGUI messageLabel;
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;
    [SerializeField] private TextMeshProUGUI yesButtonText;
    [SerializeField] private TextMeshProUGUI noButtonText;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;

    [Header("Defaults")]
    [TextArea]
    [SerializeField] private string fallbackMessage = "Are you sure?";
    [SerializeField] private string defaultYesLabel = "Yes";
    [SerializeField] private string defaultNoLabel = "No";
    [SerializeField] private string defaultOkLabel = "OK";

    private TaskCompletionSource<bool> pendingRequest;
    private Action pendingOnClose;
    private readonly List<DialogRequest> queuedRequests = new();
    private int activeIndex = -1;
    private Coroutine waitForMessagesRoutine;
    private Image requestImage;
    private RectTransform imageFrame;
    private RectTransform dialogPanel;
    private Vector2 textOnlyPanelSize;
    private Vector2 textOnlyMessagePosition;
    private Vector2 textOnlyMessageSize;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        yesButton.onClick.AddListener(() => Resolve(true));
        noButton.onClick.AddListener(() => Resolve(false));
        if (previousButton != null) previousButton.onClick.AddListener(ShowPrevious);
        if (nextButton != null) nextButton.onClick.AddListener(ShowNext);

        DontDestroyOnLoad(gameObject);
        HideInstant();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Opens a confirmation dialog with a custom message and button labels.
    /// </summary>
    public static Task<bool> Ask(string message, string yesString, string noString, Action onClose = null)
    {
        if (Instance == null)
        {
            Debug.LogError("ConfirmationDialog was called before its instance was created.");
            return Task.FromResult(false);
        }

        return Instance.Show(message, yesString, noString, false, false, onClose);
    }

    public static Task<bool> AskImmediate(string message, string yesString, string noString, Action onClose = null)
    {
        if (Instance == null)
        {
            Debug.LogError("ConfirmationDialog was called before its instance was created.");
            return Task.FromResult(false);
        }

        return Instance.Show(message, yesString, noString, false, true, onClose);
    }

    /// <summary>
    /// Opens a confirmation dialog that uses the configured fallback message.
    /// </summary>
    public static Task<bool> Ask(string yesString, string noString, Action onClose = null)
    {
        if (Instance == null)
        {
            Debug.LogError("ConfirmationDialog was called before its instance was created.");
            return Task.FromResult(false);
        }

        return Instance.Show(Instance.fallbackMessage, yesString, noString, false, false, onClose);
    }

    /// <summary>
    /// Opens a Yes/No dialog using the default button texts.
    /// </summary>
    public static Task<bool> AskYesNo(string message, Action onClose = null, Sprite image = null, string imageName = null)
    {
        if (Instance == null)
        {
            Debug.LogError("ConfirmationDialog was called before its instance was created.");
            return Task.FromResult(false);
        }

        return Instance.Show(message, Instance.defaultYesLabel, Instance.defaultNoLabel, false, false, onClose, image, imageName);
    }

    /// <summary>
    /// Opens a single-button OK dialog.
    /// </summary>
    public static Task<bool> AskOk(string message, Action onClose = null)
    {
        if (Instance == null)
        {
            Debug.LogError("ConfirmationDialog was called before its instance was created.");
            return Task.FromResult(false);
        }

        string okLabel = string.IsNullOrWhiteSpace(Instance.defaultOkLabel) ? "OK" : Instance.defaultOkLabel;
        return Instance.Show(message, okLabel, string.Empty, true, false, onClose);
    }

    private Task<bool> Show(string message, string yesString, string noString, bool singleButton = false, bool forceImmediate = false, Action onClose = null, Sprite image = null, string imageName = null)
    {
        var request = new DialogRequest
        {
            message = message,
            yesString = yesString,
            noString = noString,
            singleButton = singleButton,
            onClose = onClose,
            image = image,
            imageName = imageName,
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        if (forceImmediate)
        {
            if (!queuedRequests.Contains(request))
            {
                queuedRequests.Add(request);
            }
            if (activeIndex < 0)
            {
                activeIndex = 0;
            }
            if (waitForMessagesRoutine != null)
            {
                StopCoroutine(waitForMessagesRoutine);
                waitForMessagesRoutine = null;
            }
            ShowActiveImmediate();
            return request.tcs.Task;
        }

        EnqueueRequest(request);
        return request.tcs.Task;
    }

    private void EnqueueRequest(DialogRequest request)
    {
        if (request == null) return;
        if (!queuedRequests.Contains(request))
        {
            queuedRequests.Add(request);
        }
        if (activeIndex < 0) activeIndex = 0;
        ShowActive();
    }

    private void Resolve(bool answer)
    {
        HideInstant();
        pendingRequest?.TrySetResult(answer);
        pendingOnClose?.Invoke();
        pendingRequest = null;
        pendingOnClose = null;

        if (activeIndex >= 0 && activeIndex < queuedRequests.Count)
        {
            queuedRequests.RemoveAt(activeIndex);
            if (queuedRequests.Count == 0)
            {
                activeIndex = -1;
            }
            else
            {
                activeIndex = Mathf.Clamp(activeIndex, 0, queuedRequests.Count - 1);
            }
        }

        ShowActive();
    }

    private void HideInstant()
    {
        if (requestImage != null) requestImage.sprite = null;
        content.SetActive(false);
        IsShowing = false;
    }

    private void ShowActive()
    {
        if (queuedRequests.Count == 0)
        {
            HideInstant();
            return;
        }

        if (ShouldDelayDialog())
        {
            HideInstant();
            StartWaitForMessages();
            return;
        }

        activeIndex = Mathf.Clamp(activeIndex, 0, queuedRequests.Count - 1);
        var activeRequest = queuedRequests[activeIndex];

        pendingRequest = activeRequest.tcs;
        pendingOnClose = activeRequest.onClose;

        content.SetActive(true);
        IsShowing = true;

        messageLabel.text = string.IsNullOrWhiteSpace(activeRequest.message) ? fallbackMessage : activeRequest.message;
        ApplyRequestImage(activeRequest);
        yesButtonText.text = string.IsNullOrWhiteSpace(activeRequest.yesString) ? defaultYesLabel : activeRequest.yesString;
        yesButton.gameObject.SetActive(true);

        bool showNo = !activeRequest.singleButton;
        noButton.gameObject.SetActive(showNo);
        if (showNo)
        {
            noButtonText.text = string.IsNullOrWhiteSpace(activeRequest.noString) ? defaultNoLabel : activeRequest.noString;
        }

        bool canPrev = activeIndex > 0;
        bool canNext = activeIndex < queuedRequests.Count - 1;
        if (previousButton != null) previousButton.gameObject.SetActive(canPrev);
        if (nextButton != null) nextButton.gameObject.SetActive(canNext);
    }

    private bool ShouldDelayDialog()
    {
        bool focusPending = BoardNavigator.Instance != null && BoardNavigator.Instance.HasPendingFocus();
        return MessageDisplay.IsDisplaying() || MessageDisplayNoUI.IsBusy() || focusPending;
    }

    private void StartWaitForMessages()
    {
        if (waitForMessagesRoutine != null) return;
        waitForMessagesRoutine = StartCoroutine(WaitForMessages());
    }

    private IEnumerator WaitForMessages()
    {
        while (ShouldDelayDialog())
        {
            yield return null;
        }
        waitForMessagesRoutine = null;
        ShowActive();
    }

    private void ShowActiveImmediate()
    {
        if (queuedRequests.Count == 0)
        {
            HideInstant();
            return;
        }

        if (waitForMessagesRoutine != null)
        {
            StopCoroutine(waitForMessagesRoutine);
            waitForMessagesRoutine = null;
        }

        activeIndex = Mathf.Clamp(activeIndex, 0, queuedRequests.Count - 1);
        var activeRequest = queuedRequests[activeIndex];

        pendingRequest = activeRequest.tcs;
        pendingOnClose = activeRequest.onClose;

        content.SetActive(true);
        IsShowing = true;

        messageLabel.text = string.IsNullOrWhiteSpace(activeRequest.message) ? fallbackMessage : activeRequest.message;
        ApplyRequestImage(activeRequest);
        yesButtonText.text = string.IsNullOrWhiteSpace(activeRequest.yesString) ? defaultYesLabel : activeRequest.yesString;
        yesButton.gameObject.SetActive(true);

        bool showNo = !activeRequest.singleButton;
        noButton.gameObject.SetActive(showNo);
        if (showNo)
        {
            noButtonText.text = string.IsNullOrWhiteSpace(activeRequest.noString) ? defaultNoLabel : activeRequest.noString;
        }

        bool canPrev = activeIndex > 0;
        bool canNext = activeIndex < queuedRequests.Count - 1;
        if (previousButton != null) previousButton.gameObject.SetActive(canPrev);
        if (nextButton != null) nextButton.gameObject.SetActive(canNext);
    }

    private void ApplyRequestImage(DialogRequest request)
    {
        Sprite sprite = request.image;
        if (sprite == null && !string.IsNullOrWhiteSpace(request.imageName))
            sprite = FindFirstObjectByType<Illustrations>()?.GetIllustrationByName(request.imageName, false);

        if (dialogPanel == null)
        {
            dialogPanel = messageLabel.transform.parent as RectTransform;
            textOnlyPanelSize = dialogPanel.sizeDelta;
            textOnlyMessagePosition = messageLabel.rectTransform.anchoredPosition;
            textOnlyMessageSize = messageLabel.rectTransform.sizeDelta;
        }

        if (sprite != null && requestImage == null)
        {
            var frame = new GameObject("Confirmation artwork frame", typeof(RectTransform), typeof(Image), typeof(ImageUnaffectedBySkin));
            imageFrame = frame.GetComponent<RectTransform>();
            imageFrame.SetParent(dialogPanel, false);
            imageFrame.anchorMin = imageFrame.anchorMax = new Vector2(.5f, 1f);
            imageFrame.pivot = new Vector2(.5f, 1f);
            imageFrame.anchoredPosition = new Vector2(0, -28);
            imageFrame.sizeDelta = new Vector2(224, 224);
            var border = frame.GetComponent<Image>();
            border.color = new Color(.65f, .51f, .29f);
            border.raycastTarget = false;
            var art = new GameObject("Artwork", typeof(RectTransform), typeof(Image), typeof(ImageUnaffectedBySkin));
            art.transform.SetParent(imageFrame, false);
            requestImage = art.GetComponent<Image>();
            requestImage.raycastTarget = false;
            requestImage.preserveAspect = true;
            requestImage.rectTransform.anchorMin = Vector2.zero;
            requestImage.rectTransform.anchorMax = Vector2.one;
            requestImage.rectTransform.offsetMin = new Vector2(2, 2);
            requestImage.rectTransform.offsetMax = new Vector2(-2, -2);
        }

        bool hasImage = sprite != null;
        if (imageFrame != null) imageFrame.gameObject.SetActive(hasImage);
        if (requestImage != null) requestImage.sprite = sprite;
        float extraHeight = hasImage ? 248 : 0;
        dialogPanel.sizeDelta = textOnlyPanelSize + new Vector2(0, extraHeight);
        messageLabel.rectTransform.sizeDelta = textOnlyMessageSize - new Vector2(0, extraHeight);
        messageLabel.rectTransform.anchoredPosition = textOnlyMessagePosition - new Vector2(0, extraHeight / 2);
    }

    private void ShowPrevious()
    {
        if (queuedRequests.Count < 2) return;
        activeIndex = Mathf.Max(0, activeIndex - 1);
        ShowActive();
    }

    private void ShowNext()
    {
        if (queuedRequests.Count < 2) return;
        activeIndex = Mathf.Min(queuedRequests.Count - 1, activeIndex + 1);
        ShowActive();
    }

    private class DialogRequest
    {
        public string message;
        public string yesString;
        public string noString;
        public bool singleButton;
        public Action onClose;
        public Sprite image;
        public string imageName;
        public TaskCompletionSource<bool> tcs;
    }

    public static void CloseAll()
    {
        if (Instance == null) return;
        Instance.ForceClose();
    }

    private void ForceClose()
    {
        if (waitForMessagesRoutine != null)
        {
            StopCoroutine(waitForMessagesRoutine);
            waitForMessagesRoutine = null;
        }
        pendingRequest?.TrySetResult(false);
        pendingOnClose?.Invoke();
        pendingRequest = null;
        pendingOnClose = null;
        queuedRequests.Clear();
        activeIndex = -1;
        HideInstant();
    }
}
