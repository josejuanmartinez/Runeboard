using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The deck screen: the cards the campaign has handed the human leader so far, a page at a
/// time, in the order they arrived (starting deck first, then step by step). Cards still to
/// come are not shown. Whenever a campaign step grants cards, the screen opens on its own with
/// just those cards ("New cards"), and from there the whole deck is a click away. Opened from
/// the campaign tracker or with C; Esc, the close button or a click outside closes it.
/// Hovering a card shows its full preview.
/// </summary>
public class DeckViewer : MonoBehaviour
{
    private const int Columns = 8;
    private const int Rows = 2;
    private const int PerPage = Columns * Rows;
    private const float CellWidth = 118f;
    private const float CellHeight = 166f;
    private const float CellGap = 14f;

    private static DeckViewer instance;

    private RectTransform canvasRoot;
    private float K => canvasRoot != null ? Mathf.Max(0.5f, canvasRoot.rect.height / 1080f) : 1f;
    private GameObject overlay;
    private RectTransform grid;
    private TextMeshProUGUI title;
    private TextMeshProUGUI subtitle;
    private TextMeshProUGUI pageLabel;
    private GameObject wholeDeckButton, previousButton, nextButton;
    private List<DeckManager.DeckOverviewEntry> entries = new();
    private int page;
    // The "New cards" page: what the last campaign step granted, and why. Null on the deck page.
    private HashSet<string> newCards;
    private string newHeading;
    // What the campaign had granted when last looked at, to tell which cards are new.
    private HashSet<string> granted;
    private int seenUnlockVersion = -1;
    private int seenGrantVersion = -1;

    public static bool IsOpen => instance != null && instance.overlay != null && instance.overlay.activeSelf;

    public static void Ensure()
    {
        if (instance != null) return;
        instance = new GameObject("DeckViewer").AddComponent<DeckViewer>();
    }

