using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-10000)]
public class CardBloomWheel : MonoBehaviour
{
    [Header("Bloom Layout")]
    [SerializeField] private float bloomRadius = 280f;
    [SerializeField] private float startAngleDeg = 180f;
    [SerializeField] private float endAngleDeg = 0f;

    [Header("Animation")]
    [SerializeField] private float bloomSpeed = 14f;
    [SerializeField] private float collapseSpeed = 22f;
    [SerializeField] private float hoverDelay = 2f;

    [Header("Lines")]
    [Tooltip("If assigned, BloomLines is reparented as first sibling under this rect's parent at Start so it renders behind it.")]
    [SerializeField] private RectTransform linesBackgroundTarget;
    [SerializeField] private Vector2 lineEndOffset = Vector2.zero;

    [Header("Card Hover")]
    [SerializeField] private float lineHitTolerance = 20f;

    [Header("Trigger")]
    [Tooltip("RectTransform that opens the bloom on mouse-enter (assign SelectedCharacterIcon's rect).")]
    [SerializeField] private RectTransform hoverTriggerRect;
    [SerializeField] private SelectedCharacterIcon selectedCharacterIcon;

    [Header("Card States")]
    [Tooltip("How much non-hovered tokens darken while one is hovered (0 = none, 1 = black).")]
    [Range(0f, 1f)][SerializeField] private float unhoveredDim = 0.45f;
    [Tooltip("How strongly cards that cannot currently be played shift toward red (0 = none, 1 = full).")]
    [Range(0f, 1f)][SerializeField] private float unplayableRedness = 0.85f;

    [Header("Center Icon")]
    [Tooltip("Shown at the wheel's center while collapsed; clicking it opens the bloom.")]
    [SerializeField] private Sprite centerIconSprite;
    [SerializeField] private float centerIconSize = 72f;

    private readonly List<RectTransform> cardRects = new();
    private readonly List<CanvasGroup> cardGroups = new();
    private readonly List<Card> cardComponents = new();
    private readonly List<Vector2> bloomTargets = new();
    private readonly List<Color> cardLineColors = new();
    private readonly List<float> cardDims = new();
    private readonly List<float> cardRedness = new();

    private bool isOpen;
    private bool isVisible = true;
    private int hoveredCardIndex = -1;
    private RectTransform rectTransform;
    private Canvas parentCanvas;
    private Transform linesGraphicTransform;

    // Bypasses the hover-driven open/close so external callers (e.g. an opportunity-card
    // presentation) can pop the wheel open without the mouse dwelling on the trigger.
    private bool forcedOpen;

    // When set, the wheel tracks a world-space point every frame instead of its authored
    // anchoredPosition — used to park it over a specific hex rather than its usual hand spot.
    private bool useWorldAnchor;
    private Vector3 worldAnchorPosition;
    private Camera worldAnchorCamera;
    private Vector2 homeAnchoredPosition;

    // When set, card clicks are routed here (by index into the list passed to SetCards)
    // instead of Card.PlayFromBloom's hand-play flow. Reset to null by SetCards's default
    // parameter whenever DeckManager repopulates the wheel with hand cards.
    private System.Action<int> externalClickHandler;

    private float cachedRadius;
    private float cachedStartAngle;
    private float cachedEndAngle;
    private float hoverTimer;

    private RectTransform centerIconRect;
    private CanvasGroup centerIconGroup;
    private float centerIconAlpha;

    // Index last handed to CardCenterPreview (see UpdateCenterPreview) — -1 when nothing of
    // ours is currently shown there.
    private int lastPreviewIndex = -1;

    private float UnhoveredDim => unhoveredDim > 0.001f ? unhoveredDim : 0.45f;
    private float UnplayableRedness => unplayableRedness > 0.001f ? unplayableRedness : 0.85f;

    public bool IsOpen => isOpen;
    // 0..1 fraction of the hover dwell completed (1 once open) — drives the loading indicator
    // SelectedCharacterIcon draws at the cursor while the bloom is pending.
    public float HoverProgress => isOpen ? 1f : (hoverDelay > 0f ? Mathf.Clamp01(hoverTimer / hoverDelay) : 1f);
    public float LinesAlpha { get; private set; }
    public Vector2 LineEndOffset => lineEndOffset;
    public IReadOnlyList<RectTransform> CardRects => cardRects;
    public IReadOnlyList<Color> CardLineColors => cardLineColors;
    public RectTransform HoverTriggerRect => hoverTriggerRect;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        homeAnchoredPosition = rectTransform.anchoredPosition;

