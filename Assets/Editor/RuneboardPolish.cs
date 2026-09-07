using System;
using System.Linq;
using RetroLOTR.Scenarios;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Authors the polish pass in the open InGame scene. No runtime scene searches or restyling.</summary>
public static class RuneboardPolish
{
    static readonly Color Ivory = new(0.94f, 0.89f, 0.77f);
    static readonly Color Gold = new(0.83f, 0.67f, 0.39f);
    static TMP_FontAsset Body => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Inter/Inter-Regular SDF.asset");

    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Apply in Edit mode.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/InGame.unity") throw new InvalidOperationException("Open InGame first.");
        Undo.RegisterFullObjectHierarchyUndo(Root("StartScreen"), "Polish Runeboard title");
        Title(Root("StartScreen").transform);
        Campaign(Root("CampaignSelectionScreen").transform);
        Leaders(Root("LeaderSelection").GetComponent<LeaderSelector>());
        RuneboardGamePolish.Apply();
        Pause(Root("PauseMenu").transform);
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                if (component != null) EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        // Keep the reusable campaign prefab consistent with the authored scene instance.
        const string campaignPath = "Assets/GameObjects/CampaignSelectionScreen.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(campaignPath) != null)
        {
            GameObject prefab = PrefabUtility.LoadPrefabContents(campaignPath);
            try
            {
                Campaign(prefab.transform);
                foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
                    if (component != null) EditorUtility.SetDirty(component);
                PrefabUtility.SaveAsPrefabAsset(prefab, campaignPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        AssetDatabase.SaveAssets();
    }

    static GameObject Root(string name) => EditorSceneManager.GetActiveScene().GetRootGameObjects().First(r => r.name == name);
    static RectTransform Rect(Transform root, string path) => root.Find(path) as RectTransform;
    internal static void Place(RectTransform r, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        if (r == null) return;
        r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
        r.anchorMin = min; r.anchorMax = max; r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = offsetMin; r.offsetMax = offsetMax;
    }
    internal static void Box(RectTransform r, Vector2 anchor, Vector2 position, Vector2 size)
    {
        Place(r, anchor, anchor, position - size * 0.5f, position + size * 0.5f);
    }
    internal static RectTransform Child(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found != null) return (RectTransform)found;
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
    internal static RuneboardPanel Panel(Transform parent, string name = "Polish Surface")
    {
        RectTransform rect = Child(parent, name);
        foreach (Transform sibling in parent)
            if (sibling != rect && sibling.name == name) sibling.gameObject.SetActive(false);
        rect.gameObject.SetActive(true);
        var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        Place(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        rect.SetAsFirstSibling();
        if (rect.GetComponent<CanvasRenderer>() == null) rect.gameObject.AddComponent<CanvasRenderer>();
        var panel = rect.GetComponent<RuneboardPanel>() ?? rect.gameObject.AddComponent<RuneboardPanel>();
        panel.raycastTarget = false;
        panel.SetAllDirty();
        return panel;
    }
    internal static void QuietImage(Transform t)
    {
        if (t != null && t.TryGetComponent<Image>(out var image)) image.enabled = false;
    }
    internal static void Hide(Transform root, string path)
    {
        Transform t = root.Find(path); if (t != null) t.gameObject.SetActive(false);
    }
    internal static void Text(TMP_Text text, float size, bool body = false, Color? color = null)
    {
        if (text == null) return;
        if (body && Body != null) { text.font = Body; text.fontSharedMaterial = Body.material; }
        text.fontSize = size; text.fontSizeMax = size; text.fontSizeMin = size * 0.88f;
        text.enableAutoSizing = true;
        text.fontStyle = FontStyles.Normal;
        text.color = color ?? Ivory;
        text.raycastTarget = false;
        foreach (var outline in text.GetComponents<Outline>()) Object.DestroyImmediate(outline);
        // Preserve the chosen skin typeface, with a lighter edge for display labels.
        if (!body && text.font != null)
        {
            string path = "Assets/Art/Materials/MenuType-" + text.font.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(text.font.material);
                material.SetFloat("_OutlineWidth", 0.06f);
                material.SetFloat("_FaceDilate", 0);
                AssetDatabase.CreateAsset(material, path);
            }
            text.fontSharedMaterial = material;
        }
    }
    internal static TMP_Text Label(Transform parent, string name, string value, float size, bool body = false)
    {
        RectTransform r = Child(parent, name);
        var text = r.GetComponent<TextMeshProUGUI>() ?? r.gameObject.AddComponent<TextMeshProUGUI>();
        if (text.font == null) text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Default.asset");
        text.text = value; Text(text, size, body); text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }
    internal static void ButtonStyle(Button button, bool primary = false)
    {
        if (button == null) return;
        QuietImage(button.transform);
        var surface = Panel(button.transform);
        surface.raycastTarget = true;
        if (primary)
        {
            surface.topColor = new Color(0.25f, 0.20f, 0.105f, 0.98f);
            surface.bottomColor = new Color(0.085f, 0.09f, 0.06f, 0.98f);
            surface.edgeColor = Gold;
        }
        button.targetGraphic = surface;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.16f, 1.12f, 1.04f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.77f, 0.78f, 0.70f);
        colors.disabledColor = new Color(0.48f, 0.48f, 0.48f, 0.6f);
        colors.fadeDuration = 0.12f; button.colors = colors;
        var glow = Panel(button.transform, "Polish Highlight");
        glow.transform.SetAsLastSibling();
        glow.topColor = new Color(0.76f, 0.59f, 0.29f, 0.16f);
        glow.bottomColor = Color.clear; glow.edgeColor = Gold; glow.edgeWidth = 1.5f;
        glow.color = new Color(1, 1, 1, 0); glow.SetAllDirty();
        var feedback = button.GetComponent<MenuButtonFeedback>() ?? button.gameObject.AddComponent<MenuButtonFeedback>();
        feedback.highlight = glow;
        if (button.GetComponent<ClickableCursorOnHover>() == null) button.gameObject.AddComponent<ClickableCursorOnHover>();
        surface.SetAllDirty();
    }
    static void Reveal(Transform t)
    {
        if (t.GetComponent<CanvasGroup>() == null) t.gameObject.AddComponent<CanvasGroup>();
        if (t.GetComponent<MenuReveal>() == null) t.gameObject.AddComponent<MenuReveal>();
    }

    static void Title(Transform root)
    {
        RectTransform bar = Rect(root, "Menu Bar");
        if (bar.GetComponent<VerticalLayoutGroup>() != null) bar.GetComponent<VerticalLayoutGroup>().enabled = false;
        Place(bar, new Vector2(0.02f, 0.03f), new Vector2(0.32f, 0.97f), Vector2.zero, Vector2.zero);
        foreach (string name in new[] { "MenuBg", "Connector", "News", "ButtonBg", "ButtonBg (1)", "ButtonBg (2)", "ButtonBg (3)" }) Hide(bar, name);
        string[] actors = { "UIAnimatedCharacter", "UIAnimatedCharacter (1)", "UIAnimatedCharacter (2)" };
        for (int i = 0; i < actors.Length; i++)
            Box(Rect(bar, actors[i]), new Vector2(0.17f + i * 0.33f, 0.66f), Vector2.zero, new Vector2(225, 225));
        string[] buttons = { "StartButton", "SkinButton", "QuitButton" };
        string[] subtitles = { "Choose a campaign and begin your journey", "Change the look of your world", "Leave Middle-earth" };
        for (int i = 0; i < buttons.Length; i++)
        {
            RectTransform r = Rect(bar, buttons[i]);
            foreach (Image decoration in r.GetComponentsInChildren<Image>(true))
                if (decoration.transform != r) decoration.gameObject.SetActive(false);
            float y = i == 0 ? 0.43f : i == 1 ? 0.31f : 0.15f;
            Place(r, new Vector2(0.07f, y), new Vector2(0.93f, y), new Vector2(0, -51), new Vector2(0, 51));
            ButtonStyle(r.GetComponent<Button>(), i == 0);
            TMP_Text label = r.Find("Label").GetComponent<TMP_Text>();
            Text(label, 38, false, i == 0 ? Gold : Ivory);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Place(label.rectTransform, new Vector2(0, 0.40f), Vector2.one, new Vector2(30, 0), new Vector2(-25, -9));
            if (i == 0) label.text = "Begin your journey";
            TMP_Text subtitle = Label(r, "Polish Description", subtitles[i], 19, true);
            subtitle.color = new Color(0.69f, 0.69f, 0.61f);
            Place(subtitle.rectTransform, Vector2.zero, new Vector2(1, 0.40f), new Vector2(30, 7), new Vector2(-18, 0));
        }
        TMP_Text footer = Label(bar, "Footer", "A WORLD OF CARDS. A WAR OF CHOICES.", 15, true);
        footer.characterSpacing = 2;
        Box(footer.rectTransform, new Vector2(0.5f, 0.045f), Vector2.zero, new Vector2(660, 30));
        footer.alignment = TextAlignmentOptions.Center;
    }

    static void Campaign(Transform root)
    {
        Hide(root, "DarkCloud");
        var background = Rect(root, "Background");
        Place(background, new Vector2(0.355f, 0.16f), new Vector2(0.945f, 0.89f), Vector2.zero, Vector2.zero);
        QuietImage(background); Panel(background); Reveal(background);
        Transform panel = background.Find("Panel");
        if (panel.TryGetComponent<VerticalLayoutGroup>(out var layout)) layout.enabled = false;
        TMP_Text title = panel.Find("Title").GetComponent<TMP_Text>();
        Text(title, 39, false, Gold); title.text = "Choose your campaign"; title.alignment = TextAlignmentOptions.MidlineLeft;
        Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(32, -89), new Vector2(-80, -25));
        TMP_Text hint = Label(panel, "Polish Hint", "Explore a new world, or step into a tale already unfolding.", 17, true);
        Place(hint.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(34, -123), new Vector2(-30, -89));
        RectTransform defaultButton = Rect(panel, "DefaultCampaignButton");
        Place(defaultButton, new Vector2(0, 1), Vector2.one, new Vector2(28, -337), new Vector2(-28, -151));
        CampaignButton(defaultButton);
        RectTransform list = Rect(panel, "ScenarioList");
        Place(list, Vector2.zero, Vector2.one, new Vector2(28, 30), new Vector2(-28, -358));
        QuietImage(list);
        Transform content = list.Find("Content");
        var group = content.GetComponent<VerticalLayoutGroup>();
        group.spacing = 16; group.padding = new RectOffset(0, 0, 0, 0);
        group.childControlHeight = true; group.childForceExpandHeight = false;
        foreach (Button b in content.GetComponentsInChildren<Button>(true)) CampaignButton((RectTransform)b.transform);
        var closeRect = Child(panel, "Polish Close");
        Box(closeRect, Vector2.one, new Vector2(-43, -51), new Vector2(42, 42));
        var close = closeRect.GetComponent<Button>() ?? closeRect.gameObject.AddComponent<Button>();
        ButtonStyle(close);
        TMP_Text x = Label(closeRect, "Label", "×", 28, true);
        Place(x.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); x.alignment = TextAlignmentOptions.Center;
        close.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(close.onClick, root.GetComponent<CampaignSelectionManager>().CloseSelection);
    }
    static void CampaignButton(RectTransform r)
    {
        Hide(r, "DarkCloud");
        ButtonStyle(r.GetComponent<Button>());
        if (r.TryGetComponent<LayoutElement>(out var layout)) { layout.preferredHeight = 186; layout.minHeight = 186; }
        var title = r.Find("Title")?.GetComponent<TMP_Text>();
        Text(title, 28, false, Gold);
        if (title != null) { Place(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(153, 111), new Vector2(-20, -20)); title.alignment = TextAlignmentOptions.TopLeft; }
        var sub = r.Find("Subtitle")?.GetComponent<TMP_Text>();
        Text(sub, 20, true);
        if (sub != null) { Place(sub.rectTransform, Vector2.zero, Vector2.one, new Vector2(153, 22), new Vector2(-28, -79)); sub.alignment = TextAlignmentOptions.TopLeft; sub.overflowMode = TextOverflowModes.Ellipsis; sub.lineSpacing = 4; }
        var token = Rect(r, "Token");
        if (token != null) Box(token, new Vector2(0, 0.5f), new Vector2(77, 0), new Vector2(122, 145));
        RectTransform artworkRect = Child(r, "Polish Artwork");
        Box(artworkRect, new Vector2(0, 0.5f), new Vector2(78, 0), new Vector2(122, 138));
        Image artwork = artworkRect.GetComponent<Image>() ?? artworkRect.gameObject.AddComponent<Image>();
        artwork.raycastTarget = false; artwork.preserveAspect = true;
        Transform oldToken = r.Find("TokenCardMasked");
        if (oldToken != null)
        {
            Image oldArt = oldToken.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "TokenedImage");
            if (oldArt != null) artwork.sprite = oldArt.sprite;
            oldToken.gameObject.SetActive(false);
        }
        else
        {
            artwork.enabled = false;
            if (token != null) token.gameObject.SetActive(false);
        }
    }

    static void Leaders(LeaderSelector leader)
    {
        if (leader.startGameButton == null)
            leader.startGameButton = leader.GetComponentsInChildren<Button>(true).First(b => b.name == "StartGame");
        var canvas = leader.GetComponent<CanvasScaler>();
        canvas.referenceResolution = new Vector2(1920, 1080); canvas.matchWidthOrHeight = 0.5f;
        Transform screen = leader.leaderSelectionFullScreen.transform;
        TMP_Text title = screen.Find("SelectALeader").GetComponent<TMP_Text>();
        Text(title, 32, false, Gold); title.text = "Choose your leader";
        Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(40, -68), new Vector2(-40, -18));
        title.alignment = TextAlignmentOptions.Center;
        TMP_Text help = Label(screen, "Polish Navigation Hint", "Scroll to explore leaders  ·  Each variant brings a different deck and destiny", 16, true);
        Place(help.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(40, -102), new Vector2(-40, -68));
        help.alignment = TextAlignmentOptions.Center;
        RectTransform carousel = (RectTransform)leader.leaderCarousel.transform;
        Place(carousel, new Vector2(0, 0.43f), new Vector2(1, 0.91f), Vector2.zero, Vector2.zero);
        var settings = new SerializedObject(leader.leaderCarousel);
        settings.FindProperty("centerPosition").vector2Value = new Vector2(0, -12);
        settings.FindProperty("selectedScale").vector3Value = Vector3.one * 1.18f;
        settings.FindProperty("sideScale").vector3Value = Vector3.one * 0.86f;
        settings.FindProperty("secondSideScale").vector3Value = Vector3.one * 0.66f;
        settings.FindProperty("thirdSideScale").vector3Value = Vector3.one * 0.48f;
        settings.FindProperty("previousPosition").vector2Value = new Vector2(-340, 0);
        settings.FindProperty("nextPosition").vector2Value = new Vector2(340, 0);
        settings.FindProperty("previousSecondPosition").vector2Value = new Vector2(-570, 0);
        settings.FindProperty("nextSecondPosition").vector2Value = new Vector2(570, 0);
        settings.FindProperty("previousThirdPosition").vector2Value = new Vector2(-750, 0);
        settings.FindProperty("nextThirdPosition").vector2Value = new Vector2(750, 0);
        settings.FindProperty("hoverNavigateInterval").floatValue = 0.8f;
        settings.FindProperty("sideTiltAngle").floatValue = 4;
        settings.FindProperty("secondTiltAngle").floatValue = 7;
        settings.FindProperty("thirdTiltAngle").floatValue = 10;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Transform info = screen.Find("LeaderTextBackground");
        Place((RectTransform)info, new Vector2(0.05f, 0.11f), new Vector2(0.95f, 0.36f), Vector2.zero, Vector2.zero);
        QuietImage(info); Panel(info); Reveal(info);
        Place(leader.textUI.rectTransform, new Vector2(0, 0.22f), new Vector2(0.44f, 1), new Vector2(26, 13), new Vector2(-22, -24));
        Place(leader.variantTextUI.rectTransform, new Vector2(0.56f, 0.22f), Vector2.one, new Vector2(22, 13), new Vector2(-26, -24));
        foreach (var t in new[] { leader.textUI, leader.variantTextUI }) { Text(t, 19, true); t.alignment = TextAlignmentOptions.TopLeft; t.lineSpacing = 3; }
        Place(leader.deckTextUI.rectTransform, Vector2.zero, new Vector2(1, 0.22f), new Vector2(26, 9), new Vector2(-26, -5));
        Text(leader.deckTextUI, 17, true); leader.deckTextUI.alignment = TextAlignmentOptions.MidlineLeft;
        if (leader.bannerImage != null) Box(leader.bannerImage.rectTransform, new Vector2(0.5f, 0.60f), Vector2.zero, new Vector2(104, 190));
        Box((RectTransform)leader.startGameButton.transform, new Vector2(1, 0), new Vector2(-211, 61), new Vector2(230, 56));
        ButtonStyle(leader.startGameButton, true);
        TMP_Text start = leader.startGameButton.GetComponentInChildren<TMP_Text>(true);
        Text(start, 28, false, Gold); start.text = "Begin campaign";
        Place(start.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4));
    }

    static void Hud(Transform root)
    {
        RectTransform resources = Rect(root, "Top/StoresBG");
        QuietImage(resources); Panel(resources);
        Box(resources, new Vector2(0, 1), new Vector2(342, -26), new Vector2(620, 100));
        Hide(resources, "GameObject"); Hide(resources, "Separator");
        TMP_Text title = resources.Find("Text").GetComponent<TMP_Text>();
        Text(title, 17, true, Gold); title.text = "YOUR RESOURCES"; title.characterSpacing = 2;
        Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(20, -32), new Vector2(-20, -7));
        var stores = resources.Find("Stores").GetComponent<StoresManager>();
        Box((RectTransform)stores.transform, new Vector2(0.5f, 0), new Vector2(0, 32), new Vector2(584, 43));
        var grid = stores.GetComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(35, 40); grid.spacing = new Vector2(6, 0);
        grid.padding = new RectOffset(5, 0, 0, 0);
        foreach (var t in new[] { stores.leatherAmount, stores.mountsAmount, stores.timberAmount, stores.ironAmount, stores.steelAmount, stores.mithrilAmount, stores.goldAmount }) Text(t, 24, true);
        var values = new SerializedObject(stores);
        values.FindProperty("gainPulseScaleMultiplier").floatValue = 1.14f;
        values.FindProperty("gainImagePulseScaleMultiplier").floatValue = 1.17f;
        values.FindProperty("gainPulseColor").colorValue = new Color(0.9f, 0.79f, 0.47f);
        values.FindProperty("gainImagePulseColor").colorValue = new Color(1, 0.94f, 0.70f);
        values.ApplyModifiedPropertiesWithoutUndo();
        // Keep end turn independently reachable when the minimap is collapsed.
        Game game = Object.FindFirstObjectByType<Game>(FindObjectsInactive.Include);
        Button endTurn = game.nextTurnButton;
        if (endTurn != null)
        {
            Transform bottom = root.Find("Bottom");
            if (endTurn.transform.parent != bottom)
            {
                // A prefab child cannot be reparented. Author a standalone copy and wire it.
                Button original = endTurn;
                endTurn = Object.Instantiate(original.gameObject, bottom, false).GetComponent<Button>();
                endTurn.name = "EndTurn";
                original.gameObject.SetActive(false);
                game.nextTurnButton = endTurn;
                EditorUtility.SetDirty(game);
            }
            Box((RectTransform)endTurn.transform, new Vector2(1, 0), new Vector2(-203, 52), new Vector2(330, 68));
            endTurn.gameObject.SetActive(true); ButtonStyle(endTurn, true);
            TMP_Text label = endTurn.GetComponentInChildren<TMP_Text>(true); Text(label, 30, false, Gold); label.text = "End turn";
            label.gameObject.SetActive(true); label.enabled = true;
            label.alignment = TextAlignmentOptions.Center;
            Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4));
        }
        RectTransform selected = Rect(root, "Bottom/SelectedCharacterIcon");
        selected.anchoredPosition = new Vector2(100, 24);
        var selectedIcon = selected.GetComponent<SelectedCharacterIcon>();
        Text(selectedIcon.nameWidget, 28, false, Gold);
        Text(selectedIcon.descriptionWidget, 18, false);
        TMP_Text hint = Label(root.Find("Bottom"), "Polish Board Controls", "SCROLL  Zoom     MIDDLE DRAG  Pan     ESC  Menu", 17, true);
        Box(hint.rectTransform, new Vector2(0.53f, 0), new Vector2(0, 29), new Vector2(620, 32));
        hint.alignment = TextAlignmentOptions.Center; hint.color = new Color(0.64f, 0.66f, 0.59f);
    }

    static void Pause(Transform root)
    {
        Transform screen = root.Find("PauseScreen");
        Image veil = screen.Find("Pause Veil").GetComponent<Image>();
        veil.color = new Color(0.012f, 0.022f, 0.020f, 0.91f);
        veil.material = null;
        if (veil.GetComponent<ImageUnaffectedBySkin>() == null) veil.gameObject.AddComponent<ImageUnaffectedBySkin>();
        RectTransform bar = Rect(screen, "Pause Menu Bar");
        Hide(screen, "Fade"); QuietImage(bar);
        Box(bar, new Vector2(0.24f, 0.47f), Vector2.zero, new Vector2(620, 690));
        Panel(bar); Reveal(bar);
        if (bar.TryGetComponent<VerticalLayoutGroup>(out var group))
        {
            group.padding = new RectOffset(36, 36, 24, 28); group.spacing = 12;
            group.childControlHeight = true; group.childForceExpandHeight = false;
        }
        foreach (TMP_Text t in bar.GetComponentsInChildren<TMP_Text>(true)) Text(t, t.name == "Title" ? 36 : 27, false);
        foreach (Button b in bar.GetComponentsInChildren<Button>(true))
        {
            var item = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
            item.minHeight = 72; item.preferredHeight = 72; item.flexibleHeight = 0;
            ButtonStyle(b);
            TMP_Text label = b.GetComponentInChildren<TMP_Text>(true);
            if (label != null) { Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(18, 4), new Vector2(-18, -4)); label.alignment = TextAlignmentOptions.Center; }
        }
        TMP_Text footer = screen.Find("Footer").GetComponent<TMP_Text>();
        Text(footer, 22, true); footer.text = "Press ESC to return to your journey";
        Box(footer.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -76), new Vector2(780, 40));
        RectTransform config = Rect(screen, "Configuration");
        Box(config, new Vector2(0.66f, 0.47f), Vector2.zero, new Vector2(640, 690));
        QuietImage(config); Panel(config);
        if (config.TryGetComponent<VerticalLayoutGroup>(out var configLayout)) { configLayout.padding = new RectOffset(40, 40, 30, 30); configLayout.spacing = 15; }
        foreach (TMP_Text t in config.GetComponentsInChildren<TMP_Text>(true)) Text(t, t.name == "Title" ? 28 : 22, true);
        foreach (Button b in config.GetComponentsInChildren<Button>(true)) ButtonStyle(b);
        foreach (Slider slider in config.GetComponentsInChildren<Slider>(true))
        {
            foreach (Image image in slider.GetComponentsInChildren<Image>(true))
            {
                image.color = image.transform == slider.transform ? new Color(0.14f, 0.17f, 0.14f) : Gold;
                image.material = null;
                if (image.GetComponent<ImageUnaffectedBySkin>() == null) image.gameObject.AddComponent<ImageUnaffectedBySkin>();
            }
        }
    }

    static void Atmosphere()
    {
        Material fog = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Fog.mat");
        Undo.RecordObject(fog, "Calm painted fog");
        fog.SetFloat("_SourceInfluence", 0.08f); fog.SetFloat("_Brightness", 0.55f);
        fog.SetFloat("_Saturation", 0.42f); fog.SetFloat("_Scale", 3.2f);
        fog.SetFloat("_SpeedX", 0.012f); fog.SetFloat("_SpeedY", 0.006f);
        fog.SetFloat("_Distortion", 0.40f); fog.SetFloat("_Density", 0.48f);
        fog.SetFloat("_Contrast", 0.72f); fog.SetFloat("_PaintBands", 12);
        fog.SetFloat("_BrushGrain", 0f); fog.SetFloat("_Vignette", 0.35f);
        fog.SetColor("_ShadowColor", new Color(0.018f, 0.03f, 0.028f));
        fog.SetColor("_FogColor", new Color(0.095f, 0.135f, 0.12f));
        fog.SetColor("_HighlightColor", new Color(0.21f, 0.24f, 0.19f));
        EditorUtility.SetDirty(fog);
    }
}
