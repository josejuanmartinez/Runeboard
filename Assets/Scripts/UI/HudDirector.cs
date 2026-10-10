using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One place that owns the in-game HUD composition. The authored Layout prefab scatters
/// independent floating boxes (resources, environment, turn/date, leaders, character panel,
/// minimap) at assorted scales; this docks them into two coherent bands:
///   • a top bar — resources (left), leader medallions (centre), environment + turn/date (right)
///   • a bottom dock — character panel (left), hand tray (centre, see HandTray), minimap + End Turn (right)
/// and applies one type rule (no IM Fell below display sizes) and one panel palette.
/// All numbers are Layout-canvas units (2560×1440 reference, height-matched).
/// </summary>
public static class HudDirector
{
    public const float TopBarHeight = 86f;
    public const float DockHeight = 268f;
    public const float Margin = 16f;

    // Display-face text smaller than this (canvas units) switches to the UI sans.
    private const float MinDisplayFontSize = 30f;

    public static bool Applied { get; private set; }

    public static void Apply()
    {
        Layout layout = Object.FindFirstObjectByType<Layout>();
        if (layout == null) return;
        RectTransform root = layout.transform as RectTransform;

        BuildBand(root, "HudTopBar", top: true, TopBarHeight);
        BuildBand(root, "HudBottomDock", top: false, DockHeight);

        DockTop(root);
        DockBottom(root);
        UnifyPanels(root);
        UnifyType(root);

        // The full-screen "Background" image only exists for fades; it must not eat pointer
        // input (it blocked mouse-wheel zoom over the whole board).
        Transform background = root.Find("Background");
        if (background != null && background.TryGetComponent(out Image bg)) bg.raycastTarget = false;
        if (root.TryGetComponent(out Image rootImage)) rootImage.raycastTarget = false;

        Applied = true;
    }

    // ------------------------------------------------------------------ bands

