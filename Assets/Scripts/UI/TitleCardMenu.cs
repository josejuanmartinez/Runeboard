using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Title screen menu. The painted background ("Start intro") already carries the logo, the
/// tagline and a gold-framed column on the left, so the menu lives inside that column as a stack
/// of illustrated plaques. StartScreenController fills it with pages (Continue · New Campaign ·
/// Quit, then one plaque per campaign + Back) — the column swaps in place, no second panel.
/// Replaces the authored menu bar, which is hidden. Plaque art lives in Resources/UI/MenuCards.
/// </summary>
public class TitleCardMenu : MonoBehaviour
{
    // The framed column of the background image, in normalized screen space (the background is
    // stretched full-screen): below the baked logo/tagline, inside the gold border.
    private static readonly Vector2 ColumnMin = new(0.034f, 0.075f);
    private static readonly Vector2 ColumnMax = new(0.316f, 0.700f);
    private const float PlaqueHeight = 196f;
    private const float PlaqueSpacing = 26f;
    private const float ArtWidth = 150f;

    private const float BackLinkHeight = 72f;

    private RectTransform column;
    private CanvasGroup columnGroup;
    private UnityAction onBack;

    /// <summary>Builds the empty menu column; fill it with Clear() + AddPlaque()/AddBackLink().</summary>
    public static TitleCardMenu Build(RectTransform screenRoot)
    {
        Transform existing = screenRoot.Find("TitleCardMenu");
        if (existing != null) return existing.GetComponent<TitleCardMenu>();

        var go = new GameObject("TitleCardMenu", typeof(RectTransform));
        var root = go.GetComponent<RectTransform>();
        root.SetParent(screenRoot, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var menu = go.AddComponent<TitleCardMenu>();

        var columnGo = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
        var column = columnGo.GetComponent<RectTransform>();
        column.SetParent(root, false);
        column.anchorMin = ColumnMin;
        column.anchorMax = ColumnMax;
        column.offsetMin = column.offsetMax = Vector2.zero;
        var vlg = columnGo.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = PlaqueSpacing;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        menu.column = column;
        menu.columnGroup = columnGo.AddComponent<CanvasGroup>();
        return menu;
    }

    /// <summary>Empties the column for a new page, which then fades in. Esc runs onBack, if given.</summary>
    public void Clear(UnityAction back = null)
    {
        onBack = back;
        for (int i = column.childCount - 1; i >= 0; i--)
        {
            GameObject child = column.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        columnGroup.alpha = 0f;
    }

    private void Update()
    {
        if (columnGroup != null && columnGroup.alpha < 1f)
            columnGroup.alpha = Mathf.MoveTowards(columnGroup.alpha, 1f, Time.unscaledDeltaTime * 5f);
        if (onBack != null && Input.GetKeyDown(KeyCode.Escape)) onBack.Invoke();
    }

    /// <summary>Illustrated plaque; returns the art Image so art that loads later can be set.</summary>
    public Image AddPlaque(Sprite art, string title, string subtitle, UnityAction onClick) =>
        CreatePlaque(column, art, title, subtitle, onClick);

    /// <summary>Plaque art shipped in Resources/UI/MenuCards.</summary>
    public static Sprite MenuArt(string name) => Resources.Load<Sprite>($"UI/MenuCards/{name}");

    /// <summary>Slim text-only plaque ("‹ Back") under the illustrated ones.</summary>
    public void AddBackLink(string label, UnityAction onClick)
    {
        var slot = new GameObject(label, typeof(RectTransform));
        var slotRt = slot.GetComponent<RectTransform>();
        slotRt.SetParent(column, false);
        slotRt.sizeDelta = new Vector2(0f, BackLinkHeight);

        var plaqueGo = new GameObject("Plaque", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button), typeof(TitleCardHover));
        var plaque = plaqueGo.GetComponent<RectTransform>();
        plaque.SetParent(slotRt, false);
        plaque.anchorMin = Vector2.zero;
        plaque.anchorMax = Vector2.one;
        plaque.offsetMin = plaque.offsetMax = Vector2.zero;
        var face = plaqueGo.GetComponent<CinematicPanel>();
        face.fillTop = new Color(0.07f, 0.065f, 0.05f, 0.88f);
        face.fillBottom = new Color(0.025f, 0.024f, 0.02f, 0.94f);
        face.inset = 5f;
        face.ornament = 0f;
        face.line = TitleCardHover.RestLine;
        var button = plaqueGo.GetComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { Sounds.Instance?.PlayUiClick(); onClick?.Invoke(); });

        var text = Text(plaque, "Title", label, 32f, true);
        var trt = text.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(28f, 0f);
        trt.offsetMax = new Vector2(-24f, 0f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        plaqueGo.GetComponent<TitleCardHover>().Init(text);
    }

    private static Image CreatePlaque(RectTransform column, Sprite sprite, string title, string subtitle, UnityAction onClick)
    {
        // The slot holds the layout position; the plaque inside it slides on hover so the
        // layout never moves under the pointer.
        var slot = new GameObject(title, typeof(RectTransform));
        var slotRt = slot.GetComponent<RectTransform>();
        slotRt.SetParent(column, false);
        slotRt.sizeDelta = new Vector2(0f, PlaqueHeight);

        var plaqueGo = new GameObject("Plaque", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button), typeof(TitleCardHover));
        var plaque = plaqueGo.GetComponent<RectTransform>();
        plaque.SetParent(slotRt, false);
        plaque.anchorMin = Vector2.zero;
        plaque.anchorMax = Vector2.one;
        plaque.offsetMin = plaque.offsetMax = Vector2.zero;
        var face = plaqueGo.GetComponent<CinematicPanel>();
        face.fillTop = new Color(0.07f, 0.065f, 0.05f, 0.88f);
        face.fillBottom = new Color(0.025f, 0.024f, 0.02f, 0.94f);
        face.inset = 5f;
        face.ornament = 0f;
        face.line = TitleCardHover.RestLine;
        var button = plaqueGo.GetComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { Sounds.Instance?.PlayUiClick(); onClick?.Invoke(); });

