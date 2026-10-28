using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space city banners, one per labelled hex, pinned under the hex and redrawn crisp at any
/// zoom — built from the game's own UI art (pieces cut from Art/UI/bokshi_mockup.png into
/// Resources/UI/CityBanner): dark weathered ink, fine bronze filigree, torn edges.
///   • a spiked bronze medallion holding the settlement's own painting (its PC art from
///     Art/Hexes/PCs, cropped to the medallion's round window) inside a ring of the owner's true
///     nation colour, with the settlement size on a small badge; capitals carry the compass-star
///     ornament above it. Hovering the medallion shows the PC's card (and its land card) in the
///     CardCenterPreview dock — the only place the PC card preview comes from
///   • a torn ink name plate with filigree lines (9-sliced to the name), the name in parchment-gold
///     (BoardLookSettings.bannerFont) — brighter gold for objective targets — plus port / hidden marks
///   • the fortification level on a filigree shield at the plate's end, when there is one
///   • under the plate, a thin bronze-edged loyalty bar (red → amber → green) that pulses when the
///     place is close to revolt
///   • after the name, behind a bronze rule, everything else on the hex: chips for the armies and
///     characters present, then a bronze-ringed medallion for each marker on the hex (an artifact,
///     a pending event, an underground way — Hex.CollectHudMarkers), whose tooltip shows in a
///     matching plate on hover (these and the city medallion are the canvas's only raycast targets)
/// Hexes with units or markers but no settlement get them alone, on a small strip of the same ink
/// (the unitsPlate). Hex owns the content
/// (RefreshHexLabel fills a PlateData) and calls Show/Hide; this class only builds, places and
/// draws. Banners sit on their own overlay canvas below the HUD; apart from the markers and the
/// city medallion nothing takes raycasts, so clicks and hovers still reach the board underneath.
/// </summary>
public class HexNameplates : MonoBehaviour
{
    public struct PlateData
    {
        public bool hasSettlement;
        public string name;
        public Color nationColor;   // alpha 0 = unowned
        public bool capital;
        public bool objective;
        public int citySize;        // 1 (camp) .. 5 (city)
        public int fortSize;        // 0 none, 1 (tower) .. 5 (citadel)
        public int loyalty;         // 0 .. 100
        public bool port;
        public bool hidden;
        public Sprite painting;     // the city card's illustration (or its tile painting); null = plain field
        public bool paintingIsCard; // a card illustration: framed whole, not cropped round a tile's town
        public string unitsRichText; // inline <sprite> + count groups, may be empty
        public List<Marker> markers;  // artifact, event, underground… shown after the units; may be null
    }

    /// <summary>A small medallion on the info strip (an artifact, a pending event, an underground
    /// way…): the icon in a bronze ring, with its tooltip on hover.</summary>
    public struct Marker
    {
        public Sprite icon;
        public string tooltip;
        public System.Func<string> tooltipSource; // read on hover, so text set later still shows
    }

    private const float ReferenceHeight = 1080f;
    private const float PlateHeight = 36f;
    // The medallion is drawn well larger than the plate is tall, so the painting and the size read.
    private const float MedalScale = 1.9f;
    private const float MedalWidth = 46f * MedalScale, MedalHeight = 62f * MedalScale;  // medallion.png is 145 × 195
    // The frame's round window is ~30 × 33 (94 × 105 px of the source). The nation ring is an
    // annulus a little larger than the window (the frame hides its outer edge); the painting is a
    // rectangle whose corners stay inside the ring's outer edge and whose edges reach past its hole,
    // so the ring alone crops it round — no stencil mask, so banners still batch.
    private static readonly Vector2 RingSize = new Vector2(33f, 33f) * MedalScale;
    private const float RingHole = 0.88f;                      // hole radius / outer radius: a hairline of the nation colour
    private static readonly Vector2 PaintingSize = new Vector2(31f, 31f) * MedalScale;
    private const float PaintingCropWidth = 0.44f;             // of the PC art's width: close on the town itself
    private const float PaintingCropCentreY = 0.6f;            // up from the art's bottom edge
    private const float SizeBadge = 16f * MedalScale;
    private const float StarWidth = 66f, StarHeight = 35f;    // star.png is 132 × 70
    private const float FortBadge = 44f;
    private const float NameSize = 20f;
    private const float BarHeight = 3f;
    private const int MaxCitySize = 5;
    private static readonly Vector3 AnchorOffset = new(0f, -0.23f, 0f); // under the tile's centre
    private static readonly Color Unowned = new(0.42f, 0.38f, 0.31f, 1f);
    private static readonly Color Parchment = new(0.91f, 0.84f, 0.66f, 1f);
    private static readonly Color ObjectiveGold = new(1f, 0.82f, 0.42f, 1f);
    private static readonly Color Ink = new(0.08f, 0.06f, 0.04f, 1f);
    private static readonly Color Bronze = new(0.55f, 0.42f, 0.24f, 1f);

