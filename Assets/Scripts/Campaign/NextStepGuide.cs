using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "what do I do now" coach: one panel at the top of the screen that turns the current goal
/// (the one the objectives widget shows, CampaignHud.Focused) into a single concrete next action —
/// who acts, where, and what to play — and keeps it current as the player does it:
///   1. "Select Gandalf"              — the character best suited to the goal (a button selects him)
///   2. "Move Gandalf to Bree"        — the place the goal is worked at; its route is drawn on the map
///   3. "Play Sell 5 Leather"         — the step's own card, named, once it can be played (or why
///                                        it can't yet: no goods, no actions left…)
///   …or "Gandalf has acted this turn — end the turn" when there is nothing left to do.
/// Above the instruction, the step's story line: what is happening in the tale.
/// A first line names the player's realm (how many PCs and characters they hold, in their colour)
/// so it is always clear what is theirs.
/// </summary>
public sealed class NextStepGuide : MonoBehaviour
{
    private const float Refresh = 0.4f;

    private static NextStepGuide instance;
    private Game game;
    private RectTransform canvasRoot;
    private RectTransform panel;
    private CanvasGroup group;
    private TextMeshProUGUI eyebrow, story, action, detail;
    private Button button;
    private TextMeshProUGUI buttonLabel;
    private System.Action onButton;
    private float nextRefresh;

    public static void Attach(Game owner)
    {
        if (instance != null || owner == null) return;
        instance = owner.gameObject.AddComponent<NextStepGuide>();
        instance.game = owner;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + Refresh;
        Evaluate();
    }

    // ── Deciding the next action ────────────────────────────────────────────────────────────

    private void Evaluate()
    {
        PlayableLeader player = game != null ? game.player : null;
        bool show = player != null && game.started && !game.GameOver && game.IsPlayerCurrentlyPlaying()
            && !(ObjectiveGuide.Instance != null && ObjectiveGuide.Instance.TourRunning);
        CampaignObjective goal = show ? CampaignHud.Focused : null;
        CampaignObjectives objectives = CampaignObjectives.Instance;
        // A step done by a move (arriving somewhere) is recorded now, not at the next action
        // or turn: its message and new cards come at once and the guide moves on.
        if (goal != null && objectives != null && goal.milestone && objectives.IsActive(player, goal) && objectives.IsMet(player, goal))
        {
            objectives.CheckMilestones();
            goal = CampaignHud.Focused;
        }
        if (goal == null || objectives == null || objectives.IsMet(player, goal) && !goal.milestone)
        {
            Hide();
            return;
        }
        if (!EnsurePanel()) return;

        List<Character> mine = player.controlledCharacters.Where(c => c != null && !c.killed && c.hex != null).ToList();
        // Campaign steps: the same character and place the AI would use (CampaignAI).
        Character who = (goal.milestone ? CampaignAI.BestFor(player, goal) : null) ?? PickActor(goal, mine);
        if (who == null) { Hide(); return; }
        Hex where = (goal.milestone ? CampaignAI.PlaceFor(player, who, goal) : null) ?? PickPlace(player, goal, who, objectives);

        string track = goal.milestone ? CampaignObjectives.TrackOf(goal) : "Objective";
        eyebrow.text = $"{UiTheme.Eyebrow(track)}  <size=80%><color={UiTheme.MutedHex}>{goal.title} · {Realm(player)}</color></size>";
        string tale = !string.IsNullOrWhiteSpace(goal.story) ? goal.story : goal.description;
        story.text = string.IsNullOrWhiteSpace(tale) ? string.Empty : $"<i>{tale.Trim()}</i>";
        Character selected = Board.Instance != null ? Board.Instance.selectedCharacter : null;
        string whoName = DisplayNames.Format(who.characterName);

        if (who.ActionsLeftThisTurn <= 0 && who.hex == where)
        {
            Set($"{whoName} has acted this turn", "End the turn; carry on next turn.", null, null);
        }
        else if (selected != who)
        {
            Set($"Select {whoName}", $"{Why(goal)}", $"Select {whoName}", () => Board.Instance?.SelectCharacter(who));
        }
        else if (where != null && who.hex != where && !where.IsHexRevealed())
        {
            // Somewhere not yet explored: a heading, not a pin on a map the player hasn't seen.
            string place = PlaceName(where);
            string heading = Heading(who.hex, where);
            Set($"Travel {heading} to find {place}", $"{place} lies {heading}, beyond the lands you know. Walk that way and it will show itself when you draw near.",
                "Which way?", () => BoardNavigator.Instance?.LookAt(Vector3.Lerp(who.hex.transform.position, where.transform.position, 0.5f), 0.8f));
        }
        else if (where != null && who.hex != where)
        {
            string place = PlaceName(where);
            Set($"Move {whoName} to {place}", $"Click {place} on the map to walk there; \"Show me\" finds it for you.",
                "Show me", () => BoardNavigator.Instance?.LookAt(where.transform.position, 0.8f));
        }
        else
        {
            NextPlay(player, goal, who, objectives);
        }
        Show();
    }

