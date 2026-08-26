using System.Collections.Generic;
using System.Collections;
using TMPro;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SelectionDialog : MonoBehaviour
{
    public static SelectionDialog Instance { get; private set; }
    public static bool IsShowing { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject content;
    [SerializeField] private TextMeshProUGUI messageLabel;
    [SerializeField] private Button noButton;
    [SerializeField] private Image portraitImage;
    [SerializeField] private CanvasGroup portraitCanvasGroup;
    [SerializeField] private Illustrations illustrations;
    [SerializeField] private TextMeshProUGUI title;

    [Header("Option Buttons")]
    [SerializeField] private Transform optionButtonsContainer;
    [SerializeField] private GameObject optionButtonPrefab;

    [Header("Typewriter")]
    [SerializeField] private TypewriterEffect messageTypewriter;

    private readonly List<DialogRequest> queuedRequests = new();
    private readonly List<DialogRequest> pendingIconRequests = new();
    private DialogRequest activeRequest;
    private readonly List<Button> optionButtons = new();
    private int selectedButtonIndex = -1;
    private Coroutine buttonAnimCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (illustrations == null) illustrations = FindFirstObjectByType<Illustrations>();
        BindUiReferences();
        WireUiListeners();

        DontDestroyOnLoad(gameObject);
        HideInstant();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Opens a confirmation dialog. Portrait, dialogTitle and optionIcons are mandatory: every
    /// dialog must show a character/card portrait, a title, and a real icon per option (never
    /// null) - see GetCharacterIllustration/CharacterIconNames/CardIconNames for how callers
    /// resolve those from existing character/card art instead of leaving them blank.
    /// </summary>
    public static Task<string> Ask(string message, string yesString, string noString, List<string> options, List<string> optionDescriptions, bool isAI, Sprite portrait, EventIconType iconType, string dialogTitle, List<string> optionIcons)
    {
        if (Instance == null)
        {
            Debug.LogError("Selection dialog  was called before its instance was created.");
            return Task.FromResult(string.Empty);
        }

        return Instance.Show(message, yesString, noString, options, optionDescriptions, isAI, portrait, iconType, dialogTitle, optionIcons, forceImmediate: false);
    }

    public static Task<string> AskImmediate(string message, string yesString, string noString, List<string> options, List<string> optionDescriptions, bool isAI, Sprite portrait, EventIconType iconType, string dialogTitle, List<string> optionIcons)
    {
        if (Instance == null)
        {
            Debug.LogError("Selection dialog  was called before its instance was created.");
            return Task.FromResult(string.Empty);
        }

        return Instance.Show(message, yesString, noString, options, optionDescriptions, isAI, portrait, iconType, dialogTitle, optionIcons, forceImmediate: true);
    }

    // Resolves each character's own shipped portrait art (illustrationName, falling back to
    // characterName - the same resolution GetCharacterIllustration uses) into an option-icon
    // list, so a character-selection dialog shows each candidate's real portrait as its icon
    // instead of leaving optionIcons null.
    public static List<string> CharacterIconNames(IEnumerable<Character> characters)
    {
        List<string> names = new();
        if (characters == null) return names;
        foreach (Character character in characters)
        {
            names.Add(character != null
                ? (!string.IsNullOrWhiteSpace(character.illustrationName) ? character.illustrationName : character.characterName)
                : null);
        }
        return names;
    }

    // Same as CharacterIconNames but for card-backed options (artifacts/objects/etc.), using
    // each card's own shipped art (spriteName, falling back to its name).
    public static List<string> CardIconNames(IEnumerable<CardData> cards)
    {
        List<string> names = new();
        if (cards == null) return names;
        foreach (CardData card in cards)
        {
            names.Add(card != null
                ? (!string.IsNullOrWhiteSpace(card.spriteName) ? card.spriteName : card.name)
                : null);
        }
        return names;
    }

    private Task<string> Show(string message, string yesString, string noString, List<string> options, List<string> optionDescriptions, bool isAI, Sprite portrait, EventIconType iconType, string dialogTitle, List<string> optionIcons, bool forceImmediate = false)
    {
        BindUiReferences();
        WireUiListeners();
        if (options == null || options.Count < 1)
        {
            // Called with nothing to choose (e.g. a message-only dialog).
            // Show a single dummy option that just dismisses the dialog.
            options = new List<string> { "Close" };
            optionDescriptions = null;
            optionIcons = new List<string> { "Close" };
        }

        if (string.IsNullOrWhiteSpace(dialogTitle))
        {
            Debug.LogError($"SelectionDialog.Show called without a dialogTitle for message '{message}'.");
        }
        if (portrait == null)
        {
            Debug.LogError($"SelectionDialog.Show called without a portrait for message '{message}'.");
        }
        if (optionIcons == null || optionIcons.Count != options.Count)
        {
            Debug.LogError($"SelectionDialog.Show called with mismatched optionIcons for message '{message}'.");
        }

        var request = new DialogRequest
        {
            title = dialogTitle,
            message = message,
            yesString = yesString,
            noString = noString,
            options = options,
            optionDescriptions = optionDescriptions,
            optionIcons = optionIcons,
            portrait = portrait,
            tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        if (isAI)
        {
            int index = Random.Range(0, options.Count);
            request.tcs.TrySetResult(options[index]);
        }
        else if (forceImmediate)
        {
            OpenRequest(request);
        }
        else
        {
            QueueBehindEventIcon(request, iconType);
        }
        return request.tcs.Task;
    }

    private void QueueBehindEventIcon(DialogRequest request, EventIconType iconType)
    {
        EventIconsManager manager = EventIconsManager.FindManager();
        if (manager == null)
        {
            OpenRequest(request);
            return;
        }

        pendingIconRequests.Add(request);
        EventIcon icon = null;
        icon = manager.AddEventIcon(
            iconType,
            discardable: false,
            onOpen: () =>
            {
                pendingIconRequests.Remove(request);
                request.eventIcon = null;
                icon?.ConsumeAndDestroy();
                OpenRequest(request);
            },
            onRemove: null,
            characterPortrait: request.portrait);

        request.eventIcon = icon;
        if (icon == null)
        {
            pendingIconRequests.Remove(request);
            OpenRequest(request);
        }
    }

    private void OpenRequest(DialogRequest request)
    {
        if (request == null) return;

        if (activeRequest != null && activeRequest != request)
        {
            queuedRequests.Add(request);
            return;
        }

        activeRequest = request;
        Debug.Log($"[SelectionDialog] OpenRequest -> '{request.message}'");
        ShowInternal(request);
    }

    public void CloseCurrentSelection()
    {
        Resolve(GetSelectedOptionText());
    }

    private string GetSelectedOptionText()
    {
        if (activeRequest == null || !HasValidSelection()) return string.Empty;

        return selectedButtonIndex >= 0 && selectedButtonIndex < activeRequest.options.Count
            ? activeRequest.options[selectedButtonIndex]
            : string.Empty;
    }

    private void Resolve(string answer)
    {
        Debug.Log($"[SelectionDialog] Resolve -> '{answer}'");
        DialogRequest requestToResolve = activeRequest;
        HideInstant();
        requestToResolve?.tcs?.TrySetResult(answer);
        activeRequest = null;

        if (queuedRequests.Count > 0)
        {
            DialogRequest nextRequest = queuedRequests[0];
            queuedRequests.RemoveAt(0);
            OpenRequest(nextRequest);
        }
    }

    private void HideInstant()
    {
        content.SetActive(false);
        IsShowing = false;
        activeRequest = null;
        ClearOptionButtons();
        UpdatePortrait(null);
    }

    private void ShowActive()
    {
        if (queuedRequests.Count == 0)
        {
            HideInstant();
            return;
        }

        OpenRequest(queuedRequests[0]);
    }

    private void ShowInternal(DialogRequest request)
    {
        if (request == null) return;
        BindUiReferences();
        WireUiListeners();
        content.SetActive(true);
        EnsureDialogHierarchyActive();
        IsShowing = true;
        activeRequest = request;

        bool hasCustomTitle = !string.IsNullOrWhiteSpace(request.title);
        if (messageLabel != null)
        {
            if (messageTypewriter != null) messageTypewriter.Clear();
            messageLabel.text = request.message;
        }
        if (title != null)
        {
            title.text = hasCustomTitle ? FormatTitle(request.title) : string.Empty;
            title.gameObject.SetActive(!string.IsNullOrWhiteSpace(title.text));
        }
        UpdatePortrait(request.portrait);

        selectedButtonIndex = -1;
        BuildOptionButtons(request.options, request.optionDescriptions, request.optionIcons);

        UpdateCloseButtonState();
    }

    private void EnsureDialogHierarchyActive()
    {
        SetUiObjectActive(content, true);
        SetRectScale(content, Vector3.one);
        SetUiObjectActive(messageLabel != null ? messageLabel.gameObject : null, true);
        // Close button removed: options confirm on click, so the dialog never shows it.
        SetUiObjectActive(noButton != null ? noButton.gameObject : null, false);
        GameObject imageRoot = FindDialogChild("Image");
        SetUiObjectActive(imageRoot, true);
        SetRectScale(imageRoot, Vector3.one);

        if (portraitCanvasGroup != null)
        {
            SetUiObjectActive(portraitCanvasGroup.gameObject, true);
            portraitCanvasGroup.alpha = 1f;
            portraitCanvasGroup.interactable = true;
            portraitCanvasGroup.blocksRaycasts = true;
            SetRectScale(portraitCanvasGroup.gameObject, Vector3.one);
        }
        else
        {
            GameObject portraitRoot = FindDialogChild("CharacterImageBg");
            SetUiObjectActive(portraitRoot, true);
            SetRectScale(portraitRoot, Vector3.one);
        }

        if (portraitImage != null)
        {
            SetUiObjectActive(portraitImage.gameObject, true);
            SetRectScale(portraitImage.gameObject, Vector3.one);
        }
    }

    private static string DescribeObject(GameObject target)
    {
        if (target == null) return "null";
        Transform t = target.transform;
        return $"{target.name}(activeSelf={target.activeSelf},activeInHierarchy={target.activeInHierarchy},scale={t.localScale})";
    }

    private static void SetUiObjectActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private void SetChildImageRaycastTarget(string childName, bool raycastTarget)
    {
        GameObject child = FindDialogChild(childName);
        Image image = child != null ? child.GetComponent<Image>() : null;
        if (image != null) image.raycastTarget = raycastTarget;
    }

    private GameObject FindDialogChild(string name)
    {
        if (content == null || string.IsNullOrWhiteSpace(name)) return null;

        Transform[] children = content.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && string.Equals(children[i].name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                return children[i].gameObject;
            }
        }

        return null;
    }

    private static void SetRectScale(GameObject target, Vector3 scale)
    {
        if (target == null) return;
        if (target.transform.localScale != scale)
        {
            target.transform.localScale = scale;
        }
    }

    // Drives the option icon's hover/press color independently of the Button's own
    // targetGraphic (which must stay the full-button raycast catcher — see CreateOptionButton).
    private sealed class IconHoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Graphic icon;
        private Button button;

        public static void AttachTo(GameObject obj, Graphic icon, Button button)
        {
            if (obj == null || icon == null || button == null) return;
            IconHoverTint effect = obj.GetComponent<IconHoverTint>() ?? obj.AddComponent<IconHoverTint>();
            effect.icon = icon;
            effect.button = button;
            effect.Apply(button.colors.normalColor);
        }

        public void OnPointerEnter(PointerEventData eventData) => Apply(button.colors.highlightedColor);
        public void OnPointerExit(PointerEventData eventData) => Apply(button.colors.normalColor);
        public void OnPointerDown(PointerEventData eventData) => Apply(button.colors.pressedColor);
        public void OnPointerUp(PointerEventData eventData) => Apply(button.colors.highlightedColor);

        private void Apply(Color color)
        {
            if (icon != null) icon.color = color * button.colors.colorMultiplier;
        }
    }

    private class DialogRequest
    {
        public string title;
        public string message;
        public string yesString;
        public string noString;
        public List<string> options;
        public List<string> optionDescriptions;
        public List<string> optionIcons;
        public Sprite portrait;
        public EventIcon eventIcon;
        public TaskCompletionSource<string> tcs;
    }

    private sealed class CloseButtonFallback : MonoBehaviour, IPointerClickHandler
    {
        private SelectionDialog dialog;

        public void Bind(SelectionDialog owner)
        {
            dialog = owner;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            dialog?.CloseCurrentSelection();
        }
    }

    public static void CloseAll()
    {
        if (Instance == null) return;
        Instance.ForceClose();
    }

    private void ForceClose()
    {
        DialogRequest requestToClose = activeRequest;
        requestToClose?.tcs?.TrySetResult(string.Empty);
        for (int i = 0; i < pendingIconRequests.Count; i++)
        {
            pendingIconRequests[i]?.eventIcon?.ConsumeAndDestroy();
            pendingIconRequests[i]?.tcs?.TrySetResult(string.Empty);
        }
        pendingIconRequests.Clear();
        for (int i = 0; i < queuedRequests.Count; i++)
        {
            queuedRequests[i]?.eventIcon?.ConsumeAndDestroy();
            queuedRequests[i]?.tcs?.TrySetResult(string.Empty);
        }
        queuedRequests.Clear();
        activeRequest = null;
        HideInstant();
    }

    private void UpdatePortrait(Sprite portrait)
    {
        bool hasPortrait = portrait != null;
        if (portraitImage != null)
        {
            portraitImage.sprite = portrait;
            portraitImage.enabled = hasPortrait;
        }
        if (portraitCanvasGroup != null)
        {
            // portraitCanvasGroup ("CharacterImageBg") is also the parent of the title and
            // message text in this prefab, so alpha must stay at 1 regardless of whether a
            // portrait was supplied — zeroing it here used to blank the whole dialog body
            // whenever a dialog (e.g. Endless Stairs) was opened without a portrait.
            portraitCanvasGroup.alpha = 1f;
            portraitCanvasGroup.interactable = hasPortrait;
            portraitCanvasGroup.blocksRaycasts = hasPortrait;
        }
    }

    private bool HasValidSelection()
    {
        return selectedButtonIndex >= 0 && selectedButtonIndex < (activeRequest?.options.Count ?? 0);
    }

    private void UpdateCloseButtonState()
    {
        if (noButton != null)
        {
            noButton.interactable = HasValidSelection();
        }
    }

    private void BindUiReferences()
    {
        if (content == null)
        {
            content = FindDialogChild("Content");
        }

        if (messageLabel == null)
        {
            messageLabel = FindTextChild("Text");
        }

        if (noButton == null)
        {
            GameObject closeButton = FindDialogChild("CloseButton") ?? FindDialogChild("NoButton");
            if (closeButton != null)
            {
                noButton = closeButton.GetComponent<Button>();
            }
        }

        if (portraitImage == null)
        {
            GameObject portraitObject = FindDialogChild("CharacterImage");
            if (portraitObject != null)
            {
                portraitImage = portraitObject.GetComponent<Image>();
            }
        }

        if (portraitCanvasGroup == null)
        {
            GameObject portraitBg = FindDialogChild("CharacterImageBg");
            if (portraitBg != null)
            {
                portraitCanvasGroup = portraitBg.GetComponent<CanvasGroup>();
            }
        }

        if (title == null)
        {
            title = FindTextChild("Title");
        }

        // Title/message/decorative frame art are nested inside portraitCanvasGroup's hierarchy
        // (see UpdatePortrait). They default to raycastTarget=true, which — now that the group
        // actually blocks raycasts for dialogs with a portrait — steals clicks meant for option
        // buttons behind them. None of them are interactive, so force this off every time.
        if (messageLabel != null) messageLabel.raycastTarget = false;
        if (title != null) title.raycastTarget = false;
        if (portraitImage != null) portraitImage.raycastTarget = false;
        SetChildImageRaycastTarget("Mask", false);
        SetChildImageRaycastTarget("TitleBg", false);

        if (illustrations == null)
        {
            illustrations = FindFirstObjectByType<Illustrations>();
        }

        if (optionButtonsContainer == null)
        {
            GameObject containerObj = FindDialogChild("OptionsContainer") ?? FindDialogChild("OptionButtons");
            if (containerObj != null) optionButtonsContainer = containerObj.transform;
        }

        if (optionButtonsContainer != null)
        {
            RectTransform containerRect = optionButtonsContainer.GetComponent<RectTransform>();
            if (containerRect != null)
                containerRect.pivot = new Vector2(containerRect.pivot.x, 0f);

            ContentSizeFitter csf = optionButtonsContainer.GetComponent<ContentSizeFitter>()
                ?? optionButtonsContainer.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            VerticalLayoutGroup vlg = optionButtonsContainer.GetComponent<VerticalLayoutGroup>()
                ?? optionButtonsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.LowerLeft;
            vlg.reverseArrangement = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
        }
    }

    private void WireUiListeners()
    {
        if (noButton != null)
        {
            noButton.onClick.RemoveAllListeners();
            noButton.onClick.AddListener(CloseCurrentSelection);
            EnsureCloseButtonFallback(noButton.gameObject);
        }
    }

    private void EnsureCloseButtonFallback(GameObject closeButtonObject)
    {
        if (closeButtonObject == null) return;

        CloseButtonFallback fallback = closeButtonObject.GetComponent<CloseButtonFallback>();
        if (fallback == null)
        {
            fallback = closeButtonObject.AddComponent<CloseButtonFallback>();
        }
        fallback.Bind(this);
    }

    private TextMeshProUGUI FindTextChild(string name)
    {
        GameObject child = FindDialogChild(name);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private bool IsProtectedDialogContainer(GameObject target)
    {
        if (target == null) return false;
        if (target == content) return true;
        if (portraitCanvasGroup != null && target == portraitCanvasGroup.gameObject) return true;

        string name = target.name;
        return string.Equals(name, "Content", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Image", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "CharacterImageBg", System.StringComparison.OrdinalIgnoreCase);
    }

    private static Color GetReadableRandomColor()
    {
        float hue = Random.value;
        float saturation = Random.Range(0.55f, 1f);
        float value = Random.Range(0.8f, 1f);
        return Color.HSVToRGB(hue, saturation, value);
    }

    private static string FormatTitle(string text)
    {
        return $"<sprite name=\"ring2\"> {text} <sprite name=\"ring2\">";
    }

    // ── Typewriter helpers ────────────────────────────────────────────────────

    private void EnsureMessageTypewriter()
    {
        if (messageLabel == null) return;
        if (messageTypewriter == null)
        {
            messageTypewriter = messageLabel.GetComponent<TypewriterEffect>()
                ?? messageLabel.gameObject.AddComponent<TypewriterEffect>();
        }
        messageTypewriter.textMeshPro = messageLabel;
        messageTypewriter.typingSpeed = 28f;
    }

    // ── Option button list ────────────────────────────────────────────────────

    private void BuildOptionButtons(List<string> options, List<string> descriptions = null, List<string> icons = null)
    {
        ClearOptionButtons();
        if (options == null || optionButtonsContainer == null) return;

        for (int i = 0; i < options.Count; i++)
        {
            Color color = GetReadableRandomColor();
            string desc = descriptions != null && i < descriptions.Count ? descriptions[i] : string.Empty;
            string iconOverride = icons != null && i < icons.Count ? icons[i] : null;
            optionButtons.Add(CreateOptionButton(options[i], desc, color, i, iconOverride));
        }

        if (Application.isPlaying)
        {
            if (buttonAnimCoroutine != null) StopCoroutine(buttonAnimCoroutine);
            buttonAnimCoroutine = StartCoroutine(AnimateButtonsIn());
        }
        else
        {
            foreach (Button btn in optionButtons)
            {
                CanvasGroup cg = btn != null ? btn.GetComponent<CanvasGroup>() : null;
                if (cg != null) { cg.alpha = 1f; btn.transform.localScale = Vector3.one; }
            }
        }
    }

    private void ClearOptionButtons()
    {
        if (buttonAnimCoroutine != null) { StopCoroutine(buttonAnimCoroutine); buttonAnimCoroutine = null; }
        foreach (Button btn in optionButtons)
        {
            if (btn == null) continue;
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(btn.gameObject); continue; }
#endif
            Destroy(btn.gameObject);
        }
        optionButtons.Clear();

        // EditorRenderExample/design-time previews can leave serialized option buttons in the
        // prefab. They were never added to optionButtons, so the old cleanup ignored them and
        // every real dialog inherited a stale fourth choice. Remove any remaining option-button
        // instances from the container before rebuilding the request.
        if (optionButtonsContainer != null)
        {
            OptionButtonPrefabManager[] staleOptions = optionButtonsContainer
                .GetComponentsInChildren<OptionButtonPrefabManager>(true);
            foreach (OptionButtonPrefabManager stale in staleOptions)
            {
                if (stale == null || stale.gameObject == optionButtonPrefab) continue;
                stale.gameObject.SetActive(false);
#if UNITY_EDITOR
                if (!Application.isPlaying) { DestroyImmediate(stale.gameObject); continue; }
#endif
                Destroy(stale.gameObject);
            }
        }
        selectedButtonIndex = -1;
    }

    private Button CreateOptionButton(string text, string description, Color textColor, int index, string iconOverride = null)
    {
        bool hasDesc = !string.IsNullOrWhiteSpace(description);
        //string colorHex = ColorUtility.ToHtmlStringRGB(textColor);
        string labelText = hasDesc
            ? $"<b>{text}</b>\n{description}"
            : $"<b>{text}</b>";

        GameObject obj = Instantiate(optionButtonPrefab, optionButtonsContainer, false);
        obj.name = $"Option_{index}";
        OptionButtonPrefabManager prefabManager = obj.GetComponent<OptionButtonPrefabManager>();
        
        CanvasGroup cg = obj.GetComponent<CanvasGroup>() ?? obj.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        // The option icon is a fixed ~64px-tall image (see SelectionManagerOption prefab's
        // "Icon" child); shrinking the button below that spills the icon into neighboring
        // buttons, so the button height must stay at least icon-sized regardless of hasDesc.
        LayoutElement le = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
        le.preferredHeight = hasDesc ? 78f : 70f;
        le.minHeight       = 68f;

        prefabManager.Setup(labelText, iconOverride ?? text);

        // SelectionManagerOption's root has no Image component — the only raycastable
        // graphic on the whole button used to be the ~77x64px icon thumbnail on the left
        // (the label text has raycastTarget off), so clicking anywhere else on the button
        // (i.e. the label, which is most of its area) hit nothing. Add an invisible
        // full-size catcher purely to make the whole button clickable. It stays the
        // Button's targetGraphic (reassigning targetGraphic to the icon broke clicks —
        // Selectable's own pointer bookkeeping seems to key off it for more than color
        // tinting), so the icon's hover/press highlight is driven separately below instead.
        Image background = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
        background.sprite = null;
        background.color = new Color(0.08f, 0.06f, 0.04f, 0f);
        background.raycastTarget = true;

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = background;

        // The "Icon" child ships with its own (listener-less) Button. Unity's event bubbling
        // stops at the nearest ancestor implementing IPointerClickHandler, so clicks landing
        // on the icon were being swallowed by that inner Button and never reaching this one.
        // Disable it (excluded from ExecuteEvents once inactive) so those clicks bubble here.
        foreach (Button nestedBtn in obj.GetComponentsInChildren<Button>(true))
        {
            if (nestedBtn != btn) nestedBtn.enabled = false;
        }

        if (prefabManager.IconGraphic != null) IconHoverTint.AttachTo(obj, prefabManager.IconGraphic, btn);
        int capturedIndex = index;
        btn.onClick.AddListener(() => SelectOptionButton(capturedIndex));

        return btn;
    }

    private void SelectOptionButton(int index)
    {
        if (activeRequest?.options == null || index < 0 || index >= activeRequest.options.Count)
        {
            return;
        }

        // Clicking an option both selects and confirms it: close the dialog and
        // resolve with the chosen option in one step (no separate close button).
        selectedButtonIndex = index;
        Resolve(activeRequest.options[index]);
    }

    private void UpdateButtonSelectionVisuals()
    {
        for (int i = 0; i < optionButtons.Count; i++)
        {
            if (optionButtons[i] == null) continue;
            Image bg = optionButtons[i].GetComponent<Image>();
            if (bg == null) continue;
            bg.color = i == selectedButtonIndex
                ? new Color(0.42f, 0.30f, 0.10f, 1f)
                : new Color(0.08f, 0.06f, 0.04f, 0.88f);
        }
    }

    private IEnumerator AnimateButtonsIn()
    {
        yield return null; // let layout settle
        for (int i = 0; i < optionButtons.Count; i++)
        {
            Button btn = optionButtons[i];
            if (btn == null) continue;
            CanvasGroup cg = btn.GetComponent<CanvasGroup>() ?? btn.gameObject.AddComponent<CanvasGroup>();
            StartCoroutine(FadeScaleInButton(btn.transform, cg));
            yield return new WaitForSecondsRealtime(0.04f);
        }
        buttonAnimCoroutine = null;
    }

    private static IEnumerator FadeScaleInButton(Transform t, CanvasGroup cg)
    {
        float duration = 0.18f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            cg.alpha = p;
            float s = Mathf.Lerp(0.92f, 1f, p);
            t.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        cg.alpha = 1f;
        t.localScale = Vector3.one;
    }

    public Sprite GetCharacterIllustration(Character character)
    {
        if (character == null || string.IsNullOrWhiteSpace(character.characterName)) return null;
        return illustrations != null ? illustrations.GetIllustrationByName(character) : null;
    }

#if UNITY_EDITOR
    public void EditorRenderExample()
    {
        BindUiReferences();
        WireUiListeners();

        if (content != null) content.SetActive(true);
        EnsureDialogHierarchyActive();

        var options = new List<string>
        {
            "Stand Beneath The Black Wing",
            "Vanish Beneath Doom",
            "Cry Up To Cataclysm"
        };
        var descriptions = new List<string>
        {
            "Face the world-shaking wyrm with all the strength you can gather before fire and shadow erase the field itself.",
            "Use perfect timing and a hero's nerve to slip where such colossal destruction is least likely to fall.",
            "Attempt the impossible and speak to the black doom as though pride, darkness, or tribute might stay it for a breath."
        };

        activeRequest = new DialogRequest
        {
            title = "Ancalon",
            message = "You were crossing a mountain pass when the shadow of something vast swallowed the sun, and the stone beneath your feet began to tremble with each distant wingbeat.",
            yesString = "Decide",
            noString = "Cancel",
            options = options,
            optionDescriptions = descriptions,
            portrait = null,
            tcs = null
        };

        if (title != null) { title.text = FormatTitle(activeRequest.title); title.gameObject.SetActive(true); }
        if (messageLabel != null) messageLabel.text = activeRequest.message;

        BuildOptionButtons(options, descriptions); // resets selectedButtonIndex to -1 internally
        selectedButtonIndex = 0;                  // re-apply after build
        UpdateButtonSelectionVisuals();

        UpdateCloseButtonState();
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }

    public void EditorHide()
    {
        activeRequest = null;
        HideInstant();
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }
#endif
}
