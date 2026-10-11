using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the in-game HUD composition in the "cinematic minimal" direction (concept C):
/// no boxed bars — soft dark gradients at the screen edges carry the HUD; every panel is smoked
/// glass with a thin gold hairline; rival leaders are round medallions; the minimap is round and
/// clickable; the board gets as much of the screen as possible.
/// All numbers are Layout-canvas units (2560×1440 reference, height-matched).
/// </summary>
public static class HudDirector
{
    public const float TopBarHeight = 96f;   // visual top zone (for things that hang below it)
    public const float Margin = 24f;

    private const float MinDisplayFontSize = 30f;
    public static readonly Color Line = new(0.80f, 0.64f, 0.36f, 0.75f);

    public static bool Applied { get; private set; }

    public static void Apply()
    {
        Layout layout = Object.FindAnyObjectByType<Layout>();
        if (layout == null) return;
        RectTransform root = layout.transform as RectTransform;

        EdgeFade(root, "HudTopFade", top: true, 190f, 0.85f);
        EdgeFade(root, "HudBottomFade", top: false, 360f, 0.9f);

        RestylePanels(root);
        DockTop(root);
        DockBottom(root);
        UnifyType(root);

        Transform background = root.Find("Background");
        if (background != null && background.TryGetComponent(out Image bg)) bg.raycastTarget = false;
        if (root.TryGetComponent(out Image rootImage)) rootImage.raycastTarget = false;
        Applied = true;
    }

    // ------------------------------------------------------------------ edge gradients

