using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class Card : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private static readonly List<Card> activeCards = new();

    public static void RequestInteractionRefreshAll()
    {
        for (int i = 0; i < activeCards.Count; i++)
        {
            if (activeCards[i] != null)
            {
                activeCards[i].UpdateInteractableState();
            }
        }
    }

    [Header("UI References")]
    [FormerlySerializedAs("title")]
    [SerializeField] private TextMeshProUGUI titleText;
    [FormerlySerializedAs("description")]
    [SerializeField] private TextMeshProUGUI descriptionText;
    [FormerlySerializedAs("type")]
    [SerializeField] private Hover hover;
    [FormerlySerializedAs("requirements")]
    [SerializeField] private TextMeshProUGUI requirementsText;
    [FormerlySerializedAs("image")]
    [SerializeField] private Image cardArtImage;
    [FormerlySerializedAs("borderImage")]
    [SerializeField] private Image cardBackgroundImage;
    [SerializeField] private GameObject discardButton;
    [SerializeField] private Image deckTypeImage;

    [Header("Token / Card Flip")]
    [SerializeField] private Image tokenImage;
    [SerializeField] private Image tokenBorder;
    [SerializeField] private CanvasGroup tokenCanvasGroup;
    [SerializeField] private CanvasGroup realCardCanvasGroup;
    [SerializeField] private TextMeshProUGUI environmentalSprite;

    [Header("Tuning")]
    [SerializeField] private Color requirementsMessageColor = Color.red;
    [SerializeField] private bool showRequirementWarnings = true;
    [SerializeField] private bool showCloseIcon = true;
    [Tooltip("If false, the card's description text appears instantly instead of being typed out.")]
    [SerializeField] private bool typewriterEffect = true;
    [Tooltip("True on TokenCard instances that only carry the compact token visual: hovering unfolds the card into CardCenterPreview instead of flipping the (absent) RealCard subtree in place.")]
    [SerializeField] private bool isTokenOnlyPresentation;

    public CardData cardData { get; private set; }

    // Refreshed by UpdateInteractableState (RequestInteractionRefreshAll runs it on every
    // relevant state change); read by CardBloomWheel each frame.
    public bool LastKnownPlayable { get; private set; } = true;
    public bool IsPlayInProgress { get; private set; }

    private CanvasGroup canvasGroup;
    private LayoutElement layoutElement;
    private RectTransform rectTransform;
    private Graphic rootHitGraphic;
    public bool SuppressHoverEffects { get; set; }
    public bool UseCardArtFolderOnly { get; set; }
    public bool TypewriterEffect
    {
        get => typewriterEffect;
        set => typewriterEffect = value;
    }
    public bool ShowRequirementWarnings
    {
        get => showRequirementWarnings;
        set
        {
            showRequirementWarnings = value;
            if (cardData != null) UpdateInteractableState();
        }
    }
    public bool ShowCloseIcon
    {
        get => showCloseIcon;
        set
        {
            showCloseIcon = value;
            UpdateDiscardButtonState();
        }
    }
    private bool lockedToRealCard;
    private string baseDescription = string.Empty;
    private Image encounterArtOverlay;
    private TextMeshProUGUI encounterQuestionMark;
    private Image encounterTokenOverlay;
    private TextMeshProUGUI encounterTokenQuestionMark;
    private Coroutine descriptionTypewriterCoroutine;
    private bool isEnvironmentalPresentation;
    private bool environmentalPreviewHovered;

    private static Illustrations illustrations;
    private static DeckManager deckManager;
    private static ActionsManager actionsManager;
    private static Colors colors;
    private static CursorManager cursorManager;
    private static HexPathRenderer pathRenderer;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        canvasGroup.alpha = 1f;

        layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = gameObject.AddComponent<LayoutElement>();
        }
        layoutElement.ignoreLayout = false;

        rectTransform = GetComponent<RectTransform>();
        rootHitGraphic = GetComponent<Graphic>();
        EnsureTokenHoverHitArea();

        BindLegacyPrefabReferences();
        RestrictRaycastsToRootCard();
        UpdateDiscardButtonState();

        activeCards.Add(this);
    }

    private void OnEnable()
    {
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        if (layoutElement == null)
        {
            layoutElement = GetComponent<LayoutElement>();
        }
        if (layoutElement != null)
        {
            layoutElement.ignoreLayout = false;
        }

        BindLegacyPrefabReferences();
        RestrictRaycastsToRootCard();
        if (cardData != null)
        {
            UpdateInteractableState();
        }
    }

    private void OnDestroy()
    {
        activeCards.Remove(this);
    }

    private void Update()
    {
        if (!isEnvironmentalPresentation) return;

        CardData previewData = cardData ?? EnvironmentalCardManager.Instance?.ActiveCard;
        if (previewData == null) return;

        // Must resolve to the root canvas, not whichever nested sort-order Canvas happens to sit
        // closest in the hierarchy - a nested Canvas added purely for sortingOrder overrides
        // defaults to ScreenSpaceOverlay/no camera, which silently breaks screen-point
        // containment math if the actual root canvas renders via a camera. CardCenterPreview
        // already does this same rootCanvas resolution when placing the preview.
        Canvas canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        Vector2 pointer = Input.mousePosition;
        bool hovered = IsPointerInside(rectTransform, pointer, eventCamera)
            || IsPointerInside(tokenImage != null ? tokenImage.rectTransform : null, pointer, eventCamera)
            || IsPointerInside(tokenBorder != null ? tokenBorder.rectTransform : null, pointer, eventCamera)
            || IsPointerInside(environmentalSprite != null ? environmentalSprite.rectTransform : null, pointer, eventCamera);

        if (hovered == environmentalPreviewHovered) return;
        environmentalPreviewHovered = hovered;

        CardCenterPreview preview = CardCenterPreview.Instance != null
            ? CardCenterPreview.Instance
            : FindFirstObjectByType<CardCenterPreview>();
        if (hovered) preview?.ShowPreview(previewData, hoverDriven: true);
        else preview?.HidePreview();
    }

    private static bool IsPointerInside(RectTransform target, Vector2 pointer, Camera eventCamera)
    {
        return target != null
            && target.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(target, pointer, eventCamera);
    }

    private void OnDisable()
    {
        if (!environmentalPreviewHovered) return;
        environmentalPreviewHovered = false;
        CardCenterPreview.Instance?.HidePreview();
    }

    private void Start()
    {
        EnsureManagersLoaded();
    }

    private static void EnsureManagersLoaded()
    {
        if (illustrations == null) illustrations = FindFirstObjectByType<Illustrations>();
        if (deckManager == null) deckManager = DeckManager.Instance;
        if (actionsManager == null) actionsManager = ActionsManager.Instance;
        if (colors == null) colors = FindFirstObjectByType<Colors>();
        if (cursorManager == null) cursorManager = FindFirstObjectByType<CursorManager>();
        if (pathRenderer == null) pathRenderer = FindFirstObjectByType<HexPathRenderer>();
    }

    // The pulse effect (which image, where it sits) is authored directly in the prefab as a
    // CardEnvironmentalPulseEffect component on a child Image; this just toggles it.
    public void SetEnvironmentalPulse(bool active)
    {
        GameObject target = tokenCanvasGroup != null ? tokenCanvasGroup.gameObject : gameObject;
        CardEnvironmentalPulseEffect pulse = target.GetComponentInChildren<CardEnvironmentalPulseEffect>(true);
        if (pulse != null) pulse.enabled = active;
    }

    public void Initialize(CardData data, bool startAsToken = true)
    {
        cardData = data;
        EnsureManagersLoaded();
        BindLegacyPrefabReferences();
        RestrictRaycastsToRootCard();

        if (titleText != null) titleText.text = FormatCardTitle(data.name);
        if (hover != null) hover.Initialize(FormatCardTypeLabel(data.GetCardType()));
        ApplyCardTypeColor(data.GetCardType());

        // Only the active environmental card (Layout's "Environmental > EnvironmentalCard")
        // shows this icon; hidden by default so it never leaks onto hand cards.
        // Some legacy card prefabs resolve this TMP reference on the card root itself.
        // Never deactivate the whole card while trying to hide only the environmental icon.
        if (environmentalSprite != null && environmentalSprite.gameObject != gameObject)
            environmentalSprite.gameObject.SetActive(false);

        if (descriptionText != null)
        {
            baseDescription = GetActionDescription(data);
            descriptionText.text = baseDescription;
        }

        if (requirementsText != null)
        {
            requirementsText.text = BuildRequirementsText(data);
        }

        {
            Sprite sprite = ResolveCardArtwork(data);

            if (cardArtImage != null)
            {
                cardArtImage.sprite = sprite;
                cardArtImage.enabled = sprite != null;

                if (cardArtImage.GetComponent<CardShineEffect>() == null)
                    cardArtImage.gameObject.AddComponent<CardShineEffect>();
            }

            // Token-only cards (e.g. Layout's environmental card) never wire cardArtImage,
            // so tokenImage must be resolved unconditionally or it keeps whatever placeholder
            // sprite was last authored on the prefab regardless of which card is shown.
            if (tokenImage != null)
            {
                tokenImage.sprite = sprite;
                tokenImage.enabled = sprite != null;
            }
        }

        lockedToRealCard = !startAsToken;
        if (startAsToken) ShowToken();
        else ShowRealCard();

        if (deckTypeImage != null)
        {
            Sprite deckSprite = null;
            if (!string.IsNullOrWhiteSpace(data.deckSpriteName) && illustrations != null)
            {
                illustrations.TryGetIllustrationByName(data.deckSpriteName, out deckSprite);
            }

            // Explicitly clear/hide when there's nothing to show, instead of leaving whatever
            // placeholder sprite is authored on the prefab enabled (most cards have no deck badge).
            deckTypeImage.sprite = deckSprite;
            deckTypeImage.enabled = deckSprite != null;
        }

        if (data.IsEncounterCard())
        {
            AssignEncounterTargetHexIfNeeded(data);
            if (!data.encounterRevealed)
                SetupEncounterHiddenVisuals(data);
            else
                ClearEncounterHiddenVisualsInstant();
        }
        else
        {
            // This Card instance may have previously shown an unrevealed encounter card (e.g. a
            // recycled slot like the startup loading screen's rotating cards) — its "?" overlay
            // is only ever removed by the animated reveal flow, so a non-encounter card reused
            // into the same instance must clear it here instead of leaving it stuck on top.
            ClearEncounterHiddenVisualsInstant();
        }

        UpdateInteractableState();

        if (!data.hasShownHandAnimation && descriptionText != null && !string.IsNullOrEmpty(baseDescription))
        {
            if (!TypewriterEffect)
            {
                descriptionText.text = baseDescription;
                data.hasShownHandAnimation = true;
            }
            else
            {
                string quoteBlock = data.GetQuoteBlock();
                if (!string.IsNullOrWhiteSpace(quoteBlock) && baseDescription.Contains(quoteBlock))
                {
                    int quoteStart = baseDescription.LastIndexOf(quoteBlock, StringComparison.Ordinal);
                    string immediateText = baseDescription.Substring(0, quoteStart).TrimEnd();
                    descriptionText.text = immediateText;
                    descriptionTypewriterCoroutine = StartCoroutine(HandDrawTypewriterCoroutine("\n\n" + quoteBlock, data, append: true));
                }
                else
                {
                    descriptionText.text = string.Empty;
                    descriptionTypewriterCoroutine = StartCoroutine(HandDrawTypewriterCoroutine(baseDescription, data));
                }
            }
        }
    }

    // Centered hover previews must be fully initialized without starting the one-time
    // hand-draw typewriter coroutine or mutating that animation flag on the real CardData.
    public void InitializePreview(CardData data)
    {
        if (data == null) return;
        bool hadShownHandAnimation = data.hasShownHandAnimation;
        data.hasShownHandAnimation = true;
        Initialize(data, startAsToken: false);
        data.hasShownHandAnimation = hadShownHandAnimation;
    }

    // Called for the active environmental card shown in Layout's "Environmental > EnvironmentalCard".
    // Reveals the environmental sprite transform and renders the card's icon via the normalized name
    // (same scheme as the sprite-asset m_Name fields, e.g. "wind", "sun", "redsun").
    public void ShowEnvironmentalSprite()
    {
        if (environmentalSprite == null) return;
        isEnvironmentalPresentation = true;
        environmentalSprite.gameObject.SetActive(true);
        // RestrictRaycastsToRootCard normally disables child text raycasts. The
        // environmental glyph is offset from the token's border, however, so it must be
        // a hit target itself for pointer events to bubble up to this Card component.
        environmentalSprite.raycastTarget = true;
        // isEnvironmentalPresentation just became true, so retry the hit-area setup Awake()
        // skipped (it ran before this flag was set).
        EnsureTokenHoverHitArea();
        environmentalSprite.text = cardData != null
            ? $"<sprite name=\"{CardNameUtility.Normalize(cardData.name)}\">"
            : string.Empty;
    }

    private IEnumerator HandDrawTypewriterCoroutine(string text, CardData data, bool append = false)
    {
        if (append)
            yield return StartCoroutine(AppendTypewriterEffectCoroutine(descriptionText, text));
        else
            yield return StartCoroutine(TypewriterEffectCoroutine(descriptionText, text));
        if (data != null) data.hasShownHandAnimation = true;
        descriptionTypewriterCoroutine = null;
        UpdateInteractableState();
    }

    private IEnumerator AppendTypewriterEffectCoroutine(TextMeshProUGUI textComponent, string appendText)
    {
        if (textComponent == null || string.IsNullOrEmpty(appendText)) yield break;
        string prefix = textComponent.text;
        float delay = Mathf.Min(0.05f, 2f / appendText.Length);
        for (int i = 0; i < appendText.Length; i++)
        {
            if (textComponent == null) yield break;
            textComponent.text = prefix + appendText.Substring(0, i + 1);
            yield return new WaitForSecondsRealtime(delay);
        }
    }

    private Sprite ResolveCardArtwork(CardData data)
    {
        if (data == null || illustrations == null) return null;

        string[] candidates =
        {
            data.spriteName,
            data.portraitName,
            data.name,
            data.actionClassName,
            data.action
        };

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            bool found = UseCardArtFolderOnly
                ? illustrations.TryGetCardArtByName(candidate, out Sprite sprite)
                : illustrations.TryGetIllustrationByName(candidate, out sprite);
            if (found)
            {
                return sprite;
            }
        }

        return null;
    }

    private static void AssignEncounterTargetHexIfNeeded(CardData data)
    {
        if (data.encounterTargetHex != null) return;

        Game game = Game.Instance;
        Leader leader = game?.player;
        if (leader == null) return;

        var candidates = new HashSet<Hex>();
        if (leader.hex != null && !leader.killed)
        {
            foreach (Hex h in leader.hex.GetHexesInRadius(5)) candidates.Add(h);
        }
        if (leader.controlledCharacters != null)
        {
            foreach (Character c in leader.controlledCharacters)
            {
                if (c == null || c.killed || c.hex == null) continue;
                foreach (Hex h in c.hex.GetHexesInRadius(5)) candidates.Add(h);
            }
        }

        if (candidates.Count == 0) return;
        var list = new List<Hex>(candidates);
        data.encounterTargetHex = list[UnityEngine.Random.Range(0, list.Count)];
    }

    private bool IsUnplayedEncounterWithHex() =>
        cardData != null &&
        cardData.IsEncounterCard() &&
        !cardData.encounterRevealed &&
        cardData.encounterTargetHex != null;

    // Points at an unplayed encounter card's target hex with the same fluid HexPathRenderer
    // pulse used for the ambient opportunity-card hint (see OpportunityHexHinter), rather than a
    // separate flashing-frame cue.
    private void ShowEncounterHintPath(Character fromCharacter)
    {
        if (cardData?.encounterTargetHex == null) return;
        EnsureManagersLoaded();
        if (pathRenderer == null) return;

        Character source = fromCharacter ?? ResolveSelectedCharacter();
        if (source?.hex == null) return;

        pathRenderer.PulseHintPath(source, cardData.encounterTargetHex.v2, 5f);
    }

    private static Character ResolveSelectedCharacter()
    {
        Board board = Board.Instance;
        if (board != null && board.selectedCharacter != null) return board.selectedCharacter;
        SelectedCharacterIcon icon = FindFirstObjectByType<SelectedCharacterIcon>();
        return icon != null ? icon.CurrentCharacter : null;
    }

    private void ClearEncounterHiddenVisualsInstant()
    {
        if (encounterArtOverlay != null)
        {
            Destroy(encounterArtOverlay.gameObject);
            encounterArtOverlay = null;
            encounterQuestionMark = null;
        }
        if (encounterTokenOverlay != null)
        {
            Destroy(encounterTokenOverlay.gameObject);
            encounterTokenOverlay = null;
            encounterTokenQuestionMark = null;
        }
    }

    private void SetupEncounterHiddenVisuals(CardData data)
    {
        if (titleText != null) titleText.text = "Encounter";

        if (encounterArtOverlay == null && cardArtImage != null)
        {
            var overlayGo = new GameObject("EncounterOverlay", typeof(RectTransform), typeof(Image));
            overlayGo.transform.SetParent(cardArtImage.transform, false);
            var overlayRect = overlayGo.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            encounterArtOverlay = overlayGo.GetComponent<Image>();
            encounterArtOverlay.color = Color.black;

            var qGo = new GameObject("QuestionMark", typeof(RectTransform), typeof(TextMeshProUGUI));
            qGo.transform.SetParent(overlayGo.transform, false);
            var qRect = qGo.GetComponent<RectTransform>();
            qRect.anchorMin = Vector2.zero;
            qRect.anchorMax = Vector2.one;
            qRect.offsetMin = Vector2.zero;
            qRect.offsetMax = Vector2.zero;
            encounterQuestionMark = qGo.GetComponent<TextMeshProUGUI>();
            encounterQuestionMark.text = "?";
            encounterQuestionMark.fontSize = 64f;
            encounterQuestionMark.alignment = TextAlignmentOptions.Center;
            encounterQuestionMark.color = Color.white;
            encounterQuestionMark.fontStyle = FontStyles.Bold;
        }

        if (encounterTokenOverlay == null && tokenImage != null)
        {
            var tokenOverlayGo = new GameObject("EncounterTokenOverlay", typeof(RectTransform), typeof(Image));
            tokenOverlayGo.transform.SetParent(tokenImage.transform, false);
            var tokenOverlayRect = tokenOverlayGo.GetComponent<RectTransform>();
            tokenOverlayRect.anchorMin = Vector2.zero;
            tokenOverlayRect.anchorMax = Vector2.one;
            tokenOverlayRect.offsetMin = Vector2.zero;
            tokenOverlayRect.offsetMax = Vector2.zero;
            encounterTokenOverlay = tokenOverlayGo.GetComponent<Image>();
            encounterTokenOverlay.color = Color.black;
            encounterTokenOverlay.raycastTarget = false;

            var tqGo = new GameObject("QuestionMark", typeof(RectTransform), typeof(TextMeshProUGUI));
            tqGo.transform.SetParent(tokenOverlayGo.transform, false);
            var tqRect = tqGo.GetComponent<RectTransform>();
            tqRect.anchorMin = Vector2.zero;
            tqRect.anchorMax = Vector2.one;
            tqRect.offsetMin = Vector2.zero;
            tqRect.offsetMax = Vector2.zero;
            encounterTokenQuestionMark = tqGo.GetComponent<TextMeshProUGUI>();
            encounterTokenQuestionMark.text = "?";
            encounterTokenQuestionMark.fontSize = 64f;
            encounterTokenQuestionMark.alignment = TextAlignmentOptions.Center;
            encounterTokenQuestionMark.color = Color.white;
            encounterTokenQuestionMark.fontStyle = FontStyles.Bold;
            encounterTokenQuestionMark.raycastTarget = false;
        }

        string hexCoords = data.encounterTargetHex != null
            ? $"{data.encounterTargetHex.v2.x}, {data.encounterTargetHex.v2.y}"
            : "unknown";
        baseDescription = $"An encounter can be investigated at hex {hexCoords}";
        if (descriptionText != null) descriptionText.text = baseDescription;
    }

    private void BindLegacyPrefabReferences()
    {
        if (titleText == null) titleText = FindTextByName("Title");
        if (descriptionText == null) descriptionText = FindTextByName("Description");
        // if (typeText == null) typeText = FindTextByName("Type (1)") ?? FindTextByName("Type");
        if (requirementsText == null) requirementsText = FindTextByName("Requirements");

        if (cardArtImage == null) cardArtImage = FindImageByName("Image");
        if (cardBackgroundImage == null) cardBackgroundImage = FindImageByName("Border");
        if (discardButton == null) discardButton = FindChildByName("Discard");
    }

    private void RestrictRaycastsToRootCard()
    {
        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null) continue;
            if (graphic.gameObject == gameObject) continue;
            if (hover != null && graphic.gameObject == hover.gameObject) continue;
            if (graphic.GetComponent<Selectable>() != null) continue;
            graphic.raycastTarget = false;
        }

        if (cardBackgroundImage != null)
        {
            cardBackgroundImage.raycastTarget = true;
        }
    }

    // Token-only cards have no expanded card background on their root. Give them a
    // transparent UI Graphic so pointer enter/exit covers the complete token rect rather
    // than depending on one of the small, offset child visuals receiving the raycast.
    private void EnsureTokenHoverHitArea()
    {
        if (!(isTokenOnlyPresentation || isEnvironmentalPresentation) || rootHitGraphic != null) return;

        Image hitArea = gameObject.AddComponent<Image>();
        hitArea.color = Color.clear;
        hitArea.raycastTarget = true;
        rootHitGraphic = hitArea;
    }

    private TextMeshProUGUI FindTextByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && string.Equals(texts[i].gameObject.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return texts[i];
            }
        }
        return null;
    }

    private Image FindImageByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && string.Equals(images[i].gameObject.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return images[i];
            }
        }
        return null;
    }

    private GameObject FindChildByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && string.Equals(children[i].name, name, StringComparison.OrdinalIgnoreCase))
            {
                return children[i].gameObject;
            }
        }
        return null;
    }

    private string GetActionDescription(CardData data)
    {
        if (data == null) return string.Empty;

        CardTypeEnum cardType = data.GetCardType();
        string typePrefix = FormatCardTypeLabel(cardType);
        string body = data.GetRenderedDescription(CanShowFoundingText(data));
        if (!string.IsNullOrWhiteSpace(body))
        {
            return PrefixWithCardType(typePrefix, body);
        }

        string actionRef = data.GetActionRef();
        if (string.IsNullOrWhiteSpace(actionRef)) return string.Empty;

        CharacterAction action = actionsManager != null ? actionsManager.ResolveActionByRef(actionRef, data) : null;
        return action != null ? PrefixWithCardType(typePrefix, action.GetDescriptionForCard()) : string.Empty;
    }

    private string PrefixWithCardType(string typePrefix, string text)
    {
        if (string.IsNullOrWhiteSpace(typePrefix)) return text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return typePrefix;
        return $"{typePrefix}. {text}";
    }

    private string FormatCardTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (ShouldInsertWordSpace(value, i))
            {
                chars.Add(' ');
            }
            chars.Add(current);
        }

        string formatted = new string(chars.ToArray()).Trim().ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(formatted);
    }

    private static bool CanShowFoundingText(CardData data)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.name)) return false;

        Board board = Board.Instance;
        List<Hex> hexes = board != null ? board.GetHexes() : null;
        if (hexes == null) return false;

        string target = NormalizeLookupKey(data.name);
        if (string.IsNullOrWhiteSpace(target)) return false;

        for (int i = 0; i < hexes.Count; i++)
        {
            PC candidate = hexes[i] != null ? hexes[i].GetPCData() : null;
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.pcName)) continue;
            if (string.Equals(NormalizeLookupKey(candidate.pcName), target, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeLookupKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static bool ShouldInsertWordSpace(string value, int index)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (index <= 0 || index >= value.Length) return false;

        char current = value[index];
        if (!char.IsUpper(current)) return false;

        char previous = value[index - 1];
        if (char.IsWhiteSpace(previous)) return false;

        if (char.IsLower(previous) || char.IsDigit(previous))
        {
            return true;
        }

        if (!char.IsUpper(previous)) return false;

        if (index + 1 < value.Length && char.IsLower(value[index + 1]))
        {
            return true;
        }

        return false;
    }

    private Color GetCardTypeColor(CardTypeEnum cardType)
    {
        if (colors == null) colors = FindFirstObjectByType<Colors>();

        string colorName = cardType switch
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

        if (string.IsNullOrWhiteSpace(colorName) || colors == null) return Color.clear;

        Color c;
        try { c = colors.GetColorByName(colorName); }
        catch { c = Color.clear; }

        if (c.a < 0.01f)
            c = colorName switch { "environmental" => new Color(0.42f, 0.67f, 0.42f, 1f), _ => Color.clear };

        return c;
    }

    private void ApplyCardTypeColor(CardTypeEnum cardType)
    {
        Color c = GetCardTypeColor(cardType);
        if (c.a < 0.01f) return;

        if (cardBackgroundImage != null)
            cardBackgroundImage.color = new Color(c.r, c.g, c.b, cardBackgroundImage.color.a);
        if (tokenBorder != null)
            tokenBorder.color = new Color(c.r, c.g, c.b, tokenBorder.color.a);
    }

    private string FormatCardTypeLabel(CardTypeEnum cardType)
    {
        string label = cardType switch
        {
            CardTypeEnum.PC => "PC",
            CardTypeEnum.Land => "Land",
            CardTypeEnum.Character => "Character",
            CardTypeEnum.Army => "Army",
            CardTypeEnum.Event => "Event",
            CardTypeEnum.Action => "Action",
            CardTypeEnum.Spell => "Spell",
            CardTypeEnum.Object => "Object",
            CardTypeEnum.Encounter => "Encounter",
            CardTypeEnum.Environmental => "Environmental",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(label)) return string.Empty;

        Color c = GetCardTypeColor(cardType);
        if (c.a < 0.01f) return label;

        return $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{label}</color>";
    }

    private string BuildRequirementsText(CardData data)
    {
        if (data == null) return string.Empty;
        List<string> reqs = new();

        AppendRequirement(reqs, "commander", data.commanderSkillRequired);
        AppendRequirement(reqs, "agent", data.agentSkillRequired);
        AppendRequirement(reqs, "emmissary", data.emissarySkillRequired);
        AppendRequirement(reqs, "mage", data.mageSkillRequired);

        int totalGold = data.GetTotalGoldCost();
        AppendRequirement(reqs, "gold", totalGold);

        AppendRequirement(reqs, "leather", data.leatherRequired);
        AppendRequirement(reqs, "timber", data.timberRequired);
        AppendRequirement(reqs, "mounts", data.mountsRequired);
        AppendRequirement(reqs, "iron", data.ironRequired);
        AppendRequirement(reqs, "steel", data.steelRequired);
        AppendRequirement(reqs, "mithril", data.mithrilRequired);

        string situationLabel = FormatSituationLabel(data);
        if (!string.IsNullOrWhiteSpace(situationLabel))
        {
            string costPart = reqs.Count > 0 ? $"\n{string.Join(" ", reqs)}" : string.Empty;
            return $"{situationLabel}{costPart}";
        }

        if (reqs.Count == 0) return string.Empty;
        return $"{string.Join(" ", reqs)}";
    }

    private string FormatSituationLabel(CardData data)
    {
        if (data == null || data.GetCardType() != CardTypeEnum.Action) return string.Empty;
        CardSituationEnum situation = data.GetSituation();
        if (situation == CardSituationEnum.None) return string.Empty;

        string label = situation switch
        {
            CardSituationEnum.ArmyAtEnemyPC                    => "Army at enemy PC",
            CardSituationEnum.AgentAtEnemyPC                   => "Agent at enemy PC",
            CardSituationEnum.EmmissaryAtEnemyPC               => "Emissary at enemy PC",
            CardSituationEnum.ArmyAtFriendlyPC                 => "Army at friendly PC",
            CardSituationEnum.EmmissaryAtOwnPC                 => "Emissary at own PC",
            CardSituationEnum.AgentAtOwnPC                     => "Agent at own PC",
            CardSituationEnum.ArmyAtHexWithEnemyArmyAndNoPC   => "Army meets enemy army",
            CardSituationEnum.AgentAtHexWithEnemyCharacter     => "Agent meets enemy",
            CardSituationEnum.EmmissaryAtHexWithEnemyCharacter => "Emissary meets enemy",
            CardSituationEnum.MageAtHexWithEnemyCharacter      => "Mage meets enemy",
            CardSituationEnum.MageAtArtifactHex                => "Mage at artifact",
            CardSituationEnum.CommanderAtOwnPC                 => "Commander at own PC",
            _                                                   => string.Empty
        };

        return string.IsNullOrWhiteSpace(label) ? string.Empty : $"When: {label}";
    }

    private void AppendRequirement(List<string> requirements, string spriteName, int count)
    {
        if (requirements == null || string.IsNullOrWhiteSpace(spriteName) || count <= 0) return;
        requirements.Add($"{count}<sprite name=\"{spriteName}\">");
    }

    // Extracted from UpdateInteractableState so callers that show a card outside the
    // hand (e.g. SituationCardsUI's opportunity-card tray) can evaluate/tint playability
    // for a specific character without re-implementing the action-resolving check.
    public bool EvaluateIsPlayable(Character character)
    {
        if (cardData == null) return false;

        Leader resourceOwner = GetHumanPlayerLeader();
        bool actionConditionsMet = true;
        string actionRef = cardData.GetActionRef();

        if (!string.IsNullOrWhiteSpace(actionRef))
        {
            if (actionsManager == null)
            {
                Debug.LogWarning($"[CardPlay/Evaluate] '{cardData.name}': has actionRef '{actionRef}' but no actionsManager reference — action gate skipped (treated as met).");
            }
            else if (character == null)
            {
                // Normal during selection changes (e.g. Board.UnselectHex evaluates every
                // active card with no character yet) — not worth logging every time.
            }
            else
            {
                CharacterAction action = actionsManager.ResolveActionByRef(actionRef, cardData);
                if (action == null)
                {
                    Debug.LogWarning($"[CardPlay/Evaluate] '{cardData.name}': ResolveActionByRef('{actionRef}') returned null — no CharacterAction registered.");
                }
                else
                {
                    action.Initialize(character, cardData);
                    actionConditionsMet = action.FulfillsConditions();
                }
            }
        }

        bool meetsResources = resourceOwner == null || cardData.MeetsResourceRequirements(resourceOwner);
        bool result = cardData.EvaluatePlayability(
            character,
            _ => meetsResources,
            _ => actionConditionsMet);

        return result;
    }

    public void UpdateInteractableState()
    {
        if (cardData == null) return;

        bool isTypewriting = descriptionTypewriterCoroutine != null;

        Board board = Board.Instance;
        Character selected = board != null ? board.selectedCharacter : null;
        Leader resourceOwner = GetHumanPlayerLeader();

        bool isPlayable = EvaluateIsPlayable(selected);
        // Cached so the bloom wheel can fade unplayable tokens without re-running the
        // (action-resolving) playability evaluation every frame.
        LastKnownPlayable = isPlayable;

        if (!SuppressHoverEffects && canvasGroup != null)
        {
            canvasGroup.alpha = isPlayable ? 1f : 0.5f;
            canvasGroup.interactable = isPlayable;
            canvasGroup.blocksRaycasts = true;
        }

        if (!isTypewriting && descriptionText != null)
        {
            if (isPlayable || !showRequirementWarnings)
            {
                descriptionText.text = baseDescription;
            }
            else
            {
                string errorText = BuildRequirementsMessageText(selected, resourceOwner);
                if (!string.IsNullOrWhiteSpace(errorText))
                {
                    string colorHex = ColorUtility.ToHtmlStringRGB(requirementsMessageColor);
                    string separator = string.IsNullOrWhiteSpace(baseDescription) ? string.Empty : "\n";
                    descriptionText.text = $"{baseDescription}{separator}<color=#{colorHex}>{errorText}</color>";
                }
                else
                {
                    descriptionText.text = baseDescription;
                }
            }
        }

        UpdateDiscardButtonState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (BoardNavigator.IsNavigationInputLocked()) return;
        if (!SuppressHoverEffects)
        {
            if (isTokenOnlyPresentation || isEnvironmentalPresentation) CardCenterPreview.Instance?.ShowPreview(cardData, hoverDriven: true);
            else ShowRealCard();
        }
        if (Sounds.Instance != null) Sounds.Instance.PlayUiHover();
        if (SuppressHoverEffects) return;
        if (cursorManager != null)
        {
            if (cardData != null && cardData.isPlayable)
                cursorManager.SetClickableCursor();
            else if (IsUnplayedEncounterWithHex())
                cursorManager.SetClickableCursor();
            else
                cursorManager.SetDisableCursor();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!SuppressHoverEffects)
        {
            if (isTokenOnlyPresentation || isEnvironmentalPresentation) CardCenterPreview.Instance?.HidePreview();
            else ShowToken();
        }
        if (SuppressHoverEffects) return;
        if (cursorManager != null) cursorManager.SetDefaultCursor();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (SuppressHoverEffects)
            {
                Board board = Board.Instance;
                PlayFromBloom(board != null ? board.selectedCharacter : null);
                return;
            }
            if (canvasGroup != null && !canvasGroup.interactable)
            {
                if (IsUnplayedEncounterWithHex())
                {
                    BoardNavigator.Instance?.LookAt(cardData.encounterTargetHex.transform.position);
                    ShowEncounterHintPath(null);
                }
                return;
            }
            TryPlayCard();
        }
    }

    // Snapshots the enlarged center-preview card's on-screen footprint (world center + width)
    // at click time, for CardPlayFlight.Launch to fly out of later. Must be read now: playing
    // the card rebuilds the hand/bloom (CardBloomWheel.SetCards -> ClearCenterPreview) well
    // before the awaited action/consume calls finish, destroying the live preview clone by
    // the time Launch would otherwise query CardCenterPreview.Instance itself.
    public static (Vector3? center, float? width) SnapshotCenterPreviewSource()
    {
        RectTransform previewRect = CardCenterPreview.Instance != null ? CardCenterPreview.Instance.CurrentPreviewRect : null;
        if (previewRect == null) return (null, null);
        return (previewRect.TransformPoint(previewRect.rect.center), previewRect.rect.width * Mathf.Abs(previewRect.lossyScale.x));
    }

    // CardBloomWheel performs its own geometric hit testing because its tokens animate
    // outside their original layout rect. Route bloom clicks through that same hit result
    // instead of relying on an unrelated UI raycast to happen to reach this component.
    public void PlayFromBloom(Character selectedCharacter)
    {
        Debug.Log($"[CardPlay/Bloom] PlayFromBloom: '{cardData?.name ?? "null"}' (LastKnownPlayable={LastKnownPlayable}, selectedCharacter={(selectedCharacter != null ? selectedCharacter.characterName : "none")}).");

        // LastKnownPlayable is the exact state CardBloomWheel uses to paint a token red.
        // Keep click behavior consistent with that visual state even though bloom clicks
        // intentionally bypass the root CanvasGroup's stale interactable flag.
        if (!LastKnownPlayable)
        {
            Debug.LogWarning($"[CardPlay/Bloom] '{cardData?.name}' aborted: LastKnownPlayable=false (cached from the last UpdateInteractableState pass).");
            if (IsUnplayedEncounterWithHex())
            {
                BoardNavigator.Instance?.LookAt(cardData.encounterTargetHex.transform.position);
                ShowEncounterHintPath(selectedCharacter);
            }
            else if (selectedCharacter?.hex != null)
            {
                string reason = BuildRequirementsMessageText(selectedCharacter, GetHumanPlayerLeader());
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    Debug.LogWarning($"[CardPlay/Bloom] '{cardData?.name}' requirements message: {reason}");
                    MessageDisplayNoUI.ShowMessage(selectedCharacter.hex, selectedCharacter, reason, Color.red);
                }
            }
            return;
        }

        (Vector3? previewCenter, float? previewWidth) = SnapshotCenterPreviewSource();

        if (cardData != null && cardData.GetCardType() == CardTypeEnum.Environmental)
        {
            PlayEnvironmentalFromBloom(selectedCharacter, previewCenter, previewWidth);
            return;
        }
        TryPlayCard(selectedCharacter, invokedFromBloom: true, previewCenter, previewWidth);
    }

    private void PlayEnvironmentalFromBloom(Character selected, Vector3? previewCenter = null, float? previewWidth = null)
    {
        if (IsPlayInProgress || cardData == null)
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData?.name}' aborted: IsPlayInProgress={IsPlayInProgress}, cardData null={cardData == null}.");
            return;
        }
        EnsureManagersLoaded();

        Game game = Game.Instance;
        PlayableLeader playerLeader = game != null ? game.player : null;
        if (playerLeader == null || deckManager == null)
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}' aborted: playerLeader={(playerLeader != null)}, deckManager={(deckManager != null)}.");
            return;
        }
        EnvironmentalCardManager environment = EnvironmentalCardManager.GetOrCreate();
        if (!environment.CanNationPlay(playerLeader, game.turn, out string environmentalReason))
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': CanNationPlay=false — {environmentalReason}");
            if (selected?.hex != null)
                MessageDisplayNoUI.ShowMessage(selected.hex, selected, environmentalReason, Color.red);
            return;
        }
        if (!cardData.MeetsResourceRequirements(playerLeader))
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': MeetsResourceRequirements=false for '{playerLeader.characterName}'.");
            if (selected?.hex != null)
            {
                string reason = BuildRequirementsMessageText(selected, playerLeader);
                if (!string.IsNullOrWhiteSpace(reason))
                    MessageDisplayNoUI.ShowMessage(selected.hex, selected, reason, Color.red);
            }
            return;
        }

        CardData playedCard = cardData;
        IsPlayInProgress = true;

        if (!deckManager.TryConsumeCard(playerLeader, playedCard.name, false, out CardData consumedCard))
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': deckManager.TryConsumeCard failed.");
            IsPlayInProgress = false;
            return;
        }

        CardData activeCard = consumedCard ?? playedCard;
        if (!environment.TrySetActiveCard(activeCard, playerLeader, out _))
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': environment.TrySetActiveCard failed (already consumed from hand).");
            IsPlayInProgress = false;
            return;
        }
        Debug.Log($"[CardPlay/Environmental] '{cardData.name}' played successfully via bloom.");
        if (selected?.hex != null) selected.hex.PlayCharacterActionAnimation(selected);

        if (selected?.hex != null)
            MessageDisplayNoUI.ShowMessage(selected.hex, selected, $"{activeCard.name} takes hold — effects begin next turn", Color.green);

        CardPlayFlight.Launch(this, selected != null ? selected.hex : null, sourceWorldCenter: previewCenter, sourceWorldWidth: previewWidth);
        Destroy(gameObject);
    }

    private async void TryPlayCard(Character selectedOverride = null, bool invokedFromBloom = false, Vector3? previewCenter = null, float? previewWidth = null)
    {
        if (cardData == null)
        {
            Debug.LogWarning("[CardPlay] TryPlayCard aborted: cardData is null.");
            return;
        }
        Debug.Log($"[CardPlay] TryPlayCard start: '{cardData.name}' (type={cardData.GetCardType()}, invokedFromBloom={invokedFromBloom}).");
        if (IsPlayInProgress)
        {
            Debug.LogWarning($"[CardPlay] '{cardData.name}' aborted: a play is already in progress for this card instance.");
            return;
        }
        if (!invokedFromBloom && canvasGroup != null && !canvasGroup.interactable)
        {
            Debug.LogWarning($"[CardPlay] '{cardData.name}' aborted: card CanvasGroup is not interactable (not invoked from bloom).");
            return;
        }

        Board board = Board.Instance;
        SelectedCharacterIcon icon = FindFirstObjectByType<SelectedCharacterIcon>();
        Character selected = selectedOverride != null
            ? selectedOverride
            : board != null && board.selectedCharacter != null
                ? board.selectedCharacter
                : icon != null ? icon.CurrentCharacter : null;
        Leader resourceOwner = GetHumanPlayerLeader();
        CardData playedCard = cardData;
        Character playedSelected = selected;
        bool actionConditionsMet = true;
        string actionRef = playedCard.GetActionRef();
        if (string.IsNullOrWhiteSpace(actionRef))
        {
            Debug.Log($"[CardPlay] '{playedCard.name}' has no actionRef — skipping action-gate check (not action-backed, e.g. Land/PC/Character/Encounter).");
        }
        else if (actionsManager == null)
        {
            Debug.LogWarning($"[CardPlay] '{playedCard.name}' has actionRef '{actionRef}' but this Card has no actionsManager reference — action gate skipped.");
        }
        else if (playedSelected == null)
        {
            Debug.LogWarning($"[CardPlay] '{playedCard.name}' has actionRef '{actionRef}' but no character is selected — action gate skipped, will likely fail playability.");
        }
        else
        {
            CharacterAction action = actionsManager.ResolveActionByRef(actionRef, playedCard);
            if (action == null)
            {
                Debug.LogWarning($"[CardPlay] '{playedCard.name}': actionsManager.ResolveActionByRef('{actionRef}') returned null — no CharacterAction registered for this ref.");
            }
            else
            {
                action.Initialize(playedSelected, playedCard);
                actionConditionsMet = action.FulfillsConditions();
                if (!actionConditionsMet)
                {
                    string hexName = playedSelected.hex != null ? playedSelected.hex.name : "none";
                    string pcName = playedSelected.hex?.GetPCData()?.pcName ?? "none";
                    Debug.LogWarning(
                        $"[CardPlay] Action gate failed for card '{playedCard.name}' on '{playedSelected.characterName}' " +
                        $"(hex='{hexName}', pc='{pcName}', commander={playedSelected.GetCommander()}, agent={playedSelected.GetAgent()}, " +
                        $"emmissary={playedSelected.GetEmmissary()}, mage={playedSelected.GetMage()})");
                }
                else
                {
                    Debug.Log($"[CardPlay] '{playedCard.name}': action gate ('{action.GetType().Name}') passed for '{playedSelected.characterName}'.");
                }
            }
        }

        bool meetsResources = resourceOwner == null || playedCard.MeetsResourceRequirements(resourceOwner);
        bool playable = playedCard.EvaluatePlayability(
            playedSelected,
            _ => meetsResources,
            _ => actionConditionsMet);
        if (!playable)
        {
            Debug.LogWarning($"[CardPlay] '{playedCard.name}' NOT playable (meetsResources={meetsResources}, actionConditionsMet={actionConditionsMet}, selected={(playedSelected != null ? playedSelected.characterName : "none")}). Card will not be played.");
            if (IsUnplayedEncounterWithHex())
            {
                BoardNavigator.Instance?.LookAt(cardData.encounterTargetHex.transform.position);
                ShowEncounterHintPath(playedSelected);
            }
            else if (playedSelected?.hex != null)
            {
                // Bloom-wheel tokens set SuppressHoverEffects, which skips the dimming and
                // requirements text UpdateInteractableState() normally shows — so without this,
                // clicking an unplayable card there (e.g. a 2nd land card played this turn) does
                // nothing visible at all.
                string reason = BuildRequirementsMessageText(playedSelected, resourceOwner);
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    Debug.LogWarning($"[CardPlay] '{playedCard.name}' requirements message: {reason}");
                    MessageDisplayNoUI.ShowMessage(playedSelected.hex, playedSelected, reason, Color.red);
                }
            }
            return;
        }
        Debug.Log($"[CardPlay] '{playedCard.name}' passed playability check — proceeding to resolve (type={playedCard.GetCardType()}).");

        // Hand consumption rebuilds the bloom immediately. DeckManager uses this flag to keep
        // the clicked instance alive until its async effect and play animation have completed.
        IsPlayInProgress = true;

        // The play handlers below can stall the main thread for a couple of seconds
        // (effect resolution, reveals, spawns), during which nothing renders. Show the
        // waiting state NOW — waiting cursor plus a pressed, locked card — and yield so
        // it actually draws for a frame before the stall; otherwise the click looks like
        // it did nothing until the effects finish.
        Vector3 preResolveScale = transform.localScale;
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        transform.localScale = preResolveScale * 0.94f;
        CursorManager.Instance?.SetWaitingCursor();
        await Task.Yield();

        bool success = false;
        bool actionRollFailed = false;
        CardTypeEnum cardType = playedCard.GetCardType();

        switch (cardType)
        {
            case CardTypeEnum.Action:
            case CardTypeEnum.Event:
            case CardTypeEnum.Land:
            case CardTypeEnum.PC:
            case CardTypeEnum.Army:
            case CardTypeEnum.Spell:
            case CardTypeEnum.Object:
                (success, actionRollFailed) = await HandleActionCardPlayed(playedSelected);
                break;
            case CardTypeEnum.Encounter:
                success = await HandleEncounterCardPlayed(playedSelected);
                break;
            case CardTypeEnum.Character:
                success = await HandleCharacterCardPlayed(playedSelected);
                break;
            case CardTypeEnum.Environmental:
                success = await HandleEnvironmentalCardPlayed(playedSelected);
                break;
            default:
                Debug.LogError($"[CardPlay] '{playedCard.name}': no switch case handles card type {cardType} — card will not be played. Add a case in TryPlayCard's switch.");
                break;
        }

        Debug.Log($"[CardPlay] '{playedCard.name}' resolution finished: success={success}, actionRollFailed={actionRollFailed}.");

        // Effects resolved (or refused) — hand the cursor back before anything else;
        // this must run even if the card object was destroyed during resolution.
        CursorManager.Instance?.SetDefaultCursor();

        if (!this)
        {
            return;
        }

        if (success)
        {
            if (playedSelected?.hex != null) playedSelected.hex.PlayCharacterActionAnimation(playedSelected);
            if (actionRollFailed)
            {
                // The card was spent but its difficulty roll failed — no effect landed, so it
                // doesn't fly anywhere. Shake it, drain it red, and let it dissolve in place.
                CardPlayFailure.Launch(this, sourceWorldCenter: previewCenter);
            }
            else
            {
                // Send the card's token spiralling down onto the hex the effect landed on
                // (encounters carry their own target hex; everything else resolves at the
                // acting character's hex). Must run before Destroy so the token visual can
                // be cloned off this instance.
                Hex effectHex = playedCard.encounterTargetHex != null
                    ? playedCard.encounterTargetHex
                    : playedSelected != null ? playedSelected.hex : null;
                CardPlayFlight.Launch(this, effectHex, sourceWorldCenter: previewCenter, sourceWorldWidth: previewWidth);
            }
            // Card was successfully played, it will be removed from hand by the manager
            if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }
        else
        {
            Debug.LogWarning($"[CardPlay] '{playedCard.name}' handler reported failure — card token destroyed WITHOUT being consumed from hand, so it should reappear on next bloom rebuild. See warnings above for the specific reason.");
            // Undo the pressed/locked waiting state — the card stays in hand.
            transform.localScale = preResolveScale;
            if (canvasGroup != null)
            {
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
            UpdateInteractableState();
            IsPlayInProgress = false;
            deckManager?.RefreshHumanPlayerHandUI();
            if (gameObject != null) Destroy(gameObject);
        }
    }

    private Leader GetHumanPlayerLeader()
    {
        Game game = Game.Instance;
        return game != null ? game.player : null;
    }

    private void UpdateDiscardButtonState()
    {
        if (discardButton == null) return;
        discardButton.SetActive(showCloseIcon);
        if (!showCloseIcon) return;
        Button btn = discardButton.GetComponent<Button>();
        if (btn == null) return;
        btn.interactable = cardData != null && !cardData.IsEncounterCard();
    }

    public void Discard()
    {
        _ = TryDiscardAsync();
    }

    private async Task<bool> TryDiscardAsync()
    {
        if (cardData == null) return false;

        EnsureManagersLoaded();
        if (deckManager == null) return false;

        Leader humanLeader = GetHumanPlayerLeader();
        if (humanLeader is not PlayableLeader playable) return false;

        Game game = Game.Instance;
        if (game == null || !game.IsPlayerCurrentlyPlaying()) return false;

        bool confirm = await ConfirmationDialog.AskImmediate($"Discard {cardData.name} for a random resource?", "Yes", "No");
        if (!confirm) return false;

        if (!deckManager.TryDiscardCard(playable, cardData.name, out CardData discarded)) return false;

        GrantRandomResource(playable);

        if (this != null && gameObject != null)
        {
            StartCoroutine(AnimateDiscardAndDestroy());
        }
        return true;
    }

    private IEnumerator AnimateDiscardAndDestroy()
    {
        // Block further interaction immediately.
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        // Escape the GridLayout so siblings reflow while we fly away.
        Canvas.ForceUpdateCanvases();
        Transform gridParent = rectTransform.parent;
        Transform floatTarget = (gridParent != null && gridParent.parent != null) ? gridParent.parent : gridParent;
        if (floatTarget != null && floatTarget != rectTransform.parent)
        {
            Vector3 worldPos = rectTransform.position;
            rectTransform.SetParent(floatTarget, false);
            rectTransform.position = worldPos;
            rectTransform.SetAsLastSibling();
        }
        else if (layoutElement != null)
        {
            layoutElement.ignoreLayout = true;
        }

        Vector2 startPos = rectTransform.anchoredPosition;
        float drift = UnityEngine.Random.Range(-70f, 70f);
        Vector2 endPos = startPos + new Vector2(drift, 200f);
        float startRot = rectTransform.localEulerAngles.z;
        float endRot = startRot + UnityEngine.Random.Range(-18f, 18f);

        float duration = 0.32f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (this == null) yield break;
            float p = elapsed / duration;
            float eased = 1f - (1f - p) * (1f - p);

            rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, eased);
            rectTransform.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(startRot, endRot, p));
            rectTransform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.55f, p);
            if (canvasGroup != null) canvasGroup.alpha = 1f - p;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }

    private void GrantRandomResource(PlayableLeader leader)
    {
        if (leader == null || cardData == null) return;

        string[] resourceNames = { "gold", "timber", "leather", "mounts", "iron", "steel", "mithril" };
        string resourceName = resourceNames[UnityEngine.Random.Range(0, resourceNames.Length)];

        switch (resourceName)
        {
            case "gold": leader.AddGold(1); break;
            case "timber": leader.AddTimber(1); break;
            case "leather": leader.AddLeather(1); break;
            case "mounts": leader.AddMounts(1); break;
            case "iron": leader.AddIron(1); break;
            case "steel": leader.AddSteel(1); break;
            case "mithril": leader.AddMithril(1); break;
        }

        string message = $"{cardData.name} transformed into {resourceName}";
        MessageDisplay.ShowMessage(message, Color.yellow);
    }

    // Menu-safe minimal initialize: applies only what the round token visual needs (art +
    // type-colored border). Full Initialize walks live-game state — founding text asks the
    // Board for its hexes, playability asks for the selected character — none of which
    // exists on the campaign-selection screen, where this feeds the scenario tokens.
    public void InitializeTokenVisualOnly(CardData data)
    {
        if (data == null) return;
        cardData = data;
        EnsureManagersLoaded();
        BindLegacyPrefabReferences();
        ApplyCardTypeColor(data.GetCardType());
        if (environmentalSprite != null && environmentalSprite.gameObject != gameObject)
            environmentalSprite.gameObject.SetActive(false);
        Sprite sprite = ResolveCardArtwork(data);
        if (tokenImage != null)
        {
            tokenImage.sprite = sprite;
            // Never draw a sprite-less Image — it renders as a solid white square.
            tokenImage.enabled = sprite != null;
        }
    }

    // Wheel token tinting: 'dim' darkens non-hovered tokens multiplicatively (0 = untouched,
    // 1 = black); 'redness' shifts unplayable tokens toward red (green/blue suppressed) so
    // unavailability reads as a color, not transparency. Alpha is never touched — it belongs
    // to the wheel's CanvasGroup fades. Base colors are captured on first use, after
    // Initialize has applied the card-type border color.
    private Color tokenImageBaseColor = Color.white;
    private Color tokenBorderBaseColor = Color.white;
    private bool tokenBaseColorsCaptured;

    public void SetTokenTint(float dim01, float redness01)
    {
        if (tokenImage == null && tokenBorder == null) return;

        if (!tokenBaseColorsCaptured)
        {
            if (tokenImage != null) tokenImageBaseColor = tokenImage.color;
            if (tokenBorder != null) tokenBorderBaseColor = tokenBorder.color;
            tokenBaseColorsCaptured = true;
        }

        float k = 1f - Mathf.Clamp01(dim01);
        float redness = Mathf.Clamp01(redness01);
        if (tokenImage != null) tokenImage.color = TintTokenColor(tokenImageBaseColor, k, redness);
        if (tokenBorder != null) tokenBorder.color = TintTokenColor(tokenBorderBaseColor, k, redness);
    }

    private Color cardBackgroundBaseColor = Color.white;
    private bool cardBackgroundBaseColorCaptured;

    // Reddens the full-card border/background for opportunity-card trays (SituationCardsUI),
    // where cards render as real cards (not tokens) so SetTokenTint doesn't apply.
    public void SetUnplayableRealCardTint(bool unplayable)
    {
        if (cardBackgroundImage == null) return;

        if (!cardBackgroundBaseColorCaptured)
        {
            cardBackgroundBaseColor = cardBackgroundImage.color;
            cardBackgroundBaseColorCaptured = true;
        }

        cardBackgroundImage.color = TintTokenColor(cardBackgroundBaseColor, 1f, unplayable ? 1f : 0f);
    }

    private static Color TintTokenColor(Color baseColor, float k, float redness)
    {
        Color darkened = new(baseColor.r * k, baseColor.g * k, baseColor.b * k, baseColor.a);
        if (redness <= 0f) return darkened;
        // Push toward red: hold the red channel up (so dark art still reads red) and pull
        // green/blue down, respecting the darkening already applied.
        Color reddened = new(Mathf.Max(darkened.r, 0.75f * k), darkened.g * 0.25f, darkened.b * 0.25f, baseColor.a);
        return Color.Lerp(darkened, reddened, redness);
    }

    // Clones the compact token visual (round art + border ring) for the play-flight
    // animation and the campaign-selection tokens. The clone is display-only:
    // interaction and raycasts are stripped. tokenSize reports the visual footprint.
    // NOTE: the prefab's token root (TokenRepresentation) is a plain Transform, not a
    // RectTransform, and its children carry authored offsets that position them over
    // the card layout — the clone re-centers them so it works standalone.
    public GameObject CreateTokenVisualClone(Transform parent, out Vector2 tokenSize)
    {
        tokenSize = new Vector2(132f, 132f);
        if (tokenCanvasGroup == null) return null;

        // The border ring is the widest token piece; its rect × scale is the footprint.
        if (tokenBorder != null && tokenBorder.transform is RectTransform borderRect && borderRect.rect.size.sqrMagnitude > 1f)
        {
            tokenSize = Vector2.Scale(borderRect.rect.size, borderRect.localScale);
        }

        GameObject clone = Instantiate(tokenCanvasGroup.gameObject, parent, false);
        clone.name = "TokenVisual";
        clone.transform.localPosition = Vector3.zero;
        foreach (Transform child in clone.transform)
        {
            if (child is RectTransform childRect) childRect.anchoredPosition = Vector2.zero;
        }
        Transform environmentalChild = clone.transform.Find("Environmental");
        if (environmentalChild != null) environmentalChild.gameObject.SetActive(false);

        CanvasGroup cg = clone.GetComponent<CanvasGroup>();
        if (cg == null) cg = clone.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        CardEnvironmentalPulseEffect pulse = clone.GetComponentInChildren<CardEnvironmentalPulseEffect>(true);
        if (pulse != null) Destroy(pulse);
        foreach (Graphic graphic in clone.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }
        return clone;
    }

    // Clones the full expanded card visual (art, title, description) for the fail-flutter
    // animation — unlike the token clone, this one doesn't compact first: a fizzled roll
    // dissolves the card as the player was already looking at it. Display-only, like above.
    public GameObject CreateRealCardVisualClone(Transform parent, out Vector2 cardSize)
    {
        cardSize = Vector2.zero;
        if (realCardCanvasGroup == null) return null;

        if (realCardCanvasGroup.transform is RectTransform sourceRect)
        {
            cardSize = Vector2.Scale(sourceRect.rect.size, sourceRect.localScale);
        }

        GameObject clone = Instantiate(realCardCanvasGroup.gameObject, parent, false);
        clone.name = "RealCardVisual";
        clone.transform.localPosition = Vector3.zero;
        foreach (Transform child in clone.transform)
        {
            if (child is RectTransform childRect) childRect.anchoredPosition = Vector2.zero;
        }

        CanvasGroup cg = clone.GetComponent<CanvasGroup>();
        if (cg == null) cg = clone.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        CardEnvironmentalPulseEffect realCardPulse = clone.GetComponentInChildren<CardEnvironmentalPulseEffect>(true);
        if (realCardPulse != null) Destroy(realCardPulse);
        foreach (Graphic graphic in clone.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }
        return clone;
    }

    public void ShowToken()
    {
        if (lockedToRealCard) return;
        if (tokenCanvasGroup != null)
        {
            tokenCanvasGroup.alpha = 1f;
            tokenCanvasGroup.blocksRaycasts = true;
            tokenCanvasGroup.interactable = true;
        }
        if (realCardCanvasGroup != null)
        {
            realCardCanvasGroup.alpha = 0f;
            realCardCanvasGroup.blocksRaycasts = false;
            realCardCanvasGroup.interactable = false;
        }
        if (tokenImage != null) tokenImage.raycastTarget = true;
        if (rootHitGraphic != null) rootHitGraphic.raycastTarget = false;
    }

    public void ShowRealCard()
    {
        if (tokenCanvasGroup != null)
        {
            tokenCanvasGroup.alpha = 0f;
            tokenCanvasGroup.blocksRaycasts = false;
            tokenCanvasGroup.interactable = false;
        }
        if (realCardCanvasGroup != null)
        {
            realCardCanvasGroup.alpha = 1f;
            realCardCanvasGroup.blocksRaycasts = true;
            realCardCanvasGroup.interactable = true;
        }
        if (tokenImage != null) tokenImage.raycastTarget = false;
        if (rootHitGraphic != null) rootHitGraphic.raycastTarget = true;
        if (cardBackgroundImage != null) cardBackgroundImage.raycastTarget = true;
    }

    private string BuildRequirementsMessageText(Character selected, Leader resourceOwner)
    {
        if (cardData == null || cardData.playability == null) return string.Empty;

        List<string> messages = new();
        if (cardData.playability.failsLevelRequirements)
        {
            AppendMissingLevelMessages(messages, selected);
        }

        if (cardData.playability.failsResourceRequirements)
        {
            string resourceMessage = BuildMissingResourceMessage(resourceOwner);
            if (!string.IsNullOrWhiteSpace(resourceMessage))
            {
                messages.Add(resourceMessage);
            }
        }

        if (cardData.playability.failsStartingCityRequirement)
        {
            messages.Add($"<sprite name=\"error\">{cardData.playability.startingCityReason}");
        }

        if (cardData.playability.failsActionConditions)
        {
            if (cardData.playability.failsAlreadyActioned)
            {
                messages.Add("<sprite name=\"error\">Character has already acted this turn.");
            }
            else if (cardData.IsEncounterCard() && cardData.encounterTargetHex != null)
            {
                string hexCoords = $"{cardData.encounterTargetHex.v2.x}, {cardData.encounterTargetHex.v2.y}";
                messages.Add($"<sprite name=\"error\">Move your character to hex {hexCoords} to investigate.");
            }
            else if (cardData.IsEncounterCard())
            {
                messages.Add("<sprite name=\"error\">Move your character to that hex to investigate.");
            }
            else
            {
                messages.Add("<sprite name=\"error\">Action conditions not met.");
            }
        }

        if (cardData.playability.failsCardHistoryRequirements)
        {
            string historyReason = string.IsNullOrWhiteSpace(cardData.playability.cardHistoryReason)
                ? "Card history requirements not met."
                : cardData.playability.cardHistoryReason;
            messages.Add($"<sprite name=\"error\">{historyReason}");
        }

        return string.Join("\n", messages);
    }

    private void AppendMissingLevelMessages(List<string> messages, Character selected)
    {
        if (messages == null) return;

        if (selected == null)
        {
            messages.Add("<sprite name=\"error\">Select a character first.");
            return;
        }

        AppendMissingLevelMessage(messages, "Commander", cardData.commanderSkillRequired, selected.GetCommander());
        AppendMissingLevelMessage(messages, "Agent", cardData.agentSkillRequired, selected.GetAgent());
        AppendMissingLevelMessage(messages, "Emissary", cardData.emissarySkillRequired, selected.GetEmmissary());
        bool spellArcaneBypass = cardData.GetCardType() == CardTypeEnum.Spell
            && selected.HasStatusEffect(StatusEffectEnum.ArcaneInsight);
        if (!spellArcaneBypass)
            AppendMissingLevelMessage(messages, "Mage", cardData.mageSkillRequired, selected.GetMage());
    }

    private void AppendMissingLevelMessage(List<string> messages, string label, int required, int current)
    {
        if (messages == null || required <= 0 || current >= required) return;
        messages.Add($"<sprite name=\"error\">Need {label} {required}.");
    }

    private string BuildMissingResourceMessage(Leader resourceOwner)
    {
        if (cardData == null || cardData.playability == null) return string.Empty;

        if (resourceOwner == null)
        {
            return "<sprite name=\"error\">No leader is available to pay the card cost.";
        }

        List<string> parts = new();
        AppendMissingResourcePart(parts, "leather", cardData.leatherRequired, resourceOwner.leatherAmount);
        AppendMissingResourcePart(parts, "timber", cardData.timberRequired, resourceOwner.timberAmount);
        AppendMissingResourcePart(parts, "mounts", cardData.mountsRequired, resourceOwner.mountsAmount);
        AppendMissingResourcePart(parts, "iron", cardData.ironRequired, resourceOwner.ironAmount);
        AppendMissingResourcePart(parts, "steel", cardData.steelRequired, resourceOwner.steelAmount);
        AppendMissingResourcePart(parts, "mithril", cardData.mithrilRequired, resourceOwner.mithrilAmount);

        int goldCost = cardData.GetTotalGoldCost();
        if (goldCost > 0 && resourceOwner.goldAmount < goldCost)
        {
            parts.Add($"{goldCost}<sprite name=\"gold\">");
        }

        if (parts.Count == 0) return string.Empty;
        return $"<sprite name=\"error\">Need {string.Join(string.Empty, parts)}";
    }

    private void AppendMissingResourcePart(List<string> parts, string resourceName, int required, int current)
    {
        if (parts == null || required <= 0 || current >= required) return;
        parts.Add($"{required}<sprite name=\"{resourceName}\">");
    }

    // Returns (success, actionRollFailed): success is false only when the card couldn't be
    // played at all (no valid target/conditions, or couldn't be consumed from hand) — the card
    // stays in hand in that case. Once the card is spent, success is true; actionRollFailed
    // distinguishes a spent-but-fizzled roll (difficulty check) from a genuine effect landing,
    // so the caller can pick the fly-to-hex vs. shake-and-dissolve presentation.
    private async Task<(bool success, bool actionRollFailed)> HandleActionCardPlayed(Character selected)
    {
        string actionRef = cardData.GetActionRef();
        if (string.IsNullOrWhiteSpace(actionRef))
        {
            Debug.LogWarning($"[CardPlay/Action] '{cardData.name}' has no actionRef set on its CardData — cannot resolve a CharacterAction. Check the card's JSON/asset.");
            return (false, false);
        }

        if (actionsManager == null)
        {
            Debug.LogWarning($"[CardPlay/Action] '{cardData.name}': this Card has no actionsManager reference.");
            return (false, false);
        }

        CharacterAction action = actionsManager.ResolveActionByRef(actionRef, cardData);
        if (action == null)
        {
            Debug.LogWarning($"[CardPlay/Action] '{cardData.name}': ResolveActionByRef('{actionRef}') returned null — no CharacterAction class is registered for this ref.");
            return (false, false);
        }
        if (selected == null)
        {
            Debug.LogWarning($"[CardPlay/Action] '{cardData.name}': no character selected — cannot Initialize/Execute the action.");
            return (false, false);
        }

        action.Initialize(selected, cardData);
        if (!action.FulfillsConditions())
        {
            Debug.LogWarning($"[CardPlay/Action] '{cardData.name}': action '{action.GetType().Name}'.FulfillsConditions() returned false for '{selected.characterName}' — condition delegate rejected it (see the specific action's Initialize()).");
            return (false, false);
        }

        Game game = Game.Instance;
        PlayableLeader playerLeader = game != null ? game.player : null;
        if (playerLeader == null)
        {
            Debug.LogWarning($"[CardPlay/Action] '{cardData.name}': Game.Instance.player is null — no human PlayableLeader to consume the card from.");
            return (false, false);
        }

        // Try to consume the card from hand first
        // We use the card name now as the ID
        bool drawReplacementCard = false;
        if (!deckManager.TryConsumeActionCard(playerLeader, actionRef, drawReplacementCard, out _, cardData.name))
        {
            Debug.LogWarning($"[CardPlay/Action] '{cardData.name}': deckManager.TryConsumeActionCard failed (actionRef='{actionRef}') — card not found in hand under that ref/name, or DeckManager rejected the consume.");
            return (false, false);
        }

        // Apply any map reveals immediately if it's a Land or PC card
        deckManager.ApplyMapRevealForPlayedCard(playerLeader, cardData);

        // Execute the action
        action.Initialize(selected, cardData);
        Debug.Log($"[CardPlay/Action] '{cardData.name}': card consumed, executing action '{action.GetType().Name}' for '{selected.characterName}'.");
        await action.Execute();

        if (!action.LastExecutionSucceeded)
        {
            // If the action failed or was cancelled, we should probably return the card to hand,
            // but the current game design usually consumes it anyway on fail.
            // If we want to return it:
            // deckManager.TryReturnActionCardToHand(playerLeader, actionRef);
            Debug.LogWarning($"[CardPlay/Action] '{cardData.name}': action '{action.GetType().Name}'.Execute() did not succeed (LastExecutionSucceeded=false) — likely failed its difficulty roll, or effect/asyncEffect returned false. Card is still spent.");
            return (true, true);
        }

        Debug.Log($"[CardPlay/Action] '{cardData.name}': action '{action.GetType().Name}' executed successfully.");
        return (true, false);
    }

    private Task<bool> HandleEnvironmentalCardPlayed(Character selected)
    {
        Game game = Game.Instance;
        if (game == null)
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': Game.Instance is null.");
            return Task.FromResult(false);
        }
        PlayableLeader playerLeader = game.player;
        if (playerLeader == null)
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': game.player is null.");
            return Task.FromResult(false);
        }

        EnvironmentalCardManager environment = EnvironmentalCardManager.GetOrCreate();
        if (!environment.CanNationPlay(playerLeader, game.turn, out string environmentalReason))
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': CanNationPlay=false — {environmentalReason}");
            if (selected?.hex != null)
                MessageDisplayNoUI.ShowMessage(selected.hex, selected, environmentalReason, Color.red);
            return Task.FromResult(false);
        }

        if (!deckManager.TryConsumeCard(playerLeader, cardData.name, false, out _))
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': deckManager.TryConsumeCard failed.");
            return Task.FromResult(false);
        }

        if (!environment.TrySetActiveCard(cardData, playerLeader, out _))
        {
            Debug.LogWarning($"[CardPlay/Environmental] '{cardData.name}': environment.TrySetActiveCard failed (already consumed from hand).");
            return Task.FromResult(false);
        }

        // Environmental cards don't roll/resolve immediately like Action cards — they become
        // the ongoing effect and apply at the start of next turn. Without an explicit message
        // here, playing one looks like nothing happened at all.
        Hex feedbackHex = selected != null ? selected.hex : null;
        if (feedbackHex != null)
        {
            MessageDisplayNoUI.ShowMessage(feedbackHex, selected, $"{cardData.name} takes hold — effects begin next turn", Color.green);
        }

        // TryConsumeCard rebuilds CardBloom and schedules this Card for destruction.
        // Complete synchronously so TryPlayCard can run its shared success path before
        // Unity destroys the clicked object at the end of the frame.
        return Task.FromResult(true);
    }

    private async Task<bool> HandleEncounterCardPlayed(Character selected)
    {
        if (!cardData.encounterRevealed)
        {
            if (canvasGroup != null) canvasGroup.interactable = false;
            await RevealEncounterCardAsync();
            cardData.encounterRevealed = true;
            UpdateInteractableState();
        }

        Game game = Game.Instance;
        PlayableLeader playerLeader = game != null ? game.player : null;
        if (playerLeader == null)
        {
            Debug.LogWarning($"[CardPlay/Encounter] '{cardData.name}': game.player is null.");
            return false;
        }

        bool drawReplacementCard = false;
        if (!deckManager.TryConsumeCard(playerLeader, cardData.name, drawReplacementCard, out _))
        {
            Debug.LogWarning($"[CardPlay/Encounter] '{cardData.name}': deckManager.TryConsumeCard failed.");
            return false;
        }

        bool resolved = await EncounterResolver.ResolveAsync(cardData, selected);
        if (!resolved)
        {
            Debug.LogWarning($"[CardPlay/Encounter] '{cardData.name}': EncounterResolver.ResolveAsync returned false — returning card to hand.");
            deckManager.TryReturnCardToHand(playerLeader, cardData.name);
        }

        return resolved;
    }

    private async Task RevealEncounterCardAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        StartCoroutine(RevealEncounterCoroutine(tcs));
        await tcs.Task;
    }

    private IEnumerator RevealEncounterCoroutine(TaskCompletionSource<bool> tcs)
    {
        const float FadeDuration = 0.8f;
        float elapsed = 0f;

        while (elapsed < FadeDuration)
        {
            if (this == null) { tcs.TrySetResult(false); yield break; }
            float alpha = 1f - elapsed / FadeDuration;

            if (encounterArtOverlay != null)
            {
                Color c = encounterArtOverlay.color;
                c.a = alpha;
                encounterArtOverlay.color = c;
            }
            if (encounterQuestionMark != null)
            {
                Color c = encounterQuestionMark.color;
                c.a = alpha;
                encounterQuestionMark.color = c;
            }
            if (encounterTokenOverlay != null)
            {
                Color c = encounterTokenOverlay.color;
                c.a = alpha;
                encounterTokenOverlay.color = c;
            }
            if (encounterTokenQuestionMark != null)
            {
                Color c = encounterTokenQuestionMark.color;
                c.a = alpha;
                encounterTokenQuestionMark.color = c;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (encounterArtOverlay != null)
        {
            Destroy(encounterArtOverlay.gameObject);
            encounterArtOverlay = null;
            encounterQuestionMark = null;
        }
        if (encounterTokenOverlay != null)
        {
            Destroy(encounterTokenOverlay.gameObject);
            encounterTokenOverlay = null;
            encounterTokenQuestionMark = null;
        }

        if (titleText != null) titleText.text = FormatCardTitle(cardData.name);

        string realDescription = GetActionDescription(cardData);
        if (TypewriterEffect)
        {
            yield return StartCoroutine(TypewriterEffectCoroutine(descriptionText, realDescription));
        }
        else if (descriptionText != null)
        {
            descriptionText.text = realDescription;
        }
        baseDescription = realDescription;

        tcs.SetResult(true);
    }

    private IEnumerator TypewriterEffectCoroutine(TextMeshProUGUI textComponent, string fullText)
    {
        if (textComponent == null || string.IsNullOrEmpty(fullText)) yield break;
        textComponent.text = string.Empty;
        float delay = Mathf.Min(0.05f, 2f / fullText.Length);
        foreach (char c in fullText)
        {
            if (textComponent == null) yield break;
            textComponent.text += c;
            yield return new WaitForSecondsRealtime(delay);
        }
    }

    private Task<bool> HandleCharacterCardPlayed(Character selected)
    {
        Game game = Game.Instance;
        PlayableLeader playerLeader = game != null ? game.player : null;
        if (playerLeader == null || selected == null)
        {
            Debug.LogWarning($"[CardPlay/Character] '{cardData.name}': playerLeader={(playerLeader != null)}, selected={(selected != null)} — both required.");
            return Task.FromResult(false);
        }

        Hex hex = selected.hex;
        PC pc = hex?.GetPCData();
        if (pc == null || !CardNameUtility.Equals(pc.pcName, cardData.startingPC))
        {
            Debug.LogWarning($"[CardPlay/Character] '{cardData.name}': selected character's hex PC ('{pc?.pcName ?? "none"}') does not match required startingPC ('{cardData.startingPC}').");
            return Task.FromResult(false);
        }

        bool drawReplacementCard = false;
        if (!deckManager.TryConsumeCard(playerLeader, cardData.name, drawReplacementCard, out _))
        {
            Debug.LogWarning($"[CardPlay/Character] '{cardData.name}': deckManager.TryConsumeCard failed.");
            return Task.FromResult(false);
        }

        string characterName = cardData.name;
        Character existing = FindCharacterByName(characterName) ?? FindCharacterByGroup(cardData.characterGroup);

        if (existing == null)
        {
            if (!playerLeader.HasCharacterSlot())
            {
                Debug.LogWarning($"[CardPlay/Character] '{cardData.name}': no character slots available for '{playerLeader.characterName}'.");
                MessageDisplay.ShowMessage("No character slots available.", Color.red);
                return Task.FromResult(false);
            }

            CharacterInstantiator instantiator = FindFirstObjectByType<CharacterInstantiator>();
            if (instantiator == null)
            {
                Debug.LogWarning($"[CardPlay/Character] '{cardData.name}': no CharacterInstantiator found in scene.");
                return Task.FromResult(false);
            }

            BiomeConfig config = new()
            {
                characterName = characterName,
                alignment = (AlignmentEnum)cardData.alignment,
                race = cardData.race,
                sex = cardData.sex,
                commander = cardData.commander,
                agent = cardData.agent,
                emmissary = cardData.emmissary,
                mage = cardData.mage,
                artifacts = cardData.artifacts != null ? new List<string>(cardData.artifacts) : new List<string>()
            };

            Character newCharacter = instantiator.InstantiateCharacter(playerLeader, hex, config);
            if (newCharacter == null)
            {
                return Task.FromResult(false);
            }

            newCharacter.startingCharacter = false;
            newCharacter.characterGroup = cardData.characterGroup;
            newCharacter.hasActionedThisTurn = true;
            newCharacter.isPlayerControlled = playerLeader == game.player;
            playerLeader.TryConsumeCharacterSlot();
            hex.RedrawCharacters();

            string joinMessage = $"{characterName} has joined {playerLeader.characterName}.";
            MessageDisplayNoUI.ShowMessage(hex, newCharacter, joinMessage, Color.green, recordRumour: false);

            Rumour rumour = new Rumour
            {
                leader = playerLeader,
                character = newCharacter,
                characterName = characterName,
                rumour = joinMessage,
                v2 = hex.v2
            };
            RumoursManager.AddRumour(rumour, isPublic: false);

            return Task.FromResult(true);
        }
        else
        {
            Leader existingOwner = existing.GetOwner();
            bool heldByEnemy = existingOwner != null && existingOwner != playerLeader
                && existingOwner.GetAlignment() != playerLeader.GetAlignment();

            if (heldByEnemy)
            {
                string blockedMessage = $"Inspiration from {characterName} not available: they serve the enemy";
                EventIconsManager.ShowHexAnchoredMessage(EventIconType.HexMessage, existing.hex, hex, blockedMessage, Color.red);

                int doubleAgentTurns = UnityEngine.Random.value < 0.5f ? 1 : 3;
                existing.BecomeDoubleAgent(playerLeader, doubleAgentTurns);

                string doubledMessage = $"{characterName} is doubled for {doubleAgentTurns} turns";
                EventIconsManager.ShowHexAnchoredMessage(EventIconType.HexMessage, existing.hex, hex, doubledMessage, Color.yellow);

                return Task.FromResult(true);
            }

            InspireEffect effect = InspireEffectFactory.CreateFromCardData(cardData);
            if (effect != null)
            {
                effect.Apply(playerLeader);
            }

            string pcName = pc.pcName;
            string inspireMessage = $"The presence of {characterName} inspires {pcName}.";
            MessageDisplayNoUI.ShowMessage(hex, existing, inspireMessage, Color.cyan, recordRumour: false);

            Rumour rumour = new Rumour
            {
                leader = existing.GetOwner() ?? playerLeader,
                character = existing,
                characterName = characterName,
                rumour = inspireMessage,
                v2 = hex.v2
            };
            RumoursManager.AddRumour(rumour, isPublic: false);

            return Task.FromResult(true);
        }
    }

    private static Character FindCharacterByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        Character[] characters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        return characters.FirstOrDefault(c => c != null && CardNameUtility.Equals(c.characterName, name));
    }

    private static Character FindCharacterByGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group)) return null;
        Character[] characters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        return characters.FirstOrDefault(c => c != null && !c.killed && string.Equals(c.characterGroup, group, System.StringComparison.OrdinalIgnoreCase));
    }

}
