using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shows the player where and how to pursue their objectives:
///  • a gold place name on every objective target
///  • on the first turn, an in-place guided tour: the camera flies to each objective and a callout
///    is pinned beside the place itself (leader line + ring on the hex) explaining what to do and
///    what it is worth; the player steps through with Next / Skip (Enter / Esc). The last step
///    points at the objective tracker, which reopens any objective later.
///  • FocusObjective(): the same pinned callout for one objective (tracker click)
/// </summary>
public class ObjectiveGuide : MonoBehaviour
{
    private const float CalloutWidth = 400f;
    private const float LineGap = 70f;     // horizontal distance from the target to the callout

    private static ObjectiveGuide instance;
    private readonly List<(Transform root, SpriteRenderer glow, Hex hex)> beacons = new();
    private Game game;
    private bool tourDone;
    private Coroutine running;

    // Callout state.
    private RectTransform canvasRoot;
    private Canvas canvas;
    private RectTransform callout;
    private RectTransform leaderLine;
    private RectTransform marker;
    private Vector3? anchorWorld;
    private RectTransform anchorUi;
    private readonly HashSet<Hex> focusedHexes = new();
    private bool advance, skip;

    public static ObjectiveGuide Instance => instance;
    public bool TourRunning { get; private set; }

    public static void Attach(Game owner)
    {
        if (instance != null || owner == null) return;
        instance = owner.gameObject.AddComponent<ObjectiveGuide>();
        instance.game = owner;
        owner.HumanTurnStarted += instance.OnHumanTurnStarted;
        NextStepGuide.Attach(owner);
    }

    private void OnDestroy()
    {
        if (game != null) game.HumanTurnStarted -= OnHumanTurnStarted;
        if (instance == this) instance = null;
    }

    private void OnHumanTurnStarted()
    {
        BuildBeacons();
        if (!tourDone && !AutoTour.Active)
        {
            tourDone = true;
            Stop();
            running = StartCoroutine(Tour());
        }
    }

    // ------------------------------------------------------------------ frame

    private void Update()
    {
        // Kept well under the bloom threshold so the ring never washes out the city it marks.
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.6f);
        foreach (var b in beacons)
        {
            if (b.glow == null) continue;
            bool focused = focusedHexes.Contains(b.hex);
            b.glow.color = new Color(0.85f, 0.68f, 0.36f, (focused ? 0.75f : 0.45f) + 0.15f * pulse);
            b.glow.transform.localScale = Vector3.one * ((focused ? 1.05f : 0.95f) + 0.03f * pulse);
        }