    // At the place: name the step's card when it can be played, or say why it can't yet.
    private void NextPlay(PlayableLeader player, CampaignObjective goal, Character who, CampaignObjectives objectives)
    {
        string whoName = DisplayNames.Format(who.characterName);
        string how = string.IsNullOrWhiteSpace(goal.how) ? objectives.HowTo(player, goal) : goal.how;
        string progress = $"  <color={UiTheme.MutedHex}>({objectives.ProgressText(player, goal)})</color>";
        List<CardData> tools = ToolsInHand(player, goal, objectives);
        List<CardData> live = tools.Where(c => SituationCardsUI.IsOfferPlayable(c, who)).ToList();
        if (live.Count > 0)
        {
            string more = live.Count > 1 ? $" <size=70%><color={UiTheme.MutedHex}>or {live.Count - 1} more like it</color></size>" : string.Empty;
            Set($"Play {live[0].name}{more}", $"First card in your hand: click it.{progress}", null, null);
        }
        else if (who.ActionsLeftThisTurn <= 0)
            Set($"{whoName} has acted this turn", $"End the turn; the step waits for you.{progress}", null, null);
        else if (goal.type == "Gold")
            Set("Let the purse fill", $"A Sell card needs 5 of one good: you hold {GoodsLine(player)}. Your PCs pay gold every turn, so end the turn.{progress}", null, null);
        else if (tools.Count > 0)
        {
            CardData card = tools.OrderBy(c => Shortfall(player, c).Count).First();
            string missing = Missing(player, who, card);
            Set(missing != null ? $"{card.name} needs more" : $"{card.name} can't be played here",
                missing != null ? $"{missing}{progress}" : $"{how}{progress}", null, null);
        }
        else
            Set(goal.title, $"{how}{progress}", null, null);
    }

    // The step's tools in the player's hand (and Attack, for a battle step).
    private static List<CardData> ToolsInHand(PlayableLeader player, CampaignObjective goal, CampaignObjectives objectives)
    {
        var result = new List<CardData>();
        DeckManager deck = DeckManager.Instance;
        if (deck == null) return result;
        IReadOnlyList<CampaignObjective> steps = objectives.GetMilestones(player);
        int index = -1;
        for (int i = 0; i < steps.Count; i++) if (steps[i] == goal) { index = i; break; }
        foreach (CardData card in deck.GetHand(player))
        {
            if (card == null) continue;
            bool attack = goal.type == "Defeat" && string.Equals(card.name, "Attack", System.StringComparison.OrdinalIgnoreCase);
            if (attack || index >= 0 && objectives.ToolStage(player, card, deck.CardSource(card)) == index) result.Add(card);
        }
        return result;
    }

    // What a card still asks for that the player lacks: skill first, then goods (with how to
    // get them), else null.
    private static string Missing(PlayableLeader player, Character who, CardData card)
    {
        string whoName = DisplayNames.Format(who.characterName);
        if (who.GetCommander() < card.commanderSkillRequired) return $"It takes a commander of level {card.commanderSkillRequired}; {whoName} is level {who.GetCommander()}.";
        if (who.GetAgent() < card.agentSkillRequired) return $"It takes an agent of level {card.agentSkillRequired}.";
        if (who.GetEmmissary() < card.emissarySkillRequired) return $"It takes an emissary of level {card.emissarySkillRequired}.";
        if (who.GetMage() < card.mageSkillRequired) return $"It takes a mage of level {card.mageSkillRequired}.";
        List<(string good, int need, int have)> short_ = Shortfall(player, card);
        if (short_.Count == 0) return null;
        string cost = string.Join(" ", short_.Select(x => $"{x.need - x.have}<sprite name=\"{x.good}\">"));
        return $"You are short of {cost}. Play a Buy card at one of your PCs, or wait: your PCs pay their goods at the start of each turn.";
    }

