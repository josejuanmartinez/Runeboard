using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space city banners in the manner of Civilization: one widget per labelled hex, pinned
/// under the hex and redrawn crisp at any zoom.
///   • a ribbon in the owner's nation colour (gold rim; a diamond crest on capitals) with the name —
///     gold for objective targets — plus small port / hidden marks
///   • left, a round population badge: the settlement size, ringed by a gold arc that fills as it
///     grows from camp to city
///   • right, a steel-ringed fort badge with the fortification level, when there is one
///   • underneath, a loyalty bar (red → amber → green) that pulses when the place is close to revolt
///   • below that, chips for the armies and characters present
/// Hexes with units but no settlement get the unit chips alone. Hex owns the content
/// (RefreshHexLabel fills a PlateData) and calls Show/Hide; this class only builds, places and
/// draws. Banners sit on their own overlay canvas below the HUD and never take raycasts, so clicks
/// and hovers still reach the board underneath.
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
    }

    private const float ReferenceHeight = 1080f;
    private const float BodyHeight = 28f;
    private const float BadgeSize = 40f;
    private const float FortBadgeSize = 32f;
    private const float NameSize = 17f;
    private const float BarHeight = 5f;
    private const int MaxCitySize = 5;
    private static readonly Vector3 AnchorOffset = new(0f, -0.23f, 0f); // under the tile's centre
    private static readonly Color Unowned = new(0.42f, 0.38f, 0.31f, 1f);
    private static readonly Color Gold = new(0.92f, 0.76f, 0.42f, 1f);
    private static readonly Color NameColor = new(0.97f, 0.95f, 0.9f, 1f);
    private static readonly Color Steel = new(0.7f, 0.75f, 0.82f, 1f);

    private static HexNameplates instance;
    private RectTransform root;
    private Canvas canvas;
    private readonly Dictionary<Hex, Plate> plates = new();

    private sealed class Plate
    {
        public RectTransform rect;
        public RectTransform banner;
        public CityBannerGraphic body;
        public TextMeshProUGUI name;
        public RectTransform popBadge;
        public Image popArc;
        public Image popRing;
        public TextMeshProUGUI popText;
        public RectTransform fortBadge;
        public Image fortRing;
        public TextMeshProUGUI fortText;
        public RectTransform barBack;
        public Image barFill;
        public RectTransform units;
        public TextMeshProUGUI unitsText;
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
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
        scaler.matchWidthOrHeight = 1f;
        instance.root = (RectTransform)go.transform;
        return instance;
    }

    // ── Construction ────────────────────────────────────────────────────────────────────────

    private Plate CreatePlate(Hex hex)
    {
        var plate = new Plate();
        plate.rect = NewRect($"CityBanner_{hex.name}", root);
        plate.rect.anchorMin = plate.rect.anchorMax = Vector2.zero;
        plate.rect.pivot = new Vector2(0.5f, 1f);

        plate.banner = NewRect("Banner", plate.rect);
        TopCentre(plate.banner);
        plate.body = plate.banner.gameObject.AddComponent<CityBannerGraphic>();
        plate.body.raycastTarget = false;

        plate.name = NewText("Name", plate.banner, NameSize, FontStyles.Bold | FontStyles.SmallCaps);
        Stretch((RectTransform)plate.name.transform);
        plate.name.characterSpacing = 3f;
        plate.name.color = NameColor;

        // Population: dark disc, dim track, gold growth arc, nation-coloured ring, the size.
        plate.popBadge = NewRect("Population", plate.rect);
        plate.popBadge.sizeDelta = new Vector2(BadgeSize, BadgeSize);
        NewImage("Shadow", plate.popBadge, HudSprites.Circle, new Color(0f, 0f, 0f, 0.45f), BadgeSize + 4f, new Vector2(1.5f, -2.5f));
        NewImage("Track", plate.popBadge, HudSprites.Circle, new Color(0.08f, 0.07f, 0.06f, 1f), BadgeSize, Vector2.zero);
        plate.popArc = NewImage("Growth", plate.popBadge, HudSprites.Circle, Gold, BadgeSize - 2f, Vector2.zero);
        plate.popArc.type = Image.Type.Filled;
        plate.popArc.fillMethod = Image.FillMethod.Radial360;
        plate.popArc.fillOrigin = (int)Image.Origin360.Top;
        plate.popArc.fillClockwise = true;
        NewImage("Disc", plate.popBadge, HudSprites.Circle, new Color(0.1f, 0.09f, 0.08f, 1f), BadgeSize - 9f, Vector2.zero);
        plate.popRing = NewImage("Ring", plate.popBadge, HudSprites.Ring, Gold, BadgeSize + 2f, Vector2.zero);
        plate.popText = NewText("Size", plate.popBadge, 21f, FontStyles.Bold);
        Stretch((RectTransform)plate.popText.transform);
        plate.popText.color = NameColor;

        // Fort: smaller dark disc with the fort icon and level.
        plate.fortBadge = NewRect("Fort", plate.rect);
        plate.fortBadge.sizeDelta = new Vector2(FortBadgeSize, FortBadgeSize);
        NewImage("Shadow", plate.fortBadge, HudSprites.Circle, new Color(0f, 0f, 0f, 0.45f), FortBadgeSize + 4f, new Vector2(1.5f, -2.5f));
        NewImage("Disc", plate.fortBadge, HudSprites.Circle, new Color(0.1f, 0.09f, 0.08f, 1f), FortBadgeSize, Vector2.zero);
        plate.fortRing = NewImage("Ring", plate.fortBadge, HudSprites.Ring, Gold, FortBadgeSize + 2f, Vector2.zero);
        plate.fortText = NewText("Level", plate.fortBadge, 16f, FontStyles.Bold);
        Stretch((RectTransform)plate.fortText.transform);
        plate.fortText.color = NameColor;

        // Loyalty bar under the ribbon.
        plate.barBack = NewRect("Loyalty", plate.rect);
        var back = plate.barBack.gameObject.AddComponent<Image>();
        back.color = new Color(0.05f, 0.045f, 0.04f, 0.9f);
        back.raycastTarget = false;
        var fillRect = NewRect("Fill", plate.barBack);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(1f, 1f);
        fillRect.offsetMax = new Vector2(-1f, -1f);
        plate.barFill = fillRect.gameObject.AddComponent<Image>();
        plate.barFill.raycastTarget = false;

        // Units present: a small smoked pill under everything.
        plate.units = NewRect("Units", plate.rect);
        var unitsFace = plate.units.gameObject.AddComponent<CinematicPanel>();
        unitsFace.fillTop = new Color(0.03f, 0.035f, 0.04f, 0.8f);
        unitsFace.fillBottom = new Color(0.02f, 0.022f, 0.026f, 0.88f);
        unitsFace.lineWidth = 0f;
        unitsFace.ornament = 0f;
        unitsFace.raycastTarget = false;
        plate.unitsText = NewText("Text", plate.units, 15f, FontStyles.Normal);
        Stretch((RectTransform)plate.unitsText.transform);
        plate.unitsText.color = NameColor;
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
        foreach (TextMeshProUGUI text in new[] { plate.name, plate.popText, plate.fortText, plate.unitsText })
        {
            if (font != null && text.font != font) text.font = font;
            if (sprites != null) text.spriteAsset = sprites;
        }

        bool settlement = data.hasSettlement;
        plate.banner.gameObject.SetActive(settlement);
        plate.popBadge.gameObject.SetActive(settlement);
        plate.fortBadge.gameObject.SetActive(settlement && data.fortSize > 0);
        plate.barBack.gameObject.SetActive(settlement);
        bool hasUnits = !string.IsNullOrEmpty(data.unitsRichText);
        plate.units.gameObject.SetActive(hasUnits);

        float y = 0f;
        float width = 0f;
        if (settlement)
        {
            // Nation colour, kept dark enough for the white name to read on it.
            Color nation = data.nationColor.a > 0f ? data.nationColor : Unowned;
            Color.RGBToHSV(nation, out float h, out float s, out float v);
            Color deep = Color.HSVToRGB(h, Mathf.Clamp(s, 0.35f, 0.85f), Mathf.Clamp(v, 0.32f, 0.62f));
            Color rimColor = data.objective || data.capital ? Gold : Color.Lerp(deep, Color.white, 0.45f);
            plate.body.fillTop = Color.Lerp(deep, Color.white, 0.12f);
            plate.body.fillBottom = deep * 0.68f;
            plate.body.fillTop.a = plate.body.fillBottom.a = 0.97f;
            plate.body.rim = rimColor;
            plate.body.crest = data.capital;
            plate.body.SetVerticesDirty();

            string marks = (data.port ? " <size=80%><sprite name=\"port\"></size>" : "") + (data.hidden ? " <size=80%><sprite name=\"hidden\"></size>" : "");
            string nameColor = data.objective ? ColorUtility.ToHtmlStringRGB(Gold) : ColorUtility.ToHtmlStringRGB(NameColor);
            plate.name.text = $"<color=#{nameColor}>{data.name}</color>{marks}";
            float nameWidth = plate.name.GetPreferredValues(plate.name.text).x;

            // Ribbon spans the name with room for the badges tucked into its pointed ends.
            float bodyWidth = Mathf.Max(120f, nameWidth + BadgeSize + (data.fortSize > 0 ? FortBadgeSize : 12f) + 20f);
            plate.banner.sizeDelta = new Vector2(bodyWidth, BodyHeight);
            plate.banner.anchoredPosition = new Vector2(0f, -(BadgeSize - BodyHeight) * 0.5f);
            RectTransform nameRect = (RectTransform)plate.name.transform;
            float leftInset = BadgeSize * 0.5f + 6f, rightInset = (data.fortSize > 0 ? FortBadgeSize * 0.5f : 0f) + 10f;
            nameRect.offsetMin = new Vector2(leftInset, 0f);
            nameRect.offsetMax = new Vector2(-rightInset, 0f);

            int size = Mathf.Clamp(data.citySize, 1, MaxCitySize);
            plate.popText.text = size.ToString();
            plate.popArc.fillAmount = size / (float)MaxCitySize;
            plate.popRing.color = rimColor;
            PlaceBadge(plate.popBadge, -bodyWidth * 0.5f + 4f, -BadgeSize * 0.5f);

            if (data.fortSize > 0)
            {
                plate.fortText.text = data.fortSize.ToString();
                plate.fortRing.color = Steel; // walls: steel, where the population ring is gold
                PlaceBadge(plate.fortBadge, bodyWidth * 0.5f - 2f, -BadgeSize * 0.5f);
            }

            // Loyalty bar, slung under the ribbon between the badges.
            float barWidth = bodyWidth - BadgeSize - 4f;
            plate.barBack.anchorMin = plate.barBack.anchorMax = new Vector2(0.5f, 1f);
            plate.barBack.pivot = new Vector2(0.5f, 1f);
            plate.barBack.sizeDelta = new Vector2(barWidth, BarHeight + 2f);
            plate.barBack.anchoredPosition = new Vector2(BadgeSize * 0.25f, -(BadgeSize + BodyHeight) * 0.5f + 1f);
            float loyalty = Mathf.Clamp01(data.loyalty / 100f);
            RectTransform fill = plate.barFill.rectTransform;
            fill.anchorMax = new Vector2(loyalty, 1f); // the bar fills from the left
            plate.barFill.color = LoyaltyColor(loyalty);
            plate.lowLoyalty = data.loyalty < 30;

            y = BadgeSize + 2f;
            width = bodyWidth + BadgeSize * 0.5f;
        }

        if (hasUnits)
        {
            plate.unitsText.text = data.unitsRichText;
            Vector2 preferred = plate.unitsText.GetPreferredValues(data.unitsRichText);
            plate.units.anchorMin = plate.units.anchorMax = new Vector2(0.5f, 1f);
            plate.units.pivot = new Vector2(0.5f, 1f);
            plate.units.sizeDelta = new Vector2(preferred.x + 16f, Mathf.Max(20f, preferred.y + 4f));
            plate.units.anchoredPosition = new Vector2(0f, -(y + (settlement ? 3f : 0f)));
            y += plate.units.sizeDelta.y + 3f;
            width = Mathf.Max(width, plate.units.sizeDelta.x);
        }

        plate.rect.sizeDelta = new Vector2(width, y);
        plate.height = y;
    }

    private static void PlaceBadge(RectTransform badge, float x, float y)
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
        float zoom = cam.orthographic ? cam.orthographicSize : ZoomScaledLabel.ReferenceZoom;
        float scale = Mathf.Clamp(Mathf.Pow(ZoomScaledLabel.ReferenceZoom / zoom, 0.55f), 0.5f, 1.1f);
        plate.rect.localScale = new Vector3(scale, scale, 1f);
        float toCanvas = root.rect.height / Mathf.Max(1f, Screen.height);
        plate.rect.anchoredPosition = new Vector2(Mathf.Round(screen.x * toCanvas), Mathf.Round(screen.y * toCanvas));
    }
}