    private static void EdgeFade(RectTransform root, string name, bool top, float height, float strength)
    {
        if (root.Find(name) != null) return;
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(root, false);
        rt.SetSiblingIndex(0);
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, height);
        var img = go.GetComponent<Image>();
        img.sprite = top ? HudSprites.FadeDown : HudSprites.FadeUp;
        img.type = Image.Type.Simple;
        img.color = new Color(1f, 1f, 1f, strength);
        img.raycastTarget = false;
    }

    // ------------------------------------------------------------------ panels

    // Every authored RuneboardPanel becomes smoked glass with a gold hairline and square corners;
    // translucent "gold ghost" hover highlights keep their role but lose the heavy fill.
    private static void RestylePanels(RectTransform root)
    {
        foreach (RuneboardPanel panel in root.GetComponentsInChildren<RuneboardPanel>(true))
        {
            bool ghost = panel.topColor.a < 0.6f;
            panel.corner = 0f;
            panel.edgeWidth = 1.2f;
            if (ghost)
            {
                panel.topColor = new Color(0.80f, 0.64f, 0.36f, 0.10f);
                panel.bottomColor = new Color(0.80f, 0.64f, 0.36f, 0.0f);
                panel.edgeColor = new Color(0.86f, 0.70f, 0.40f, 0.9f);
            }
            else
            {
                panel.topColor = new Color(0.035f, 0.040f, 0.045f, 0.74f);
                panel.bottomColor = new Color(0.020f, 0.022f, 0.026f, 0.84f);
                panel.edgeColor = Line;
            }
            panel.SetVerticesDirty();
        }
    }

    // ------------------------------------------------------------------ top

    private static void DockTop(RectTransform root)
    {
        RectTransform top = root.Find("Top") as RectTransform;
        if (top == null) return;
        if (top.TryGetComponent(out Image topImage)) topImage.enabled = false;
        top.anchoredPosition = new Vector2(top.anchoredPosition.x, 0f);
        top.sizeDelta = new Vector2(top.sizeDelta.x, TopBarHeight);

        // Resources: bare icons + numbers on the gradient, top-left.
        RectTransform stores = top.Find("StoresBG") as RectTransform;
        if (stores != null)
        {
            SetActive(stores, "Polish Surface", false);
            SetActive(stores, "Text", false);
            if (stores.TryGetComponent(out Image storesImage)) storesImage.enabled = false;
            Place(stores, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(Margin - 10f, -TopBarHeight * 0.45f), 0.95f);
            RectTransform grid = stores.Find("Stores") as RectTransform;
            if (grid != null) grid.anchoredPosition = new Vector2(grid.anchoredPosition.x, stores.rect.height * 0.5f);
            AddHairline(top, new Vector2(0f, 1f), new Vector2(Margin, -TopBarHeight + 8f), new Vector2(600f, 1.2f), new Vector2(0f, 0.5f));
        }

        foreach (string name in new[] { "PlayableLeaderIcon", "PlayableLeaderIcon-Competitor1", "PlayableLeaderIcon-Competitor2" })
        {
            RectTransform icon = top.Find(name) as RectTransform;
            if (icon != null) MakeMedallion(icon);
        }

        // Environment chip + turn/date share one hairline panel at the top-right.
        RectTransform turn = top.Find("HexTurnDarkCloud") as RectTransform;
        RectTransform environment = top.Find("Environmental") as RectTransform;
        RectTransform panel = EnsureCinematicPanel(top, "HudTopRightPanel", new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-Margin, -14f), new Vector2(640f, 104f), topOrnament: false, bottomOrnament: true);
        float x = -Margin - 24f;
        if (turn != null)
        {
            SetActive(turn, "Polish Surface", false);
            SetActive(turn, "HexNumberManager", false);
            if (turn.TryGetComponent(out Image turnImage)) turnImage.enabled = false;
            Place(turn, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(x, -66f), 0.78f);
            x -= turn.rect.width * 0.78f + 8f;
        }
        if (environment != null)
        {
            SetActive(environment, "Polish Surface", false);
            Place(environment, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(x, -66f), 0.8f);
        }
        panel.SetAsFirstSibling();
    }

    // Round portrait in a thin gold ring, name and score under it.
    private static void MakeMedallion(RectTransform icon)
    {
        icon.localScale = Vector3.one * 0.82f;
        icon.anchoredPosition = new Vector2(icon.anchoredPosition.x * 0.86f, -6f);

        if (icon.Find("LeaderImageMask") is RectTransform mask && mask.TryGetComponent(out Image maskImage))
        {
            maskImage.sprite = HudSprites.Circle;
            maskImage.type = Image.Type.Simple;
            mask.localScale = Vector3.one;
            mask.anchorMin = mask.anchorMax = new Vector2(0.5f, 1f);
            mask.pivot = new Vector2(0.5f, 1f);
            mask.sizeDelta = new Vector2(104f, 104f);
            mask.anchoredPosition = new Vector2(0f, -8f);
            if (mask.Find("LeaderImage") is RectTransform portrait)
            {
                portrait.anchorMin = Vector2.zero;
                portrait.anchorMax = Vector2.one;
                portrait.offsetMin = new Vector2(-10f, -18f);
                portrait.offsetMax = new Vector2(10f, 4f);
                portrait.localScale = Vector3.one;
            }
        }
        if (icon.Find("Border") is RectTransform border && border.TryGetComponent(out Image borderImage))
        {
            borderImage.sprite = HudSprites.Ring;
            borderImage.color = new Color(0.88f, 0.72f, 0.42f, 1f);
            borderImage.type = Image.Type.Simple;
            border.localScale = Vector3.one;
            border.anchorMin = border.anchorMax = new Vector2(0.5f, 1f);
            border.pivot = new Vector2(0.5f, 1f);
            border.sizeDelta = new Vector2(120f, 120f);
            border.anchoredPosition = new Vector2(0f, 0f);
            border.SetAsLastSibling();
        }
        if (icon.Find("Bar") is RectTransform bar)
        {
            if (bar.TryGetComponent(out Image barImage)) barImage.enabled = false;
            if (bar.Find("TextBg") is Transform textBg && textBg.TryGetComponent(out Image textBgImage)) textBgImage.enabled = false;
            bar.localScale = Vector3.one * 1.2f;
            bar.anchoredPosition = new Vector2(0f, -66f);
        }
        SetActive(icon, "AlignmentImage", false);
    }

    // ------------------------------------------------------------------ bottom

    private static void DockBottom(RectTransform root)
    {
        RectTransform bottom = root.Find("Bottom") as RectTransform;
        if (bottom == null) return;

        RectTransform character = bottom.Find("SelectedCharacterIcon") as RectTransform;
        if (character != null)
        {
            SetActive(character, "LeaderBorder", false);
            SetActive(character, "DarkClouds", false);
            character.localScale = Vector3.one * 0.84f;
            character.anchorMin = character.anchorMax = new Vector2(0f, 0f);
            character.pivot = new Vector2(0f, 0f);
            character.anchoredPosition = new Vector2(Margin, Margin);
        }

        RectTransform endTurn = bottom.Find("EndTurn") as RectTransform;
        RectTransform minimap = bottom.Find("Minimap") as RectTransform;
        float y = Margin;
        if (endTurn != null)
        {
            endTurn.localScale = Vector3.one * 0.9f;
            endTurn.anchorMin = endTurn.anchorMax = new Vector2(1f, 0f);
            endTurn.pivot = new Vector2(1f, 0f);
            endTurn.anchoredPosition = new Vector2(-Margin, y);
            if (endTurn.Find("Text") is RectTransform label && label.TryGetComponent(out TMP_Text text))
            {
                UiTheme.StyleTitle(text, 30f);
                text.characterSpacing = 8f;
                text.text = text.text.ToUpperInvariant();
            }
            y += endTurn.rect.height * 0.9f + 14f;
        }
        if (minimap != null) MakeRoundMinimap(minimap, y);
    }

    private static void MakeRoundMinimap(RectTransform minimap, float y)
    {
        SetActive(minimap, "Polish Surface", false);
        SetActive(minimap, "CollapseTab", false);
        if (minimap.TryGetComponent(out Image frame)) frame.enabled = false;
        minimap.localScale = Vector3.one * 0.9f;
        minimap.anchorMin = minimap.anchorMax = new Vector2(1f, 0f);
        minimap.pivot = new Vector2(1f, 0f);
        minimap.anchoredPosition = new Vector2(-Margin, y);

        RectTransform bg = minimap.Find("MinimapBg") as RectTransform;
        if (bg == null) return;
        if (bg.TryGetComponent(out Image bgImage)) bgImage.enabled = false;
        SetActive(bg, "Stick", false);
        SetActive(bg, "Banner", false);
        SetActive(bg, "NationColorBorder", false);

        RectTransform map = bg.Find("Minimap") as RectTransform;
        if (map == null || bg.Find("MinimapCircle") != null) return;
        var circleGo = new GameObject("MinimapCircle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        var circle = circleGo.GetComponent<RectTransform>();
        circle.SetParent(bg, false);
        circle.anchorMin = circle.anchorMax = new Vector2(0.5f, 0f);
        circle.pivot = new Vector2(0.5f, 0.5f);
        circle.sizeDelta = new Vector2(270f, 270f);
        circle.anchoredPosition = new Vector2(0f, 150f);
        var circleImage = circleGo.GetComponent<Image>();
        circleImage.sprite = HudSprites.Circle;
        circleGo.GetComponent<Mask>().showMaskGraphic = true;
        circleImage.color = new Color(0.02f, 0.025f, 0.03f, 1f);

        map.SetParent(circle, false);
        map.anchorMin = map.anchorMax = new Vector2(0.5f, 0.5f);
        map.pivot = new Vector2(0.5f, 0.5f);
        map.sizeDelta = new Vector2(300f, 300f);
        map.anchoredPosition = Vector2.zero;

        var ringGo = new GameObject("MinimapRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var ring = ringGo.GetComponent<RectTransform>();
        ring.SetParent(bg, false);
        ring.anchorMin = ring.anchorMax = circle.anchorMin;
        ring.sizeDelta = new Vector2(292f, 292f);
        ring.anchoredPosition = circle.anchoredPosition;
        var ringImage = ringGo.GetComponent<Image>();
        ringImage.sprite = HudSprites.Ring;
        ringImage.color = new Color(0.88f, 0.72f, 0.42f, 1f);
        ringImage.raycastTarget = false;
    }

    /// <summary>Left edge (canvas units, from the canvas' left) of the bottom-right column.</summary>
    public static float DockRightColumnLeft(RectTransform canvasRoot)
    {
        Layout layout = Object.FindAnyObjectByType<Layout>();
        RectTransform endTurn = layout != null ? layout.transform.Find("Bottom/EndTurn") as RectTransform : null;
        if (endTurn == null || canvasRoot == null) return -1f;
        Vector3[] c = new Vector3[4];
        endTurn.GetWorldCorners(c);
        return canvasRoot.InverseTransformPoint(c[0]).x - canvasRoot.rect.xMin;
    }

    // ------------------------------------------------------------------ type

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

    public static RectTransform EnsureCinematicPanel(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, bool topOrnament = true, bool bottomOrnament = false)
    {
        RectTransform rt = parent.Find(name) as RectTransform;
        if (rt == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
        }
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var panel = rt.GetComponent<CinematicPanel>();
        panel.raycastTarget = false;
        panel.topOrnament = topOrnament;
        panel.bottomOrnament = bottomOrnament;
        return rt;
    }

    private static void AddHairline(RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        var go = new GameObject("HudHairline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = new Color(Line.r, Line.g, Line.b, 0.45f);
        img.raycastTarget = false;
    }

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
}
