using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BoardNavigator : MonoBehaviour
{
    public static BoardNavigator Instance { get; private set; }

    [Header("Move")]
    public float moveSpeed = 5.0f;

    [Header("Zoom")]
    public float zoomSpeed = 10.0f;
    public float smoothTime = 10f;

    [Tooltip("Minimum orthographic size (prevents camera from going flat)")]
    public float minZoom = 0.5f;

    [Tooltip("Maximum orthographic size (prevents excessive zoom-out)")]
    public float maxZoom = 20f;

    private bool isMouseWheelHeld = false;
    private Camera boardCamera;
    private Coroutine lookAtCoroutine;
    private float targetZoom;
    private static readonly List<RaycastResult> raycastResults = new(16);
    private static PointerEventData sharedPED;
    private readonly Queue<FocusRequest> focusQueue = new();
    private Coroutine focusQueueRoutine;

    [Header("Enemy Follow")]
    [SerializeField] private float enemyFocusDuration = 0.04f;
    [SerializeField] private float enemyFocusPause = 0.006f;
    [SerializeField] private float lookAtGlobalSpeedMultiplier = 5f;

    [Header("Discovered-area clamp")]
    [Tooltip("Extra world-space slack allowed beyond the discovered hexes, as a fraction of the camera's half-height. " +
             "1 = the discovered edge may reach the screen edge before the camera stops.")]
    [SerializeField] private float discoveredEdgeSlack = 0.9f;
    [Tooltip("Minimum world-space slack allowed beyond the discovered hexes.")]
    [SerializeField] private float discoveredMinMargin = 1.5f;
    [Tooltip("Seconds between recomputing the discovered-area bounds.")]
    [SerializeField] private float discoveredBoundsRefreshInterval = 0.25f;

    private Board boardForBounds;
    private bool hasDiscoveredBounds;
    private Vector2 discoveredMin;
    private Vector2 discoveredMax;
    private float nextDiscoveredBoundsRefresh;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        boardCamera = GetComponent<Camera>();

        if (boardCamera != null && boardCamera.orthographic)
        {
            // Initialize the target zoom so it matches the current camera size
            targetZoom = Mathf.Clamp(boardCamera.orthographicSize, minZoom, maxZoom);
        }
    }

    void Update()
    {
        if (IsNavigationInputLocked())
        {
            if (isMouseWheelHeld)
            {
                isMouseWheelHeld = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }

        if (IsPointerOverVisibleUIElement()) return;

        // Middle mouse button (wheel) pans the board
        if (Input.GetMouseButtonDown(2))
        {
            isMouseWheelHeld = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (Input.GetMouseButtonUp(2))
        {
            isMouseWheelHeld = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (isMouseWheelHeld)
            HandleMovement();
        else
            HandleZoom();
    }

    void LateUpdate()
    {
        // Keep the camera within (a margin of) the bounding box of the discovered hexes.
        // This runs on BoardNavigator.Instance, which is the live camera that all panning
        // moves, so it clamps reliably regardless of how the scene cameras are wired.
        ClampToDiscoveredBounds();
    }

    private void ClampToDiscoveredBounds()
    {
        if (Time.unscaledTime >= nextDiscoveredBoundsRefresh)
        {
            RefreshDiscoveredBounds();
            nextDiscoveredBoundsRefresh = Time.unscaledTime + Mathf.Max(0.05f, discoveredBoundsRefreshInterval);
        }

        if (!hasDiscoveredBounds) return;

        float half = boardCamera != null && boardCamera.orthographic ? boardCamera.orthographicSize : discoveredMinMargin;
        float marginY = Mathf.Max(discoveredMinMargin, half * discoveredEdgeSlack);
        float marginX = Mathf.Max(discoveredMinMargin, half * discoveredEdgeSlack * (boardCamera != null ? boardCamera.aspect : 1f));

        Vector3 pos = transform.position;
        float x = Mathf.Clamp(pos.x, discoveredMin.x - marginX, discoveredMax.x + marginX);
        float y = Mathf.Clamp(pos.y, discoveredMin.y - marginY, discoveredMax.y + marginY);

        if (!Mathf.Approximately(x, pos.x) || !Mathf.Approximately(y, pos.y))
        {
            transform.position = new Vector3(x, y, pos.z);
        }
    }

    private void RefreshDiscoveredBounds()
    {
        if (boardForBounds == null) boardForBounds = FindAnyObjectByType<Board>();
        if (boardForBounds == null || boardForBounds.hexes == null)
        {
            hasDiscoveredBounds = false;
            return;
        }

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;
        foreach (Hex hex in boardForBounds.hexes.Values)
        {
            if (hex == null || !hex.IsHexRevealed()) continue;
            Vector3 p = hex.transform.position;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
            any = true;
        }

        hasDiscoveredBounds = any;
        if (any)
        {
            discoveredMin = new Vector2(minX, minY);
            discoveredMax = new Vector2(maxX, maxY);
        }
    }

    void HandleMovement()
    {
        float moveX = Input.GetAxis("Mouse X");
        float moveY = Input.GetAxis("Mouse Y");

        Vector3 move = new Vector3(moveX, moveY, 0);
        transform.position += move * moveSpeed * Time.deltaTime;
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (boardCamera != null && boardCamera.orthographic)
        {
            if (Mathf.Abs(scroll) > 0.001f)
            {
                targetZoom -= scroll * zoomSpeed;
                targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
            }

            boardCamera.orthographicSize = Mathf.Lerp(
                boardCamera.orthographicSize,
                targetZoom,
                Time.deltaTime * smoothTime
            );

            // Absolute safeguard
            boardCamera.orthographicSize = Mathf.Clamp(boardCamera.orthographicSize, minZoom, maxZoom);
        }
    }

    public void LookAt(Vector3 targetPosition, float duration = 1.0f, float delay = 0.0f)
    {
        if (lookAtCoroutine != null)
            StopCoroutine(lookAtCoroutine);

        float speed = Mathf.Max(0.01f, lookAtGlobalSpeedMultiplier);
        float scaledDuration = Mathf.Max(0.01f, duration / speed);
        float scaledDelay = Mathf.Max(0f, delay / speed);
        lookAtCoroutine = StartCoroutine(SmoothLookAt(targetPosition, scaledDuration, scaledDelay));
    }

    public void EnqueueEnemyFocus(Hex hex, Leader leader = null)
    {
        Game game = FindAnyObjectByType<Game>();
        if (game != null && game.player != null && game.IsHumanActivelyActing())
        {
            EnqueueFocus(hex, enemyFocusDuration, enemyFocusPause);
            return;
        }
        if (game != null)
        {
            game.QueueNpcFocus(leader, hex);
        }
    }

    public void EnqueueMessageFocus(Hex hex, System.Action onComplete = null)
    {
        EnqueueFocus(hex, enemyFocusDuration, enemyFocusPause, true, () =>
        {
            onComplete?.Invoke();
            EnqueueReturnToSelected();
        });
    }

    public void EnqueueNpcPlaybackFocus(Hex hex)
    {
        EnqueueFocus(hex, enemyFocusDuration, enemyFocusPause, true);
    }

    public void EnqueueFocus(Hex hex, float duration, float pause, bool allowDuringMessages = false, System.Action onComplete = null)
    {
        if (hex == null) return;
        float speed = Mathf.Max(0.01f, lookAtGlobalSpeedMultiplier);
        focusQueue.Enqueue(new FocusRequest
        {
            hex = hex,
            duration = Mathf.Max(0.01f, duration / speed),
            pause = Mathf.Max(0f, pause / speed),
            allowDuringMessages = allowDuringMessages,
            onComplete = onComplete
        });
        if (focusQueueRoutine == null)
        {
            focusQueueRoutine = StartCoroutine(ProcessFocusQueue());
        }
    }

    private IEnumerator SmoothLookAt(Vector3 targetPosition, float duration = 1.0f, float delay = 0.0f)
    {
        Vector3 startPosition = transform.position;
        targetPosition.z = startPosition.z;
        float journeyLength = Vector3.Distance(startPosition, targetPosition);

        if (journeyLength < 0.001f)
        {
            lookAtCoroutine = null;
            yield break;
        }

        float delayElapsed = 0f;
        while (delayElapsed < delay)
        {
            if (ShouldPauseFocus())
            {
                yield return null;
                continue;
            }
            delayElapsed += Time.deltaTime;
            yield return null;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (ShouldPauseFocus())
            {
                yield return null;
                continue;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / duration);

            Vector3 newPosition = Vector3.Lerp(startPosition, targetPosition, t);
            if (!float.IsNaN(newPosition.x) && !float.IsNaN(newPosition.y) && !float.IsNaN(newPosition.z))
                transform.position = newPosition;

            yield return null;
        }

        transform.position = targetPosition;
        lookAtCoroutine = null;
    }

    private IEnumerator ProcessFocusQueue()
    {
        while (focusQueue.Count > 0)
        {
            while (MessageDisplayNoUI.IsHoldingFocus)
            {
                yield return null;
            }

            FocusRequest request = focusQueue.Dequeue();
            if (request.hex == null) continue;
            if (ShouldSkipFocusHex(request.hex))
            {
                request.onComplete?.Invoke();
                continue;
            }

            if (!request.allowDuringMessages && MessageDisplayNoUI.IsBusy() && !MessageDisplayNoUI.IsDisplaying)
            {
                focusQueue.Enqueue(request);
                yield return null;
                continue;
            }

            while (ShouldPauseFocus()
                || (!request.allowDuringMessages && MessageDisplayNoUI.IsBusy())
                || (request.allowDuringMessages && MessageDisplayNoUI.IsDisplaying))
            {
                yield return null;
            }

            Vector3 targetPosition = request.hex.transform.position;
            if (lookAtCoroutine != null)
            {
                StopCoroutine(lookAtCoroutine);
                lookAtCoroutine = null;
            }
            lookAtCoroutine = StartCoroutine(SmoothLookAt(targetPosition, request.duration, 0.0f));
            while (lookAtCoroutine != null)
            {
                yield return null;
            }

            if (request.pause > 0f)
            {
                float pauseElapsed = 0f;
                while (pauseElapsed < request.pause)
                {
                    if (ShouldPauseFocus())
                    {
                        yield return null;
                        continue;
                    }
                    pauseElapsed += Time.deltaTime;
                    yield return null;
                }
            }

            request.onComplete?.Invoke();
        }

        focusQueueRoutine = null;
    }

    private struct FocusRequest
    {
        public Hex hex;
        public float duration;
        public float pause;
        public bool allowDuringMessages;
        public System.Action onComplete;
    }

    private void EnqueueReturnToSelected()
    {
        Board board = FindAnyObjectByType<Board>();
        if (board == null || board.selectedCharacter == null) return;
        Hex selectedHex = board.selectedCharacter.hex;
        if (selectedHex == null || !selectedHex.IsHexSeen()) return;
        EnqueueFocus(selectedHex, enemyFocusDuration, 0f, false);
    }

    public bool HasPendingFocus()
    {
        return focusQueue.Count > 0 || focusQueueRoutine != null || lookAtCoroutine != null;
    }

    // Set the instant any single flag below is the one holding IsNavigationInputLocked() true,
    // logged once on the false->true transition, plus a periodic heartbeat every few seconds
    // while it stays locked (see the diagnostic block at the bottom of this method) — a single
    // edge-triggered log misses the case where one flag drops and another picks up the lock in
    // the same frame (the aggregate OR never dips to false), which was exactly what happened
    // investigating this: TurnBanner released cleanly but the log tail still showed no further
    // [NavLock] lines because something else silently took over. The heartbeat guarantees
    // whatever tail gets pasted next shows a *current* breakdown, not just the original trigger.
    private static bool s_wasLocked;
    private static float s_nextHeartbeatTime;

    public static bool IsNavigationInputLocked()
    {
        bool popupActive = PopupManager.IsShowing || ConfirmationDialog.IsShowing || SelectionDialog.IsShowing;
        bool focusQueued = Instance != null && Instance.HasPendingFocus();
        bool messageUiShowing = MessageDisplay.IsDisplaying();
        bool messageNoUiShowing = MessageDisplayNoUI.IsDisplaying;
        // TurnBanner.IsShowing covers both the "TURN X" banner and the Gathering Resources
        // banner that follows it (same CenterDisplayLock-backed sequence); instructions are
        // the onboarding queue Game.StartGame holds turn 0 open for. Both mean the game is
        // meant to be fully paused, so no board/keyboard input should get through either.
        bool bannerShowing = TurnBanner.IsShowing;
        bool instructionsShowing = TutorialInstructionsManager.Instance.IsShowing;
        bool startupPopupBlocked = IsStartupPopupLookAtBlocked();
        bool locked = popupActive || focusQueued || messageUiShowing || messageNoUiShowing || bannerShowing || instructionsShowing || startupPopupBlocked;

        bool dueForHeartbeat = locked && s_wasLocked && Time.unscaledTime >= s_nextHeartbeatTime;
        if ((locked && !s_wasLocked) || dueForHeartbeat)
        {
            string tag = dueForHeartbeat ? "HEARTBEAT (still locked)" : "ENGAGED";
            Debug.Log($"[NavLock] {tag} — popupActive={popupActive} focusQueued={focusQueued} " +
                $"(focusQueue={Instance?.focusQueue.Count ?? -1} focusQueueRoutine={Instance?.focusQueueRoutine != null} lookAtCoroutine={Instance?.lookAtCoroutine != null}) " +
                $"messageUiShowing={messageUiShowing} messageNoUiShowing={messageNoUiShowing} " +
                $"messageNoUiHoldingFocus={MessageDisplayNoUI.IsHoldingFocus} messageNoUiFocusHoldCount={MessageDisplayNoUI.FocusHoldCountDebug} " +
                $"bannerShowing={bannerShowing} instructionsShowing={instructionsShowing} startupPopupBlocked={startupPopupBlocked}");
            s_nextHeartbeatTime = Time.unscaledTime + 3f;
        }
        else if (!locked && s_wasLocked)
        {
            Debug.Log("[NavLock] cleared.");
        }
        s_wasLocked = locked;

        return locked;
    }

    public void LookAtSelected()
    {
        Board board = FindAnyObjectByType<Board>();
        if (board != null && board.selectedCharacter != null && board.selectedCharacter.hex != null)
        {
            LookAt(board.selectedCharacter.hex.transform.position, 1.0f, 0.0f);
        }
    }

    public void ClampToLastValidPosition(Vector3 lastValidPosition, Vector2Int lastHitHexCoords)
    {
        var desiredPosition = transform.position;
        var attemptedDelta = desiredPosition - lastValidPosition;
        const float epsilon = 0.0001f;
        bool clamped = false;

        if (attemptedDelta.x > epsilon)
        {
            desiredPosition.x = lastValidPosition.x;
            clamped = true;
        }
        else if (attemptedDelta.x < -epsilon)
        {
            desiredPosition.x = lastValidPosition.x;
            clamped = true;
        }

        if (attemptedDelta.y > epsilon)
        {
            desiredPosition.y = lastValidPosition.y;
            clamped = true;
        }
        else if (attemptedDelta.y < -epsilon)
        {
            desiredPosition.y = lastValidPosition.y;
            clamped = true;
        }

        if (!clamped) return;

        transform.position = desiredPosition;

        /*if (lastHitHexCoords.x >= 0 && lastHitHexCoords.y >= 0)
        {
            Debug.Log($"Cannot move past hex {lastHitHexCoords.x},{lastHitHexCoords.y}; movement clamped to board bounds.");
        }
        else
        {
            Debug.Log("Cannot move further in that direction; movement clamped to board bounds.");
        }
        */
    }

    public static bool IsPointerOverVisibleUIElement()
    {
        if (EventSystem.current == null) return false;

        if (sharedPED == null) sharedPED = new PointerEventData(EventSystem.current);
        sharedPED.position = Input.mousePosition;

        raycastResults.Clear();
        EventSystem.current.RaycastAll(sharedPED, raycastResults);

        for (int i = 0, n = raycastResults.Count; i < n; i++)
        {
            var go = raycastResults[i].gameObject;
            if (go.TryGetComponent<Canvas>(out _)) continue;

            if (go.TryGetComponent<Image>(out var img))
                if (img.raycastTarget && img.color.a > 0.01f) return true;

            if (go.TryGetComponent<TextMeshProUGUI>(out var tmp) && tmp.color.a > 0.01f)
                return true;
        }
        return false;
    }

    private static bool ShouldPauseFocus()
    {
        return PopupManager.IsShowing || ConfirmationDialog.IsShowing || SelectionDialog.IsShowing || MessageDisplayNoUI.IsDisplaying || IsStartupPopupLookAtBlocked();
    }

    private static bool IsStartupPopupLookAtBlocked()
    {
        Game game = Game.Instance;
        return game != null && game.ShouldBlockLookAtUntilStartupPopupCloses();
    }

    private static bool ShouldSkipFocusHex(Hex hex)
    {
        return hex == null || (!hex.IsHexSeen() && !hex.IsHexRevealed());
    }
}
