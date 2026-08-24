using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Paged grid of cards for browsing (deck lists, compendiums, card pickers, ...). Cards render
// as the usual token face with a caption label below each one; hovering a card shows the
// enlarged CardCenterPreview exactly like the hand tray / bloom wheel do. Unlike hand cards, a
// gallery card never plays itself on click — callers that want a click action hook
// OnCardClicked (see RegisterOnCardClicked).
public class CardGallery : MonoBehaviour
{
    [Header("Grid")]
    [Tooltip("How many card columns are visible per page.")]
    [SerializeField] private int columns = 1;
    [Tooltip("How many card rows are visible per page.")]
    [SerializeField] private int rows = 1;
    [Tooltip("Visual size of each card (excludes the label below it).")]
    [SerializeField] private Vector2 cardSize = new(120f, 170f);
    [Tooltip("Gap between grid cells (each cell is the card plus its label).")]
    [SerializeField] private Vector2 spacing = new(20f, 20f);

    [Header("Label")]
    [Tooltip("Height reserved for the caption text below each card.")]
    [SerializeField] private float labelHeight = 28f;
    [Tooltip("Gap between the card and its label.")]
    [SerializeField] private float labelSpacing = 4f;
    [SerializeField] private float labelFontSize = 14f;
    [SerializeField] private Color labelColor = Color.white;
    [SerializeField] private TextAlignmentOptions labelAlignment = TextAlignmentOptions.Center;
    [Tooltip("TMP sprite asset (spritesheet) used to resolve <sprite name=...> tags in the label text, e.g. troop-type icons. The label is built at runtime, so it has no sprite asset of its own unless one is assigned here.")]
    [SerializeField] private TMP_SpriteAsset labelSpriteAsset;

    [Header("References")]
    [Tooltip("Parent that holds the instantiated card cells. A GridLayoutGroup is added here automatically if missing.")]
    [SerializeField] private RectTransform gridContainer;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;

    [Header("Events")]
    [Tooltip("Invoked when a card cell is clicked. The gallery itself never plays or otherwise consumes the card.")]
    [SerializeField] private UnityEvent<CardData> onCardClicked;

    private readonly List<CardData> cards = new();
    private readonly List<GameObject> cardInstances = new();
    private GridLayoutGroup gridLayout;
    private int currentPage;

    // Caption shown below each card; defaults to the card's name. Override via SetLabelSelector
    // for custom captions (cost, count, anything else the caller wants to show).
    private Func<CardData, string> labelSelector = data => data != null ? data.name : string.Empty;

    private int CardsPerPage => Mathf.Max(1, columns) * Mathf.Max(1, rows);
    private int PageCount => cards.Count == 0 ? 0 : Mathf.CeilToInt(cards.Count / (float)CardsPerPage);

    private void Awake()
    {
        if (gridContainer == null) gridContainer = transform as RectTransform;

        gridLayout = gridContainer.GetComponent<GridLayoutGroup>();
        if (gridLayout == null) gridLayout = gridContainer.gameObject.AddComponent<GridLayoutGroup>();
        gridLayout.childAlignment = TextAnchor.MiddleCenter;
        ApplyGridConfiguration();
    }

    private void OnEnable()
    {
        if (previousButton != null) previousButton.onClick.AddListener(ShowPreviousPage);
        if (nextButton != null) nextButton.onClick.AddListener(ShowNextPage);
        Refresh();
    }

    private void OnDisable()
    {
        if (previousButton != null) previousButton.onClick.RemoveListener(ShowPreviousPage);
        if (nextButton != null) nextButton.onClick.RemoveListener(ShowNextPage);
    }

    public void SetCards(List<CardData> newCards)
    {
        cards.Clear();
        if (newCards != null) cards.AddRange(newCards);
        currentPage = 0;
        Refresh();
    }

    // Grid dimensions double as "how many to show" (columns * rows per page); 1x1 shows one
    // card at a time, e.g. 3x2 shows six.
    public void SetGridSize(int newColumns, int newRows)
    {
        columns = Mathf.Max(1, newColumns);
        rows = Mathf.Max(1, newRows);
        currentPage = 0;
        ApplyGridConfiguration();
        Refresh();
    }

    public void SetCardSize(Vector2 newCardSize)
    {
        cardSize = newCardSize;
        ApplyGridConfiguration();
        Refresh();
    }

    public void SetLabelSelector(Func<CardData, string> selector)
    {
        labelSelector = selector ?? (data => data != null ? data.name : string.Empty);
        Refresh();
    }

    public void SetLabelStyle(float fontSize, Color color, TextAlignmentOptions alignment)
    {
        labelFontSize = fontSize;
        labelColor = color;
        labelAlignment = alignment;
        Refresh();
    }

    public void SetLabelSpriteAsset(TMP_SpriteAsset spriteAsset)
    {
        labelSpriteAsset = spriteAsset;
        Refresh();
    }

    public void ShowNextPage()
    {
        if (currentPage >= PageCount - 1) return;
        currentPage++;
        Refresh();
    }

    public void ShowPreviousPage()
    {
        if (currentPage <= 0) return;
        currentPage--;
        Refresh();
    }

