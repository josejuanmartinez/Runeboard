using RetroLOTR.Scenarios;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runtime-built campaign UI: the objective tracker (under the Environment box, top-left)
/// and the end-of-war screen that replaces the old instant Application.Quit().
/// Built in code and parented to the HUD's root canvas so it inherits its scaling, while
/// popups (which sit later in that canvas) still draw above the tracker.
/// </summary>
public class CampaignHud : MonoBehaviour
{
    private static CampaignHud instance;

    private static readonly Color Gold = new(0.86f, 0.73f, 0.45f, 1f);
    private const string MetColor = "#9BD37A";
    private const string UnmetColor = "#B9AE98";
    private const string MutedColor = "#8C8371";

    private CampaignObjectives objectives;
    private RectTransform canvasRoot;
    // Panels are authored in 1080p pixels and scaled to the HUD canvas (≈2 units per pixel).
    private float K => canvasRoot != null ? Mathf.Max(0.5f, canvasRoot.rect.height / 1080f) : 1f;
    private RectTransform trackerRect;
    private TextMeshProUGUI trackerText;
    private bool trackerPlaced;
    private bool expanded = true;
    private float nextRefresh;
    private GameObject endScreen;

    public static void Ensure(CampaignObjectives source)
    {
        if (instance == null)
        {
            var go = new GameObject("CampaignHud");
            instance = go.AddComponent<CampaignHud>();
        }
        instance.Bind(source);
    }

    public static void ShowEndScreen(bool win, string reason) => (instance ?? CreateDetached())?.BuildEndScreen(win, reason);

    private static CampaignHud CreateDetached()
    {
        var go = new GameObject("CampaignHud");
        instance = go.AddComponent<CampaignHud>();
        return instance;
    }

    private void Bind(CampaignObjectives source)
    {
        if (objectives != null) objectives.Changed -= Refresh;
        objectives = source;
        if (objectives != null) objectives.Changed += Refresh;
        EnsureTracker();
        Refresh();
    }

    private void OnDestroy()
    {
        if (objectives != null) objectives.Changed -= Refresh;
        if (instance == this) instance = null;
    }

    private void Update()
    {
        // PCs change hands mid-turn (sieges, events), so keep the ticks honest between turns.
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1f;
        if (trackerRect != null && !trackerPlaced) PlaceTracker();
        Refresh();
    }

    private bool EnsureCanvas()
    {
        if (canvasRoot != null) return true;
        Layout layout = FindAnyObjectByType<Layout>();
        Canvas canvas = layout != null ? layout.GetComponentInParent<Canvas>() : null;
        if (canvas == null) canvas = FindObjectsByType<Canvas>().FirstOrDefault(c => c.isRootCanvas && c.renderMode != RenderMode.WorldSpace);
        if (canvas == null) return false;
        canvasRoot = canvas.rootCanvas.transform as RectTransform;
        return canvasRoot != null;
    }

    // ---------------------------------------------------------------- Tracker

    private void EnsureTracker()
    {
        if (trackerRect != null || !EnsureCanvas()) return;

        var panelGo = new GameObject("CampaignObjectivesTracker", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(Button));
        trackerRect = panelGo.GetComponent<RectTransform>();
        trackerRect.SetParent(canvasRoot, false);
        trackerRect.SetSiblingIndex(0);
        trackerRect.anchorMin = trackerRect.anchorMax = new Vector2(0f, 1f);
        trackerRect.pivot = new Vector2(0f, 1f);
        trackerRect.sizeDelta = new Vector2(250f, 0f);
        trackerRect.localScale = new Vector3(K, K, 1f);

        var layoutGroup = panelGo.GetComponent<VerticalLayoutGroup>();
        layoutGroup.padding = new RectOffset(10, 10, 8, 10);
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandHeight = false;
        var fitter = panelGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Clicking the tracker folds the objective descriptions away.
        var button = panelGo.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OnTrackerClicked);

