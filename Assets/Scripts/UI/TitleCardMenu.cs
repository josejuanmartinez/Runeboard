using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Title screen menu, laid over the tabletop (TitleDiorama): the gold logo above the board, a few
/// of the game's cards lying in the table's corners, and the menu itself as a hand of cards fanned
/// along the bottom — the same faces as the in-game hand (HandTray): painted art, a dark name plate,
/// gold eyebrow and display-face title. Hovering lifts a card out of the fan and reads its line
/// above the hand. StartScreenController fills the hand with pages (Continue · New Campaign · Quit,
/// then one card per campaign + Back); each page deals in from below. Menu art lives in
/// Resources/UI/MenuCards, the logo in Resources/UI/RuneboardLogo.
/// </summary>
public class TitleCardMenu : MonoBehaviour
{
    // Canvas units (the start screen's scaler is 2560×1440).
    private const float CardWidth = 252f;
    private const float CardHeight = 354f;
    private const float CardSpacing = 236f;
    private const float FanDegrees = 5f;    // rotation step between neighbours
    private const float FanDrop = 14f;      // how much lower each step out from the middle sits
    private const float RestBottom = 46f;
    private const float HoverLift = 46f;
    private const float HoverScale = 1.1f;
    private const float HoverSpread = 34f;  // neighbours make room

    private RectTransform hand;
    private TextMeshProUGUI caption;
    private CanvasGroup tableLayer;          // logo, vignette, corner cards: only over the diorama
    private readonly List<TitleMenuCard> cards = new();
    private readonly List<(Image img, string card)> pendingArt = new();
    private TitleMenuCard hovered;
    private UnityAction onBack;
    private float dealtAt;
    private float tableTarget;

    /// <summary>Builds the empty hand and the table dressing; fill it with Clear() + AddCard()/AddBackCard().</summary>
    public static TitleCardMenu Build(RectTransform screenRoot)
    {
        Transform existing = screenRoot.Find("TitleCardMenu");
        if (existing != null) return existing.GetComponent<TitleCardMenu>();

        var go = new GameObject("TitleCardMenu", typeof(RectTransform));
        var root = go.GetComponent<RectTransform>();
        root.SetParent(screenRoot, false);
        Stretch(root);
        var menu = go.AddComponent<TitleCardMenu>();

        var tableGo = new GameObject("Table", typeof(RectTransform), typeof(CanvasGroup));
        var table = tableGo.GetComponent<RectTransform>();
        table.SetParent(root, false);
        Stretch(table);
        menu.tableLayer = tableGo.GetComponent<CanvasGroup>();
        menu.tableLayer.alpha = 0f;
        menu.tableLayer.blocksRaycasts = false;
        menu.BuildTable(table);

        var captionText = Text(root, "Caption", "", 30f, false);
        captionText.alignment = TextAlignmentOptions.Bottom;
        captionText.color = UiTheme.Text;
        var crt = captionText.rectTransform;
        crt.anchorMin = new Vector2(0.2f, 0f);
        crt.anchorMax = new Vector2(0.8f, 0f);
        crt.pivot = new Vector2(0.5f, 0f);
        crt.sizeDelta = new Vector2(0f, 44f);
        crt.anchoredPosition = new Vector2(0f, RestBottom + HoverLift + CardHeight * HoverScale + 18f);
        menu.caption = captionText;

        var handGo = new GameObject("Hand", typeof(RectTransform));
        menu.hand = handGo.GetComponent<RectTransform>();
        menu.hand.SetParent(root, false);
        menu.hand.anchorMin = menu.hand.anchorMax = new Vector2(0.5f, 0f);
        menu.hand.pivot = new Vector2(0.5f, 0f);
        menu.hand.sizeDelta = Vector2.zero;
        return menu;
    }

    /// <summary>Plaque art shipped in Resources/UI/MenuCards.</summary>
    public static Sprite MenuArt(string name) => Resources.Load<Sprite>($"UI/MenuCards/{name}");

    /// <summary>Shows the logo, vignette and corner cards — once the tabletop is behind them
    /// (the painted backdrop it replaces carries its own logo).</summary>
    public void ShowTable(bool show) => tableTarget = show ? 1f : 0f;