    public static void Toggle()
    {
        Ensure();
        if (IsOpen) instance.Close();
        else instance.Open();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        Game game = Game.Instance;
        bool inGame = game != null && game.started && !game.GameOver && game.player != null;
        if (!inGame) { if (IsOpen) Close(); return; }
        WatchGrants(game.player);
        if (IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.C)) Close();
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.mouseScrollDelta.y < -0.1f) Flip(1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.mouseScrollDelta.y > 0.1f) Flip(-1);
        }
        else if (Input.GetKeyDown(KeyCode.C) && !ConfirmationDialog.IsShowing && !SelectionDialog.IsShowing && !PopupManager.IsShowing
            && EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>() == null)
        {
            Open();
        }
    }

    // ------------------------------------------------------------------ open / close

    private void Open() => Open(null, null);

    // The deck page (cards == null) or the "New cards" page for the cards just granted.
    private void Open(HashSet<string> cards, string heading)
    {
        PlayableLeader player = Game.Instance != null ? Game.Instance.player : null;
        if (player == null || DeckManager.Instance == null || !Build()) return;
        newCards = cards;
        newHeading = heading;
        entries = DeckManager.Instance.GetDeckOverview(player)
            .Where(e => e.unlocked && (cards == null || cards.Contains(e.card.name ?? string.Empty)))
            .OrderBy(e => e.stage)
            .ToList();
        page = 0;
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        Sounds.Instance?.PlayUiClick();
        Render();
    }

    /// <summary>Closes the screen if it is open (AutoTour, after photographing it).</summary>
    public static void CloseIfOpen()
    {
        if (IsOpen) instance.Close();
    }

    // A campaign step reached in play grants cards (its reward, and the tools of the steps it
    // opens): show them. Any other change of the unlock state (a load, a new game) only
    // re-reads what is granted, so nothing pops up for cards the player already had.
    private void WatchGrants(PlayableLeader player)
    {
        CampaignObjectives campaign = CampaignObjectives.Instance;
        DeckManager deck = DeckManager.Instance;
        if (campaign == null || deck == null || !deck.HasDeckFor(player) || campaign.UnlockVersion == seenUnlockVersion) return;
        bool reached = granted != null && campaign.PlayerGrantVersion != seenGrantVersion;
        seenUnlockVersion = campaign.UnlockVersion;
        seenGrantVersion = campaign.PlayerGrantVersion;
        var now = new HashSet<string>(deck.GetGrantedCards(player).Select(c => c.name ?? string.Empty));
        if (reached)
        {
            var fresh = new HashSet<string>(now.Where(n => !granted.Contains(n)));
            CampaignObjective step = campaign.LastPlayerStep;
            string heading = step != null ? $"{CampaignObjectives.TrackOf(step)} step reached: {step.title}" : "More of your deck joins the war";
            if (fresh.Count > 0) Open(fresh, heading);
        }
        granted = now;
    }

    private void Close()
    {
        CardCenterPreview.Instance?.HidePreview();
        if (overlay != null) overlay.SetActive(false);
    }

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)PerPage));

    private void Flip(int delta)
    {
        int next = Mathf.Clamp(page + delta, 0, PageCount - 1);
        if (next == page) return;
        page = next;
        CardCenterPreview.Instance?.HidePreview();
        Sounds.Instance?.PlayUiHover();
        Render();
    }

    // ------------------------------------------------------------------ content

    private void Render()
    {
        PlayableLeader player = Game.Instance.player;
        CampaignObjectives campaign = CampaignObjectives.Instance;
        string cards = $"<color={UiTheme.GoldHex}>{entries.Count}</color> card{(entries.Count == 1 ? "" : "s")}";
        if (newCards != null)
        {
            title.text = "New cards";
            subtitle.text = $"{newHeading}   ·   {cards} join your deck";
        }
        else
        {
            title.text = $"{CampaignObjectives.DisplayLeader(player)}'s deck";
            string progress = campaign != null && campaign.HasCampaign(player)
                ? (campaign.IsChainComplete(player)
                    ? "   ·   Campaign complete: your deck is whole"
                    : "   ·   Each campaign step brings more")
                : string.Empty;
            subtitle.text = $"{cards}{progress}";
        }
        wholeDeckButton.SetActive(newCards != null);

        foreach (Transform child in grid) Destroy(child.gameObject);
        List<DeckManager.DeckOverviewEntry> shown = entries.Skip(page * PerPage).Take(PerPage).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            int col = i % Columns, row = i / Columns;
            // A short last row sits centred under the full ones.
            int inRow = Mathf.Min(Columns, shown.Count - row * Columns);
            float x = (col - (inRow - 1) * 0.5f) * (CellWidth + CellGap);
            float y = -row * (CellHeight + CellGap + 18f);
            CreateCell(shown[i], new Vector2(x, y));
        }
        pageLabel.text = PageCount > 1 ? $"Page {page + 1} / {PageCount}" : string.Empty;
        previousButton.SetActive(PageCount > 1);
        nextButton.SetActive(PageCount > 1);
    }

    private void CreateCell(DeckManager.DeckOverviewEntry entry, Vector2 position)
    {
        CardData card = entry.card;
        bool dim = entry.spent;

        var cell = NewRect(card.name, grid);
        cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 1f);
        cell.pivot = new Vector2(0.5f, 1f);
        cell.sizeDelta = new Vector2(CellWidth, CellHeight);
        cell.anchoredPosition = position;

        var face = cell.gameObject.AddComponent<CinematicPanel>();
        face.fillTop = new Color(0.10f, 0.09f, 0.075f, 1f);
        face.fillBottom = new Color(0.04f, 0.037f, 0.032f, 1f);
        face.inset = 3f;
        face.ornament = 0f;
        face.lineWidth = 1.2f;
        face.line = newCards != null ? new Color(1f, 0.84f, 0.5f, 1f) : new Color(0.86f, 0.70f, 0.40f, 0.85f);
        face.raycastTarget = true;

        var window = NewRect("ArtWindow", cell);
        window.gameObject.AddComponent<RectMask2D>();
        window.anchorMin = Vector2.zero;
        window.anchorMax = Vector2.one;
        window.offsetMin = new Vector2(5f, 5f);
        window.offsetMax = new Vector2(-5f, -5f);

        Sprite sprite = HandTray.ResolveArt(card);
        var art = NewGraphic<Image>(window, "Art");
        art.sprite = sprite;
        art.preserveAspect = false;
        Vector2 windowSize = new(CellWidth - 10f, CellHeight - 10f);
        float aspect = sprite != null ? sprite.rect.width / sprite.rect.height : windowSize.x / windowSize.y;
        art.rectTransform.sizeDelta = aspect > windowSize.x / windowSize.y
            ? new Vector2(windowSize.y * aspect, windowSize.y)
            : new Vector2(windowSize.x, windowSize.x / aspect);
        art.color = sprite == null ? new Color(0.12f, 0.11f, 0.10f, 1f)
            : entry.spent ? new Color(0.5f, 0.5f, 0.5f, 1f)
            : Color.white;

        var plate = NewGraphic<Image>(window, "Plate");
        plate.sprite = HudSprites.FadeUp;
        plate.color = new Color(1f, 1f, 1f, 0.96f);
        var pr = plate.rectTransform;
        pr.anchorMin = new Vector2(0f, 0f);
        pr.anchorMax = new Vector2(1f, 0f);
        pr.pivot = new Vector2(0.5f, 0f);
        pr.sizeDelta = new Vector2(0f, 76f);

        TextMeshProUGUI type = NewText(cell, "Type", 8.5f, TextAlignmentOptions.Bottom);
        type.text = $"<cspace=0.12em>{HandTray.TypeLabel(card)}</cspace>";
        type.color = UiTheme.Gold;
        type.textWrappingMode = TextWrappingModes.NoWrap;
        Place(type.rectTransform, 8f, 42f, 56f);

        TextMeshProUGUI name = NewText(cell, "Name", 14f, TextAlignmentOptions.Top);
        UiTheme.StyleTitle(name, 14f, dim ? UiTheme.Muted : UiTheme.Text);
        name.enableAutoSizing = true;
        name.fontSizeMin = 10f;
        name.fontSizeMax = 14f;
        name.lineSpacing = -12f;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.text = DisplayNames.Format(card.name);
        Place(name.rectTransform, 8f, 7f, 42f);

        if (newCards != null && !entry.spent)
        {
            // A gold tab hanging from the card's top edge.
            var badge = NewGraphic<Image>(cell, "New");
            badge.color = UiTheme.Gold;
            var br = badge.rectTransform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 1f);
            br.pivot = new Vector2(0.5f, 0.5f);
            br.sizeDelta = new Vector2(46f, 17f);
            br.anchoredPosition = new Vector2(0f, -1f);
            TextMeshProUGUI label = NewText(br, "Label", 10f, TextAlignmentOptions.Center);
            UiTheme.StyleTitle(label, 10f, new Color(0.08f, 0.06f, 0.03f, 1f));
            label.text = "<cspace=0.14em>NEW</cspace>";
            Stretch(label.rectTransform);
        }
        if (entry.spent)
        {
            // The "played" note sits in the top of the art.
            TextMeshProUGUI note = NewText(cell, "Note", 10f, TextAlignmentOptions.Top);
            note.text = $"<color={UiTheme.MutedHex}><cspace=0.1em>PLAYED</cspace></color>";
            var nr = note.rectTransform;
            nr.anchorMin = new Vector2(0f, 1f);
            nr.anchorMax = new Vector2(1f, 1f);
            nr.pivot = new Vector2(0.5f, 1f);
            nr.offsetMin = new Vector2(8f, -70f);
            nr.offsetMax = new Vector2(-8f, -12f);
        }

        var hover = cell.gameObject.AddComponent<DeckViewerCell>();
        hover.card = card;
    }

    // ------------------------------------------------------------------ build

    private bool Build()
    {
        if (overlay != null) return true;
        Layout layout = FindAnyObjectByType<Layout>();
        Canvas canvas = layout != null ? layout.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return false;
        canvasRoot = canvas.rootCanvas.transform as RectTransform;

        overlay = new GameObject("DeckViewer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var root = overlay.GetComponent<RectTransform>();
        root.SetParent(canvasRoot, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var dim = overlay.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        var dimButton = overlay.GetComponent<Button>();
        dimButton.transition = Selectable.Transition.None;
        dimButton.onClick.AddListener(Close);

        var panel = NewRect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        float width = Columns * CellWidth + (Columns - 1) * CellGap + 80f;
        float height = Rows * (CellHeight + CellGap + 18f) + 190f;
        panel.sizeDelta = new Vector2(width, height);
        panel.localScale = new Vector3(K, K, 1f);
        var panelFace = panel.gameObject.AddComponent<CinematicPanel>();
        panelFace.fillTop = new Color(0.06f, 0.055f, 0.05f, 0.97f);
        panelFace.fillBottom = new Color(0.025f, 0.023f, 0.02f, 0.98f);
        panelFace.raycastTarget = true;
        // Clicks on the panel itself must not reach the dim backdrop (which closes).
        panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

        title = NewText(panel, "Title", 30f, TextAlignmentOptions.TopLeft);
        UiTheme.StyleTitle(title, 30f, UiTheme.Gold);
        PlaceTop(title.rectTransform, 40f, 24f, 40f);

        subtitle = NewText(panel, "Subtitle", 14f, TextAlignmentOptions.TopLeft);
        subtitle.richText = true;
        PlaceTop(subtitle.rectTransform, 40f, 66f, 22f);

        // On the "New cards" page: the way on to the whole deck.
        wholeDeckButton = AddButton(panel, "Whole deck  ›", new Vector2(1f, 1f), new Vector2(-60f, -30f), new Vector2(170f, 34f), 15f, () => Open(null, null));

        AddButton(panel, "×", new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(34f, 34f), 24f, Close);

        grid = NewRect("Grid", panel);
        grid.anchorMin = grid.anchorMax = new Vector2(0.5f, 1f);
        grid.pivot = new Vector2(0.5f, 1f);
        grid.sizeDelta = new Vector2(width - 80f, height - 190f);
        grid.anchoredPosition = new Vector2(0f, -108f);

        previousButton = AddButton(panel, "‹  Previous", new Vector2(0.5f, 0f), new Vector2(-180f, 26f), new Vector2(150f, 36f), 16f, () => Flip(-1));
        nextButton = AddButton(panel, "Next  ›", new Vector2(0.5f, 0f), new Vector2(180f, 26f), new Vector2(150f, 36f), 16f, () => Flip(1));
        pageLabel = NewText(panel, "Page", 15f, TextAlignmentOptions.Center);
        var pl = pageLabel.rectTransform;
        pl.anchorMin = pl.anchorMax = new Vector2(0.5f, 0f);
        pl.pivot = new Vector2(0.5f, 0f);
        pl.sizeDelta = new Vector2(180f, 36f);
        pl.anchoredPosition = new Vector2(0f, 26f);

        TextMeshProUGUI hint = NewText(panel, "Hint", 11f, TextAlignmentOptions.BottomRight);
        hint.text = $"<color={UiTheme.MutedHex}>C or Esc to close  ·  ← → to turn pages</color>";
        var hr = hint.rectTransform;
        hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(1f, 0f);
        hr.sizeDelta = new Vector2(320f, 20f);
        hr.anchoredPosition = new Vector2(-40f, 34f);

        overlay.SetActive(false);
        return true;
    }

    /// <summary>"12 cards: 6 armies, 4 characters, 2 actions": what a campaign step unlocks.</summary>
    public static string UnlockSummary(PlayableLeader leader, CampaignObjective step, int maxGroups = 3)
    {
        if (DeckManager.Instance == null || leader == null || step == null) return string.Empty;
        List<CardData> cards = DeckManager.Instance.GetCardsUnlockedBy(leader, step);
        if (cards.Count == 0) return string.Empty;
        var groups = cards.GroupBy(c => c.GetCardType()).OrderByDescending(g => g.Count()).ToList();
        var sb = new StringBuilder($"{cards.Count} card{(cards.Count == 1 ? "" : "s")}");
        if (groups.Count > 0) sb.Append(": ");
        sb.Append(string.Join(", ", groups.Take(maxGroups).Select(g => $"{g.Count()} {TypePlural(g.Key, g.Count())}")));
        if (groups.Count > maxGroups) sb.Append(", …");
        return sb.ToString();
    }

    private static string TypePlural(CardTypeEnum type, int n)
    {
        string one = type switch
        {
            CardTypeEnum.PC => "place",
            CardTypeEnum.Environmental => "environment",
            _ => type.ToString().ToLowerInvariant(),
        };
        return n == 1 ? one : one == "army" ? "armies" : one + "s";
    }

    // ------------------------------------------------------------------ helpers

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    private static T NewGraphic<T>(RectTransform parent, string name) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var g = go.GetComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    private static TextMeshProUGUI NewText(RectTransform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var text = NewGraphic<TextMeshProUGUI>(parent, name);
        UiTheme.StyleBody(text, size);
        text.alignment = alignment;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // Bottom band of a cell, inset from both sides.
    private static void Place(RectTransform rt, float side, float bottom, float top)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(side, bottom);
        rt.offsetMax = new Vector2(-side, top);
    }

    // A row hanging from the panel's top edge.
    private static void PlaceTop(RectTransform rt, float side, float fromTop, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(side, -fromTop - height);
        rt.offsetMax = new Vector2(-side, -fromTop);
    }

    private static GameObject AddButton(RectTransform parent, string label, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect(label, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        var face = rt.gameObject.AddComponent<CinematicPanel>();
        face.ornament = 0f;
        face.raycastTarget = true;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(onClick);
        TextMeshProUGUI text = NewText(rt, "Label", fontSize, TextAlignmentOptions.Center);
        UiTheme.StyleTitle(text, fontSize);
        Stretch(text.rectTransform);
        text.text = label;
        return rt.gameObject;
    }
}

/// <summary>Shows the full card preview while a deck-viewer card is hovered.</summary>
public class DeckViewerCell : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public CardData card;
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (card == null) return;
        CardCenterPreview.Instance?.ShowPreview(card, hoverDriven: true);
        Sounds.Instance?.PlayUiHover();
    }
    public void OnPointerExit(PointerEventData eventData) => CardCenterPreview.Instance?.HidePreview();
}