        var linesGo = new GameObject("BloomLines", typeof(RectTransform));
        linesGo.transform.SetParent(transform, false);
        var linesRect = linesGo.GetComponent<RectTransform>();
        linesRect.anchorMin = Vector2.zero;
        linesRect.anchorMax = Vector2.one;
        linesRect.offsetMin = Vector2.zero;
        linesRect.offsetMax = Vector2.zero;
        linesGo.AddComponent<CardBloomLinesGraphic>().Init(this);
        linesGo.transform.SetAsFirstSibling();
        linesGraphicTransform = linesGo.transform;

        BuildCenterIcon();
    }

    // Purely visual icon shown at the wheel's center while collapsed, so the bloom starts
    // closed. Hidden once open or once there are no cards to show (e.g. before the wheel has
    // ever been populated, so it doesn't appear during load screens). It has no click handling
    // of its own — SituationCardsUI's full-screen dismiss catcher sits behind the whole wheel
    // and owns every click while an offer is up (see its own click handler), so routing "click
    // the icon" through a second, independent raycast target here would race that catcher
    // non-deterministically instead of reliably opening the bloom.
    private void BuildCenterIcon()
    {
        var iconGo = new GameObject("CenterIcon", typeof(RectTransform));
        iconGo.transform.SetParent(transform, false);

        centerIconRect = iconGo.GetComponent<RectTransform>();
        centerIconRect.anchorMin = centerIconRect.anchorMax = centerIconRect.pivot = new Vector2(0.5f, 0.5f);
        centerIconRect.sizeDelta = Vector2.one * centerIconSize;
        centerIconRect.anchoredPosition = Vector2.zero;

        Image iconImage = iconGo.AddComponent<Image>();
        iconImage.sprite = centerIconSprite;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        centerIconGroup = iconGo.AddComponent<CanvasGroup>();
        centerIconGroup.alpha = 0f;
        centerIconAlpha = 0f;

        iconGo.transform.SetAsLastSibling();
    }

    /// <summary>Opens the bloom (clicking the collapsed-state center icon).</summary>
    public void OpenBloom()
    {
        if (!isOpen) SetOpenState(true);
    }

    /// <summary>Collapses the bloom back to just its center icon (used by SituationCardsUI's dismiss catcher when a click lands back on that icon while open — a manual toggle-close alongside the existing auto-close-on-mouse-away).</summary>
    public void CollapseBloom()
    {
        if (isOpen) SetOpenState(false);
    }

    /// <summary>True if the mouse is currently over the collapsed-state center icon.</summary>
    public bool IsCenterIconUnderMouse()
    {
        return centerIconRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(centerIconRect, Input.mousePosition, CanvasCamera());
    }

    private void Start()
    {
        if (linesBackgroundTarget != null && linesGraphicTransform != null)
        {
            linesGraphicTransform.SetParent(linesBackgroundTarget.parent, false);
            var lr = linesGraphicTransform.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero;
            lr.offsetMax = Vector2.zero;
            linesGraphicTransform.SetAsFirstSibling();
        }
    }

    private void OnDestroy()
    {
        if (linesGraphicTransform != null)
            Destroy(linesGraphicTransform.gameObject);
        ClearCenterPreview();
    }

    // Hard teardown: hides whatever CardCenterPreview is currently showing on our behalf.
    // Used when the wheel is rebuilt, hidden, or destroyed (i.e. card played / cancelled).
    private void ClearCenterPreview()
    {
        lastPreviewIndex = -1;
        CardCenterPreview.Instance?.HidePreview();
    }

    private void Update()
    {
        // Capture the click before board/gameplay Update methods can change selection, hide the
        // hand, or collapse this wheel in response to the same mouse-down.
        if (isOpen && Input.GetMouseButtonDown(0))
        {
            int clickedIndex = FindHoveredCardIndex(CanvasCamera());
            if (clickedIndex >= 0) PlayCardAtIndex(clickedIndex);
        }
        // While collapsed, SituationCardsUI's dismiss catcher is inactive (see
        // SyncBloomDismissCatcher) so there's nothing else listening for this click —
        // handle opening directly.
        else if (!isOpen && isVisible && cardRects.Count > 0 && Input.GetMouseButtonDown(0)
            && IsCenterIconUnderMouse())
        {
            OpenBloom();
        }

        if (useWorldAnchor) ApplyWorldAnchor();

        if (!isVisible) return;

#if UNITY_EDITOR
        if (debugForcedOpen)
        {
            if (!isOpen) SetOpenState(true);
            if (bloomRadius != cachedRadius || startAngleDeg != cachedStartAngle || endAngleDeg != cachedEndAngle)
                RecalculateBloomTargets();
            hoveredCardIndex = -1;
            AnimateCards();
            LinesAlpha = 1f;
            return;
        }
#endif

        Camera cam = CanvasCamera();
        Camera triggerCam = TriggerCamera();
        bool characterActed = selectedCharacterIcon != null
            && selectedCharacterIcon.CurrentCharacter != null
            && selectedCharacterIcon.CurrentCharacter.hasActionedThisTurn;
        bool mouseOnTrigger = !characterActed && hoverTriggerRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(hoverTriggerRect, Input.mousePosition, triggerCam);
        bool mouseInArea = !characterActed && isOpen && IsMouseInsideBloomArea(cam, triggerCam);

        if (mouseOnTrigger && !isOpen)
        {
            hoverTimer += Time.deltaTime;
        }
        else if (!mouseOnTrigger && !mouseInArea)
        {
            hoverTimer = 0f;
        }

        bool shouldBeOpen = forcedOpen || (mouseOnTrigger && hoverTimer >= hoverDelay) || mouseInArea;

        if (shouldBeOpen != isOpen)
            SetOpenState(shouldBeOpen);

        if (bloomRadius != cachedRadius || startAngleDeg != cachedStartAngle || endAngleDeg != cachedEndAngle)
            RecalculateBloomTargets();

        hoveredCardIndex = isOpen ? FindHoveredCardIndex(cam) : -1;
        AnimateCards();
        LinesAlpha = 1f;
    }

    // Shared by the wheel's own manual click detection above and by the enlarged center
    // preview's click catcher, so both paths play the exact same (already-validated) token
    // instead of the preview clone re-deriving its own playability.
    private void PlayCardAtIndex(int index)
    {
        if (index < 0 || index >= cardComponents.Count)
        {
            Debug.LogWarning($"[CardPlay/Bloom] PlayCardAtIndex({index}) out of range (cardComponents.Count={cardComponents.Count}).");
            return;
        }

        if (externalClickHandler != null)
        {
            Debug.Log($"[CardPlay/Bloom] Index {index} routed to externalClickHandler (e.g. SituationCardsUI opportunity bloom).");
            externalClickHandler(index);
        }
        else
        {
            Card clickedCard = cardComponents[index];
            if (clickedCard == null)
            {
                Debug.LogWarning($"[CardPlay/Bloom] Index {index} has a null Card component — nothing to play.");
                return;
            }
            Debug.Log($"[CardPlay/Bloom] Index {index} -> PlayFromBloom('{clickedCard.cardData?.name}').");
            clickedCard.PlayFromBloom(selectedCharacterIcon != null ? selectedCharacterIcon.CurrentCharacter : null);
        }
    }

    // Called by DeckManager after spawning / clearing cards. onCardClicked, when supplied,
    // receives the clicked card's index into `cards` instead of the click driving
    // Card.PlayFromBloom's hand-play flow (see SituationCardsUI's bloom presentation).
    public void SetCards(List<GameObject> cards, System.Action<int> onCardClicked = null)
    {
        externalClickHandler = onCardClicked;
        ClearCenterPreview();
        cardRects.Clear();
        cardGroups.Clear();
        cardComponents.Clear();
        bloomTargets.Clear();
        cardLineColors.Clear();
        cardDims.Clear();
        cardRedness.Clear();

        Colors colors = FindFirstObjectByType<Colors>();

        if (cards != null)
        {
            foreach (GameObject go in cards)
            {
                if (go == null) continue;
                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt == null) continue;

                // Bloom targets are offsets from this wheel's centre. TokenCard.prefab is
                // authored with bottom-left anchors, which makes an anchoredPosition of
                // (x, y) land at (x - halfWidth, y - halfHeight) in this 100x100 wheel.
                // That shifted the whole nominal semicircle 50px left/down: the left and
                // right spokes became 330px and 230px respectively, and the end tokens fell
                // below the shared origin. Normalize every supplied token to the coordinate
                // system RecalculateBloomTargets and CardBloomLinesGraphic both use.
                Vector2 center = new(0.5f, 0.5f);
                rt.anchorMin = center;
                rt.anchorMax = center;
                rt.pivot = center;
                cardRects.Add(rt);
                cardGroups.Add(go.GetComponent<CanvasGroup>());

                Card card = go.GetComponent<Card>();
                cardComponents.Add(card);
                if (card != null) card.SuppressHoverEffects = true;

                cardLineColors.Add(ResolveCardColor(card, colors));
                cardDims.Add(0f);
                cardRedness.Add(0f);
            }
        }

        RecalculateBloomTargets();
        SnapAllToCollapsed();
    }

    private static Color ResolveCardColor(Card card, Colors colors)
    {
        if (card == null || card.cardData == null || colors == null) return Color.white;
        string name = card.cardData.GetCardType() switch
        {
            CardTypeEnum.PC => "pc",
            CardTypeEnum.Land => "land",
            CardTypeEnum.Character => "character",
            CardTypeEnum.Army => "army",
            CardTypeEnum.Event => "event",
            CardTypeEnum.Action => "action",
            CardTypeEnum.Spell => "spell",
            CardTypeEnum.Object => "object",
            CardTypeEnum.Encounter => "encounter",
            CardTypeEnum.Environmental => "environmental",
            _ => null
        };
        if (name == null) return Color.white;
        try { return colors.GetColorByName(name); }
        catch { return Color.white; }
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;

        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;

        if (!visible)
        {
            if (isOpen) SetOpenState(false);
            // Update() stops running while hidden, so the preview/backdrop can't fade out
            // on their own — tear them down immediately (covers card played / cancelled).
            ClearCenterPreview();
        }
    }

    // Bypasses the hover-dwell requirement so the wheel can be popped open programmatically
    // (e.g. presenting opportunity cards) — pass false to hand control back to hover-driven
    // opening.
    public void SetForcedOpen(bool open)
    {
        forcedOpen = open;
        if (open) hoverTimer = hoverDelay;
    }

    /// <summary>Closes an open bloom and returns whether Escape was consumed.</summary>
    public bool TryClose()
    {
        if (!isOpen) return false;

        forcedOpen = false;
        hoverTimer = 0f;
        SetOpenState(false);
        ClearCenterPreview();
        return true;
    }

    // Repositions the wheel to track a world-space point every frame (e.g. a hex the acting
    // character stands on) instead of its authored anchoredPosition. worldCamera should be
    // the scene camera the position was captured in (Camera.main for the board).
    public void SetWorldAnchor(Vector3 worldPosition, Camera worldCamera)
    {
        useWorldAnchor = true;
        worldAnchorPosition = worldPosition;
        worldAnchorCamera = worldCamera;
        ApplyWorldAnchor();
    }

    // Restores the wheel to its originally authored anchoredPosition (its usual hand spot).
    public void ClearWorldAnchor()
    {
        useWorldAnchor = false;
        if (rectTransform != null) rectTransform.anchoredPosition = homeAnchoredPosition;
    }

    private void ApplyWorldAnchor()
    {
        if (!useWorldAnchor || rectTransform == null) return;
        if (rectTransform.parent is not RectTransform parentRect) return;

        Camera sceneCam = worldAnchorCamera != null ? worldAnchorCamera : Camera.main;
        if (sceneCam == null) return;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(sceneCam, worldAnchorPosition);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, CanvasCamera(), out Vector2 localPoint))
        {
            rectTransform.anchoredPosition = localPoint;
        }
    }

    private void SetOpenState(bool open)
    {
        isOpen = open;
        if (!open)
        {
            // Don't hard-clear: dropping the hovered index lets CardCenterPreview's own
            // fade-out play normally (via UpdateCenterPreview -> HidePreview) instead of
            // snapping off mid-animation.
            hoveredCardIndex = -1;
        }

        for (int i = 0; i < cardGroups.Count; i++)
        {
            if (cardGroups[i] == null) continue;
            cardGroups[i].blocksRaycasts = open;
            cardGroups[i].interactable = open;
        }

    }

    private void SnapAllToCollapsed()
    {
        isOpen = false;
        hoveredCardIndex = -1;

        centerIconAlpha = (isVisible && cardRects.Count > 0) ? 1f : 0f;
        if (centerIconGroup != null) centerIconGroup.alpha = centerIconAlpha;

        for (int i = 0; i < cardRects.Count; i++)
        {
            if (cardRects[i] != null)
            {
                cardRects[i].anchoredPosition = Vector2.zero;
                cardRects[i].localScale = Vector3.one;
            }

            if (i < cardGroups.Count && cardGroups[i] != null)
            {
                cardGroups[i].alpha = 0f;
                cardGroups[i].blocksRaycasts = false;
                cardGroups[i].interactable = false;
            }

            if (i < cardDims.Count)
            {
                cardDims[i] = 0f;
                if (i < cardRedness.Count) cardRedness[i] = 0f;
                if (i < cardComponents.Count && cardComponents[i] != null) cardComponents[i].SetTokenTint(0f, 0f);
            }
        }
    }

    private void RecalculateBloomTargets()
    {
        cachedRadius = bloomRadius;
        cachedStartAngle = startAngleDeg;
        cachedEndAngle = endAngleDeg;

        bloomTargets.Clear();
        int n = cardRects.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            float t = n > 1 ? (float)i / (n - 1) : 0.5f;
            float angleDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
            float rad = angleDeg * Mathf.Deg2Rad;
            bloomTargets.Add(new Vector2(
                bloomRadius * Mathf.Cos(rad),
                bloomRadius * Mathf.Sin(rad)
            ));
        }
    }

    private void AnimateCards()
    {
        float speed = isOpen ? bloomSpeed : collapseSpeed;

        for (int i = 0; i < cardRects.Count; i++)
        {
            if (cardRects[i] == null) continue;

            // Every card stays visible at all times — hovering no longer hides siblings.
            if (!cardRects[i].gameObject.activeSelf)
                cardRects[i].gameObject.SetActive(true);

            // Position
            Vector2 posTarget = isOpen && i < bloomTargets.Count ? bloomTargets[i] : Vector2.zero;
            cardRects[i].anchoredPosition = Vector2.Lerp(
                cardRects[i].anchoredPosition, posTarget, Time.deltaTime * speed);

            // Cards in the wheel are always tokens; the hovered card is mirrored as a
            // real card in the center preview instead of flipping in place.
            Card card = i < cardComponents.Count ? cardComponents[i] : null;
            if (card != null)
            {
                card.ShowToken();

                // Two tint channels: darken the tokens the player is NOT pointing at while
                // one is hovered (focus), and shift unplayable cards toward red
                // (availability) — transparency stays reserved for the wheel's open/close.
                if (i < cardDims.Count && i < cardRedness.Count)
                {
                    float dimTarget = isOpen && hoveredCardIndex >= 0 && i != hoveredCardIndex ? UnhoveredDim : 0f;
                    float redTarget = isOpen && !card.LastKnownPlayable ? UnplayableRedness : 0f;
                    cardDims[i] = Mathf.Lerp(cardDims[i], dimTarget, Time.deltaTime * speed);
                    cardRedness[i] = Mathf.Lerp(cardRedness[i], redTarget, Time.deltaTime * speed);
                    card.SetTokenTint(cardDims[i], cardRedness[i]);
                }
            }

            // Alpha — only the wheel's fade in/out.
            if (i < cardGroups.Count && cardGroups[i] != null)
            {
                float alphaTarget = isOpen ? 1f : 0f;
                cardGroups[i].alpha = Mathf.Lerp(cardGroups[i].alpha, alphaTarget, Time.deltaTime * speed * 1.5f);
            }
        }

        UpdateCenterPreview(isOpen ? hoveredCardIndex : -1);
        UpdateCenterIcon(speed);
    }

    private void UpdateCenterIcon(float speed)
    {
        if (centerIconGroup == null) return;

        // Only ever visible once the wheel actually has an offer to show — otherwise it would
        // sit at the wheel's authored position (e.g. over loading screens) with nothing behind it.
        float target = (isOpen || !isVisible || cardRects.Count == 0) ? 0f : 1f;
        centerIconAlpha = Mathf.Lerp(centerIconAlpha, target, Time.deltaTime * speed * 1.5f);
        centerIconGroup.alpha = centerIconAlpha;
    }

    // Shows the hovered card's full face via the shared CardCenterPreview singleton (same
    // fly-in/backdrop/hover-safety-net every other card-hover site uses), leaving the wheel's
    // own tokens untouched. Previously this cloned the hovered token and flipped it to its
    // RealCard face in place — broken once tokens moved to their own token-only prefab with no
    // RealCard subtree to flip to, and a duplicate of CardCenterPreview besides.
    private void UpdateCenterPreview(int index)
    {
        if (index == lastPreviewIndex) return;
        lastPreviewIndex = index;

        CardData data = (index >= 0 && index < cardComponents.Count) ? cardComponents[index]?.cardData : null;
        if (data == null)
        {
            CardCenterPreview.Instance?.HidePreview();
            return;
        }

        // When this wheel is world-anchored (the SituationCardsUI opportunity-card bloom, parked
        // over a hex rather than its usual hand-tray spot), the enlarged preview must center on
        // that same world point — otherwise it shows at its own unrelated fixed screen anchor
        // while the ring of tokens it "bloomed" from surrounds a completely different spot.
        if (useWorldAnchor)
            CardCenterPreview.Instance?.ShowPreview(data, hoverDriven: true, worldAnchor: worldAnchorPosition, worldAnchorCamera: worldAnchorCamera, onClick: () => PlayCardAtIndex(index));
        else
            CardCenterPreview.Instance?.ShowPreview(data, hoverDriven: true, onClick: () => PlayCardAtIndex(index));
    }

    private int FindHoveredCardIndex(Camera cam)
    {
        Vector2 mouse = Input.mousePosition;
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < cardRects.Count; i++)
        {
            if (cardRects[i] == null || !cardRects[i].gameObject.activeSelf) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(cardRects[i], mouse, cam)) continue;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(cam, cardRects[i].position);
            float dist = (mouse - center).sqrMagnitude;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }

    private Camera CanvasCamera()
    {
        if (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            return parentCanvas.worldCamera;
        return null;
    }

    private Camera TriggerCamera()
    {
        if (hoverTriggerRect == null) return null;
        Canvas c = hoverTriggerRect.GetComponentInParent<Canvas>();
        if (c == null || c.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return c.worldCamera;
    }

    private bool IsMouseInsideBloomArea(Camera cam, Camera triggerCam)
    {
        for (int i = 0; i < cardRects.Count; i++)
        {
            if (cardRects[i] == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(cardRects[i], Input.mousePosition, cam))
                return true;
        }

        if (hoverTriggerRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(hoverTriggerRect, Input.mousePosition, triggerCam))
            return true;

        if (IsMouseNearLines(cam, triggerCam)) return true;

        return rectTransform != null &&
               RectTransformUtility.RectangleContainsScreenPoint(rectTransform, Input.mousePosition, cam);
    }

    private bool IsMouseNearLines(Camera cam, Camera triggerCam)
    {
        if (hoverTriggerRect == null || cardRects.Count == 0) return false;
        Vector2 triggerScreen = RectTransformUtility.WorldToScreenPoint(triggerCam,
            hoverTriggerRect.TransformPoint(hoverTriggerRect.rect.center));
        Vector2 mouse = Input.mousePosition;
        for (int i = 0; i < cardRects.Count; i++)
        {
            if (cardRects[i] == null) continue;
            Vector2 cardScreen = RectTransformUtility.WorldToScreenPoint(cam, cardRects[i].position);
            if (DistanceToSegment(mouse, triggerScreen, cardScreen) <= lineHitTolerance)
                return true;
        }
        return false;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        if (ab.sqrMagnitude < 0.001f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + t * ab);
    }

#if UNITY_EDITOR
    private bool debugForcedOpen;

    public void DebugForceOpen()
    {
        debugForcedOpen = true;
        isVisible = true;
        hoverTimer = 0f;
        if (!isOpen) SetOpenState(true);
    }

    public void DebugForceClose()
    {
        debugForcedOpen = false;
        hoverTimer = 0f;
        if (isOpen) SetOpenState(false);
    }

    private const string PreviewPrefix = "BloomPreview_";
    [SerializeField] private int editorPreviewCardCount = 5;

    public void EditorPreviewBloom()
    {
        CanvasGroup rootCg = GetComponent<CanvasGroup>();
        if (rootCg != null) { rootCg.alpha = 1f; rootCg.interactable = true; rootCg.blocksRaycasts = true; }

        List<RectTransform> rects = CollectActiveChildRects(out List<CanvasGroup> groups);

        if (rects.Count == 0)
        {
            for (int i = 0; i < editorPreviewCardCount; i++)
            {
                var go = new GameObject($"{PreviewPrefix}{i}", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Preview Bloom");
                go.transform.SetParent(transform, false);
                var img = go.GetComponent<UnityEngine.UI.Image>();
                img.color = new Color(0.4f, 0.7f, 1f, 0.5f);
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(80f, 110f);
                rects.Add(rt);
                groups.Add(null);
            }
        }

        int n = rects.Count;
        for (int i = 0; i < n; i++)
        {
            UnityEditor.Undo.RecordObject(rects[i], "Preview Bloom");
            float t = n > 1 ? (float)i / (n - 1) : 0.5f;
            float angleDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
            float rad = angleDeg * Mathf.Deg2Rad;
            rects[i].anchoredPosition = new Vector2(bloomRadius * Mathf.Cos(rad), bloomRadius * Mathf.Sin(rad));
            rects[i].localScale = Vector3.one;
            if (groups[i] != null) { UnityEditor.Undo.RecordObject(groups[i], "Preview Bloom"); groups[i].alpha = 1f; }
            UnityEditor.EditorUtility.SetDirty(rects[i].gameObject);
        }

        RefreshLinesGraphic();
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }

    public void EditorResetBloom()
    {
        DestroyPreviewChildren();
        List<RectTransform> rects = CollectActiveChildRects(out List<CanvasGroup> groups);
        for (int i = 0; i < rects.Count; i++)
        {
            UnityEditor.Undo.RecordObject(rects[i], "Reset Bloom");
            rects[i].anchoredPosition = Vector2.zero;
            rects[i].localScale = Vector3.one;
            if (groups[i] != null) { UnityEditor.Undo.RecordObject(groups[i], "Reset Bloom"); groups[i].alpha = 0f; }
            UnityEditor.EditorUtility.SetDirty(rects[i].gameObject);
        }

        RefreshLinesGraphic();
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }

    private void RefreshLinesGraphic()
    {
        CardBloomLinesGraphic graphic = GetComponentInChildren<CardBloomLinesGraphic>();
        if (graphic == null)
        {
            var linesGo = new GameObject("BloomLines", typeof(RectTransform));
            UnityEditor.Undo.RegisterCreatedObjectUndo(linesGo, "Preview Bloom");
            linesGo.transform.SetParent(transform, false);
            var linesRect = linesGo.GetComponent<RectTransform>();
            linesRect.anchorMin = Vector2.zero;
            linesRect.anchorMax = Vector2.one;
            linesRect.offsetMin = Vector2.zero;
            linesRect.offsetMax = Vector2.zero;
            graphic = linesGo.AddComponent<CardBloomLinesGraphic>();
            linesGo.transform.SetAsFirstSibling();
        }
        graphic.Init(this);
        graphic.SetAllDirty();
    }

    public List<RectTransform> GetEditorPreviewRects()
    {
        var rects = new List<RectTransform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.GetComponent<CardBloomLinesGraphic>() != null) continue;
            RectTransform rt = child.GetComponent<RectTransform>();
            if (rt != null) rects.Add(rt);
        }
        return rects;
    }

    private void DestroyPreviewChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith(PreviewPrefix))
                UnityEditor.Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private List<RectTransform> CollectActiveChildRects(out List<CanvasGroup> groups)
    {
        var rects = new List<RectTransform>();
        groups = new List<CanvasGroup>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            if (child.GetComponent<CardBloomLinesGraphic>() != null) continue;
            if (child.name.StartsWith(PreviewPrefix)) continue;
            RectTransform rt = child.GetComponent<RectTransform>();
            if (rt == null) continue;
            rects.Add(rt);
            groups.Add(child.GetComponent<CanvasGroup>());
        }
        return rects;
    }
#endif
}