    /// <summary>Empties the hand for a new page, which then deals in. Esc runs back, if given.</summary>
    public void Clear(UnityAction back = null)
    {
        onBack = back;
        foreach (TitleMenuCard card in cards)
            if (card != null) Destroy(card.gameObject);
        cards.Clear();
        hovered = null;
        caption.text = "";
        dealtAt = Time.unscaledTime;
    }

    /// <summary>One menu card; returns the art Image so art that loads later can be set.</summary>
    public Image AddCard(Sprite art, string eyebrow, string title, string line, UnityAction onClick)
    {
        TitleMenuCard card = CreateCard(art, eyebrow, title, line, onClick);
        return card.Art;
    }

    /// <summary>A plain card at the end of the hand that returns to the previous page.</summary>
    public void AddBackCard(string title, UnityAction onClick) => CreateCard(null, "", title, "Return to the title", onClick);

    // ── Layout ──────────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (onBack != null && Input.GetKeyDown(KeyCode.Escape)) onBack.Invoke();
        if (tableLayer != null && !Mathf.Approximately(tableLayer.alpha, tableTarget))
            tableLayer.alpha = Mathf.MoveTowards(tableLayer.alpha, tableTarget, Time.unscaledDeltaTime * 0.8f);
        FillPendingArt();
        Layout();
    }

    private void Layout()
    {
        int count = cards.Count;
        if (count == 0) return;
        int hoveredIndex = hovered != null ? cards.IndexOf(hovered) : -1;
        float middle = (count - 1) * 0.5f;
        float ease = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f);
        for (int i = 0; i < count; i++)
        {
            TitleMenuCard card = cards[i];
            float step = i - middle;
            float x = step * CardSpacing;
            float y = RestBottom - FanDrop * step * step;
            float angle = -step * FanDegrees;
            float scale = 1f;
            if (hoveredIndex >= 0)
            {
                if (i == hoveredIndex) { y = RestBottom + HoverLift; angle = 0f; scale = HoverScale; }
                else x += Mathf.Sign(i - hoveredIndex) * HoverSpread;
            }
            // Dealt from below the screen, one after another.
            float deal = Mathf.Clamp01((Time.unscaledTime - dealtAt - i * 0.07f) / 0.45f);
            deal = 1f - (1f - deal) * (1f - deal) * (1f - deal);
            y -= (1f - deal) * (CardHeight + 140f);
            angle += (1f - deal) * -step * 8f;

            RectTransform rt = card.Rect;
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(x, y), deal < 1f ? 1f : ease);
            float z = Mathf.LerpAngle(rt.localEulerAngles.z, angle, deal < 1f ? 1f : ease);
            rt.localRotation = Quaternion.Euler(0f, 0f, z);
            rt.localScale = Vector3.one * Mathf.Lerp(rt.localScale.x, scale, ease);
            card.SetLit(i == hoveredIndex);
        }
    }

    internal void SetHovered(TitleMenuCard card, bool enter)
    {
        if (enter)
        {
            if (hovered == card) return;
            hovered = card;
            card.transform.SetAsLastSibling();
            caption.text = card.Line;
            Sounds.Instance?.PlayUiHover();
        }
        else if (hovered == card)
        {
            hovered = null;
            caption.text = "";
            // Back to its place in the fan's overlap order.
            card.transform.SetSiblingIndex(cards.IndexOf(card));
        }
    }

    // ── Cards ───────────────────────────────────────────────────────────────────────────────

    private TitleMenuCard CreateCard(Sprite art, string eyebrow, string title, string line, UnityAction onClick)
    {
        RectTransform card = CardFace(hand, title, art, eyebrow, title, out Image artImage, out CinematicPanel face, out TextMeshProUGUI name);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f);
        card.anchoredPosition = new Vector2(0f, -CardHeight - 200f);
        face.raycastTarget = true;
        if (art == null) name.text = $"<size=160%>‹</size>\n{title}";

        var button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { Sounds.Instance?.PlayUiClick(); onClick?.Invoke(); });

        var handler = card.gameObject.AddComponent<TitleMenuCard>();
        handler.Init(this, card, artImage, face, name, line);
        face.gameObject.AddComponent<TitleMenuCardHit>().Card = handler;
        cards.Add(handler);
        return handler;
    }

    // The in-game hand card's face (HandTray.CreateSlot), at the title's size. Pivot at the
    // bottom centre, so a fanned card turns about its foot.
    private static RectTransform CardFace(RectTransform parent, string objectName, Sprite art, string eyebrow, string title,
        out Image artImage, out CinematicPanel face, out TextMeshProUGUI name)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        var card = go.GetComponent<RectTransform>();
        card.SetParent(parent, false);
        card.pivot = new Vector2(0.5f, 0f);
        card.sizeDelta = new Vector2(CardWidth, CardHeight);

        var shadow = Graphic<Image>(card, "Shadow");
        shadow.sprite = HudSprites.Circle;
        shadow.color = new Color(0f, 0f, 0f, 0.6f);
        Stretch(shadow.rectTransform);
        shadow.rectTransform.offsetMin = new Vector2(-26f, -24f);
        shadow.rectTransform.offsetMax = new Vector2(26f, 6f);

        face = Graphic<CinematicPanel>(card, "Face");
        Stretch(face.rectTransform);
        face.fillTop = new Color(0.10f, 0.09f, 0.075f, 1f);
        face.fillBottom = new Color(0.04f, 0.037f, 0.032f, 1f);
        face.inset = 4f;
        face.ornament = 0f;
        face.lineWidth = 1.8f;
        face.line = TitleMenuCard.RestLine;
        face.raycastTarget = false;

        // Stencil Mask, not RectMask2D: the art window has to turn with the fanned card.
        var windowGo = new GameObject("ArtWindow", typeof(RectTransform), typeof(Image), typeof(Mask));
        windowGo.GetComponent<Image>().raycastTarget = false;
        windowGo.GetComponent<Mask>().showMaskGraphic = false;
        var window = windowGo.GetComponent<RectTransform>();
        window.SetParent(card, false);
        Stretch(window);
        window.offsetMin = new Vector2(9f, 9f);
        window.offsetMax = new Vector2(-9f, -9f);

        artImage = Graphic<Image>(window, "Art");
        SetArt(artImage, art);

        var plate = Graphic<Image>(window, "Plate");
        plate.sprite = HudSprites.FadeUp;
        plate.color = new Color(1f, 1f, 1f, 0.97f);
        var pr = plate.rectTransform;
        pr.anchorMin = new Vector2(0f, 0f);
        pr.anchorMax = new Vector2(1f, 0f);
        pr.pivot = new Vector2(0.5f, 0f);
        pr.sizeDelta = new Vector2(0f, art != null ? 150f : CardHeight);

        TextMeshProUGUI type = Text(card, "Type", $"<cspace=0.14em>{eyebrow.ToUpperInvariant()}</cspace>", 14f, false);
        type.color = UiTheme.Gold;
        type.textWrappingMode = TextWrappingModes.NoWrap;
        type.alignment = TextAlignmentOptions.Bottom;
        var tr = type.rectTransform;
        tr.anchorMin = new Vector2(0f, 0f);
        tr.anchorMax = new Vector2(1f, 0f);
        tr.pivot = new Vector2(0.5f, 0f);
        tr.offsetMin = new Vector2(14f, 74f);
        tr.offsetMax = new Vector2(-14f, 94f);

        name = Text(card, "Name", title, 30f, true);
        name.color = UiTheme.Text;
        name.enableAutoSizing = true;
        name.fontSizeMin = 18f;
        name.fontSizeMax = 30f;
        name.lineSpacing = -14f;
        name.alignment = art != null ? TextAlignmentOptions.Top : TextAlignmentOptions.Center;
        var nr = name.rectTransform;
        if (art != null)
        {
            nr.anchorMin = new Vector2(0f, 0f);
            nr.anchorMax = new Vector2(1f, 0f);
            nr.pivot = new Vector2(0.5f, 0f);
            nr.offsetMin = new Vector2(14f, 14f);
            nr.offsetMax = new Vector2(-14f, 72f);
        }
        else
        {
            Stretch(nr);
            nr.offsetMin = new Vector2(16f, 16f);
            nr.offsetMax = new Vector2(-16f, -16f);
        }
        return card;
    }

    /// <summary>Cover-fits art into a card's window (centre crop, favouring the top, no gaps).</summary>
    public static void SetArt(Image img, Sprite sprite)
    {
        if (img == null) return;
        img.sprite = sprite;
        img.color = sprite != null ? Color.white : new Color(0.12f, 0.11f, 0.10f, 1f);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        Vector2 window = new(CardWidth - 18f, CardHeight - 18f);
        float aspect = sprite != null ? sprite.rect.width / sprite.rect.height : window.x / window.y;
        rt.sizeDelta = aspect > window.x / window.y
            ? new Vector2(window.y * aspect, window.y)
            : new Vector2(window.x, window.x / aspect);
        rt.anchoredPosition = new Vector2(0f, Mathf.Min(14f, (rt.sizeDelta.y - window.y) * 0.5f));
    }

    // ── Table dressing ──────────────────────────────────────────────────────────────────────

    // Cards dropped on the table in the corners, with the game's own art. Leaning back as if they
    // lay flat (squashed along their length), some running off the screen edge.
    private static readonly (string card, Vector2 anchor, Vector2 at, float angle)[] CornerCards =
    {
        ("Gandalf", new Vector2(0f, 0f), new Vector2(150f, 70f), 14f),
        ("Galadriel", new Vector2(0f, 0f), new Vector2(330f, 30f), 4f),
        ("Aragorn", new Vector2(0f, 0f), new Vector2(500f, -40f), -9f),
        ("Sauron", new Vector2(1f, 0f), new Vector2(-170f, 60f), -13f),
        ("Saruman", new Vector2(1f, 0f), new Vector2(-350f, -10f), -2f),
    };

    private void BuildTable(RectTransform table)
    {
        // The room's dark corners, and a deeper shade along the bottom where the hand sits.
        var vignette = Graphic<RawImage>(table, "Vignette");
        vignette.texture = VignetteTexture();
        Stretch(vignette.rectTransform);
        var shade = Graphic<Image>(table, "HandShade");
        shade.sprite = HudSprites.FadeUp;
        shade.color = new Color(0f, 0f, 0f, 0.75f);
        var sr = shade.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f);
        sr.anchorMax = new Vector2(1f, 0f);
        sr.pivot = new Vector2(0.5f, 0f);
        sr.sizeDelta = new Vector2(0f, 420f);
        var top = Graphic<Image>(table, "LogoShade");
        top.sprite = HudSprites.FadeDown;
        top.color = new Color(0f, 0f, 0f, 0.7f);
        var tr = top.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(0f, 420f);

        foreach ((string card, Vector2 anchor, Vector2 at, float angle) in CornerCards)
        {
            RectTransform face = CardFace(table, card, null, "", "", out Image art, out _, out TextMeshProUGUI name);
            name.text = "";
            face.Find("ArtWindow/Plate").gameObject.SetActive(false);
            face.anchorMin = face.anchorMax = anchor;
            face.pivot = new Vector2(0.5f, 0.5f);
            face.anchoredPosition = at;
            face.localRotation = Quaternion.Euler(0f, 0f, angle);
            face.localScale = new Vector3(0.95f, 0.8f, 1f);
            pendingArt.Add((art, card));
        }

        Sprite logoSprite = Resources.Load<Sprite>("UI/RuneboardLogo");
        if (logoSprite != null)
        {
            var logo = Graphic<Image>(table, "Logo");
            logo.sprite = logoSprite;
            logo.preserveAspect = true;
            var lr = logo.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 1f);
            lr.pivot = new Vector2(0.5f, 1f);
            lr.sizeDelta = new Vector2(900f, 900f * logoSprite.rect.height / logoSprite.rect.width);
            lr.anchoredPosition = new Vector2(0f, 6f);

            var tagline = Text(table, "Tagline", "A tradeable card & hex strategy engine", 30f, true);
            tagline.color = new Color(0.93f, 0.86f, 0.70f, 0.92f);
            tagline.alignment = TextAlignmentOptions.Top;
            var gr = tagline.rectTransform;
            gr.anchorMin = gr.anchorMax = new Vector2(0.5f, 1f);
            gr.pivot = new Vector2(0.5f, 1f);
            gr.sizeDelta = new Vector2(1200f, 50f);
            gr.anchoredPosition = new Vector2(0f, -lr.sizeDelta.y + 6f);
        }
    }

    // Card art arrives through Addressables; on the first screen it is often not loaded yet.
    private void FillPendingArt()
    {
        if (pendingArt.Count == 0 || Time.frameCount % 15 != 0) return;
        for (int i = pendingArt.Count - 1; i >= 0; i--)
        {
            (Image img, string card) = pendingArt[i];
            if (img == null) { pendingArt.RemoveAt(i); continue; }
            Sprite art = RetroLOTR.Scenarios.CampaignSelectionManager.FindRepresentativeArt(card);
            if (art == null) continue;
            SetArt(img, art);
            pendingArt.RemoveAt(i);
        }
    }

    /// <summary>Queues art that loads later for a card already on screen.</summary>
    public void FillArtWhenLoaded(Image img, string card) => pendingArt.Add((img, card));

    private static Texture2D vignetteTexture;

    private static Texture2D VignetteTexture()
    {
        if (vignetteTexture != null) return vignetteTexture;
        const int size = 128;
        vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "TitleVignette", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Elliptical, so a 16:9 screen darkens evenly toward every edge.
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Sqrt(2f);
                float a = Mathf.SmoothStep(0.3f, 0.95f, d) * 0.92f;
                pixels[y * size + x] = new Color32(6, 4, 2, (byte)(a * 255f));
            }
        vignetteTexture.SetPixels32(pixels);
        vignetteTexture.Apply();
        return vignetteTexture;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static T Graphic<T>(RectTransform parent, string name) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var g = go.GetComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    private static TextMeshProUGUI Text(RectTransform parent, string name, string value, float size, bool display)
    {
        var t = Graphic<TextMeshProUGUI>(parent, name);
        if (display) UiTheme.StyleTitle(t, size); else UiTheme.StyleBody(t, size, UiTheme.Text);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.text = value;
        return t;
    }
}

