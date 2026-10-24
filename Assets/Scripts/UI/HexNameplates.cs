using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space city banners, one per labelled hex, pinned under the hex and redrawn crisp at any
/// zoom — built from the game's own UI art (pieces cut from Art/UI/bokshi_mockup.png into
/// Resources/UI/CityBanner): dark weathered ink, fine bronze filigree, torn edges.
///   • a spiked bronze medallion holding the owner's true nation colour and the settlement size;
///     capitals carry the compass-star ornament above it
///   • a torn ink name plate with filigree lines (9-sliced to the name), the name in parchment-gold
///     (BoardLookSettings.bannerFont) — brighter gold for objective targets — plus port / hidden marks
///   • the fortification level on a filigree shield at the plate's end, when there is one
///   • under the plate, a thin bronze-edged loyalty bar (red → amber → green) that pulses when the
///     place is close to revolt
///   • after the name, behind a bronze rule, everything else on the hex: chips for the armies and
///     characters present, then a bronze-ringed medallion for each marker on the hex (an artifact,
///     a pending event, an underground way — Hex.CollectHudMarkers), whose tooltip shows in a
///     matching plate on hover (the strip's medallions are the canvas's only raycast targets)
/// Hexes with units or markers but no settlement get them alone, on a small strip of the same ink
/// (the unitsPlate). Hex owns the content
/// (RefreshHexLabel fills a PlateData) and calls Show/Hide; this class only builds, places and
/// draws. Banners sit on their own overlay canvas below the HUD; apart from the markers nothing
/// takes raycasts, so clicks and hovers still reach the board underneath.
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
    private const float PlateSourceHeight = 180f;            // plate.png's height, for its 9-slice ends
    private const float MedalWidth = 46f, MedalHeight = 62f;  // medallion.png is 145 × 195
    private const float StarWidth = 66f, StarHeight = 35f;    // star.png is 132 × 70
    private const float FortWidth = 28f, FortHeight = 33f;    // shield.png is 115 × 135
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

    private sealed class Plate
    {
        public RectTransform rect;
        public RectTransform banner;
        public TextMeshProUGUI name;
        public RectTransform medal;
        public Image medalFill;
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

    // The torn ink plate, 9-sliced so its ragged ends keep their shape at any width.
    private static Image PlateImage(RectTransform rt, float height)
    {
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = plateSprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = PlateSourceHeight / height;
        image.raycastTarget = false;
        return image;
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

        // The medallion: the nation colour behind the bronze frame, the size on top.
        plate.medal = NewRect("Medallion", plate.rect);
        plate.medal.sizeDelta = new Vector2(MedalWidth, MedalHeight);
        plate.medalFill = NewImage("Field", plate.medal, medalFillSprite, Color.white, 0f, Vector2.zero);
        Stretch(plate.medalFill.rectTransform);
        Image frame = NewImage("Frame", plate.medal, medalFrameSprite, Color.white, 0f, Vector2.zero);
        Stretch(frame.rectTransform);
        plate.popText = NewText("Size", plate.medal, 26f, FontStyles.Normal);
        Stretch((RectTransform)plate.popText.transform);

        plate.star = NewRect("Capital", plate.rect);
        plate.star.sizeDelta = new Vector2(StarWidth, StarHeight);
        Stretch(NewImage("Star", plate.star, starSprite, Color.white, 0f, Vector2.zero).rectTransform);

        // The fort level on a filigree shield at the plate's end.
        plate.fortBadge = NewRect("Fort", plate.rect);
        plate.fortBadge.sizeDelta = new Vector2(FortWidth, FortHeight);
        Stretch(NewImage("Shield", plate.fortBadge, shieldSprite, Color.white, 0f, Vector2.zero).rectTransform);
        plate.fortText = NewText("Level", plate.fortBadge, 17f, FontStyles.Normal);
        Stretch((RectTransform)plate.fortText.transform);
        ((RectTransform)plate.fortText.transform).offsetMin = new Vector2(0f, 4f); // up out of the point
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
        plate.divider.gameObject.SetActive(settlement && hasUnits);

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
            plate.medalFill.color = nation;

            // The size: dark ink on light nation colours, parchment on dark ones.
            float luma = 0.2126f * nation.r + 0.7152f * nation.g + 0.0722f * nation.b;
            bool lightField = luma > 0.5f;
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
            float rightInset = 22f + (data.fortSize > 0 ? FortWidth * 0.5f : 0f);
            float unitsSpace = hasUnits ? DividerSpace + contentWidth : 0f;
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
                // Chips inside the plate, right of the name; the strip's own plate is not drawn.
                float contentLeft = left + MedalWidth * 0.5f + plateWidth - rightInset - contentWidth;
                plate.unitsPlate.enabled = false;
                plate.units.anchorMin = plate.units.anchorMax = new Vector2(0.5f, 1f);
                plate.units.pivot = new Vector2(0f, 0.5f);
                plate.units.sizeDelta = new Vector2(contentWidth, PlateHeight);
                plate.units.anchoredPosition = new Vector2(contentLeft, plateTop - PlateHeight * 0.5f);
                LayoutStripContent(plate, data, textWidth, gap, markerCount, 0f);
                RectTransform divider = plate.divider.rectTransform;
                divider.anchorMin = divider.anchorMax = new Vector2(0f, 0.5f);
                divider.pivot = new Vector2(0.5f, 0.5f);
                divider.anchoredPosition = new Vector2(plateWidth - rightInset - contentWidth - DividerSpace * 0.5f, 0f);
            }

            plate.popText.text = Mathf.Clamp(data.citySize, 1, MaxCitySize).ToString();
            PlaceAt(plate.medal, left + MedalWidth * 0.5f, -MedalHeight * 0.5f);
            if (data.capital) PlaceAt(plate.star, left + MedalWidth * 0.5f, StarHeight * 0.32f);

            if (data.fortSize > 0)
            {
                plate.fortText.text = data.fortSize.ToString();
                PlaceAt(plate.fortBadge, left + MedalWidth * 0.5f + plateWidth - 12f, plateTop - PlateHeight * 0.5f);
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
            plate.unitsPlate.enabled = true;
            plate.units.anchorMin = plate.units.anchorMax = new Vector2(0.5f, 1f);
            plate.units.pivot = new Vector2(0.5f, 1f);
            plate.units.sizeDelta = new Vector2(contentWidth + Padding * 2f, markerCount > 0 ? MarkerSize + 2f : 24f);
            plate.units.anchoredPosition = new Vector2(0f, -y);
            LayoutStripContent(plate, data, textWidth, gap, markerCount, Padding);
            y += plate.units.sizeDelta.y + 2f;
            width = Mathf.Max(width, plate.units.sizeDelta.x);
        }

        plate.rect.sizeDelta = new Vector2(width, y);
        plate.height = y;
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