    private static void BuildBand(RectTransform root, string name, bool top, float height)
    {
        if (root.Find(name) != null) return;
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RuneboardPanel));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(root, false);
        rt.SetSiblingIndex(0);
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, height);
        var panel = go.GetComponent<RuneboardPanel>();
        panel.raycastTarget = false;
        panel.corner = 0f;
        panel.topColor = top ? new Color(0.07f, 0.075f, 0.07f, 0.94f) : new Color(0.05f, 0.055f, 0.05f, 0.80f);
        panel.bottomColor = top ? new Color(0.03f, 0.035f, 0.033f, 0.90f) : new Color(0.02f, 0.025f, 0.024f, 0.96f);
        panel.edgeColor = new Color(0.60f, 0.46f, 0.24f, 0.55f);
        panel.edgeWidth = 1.5f;
    }

    // ------------------------------------------------------------------ top bar

    private static void DockTop(RectTransform root)
    {
        RectTransform top = root.Find("Top") as RectTransform;
        if (top == null) return;
        top.anchoredPosition = new Vector2(top.anchoredPosition.x, 0f);
        top.sizeDelta = new Vector2(top.sizeDelta.x, TopBarHeight);
        float midY = -TopBarHeight * 0.5f;

        // Resources: drop the floating box and its "YOUR RESOURCES" caption; the bar is the box.
        RectTransform stores = top.Find("StoresBG") as RectTransform;
        if (stores != null)
        {
            SetActive(stores, "Polish Surface", false);
            SetActive(stores, "Text", false);
            if (stores.TryGetComponent(out Image storesImage)) storesImage.enabled = false;
            Place(stores, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(Margin, midY), 0.92f);
            RectTransform grid = stores.Find("Stores") as RectTransform;
            if (grid != null) grid.anchoredPosition = new Vector2(grid.anchoredPosition.x, stores.rect.height * 0.5f);
        }

        // Leader medallions: smaller, centred on the bar, hanging just below it.
        foreach (string name in new[] { "PlayableLeaderIcon", "PlayableLeaderIcon-Competitor1", "PlayableLeaderIcon-Competitor2" })
        {
            RectTransform icon = top.Find(name) as RectTransform;
            if (icon == null) continue;
            icon.localScale = Vector3.one * 0.66f;
            icon.anchoredPosition = new Vector2(icon.anchoredPosition.x * 0.66f, -4f);
        }

        // Turn + date, then the environment chip to its left, both inside the bar.
        RectTransform turn = top.Find("HexTurnDarkCloud") as RectTransform;
        float rightEdge = -Margin;
        if (turn != null)
        {
            SetActive(turn, "Polish Surface", false);
            if (turn.TryGetComponent(out Image turnImage)) turnImage.enabled = false;
            float scale = 0.7f;
            Place(turn, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(rightEdge, midY), scale);
            rightEdge -= turn.rect.width * scale + Margin;
        }

        RectTransform environment = top.Find("Environmental") as RectTransform;
        if (environment != null)
        {
            SetActive(environment, "Polish Surface", false);
            Place(environment, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(rightEdge, midY), 0.8f);
            AddDivider(top, new Vector2(1f, 1f), new Vector2(rightEdge + Margin * 0.5f, midY));
        }
    }

    // ------------------------------------------------------------------ bottom dock

    private static void DockBottom(RectTransform root)
    {
        RectTransform bottom = root.Find("Bottom") as RectTransform;
        if (bottom == null) return;

        RectTransform character = bottom.Find("SelectedCharacterIcon") as RectTransform;
        if (character != null)
        {
            float scale = 0.86f;
            character.localScale = Vector3.one * scale;
            character.anchorMin = character.anchorMax = new Vector2(0f, 0f);
            character.pivot = new Vector2(0f, 0f);
            character.anchoredPosition = new Vector2(Margin, Margin * 0.75f);
        }

        // Right column: End Turn at the bottom, minimap stacked above it.
        RectTransform endTurn = bottom.Find("EndTurn") as RectTransform;
        RectTransform minimap = bottom.Find("Minimap") as RectTransform;
        float columnScale = 0.82f;
        float y = Margin * 0.75f;
        if (endTurn != null)
        {
            endTurn.localScale = Vector3.one * columnScale;
            endTurn.anchorMin = endTurn.anchorMax = new Vector2(1f, 0f);
            endTurn.pivot = new Vector2(1f, 0f);
            endTurn.anchoredPosition = new Vector2(-Margin, y);
            y += endTurn.rect.height * columnScale + Margin * 0.6f;
        }
        if (minimap != null)
        {
            minimap.localScale = Vector3.one * columnScale;
            minimap.anchorMin = minimap.anchorMax = new Vector2(1f, 0f);
            minimap.pivot = new Vector2(1f, 0f);
            minimap.anchoredPosition = new Vector2(-Margin, y);
        }
    }

    /// <summary>Left edge (canvas units, from the canvas' left) of the dock's right column.</summary>
    public static float DockRightColumnLeft(RectTransform canvasRoot)
    {
        Layout layout = Object.FindFirstObjectByType<Layout>();
        RectTransform endTurn = layout != null ? layout.transform.Find("Bottom/EndTurn") as RectTransform : null;
        if (endTurn == null || canvasRoot == null) return -1f;
        Vector3[] c = new Vector3[4];
        endTurn.GetWorldCorners(c);
        return canvasRoot.InverseTransformPoint(c[0]).x - canvasRoot.rect.xMin;
    }

    // ------------------------------------------------------------------ style unification

    // RuneboardPanels in the HUD drifted into several near-identical palettes; the solid dark
    // ones become one palette (translucent "gold ghost" accents are left alone).
    private static void UnifyPanels(RectTransform root)
    {
        foreach (RuneboardPanel panel in root.GetComponentsInChildren<RuneboardPanel>(true))
        {
            if (panel.topColor.a < 0.6f) continue;
            if (panel.name == "HudTopBar" || panel.name == "HudBottomDock") continue;
            panel.topColor = new Color(0.10f, 0.105f, 0.10f, 0.96f);
            panel.bottomColor = new Color(0.035f, 0.04f, 0.038f, 0.97f);
            panel.edgeColor = new Color(0.66f, 0.51f, 0.27f, 0.75f);
            panel.SetVerticesDirty();
        }
    }

    private static void UnifyType(RectTransform root)
    {
        TMP_FontAsset display = TMP_Settings.defaultFontAsset;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == display && text.fontSize < MinDisplayFontSize)
            {
                text.font = UiTheme.Body;
                text.fontStyle &= ~FontStyles.Bold;
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, float scale)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.localScale = Vector3.one * scale;
        rt.anchoredPosition = position;
    }

    private static void SetActive(Transform parent, string child, bool active)
    {
        Transform t = parent.Find(child);
        if (t != null) t.gameObject.SetActive(active);
    }

    private static void AddDivider(RectTransform parent, Vector2 anchor, Vector2 position)
    {
        var go = new GameObject("HudDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(1.5f, TopBarHeight * 0.55f);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.66f, 0.51f, 0.27f, 0.45f);
        img.raycastTarget = false;
    }
}