        // Art: the portrait card painting, centre-cropped into a tall window on the left.
        var windowGo = new GameObject("ArtWindow", typeof(RectTransform), typeof(RectMask2D));
        var window = windowGo.GetComponent<RectTransform>();
        window.SetParent(plaque, false);
        window.anchorMin = new Vector2(0f, 0f);
        window.anchorMax = new Vector2(0f, 1f);
        window.pivot = new Vector2(0f, 0.5f);
        window.offsetMin = new Vector2(16f, 16f);
        window.offsetMax = new Vector2(16f + ArtWidth, -16f);

        var artGo = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var artRt = artGo.GetComponent<RectTransform>();
        artRt.SetParent(window, false);
        artRt.anchorMin = artRt.anchorMax = artRt.pivot = new Vector2(0.5f, 0.5f);
        var img = artGo.GetComponent<Image>();
        img.raycastTarget = false;
        SetArt(img, sprite);

        var frameGo = new GameObject("ArtFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel));
        var frame = frameGo.GetComponent<RectTransform>();
        frame.SetParent(plaque, false);
        frame.anchorMin = window.anchorMin;
        frame.anchorMax = window.anchorMax;
        frame.pivot = window.pivot;
        frame.offsetMin = window.offsetMin;
        frame.offsetMax = window.offsetMax;
        var framePanel = frameGo.GetComponent<CinematicPanel>();
        framePanel.fill = false;
        framePanel.inset = 0f;
        framePanel.ornament = 0f;
        framePanel.line = new Color(0.80f, 0.64f, 0.36f, 0.55f);
        framePanel.raycastTarget = false;

        float textLeft = 16f + ArtWidth + 26f;
        var titleText = Text(plaque, "Title", title, 44f, true);
        var trt = titleText.rectTransform;
        trt.anchorMin = new Vector2(0f, 0.5f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(textLeft, 2f);
        trt.offsetMax = new Vector2(-24f, -18f);
        titleText.alignment = TextAlignmentOptions.BottomLeft;

        var subText = Text(plaque, "Subtitle", subtitle, 22f, false);
        var srt = subText.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 0.5f);
        srt.offsetMin = new Vector2(textLeft, 18f);
        srt.offsetMax = new Vector2(-24f, -8f);
        subText.alignment = TextAlignmentOptions.TopLeft;
        subText.color = UiTheme.Muted;

        plaqueGo.GetComponent<TitleCardHover>().Init(titleText);
        return img;
    }

    /// <summary>Cover-fits art into its plaque window (centre crop, no gaps).</summary>
    public static void SetArt(Image img, Sprite sprite)
    {
        if (img == null) return;
        img.sprite = sprite;
        img.color = sprite != null ? Color.white : new Color(0.1f, 0.1f, 0.12f, 1f);
        Vector2 window = new(ArtWidth, PlaqueHeight - 32f);
        float aspect = sprite != null ? sprite.rect.width / sprite.rect.height : window.x / window.y;
        img.rectTransform.sizeDelta = aspect > window.x / window.y
            ? new Vector2(window.y * aspect, window.y)
            : new Vector2(window.x, window.x / aspect);
    }

    private static TextMeshProUGUI Text(RectTransform parent, string name, string value, float size, bool display)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (display) UiTheme.StyleTitle(t, size); else UiTheme.StyleBody(t, size, UiTheme.Text);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        t.text = value;
        return t;
    }
}

/// <summary>Slide a title plaque right and brighten its hairline on hover.</summary>
public class TitleCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static readonly Color RestLine = new(0.80f, 0.64f, 0.36f, 0.55f);
    private static readonly Color HoverLine = new(1f, 0.85f, 0.5f, 1f);
    private TMP_Text title;
    private float lift, target;

    public void Init(TMP_Text titleText) => title = titleText;

    public void OnPointerEnter(PointerEventData e)
    {
        target = 1f;
        Sounds.Instance?.PlayUiHover();
    }

    public void OnPointerExit(PointerEventData e) => target = 0f;

    private void OnDisable()
    {
        target = lift = 0f;
        Apply();
    }

    private void Update()
    {
        if (Mathf.Approximately(lift, target)) return;
        lift = Mathf.MoveTowards(lift, target, Time.unscaledDeltaTime * 7f);
        Apply();
    }

    private void Apply()
    {
        var rt = (RectTransform)transform;
        float e = lift * lift * (3f - 2f * lift);
        rt.offsetMin = new Vector2(14f * e, 0f);
        rt.offsetMax = new Vector2(14f * e, 0f);
        if (TryGetComponent(out CinematicPanel p))
        {
            p.line = Color.Lerp(RestLine, HoverLine, e);
            p.fillTop = Color.Lerp(new Color(0.07f, 0.065f, 0.05f, 0.88f), new Color(0.16f, 0.13f, 0.08f, 0.92f), e);
            p.SetVerticesDirty();
        }
        if (title != null) title.color = Color.Lerp(UiTheme.Gold, new Color(1f, 0.90f, 0.62f, 1f), e);
    }
}