    public void SetPage(int pageIndex)
    {
        currentPage = Mathf.Clamp(pageIndex, 0, Mathf.Max(0, PageCount - 1));
        Refresh();
    }

    public int GetPageCount() => PageCount;
    public int GetCurrentPage() => currentPage;

    public void RegisterOnCardClicked(UnityAction<CardData> listener)
    {
        if (listener != null) onCardClicked.AddListener(listener);
    }

    public void UnregisterOnCardClicked(UnityAction<CardData> listener)
    {
        if (listener != null) onCardClicked.RemoveListener(listener);
    }

    private void ApplyGridConfiguration()
    {
        if (gridLayout == null) return;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = Mathf.Max(1, columns);
        gridLayout.cellSize = new Vector2(cardSize.x, cardSize.y + labelSpacing + labelHeight);
        gridLayout.spacing = spacing;
    }

    private void Refresh()
    {
        ClearCardInstances();

        bool hasMultiplePages = PageCount > 1;
        if (previousButton != null)
        {
            previousButton.gameObject.SetActive(hasMultiplePages);
            previousButton.interactable = currentPage > 0;
        }
        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(hasMultiplePages);
            nextButton.interactable = currentPage < PageCount - 1;
        }

        if (cards.Count == 0) return;

        DeckManager deckManager = DeckManager.Instance;
        GameObject template = deckManager != null ? deckManager.GetTokenCardPrefabTemplate() : null;
        if (template == null)
        {
            Debug.LogWarning($"[CardGallery] '{name}' Refresh aborted — no card template (DeckManager.Instance={deckManager != null}).");
            return;
        }

        int start = currentPage * CardsPerPage;
        int end = Mathf.Min(start + CardsPerPage, cards.Count);

        for (int i = start; i < end; i++)
        {
            CardData data = cards[i];
            if (data == null) continue;

            cardInstances.Add(BuildCardCell(template, data));
        }
    }

    // Builds one grid cell: a plain RectTransform running a VerticalLayoutGroup that stacks the
    // card token on top of its caption label, so the whole cell — not just the card — is what
    // the GridLayoutGroup on gridContainer positions and pages.
    private GameObject BuildCardCell(GameObject template, CardData data)
    {
        var cellGo = new GameObject(string.IsNullOrEmpty(data.name) ? "Card" : data.name, typeof(RectTransform));
        cellGo.transform.SetParent(gridContainer, false);

        VerticalLayoutGroup cellLayout = cellGo.AddComponent<VerticalLayoutGroup>();
        cellLayout.childAlignment = TextAnchor.UpperCenter;
        cellLayout.childControlWidth = true;
        cellLayout.childControlHeight = true;
        cellLayout.childForceExpandWidth = false;
        cellLayout.childForceExpandHeight = false;
        cellLayout.spacing = labelSpacing;

        GameObject cardGo = Instantiate(template, cellGo.transform);
        cardGo.SetActive(true);

        LayoutElement cardLayoutElement = cardGo.GetComponent<LayoutElement>();
        if (cardLayoutElement == null) cardLayoutElement = cardGo.AddComponent<LayoutElement>();
        cardLayoutElement.preferredWidth = cardSize.x;
        cardLayoutElement.preferredHeight = cardSize.y;

        Card cardComponent = cardGo.GetComponent<Card>();
        if (cardComponent != null)
        {
            cardComponent.UseCardArtFolderOnly = true;
            cardComponent.ShowCloseIcon = false;
            cardComponent.Initialize(data);
        }

        AddClickCatcher(cardGo, data);
        BuildLabel(cellGo.transform, data);

        return cellGo;
    }

    private void BuildLabel(Transform parent, CardData data)
    {
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(parent, false);

        TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
        if (labelSpriteAsset != null) label.spriteAsset = labelSpriteAsset;
        label.text = labelSelector(data);
        label.fontSize = labelFontSize;
        label.color = labelColor;
        label.alignment = labelAlignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;

        LayoutElement labelLayoutElement = labelGo.AddComponent<LayoutElement>();
        labelLayoutElement.preferredWidth = cardSize.x;
        labelLayoutElement.preferredHeight = labelHeight;
    }

    // A full-cover, topmost click target: swallows the click (routing it to onCardClicked)
    // without touching the card's own raycasting, so hover -> CardCenterPreview (driven by
    // Card.OnPointerEnter/Exit walking up from whatever the raycaster hits) keeps working
    // exactly as it does everywhere else token cards are shown. ignoreParentGroups keeps it
    // clickable even when the card dims itself out as currently unplayable.
    private void AddClickCatcher(GameObject cardGo, CardData data)
    {
        var catcherGo = new GameObject("ClickCatcher", typeof(RectTransform));
        catcherGo.transform.SetParent(cardGo.transform, false);
        catcherGo.transform.SetAsLastSibling();

        var rt = catcherGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        var img = catcherGo.AddComponent<Image>();
        img.color = Color.clear;
        img.raycastTarget = true;

        var cg = catcherGo.AddComponent<CanvasGroup>();
        cg.ignoreParentGroups = true;

        var btn = catcherGo.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onCardClicked.Invoke(data));
    }

    private void ClearCardInstances()
    {
        foreach (GameObject go in cardInstances)
            if (go != null) Destroy(go);
        cardInstances.Clear();
    }
}
