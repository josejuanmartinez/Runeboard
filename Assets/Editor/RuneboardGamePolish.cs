using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static RuneboardPolish;

/// <summary>Authors the board HUD and reusable dialogs through Unity's serialization APIs.</summary>
public static class RuneboardGamePolish
{
    static readonly Color Ink = new(0.045f, 0.055f, 0.063f, 0.98f);
    static readonly Color Gold = new(0.79f, 0.64f, 0.40f);
    static readonly Color Paper = new(0.91f, 0.89f, 0.81f);
    static RectTransform R(Transform t, string path) => t.Find(path) as RectTransform;
    static Transform Root(string name) => UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().First(r => r.name == name).transform;
    static void Stretch(RectTransform r) => Place(r, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    static void Surface(Transform t)
    {
        QuietImage(t);
        var p = Panel(t); p.topColor = new Color(0.105f, 0.12f, 0.13f, 0.98f);
        p.bottomColor = Ink; p.edgeColor = new Color(Gold.r, Gold.g, Gold.b, 0.65f); p.corner = 5; p.SetAllDirty();
    }
    static void Flat(Image image, Color color)
    {
        if (image == null) return;
        image.sprite = null; image.material = null; image.color = color; image.enabled = true;
        if (image.GetComponent<ImageUnaffectedBySkin>() == null) image.gameObject.AddComponent<ImageUnaffectedBySkin>();
    }
    static void Body(TMP_Text text, float size, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        Text(text, size, true, Paper); text.alignment = alignment; text.lineSpacing = 5;
        text.textWrappingMode = TextWrappingModes.Normal;
    }
    static void Heading(TMP_Text text, float size)
    {
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Default.asset");
        Text(text, size, false, Gold); text.alignment = TextAlignmentOptions.MidlineLeft;
    }
    static T Ref<T>(Object o, string name) where T : Object => (T)new SerializedObject(o).FindProperty(name).objectReferenceValue;
    static void Assign(Object o, string field, Object value)
    {
        var so = new SerializedObject(o); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void SavePrefab(string path, Action<GameObject> author)
    {
        var go = PrefabUtility.LoadPrefabContents(path);
        try { author(go); foreach (var c in go.GetComponentsInChildren<Component>(true)) if (c != null) EditorUtility.SetDirty(c); PrefabUtility.SaveAsPrefabAsset(go, path); }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Author in Edit mode.");
        SavePrefab("Assets/GameObjects/SelectedCharacterIcon.prefab", go => CharacterPanel(go.GetComponent<SelectedCharacterIcon>()));
        SavePrefab("Assets/GameObjects/PopupManager.prefab", go => Narrative(go.GetComponent<PopupManager>()));
        SavePrefab("Assets/GameObjects/HexParts/HexMovementCost.prefab", Movement);
        BoardHud(Root("Layout"));
        CharacterPanel(Root("Layout").GetComponentInChildren<SelectedCharacterIcon>(true));
        Narrative(Root("PopupManager").GetComponent<PopupManager>());
        Confirm(Root("YesNoManager").GetComponent<ConfirmationDialog>());
        Choice(Root("SelectionManager").GetComponent<SelectionDialog>());
        foreach (var tutorial in Root("TutorialInstructionsManager").GetComponentsInChildren<TutorialInstructionPopup>(true)) Tutorial(tutorial);
        TurnAndMessages();
        Terrain();
        // Do this after prefab saves, which can refresh nested instance overrides.
        foreach(var name in new[]{"PopupManager","YesNoManager","SelectionManager","TutorialInstructionsManager"})
            foreach(var canvas in Root(name).GetComponentsInChildren<Canvas>(true))
            {
                canvas.overrideSorting = true; canvas.sortingOrder = 12000;
                EditorUtility.SetDirty(canvas);
                if(PrefabUtility.IsPartOfPrefabInstance(canvas))PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
            }
        foreach(var preview in Root("SelectionManager").GetComponentsInChildren<OptionButtonPrefabManager>(true))
        {
            preview.gameObject.SetActive(false);
            if(PrefabUtility.IsPartOfPrefabInstance(preview.gameObject))PrefabUtility.RecordPrefabInstancePropertyModifications(preview.gameObject);
        }
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component != null)
                {
                    EditorUtility.SetDirty(component);
                    if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    if (component is Transform transform)
                    {
                        EditorUtility.SetDirty(transform.gameObject);
                        if (PrefabUtility.IsPartOfPrefabInstance(transform.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
                    }
                }
    }
    static void BoardHud(Transform root)
    {
        // The old scaled Bottom canvas pushed right-anchored controls beyond the viewport.
        R(root, "Bottom").localScale = Vector3.one;
        var bottom = root.Find("Bottom");
        var game = Object.FindFirstObjectByType<Game>(FindObjectsInactive.Include);
        Button end = game.nextTurnButton;
        Box((RectTransform)end.transform, new Vector2(1, 0), new Vector2(-190, 68), new Vector2(316, 80));
        end.gameObject.SetActive(true); end.enabled = true; ButtonStyle(end, true);
        foreach (Canvas canvas in end.GetComponentsInChildren<Canvas>(true)) canvas.overrideSorting = false;
        var label = end.GetComponentInChildren<TMP_Text>(true); Body(label, 29, TextAlignmentOptions.Center); Stretch(label.rectTransform);
        if (end.GetComponent<CanvasGroup>() == null) end.gameObject.AddComponent<CanvasGroup>();
        var state = end.GetComponent<EndTurnPresentation>() ?? end.gameObject.AddComponent<EndTurnPresentation>(); state.label = label;
        label.text = "End turn";
        end.onClick = new Button.ButtonClickedEvent(); UnityEventTools.AddPersistentListener(end.onClick, game.NextPlayer);
        var old = root.Find("Bottom/Minimap/MinimapBg/NextTurn"); if (old != null) old.gameObject.SetActive(false);
        Hide(bottom, "Polish Board Controls");

        var turn = R(root, "Top/HexTurnDarkCloud"); Surface(turn); Hide(turn, "Separators");
        Box(turn, new Vector2(1, 1), new Vector2(-232, -40), new Vector2(400, 124));
        var turnText = turn.Find("TurnNumberManager/Text").GetComponent<TMP_Text>(); Body(turnText, 30, TextAlignmentOptions.MidlineLeft);
        Box(R(turn, "TurnNumberManager"), new Vector2(0, 1), new Vector2(100, -30), new Vector2(155, 35)); Stretch(turnText.rectTransform);
        var coordinates = turn.Find("HexNumberManager/Text").GetComponent<TMP_Text>(); Body(coordinates, 20, TextAlignmentOptions.MidlineRight);
        Box(R(turn, "HexNumberManager"), Vector2.one, new Vector2(-93, -30), new Vector2(146, 30)); Stretch(coordinates.rectTransform); coordinates.color = Gold;
        Box(R(turn, "DateManager"), new Vector2(0.5f, 0), new Vector2(0, 32), new Vector2(354, 32));
        var date = turn.Find("DateManager/Text").GetComponent<TMP_Text>(); Body(date, 22, TextAlignmentOptions.MidlineLeft); Stretch(date.rectTransform);
        var info = R(turn, "HexInfoBg"); Surface(info);
        Box(info, new Vector2(0.5f, 0), new Vector2(0, -186), new Vector2(400, 338));
        Place(R(info, "HexInfo"), Vector2.zero, Vector2.one, new Vector2(24, 40), new Vector2(-24, -23));
        Body(info.Find("HexInfo").GetComponent<TMP_Text>(), 22);
        Assign(info.GetComponentInChildren<HexUIHover>(true), "panelRoot", info);
        Box(R(info, "Tooltip"), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(350, 25));
        Body(info.Find("Tooltip").GetComponent<TMP_Text>(), 16);

        var minimap = R(bottom, "Minimap"); Surface(minimap);
        Box(minimap, new Vector2(1, 0), new Vector2(-190, 290), new Vector2(316, 328));
        var map = R(minimap, "MinimapBg"); Stretch(map); QuietImage(map);
        Hide(map, "Fog"); Hide(minimap, "MinimapSide");
        Box(R(map, "Minimap"), new Vector2(0.5f, 0.5f), new Vector2(0, -8), new Vector2(276, 228));
        Box(R(map, "Banner"), new Vector2(1, 1), new Vector2(-31, -34), new Vector2(28, 46));
        var nation = R(map, "NationColorBorder"); Box(nation, new Vector2(0, 1), new Vector2(125, -32), new Vector2(220, 35));
        foreach (var t in nation.GetComponentsInChildren<TMP_Text>(true)) { Body(t, 22, TextAlignmentOptions.MidlineLeft); Stretch(t.rectTransform); }
        foreach (string tab in new[] { "CollapseTab", "ShowTab" })
        {
            var t = R(minimap, tab); Box(t, new Vector2(0, 0.5f), new Vector2(-22, 0), new Vector2(36, 62));
            ButtonStyle(t.GetComponent<Button>()); var text = t.GetComponentInChildren<TMP_Text>(true); Body(text, 24, TextAlignmentOptions.Center); Stretch(text.rectTransform);
        }
        // Keep the map-options controls available in a compact rail above the map.
        var side = R(minimap, "MinimapSide"); side.gameObject.SetActive(true); QuietImage(side);
        foreach (var canvas in minimap.GetComponentsInChildren<Canvas>(true)) canvas.overrideSorting = false;
        var collapse = R(minimap,"CollapseTab").GetComponent<Button>();
        var expand = R(minimap,"ShowTab").GetComponent<Button>();
        var mapSurface = minimap.Find("Polish Surface").gameObject;
        for(int i=collapse.onClick.GetPersistentEventCount()-1;i>=0;i--)if(collapse.onClick.GetPersistentTarget(i)==mapSurface)UnityEventTools.RemovePersistentListener(collapse.onClick,i);
        for(int i=expand.onClick.GetPersistentEventCount()-1;i>=0;i--)if(expand.onClick.GetPersistentTarget(i)==mapSurface)UnityEventTools.RemovePersistentListener(expand.onClick,i);
        UnityEventTools.AddBoolPersistentListener(collapse.onClick, mapSurface.SetActive, false);
        UnityEventTools.AddBoolPersistentListener(expand.onClick, mapSurface.SetActive, true);
        Box(side, new Vector2(0.5f, 1), new Vector2(0, 53), new Vector2(316, 82));
        var vertical = R(side, "Vertical"); Stretch(vertical); Surface(vertical);
        string[] options = { "RegionsButton", "GridButton", "HintButton", "TutorialButton", "SkinButton" };
        for (int i = 0; i < options.Length; i++)
        {
            var option = R(vertical, options[i]); if (option == null) continue;
            Box(option, new Vector2(0, 1), new Vector2(i < 3 ? 52 + i * 103 : 75 + (i - 3) * 157, i < 3 ? -22 : -60), new Vector2(i < 3 ? 94 : 140, 28));
            foreach (var t in option.GetComponentsInChildren<TMP_Text>(true)) Body(t, 16, TextAlignmentOptions.Center);
            foreach (var icon in option.GetComponentsInChildren<Image>(true)) if (icon.name.EndsWith("Icon")) icon.gameObject.SetActive(false);
        }
        var resources = R(root, "Top/StoresBG"); Surface(resources);
        var events = R(root,"Right/Events");
        events.anchoredPosition = new Vector2(-448, events.anchoredPosition.y);
        foreach (var leader in root.GetComponentsInChildren<PlayableLeaderIcon>(true)) Hide(leader.transform, "Clouds");
    }
    static void CharacterPanel(SelectedCharacterIcon c)
    {
        Transform t = c.transform;
        Box((RectTransform)t, Vector2.zero, new Vector2(436, 212), new Vector2(808, 364)); Surface(t);
        foreach (string name in new[] { "DarkClouds", "LeaderBorderFog", "ArtifactsIcon", "StatusIcon", "Line" }) Hide(t, name);
        foreach (var canvas in t.GetComponentsInChildren<Canvas>(true)) canvas.overrideSorting = false;
        QuietImage(c.border.transform);
        var mask = R(t, "Mask"); Box(mask, new Vector2(0, 1), new Vector2(105, -143), new Vector2(164, 218));
        Flat(mask.GetComponent<Image>(), Color.white); mask.GetComponent<Mask>().showMaskGraphic = false;
        Stretch(c.icon.rectTransform); c.icon.preserveAspect = false;
        Box(R(t, "NameBg"), new Vector2(0, 1), new Vector2(485, -40), new Vector2(560, 43));
        Stretch(c.nameWidget.rectTransform); Heading(c.nameWidget, 31);
        Box(R(t, "DescriptionBg"), new Vector2(0, 1), new Vector2(449, -116), new Vector2(490, 88));
        Stretch(c.descriptionWidget.rectTransform); Body(c.descriptionWidget, 21); c.descriptionWidget.overflowMode = TextOverflowModes.Ellipsis;
        Box(c.animatedCharacter.rectTransform, new Vector2(1, 1), new Vector2(-62, -153), new Vector2(88, 144)); c.animatedCharacter.preserveAspect = true;
        Box((RectTransform)c.artifactStatusGridLayoutTransform, new Vector2(0, 1), new Vector2(445, -226), new Vector2(480, 54));
        var artifacts = c.artifactStatusGridLayoutTransform.GetComponent<GridLayoutGroup>();
        if (artifacts != null) { artifacts.cellSize = new Vector2(44, 44); artifacts.spacing = new Vector2(8, 0); artifacts.constraint = GridLayoutGroup.Constraint.FixedRowCount; artifacts.constraintCount = 1; }
        var levels = (RectTransform)c.levelsGameObject.transform;
        Place(levels, Vector2.zero, new Vector2(1, 0), new Vector2(22, 20), new Vector2(-22, 90));
        QuietImage(levels); var grid = levels.GetComponent<GridLayoutGroup>(); if (grid != null) grid.enabled = false;
        string[] icons = { "CommanderBackground", "AgentBackground", "EmmissaryBackground", "MageBackground", "MovementIcon" };
        string[] captions = { "COMMAND", "AGENT", "ENVOY", "MAGIC", "MOVE LEFT" };
        TMP_Text[] values = { c.commander, c.agent, c.emmissary, c.mage, c.movementLeft };
        for (int i = 0; i < 5; i++)
        {
            float x = 32 + i * 139;
            Box(R(levels, icons[i]), new Vector2(0, 0.5f), new Vector2(x, -9), new Vector2(29, 29));
            Box(values[i].rectTransform, new Vector2(0, 0.5f), new Vector2(x + 42, -9), new Vector2(47, 36)); Body(values[i], 28, TextAlignmentOptions.Center);
            var cap = Label(levels, "Polish " + captions[i], captions[i], 14, true);
            Box(cap.rectTransform, new Vector2(0, 1), new Vector2(x + 30, -8), new Vector2(122, 23)); cap.alignment = TextAlignmentOptions.Center; cap.color = Gold;
        }
        foreach (var g in new[] { c.actionedIcon, c.unactionedIcon })
        {
            Box((RectTransform)g.transform, new Vector2(1, 0.5f), new Vector2(-30, -6), new Vector2(70, 32));
            foreach(var image in g.GetComponentsInChildren<Image>(true))image.enabled=false;
            var status=Label(g.transform,"Polish Status",g==c.actionedIcon?"DONE":"READY",13,true);Stretch(status.rectTransform);status.alignment=TextAlignmentOptions.Center;status.color=g==c.actionedIcon?Gold:new Color(.5f,.78f,.6f);
        }
        var hp = R(t, "HealthBg"); Box(hp, new Vector2(0, 1), new Vector2(105, -264), new Vector2(164, 7)); Flat(hp.GetComponent<Image>(), new Color(.16f,.19f,.19f));
        Stretch(c.health.rectTransform); Flat(c.health, new Color(.38f,.65f,.49f)); c.health.type = Image.Type.Filled; c.health.fillMethod = Image.FillMethod.Horizontal;
        Hide(hp, "Heart");
        var others = (RectTransform)c.otherCharacters.transform; Box(others, new Vector2(0, 1), new Vector2(355, 57), new Vector2(710, 90));
        var otherGrid = others.GetComponent<GridLayoutGroup>();
        if (otherGrid != null) { otherGrid.cellSize = new Vector2(78, 78); otherGrid.spacing = new Vector2(9, 0); otherGrid.constraint = GridLayoutGroup.Constraint.FixedRowCount; otherGrid.constraintCount = 1; }
        foreach (var other in others.GetComponentsInChildren<CharacterIconWithText>(true)) Hide(other.transform, "DarkCloud");
        var gallery = R(t, "CardGallery"); if (gallery != null) Box(gallery, new Vector2(0, 1), new Vector2(300, 189), new Vector2(590, 146));
        var log = R(t, "LogWidget");
        if (log != null)
        {
            Box(log, Vector2.one, new Vector2(-17, -31), new Vector2(32, 32));
            Hide(log, "SpeechIcon ");
            foreach (string name in new[] { "CollapseTab", "ShowTab" })
            {
                var tab = R(log, name); Stretch(tab); ButtonStyle(tab.GetComponent<Button>());
                var text = Label(tab, "Polish Label", "...", 22, true); Stretch(text.rectTransform); text.alignment = TextAlignmentOptions.Center;
            }
            Box(R(log, "Bar"), new Vector2(0, 1), new Vector2(-340, 185), new Vector2(680, 280));
        }
    }
    static void ModalCanvas(Transform content)
    {
        var canvas = content.GetComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = 12000;
        foreach (var nested in content.GetComponentsInChildren<Canvas>(true)) if (nested != canvas) nested.overrideSorting = false;
        var scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
        Flat(content.GetComponent<Image>(), new Color(0.018f, 0.025f, 0.035f, 0.83f));
    }
    static void ModalButton(Button b, string label, bool primary = false)
    {
        ButtonStyle(b, primary);
        TMP_Text text = b.GetComponentInChildren<TMP_Text>(true);
        if (text == null) text = Label(b.transform, "Polish Label", label, 19, true);
        if (label != null) text.text = label;
        Body(text, 19, TextAlignmentOptions.Center); Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 6), new Vector2(-12, -6));
    }
    static void Confirm(ConfirmationDialog dialog)
    {
        var content = Ref<GameObject>(dialog, "content").transform; ModalCanvas(content);
        var panel = R(content, "Image"); Box(panel, new Vector2(.5f,.5f), Vector2.zero, new Vector2(600, 280)); Surface(panel);
        Hide(panel, "Border"); Hide(panel, "Border (1)");
        var message = Ref<TMP_Text>(dialog, "messageLabel"); Body(message, 23, TextAlignmentOptions.Center);
        Place(message.rectTransform, Vector2.zero, Vector2.one, new Vector2(44, 114), new Vector2(-44, -47));
        var yes = Ref<Button>(dialog, "yesButton"); var no = Ref<Button>(dialog, "noButton");
        Box((RectTransform)yes.transform, new Vector2(.5f,0), new Vector2(-133, 55), new Vector2(234, 54)); ModalButton(yes, "Continue", true);
        Box((RectTransform)no.transform, new Vector2(.5f,0), new Vector2(133, 55), new Vector2(234, 54)); ModalButton(no, "Cancel");
        for (int i=0;i<2;i++) { var b=Ref<Button>(dialog,i==0?"previousButton":"nextButton"); Box((RectTransform)b.transform,new Vector2(.5f,.5f),new Vector2(i==0?-332:332,0),new Vector2(40,50)); ModalButton(b,i==0?"<":">"); }
    }
    static void Narrative(PopupManager popup)
    {
        var content = popup.transform.Find("Content"); ModalCanvas(content);
        // PopupManager's root owns its scaler; avoid scaling this canvas twice.
        var rootScaler = popup.GetComponent<CanvasScaler>(); if(rootScaler!=null){rootScaler.referenceResolution=new Vector2(1280,720);rootScaler.matchWidthOrHeight=.5f;}
        var panel = (RectTransform)popup.container.transform; Box(panel,new Vector2(.5f,.5f),Vector2.zero,new Vector2(900,570)); Surface(panel);
        popup.referenceHeight = 570;
        popup.useFramedLayout = true;
        Hide(panel,"Border"); QuietImage(panel.Find("TitleBg"));
        Place(R(panel,"TitleBg"),new Vector2(0,1),Vector2.one,new Vector2(35,-87),new Vector2(-80,-26));
        Stretch(popup.titleWidget.rectTransform); Heading(popup.titleWidget,30);
        var actors=R(panel,"GridLayout"); Place(actors,Vector2.zero,Vector2.one,new Vector2(32,78),new Vector2(-646,-110));
        var grid=actors.GetComponent<GridLayoutGroup>(); grid.cellSize=new Vector2(204,174);grid.spacing=new Vector2(0,20);grid.padding=new RectOffset();grid.childAlignment=TextAnchor.UpperCenter;grid.startCorner=GridLayoutGroup.Corner.UpperLeft;grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=1;
        foreach(var actor in new[]{popup.actor1,popup.actor2})
        {
            var mask=actor.transform.parent; Flat(mask.GetComponent<Image>(),Color.white); mask.GetComponent<Mask>().showMaskGraphic=false; Stretch(actor.rectTransform);actor.preserveAspect=true;
        }
        var scroll=R(panel,"ScrollableText"); Place(scroll,Vector2.zero,Vector2.one,new Vector2(281,81),new Vector2(-35,-115));QuietImage(scroll);
        var auto=scroll.GetComponent<AutoScroll>();if(auto!=null)auto.enabled=false;
        var sr=scroll.GetComponent<ScrollRect>();sr.horizontal=false;sr.vertical=true;sr.scrollSensitivity=25;sr.movementType=ScrollRect.MovementType.Clamped;
        Hide(scroll,"Scrollbar Horizontal");
        var viewport=R(scroll,"Viewport"); Place(viewport,Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-15,0));
        Flat(viewport.GetComponent<Image>(),Color.white);viewport.GetComponent<Mask>().showMaskGraphic=false;
        Body(popup.textWidget,20);popup.textWidget.enableAutoSizing=false;
        popup.textWidget.overflowMode=TextOverflowModes.Overflow;
        var textRect=popup.textWidget.rectTransform; textRect.localScale=Vector3.one;textRect.anchorMin=new Vector2(0,1);textRect.anchorMax=Vector2.one;textRect.pivot=new Vector2(.5f,1);textRect.anchoredPosition=Vector2.zero;textRect.sizeDelta=new Vector2(-12,0);
        var fitter=textRect.GetComponent<ContentSizeFitter>()??textRect.gameObject.AddComponent<ContentSizeFitter>();fitter.horizontalFit=ContentSizeFitter.FitMode.Unconstrained;fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        sr.content=textRect;
        foreach(var image in scroll.GetComponentsInChildren<Image>(true))if(image.transform!=viewport && image.transform!=scroll){image.material=null;image.color=image.name=="Handle"?Gold:new Color(.15f,.18f,.2f);}
        var close=panel.Find("Close").GetComponent<Button>();Box((RectTransform)close.transform,Vector2.one,new Vector2(-33,-33),new Vector2(38,38));ModalButton(close,"×");
        var done=Child(panel,"Polish Continue");Box(done,new Vector2(1,0),new Vector2(-130,38),new Vector2(190,44));
        var button=done.GetComponent<Button>()??done.gameObject.AddComponent<Button>();ModalButton(button,"Continue",true);button.onClick=new Button.ButtonClickedEvent();UnityEventTools.AddPersistentListener(button.onClick,popup.Hide);
        var arrows=new[]{popup.leftArrow,popup.rightArrow};for(int i=0;i<2;i++){Box((RectTransform)arrows[i].transform,new Vector2(0,0),new Vector2(52+i*55,38),new Vector2(40,40));ModalButton(arrows[i].GetComponent<Button>(),i==0?"<":">");}
    }
    static void Choice(SelectionDialog dialog)
    {
        var content=Ref<GameObject>(dialog,"content").transform;ModalCanvas(content);
        var panel=R(content,"Image");Box(panel,new Vector2(.5f,.5f),Vector2.zero,new Vector2(920,594));Surface(panel);
        foreach(var preview in panel.GetComponentsInChildren<OptionButtonPrefabManager>(true)) preview.gameObject.SetActive(false);
        foreach(string name in new[]{"Scroll","Scroll (1)","Scroll (2)","Scroll (3)"})Hide(panel,name);
        var artRoot=Ref<CanvasGroup>(dialog,"portraitCanvasGroup").transform;Stretch((RectTransform)artRoot);QuietImage(artRoot);Hide(artRoot,"Frame");
        var scroll=R(artRoot,"Scroll");Stretch(scroll);QuietImage(scroll);
        var title=Ref<TMP_Text>(dialog,"title");QuietImage(title.transform.parent);Place((RectTransform)title.transform.parent,new Vector2(0,1),Vector2.one,new Vector2(35,-81),new Vector2(-80,-24));Stretch(title.rectTransform);Body(title,27,TextAlignmentOptions.MidlineLeft);title.color=Gold;
        var message=Ref<TMP_Text>(dialog,"messageLabel");Place(message.rectTransform,Vector2.zero,Vector2.one,new Vector2(304,365),new Vector2(-38,-107));Body(message,19);
        var portrait=Ref<Image>(dialog,"portraitImage");var mask=portrait.transform.parent;Box((RectTransform)mask,new Vector2(0,1),new Vector2(151,-298),new Vector2(230,376));Flat(mask.GetComponent<Image>(),Color.white);mask.GetComponent<Mask>().showMaskGraphic=false;Stretch(portrait.rectTransform);portrait.preserveAspect=true;
        var close=Ref<Button>(dialog,"noButton");Box((RectTransform)close.transform,Vector2.one,new Vector2(-33,-33),new Vector2(38,38));ModalButton(close,"×");
        var old=Ref<Transform>(dialog,"optionButtonsContainer");
        var optionsRoot=Child(panel,"Polish Options");Place(optionsRoot,Vector2.zero,Vector2.one,new Vector2(302,48),new Vector2(-33,-253));
        var sr=optionsRoot.GetComponent<ScrollRect>()??optionsRoot.gameObject.AddComponent<ScrollRect>();sr.horizontal=false;sr.vertical=true;sr.movementType=ScrollRect.MovementType.Clamped;sr.scrollSensitivity=28;
        var viewport=Child(optionsRoot,"Viewport");Stretch(viewport);if(viewport.GetComponent<RectMask2D>()==null)viewport.gameObject.AddComponent<RectMask2D>();
        var list=Child(viewport,"Content");list.anchorMin=new Vector2(0,1);list.anchorMax=Vector2.one;list.pivot=new Vector2(.5f,1);list.anchoredPosition=Vector2.zero;list.sizeDelta=Vector2.zero;
        var group=list.GetComponent<VerticalLayoutGroup>()??list.gameObject.AddComponent<VerticalLayoutGroup>();group.padding=new RectOffset(3,3,3,3);group.spacing=10;group.childControlWidth=true;group.childControlHeight=true;group.childForceExpandHeight=false;
        var fit=list.GetComponent<ContentSizeFitter>()??list.gameObject.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        sr.viewport=viewport;sr.content=list;if(old!=list)old.gameObject.SetActive(false);Assign(dialog,"optionButtonsContainer",list);
        var track=Child(optionsRoot,"Scrollbar");Place(track,new Vector2(1,0),Vector2.one,new Vector2(-7,0),Vector2.zero);
        var trackImage=track.GetComponent<Image>()??track.gameObject.AddComponent<Image>();Flat(trackImage,new Color(.12f,.14f,.15f));
        var handle=Child(track,"Handle");Stretch(handle);var handleImage=handle.GetComponent<Image>()??handle.gameObject.AddComponent<Image>();Flat(handleImage,Gold);
        var scrollbar=track.GetComponent<Scrollbar>()??track.gameObject.AddComponent<Scrollbar>();scrollbar.handleRect=handle;scrollbar.targetGraphic=handleImage;scrollbar.direction=Scrollbar.Direction.BottomToTop;
        sr.verticalScrollbar=scrollbar;sr.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        viewport.offsetMax=new Vector2(-17,0);
        var prefab=Ref<GameObject>(dialog,"optionButtonPrefab");SavePrefab(AssetDatabase.GetAssetPath(prefab),Option);
    }
    static void Option(GameObject go)
    {
        foreach(var image in go.GetComponentsInChildren<Image>(true)) if(image.name!="Icon")image.enabled=false;
        ModalButton(go.GetComponent<Button>(),null);
        var text=go.GetComponentInChildren<TMP_Text>(true);Place(text.rectTransform,Vector2.zero,Vector2.one,new Vector2(66,10),new Vector2(-20,-10));Body(text,18,TextAlignmentOptions.MidlineLeft);
        var manager=go.GetComponent<OptionButtonPrefabManager>();
        if(manager.IconGraphic!=null)
        {
            var icon=manager.IconGraphic; var holder=icon.transform.parent;
            if(holder!=go.transform){Box((RectTransform)holder,new Vector2(0,.5f),new Vector2(33,0),new Vector2(38,38));Stretch(icon.rectTransform);foreach(var decoration in holder.GetComponentsInChildren<Image>(true))if(decoration!=icon)decoration.enabled=false;}
            else Box(icon.rectTransform,new Vector2(0,.5f),new Vector2(33,0),new Vector2(38,38));
            icon.preserveAspect=true;
        }
    }
    static void Movement(GameObject go)
    {
        var manager=go.GetComponent<MovementCostManager>();
        var rootRenderer=go.GetComponent<SpriteRenderer>();if(rootRenderer!=null)rootRenderer.enabled=false;
        manager.dot.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");manager.dot.color=new Color(.08f,.11f,.14f,.98f);manager.dot.sharedMaterial=AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        manager.dot.transform.localScale=new Vector3(.38f,.30f,1);manager.dot.sortingOrder=9991;
        var text=manager.movementText;Text(text,4.5f,true);text.enableAutoSizing=false;text.color=Paper;text.alignment=TextAlignmentOptions.Center;text.textWrappingMode=TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta=new Vector2(.42f,.32f);text.transform.localPosition=new Vector3(0,0,-.01f);text.transform.localScale=Vector3.one;
        var terrain=go.transform.Find("Terrain Symbols");
        if(terrain==null){terrain=new GameObject("Terrain Symbols",typeof(RectTransform),typeof(TextMeshPro)).transform;terrain.SetParent(go.transform,false);}
        manager.terrainText=terrain.GetComponent<TextMeshPro>();Text(manager.terrainText,3.2f,true);manager.terrainText.spriteAsset=text.spriteAsset;manager.terrainText.alignment=TextAlignmentOptions.Center;manager.terrainText.textWrappingMode=TextWrappingModes.NoWrap;
        manager.terrainText.rectTransform.sizeDelta=new Vector2(.6f,.18f);terrain.localPosition=new Vector3(0,.29f,-.01f);manager.terrainText.renderer.sortingOrder=9999;
    }
    static void Tutorial(TutorialInstructionPopup tutorial)
    {
        var content=tutorial.transform.Find("Content");if(content==null)return;ModalCanvas(content);
        var panel=R(content,"Image");Box(panel,new Vector2(.5f,.5f),Vector2.zero,new Vector2(860,570));Surface(panel);
        foreach(string decoration in new[]{"Border","DarkClouds","GameObject","Separator"})Hide(panel,decoration);
        var title=R(panel,"TitleBg");QuietImage(title);Place(title,new Vector2(0,1),Vector2.one,new Vector2(36,-83),new Vector2(-80,-23));
        foreach(var text in title.GetComponentsInChildren<TMP_Text>(true)){Stretch(text.rectTransform);Heading(text,29);}
        var scroll=R(panel,"ScrollableText");Place(scroll,Vector2.zero,Vector2.one,new Vector2(42,104),new Vector2(-42,-116));QuietImage(scroll);
        var auto=scroll.GetComponent<AutoScroll>();if(auto!=null)auto.enabled=false;
        var sr=scroll.GetComponent<ScrollRect>();sr.horizontal=false;sr.vertical=true;sr.scrollSensitivity=28;sr.movementType=ScrollRect.MovementType.Clamped;
        Hide(scroll,"Scrollbar Horizontal");var viewport=R(scroll,"Viewport");Place(viewport,Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-16,0));
        Flat(viewport.GetComponent<Image>(),Color.white);var mask=viewport.GetComponent<Mask>();if(mask!=null)mask.showMaskGraphic=false;
        var textBody=sr.content.GetComponent<TMP_Text>();Body(textBody,21);textBody.enableAutoSizing=false;textBody.overflowMode=TextOverflowModes.Overflow;
        var textRect=textBody.rectTransform;textRect.localScale=Vector3.one;textRect.anchorMin=new Vector2(0,1);textRect.anchorMax=Vector2.one;textRect.pivot=new Vector2(.5f,1);textRect.anchoredPosition=Vector2.zero;textRect.sizeDelta=new Vector2(-16,0);
        var close=panel.Find("Close").GetComponent<Button>();Box((RectTransform)close.transform,Vector2.one,new Vector2(-33,-33),new Vector2(38,38));ModalButton(close,"×");
        var done=Child(panel,"Polish Continue");Box(done,new Vector2(1,0),new Vector2(-127,39),new Vector2(182,44));var b=done.GetComponent<Button>()??done.gameObject.AddComponent<Button>();ModalButton(b,"Continue",true);b.onClick=new Button.ButtonClickedEvent();UnityEventTools.AddPersistentListener(b.onClick,tutorial.Close);
        for(int i=0;i<2;i++){var arrow=R(panel,i==0?"LeftArrow":"RightArrow");Box(arrow,Vector2.zero,new Vector2(55+i*58,39),new Vector2(42,42));ModalButton(arrow.GetComponent<Button>(),i==0?"<":">");}
        var terrain=panel.Cast<Transform>().Where(t=>t.name.StartsWith("Terrain")).ToArray();
        for(int i=0;i<terrain.Length;i++)Box((RectTransform)terrain[i],new Vector2(.5f,0),new Vector2((i-(terrain.Length-1)*.5f)*55,86),new Vector2(36,36));
    }
    static void TurnAndMessages()
    {
        var banner=Root("TurnBanner").GetComponent<TurnBanner>();
        Heading(Ref<TMP_Text>(banner,"turnText"),64);Body(Ref<TMP_Text>(banner,"dateText"),25,TextAlignmentOptions.Center);Body(Ref<TMP_Text>(banner,"infoText"),20,TextAlignmentOptions.Center);
        Box(Ref<RectTransform>(banner,"infoRect"),new Vector2(.5f,.5f),new Vector2(0,-137),new Vector2(850,72));
        foreach(string name in new[]{"leftBannerRect","rightBannerRect"}){var rect=Ref<RectTransform>(banner,name);rect.sizeDelta=new Vector2(104,210);}
        var message=Root("MessageUI").GetComponent<MessageDisplay>();var text=Ref<TMP_Text>(message,"messageText");Body(text,26,TextAlignmentOptions.Center);
        var bg=(RectTransform)text.transform.parent;Flat(bg.GetComponent<Image>(),Color.clear);Surface(bg);
        Box(bg,new Vector2(.5f,.76f),Vector2.zero,new Vector2(640,66));
        Stretch(text.rectTransform);
        var fit=bg.GetComponent<ImageFitToTMP>();var so=new SerializedObject(fit);so.FindProperty("padding").vector2Value=new Vector2(52,26);so.FindProperty("growRight").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Terrain()
    {
        // Keep blending at the seams; the previous full-radius band smears the whole tile.
        var terrain=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/HexSeamlessBlendGame.mat");
        terrain.SetFloat("_BlendBand",.20f);terrain.SetFloat("_FogFade",.16f);terrain.SetFloat("_EdgeTrim",.08f);EditorUtility.SetDirty(terrain);
        var fog=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Fog.mat");
        fog.SetColor("_ShadowColor",new Color(.042f,.051f,.065f));fog.SetColor("_FogColor",new Color(.125f,.15f,.18f));fog.SetColor("_HighlightColor",new Color(.23f,.25f,.28f));
        fog.SetFloat("_Brightness",.8f);fog.SetFloat("_Scale",2.1f);fog.SetFloat("_SpeedX",.008f);fog.SetFloat("_SpeedY",.002f);fog.SetFloat("_Density",.48f);fog.SetFloat("_Contrast",.52f);fog.SetFloat("_Vignette",.16f);fog.SetFloat("_BrushGrain",0);fog.SetFloat("_PaintBands",12);EditorUtility.SetDirty(fog);
    }
}