    private static HexNameplates instance;
    private RectTransform root;
    private Canvas canvas;
    private readonly Dictionary<Hex, Plate> plates = new();

    private static Sprite plateSprite, medalFrameSprite, medalFillSprite, shieldSprite, starSprite, ringSprite;
    private static Sprite nationRingSprite, discSprite; // generated: white annulus and disc, tinted per nation

    private const float MarkerSize = 26f;

    private sealed class MarkerView
    {
        public RectTransform rect;
        public Image icon;
        public MarkerHover hover;
    }

    // Shows a marker's tooltip while the pointer is on it (the banner canvas raycasts only these).
    private sealed class MarkerHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public string text;
        public System.Func<string> source;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) => ShowTooltip((RectTransform)transform, source?.Invoke() ?? text);
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) => HideTooltip();
        private void OnDisable() => HideTooltip();
    }

    // Hovering a city's medallion shows its PC card (+ land card); leaving hides it. Hex owns the
    // preview (delay, visibility checks), as it did when the hex itself was the hover target.
    private sealed class MedalHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Hex hex;
        private bool hovered;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) => Set(true);
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) => Set(false);
        private void OnDisable() => Set(false);
        private void Set(bool on)
        {
            if (hovered == on) return;
            hovered = on;
            if (hex != null) hex.SetPcMedallionHovered(on);
        }
    }

    private sealed class Plate
    {
        public RectTransform rect;
        public RectTransform banner;
        public TextMeshProUGUI name;
        public RectTransform medal;
        public Image medalFill;
        public Image paintingBack;
        public RawImage painting;
        public Image nationRing;
        public Image sizeBadge;
        public TextMeshProUGUI popText;
        public RectTransform star;
        public RectTransform fortBadge;
        public TextMeshProUGUI fortText;
        public RectTransform barBack;
        public Image barFill;
        public RectTransform units;
        public Image unitsPlate;     // the strip's own ink plate: off when the chips sit in the city plate
        public Image divider;        // bronze rule between the name and the chips in the city plate
        public TextMeshProUGUI unitsText;
        public readonly List<MarkerView> markers = new();
        public bool lowLoyalty;
        public float height;
    }

    public static void Show(Hex hex, PlateData data, TMP_SpriteAsset sprites, TMP_FontAsset font)
    {
        if (hex == null) return;
        HexNameplates self = Ensure();
        if (!self.plates.TryGetValue(hex, out Plate plate)) self.plates[hex] = plate = self.CreatePlate(hex);
        self.Apply(plate, data, sprites, font);
        plate.rect.gameObject.SetActive(true);
        self.Place(hex, plate);
    }

    public static void Hide(Hex hex)
    {
        if (instance == null || hex == null) return;
        if (instance.plates.TryGetValue(hex, out Plate plate) && plate.rect != null) plate.rect.gameObject.SetActive(false);
    }

    private static HexNameplates Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("HexNameplates", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        instance = go.AddComponent<HexNameplates>();
        instance.canvas = go.GetComponent<Canvas>();
        instance.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        instance.canvas.sortingOrder = -50; // under every HUD canvas, still above the board
        go.AddComponent<GraphicRaycaster>(); // only the info-strip markers are raycast targets
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
        scaler.matchWidthOrHeight = 1f;
        instance.root = (RectTransform)go.transform;
        return instance;
    }

    // ── Construction ────────────────────────────────────────────────────────────────────────

    private static void LoadSprites()
    {
        if (plateSprite != null) return;
        plateSprite = Resources.Load<Sprite>("UI/CityBanner/plate");
        medalFrameSprite = Resources.Load<Sprite>("UI/CityBanner/medallion_frame");
        medalFillSprite = Resources.Load<Sprite>("UI/CityBanner/medallion_fill");
        shieldSprite = Resources.Load<Sprite>("UI/CityBanner/shield");
        starSprite = Resources.Load<Sprite>("UI/CityBanner/star");
        ringSprite = Resources.Load<Sprite>("UI/CityBanner/ring");
    }

    // The name plate: a rounded capsule of dark ink with a thin bronze edge, the same round
    // language as the medallion, 9-sliced so its round ends keep their shape at any width.
    private static Image PlateImage(RectTransform rt, float height)
    {
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = CapsuleSprite();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = CapsuleSource / height;
        image.raycastTarget = false;
        return image;
    }

    private const int CapsuleSource = 64; // the capsule sprite's height (and its end-cap width x2)
    private static Sprite capsuleSprite;

    private static Sprite CapsuleSprite()
    {
        if (capsuleSprite != null) return capsuleSprite;
        const int H = CapsuleSource, W = CapsuleSource + 2;
        float r = H * 0.5f;
        var texture = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "CityBannerCapsule", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[W * H];
        Color fillTop = new(0.11f, 0.09f, 0.07f, 0.95f), fillBottom = new(0.045f, 0.037f, 0.03f, 0.95f);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            // Distance to the capsule's spine (a short horizontal segment through the middle).
            float cx = Mathf.Clamp(x + 0.5f, r, W - r);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, r));
            float outer = Mathf.Clamp01(r - d);
            float edge = Mathf.Clamp01(1f - Mathf.Abs(d - (r - 2.5f)) / 1.6f); // a bronze line just inside the rim
            Color fill = Color.Lerp(fillBottom, fillTop, y / (float)H);
            Color c = Color.Lerp(fill, BronzeLight, edge * 0.9f);
            c.a = outer * Mathf.Max(fill.a, edge);
            pixels[y * W + x] = c;
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        capsuleSprite = Sprite.Create(texture, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return capsuleSprite;
    }

    private Plate CreatePlate(Hex hex)
    {
        LoadSprites();
        var plate = new Plate();
        plate.rect = NewRect($"CityBanner_{hex.name}", root);
        plate.rect.anchorMin = plate.rect.anchorMax = Vector2.zero;
        plate.rect.pivot = new Vector2(0.5f, 1f);

        // Name plate first, so the medallion and shield sit over its ends.
        plate.banner = NewRect("Plate", plate.rect);
        TopCentre(plate.banner);
        PlateImage(plate.banner, PlateHeight);
        plate.name = NewText("Name", plate.banner, NameSize, FontStyles.Normal); // a pixel face: no faux bold or small caps
        Stretch((RectTransform)plate.name.transform);
        plate.name.characterSpacing = 1f;
        plate.name.color = Parchment;

        // Loyalty: a hairline bronze frame round a thin bar, under the plate.
        plate.barBack = NewRect("Loyalty", plate.rect);
        var back = plate.barBack.gameObject.AddComponent<Image>();
        back.color = Bronze;
        back.raycastTarget = false;
        var inner = NewRect("Back", plate.barBack);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(1f, 1f);
        inner.offsetMax = new Vector2(-1f, -1f);
        var innerImage = inner.gameObject.AddComponent<Image>();
        innerImage.color = new Color(0.06f, 0.05f, 0.04f, 1f);
        innerImage.raycastTarget = false;
        var fillRect = NewRect("Fill", inner);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        plate.barFill = fillRect.gameObject.AddComponent<Image>();
        plate.barFill.raycastTarget = false;

        // The medallion: the nation colour behind the bronze frame, the settlement's painting in its
        // window inside a ring of that colour, and the size on a small badge at its lower left.
        plate.medal = NewRect("Medallion", plate.rect);
        plate.medal.sizeDelta = new Vector2(MedalWidth, MedalHeight);
        plate.medalFill = NewImage("Field", plate.medal, medalFillSprite, Color.white, 0f, Vector2.zero);
        Stretch(plate.medalFill.rectTransform);
        plate.paintingBack = NewImage("PaintingBack", plate.medal, DiscSprite(), Ink, 0f, Vector2.zero);
        plate.paintingBack.rectTransform.sizeDelta = PaintingSize;
        // A round window: the art is masked to a disc.
        RectTransform window = NewRect("Window", plate.medal);
        window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
        window.sizeDelta = PaintingSize;
        var windowImage = window.gameObject.AddComponent<Image>();
        windowImage.sprite = DiscSprite();
        windowImage.raycastTarget = false;
        window.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        RectTransform paintingRect = NewRect("Painting", window);
        paintingRect.anchorMin = paintingRect.anchorMax = new Vector2(0.5f, 0.5f);
        paintingRect.sizeDelta = PaintingSize;
        plate.painting = paintingRect.gameObject.AddComponent<RawImage>();
        plate.painting.raycastTarget = false;
        plate.nationRing = NewImage("NationRing", plate.medal, NationRingSprite(), Color.white, 0f, Vector2.zero);
        plate.nationRing.rectTransform.sizeDelta = RingSize;
        // A bronze ring round the portrait (the old spiked frame stays only as the hover target).
        NewImage("Bronze", plate.medal, BronzeRingSprite(), BronzeLight, RingSize.x + 7f * MedalScale, Vector2.zero);
        Image frame = NewImage("Frame", plate.medal, medalFrameSprite, new Color(1f, 1f, 1f, 0f), 0f, Vector2.zero);
        Stretch(frame.rectTransform);
        // The whole medallion is the hover target for the PC card preview.
        frame.raycastTarget = true;
        frame.gameObject.AddComponent<MedalHover>().hex = hex;
        Vector2 badgeAt = new(-MedalWidth * 0.5f + SizeBadge * 0.5f - 1f, -MedalHeight * 0.5f + SizeBadge * 0.5f + 6f * MedalScale);
        plate.sizeBadge = NewImage("SizeBadge", plate.medal, DiscSprite(), Color.white, SizeBadge, badgeAt);
        NewImage("Ring", plate.sizeBadge.rectTransform, BronzeRingSprite(), BronzeLight, SizeBadge + 3f, Vector2.zero);
        plate.popText = NewText("Size", plate.sizeBadge.rectTransform, 18f * MedalScale, FontStyles.Normal);
        Stretch((RectTransform)plate.popText.transform);
        // Centred on the glyph's own shape: the pixel face's metrics sat the digit off-centre,
        // and a touch above the middle, clear of the badge's bronze rim at the bottom.
        plate.popText.alignment = TextAlignmentOptions.CenterGeoAligned;
        ((RectTransform)plate.popText.transform).offsetMin = new Vector2(0f, 4f);
        ((RectTransform)plate.popText.transform).offsetMax = new Vector2(0f, 4f);

        plate.star = NewRect("Capital", plate.rect);
        plate.star.sizeDelta = new Vector2(StarWidth, StarHeight);
        Stretch(NewImage("Star", plate.star, starSprite, Color.white, 0f, Vector2.zero).rectTransform);

        // The fort level on a round badge at the plate's end, the same disc and bronze ring as the
        // size badge, with the fort mark beside the number.
        plate.fortBadge = NewRect("Fort", plate.rect);
        plate.fortBadge.sizeDelta = new Vector2(FortBadge, FortBadge);
        Stretch(NewImage("Disc", plate.fortBadge, DiscSprite(), Ink, 0f, Vector2.zero).rectTransform);
        NewImage("Ring", plate.fortBadge, BronzeRingSprite(), BronzeLight, FortBadge + 3f, Vector2.zero);
        plate.fortText = NewText("Level", plate.fortBadge, 20f, FontStyles.Normal);
        Stretch((RectTransform)plate.fortText.transform);
        plate.fortText.alignment = TextAlignmentOptions.CenterGeoAligned;
        plate.fortText.color = Parchment;

        // Units present: a small plate of the same ink.
        plate.units = NewRect("Units", plate.rect);
        plate.unitsPlate = PlateImage(plate.units, 24f);
        plate.divider = NewImage("Divider", plate.banner, null, Bronze, 0f, Vector2.zero);
        plate.divider.rectTransform.sizeDelta = new Vector2(1.5f, PlateHeight * 0.5f);
        plate.unitsText = NewText("Text", plate.units, 15f, FontStyles.Normal);
        Stretch((RectTransform)plate.unitsText.transform);
        plate.unitsText.color = Parchment;
        return plate;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static Image NewImage(string name, RectTransform parent, Sprite sprite, Color color, float size, Vector2 offset)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = offset;
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI NewText(string name, RectTransform parent, float size, FontStyles style)
    {
        RectTransform rt = NewRect(name, parent);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.richText = true;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void TopCentre(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
    }

    // ── Content and layout ──────────────────────────────────────────────────────────────────

    private void Apply(Plate plate, PlateData data, TMP_SpriteAsset sprites, TMP_FontAsset font)
    {
        // The banner face (BoardLookSettings.bannerFont) with its own plain material: the hex
        // label's font material is overbright (HDR face colour) and glowed under the bloom.
        TMP_FontAsset bannerFont = BoardLookSettings.Instance.bannerFont != null ? BoardLookSettings.Instance.bannerFont : font;
        foreach (TextMeshProUGUI text in new[] { plate.name, plate.popText, plate.fortText, plate.unitsText })
        {
            if (bannerFont != null && text.font != bannerFont) text.font = bannerFont;
            if (bannerFont != null && text != plate.popText && text.fontSharedMaterial != bannerFont.material) text.fontSharedMaterial = bannerFont.material;
            if (sprites != null) text.spriteAsset = sprites;
        }

        bool settlement = data.hasSettlement;
        plate.banner.gameObject.SetActive(settlement);
        plate.medal.gameObject.SetActive(settlement);
        plate.star.gameObject.SetActive(settlement && data.capital);
        plate.fortBadge.gameObject.SetActive(settlement && data.fortSize > 0);
        plate.barBack.gameObject.SetActive(settlement);
        bool hasUnitText = !string.IsNullOrEmpty(data.unitsRichText);
        int markerCount = data.markers != null ? data.markers.Count : 0;
        bool hasUnits = hasUnitText || markerCount > 0; // unit chips and/or markers
        plate.units.gameObject.SetActive(hasUnits);
        plate.divider.gameObject.SetActive(false);

        // The chips and markers: inside the city plate after the name, or on their own strip.
        plate.unitsText.text = hasUnitText ? data.unitsRichText : string.Empty;
        plate.unitsText.alignment = TextAlignmentOptions.Left;
        float textWidth = hasUnitText ? plate.unitsText.GetPreferredValues(data.unitsRichText).x : 0f;
        float gap = hasUnitText && markerCount > 0 ? 8f : 0f;
        float contentWidth = hasUnits ? textWidth + gap + markerCount * (MarkerSize + 2f) : 0f;
        const float DividerSpace = 16f;

        float y = 0f;
        float width = 0f;
        if (settlement)
        {
            // The nation colour exactly as the nation has it.
            Color nation = data.nationColor.a > 0f ? data.nationColor : Unowned;
            nation.a = 1f;
            // The nation shows as a hairline round the painting; the field and the size badge stay
            // ink and bronze, so the town's painting is what reads.
            plate.medalFill.color = new Color(0f, 0f, 0f, 0f); // no field: the portrait and its ring
            plate.nationRing.color = nation;   // a plain white ring, tinted: the true colour, undarkened
            plate.sizeBadge.color = Ink;
            ApplyPainting(plate, data.painting, data.paintingIsCard);

            // The size: dark ink on light nation colours, parchment on dark ones.
            bool lightField = false; // the badge is ink
            plate.popText.color = lightField ? Ink : Parchment;
            // Ink on light fields needs no outline; parchment on dark ones gets a thin dark edge.
            if (bannerFont != null)
            {
                Material wanted = lightField ? bannerFont.material : NumberMaterial(bannerFont);
                if (plate.popText.fontSharedMaterial != wanted) plate.popText.fontSharedMaterial = wanted;
            }

            string marks = (data.port ? " <size=80%><sprite name=\"port\"></size>" : "") + (data.hidden ? " <size=80%><sprite name=\"hidden\"></size>" : "");
            string nameColor = ColorUtility.ToHtmlStringRGB(data.objective ? ObjectiveGold : Parchment);
            plate.name.text = $"<color=#{nameColor}>{data.name}</color>{marks}";
            float nameWidth = plate.name.GetPreferredValues(plate.name.text).x;

            // The plate runs from under the medallion past the name — and the units and markers on
            // this hex, after a bronze rule — to the fort shield: one widget per city.
            float leftInset = MedalWidth * 0.5f + 10f;
            float rightInset = 18f + (data.fortSize > 0 ? FortBadge * 0.5f : 0f);
            float unitsSpace = 0f; // units and markers go on their own strip under the plate
            float plateWidth = Mathf.Max(120f, nameWidth + leftInset + rightInset + unitsSpace);
            float total = MedalWidth * 0.5f + plateWidth;
            float left = -total * 0.5f;
            float plateTop = -(MedalHeight - PlateHeight) * 0.5f;

            plate.banner.anchorMin = plate.banner.anchorMax = new Vector2(0.5f, 1f);
            plate.banner.pivot = new Vector2(0f, 1f);
            plate.banner.sizeDelta = new Vector2(plateWidth, PlateHeight);
            plate.banner.anchoredPosition = new Vector2(left + MedalWidth * 0.5f, plateTop);
            RectTransform nameRect = (RectTransform)plate.name.transform;
            nameRect.offsetMin = new Vector2(leftInset, 0f);
            nameRect.offsetMax = new Vector2(-(rightInset + unitsSpace), 0f);

            if (hasUnits)
            {
                // The units and markers on their own small ink strip under the name plate, so a city
                // with an army reads as two clean rows instead of one crowded plate.
                const float Padding = 14f;
                plate.unitsPlate.enabled = hasUnitText;
                float padding = hasUnitText ? Padding : 0f;
                plate.units.anchorMin = plate.units.anchorMax = new Vector2(0.5f, 1f);
                plate.units.pivot = new Vector2(0.5f, 1f);
                plate.units.sizeDelta = new Vector2(contentWidth + padding * 2f, markerCount > 0 ? MarkerSize + 2f : 24f);
                plate.units.anchoredPosition = new Vector2(left + MedalWidth * 0.5f + plateWidth * 0.5f + MedalWidth * 0.25f, plateTop - PlateHeight - BarHeight - 6f);
                LayoutStripContent(plate, data, textWidth, gap, markerCount, padding);
            }

            plate.popText.text = Mathf.Clamp(data.citySize, 1, MaxCitySize).ToString();
            PlaceAt(plate.medal, left + MedalWidth * 0.5f, -MedalHeight * 0.5f);
            if (data.capital) PlaceAt(plate.star, left + MedalWidth * 0.5f, StarHeight * 0.32f);

            if (data.fortSize > 0)
            {
                plate.fortText.text = $"<size=90%><sprite name=\"fort\"></size>{data.fortSize}";
                PlaceAt(plate.fortBadge, left + MedalWidth * 0.5f + plateWidth - 4f, plateTop - PlateHeight * 0.5f);
            }

            // Loyalty under the plate, from the medallion to before the ragged end.
            float barWidth = plateWidth - MedalWidth * 0.5f - 24f;
            plate.barBack.anchorMin = plate.barBack.anchorMax = new Vector2(0.5f, 1f);
            plate.barBack.pivot = new Vector2(0f, 1f);
            plate.barBack.sizeDelta = new Vector2(barWidth, BarHeight + 2f);
            plate.barBack.anchoredPosition = new Vector2(left + MedalWidth + 2f, plateTop - PlateHeight + 3f);
            float loyalty = Mathf.Clamp01(data.loyalty / 100f);
            RectTransform fill = plate.barFill.rectTransform;
            fill.anchorMax = new Vector2(loyalty, 1f); // the bar fills from the left
            plate.barFill.color = LoyaltyColor(loyalty);
            plate.lowLoyalty = data.loyalty < 30;

            y = MedalHeight;
            width = total;
        }

        if (hasUnits && !settlement)
        {
            // No settlement: the chips and markers on their own small ink strip under the hex.
            const float Padding = 17f;
            // Markers alone (an artifact, an event, an underground way) float as their round icons;
            // the ink strip only backs unit chips.
            plate.unitsPlate.enabled = hasUnitText;
            plate.units.anchorMin = plate.units.anchorMax = new Vector2(0.5f, 1f);
            plate.units.pivot = new Vector2(0.5f, 1f);
            float padding = hasUnitText ? Padding : 0f;
            plate.units.sizeDelta = new Vector2(contentWidth + padding * 2f, markerCount > 0 ? MarkerSize + 2f : 24f);
            plate.units.anchoredPosition = new Vector2(0f, -y);
            LayoutStripContent(plate, data, textWidth, gap, markerCount, padding);
            y += plate.units.sizeDelta.y + 2f;
            width = Mathf.Max(width, plate.units.sizeDelta.x);
        }

        plate.rect.sizeDelta = new Vector2(width, y);
        plate.height = y;
    }

    // The settlement's painting in the medallion window: a crop round the town, centred on the hex
    // tile's top face, read straight from the sprite's texture (atlas-aware) by a RawImage.
    private static void ApplyPainting(Plate plate, Sprite sprite, bool card)
    {
        bool show = sprite != null && sprite.texture != null;
        plate.painting.enabled = show;
        plate.paintingBack.enabled = show;
        if (!show) { plate.painting.texture = null; return; }

        Texture texture = sprite.texture;
        Rect source = sprite.rect;
        if (sprite.packed && sprite.packingMode == SpritePackingMode.Rectangle) source = sprite.textureRect;
        float aspect = source.width / Mathf.Max(1f, source.height);
        // A card illustration: most of its width, high on the art where the place is drawn.
        float cropW = card ? 0.86f : PaintingCropWidth;
        float cropH = Mathf.Min(1f, cropW * aspect * (PaintingSize.y / PaintingSize.x));
        float cropY = Mathf.Clamp((card ? 0.58f : PaintingCropCentreY) - cropH * 0.5f, 0f, 1f - cropH);
        float cropX = (1f - cropW) * 0.5f;
        float texW = Mathf.Max(1f, texture.width), texH = Mathf.Max(1f, texture.height);
        plate.painting.texture = texture;
        plate.painting.uvRect = new Rect(
            (source.x + cropX * source.width) / texW,
            (source.y + cropY * source.height) / texH,
            cropW * source.width / texW,
            cropH * source.height / texH);
    }

    // A white annulus (hole RingHole of the radius), anti-aliased, for the medallion's nation ring.
    // A clean bronze annulus (ring.png carries a dark square backdrop).
    private static Sprite bronzeRingSprite;
    private static readonly Color BronzeLight = new(0.78f, 0.6f, 0.32f, 1f);

    private static Sprite BronzeRingSprite()
    {
        if (bronzeRingSprite == null) bronzeRingSprite = RoundSprite("CityBannerBronzeRing", 0.84f);
        return bronzeRingSprite;
    }

    private static Sprite NationRingSprite()
    {
        if (nationRingSprite == null) nationRingSprite = RoundSprite("CityBannerNationRing", RingHole);
        return nationRingSprite;
    }

    private static Sprite DiscSprite()
    {
        if (discSprite == null) discSprite = RoundSprite("CityBannerDisc", 0f);
        return discSprite;
    }

    private static Sprite RoundSprite(string name, float hole)
    {
        const int Size = 128;
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[Size * Size];
        float radius = Size * 0.5f, inner = radius * hole;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
            float a = Mathf.Clamp01(radius - d);                        // soft outer edge
            if (hole > 0f) a = Mathf.Min(a, Mathf.Clamp01(d - inner));  // and inner edge
            pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    // Unit chips text, then a medallion per marker, left to right from `start` inside plate.units.
    private static void LayoutStripContent(Plate plate, PlateData data, float textWidth, float gap, int markerCount, float start)
    {
        RectTransform textRect = (RectTransform)plate.unitsText.transform;
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 0.5f);
        textRect.sizeDelta = new Vector2(textWidth, 0f);
        textRect.anchoredPosition = new Vector2(start, 0f);

        for (int i = 0; i < Mathf.Max(markerCount, plate.markers.Count); i++)
        {
            if (i >= markerCount) { plate.markers[i].rect.gameObject.SetActive(false); continue; }
            if (i >= plate.markers.Count) plate.markers.Add(NewMarker(plate.units));
            MarkerView view = plate.markers[i];
            Marker marker = data.markers[i];
            view.rect.gameObject.SetActive(true);
            view.icon.sprite = marker.icon;
            view.icon.enabled = marker.icon != null;
            view.hover.text = marker.tooltip;
            view.hover.source = marker.tooltipSource;
            view.rect.anchorMin = view.rect.anchorMax = new Vector2(0f, 0.5f);
            view.rect.pivot = new Vector2(0f, 0.5f);
            view.rect.anchoredPosition = new Vector2(start + textWidth + gap + i * (MarkerSize + 2f), 0f);
        }
    }

    // A marker medallion: the bronze ring (the strip's only raycast target) with the icon inside.
    private static MarkerView NewMarker(RectTransform strip)
    {
        var view = new MarkerView { rect = NewRect("Marker", strip) };
        view.rect.sizeDelta = new Vector2(MarkerSize, MarkerSize);
        // A dark disc behind the icon, so it reads on the board without a strip behind it.
        NewImage("Back", view.rect, DiscSprite(), new Color(0.06f, 0.05f, 0.04f, 0.92f), MarkerSize - 2f, Vector2.zero);
        view.icon = NewImage("Icon", view.rect, null, Color.white, MarkerSize * 0.62f, Vector2.zero);
        view.icon.preserveAspect = true;
        Image ring = NewImage("Ring", view.rect, ringSprite, Color.white, MarkerSize, Vector2.zero);
        ring.raycastTarget = true;
        view.hover = ring.gameObject.AddComponent<MarkerHover>();
        return view;
    }

    // ── Tooltip ─────────────────────────────────────────────────────────────────────────────
    // One shared tooltip for the markers: the same torn ink plate, parchment text, above the marker.

    private static RectTransform tooltip;
    private static TextMeshProUGUI tooltipText;

    private static void ShowTooltip(RectTransform anchor, string text)
    {
        if (instance == null || string.IsNullOrWhiteSpace(text)) return;
        if (tooltip == null)
        {
            tooltip = NewRect("Tooltip", instance.root);
            tooltip.anchorMin = tooltip.anchorMax = Vector2.zero;
            tooltip.pivot = new Vector2(0.5f, 0f);
            PlateImage(tooltip, 34f);
            tooltipText = NewText("Text", tooltip, 18f, FontStyles.Normal);
            Stretch((RectTransform)tooltipText.transform);
            ((RectTransform)tooltipText.transform).offsetMin = new Vector2(22f, 6f);
            ((RectTransform)tooltipText.transform).offsetMax = new Vector2(-22f, -6f);
            tooltipText.textWrappingMode = TextWrappingModes.Normal;
            tooltipText.color = Parchment;
            TMP_FontAsset font = BoardLookSettings.Instance.bannerFont;
            if (font != null) { tooltipText.font = font; tooltipText.fontSharedMaterial = font.material; }
        }
        tooltipText.text = text;
        Vector2 size = tooltipText.GetPreferredValues(text, 320f, 0f);
        tooltip.sizeDelta = new Vector2(Mathf.Min(size.x, 320f) + 44f, size.y + 16f);
        tooltip.gameObject.SetActive(true);
        tooltip.SetAsLastSibling();
        // Above the marker, in the banner canvas's space.
        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Vector2 top = (corners[1] + corners[2]) * 0.5f;
        Vector2 local = instance.root.InverseTransformPoint(top);
        Vector2 rootSize = instance.root.rect.size;
        tooltip.anchoredPosition = local + rootSize * instance.root.pivot + new Vector2(0f, 6f);
    }

    private static void HideTooltip()
    {
        if (tooltip != null) tooltip.gameObject.SetActive(false);
    }

    // One shared outlined copy of the banner font's material, for numbers on the coloured medallion.
    private static Material numberMaterial;

    private static Material NumberMaterial(TMP_FontAsset font)
    {
        if (numberMaterial != null) return numberMaterial;
        numberMaterial = new Material(font.material) { name = $"{font.name} (banner number)" };
        numberMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
        numberMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
        numberMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.85f));
        numberMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
        return numberMaterial;
    }

    private static void PlaceAt(RectTransform badge, float x, float y)
    {
        badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 1f);
        badge.pivot = new Vector2(0.5f, 0.5f);
        badge.anchoredPosition = new Vector2(x, y);
    }

    private static Color LoyaltyColor(float t)
    {
        Color low = new(0.84f, 0.22f, 0.16f), mid = new(0.9f, 0.68f, 0.24f), high = new(0.4f, 0.78f, 0.36f);
        return t < 0.5f ? Color.Lerp(low, mid, t * 2f) : Color.Lerp(mid, high, (t - 0.5f) * 2f);
    }

    // ── Per frame ───────────────────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        bool boardLive = Game.Instance != null && Game.Instance.started;
        if (canvas.enabled != boardLive) canvas.enabled = boardLive;
        if (!boardLive) return;

        // Places close to revolt pulse their loyalty bar.
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5f);
        foreach (KeyValuePair<Hex, Plate> pair in plates)
        {
            Plate plate = pair.Value;
            if (pair.Key == null || plate.rect == null || !plate.rect.gameObject.activeSelf) continue;
            Place(pair.Key, plate);
            if (plate.lowLoyalty)
            {
                Color c = plate.barFill.color;
                c.a = pulse;
                plate.barFill.color = c;
            }
        }
    }

    private void Place(Hex hex, Plate plate)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(hex.transform.position + AnchorOffset);
        bool onScreen = screen.z > 0f && screen.x > -200f && screen.x < Screen.width + 200f && screen.y > -100f && screen.y < Screen.height + 100f;
        if (!onScreen) { plate.rect.localScale = Vector3.zero; return; }

        // Constant on-screen size around the default zoom, a little smaller when zoomed far out
        // so neighbouring banners don't pile up.
        float zoom = cam.orthographicSize; // the board zoom, in perspective too (BoardNavigator)
        float scale = Mathf.Clamp(Mathf.Pow(ZoomScaledLabel.ReferenceZoom / zoom, 0.55f), 0.5f, 1.1f);
        plate.rect.localScale = new Vector3(scale, scale, 1f);
        float toCanvas = root.rect.height / Mathf.Max(1f, Screen.height);
        plate.rect.anchoredPosition = new Vector2(Mathf.Round(screen.x * toCanvas), Mathf.Round(screen.y * toCanvas));
    }
}