/// <summary>One card in the title hand: its line for the caption, and a brighter frame while lifted.</summary>
public class TitleMenuCard : MonoBehaviour
{
    public static readonly Color RestLine = new(0.80f, 0.64f, 0.36f, 0.6f);
    private static readonly Color LitLine = new(1f, 0.86f, 0.52f, 1f);

    public RectTransform Rect { get; private set; }
    public Image Art { get; private set; }
    public string Line { get; private set; }
    private TitleCardMenu menu;
    private CinematicPanel face;
    private TextMeshProUGUI title;
    private float lit, litTarget;

    public void Init(TitleCardMenu owner, RectTransform rect, Image art, CinematicPanel facePanel, TextMeshProUGUI titleText, string line)
    {
        menu = owner;
        Rect = rect;
        Art = art;
        face = facePanel;
        title = titleText;
        Line = line;
    }

    internal void Hover(bool enter) => menu.SetHovered(this, enter);

    public void SetLit(bool on)
    {
        litTarget = on ? 1f : 0f;
        if (Mathf.Approximately(lit, litTarget)) return;
        lit = Mathf.MoveTowards(lit, litTarget, Time.unscaledDeltaTime * 6f);
        face.line = Color.Lerp(RestLine, LitLine, lit);
        face.SetVerticesDirty();
        title.color = Color.Lerp(UiTheme.Text, new Color(1f, 0.90f, 0.62f, 1f), lit);
    }

    private void OnDisable()
    {
        if (menu != null) menu.SetHovered(this, false);
    }
}

/// <summary>Forwards pointer enter/exit on a title card's face to its card.</summary>
public class TitleMenuCardHit : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TitleMenuCard Card;
    public void OnPointerEnter(PointerEventData e) => Card?.Hover(true);
    public void OnPointerExit(PointerEventData e) => Card?.Hover(false);
}
