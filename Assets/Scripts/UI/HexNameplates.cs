using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space place nameplates: one UI widget per labelled hex (settlement name + size, fort,
/// loyalty, and the armies/characters present), pinned under the hex and redrawn crisp at any
/// zoom. Replaces the world-space TextMeshPro + SpriteRenderer band that used to sit on the tile.
/// Hex owns the content (RefreshHexLabel builds the rich text) and calls Show/Hide; this class
/// only places and draws. Plates sit on their own overlay canvas below the HUD and never take
/// raycasts, so clicks and hovers still reach the board underneath.
/// </summary>
public class HexNameplates : MonoBehaviour
{
    private const float ReferenceHeight = 1080f;
    private const float FontSize = 19f;
    private static readonly Vector3 AnchorOffset = new(0f, -0.23f, 0f); // under the tile's centre

    private static HexNameplates instance;
    private RectTransform root;
    private Canvas canvas;
    private readonly Dictionary<Hex, Plate> plates = new();

    private sealed class Plate
    {
        public RectTransform rect;
        public TextMeshProUGUI text;
    }

    public static void Show(Hex hex, string richText, TMP_SpriteAsset sprites, TMP_FontAsset font)
    {
        if (hex == null) return;
        HexNameplates self = Ensure();
        if (!self.plates.TryGetValue(hex, out Plate plate)) self.plates[hex] = plate = self.CreatePlate(hex);
        if (sprites != null) plate.text.spriteAsset = sprites;
        if (font != null && plate.text.font != font) plate.text.font = font;
        plate.text.text = richText;
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

    private Plate CreatePlate(Hex hex)
    {
        var go = new GameObject($"Plate_{hex.name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var rt = (RectTransform)go.transform;
        rt.SetParent(root, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 1f);

        // Smoked glass, no hairline: the plate should read as a label, not another framed panel.
        var face = go.GetComponent<CinematicPanel>();
        face.fillTop = new Color(0.03f, 0.035f, 0.04f, 0.78f);
        face.fillBottom = new Color(0.02f, 0.022f, 0.026f, 0.86f);
        face.lineWidth = 0f;
        face.ornament = 0f;
        face.raycastTarget = false;

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 5, 6);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(rt, false);
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.fontSize = FontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.lineSpacing = -6f;
        text.richText = true;
        text.raycastTarget = false;
        text.color = new Color(.94f, .92f, .84f);
        return new Plate { rect = rt, text = text };
    }

    private void LateUpdate()
    {
        bool boardLive = Game.Instance != null && Game.Instance.started;
        if (canvas.enabled != boardLive) canvas.enabled = boardLive;
        if (!boardLive) return;

        foreach (KeyValuePair<Hex, Plate> pair in plates)
        {
            if (pair.Key == null || pair.Value.rect == null || !pair.Value.rect.gameObject.activeSelf) continue;
            Place(pair.Key, pair.Value);
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
        // so neighbouring plates don't pile up.
        float zoom = cam.orthographic ? cam.orthographicSize : ZoomScaledLabel.ReferenceZoom;
        float scale = Mathf.Clamp(Mathf.Pow(ZoomScaledLabel.ReferenceZoom / zoom, 0.45f), 0.62f, 1.1f);
        plate.rect.localScale = new Vector3(scale, scale, 1f);
        float toCanvas = root.rect.height / Mathf.Max(1f, Screen.height);
        plate.rect.anchoredPosition = new Vector2(Mathf.Round(screen.x * toCanvas), Mathf.Round(screen.y * toCanvas));
    }
}