    private static List<(string good, int need, int have)> Shortfall(PlayableLeader player, CardData card)
    {
        var result = new List<(string, int, int)>();
        void Check(string good, int need, int have) { if (need > have) result.Add((good, need, have)); }
        Check("gold", card.GetTotalGoldCost(), player.goldAmount);
        Check("leather", card.leatherRequired, player.leatherAmount);
        Check("timber", card.timberRequired, player.timberAmount);
        Check("mounts", card.mountsRequired, player.mountsAmount);
        Check("iron", card.ironRequired, player.ironAmount);
        Check("steel", card.steelRequired, player.steelAmount);
        Check("mithril", card.mithrilRequired, player.mithrilAmount);
        return result;
    }

    private static string GoodsLine(PlayableLeader player)
    {
        var goods = new[] { ProducesEnum.leather, ProducesEnum.timber, ProducesEnum.mounts, ProducesEnum.iron, ProducesEnum.steel, ProducesEnum.mithril };
        return string.Join(", ", goods.Select(g => $"{player.GetResourceAmount(g)}<sprite name=\"{g}\">"));
    }

    // The character for the goal: one whose skills suit it, most skilled first; any character else.
    private static Character PickActor(CampaignObjective goal, List<Character> mine)
    {
        if (mine.Count == 0) return null;
        string track = CampaignObjectives.TrackOf(goal).ToLowerInvariant();
        if (goal.type == "Captured")
        {
            Character commander = mine.Where(c => c.IsArmyCommander()).OrderByDescending(c => c.GetCommander()).FirstOrDefault();
            if (commander != null) return commander;
        }
        int Skill(Character c) => track switch
        {
            "commander" => c.GetCommander(),
            "agent" => c.GetAgent(),
            "emissary" => c.GetEmmissary(),
            "mage" => c.GetMage(),
            _ => c.GetCommander() + c.GetAgent() + c.GetEmmissary() + c.GetMage(),
        };
        return mine.Where(c => CampaignObjectives.SuitsTrack(c, goal)).OrderByDescending(Skill).ThenByDescending(c => c.ActionsLeftThisTurn).FirstOrDefault()
            ?? mine[0];
    }

    // Where the goal is worked at, for that character.
    private static Hex PickPlace(PlayableLeader player, CampaignObjective goal, Character who, CampaignObjectives objectives)
    {
        Vector3 from = who.hex.transform.position;
        Hex Nearest(IEnumerable<Hex> hexes) => hexes.Where(h => h != null).OrderBy(h => (h.transform.position - from).sqrMagnitude).FirstOrDefault();
        List<Hex> targets = objectives.GetTargetHexes(goal);
        if (targets.Count > 0) return Nearest(targets);
        switch (goal.type)
        {
            case "Gold":
            case "Loyalty":
            case "Armies":
            case "Troops":
            case "Fortifications":
            {
                // At one of the player's own PCs.
                Hex own = Nearest(player.controlledPcs.Where(pc => pc != null && pc.owner == player).Select(pc => pc.hex));
                return own ?? who.hex;
            }
            case "Captured":
            {
                Hex enemy = Nearest(Board.Instance != null ? Board.Instance.hexes.Values.Where(h => h != null && h.IsHexRevealed()
                    && h.GetPCData() is PC pc && pc.owner != null && pc.owner != player && pc.owner.alignment != player.alignment).Select(h => h) : Enumerable.Empty<Hex>());
                return enemy ?? who.hex;
            }
            case "Characters":
                return objectives.GetFocusHex(player, goal) ?? who.hex;
            default:
                return who.hex;
        }
    }