        if (callout == null) return;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) advance = true;
        if (Input.GetKeyDown(KeyCode.Escape)) skip = true;
    }

    private void LateUpdate()
    {
        if (callout != null) PlaceCallout();
    }

    // ------------------------------------------------------------------ beacons

    private void BuildBeacons()
    {
        CampaignObjectives objectives = CampaignObjectives.Instance;
        if (objectives == null || game?.player == null) return;
        foreach (var b in beacons)
        {
            if (b.root != null) Destroy(b.root.gameObject);
            if (b.hex != null) b.hex.SetObjectiveMarker(false);
        }
        beacons.Clear();
        // While the campaign chain runs, the war's objectives are not on the map yet.
        if (objectives.HasCampaign(game.player) && !objectives.IsChainComplete(game.player)) return;
        foreach (CampaignObjective o in objectives.GetObjectives(game.player))
        {
            foreach (Hex hex in objectives.GetTargetHexes(o))
            {
                if (beacons.Exists(b => b.hex == hex)) continue;
                beacons.Add(CreateBeacon(hex, o));
            }
        }
    }

    private (Transform, SpriteRenderer, Hex) CreateBeacon(Hex hex, CampaignObjective o)
    {
        var root = new GameObject($"ObjectiveBeacon_{o.id}").transform;
        root.position = hex.transform.position + new Vector3(0f, 0f, -0.1f);

        // No ring on the ground: with 3D miniatures and towns on the hex it read as a second
        // selection circle. The place's own label (gold name, SetObjectiveMarker) marks it.
        hex.SetObjectiveMarker(true);
        return (root, null, hex);
    }

    // ------------------------------------------------------------------ tour

    /// <summary>Flies the camera to an objective and pins its explanation beside the place.</summary>
    public void FocusObjective(CampaignObjective o)
    {
        Stop();
        running = StartCoroutine(Single(o));
    }

    private void Stop()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        TourRunning = false;
        skip = advance = false;
        HideCallout();
    }

    private IEnumerator Single(CampaignObjective o)
    {
        yield return Step(o, null, -1, -1, lastStep: true);
        HideCallout();
        skip = false;
        running = null;
    }

    private IEnumerator Tour()
    {
        CampaignObjectives objectives = CampaignObjectives.Instance;
        if (objectives == null || game?.player == null || !EnsureCanvas()) yield break;

        // Let the turn banner, intro and any popup finish first, so the tour has the screen.
        yield return new WaitForSecondsRealtime(0.6f);
        float deadline = Time.unscaledTime + 20f;
        while (Time.unscaledTime < deadline && (TurnBanner.IsShowing || PopupManager.IsShowing || VideoPopupManager.IsShowing
            || ConfirmationDialog.IsShowing || SelectionDialog.IsShowing))
            yield return null;
        yield return new WaitForSecondsRealtime(0.4f);

        TourRunning = true;
        bool campaign = objectives.HasCampaign(game.player) && !objectives.IsChainComplete(game.player);
        // One goal, the one the objectives widget shows first (CampaignHud): a wall of steps up
        // front was more than a new player can take in.
        List<CampaignObjective> all = campaign ? objectives.ActiveMilestones(game.player) : new(objectives.GetObjectives(game.player));
        List<CampaignObjective> list = all.Count > 0 ? new List<CampaignObjective> { all[0] } : all;
        int total = list.Count + 1;
        for (int i = 0; i < list.Count && !skip; i++)
            yield return Step(list[i], null, i + 1, total, lastStep: false);

        if (!skip)
        {
            RectTransform tracker = canvasRoot != null ? FindDeep(canvasRoot, "CampaignObjectivesTracker") : null;
            yield return TrackerStep(tracker, total);
        }

        HideCallout();
        Character selected = Board.Instance != null ? Board.Instance.selectedCharacter : null;
        if (selected?.hex != null) BoardNavigator.Instance?.LookAt(selected.hex.transform.position, 0.8f);
        TourRunning = false;
        skip = false;
        running = null;
    }

    private IEnumerator Step(CampaignObjective o, RectTransform uiAnchor, int index, int total, bool lastStep)
    {
        CampaignObjectives objectives = CampaignObjectives.Instance;
        List<Hex> targets = objectives != null ? objectives.GetTargetHexes(o) : new List<Hex>();
        // A campaign step flies to where it is done (a hero's home to recruit at, else your capital).
        if (o.milestone && targets.Count == 0 && objectives != null)
        {
            Hex focus = objectives.GetFocusHex(game.player, o);
            if (focus != null && focus != game.player.hex) targets.Add(focus);
        }

        focusedHexes.Clear();
        foreach (Hex h in targets) if (h != null) focusedHexes.Add(h);

        Vector3? world = null;
        if (targets.Count > 0)
        {
            Vector3 c = Vector3.zero;
            foreach (Hex h in targets) c += h.transform.position;
            c /= targets.Count;
            world = targets.Count == 1 ? targets[0].transform.position : c;
            BoardNavigator.Instance?.LookAt(world.Value, 0.9f);
        }
        else
        {
            // Objectives without a place (e.g. "hold N cities") are explained from your capital.
            Hex home = game.player.hex;
            if (home != null) { world = home.transform.position; BoardNavigator.Instance?.LookAt(world.Value, 0.9f); }
        }

        int unlockCount = o.milestone && DeckManager.Instance != null ? DeckManager.Instance.CountCardsUnlockedBy(game.player, o) : 0;
        string reward = o.milestone ? $"{CampaignObjectives.TrackOf(o).ToUpperInvariant()} STEP · UNLOCKS <color={UiTheme.GoldHex}>{unlockCount}</color> CARDS"
            : o.decisive ? $"<color={UiTheme.GoldHex}>WINS THE WAR</color>" : $"<color={UiTheme.GoldHex}>+{o.points}</color> POINTS";
        string step = index > 0 ? $"<color={UiTheme.MutedHex}>{index} / {total}</color>   " : "";
        string places = targets.Count > 1 ? $"\n<color={UiTheme.MutedHex}>{targets.Count} places, all marked on the map</color>" : "";
        string how = objectives != null ? objectives.HowTo(game.player, o) : string.Empty;
        bool met = objectives != null && objectives.IsMet(game.player, o);
        string status = met ? $"\n<color={UiTheme.GoodHex}>Currently held</color>" : "";

        if (o.milestone)
        {
            ShowCallout($"{step}<size=78%><cspace=0.1em>{CampaignObjectives.TrackOf(o).ToUpperInvariant()} STEP</cspace></size>",
                o.title,
                ObjectiveLines(o, objectives),
                null,
                lastStep ? "Got it" : "Next",
                showSkip: !lastStep && index > 0,
                story: string.IsNullOrWhiteSpace(o.story) ? o.description : o.story,
                art: StepArt(o));
        }
        else
        {
            ShowCallout($"{step}<size=78%><cspace=0.1em>OBJECTIVE · {reward}</cspace></size>",
                o.title,
                $"{o.description}{places}{status}",
                string.IsNullOrWhiteSpace(how) ? null : how,
                lastStep ? "Got it" : "Next",
                showSkip: !lastStep && index > 0,
                art: StepArt(o));
        }
        anchorWorld = world;
        anchorUi = uiAnchor;

        yield return WaitForAdvance();
        focusedHexes.Clear();
    }

    // The step as an objective: its instruction one sentence a line, numbered; where it stands;
    // and the cards it brings, by name.
    private string ObjectiveLines(CampaignObjective o, CampaignObjectives objectives)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(UiTheme.Eyebrow("Your objective")).Append('\n');
        string[] sentences = System.Text.RegularExpressions.Regex.Split((o.how ?? string.Empty).Trim(), @"(?<=[.:!?])\s+(?=[A-Z])");
        int n = 0;
        foreach (string sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence)) continue;
            sb.Append($"<color={UiTheme.GoldHex}>{++n}.</color>  {sentence.Trim()}\n");
        }
        if (objectives != null)
            sb.Append($"<size=90%><color={UiTheme.MutedHex}>Now: {objectives.ProgressText(game.player, o)}</color></size>\n");
        if (DeckManager.Instance != null)
        {
            List<string> names = DeckManager.Instance.GetCardsUnlockedBy(game.player, o).Select(c => DisplayNames.Format(c.name)).Distinct().ToList();
            if (names.Count > 0)
                sb.Append($"<size=90%><color={UiTheme.GoodHex}>Brings:</color> {string.Join(", ", names.Take(6))}{(names.Count > 6 ? $" and {names.Count - 6} more" : string.Empty)}</size>");
        }
        return sb.ToString().TrimEnd();
    }

    // What the step is about, as a picture: the person for a recruit or a battle, else the place
    // (its card's painting, or its map tile), else the player's own leader.
    private Sprite StepArt(CampaignObjective o)
    {
        Illustrations art = FindAnyObjectByType<Illustrations>();
        if (!string.IsNullOrWhiteSpace(o.character) && art != null)
        {
            Sprite who = art.GetCharacterArtByName(o.character, logMissing: false);
            if (who != null) return who;
        }
        if (!string.IsNullOrWhiteSpace(o.pc))
        {
            CardData card = DeckManager.Instance != null ? DeckManager.Instance.FindPcCardByPcName(o.pc) : null;
            Sprite place = card != null ? HandTray.ResolveArt(card) : null;
            if (place != null) return place;
            Hex hex = CampaignObjectives.Instance != null ? CampaignObjectives.Instance.GetTargetHexes(o).FirstOrDefault() : null;
            if (hex != null && hex.pcTexture != null && hex.pcTexture.sprite != null) return hex.pcTexture.sprite;
        }
        return art != null && game.player != null ? art.GetCharacterArtByName(game.player.characterName, logMissing: false) : null;
    }

    private IEnumerator TrackerStep(RectTransform tracker, int total)
    {
        CampaignObjectives objectives = CampaignObjectives.Instance;
        bool campaign = objectives != null && objectives.HasCampaign(game.player) && !objectives.IsChainComplete(game.player);
        string body = campaign
            ? "You begin alone. Your campaign lives here: first one step, then a few at once. Each step puts the cards that do it in your hand, and reaching it adds more: win realms over, recruit heroes, raise hosts. Click a step to fly to where it is done. When the last chapter is done your deck is complete and the war's final objectives open.\nOpen \"Your deck\" (or press C) to see every card: the ones you hold, and the step that brings each of the others."
            : "Your objectives and score live here. Click any of them to see how to achieve it. Open \"Your deck\" (or press C) to see every card you hold.";
        ShowCallout($"<color={UiTheme.MutedHex}>{total} / {total}</color>",
            "Your war at a glance",
            body,
            null, "Begin", showSkip: false);
        anchorWorld = null;
        anchorUi = tracker;
        yield return WaitForAdvance();
    }

    private IEnumerator WaitForAdvance()
    {
        advance = false;
        float ignoreUntil = Time.unscaledTime + 0.35f; // a click that ended the last step mustn't skip this one
        while (!advance && !skip)
        {
            if (Time.unscaledTime < ignoreUntil) advance = false;
            yield return null;
        }
        advance = false;
    }

    // ------------------------------------------------------------------ callout

    private bool EnsureCanvas()
    {
        if (canvasRoot != null) return true;
        Layout layout = FindAnyObjectByType<Layout>();
        canvas = layout != null ? layout.GetComponentInParent<Canvas>()?.rootCanvas : null;
        canvasRoot = canvas != null ? canvas.transform as RectTransform : null;
        return canvasRoot != null;
    }

    private float K => canvasRoot != null ? Mathf.Max(0.5f, canvasRoot.rect.height / 1080f) : 1f;

    private void ShowCallout(string eyebrow, string title, string body, string how, string nextLabel, bool showSkip,
        string story = null, Sprite art = null)
    {
        HideCallout();
        if (!EnsureCanvas()) return;

        // Leader line + ring marker sit under the callout.
        leaderLine = NewRect("ObjectiveLeaderLine", canvasRoot);
        var lineImg = leaderLine.gameObject.AddComponent<Image>();
        lineImg.color = new Color(0.88f, 0.72f, 0.42f, 0.85f);
        lineImg.raycastTarget = false;
        leaderLine.pivot = new Vector2(0f, 0.5f);

        marker = NewRect("ObjectiveMarker", canvasRoot);
        var ring = marker.gameObject.AddComponent<Image>();
        ring.sprite = HudSprites.Ring;
        ring.color = new Color(1f, 0.85f, 0.5f, 1f);
        ring.raycastTarget = false;
        marker.sizeDelta = new Vector2(46f, 46f) * K;

        callout = NewRect("ObjectiveCallout", canvasRoot);
        callout.localScale = Vector3.one * K;
        callout.pivot = new Vector2(0f, 0.5f);
        callout.sizeDelta = new Vector2(CalloutWidth, 0f);
        var panel = callout.gameObject.AddComponent<CinematicPanel>();
        panel.fillTop = new Color(0.05f, 0.05f, 0.045f, 0.94f);
        panel.fillBottom = new Color(0.025f, 0.025f, 0.022f, 0.96f);
        panel.raycastTarget = true; // swallow clicks so they don't select hexes underneath
        var vlg = callout.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(22, 22, 16, 16);
        vlg.spacing = 6f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        callout.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        AddText(callout, eyebrow, 13f, UiTheme.Text, false);
        if (art != null) AddArt(callout, art);
        AddText(callout, title, 26f, UiTheme.Gold, true);
        if (!string.IsNullOrWhiteSpace(story))
        {
            AddText(callout, $"<i>{story.Trim()}</i>", 14.5f, new Color(0.86f, 0.8f, 0.67f), false);
            AddRule(callout);
        }
        AddText(callout, body, 14.5f, UiTheme.Text, false);
        if (how != null) AddText(callout, $"<color={UiTheme.GoodHex}>How:</color> {how}", 14f, UiTheme.Muted, false);

        var row = NewRect("Buttons", callout);
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.spacing = 14f;
        hlg.padding = new RectOffset(0, 0, 8, 0);
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
        if (showSkip) AddButton(row, "Skip tour", primary: false, () => skip = true);
        AddButton(row, nextLabel, primary: true, () => advance = true);

        PlaceCallout();
    }

    private void HideCallout()
    {
        if (callout != null) Destroy(callout.gameObject);
        if (leaderLine != null) Destroy(leaderLine.gameObject);
        if (marker != null) Destroy(marker.gameObject);
        callout = leaderLine = marker = null;
        anchorWorld = null;
        anchorUi = null;
    }

    // Pins the callout beside its anchor (a board point or a HUD element), on whichever side has
    // room, clamped clear of the top bar and the bottom dock; the leader line joins them.
    private void PlaceCallout()
    {
        Rect r = canvasRoot.rect;
        float k = K;
        Vector2 size = new(CalloutWidth * k, callout.rect.height * k);
        Vector2 anchor;
        bool hasAnchor = TryAnchorLocal(out anchor);
        if (!hasAnchor) anchor = new Vector2(r.center.x - size.x * 0.5f - LineGap * k, r.center.y);

        float top = r.yMax - 130f * k, bottom = r.yMin + 300f * k;
        bool right = anchor.x + LineGap * k + size.x < r.xMax - 24f * k;
        float x = right ? anchor.x + LineGap * k : anchor.x - LineGap * k - size.x;
        x = Mathf.Clamp(x, r.xMin + 24f * k, r.xMax - 24f * k - size.x);
        float y = Mathf.Clamp(anchor.y, bottom + size.y * 0.5f, top - size.y * 0.5f);
        callout.anchorMin = callout.anchorMax = new Vector2(0.5f, 0.5f);
        callout.anchoredPosition = new Vector2(x, y);

        bool showMarker = hasAnchor && anchorWorld.HasValue;
        marker.gameObject.SetActive(showMarker);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.anchoredPosition = anchor;
        float s = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 3f);
        marker.localScale = new Vector3(s, s * 0.62f, 1f); // squashed like the board's perspective

        leaderLine.gameObject.SetActive(hasAnchor);
        if (!hasAnchor) return;
        Vector2 end = new(right ? x : x + size.x, y);
        Vector2 start = anchor + (end - anchor).normalized * (showMarker ? 22f * k : 0f);
        Vector2 d = end - start;
        leaderLine.anchorMin = leaderLine.anchorMax = new Vector2(0.5f, 0.5f);
        leaderLine.anchoredPosition = start;
        leaderLine.sizeDelta = new Vector2(d.magnitude, 1.6f * k);
        leaderLine.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    // Anchor in canvas-local coordinates (origin at the canvas centre).
    private bool TryAnchorLocal(out Vector2 local)
    {
        local = Vector2.zero;
        Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (anchorWorld.HasValue)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 screen = cam.WorldToScreenPoint(anchorWorld.Value);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screen, uiCam, out local);
        }
        if (anchorUi != null && anchorUi.gameObject.activeInHierarchy)
        {
            Vector3[] c = new Vector3[4];
            anchorUi.GetWorldCorners(c);
            Vector3 rightMid = (c[2] + c[3]) * 0.5f;
            local = canvasRoot.InverseTransformPoint(rightMid);
            return true;
        }
        return false;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    // A wide band of the picture, cropped to fill (the painting's middle), framed in gold.
    private static void AddArt(RectTransform parent, Sprite sprite)
    {
        const float height = 150f;
        var frame = NewRect("Art", parent);
        frame.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        var border = frame.gameObject.AddComponent<Image>();
        border.color = new Color(UiTheme.Gold.r, UiTheme.Gold.g, UiTheme.Gold.b, 0.55f);
        border.raycastTarget = false;
        var window = NewRect("Window", frame);
        window.anchorMin = Vector2.zero;
        window.anchorMax = Vector2.one;
        window.offsetMin = new Vector2(1.5f, 1.5f);
        window.offsetMax = new Vector2(-1.5f, -1.5f);
        window.gameObject.AddComponent<RectMask2D>();
        var picture = NewRect("Picture", window);
        picture.anchorMin = picture.anchorMax = new Vector2(0.5f, 0.5f);
        var image = picture.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.gameObject.AddComponent<ImageUnaffectedBySkin>();
        var fit = picture.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
        // A soft fade at the bottom into the panel.
        var fade = NewRect("Fade", window);
        fade.anchorMin = Vector2.zero;
        fade.anchorMax = new Vector2(1f, 0.35f);
        fade.offsetMin = fade.offsetMax = Vector2.zero;
        var fadeImage = fade.gameObject.AddComponent<Image>();
        fadeImage.sprite = HudSprites.FadeUp;
        fadeImage.color = new Color(0.03f, 0.03f, 0.027f, 0.85f);
        fadeImage.raycastTarget = false;
    }

    private static void AddRule(RectTransform parent)
    {
        var rule = NewRect("Rule", parent);
        rule.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
        var image = rule.gameObject.AddComponent<Image>();
        image.color = new Color(UiTheme.Gold.r, UiTheme.Gold.g, UiTheme.Gold.b, 0.35f);
        image.raycastTarget = false;
    }

    private static void AddText(RectTransform parent, string value, float size, Color color, bool display)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (display) UiTheme.StyleTitle(t, size, color); else UiTheme.StyleBody(t, size, color);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        t.raycastTarget = false;
        t.text = value;
    }

    private static void AddButton(RectTransform parent, string label, bool primary, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button), typeof(LayoutElement));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var panel = go.GetComponent<CinematicPanel>();
        panel.ornament = 0f;
        panel.inset = 2f;
        panel.fill = primary;
        panel.fillTop = new Color(0.20f, 0.16f, 0.09f, 0.95f);
        panel.fillBottom = new Color(0.12f, 0.09f, 0.05f, 0.95f);
        panel.line = primary ? new Color(0.92f, 0.76f, 0.45f, 1f) : new Color(0f, 0f, 0f, 0f);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = primary ? 130f : 100f;
        le.preferredHeight = 40f;
        var button = go.GetComponent<Button>();
        button.targetGraphic = panel;
        button.onClick.AddListener(() => { Sounds.Instance?.PlayUiClick(); onClick(); });
        var colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.2f, 1.1f, 1f);
        button.colors = colors;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var trt = textGo.GetComponent<RectTransform>();
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var t = textGo.GetComponent<TextMeshProUGUI>();
        UiTheme.StyleBody(t, 15f, primary ? UiTheme.Gold : UiTheme.Muted);
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.text = primary ? $"{label}  ›" : label;
    }

    private static RectTransform FindDeep(Transform root, string name)
    {
        foreach (RectTransform t in root.GetComponentsInChildren<RectTransform>(true))
            if (t.name == name) return t;
        return null;
    }
}
