using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static RuneboardPolish;

/// <summary>Repairs the reusable HUD widgets and their serialized scene overrides.</summary>
public static class RuneboardUiRepairs
{
    static readonly Color Gold=new(.78f,.65f,.43f), Paper=new(.92f,.91f,.85f), Ink=new(.065f,.08f,.095f);
    static Transform Root(string name)=>UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().First(g=>g.name==name).transform;
    static RectTransform R(Transform t,string path)=>t.Find(path) as RectTransform;
    static void Stretch(RectTransform r)=>Place(r,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
    static void Flat(Image image,Color color){if(image==null)return;image.sprite=null;image.material=null;image.color=color;image.enabled=true;if(image.GetComponent<ImageUnaffectedBySkin>()==null)image.gameObject.AddComponent<ImageUnaffectedBySkin>();}
    static void Surface(Transform t){QuietImage(t);var p=Panel(t);p.topColor=new Color(.11f,.135f,.16f,.99f);p.bottomColor=new Color(.045f,.06f,.078f,.99f);p.edgeColor=new Color(.68f,.56f,.36f,.85f);p.corner=4;p.SetAllDirty();}
    static void Body(TMP_Text text,float size){Text(text,size,true,Paper);text.alignment=TextAlignmentOptions.TopLeft;text.lineSpacing=5;text.enableAutoSizing=false;}
    static void Set(Object target,string field,Object value){var so=new SerializedObject(target);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static T Ref<T>(Object target,string field) where T:Object => (T)new SerializedObject(target).FindProperty(field).objectReferenceValue;
    static void Prefab(string path,Action<GameObject> author){var go=PrefabUtility.LoadPrefabContents(path);try{author(go);PrefabUtility.SaveAsPrefabAsset(go,path);}finally{PrefabUtility.UnloadPrefabContents(go);}}
    static void ButtonLabel(Button button,string text)
    {
        ButtonStyle(button);var label=button.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.name=="Repair Label")??Label(button.transform,"Repair Label",text,19,true);
        foreach(var old in button.GetComponentsInChildren<TMP_Text>(true))if(old!=label)old.gameObject.SetActive(false);
        label.gameObject.SetActive(true);label.text=text;Body(label,19);label.alignment=TextAlignmentOptions.Center;Stretch(label.rectTransform);
    }
    static void TogglePair(Button show,Button hide,GameObject[] contents)
    {
        show.onClick=new Button.ButtonClickedEvent();hide.onClick=new Button.ButtonClickedEvent();
        foreach(var content in contents){UnityEventTools.AddBoolPersistentListener(show.onClick,content.SetActive,true);UnityEventTools.AddBoolPersistentListener(hide.onClick,content.SetActive,false);}
        UnityEventTools.AddBoolPersistentListener(show.onClick,show.gameObject.SetActive,false);UnityEventTools.AddBoolPersistentListener(show.onClick,hide.gameObject.SetActive,true);
        UnityEventTools.AddBoolPersistentListener(hide.onClick,show.gameObject.SetActive,true);UnityEventTools.AddBoolPersistentListener(hide.onClick,hide.gameObject.SetActive,false);
    }
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Apply in Edit mode.");
        Prefab("Assets/GameObjects/Reusable/Hover.prefab",g=>StyleHover(g.GetComponent<Hover>()));
        foreach(var path in new[]{"Artifact-Status","Card","PlayableLeaderIcon","SituationCardsUI"})Prefab("Assets/GameObjects/Reusable/"+path+".prefab",g=>{foreach(var hover in g.GetComponentsInChildren<Hover>(true))StyleHover(hover);});
        Prefab("Assets/GameObjects/Reusable/EventIcon.prefab",g=>Event(g.GetComponent<EventIcon>()));
        Prefab("Assets/GameObjects/Reusable/CharacterIconWithText.prefab",g=>Portrait(g.GetComponent<CharacterIconWithText>()));
        Prefab("Assets/GameObjects/SelectedCharacterIcon.prefab",g=>Character(g.GetComponent<SelectedCharacterIcon>()));
        Prefab("Assets/GameObjects/CalendarWidgetPanel.prefab",g=>Calendar(g.GetComponent<CalendarWidget>()));
        Prefab("Assets/GameObjects/StartupLoadingScreen.prefab",g=>Loading(g.GetComponent<StartupLoadingScreen>()));
        Prefab("Assets/GameObjects/VideoPopupManager.prefab",g=>Video(g.GetComponent<VideoPopupManager>()));
        Prefab("Assets/GameObjects/HexParts/HexPcText.prefab",g=>{var fit=g.GetComponentInChildren<SpriteRendererFitToTMP>(true);var so=new SerializedObject(fit);so.FindProperty("drawBackground").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();fit.Fit();});
        var layout=Root("Layout");Character(layout.GetComponentInChildren<SelectedCharacterIcon>(true));
        Map(layout);Events(layout);Environment(layout);
        foreach(var calendar in layout.GetComponentsInChildren<CalendarWidget>(true))Calendar(calendar);
        Loading(Root("StartupLoadingScreen").GetComponent<StartupLoadingScreen>());
        Video(Root("VideoPopupManager").GetComponent<VideoPopupManager>());
        Progress(R(Root("LeaderSelection"),"Wrapper/Progress"));
        foreach(var h in Object.FindObjectsByType<Hover>(FindObjectsInactive.Include,FindObjectsSortMode.None))StyleHover(h);
        foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach(var c in root.GetComponentsInChildren<Component>(true))if(c!=null){EditorUtility.SetDirty(c);if(PrefabUtility.IsPartOfPrefabInstance(c))PrefabUtility.RecordPrefabInstancePropertyModifications(c);if(c is Transform t){EditorUtility.SetDirty(t.gameObject);if(PrefabUtility.IsPartOfPrefabInstance(t.gameObject))PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);}}
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
    }
    static void StyleHover(Hover hover)
    {
        var old=hover.textWidget;var legacy=hover.transform.Find("Tooltip")?.GetComponent<TMP_Text>();
        var source=legacy!=null&&!string.IsNullOrWhiteSpace(legacy.text)?legacy:old;
        var frame=Child(hover.transform,"Tooltip Frame");
        var label=Label(frame,"Text",hover.CreateTextWithBackground(source!=null?source.text:string.Empty),20,true);
        if(source!=null)label.spriteAsset=source.spriteAsset;
        if(old!=null&&old!=label){label.spriteAsset=old.spriteAsset;old.gameObject.SetActive(false);}
        foreach(var fit in frame.GetComponentsInChildren<ContentSizeFitter>(true))fit.enabled=false;
        Surface(frame);Body(label,20);label.margin=Vector4.zero;label.fontSharedMaterial=label.font.material;
        Place(label.rectTransform,Vector2.zero,Vector2.one,new Vector2(18,14),new Vector2(-18,-14));
        frame.anchorMin=frame.anchorMax=new Vector2(.5f,.5f);frame.pivot=new Vector2(0,1);frame.localScale=Vector3.one;
        hover.tooltipPanel=frame.gameObject;hover.textWidget=(TextMeshProUGUI)label;hover.readingSize=20;hover.maximumWidth=440;
        var canvas=hover.GetComponent<Canvas>();if(canvas!=null){canvas.overrideSorting=false;canvas.sortingOrder=0;}
        var overlay=frame.GetComponent<Canvas>();if(overlay==null)overlay=frame.gameObject.AddComponent<Canvas>();overlay.overrideSorting=true;overlay.sortingOrder=15000;
        frame.gameObject.SetActive(false);
    }
    static void Character(SelectedCharacterIcon c)
    {
        var t=c.transform;
        Box(R(t,"DescriptionBg"),new Vector2(0,1),new Vector2(397,-128),new Vector2(385,112));
        var viewport=Child(t,"Animated Portrait");Box(viewport,new Vector2(1,1),new Vector2(-107,-166),new Vector2(174,224));if(viewport.GetComponent<RectMask2D>()==null)viewport.gameObject.AddComponent<RectMask2D>();
        c.animatedCharacter.rectTransform.SetParent(viewport,false);Box(c.animatedCharacter.rectTransform,new Vector2(.5f,.5f),new Vector2(0,10),new Vector2(360,350));c.animatedCharacter.preserveAspect=true;c.animatedCharacter.raycastTarget=false;
        foreach(var icon in c.otherCharacters.GetComponentsInChildren<CharacterIconWithText>(true))Portrait(icon);
        var grid=c.otherCharacters.GetComponent<GridLayoutGroup>();if(grid!=null){grid.cellSize=new Vector2(90,104);grid.spacing=new Vector2(10,0);grid.constraint=GridLayoutGroup.Constraint.FixedRowCount;grid.constraintCount=1;}
        var companions=Child(t,"Companions");Box(companions,new Vector2(0,1),new Vector2(400,70),new Vector2(800,110));
        var companionViewport=Child(companions,"Viewport");Stretch(companionViewport);if(companionViewport.GetComponent<RectMask2D>()==null)companionViewport.gameObject.AddComponent<RectMask2D>();
        var others=(RectTransform)c.otherCharacters.transform;others.SetParent(companionViewport,false);others.anchorMin=others.anchorMax=new Vector2(0,.5f);others.pivot=new Vector2(0,.5f);others.anchoredPosition=Vector2.zero;others.sizeDelta=new Vector2(800,110);
        var fitter=others.GetComponent<ContentSizeFitter>()??others.gameObject.AddComponent<ContentSizeFitter>();fitter.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;fitter.verticalFit=ContentSizeFitter.FitMode.Unconstrained;
        var scroll=companions.GetComponent<ScrollRect>()??companions.gameObject.AddComponent<ScrollRect>();scroll.horizontal=true;scroll.vertical=false;scroll.viewport=companionViewport;scroll.content=others;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=30;
        foreach(var canvas in others.GetComponentsInChildren<Canvas>(true))canvas.overrideSorting=false;
        Log(c.GetComponentInChildren<LogManager>(true));
    }
    static void Portrait(CharacterIconWithText icon)
    {
        Surface(icon.transform);Hide(icon.transform,"DarkCloud");var frame=R(icon.transform,"Frame");Place(frame,Vector2.zero,Vector2.one,new Vector2(5,25),new Vector2(-5,-5));Flat(frame.GetComponent<Image>(),Color.white);frame.GetComponent<Mask>().showMaskGraphic=false;
        Stretch(icon.image.rectTransform);icon.image.preserveAspect=false;icon.image.color=Color.white;
        var zoom=icon.image.GetComponents<MonoBehaviour>().FirstOrDefault(c=>c.GetType().Name=="ZoomImage");if(zoom!=null)zoom.enabled=false;
        Body(icon.characterText,13);icon.characterText.alignment=TextAlignmentOptions.Center;icon.characterText.overflowMode=TextOverflowModes.Ellipsis;icon.characterText.textWrappingMode=TextWrappingModes.NoWrap;Place(icon.characterText.rectTransform,Vector2.zero,new Vector2(1,0),new Vector2(4,7),new Vector2(-4,25));
        var hp=R(icon.transform,"HealthBg");Place(hp,Vector2.zero,new Vector2(1,0),new Vector2(5,3),new Vector2(-5,6));Flat(hp.GetComponent<Image>(),new Color(.15f,.18f,.2f));Hide(hp,"Heart");Stretch(icon.healthBar.rectTransform);Flat(icon.healthBar,new Color(.4f,.68f,.51f));icon.healthBar.type=Image.Type.Filled;icon.healthBar.fillMethod=Image.FillMethod.Horizontal;
    }
    static void Log(LogManager log)
    {
        if(log==null)return;var t=log.transform;Hide(t,"SpeechIcon ");
        Box((RectTransform)t,new Vector2(1,1),new Vector2(-65,-32),new Vector2(106,34));
        var show=R(t,"ShowTab");var hide=R(t,"CollapseTab");if(show==null||hide==null)return;
        Stretch(show);Stretch(hide);ButtonLabel(show.GetComponent<Button>(),"Journal");ButtonLabel(hide.GetComponent<Button>(),"Close");
        var bar=R(t,"Bar");Box(bar,new Vector2(1,0),new Vector2(400,-144),new Vector2(740,324));Surface(bar);Hide(bar,"Background");
        var title=Label(bar,"Journal Title","JOURNAL",18,true);title.color=Gold;Box(title.rectTransform,new Vector2(0,1),new Vector2(175,-30),new Vector2(300,28));
        Place((RectTransform)log.scrollRect.transform,Vector2.zero,Vector2.one,new Vector2(22,20),new Vector2(-22,-57));Stretch(log.scrollRect.viewport);log.scrollRect.horizontal=false;log.scrollRect.vertical=true;log.scrollRect.movementType=ScrollRect.MovementType.Clamped;
        var label=log.entryTemplate.GetComponent<TMP_Text>();Body(label,20);label.textWrappingMode=TextWrappingModes.Normal;
        var group=log.content.GetComponent<VerticalLayoutGroup>();group.spacing=12;group.childControlHeight=true;group.childForceExpandHeight=false;group.padding=new RectOffset(4,4,4,4);
        log.newEntryStartScale=1;log.newEntryFlashDuration=.35f;
        var cg=bar.GetComponent<CanvasGroup>();if(cg!=null){cg.alpha=1;cg.interactable=true;cg.blocksRaycasts=true;}
        TogglePair(show.GetComponent<Button>(),hide.GetComponent<Button>(),new[]{bar.gameObject});bar.gameObject.SetActive(false);show.gameObject.SetActive(true);hide.gameObject.SetActive(false);
        foreach(var canvas in t.GetComponentsInChildren<Canvas>(true)){canvas.overrideSorting=true;canvas.sortingOrder=110;}
    }
    static void Map(Transform layout)
    {
        var map=R(layout,"Bottom/Minimap");var bg=R(map,"MinimapBg");var side=R(map,"MinimapSide");
        Box(map,new Vector2(1,0),new Vector2(-190,300),new Vector2(316,348));
        var banner=R(bg,"Banner");Box(banner,Vector2.one,new Vector2(-53,-68),new Vector2(64,108));banner.GetComponent<Image>().preserveAspect=true;
        Box(R(bg,"NationColorBorder"),new Vector2(0,1),new Vector2(115,-44),new Vector2(194,48));Hide(bg,"NationColorBorder/NationColor");
        foreach(var text in R(bg,"NationColorBorder").GetComponentsInChildren<TMP_Text>(true)){Body(text,22);text.alignment=TextAlignmentOptions.MidlineLeft;Stretch(text.rectTransform);}
        Box(R(bg,"Minimap"),new Vector2(.5f,0),new Vector2(0,133),new Vector2(274,214));
        var show=R(map,"ShowTab");var hide=R(map,"CollapseTab");
        foreach(var tab in new[]{show,hide})Box(tab,new Vector2(1,0),new Vector2(-48,-20),new Vector2(96,36));
        ButtonLabel(show.GetComponent<Button>(),"Show map");ButtonLabel(hide.GetComponent<Button>(),"Hide map");
        TogglePair(show.GetComponent<Button>(),hide.GetComponent<Button>(),new[]{bg.gameObject,side.gameObject,map.Find("Polish Surface").gameObject});
        bg.gameObject.SetActive(true);side.gameObject.SetActive(true);hide.gameObject.SetActive(true);show.gameObject.SetActive(false);
        foreach(var canvas in map.GetComponentsInChildren<Canvas>(true))canvas.overrideSorting=false;
        Box(R(layout,"Bottom/EndTurn"),new Vector2(1,0),new Vector2(-190,53),new Vector2(316,64));
    }
    static void Events(Transform layout)
    {
        var events=R(layout,"Right/Events");Box(events,Vector2.one,new Vector2(-190,-690),new Vector2(316,260));
        var owner=R(events,"ScrollableGridEvents");Stretch(owner);Hide(owner,"LeftBlackBorder");Stretch(R(owner,"Events"));
        var sr=owner.GetComponentInChildren<ScrollRect>(true);Stretch((RectTransform)sr.transform);Stretch(sr.viewport);sr.horizontal=false;sr.vertical=true;sr.movementType=ScrollRect.MovementType.Clamped;
        var grid=sr.content.GetComponent<GridLayoutGroup>();grid.cellSize=new Vector2(86,108);grid.spacing=new Vector2(12,10);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=3;grid.startCorner=GridLayoutGroup.Corner.UpperLeft;grid.childAlignment=TextAnchor.UpperLeft;grid.padding=new RectOffset(12,12,6,6);
        sr.content.anchorMin=new Vector2(0,1);sr.content.anchorMax=Vector2.one;sr.content.pivot=new Vector2(.5f,1);sr.content.anchoredPosition=Vector2.zero;
        foreach(var canvas in events.GetComponentsInChildren<Canvas>(true))canvas.overrideSorting=false;
    }
    static void Calendar(CalendarWidget calendar)
    {
        var root=(RectTransform)calendar.transform;Box(root,Vector2.one,new Vector2(-324,-490),new Vector2(584,690));Surface(root);Hide(root,"Background");
        foreach(var layout in root.GetComponents<LayoutGroup>())layout.enabled=false;var fitter=root.GetComponent<ContentSizeFitter>();if(fitter!=null)fitter.enabled=false;
        var header=R(root,"Header").GetComponent<TMP_Text>();Body(header,29);header.color=Gold;Box(header.rectTransform,new Vector2(.5f,1),new Vector2(0,-41),new Vector2(524,46));
        var grid=R(root,"Grid");Box(grid,new Vector2(.5f,.5f),new Vector2(0,46),new Vector2(528,430));var layoutGrid=grid.GetComponent<GridLayoutGroup>();layoutGrid.cellSize=new Vector2(82,76);layoutGrid.spacing=new Vector2(7,9);layoutGrid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layoutGrid.constraintCount=6;layoutGrid.padding=new RectOffset();
        foreach(Transform day in grid){Flat(day.GetComponent<Image>(),Ink);var num=day.Find("Num").GetComponent<TMP_Text>();Body(num,23);num.alignment=TextAlignmentOptions.TopLeft;Place(num.rectTransform,Vector2.zero,Vector2.one,new Vector2(10,24),new Vector2(-8,-7));var icon=day.Find("Icon").GetComponent<TMP_Text>();Body(icon,19);icon.alignment=TextAlignmentOptions.BottomRight;Place(icon.rectTransform,Vector2.zero,Vector2.one,new Vector2(5,6),new Vector2(-8,-25));}
        var footer=R(root,"Footer").GetComponent<TMP_Text>();Body(footer,17);Box(footer.rectTransform,new Vector2(.5f,0),new Vector2(0,83),new Vector2(522,120));footer.text="Today is highlighted in gold. Hover a marked day to read its story.";
        var so=new SerializedObject(calendar);so.FindProperty("cellSprite").objectReferenceValue=null;so.FindProperty("cellColor").colorValue=Ink;so.FindProperty("todayColor").colorValue=new Color(.34f,.28f,.15f);so.FindProperty("eventCellColor").colorValue=new Color(.13f,.2f,.25f);so.FindProperty("textColor").colorValue=Paper;so.FindProperty("eventSpriteScalePercent").intValue=135;so.ApplyModifiedPropertiesWithoutUndo();
        var canvas=root.GetComponent<Canvas>()??root.gameObject.AddComponent<Canvas>();canvas.overrideSorting=true;canvas.sortingOrder=500;
        so.FindProperty("gandalfColor").colorValue=new Color(.65f,.82f,.95f);so.FindProperty("sarumanColor").colorValue=new Color(.82f,.73f,.91f);so.FindProperty("sauronColor").colorValue=new Color(.95f,.52f,.4f);so.FindProperty("mixedColor").colorValue=Gold;so.ApplyModifiedPropertiesWithoutUndo();
        if(root.GetComponent<GraphicRaycaster>()==null)root.gameObject.AddComponent<GraphicRaycaster>();Panel(root).raycastTarget=true;
    }
    static void Event(EventIcon icon)
    {
        Surface(icon.transform);Hide(icon.transform,"Line");Hide(icon.transform,"Border");
        Place(icon.characterImage.rectTransform,Vector2.zero,Vector2.one,new Vector2(5,28),new Vector2(-5,-5));icon.characterImage.preserveAspect=false;
        Box(icon.eventImage.rectTransform,new Vector2(.5f,0),new Vector2(0,15),new Vector2(22,22));icon.eventImage.preserveAspect=true;
    }
    static void Progress(RectTransform root)
    {
        Surface(root);Box(root,new Vector2(.5f,0),new Vector2(0,122),new Vector2(920,146));
        var slider=root.GetComponentInChildren<Slider>(true);Box((RectTransform)slider.transform,new Vector2(.5f,0),new Vector2(0,37),new Vector2(820,8));slider.interactable=false;slider.transition=Selectable.Transition.None;
        if(slider.handleRect!=null)slider.handleRect.gameObject.SetActive(false);
        Flat(R(slider.transform,"Background").GetComponent<Image>(),new Color(.035f,.047f,.06f));Stretch(R(slider.transform,"Fill Area"));Flat(slider.fillRect.GetComponent<Image>(),Gold);
        var title=R(root,"Title").GetComponent<TMP_Text>();Body(title,27);title.alignment=TextAlignmentOptions.Center;title.color=Gold;title.text="Preparing your journey";Box(title.rectTransform,new Vector2(.5f,1),new Vector2(0,-35),new Vector2(820,38));
        var status=R(root,"Text").GetComponent<TMP_Text>();Body(status,19);status.alignment=TextAlignmentOptions.Center;Box(status.rectTransform,new Vector2(.5f,1),new Vector2(0,-77),new Vector2(820,30));
    }
    static void Loading(StartupLoadingScreen loading)
    {
        Progress(R(loading.transform,"Progress Widget"));Hide(loading.transform,"Progress Widget/Logo");
        Box(R(loading.transform,"Cards"),new Vector2(.5f,.5f),new Vector2(0,30),new Vector2(1798,465));
        var title=Label(loading.transform,"Journey Title","MIDDLE-EARTH AWAITS",38,true);title.color=Gold;title.alignment=TextAlignmentOptions.Center;Box(title.rectTransform,new Vector2(.5f,1),new Vector2(0,-170),new Vector2(1000,65));
        var prompt=Ref<TextMeshProUGUI>(loading,"continuePromptText");if(prompt!=null){Body(prompt,23);prompt.alignment=TextAlignmentOptions.Center;Box(prompt.rectTransform,new Vector2(.5f,0),new Vector2(0,30),new Vector2(960,35));}
    }
    static void Environment(Transform layout)
    {
        var root=R(layout,"Top/Environmental");Box(root,new Vector2(0,1),new Vector2(182,-194),new Vector2(300,100));Surface(root);
        var card=Ref<Card>(layout.GetComponent<Layout>(),"environmentalCard");Box((RectTransform)card.transform,new Vector2(0,.5f),new Vector2(50,0),new Vector2(84,84));
        var token=card.transform.Find("TokenRepresentation");token.localPosition=Vector3.zero;token.localScale=Vector3.one;
        var mask=R(token,"Token");Box(mask,new Vector2(.5f,.5f),Vector2.zero,new Vector2(78,78));Flat(mask.GetComponent<Image>(),Color.white);mask.GetComponent<Mask>().showMaskGraphic=false;Stretch(R(mask,"TokenedImage"));Hide(token,"Border");
        var glyph=R(mask,"Environmental");Box(glyph,new Vector2(1,0),new Vector2(-10,10),new Vector2(24,24));glyph.GetComponent<TMP_Text>().fontSize=22;
        var label=Label(root,"Environment Title","ENVIRONMENT",16,true);label.color=Gold;Box(label.rectTransform,new Vector2(0,.5f),new Vector2(195,15),new Vector2(184,26));
        var hint=Label(root,"Environment Hint","Hover to inspect",18,true);Box(hint.rectTransform,new Vector2(0,.5f),new Vector2(195,-14),new Vector2(184,26));
        Set(layout.GetComponent<Layout>(),"environmentalName",hint);
    }
    static void Video(VideoPopupManager video)
    {
        var content=video.transform.Find("Content");var canvas=content.GetComponent<Canvas>();canvas.sortingOrder=12000;var scaler=content.GetComponent<CanvasScaler>()??content.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;
        Flat(content.GetComponent<Image>(),new Color(.015f,.02f,.028f,.94f));var panel=R(content,"Panel");Box(panel,new Vector2(.5f,.5f),Vector2.zero,new Vector2(1060,594));Surface(panel);
        Box(video.videoDisplay.rectTransform,new Vector2(0,.5f),new Vector2(297,0),new Vector2(550,550));
        var scroll=R(panel,"ScrollableText");
        if(scroll==null)
        {
            var body=R(panel,"Text").GetComponent<TMP_Text>();Body(body,21);Place(body.rectTransform,Vector2.zero,Vector2.one,new Vector2(600,97),new Vector2(-35,-50));
            video.scrollableText=body.GetComponent<TypewriterEffect>()??body.gameObject.AddComponent<TypewriterEffect>();video.scrollableText.textMeshPro=body;
        }
        else
        {
        Hide(panel,"Text");
        Place(scroll,Vector2.zero,Vector2.one,new Vector2(600,97),new Vector2(-35,-50));QuietImage(scroll);var sr=scroll.GetComponent<ScrollRect>();sr.horizontal=false;sr.movementType=ScrollRect.MovementType.Clamped;Hide(scroll,"Scrollbar Horizontal");
        var auto=scroll.GetComponent<AutoScroll>();if(auto!=null)auto.enabled=false;
        video.scrollableText=scroll.GetComponentInChildren<TypewriterEffect>(true);
        if(video.scrollableText==null)video.scrollableText=sr.content.gameObject.AddComponent<TypewriterEffect>();
        if(video.scrollableText.textMeshPro==null)video.scrollableText.textMeshPro=sr.content.GetComponent<TMP_Text>();
        Body(video.scrollableText.textMeshPro,21);var text=video.scrollableText.textMeshPro.rectTransform;text.anchorMin=new Vector2(0,1);text.anchorMax=Vector2.one;text.pivot=new Vector2(.5f,1);text.anchoredPosition=Vector2.zero;text.sizeDelta=new Vector2(-18,0);
        }
        Box((RectTransform)video.closeButton.transform,new Vector2(1,0),new Vector2(-139,44),new Vector2(214,46));ButtonLabel(video.closeButton,"Continue");
        foreach(var nested in content.GetComponentsInChildren<Canvas>(true))if(nested!=canvas)nested.overrideSorting=false;
    }
}