    private static string Why(CampaignObjective goal) => goal.type switch
    {
        "HoldPC" => "Anyone can take a place by playing its card there.",
        "ReachPC" => "Anyone can make the journey.",
        "Recruit" when CampaignObjectives.FindNpl(goal.character) != null => "An emissary wins a realm over with A Friend or Three.",
        "Recruit" => "Anyone can recruit a hero at their home.",
        "Captured" => "A commander with an army takes enemy cities.",
        "Defeat" => "A commander with an army marches onto the enemy and plays Attack.",
        "Gold" or "Loyalty" => "An emissary works your own cities.",
        "Armies" or "Troops" => "A commander raises armies in your cities.",
        "AgentLevels" => "An agent learns by acting.",
        "Artifacts" => "A mage finds artifacts.",
        "Characters" => "Any character can recruit a hero at their home.",
        _ => "Select them to see what they can do.",
    };

    // The compass heading from one hex to another, as the board is drawn (north up).
    private static string Heading(Hex from, Hex to)
    {
        Vector3 d = to.transform.position - from.transform.position;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
        int index = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 45f) % 8;
        return names[index];
    }

    private static string PlaceName(Hex hex)
    {
        PC pc = hex != null ? hex.GetPCData() : null;
        return pc != null ? DisplayNames.Format(pc.pcName) : "the marked place";
    }

    private static string Realm(PlayableLeader player)
    {
        int pcs = player.controlledPcs.Count(pc => pc != null && pc.owner == player);
        int chars = player.controlledCharacters.Count(c => c != null && !c.killed);
        string colour = ColorUtility.ToHtmlStringRGB(player.nationColor);
        return $"<color=#{colour}>■</color> your realm: {pcs} {(pcs == 1 ? "city" : "cities")}, {chars} {(chars == 1 ? "character" : "characters")}";
    }

    // ── Panel ───────────────────────────────────────────────────────────────────────────────

    private void Set(string title, string body, string buttonText, System.Action click)
    {
        action.text = title;
        detail.text = body;
        onButton = click;
        button.gameObject.SetActive(buttonText != null);
        if (buttonText != null) buttonLabel.text = buttonText;
    }

    private void Show()
    {
        if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
        group.alpha = Mathf.MoveTowards(group.alpha, 1f, 0.5f);
        KeepMessagesBelow(true);
    }

    private void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        KeepMessagesBelow(false);
    }

    // The top-centre notices (MessageDisplay) sit where this panel does: while it shows, they
    // drop below it; when it hides, they go back.
    private RectTransform messages;
    private Vector3? messagesHome;

    private void KeepMessagesBelow(bool below)
    {
        if (messages == null)
        {
            MessageDisplay display = FindAnyObjectByType<MessageDisplay>();
            if (display == null) return;
            messages = display.transform as RectTransform;
        }
        if (messages == null) return;
        if (!below || panel == null)
        {
            if (messagesHome.HasValue) messages.position = messagesHome.Value;
            messagesHome = null;
            return;
        }
        if (!messagesHome.HasValue) messagesHome = messages.position;
        var corners = new Vector3[4];
        panel.GetWorldCorners(corners);
        float panelBottom = corners[0].y;
        messages.position = messagesHome.Value;
        // The root is a 0x0 anchor: measure the pill drawn behind the text.
        ImageFitToTMP pill = messages.GetComponentInChildren<ImageFitToTMP>(true);
        RectTransform box = pill != null ? pill.transform as RectTransform : messages;
        box.GetWorldCorners(corners);
        float gap = (corners[1].y - corners[0].y) * 0.05f + 8f * panel.lossyScale.y;
        float overlap = corners[1].y - (panelBottom - gap);
        if (overlap > 0f) messages.position = messagesHome.Value - new Vector3(0f, overlap, 0f);
    }

    private bool EnsurePanel()
    {
        if (panel != null) return true;
        Layout layout = FindAnyObjectByType<Layout>();
        Canvas canvas = layout != null ? layout.GetComponentInParent<Canvas>()?.rootCanvas : null;
        canvasRoot = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRoot == null) return false;
        float k = Mathf.Max(0.5f, canvasRoot.rect.height / 1080f);

        var go = new GameObject("NextStepGuide", typeof(RectTransform), typeof(CanvasGroup));
        panel = go.GetComponent<RectTransform>();
        panel.SetParent(canvasRoot, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -158f * k); // under the leaders and their realm dots
        panel.sizeDelta = new Vector2(680f * k, 140f * k);
        group = go.GetComponent<CanvasGroup>();

        var face = new GameObject("Face", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel)).GetComponent<CinematicPanel>();
        face.rectTransform.SetParent(panel, false);
        face.rectTransform.anchorMin = Vector2.zero;
        face.rectTransform.anchorMax = Vector2.one;
        face.rectTransform.offsetMin = face.rectTransform.offsetMax = Vector2.zero;
        face.fillTop = new Color(0.07f, 0.065f, 0.05f, 0.92f);
        face.fillBottom = new Color(0.03f, 0.028f, 0.022f, 0.95f);
        face.ornament = 0f;
        face.raycastTarget = false;

        eyebrow = Text("Eyebrow", 15f * k, false, new Vector2(18f * k, -10f * k), new Vector2(-18f * k, -30f * k));
        story = Text("Story", 15f * k, false, new Vector2(18f * k, -30f * k), new Vector2(-18f * k, -62f * k));
        story.color = new Color(0.85f, 0.79f, 0.66f);
        story.textWrappingMode = TextWrappingModes.Normal;
        action = Text("Action", 26f * k, true, new Vector2(18f * k, -64f * k), new Vector2(-170f * k, -96f * k));
        detail = Text("Detail", 15f * k, false, new Vector2(18f * k, -96f * k), new Vector2(-170f * k, -134f * k));
        detail.textWrappingMode = TextWrappingModes.Normal;

        var buttonGo = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button));
        var br = buttonGo.GetComponent<RectTransform>();
        br.SetParent(panel, false);
        br.anchorMin = br.anchorMax = new Vector2(1f, 0.5f);
        br.pivot = new Vector2(1f, 0.5f);
        br.anchoredPosition = new Vector2(-16f * k, -8f * k);
        br.sizeDelta = new Vector2(146f * k, 40f * k);
        var bface = buttonGo.GetComponent<CinematicPanel>();
        bface.fillTop = new Color(0.30f, 0.23f, 0.11f, 0.95f);
        bface.fillBottom = new Color(0.18f, 0.13f, 0.06f, 0.95f);
        bface.ornament = 0f;
        button = buttonGo.GetComponent<Button>();
        button.targetGraphic = bface;
        button.onClick.AddListener(() => { Sounds.Instance?.PlayUiClick(); onButton?.Invoke(); });
        buttonLabel = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        buttonLabel.rectTransform.SetParent(br, false);
        buttonLabel.rectTransform.anchorMin = Vector2.zero;
        buttonLabel.rectTransform.anchorMax = Vector2.one;
        buttonLabel.rectTransform.offsetMin = buttonLabel.rectTransform.offsetMax = Vector2.zero;
        UiTheme.StyleTitle(buttonLabel, 17f * k, UiTheme.Text);
        buttonLabel.alignment = TextAlignmentOptions.Center;
        buttonLabel.enableAutoSizing = true;
        buttonLabel.fontSizeMin = 11f * k;
        buttonLabel.fontSizeMax = 17f * k;
        buttonLabel.raycastTarget = false;
        return true;
    }

    private TextMeshProUGUI Text(string name, float size, bool title, Vector2 topLeft, Vector2 bottomRight)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        t.rectTransform.SetParent(panel, false);
        t.rectTransform.anchorMin = new Vector2(0f, 1f);
        t.rectTransform.anchorMax = new Vector2(1f, 1f);
        t.rectTransform.offsetMin = new Vector2(topLeft.x, bottomRight.y);
        t.rectTransform.offsetMax = new Vector2(bottomRight.x, topLeft.y);
        if (title) UiTheme.StyleTitle(t, size, new Color(1f, 0.9f, 0.62f)); else UiTheme.StyleBody(t, size, UiTheme.Text);
        t.enableAutoSizing = true;
        t.fontSizeMin = size * 0.6f;
        t.fontSizeMax = size;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        return t;
    }
}