        trackerText = CreateText(trackerRect, "Text", 12.5f, TextAlignmentOptions.TopLeft);
        trackerText.lineSpacing = 4f;
        trackerText.richText = true;
        PlaceTracker();
    }

    // Sit just below the Environment box when it can be found; otherwise use a fixed slot.
    private void PlaceTracker()
    {
        if (trackerRect == null) return;
        if (HudDirector.Applied)
        {
            // Docked HUD: the tracker hangs from the top bar on the left edge.
            trackerRect.anchoredPosition = new Vector2(HudDirector.Margin, -(HudDirector.TopBarHeight + HudDirector.Margin));
            trackerPlaced = true;
            return;
        }
        RectTransform anchor = FindEnvironmentBox();
        if (anchor != null)
        {
            Vector3[] corners = new Vector3[4];
            anchor.GetWorldCorners(corners); // 0 = bottom-left
            Vector3 local = canvasRoot.InverseTransformPoint(corners[0]);
            Rect r = canvasRoot.rect;
            trackerRect.anchoredPosition = new Vector2(local.x - r.xMin, local.y - r.yMax - 8f * K);
            trackerPlaced = true;
        }
        else
        {
            trackerRect.anchoredPosition = new Vector2(20f * K, -200f * K);
        }
    }

    private static RectTransform FindEnvironmentBox()
    {
        Layout layout = FindAnyObjectByType<Layout>();
        if (layout == null) return null;
        foreach (RectTransform rt in layout.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt.gameObject.activeInHierarchy && rt.name == "Environmental") return rt;
        }
        return null;
    }

    private void OnTrackerClicked()
    {
        int link = TMP_TextUtilities.FindIntersectingLink(trackerText, Input.mousePosition, null);
        if (link >= 0 && objectives != null && Game.Instance?.player != null)
        {
            string id = trackerText.textInfo.linkInfo[link].GetLinkID();
            foreach (CampaignObjective o in objectives.GetObjectives(Game.Instance.player))
            {
                if (o.id != id) continue;
                ObjectiveGuide.Instance?.FocusObjective(o);
                Sounds.Instance?.PlayUiClick();
                return;
            }
        }
        expanded = !expanded;
        Refresh();
    }

    private void Refresh()
    {
        if (trackerText == null || objectives == null) return;
        Game game = Game.Instance;
        PlayableLeader player = game != null ? game.player : null;
        if (player == null) { trackerRect.gameObject.SetActive(false); return; }
        trackerRect.gameObject.SetActive(endScreen == null && game.started);

        var sb = new StringBuilder();
        sb.Append(UiTheme.Eyebrow("The War of the Ring")).Append('\n');
        sb.Append($"Turn {game.turn} of {objectives.AgeLength}  <color={UiTheme.MutedHex}><size=90%>· Age ends in {objectives.TurnsLeft}</size></color>\n");
        sb.Append($"<size=92%>Your score <color={UiTheme.GoldHex}>{objectives.Score(player)}</color>/{objectives.MaxScore(player)}");
        foreach (PlayableLeader rival in objectives.AllLeaders().Where(l => l != player))
        {
            string name = CampaignObjectives.DisplayLeader(rival);
            sb.Append(rival.killed ? $"   <color={UiTheme.MutedHex}><s>{name}</s></color>" : $"   <color={UiTheme.MutedHex}>{name} {objectives.Score(rival)}</color>");
        }
        sb.Append("</size>\n");

        foreach (CampaignObjective o in objectives.GetObjectives(player))
        {
            bool met = objectives.IsMet(player, o);
            string color = met ? UiTheme.GoodHex : UiTheme.TextHex;
            string tag = o.decisive ? $"<color={UiTheme.GoldHex}>WIN</color>" : $"<color={UiTheme.GoldHex}>+{o.points}</color>";
            sb.Append($"\n{tag}  <link=\"{o.id}\"><color={color}><u>{o.title}</u></color></link>{(met && !o.decisive ? $"  <size=80%><color={UiTheme.GoodHex}>held</color></size>" : string.Empty)}");
            if (expanded && !string.IsNullOrWhiteSpace(o.description))
                sb.Append($"\n<size=84%><color={UiTheme.MutedHex}>{o.description}</color></size>");
        }
        sb.Append($"\n<size=80%><color={UiTheme.MutedHex}>Click an objective to see where and how</color></size>");
        trackerText.text = sb.ToString();

        FindAnyObjectByType<PlayableLeaderIcons>()?.RefreshVictoryPointsForAll();
    }

    // ---------------------------------------------------------------- End screen

    private void BuildEndScreen(bool win, string reason)
    {
        if (endScreen != null || !EnsureCanvas()) return;
        if (trackerRect != null) trackerRect.gameObject.SetActive(false);

        endScreen = new GameObject("CampaignEndScreen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var root = endScreen.GetComponent<RectTransform>();
        root.SetParent(canvasRoot, false);
        root.SetAsLastSibling();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var dim = endScreen.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.78f);
        dim.raycastTarget = true;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var panel = panelGo.GetComponent<RectTransform>();
        panel.SetParent(root, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(620f, 0f);
        panel.localScale = new Vector3(K, K, 1f);
        var layout = panelGo.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 32, 32);
        layout.spacing = 14f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        panelGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CreateText(panel, "Title", 54f, TextAlignmentOptions.Center);
        UiTheme.StyleTitle(title, 54f, win ? UiTheme.Gold : UiTheme.Bad);
        title.text = win ? "Victory" : "Defeat";

        TextMeshProUGUI subtitle = CreateText(panel, "Reason", 20f, TextAlignmentOptions.Center);
        subtitle.text = string.IsNullOrWhiteSpace(reason) ? (win ? "The war is won." : "The war is lost.") : reason;

        TextMeshProUGUI table = CreateText(panel, "Scores", 18f, TextAlignmentOptions.Left);
        table.text = BuildScoreTable();

        var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        var buttonsRect = buttons.GetComponent<RectTransform>();
        buttonsRect.SetParent(panel, false);
        var row = buttons.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 20f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = false;
        var rowLayout = buttons.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 52f;

        CreateButton(buttonsRect, "Return to title", ReturnToTitle);
        CreateButton(buttonsRect, "Quit", Application.Quit);
    }

    private string BuildScoreTable()
    {
        Game game = Game.Instance;
        var sb = new StringBuilder();
        foreach (PlayableLeader leader in (objectives != null ? objectives.AllLeaders() : Enumerable.Empty<PlayableLeader>()))
        {
            string you = game != null && leader == game.player ? " <size=80%>(you)</size>" : string.Empty;
            string status = leader.killed ? $"<color={MutedColor}>fallen</color>" : $"{objectives.Score(leader)}/{objectives.MaxScore(leader)} points";
            sb.Append($"<color={UiTheme.GoldHex}>{CampaignObjectives.DisplayLeader(leader)}</color>{you}  —  {status}\n");
            foreach (CampaignObjective o in objectives.GetObjectives(leader))
            {
                bool met = !leader.killed && objectives.IsMet(leader, o);
                sb.Append($"<size=80%><color={(met ? MetColor : MutedColor)}>   {(o.decisive ? "WIN" : "+" + o.points)}  {o.title}</color></size>\n");
            }
        }
        if (game != null) sb.Append($"\n<size=80%><color={MutedColor}>The war lasted {game.turn} turns.</color></size>");
        return sb.ToString().TrimEnd();
    }

    private static void ReturnToTitle()
    {
        GameConfig.ScenarioChosen = false;
        GameConfig.ScenarioToLoad = null;
        GameConfig.SkipIntro = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------------------------------------------------------------- Helpers

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        UiTheme.StyleBody(text, size);
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static void CreateButton(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button), typeof(LayoutElement));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var element = go.GetComponent<LayoutElement>();
        element.preferredWidth = 230f;
        element.preferredHeight = 52f;
        var graphic = go.GetComponent<CinematicPanel>();
        var button = go.GetComponent<Button>();
        button.targetGraphic = graphic;
        button.onClick.AddListener(onClick);
        TextMeshProUGUI text = CreateText(go.GetComponent<RectTransform>(), "Label", 22f, TextAlignmentOptions.Center);
        UiTheme.StyleTitle(text, 24f);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        text.text = label;
    }
}
