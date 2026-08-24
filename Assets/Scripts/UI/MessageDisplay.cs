using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Text.RegularExpressions;

[RequireComponent(typeof(CanvasGroup))]
public class MessageDisplay : MonoBehaviour
{
    private const float MinimumVisibleDuration = 2f;
    private static MessageDisplay instance;
    private static bool displayPaused;
    private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private float displayDuration = 0.08f;
    [SerializeField] private float fadeDuration = 0.02f;

    private Queue<MessageData> messageQueue = new Queue<MessageData>();
    private bool isDisplayingMessage = false;
    private bool persistentActive = false;
    private Coroutine waitForSyncRoutine;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        // Singleton pattern to ensure only one instance exists
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        // messageText itself stays enabled permanently; the canvas group governs visibility
        // (alpha for the fade, interactable/blocksRaycasts for hit-testing) instead.
        messageText.enabled = true;
        canvasGroup.alpha = 0f;
        SetCanvasGroupVisible(false);
    }

    private void SetCanvasGroupVisible(bool visible)
    {
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    /// <summary>
    /// Static method to display a message in the center of the screen
    /// </summary>
    /// <param name="message">Text message to display</param>
    /// <param name="color">Color for the text (defaults to white if not specified)</param>
    public static void ShowMessage(string message, Color? color = null, bool forceImmediate = false, bool logToWidget = true, bool playSound = true)
    {
        Game game = Game.Instance;
        if (game == null) return;
        if (!game.started || game.currentlyPlaying != game.player) return;
        // An NPL may now think during the human leader's alignment window. Its center-screen
        // messages must not inherit the human current-turn context, sound, or LogWidget entry.
        Leader aiActor = AITurnController.CurrentExecutingLeader;
        if (aiActor != null && aiActor != game.player) return;
        string formattedMessage = FormatMessageForDisplay(ResourceSpriteFormatter.ReplaceResourceWordsWithSprites(message));
        Color resolved = color ?? Color.white;
        // playSound: false lets a caller that already plays its own cue (see
        // PlayableLeader.ApplyVariantTransformation, which follows this with
        // PlayArtifactFound) skip the color-based sound instead of stacking an extra one.
        // Turn zero already has its popup and TurnBanner cues. Calendar/startup messages are
        // still displayed and logged, but must not create a rapid stack of identical chimes
        // before control reaches the player.
        bool allowSound = playSound && !game.IsInitialTurnStarting;
        if (allowSound && IsNegativeColor(resolved))
        {
            Sounds.Instance?.PlayNegative();
        }
        else if (allowSound && IsPositiveColor(resolved))
        {
            Sounds.Instance?.PlayPositive();
        }
        else if (allowSound)
        {
            Sounds.Instance?.PlayMessage();
        }

        if (forceImmediate) instance.ShowNow(formattedMessage, resolved);
        else instance.EnqueueMessage(formattedMessage, resolved);

        // Callers that log their own (more specific) category right after this call pass
        // logToWidget: false, so the event doesn't also show up here as a plain Notification.
        if (logToWidget)
        {
            LogManager.Log(LogCategory.Notification, game.currentlyPlaying?.characterName, null, message);
        }
    }

    public static bool IsBusy()
    {
        if (instance == null) return false;
        return instance.persistentActive || instance.isDisplayingMessage || instance.messageQueue.Count > 0;
    }

    public static bool IsDisplaying()
    {
        if (instance == null) return false;
        return instance.isDisplayingMessage;
    }

    /// <summary>
    /// Show a persistent message (no fade, stays until cleared). Used for turn banners.
    /// </summary>
    public static void ShowPersistent(string message, Color? color = null)
    {
        if (instance == null) return;
        instance.SetPersistent(message, color ?? Color.white);
    }

    /// <summary>
    /// Clear any persistent message.
    /// </summary>
    public static void ClearPersistent()
    {
        if (instance == null) return;
        instance.RemovePersistent();
    }

    /// <summary>
    /// Adds a message to the queue and starts processing if not already doing so
    /// </summary>
    private void EnqueueMessage(string message, Color textColor)
    {
        // Create message data object
        MessageData messageData = new MessageData(message, textColor);

        // Add to queue
        messageQueue.Enqueue(messageData);

        // If not currently displaying a message, start the process
        if (!isDisplayingMessage)
        {
            ProcessNextMessage();
        }
    }

    /// <summary>
    /// Processes the next message in the queue if one exists
    /// </summary>
    private void ProcessNextMessage()
    {
        if (displayPaused) { isDisplayingMessage = false; return; }
        if (persistentActive) { isDisplayingMessage = false; return; }
        if (ShouldDelayForFocusOrWorldMessages())
        {
            // Nothing is actually being displayed while we wait here (DisplayCoroutine already
            // finished, or hasn't started) — must clear this so WaitForSyncThenProcess's
            // `if (!isDisplayingMessage) ProcessNextMessage()` below can fire once the wait
            // ends. Leaving it true here stranded the queue forever whenever this branch was
            // entered from the end of a DisplayCoroutine (e.g. a toast finishing right as the
            // Turn banner starts) with nothing left queued: this flag never got reset by
            // anything else, so IsDisplaying() stayed true - and the board-input lock it feeds
            // in BoardNavigator.IsNavigationInputLocked() stayed engaged - permanently.
            isDisplayingMessage = false;
            if (waitForSyncRoutine == null)
            {
                waitForSyncRoutine = StartCoroutine(WaitForSyncThenProcess());
            }
            return;
        }

        if (messageQueue.Count > 0)
        {
            MessageData nextMessage = messageQueue.Dequeue();
            // Start displaying this message
            StartCoroutine(DisplayCoroutine(nextMessage.Message, nextMessage.TextColor));
        }
        else
        {
            // No more messages to display
            isDisplayingMessage = false;
        }
    }

    private void ShowNow(string message, Color textColor)
    {
        // Both/OnlyImmediate mode promises the message shows up on its own, not only if
        // something later (e.g. clicking its event icon) happens to retry it while nothing is
        // blocking anymore. Degrade to a queued message instead of silently dropping it - it
        // will surface as soon as the persistent banner clears/unpauses (see RemovePersistent
        // and SetPaused, which both drain the queue once their block lifts), or once the
        // Turn/Gathering-Resources banners finish (see ShouldDelayForFocusOrWorldMessages).
        if (displayPaused || persistentActive || TurnBanner.IsShowing)
        {
            EnqueueMessage(message, textColor);
            return;
        }
        if (waitForSyncRoutine != null)
        {
            StopCoroutine(waitForSyncRoutine);
            waitForSyncRoutine = null;
        }

        StopAllCoroutines();
        messageQueue.Clear();
        isDisplayingMessage = false;
        StartCoroutine(DisplayCoroutine(message, textColor));
    }

    private IEnumerator WaitForSyncThenProcess()
    {
        while (ShouldDelayForFocusOrWorldMessages())
        {
            yield return null;
        }

        waitForSyncRoutine = null;
        if (!isDisplayingMessage)
        {
            ProcessNextMessage();
        }
    }

    private static bool ShouldDelayForFocusOrWorldMessages()
    {
        // Only block while a world-space message is actively being displayed.
        // Using IsBusy() here can starve UI messages when deferred entries remain queued.
        // Also holds off entirely while the Turn/Gathering-Resources cinematic banners are up
        // (or queued to show) - those are CenterDisplayLock-exclusive full-screen displays,
        // same as the PC/region grant previews, so nothing else should be competing for
        // attention until that sequence has fully finished.
        return MessageDisplayNoUI.IsDisplaying || MessageDisplayNoUI.IsHoldingFocus || TurnBanner.IsShowing;
    }

    /// <summary>
    /// Coroutine to display a message with fade effects
    /// </summary>
    private IEnumerator DisplayCoroutine(string message, Color textColor)
    {
        Debug.Log($"[MsgDisplay] DisplayCoroutine START '{message}' (frame={Time.frameCount})");
        isDisplayingMessage = true;
        SetCanvasGroupVisible(true);

        // Set up the message
        messageText.text = message;
        messageText.color = textColor;

        // Fade in
        yield return FadeCanvasGroup(canvasGroup, 0f, 1f, fadeDuration);

        // Wait for display duration
        float visibleDuration = Mathf.Max(displayDuration, MinimumVisibleDuration);
        float waitDuration = Mathf.Max(0f, visibleDuration - (fadeDuration * 2));
        yield return new WaitForSeconds(waitDuration);

        // Fade out
        yield return FadeCanvasGroup(canvasGroup, 1f, 0f, fadeDuration);

        SetCanvasGroupVisible(false);

        Debug.Log($"[MsgDisplay] DisplayCoroutine END '{message}' (frame={Time.frameCount})");
        // Process the next message in the queue if there is one
        ProcessNextMessage();
    }

    private void OnDisable()
    {
        Debug.Log($"[MsgDisplay] OnDisable — isDisplayingMessage={isDisplayingMessage} messageQueue.Count={messageQueue.Count}");
    }

    /// <summary>
    /// Fades a canvas group from one alpha value to another over a specified duration
    /// </summary>
    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float startAlpha, float endAlpha, float duration)
    {
        float elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            cg.alpha = Mathf.Lerp(startAlpha, endAlpha, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        cg.alpha = endAlpha;
    }

    /// <summary>
    /// Internal class to store message data in the queue
    /// </summary>
    private class MessageData
    {
        public string Message { get; private set; }
        public Color TextColor { get; private set; }

        public MessageData(string message, Color textColor)
        {
            Message = message;
            TextColor = textColor;
        }
    }

    public static void SetPaused(bool paused)
    {
        displayPaused = paused;
        if (!displayPaused && instance != null && !instance.isDisplayingMessage)
        {
            instance.ProcessNextMessage();
        }
    }

    private void SetPersistent(string message, Color textColor)
    {
        if (waitForSyncRoutine != null)
        {
            StopCoroutine(waitForSyncRoutine);
            waitForSyncRoutine = null;
        }
        StopAllCoroutines();
        messageQueue.Clear();
        isDisplayingMessage = false;
        persistentActive = true;

        messageText.text = message;
        messageText.color = textColor;
        canvasGroup.alpha = 1f;
        SetCanvasGroupVisible(true);
    }

    private void RemovePersistent()
    {
        if (waitForSyncRoutine != null)
        {
            StopCoroutine(waitForSyncRoutine);
            waitForSyncRoutine = null;
        }
        persistentActive = false;
        messageText.text = "";
        canvasGroup.alpha = 0f;
        SetCanvasGroupVisible(false);

        // Anything enqueued while the persistent banner was up (ProcessNextMessage bails out
        // early whenever persistentActive is true) was left sitting in the queue with nothing
        // to wake it back up - without this it would only ever surface if some unrelated later
        // message happened to call ProcessNextMessage again (or the user dug it up via its
        // event icon instead of it just showing up on its own, as Both mode promises).
        if (!isDisplayingMessage) ProcessNextMessage();
    }

    private static bool IsNegativeColor(Color color)
    {
        return color.r >= 0.7f && color.g <= 0.4f;
    }

    private static bool IsPositiveColor(Color color)
    {
        return color.g >= 0.6f && color.b <= 0.6f;
    }

    private static string FormatMessageForDisplay(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        return Regex.Replace(message.Trim(), @"\.\s+", ".\n");
    }
}
