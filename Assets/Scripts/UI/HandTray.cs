using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The human player's always-visible card tray, replacing the hidden per-character bloom wheel.
/// Left group: the leader's hand. On the campaign chain that is every card unlocked so far (no
/// draw, no limit); otherwise it is drawn from their deck each turn (DeckManager.DrawHandForTurn).
/// Only the cards the selected character can play where they stand are shown.
/// Right group: situational opportunities for the selected character where they stand.
/// Every play spends one of the selected character's actions (one a turn, two for the champion).
/// Hover = full card preview, left click = play with the selected character,
/// right click (hand only, off the campaign chain) = discard for a random resource.
///
/// Layout: cards are placed by hand on a shallow arc (no layout group). Pointer input goes to a
/// separate row of fixed, non-overlapping hit strips, so lifting, scaling or re-ordering a card
/// visual can never move what is under the pointer (the old layout-group version re-ordered the
/// slots on hover, which slid a neighbour under the cursor and made cards swap endlessly).
/// </summary>
public class HandTray : MonoBehaviour
{
    public static HandTray Instance { get; private set; }

    private const int MaxOpportunities = 3;
    private const float TokenWidth = 120f;
    private const float TokenHeight = 170f;
    private const float CardWidth = 132f;
    private const float CardHeight = 186f;
    private const float GroupGap = 34f;
    internal const float HoverLift = 34f;
    internal const float HoverScale = 1.16f;
    private const float HoverSpread = 26f;

    private RectTransform canvasRoot;
    private RectTransform root;
    // The HUD canvas runs at roughly 2 units per 1080p pixel; panels are authored in 1080p
    // pixels and the root is scaled by this so text and cards read at a sensible size.
    private float K => canvasRoot != null ? Mathf.Max(0.5f, canvasRoot.rect.height / 1080f) : 1f;
    private RectTransform visualsLayer;
    private RectTransform hitLayer;
    private TextMeshProUGUI header;
    private readonly List<HandTraySlot> slots = new();
    private readonly List<GameObject> extras = new();
    private readonly List<SituationCardOffer> opportunities = new();
    private readonly HashSet<string> playedThisTurn = new();
    private int playedTurn = -1;
    private Character opportunitiesFor;
    private Hex opportunitiesHex;
    private Character lastSelected;
    private int lastActionsLeft = -1;
    private int lastTurn = -1;
    private int handSignature;
    private bool dirty = true;
    private bool playing;
    private HandTraySlot hovered;
    private float nextPlayabilityRefresh;
    private static Illustrations illustrations;

    public static void Ensure()
    {
        if (Instance != null) return;
        var go = new GameObject("HandTray");
        Instance = go.AddComponent<HandTray>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Shows the full-card preview for the tray's Nth card (used by AutoTour).</summary>
    public bool DebugPreview(int index)
    {
        if (index < 0) { CardCenterPreview.Instance?.HidePreview(); return false; }
        if (index >= slots.Count || slots[index] == null) return false;
        OnHover(slots[index], true);
        return true;
    }

    public void SetOpportunities(List<SituationCardOffer> offers, Character character)
    {
        opportunities.Clear();
        if (offers != null)
        {
            // Only situational offers: deck cards suggested by the AI would bypass the hand.
            // ...and nothing already played this turn (situational cards are re-offerable copies).
            int turn = Game.Instance != null ? Game.Instance.turn : 0;
            if (turn != playedTurn) { playedTurn = turn; playedThisTurn.Clear(); }
            opportunities.AddRange(offers.Where(o => o?.Card != null && o.Source == SituationCardOfferSource.Situation
                && !playedThisTurn.Contains(o.Card.name)).Take(MaxOpportunities));
        }
        opportunitiesFor = character;
        opportunitiesHex = character != null ? character.hex : null;
        dirty = true;
    }

    private void Update()
    {
        Game game = Game.Instance;
        if (game == null || !game.started || game.player == null || game.GameOver)
        {
            if (root != null) root.gameObject.SetActive(false);
            return;
        }
        if (!EnsureBuilt()) return;

        PlayableLeader player = game.player;
        bool humanTurn = game.IsPlayerCurrentlyPlaying() && !game.IsPlayerAutoplayEnabledFor(player);
        root.gameObject.SetActive(humanTurn);
        if (!humanTurn) return;

        DeckManager deck = DeckManager.Instance;
        if (deck == null || !deck.HasDeckFor(player)) return;
        if (deck.DrawHandForTurn(player, game.turn, deck.GetHandSize())) dirty = true;

        Character selected = Board.Instance != null ? Board.Instance.selectedCharacter : null;
        if (selected != lastSelected) { lastSelected = selected; dirty = true; }
        if (opportunitiesFor != null && (opportunitiesFor != selected || opportunitiesFor.hex != opportunitiesHex))
        {
            opportunities.Clear();
            opportunitiesFor = null;
            dirty = true;
        }
        int actionsLeft = selected != null ? selected.ActionsLeftThisTurn : -1;
        if (actionsLeft != lastActionsLeft || game.turn != lastTurn) { lastActionsLeft = actionsLeft; lastTurn = game.turn; dirty = true; }
        int signature = ComputeHandSignature(deck.GetHand(player));
        if (signature != handSignature) { handSignature = signature; dirty = true; }

        // Don't rebuild under a hovered card unless something actually changed the hand.
        if (expandedFamily != null && Input.GetKeyDown(KeyCode.Escape)) { expandedFamily = null; dirty = true; }
        if (dirty && !playing) Rebuild(player, selected);
        else if (Time.unscaledTime >= nextPlayabilityRefresh && !playing) RefreshTints(selected);
    }

    private static int ComputeHandSignature(IReadOnlyList<CardData> hand)
    {
        unchecked
        {
            int h = 17;
            foreach (CardData c in hand) h = h * 31 + (c != null ? c.GetHashCode() : 0);
            return h * 31 + hand.Count;
        }
    }

    // ------------------------------------------------------------------ Layout

    private bool EnsureBuilt()
    {
        if (root != null) return true;
        Layout layout = FindAnyObjectByType<Layout>();
        Canvas canvas = layout != null ? layout.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return false;
        canvasRoot = canvas.rootCanvas.transform as RectTransform;

        // No tray box (cinematic HUD): the hand floats over the bottom gradient.
        var go = new GameObject("HandTrayPanel", typeof(RectTransform));
        root = go.GetComponent<RectTransform>();
        root.SetParent(canvasRoot, false);
        root.SetSiblingIndex(Mathf.Min(3, canvasRoot.childCount - 1));
        root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
        root.pivot = new Vector2(0.5f, 0f);

        header = CreateText(root, "Header", 14f, TextAlignmentOptions.Bottom);
        header.textWrappingMode = TextWrappingModes.NoWrap;
        header.overflowMode = TextOverflowModes.Ellipsis;
        var hr = header.rectTransform;
        hr.anchorMin = new Vector2(0f, 1f);
        hr.anchorMax = new Vector2(1f, 1f);
        hr.pivot = new Vector2(0.5f, 1f);
        hr.offsetMin = new Vector2(0f, -22f);
        hr.offsetMax = Vector2.zero;

        visualsLayer = CreateLayer("Cards");
        hitLayer = CreateLayer("HitStrips");
        return true;
    }

    private RectTransform CreateLayer(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(root, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = new Vector2(0f, 4f);
        return rt;
    }

    // Centred on the screen, as wide as fits symmetrically between the selected-character panel
    // (left) and the minimap column (right). Returns the available width in tray (1080p) units.
    private float PlaceRoot(float cardHeight)
    {
        Rect r = canvasRoot.rect;
        float left = r.width * 0.33f, right = r.width * 0.855f;
        float dockRight = HudDirector.DockRightColumnLeft(canvasRoot);
        if (dockRight > 0f) right = dockRight - 12f;
        RectTransform leftPanel = FindAnyObjectByType<SelectedCharacterIcon>()?.transform as RectTransform;
        if (leftPanel != null && leftPanel.gameObject.activeInHierarchy) left = Mathf.Max(left * 0.8f, CanvasX(leftPanel, right: true) + 12f);
        float centre = r.width * 0.5f;
        float width = Mathf.Max(200f, 2f * Mathf.Min(centre - left, right - centre));
        root.localScale = new Vector3(K, K, 1f);
        root.anchoredPosition = new Vector2(centre, 10f);
        root.sizeDelta = new Vector2(width / K, cardHeight + 34f);
        return width / K;
    }

    private float CanvasX(RectTransform rt, bool right)
    {
        Vector3[] c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector3 local = canvasRoot.InverseTransformPoint(right ? c[2] : c[0]);
        return local.x - canvasRoot.rect.xMin;
    }

    // ------------------------------------------------------------------ Content

    private void Rebuild(PlayableLeader player, Character selected)
    {
        dirty = false;
        if (hovered != null) { CardCenterPreview.Instance?.HidePreview(); hovered = null; }
        foreach (HandTraySlot s in slots) if (s != null) s.DestroyParts();
        slots.Clear();
        foreach (GameObject g in extras) if (g != null) Destroy(g);
        extras.Clear();

        DeckManager deck = DeckManager.Instance;
        IReadOnlyList<CardData> hand = deck.GetHand(player);
        float available = PlaceRoot(CardHeight);

        string selectedName = selected != null && selected.GetOwner() == player ? selected.characterName : null;
        string text = selectedName != null
            ? $"{selectedName}   <color={UiTheme.MutedHex}>·</color>   {UiTheme.Eyebrow(selected.IsChampion ? "Champion actions" : "Actions")} <color={UiTheme.GoldHex}>{selected.ActionsLeftThisTurn}</color><color={UiTheme.MutedHex}>/{selected.MaxActionsPerTurn}</color>"
            : $"<color={UiTheme.MutedHex}>Select a character to play cards</color>";

        // Only cards the selected character can play right now are shown; the rest stay in hand
        // (counted in the header) until the situation, character or resources make them live.
        // Look-alike cards (every Buy, every Sell, every army...) sit in one stack: the fan shows
        // one card per family, and clicking a stack fans out just that family.
        var playable = new List<CardData>();
        int held = 0;
        foreach (CardData card in hand)
        {
            if (IsPlayable(card, selected)) playable.Add(card);
            else held++;
        }
        if (selected != expandedFor || expandedFamily != null && !playable.Any(c => FamilyOf(c) == expandedFamily)) expandedFamily = null;
        if (selected != expandedFor) page = 0;
        expandedFor = selected;

        // Everything playable, most useful first; the fan then shows one page of it.
        var entries = new List<(SituationCardOffer offer, bool isHand, string family, int count)>();
        if (expandedFamily != null)
        {
            foreach (CardData card in playable.Where(c => FamilyOf(c) == expandedFamily))
                entries.Add((new SituationCardOffer(card, SituationCardOfferSource.Hand, true), true, null, 1));
        }
        else
        {
            foreach (IGrouping<string, CardData> family in playable
                .OrderBy(c => Priority(player, c))
                .GroupBy(FamilyOf))
            {
                int count = family.Count();
                entries.Add((new SituationCardOffer(family.First(), SituationCardOfferSource.Hand, true), true, count > 1 ? family.Key : null, count));
            }
            // What is on offer at this hex follows the hand. An offer that is already a card in
            // the hand would only show it twice.
            HashSet<string> inHand = new(playable.Select(c => c.name), System.StringComparer.OrdinalIgnoreCase);
            foreach (SituationCardOffer offer in opportunities)
                if (!inHand.Contains(offer.Card.name) && IsPlayable(offer.Card, selected))
                    entries.Add((new SituationCardOffer(offer.Card, offer.Source, true), false, null, 1));
        }

        int pages = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)PageSize));
        page = Mathf.Clamp(page, 0, pages - 1);
        foreach (var e in entries.Skip(page * PageSize).Take(PageSize))
        {
            HandTraySlot slot = CreateSlot(e.offer, isHand: e.isHand);
            if (e.family != null) slot.MakeStack(e.family, e.count);
            slots.Add(slot);
        }
        int handCount = slots.Count(sl => sl.IsHand);
        if (expandedFamily != null) extras.Add(CreateBackChip(FamilyName(expandedFamily), entries.Count));
        if (pages > 1) AddPager(pages);

        bool campaign = DeckManager.IsCampaignLeader(player);
        if (selected != null && slots.Count == 0)
            // Nothing to play: say why, and where the next step is.
            text += selected.ActionsLeftThisTurn <= 0
                ? $"   <color={UiTheme.MutedHex}>·   no actions left this turn: end the turn</color>"
                : $"   <color={UiTheme.MutedHex}>·   nothing to play here: the </color><color={UiTheme.GoldHex}>Next</color><color={UiTheme.MutedHex}> panel above shows where to go</color>";
        else if (held > 0) text += $"   <color={UiTheme.MutedHex}>·   +{held} {(campaign ? "more in your deck" : "in hand")}, not playable here</color>";
        header.text = text;

        if (slots.Count == 0)
            extras.Add(CreateEmptyNote(selected == null ? "Select one of your characters" : "Nothing this character can play here — move, or pick another character"));

        LayoutFan(available, handCount);
        nextPlayabilityRefresh = Time.unscaledTime + 0.5f;
    }

    // Shallow arc: cards overlap when space is short, outer cards tilt out and sit a little
    // lower. Opportunities ("Here") follow the hand after a small gap. Pivots are at the card's
    // bottom centre so the tilt reads like a held hand.
    private void LayoutFan(float available, int handCount)
    {
        int n = slots.Count;
        if (n == 0) return;
        int opp = n - handCount;
        float gap = handCount > 0 && opp > 0 ? GroupGap : 0f;
        float maxStep = CardWidth * 0.92f;
        float step = n <= 1 ? 0f : Mathf.Min(maxStep, (available - CardWidth * 1.3f - gap) / (n - 1));
        // The campaign hand can hold many playable cards at once; let them overlap further.
        step = Mathf.Max(step, CardWidth * 0.3f);
        float span = step * (n - 1) + gap;
        float maxAngle = Mathf.Min(9f, 1.8f * n);
        float maxDrop = Mathf.Min(16f, 3f * n);

        float[] xs = new float[n];
        for (int i = 0; i < n; i++) xs[i] = -span * 0.5f + step * i + (i >= handCount ? gap : 0f);

        for (int i = 0; i < n; i++)
        {
            float t = n <= 1 ? 0f : (xs[i] / Mathf.Max(1f, span * 0.5f)); // -1 .. 1
            slots[i].SetRest(xs[i], -t * maxAngle, -t * t * maxDrop, i);

            // Hit strip: from halfway to the left neighbour to halfway to the right one.
            float leftEdge = i == 0 ? xs[i] - CardWidth * 0.5f : (xs[i - 1] + xs[i]) * 0.5f;
            float rightEdge = i == n - 1 ? xs[i] + CardWidth * 0.5f : (xs[i] + xs[i + 1]) * 0.5f;
            slots[i].SetHitStrip(leftEdge, rightEdge, CardHeight + HoverLift + 24f);
        }

        if (opp > 0 && handCount > 0)
        {
            float x = (xs[handCount - 1] + xs[handCount]) * 0.5f;
            extras.Add(CreateHereLabel(x));
        }
        ApplyHoverOffsets();
    }

    private void ApplyHoverOffsets()
    {
        int h = hovered != null ? slots.IndexOf(hovered) : -1;
        for (int i = 0; i < slots.Count; i++)
        {
            float push = 0f;
            if (h >= 0 && i != h)
            {
                int d = i - h;
                push = Mathf.Sign(d) * HoverSpread / Mathf.Abs(d);
            }
            slots[i].SetTarget(i == h, push);
        }
        // Draw order: left to right, the hovered card on top. Positions don't depend on it.
        foreach (HandTraySlot s in slots) s.transform.SetAsLastSibling();
        if (h >= 0) slots[h].transform.SetAsLastSibling();
    }

    private static bool IsPlayable(CardData card, Character selected)
    {
        if (card == null || selected == null || !selected.isPlayerControlled) return false;
        return SituationCardsUI.IsOfferPlayable(card, selected);
    }

    // Playability changes as resources, positions and actions left change mid-turn.
    private void RefreshTints(Character selected)
    {
        nextPlayabilityRefresh = Time.unscaledTime + 0.5f;
        foreach (HandTraySlot slot in slots)
        {
            if (slot == null) continue;
            bool playable = IsPlayable(slot.Offer.Card, selected);
            if (playable != slot.Playable) slot.SetPlayable(playable);
        }
    }

    // ------------------------------------------------------------------ Card families

    private string expandedFamily;
    private Character expandedFor;

    // The fan never holds more than a page of cards; the rest are a flip away.
    private const int PageSize = 8;
    private int page;

    // What to show first: the open campaign step's cards, then actions and spells (what a
    // character does), then heroes, armies, places and lands, then the markets.
    private static int Priority(PlayableLeader player, CardData card)
    {
        if (IsForOpenStep(player, card)) return 0;
        string family = FamilyOf(card);
        if (family.StartsWith("card:")) return card.GetCardType() is CardTypeEnum.Action or CardTypeEnum.Spell ? 1 : 2;
        return family switch
        {
            "Character" => 3,
            "Army" => 4,
            "PC" or "Land" => 5,
            _ => 6,
        };
    }

    private void Flip(int delta)
    {
        page += delta;
        dirty = true;
        Sounds.Instance?.PlayUiHover();
    }

    // "‹  2 / 5  ›" under the header, over the fan's right end.
    private void AddPager(int pages)
    {
        var go = new GameObject("Pager", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(hitLayer, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(150f, 30f);
        rt.anchoredPosition = new Vector2(0f, CardHeight + HoverLift + 30f + (expandedFamily != null ? 36f : 0f));
        extras.Add(go);

        TextMeshProUGUI label = CreateText(rt, "Page", 13f, TextAlignmentOptions.Center);
        label.text = $"<color={UiTheme.MutedHex}>{page + 1} / {pages}</color>";
        Stretch(label.rectTransform);
        PagerArrow(rt, "‹", -1, page > 0);
        PagerArrow(rt, "›", 1, page < pages - 1);
    }

    private void PagerArrow(RectTransform parent, string glyph, int delta, bool live)
    {
        var go = new GameObject(delta < 0 ? "Previous" : "Next", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(delta < 0 ? 0f : 1f, 0.5f);
        rt.sizeDelta = new Vector2(34f, 30f);
        var face = go.GetComponent<CinematicPanel>();
        face.ornament = 0f;
        face.raycastTarget = true;
        var button = go.GetComponent<Button>();
        button.targetGraphic = face;
        button.interactable = live;
        if (live) button.onClick.AddListener(() => Flip(delta));
        TextMeshProUGUI label = CreateText(rt, "Glyph", 18f, TextAlignmentOptions.Center);
        UiTheme.StyleTitle(label, 18f, live ? UiTheme.Gold : UiTheme.Muted);
        label.text = glyph;
        Stretch(label.rectTransform);
    }

    private void Expand(string family)
    {
        expandedFamily = family;
        page = 0;
        dirty = true;
        Sounds.Instance?.PlayUiClick();
    }

    // Cards that differ only in what they trade or raise share a family; anything else is its own.
    private static string FamilyOf(CardData card)
    {
        string name = card.name ?? string.Empty;
        if (name.StartsWith("Buy ", System.StringComparison.OrdinalIgnoreCase)) return "Buy";
        if (name.StartsWith("Sell ", System.StringComparison.OrdinalIgnoreCase)) return "Sell";
        return card.GetCardType() switch
        {
            CardTypeEnum.Army => "Army",
            CardTypeEnum.PC => "PC",
            CardTypeEnum.Land => "Land",
            CardTypeEnum.Character => "Character",
            _ => "card:" + name,
        };
    }

    private static string FamilyName(string family) => family switch
    {
        "Buy" => "Buy goods",
        "Sell" => "Sell goods",
        "Army" => "Armies",
        "PC" => "Places",
        "Land" => "Lands",
        "Character" => "Heroes",
        _ => family,
    };

    internal static string StackLabel(string family, int count) => $"{FamilyName(family)}  ×{count}";

    // A tool of a campaign step that is open right now: what the guide is asking for.
    private static bool IsForOpenStep(PlayableLeader player, CardData card)
    {
        CampaignObjectives campaign = CampaignObjectives.Instance;
        DeckManager deck = DeckManager.Instance;
        if (campaign == null || deck == null || !campaign.HasCampaign(player)) return false;
        int tool = campaign.ToolStage(player, card, deck.CardSource(card));
        if (tool < 0) return false;
        IReadOnlyList<CampaignObjective> steps = campaign.GetMilestones(player);
        return tool < steps.Count && campaign.IsActive(player, steps[tool]);
    }

    // Above an opened family: its name and the way back to the whole hand.
    private GameObject CreateBackChip(string familyName, int count)
    {
        var go = new GameObject("BackToHand", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(hitLayer, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(300f, 30f);
        rt.anchoredPosition = new Vector2(0f, CardHeight + HoverLift + 30f);
        var face = go.GetComponent<CinematicPanel>();
        face.ornament = 0f;
        face.raycastTarget = true;
        var button = go.GetComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(() => { expandedFamily = null; page = 0; dirty = true; Sounds.Instance?.PlayUiClick(); });
        TextMeshProUGUI label = CreateText(rt, "Label", 13f, TextAlignmentOptions.Center);
        label.text = $"<color={UiTheme.GoldHex}>‹  All cards</color>   <color={UiTheme.MutedHex}>·   {familyName} ({count})</color>";
        Stretch(label.rectTransform);
        return go;
    }

    private GameObject CreateEmptyNote(string message)
    {
        var go = new GameObject("EmptyNote", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(visualsLayer, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(560f, 60f);
        rt.anchoredPosition = new Vector2(0f, 30f);
        TextMeshProUGUI label = CreateText(rt, "Note", 13f, TextAlignmentOptions.Center);
        label.text = $"<color={UiTheme.MutedHex}>{message}</color>";
        Stretch(label.rectTransform);
        return go;
    }

    private GameObject CreateHereLabel(float x)
    {
        var go = new GameObject("Here", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(visualsLayer, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(CardHeight, 18f);
        rt.anchoredPosition = new Vector2(x, CardHeight * 0.5f);
        rt.localEulerAngles = new Vector3(0f, 0f, 90f);
        TextMeshProUGUI label = CreateText(rt, "Label", 12f, TextAlignmentOptions.Center);
        label.text = $"<color={UiTheme.GoodHex}><size=78%><cspace=0.12em>HERE</cspace></size></color>";
        Stretch(label.rectTransform);
        return go;
    }

    private HandTraySlot CreateSlot(SituationCardOffer offer, bool isHand)
    {
        // ---- Visual: the card face, rotated/lifted freely (never part of any layout).
        var visualGo = new GameObject(offer.Card.name, typeof(RectTransform), typeof(CanvasGroup));
        var visual = visualGo.GetComponent<RectTransform>();
        visual.SetParent(visualsLayer, false);
        visual.anchorMin = visual.anchorMax = new Vector2(0.5f, 0f);
        visual.pivot = new Vector2(0.5f, 0f);
        visual.sizeDelta = new Vector2(CardWidth, CardHeight);
        visualGo.GetComponent<CanvasGroup>().blocksRaycasts = false;

        // Soft drop shadow so overlapping cards separate.
        var shadow = CreateGraphic<Image>(visual, "Shadow");
        shadow.sprite = HudSprites.Circle;
        shadow.color = new Color(0f, 0f, 0f, 0.55f);
        var sr = shadow.rectTransform;
        sr.anchorMin = Vector2.zero;
        sr.anchorMax = Vector2.one;
        sr.offsetMin = new Vector2(-16f, -14f);
        sr.offsetMax = new Vector2(16f, 4f);

        var face = CreateGraphic<CinematicPanel>(visual, "Face");
        Stretch(face.rectTransform);
        face.fillTop = new Color(0.10f, 0.09f, 0.075f, 1f);
        face.fillBottom = new Color(0.04f, 0.037f, 0.032f, 1f);
        face.inset = 3f;
        face.ornament = 0f;
        face.lineWidth = 1.4f;

        // Art: full-bleed painting, centre-cropped to the window.
        // Stencil Mask, not RectMask2D: RectMask2D clips to an axis-aligned rect, so on a fanned
        // (rotated) card the art window would stay upright while the frame tilts.
        var windowGo = new GameObject("ArtWindow", typeof(RectTransform), typeof(Image), typeof(Mask));
        windowGo.GetComponent<Image>().raycastTarget = false;
        windowGo.GetComponent<Mask>().showMaskGraphic = false;
        var window = windowGo.GetComponent<RectTransform>();
        window.SetParent(visual, false);
        window.anchorMin = Vector2.zero;
        window.anchorMax = Vector2.one;
        window.offsetMin = new Vector2(6f, 6f);
        window.offsetMax = new Vector2(-6f, -6f);

        bool hidden = offer.Card.IsEncounterCard() && !offer.Card.encounterRevealed;
        Sprite artSprite = hidden ? null : ResolveArt(offer.Card);
        var art = CreateGraphic<Image>(window, "Art");
        art.sprite = artSprite;
        var ar = art.rectTransform;
        ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 0.5f);
        Vector2 windowSize = new(CardWidth - 12f, CardHeight - 12f);
        float aspect = artSprite != null ? artSprite.rect.width / artSprite.rect.height : windowSize.x / windowSize.y;
        ar.sizeDelta = aspect > windowSize.x / windowSize.y
            ? new Vector2(windowSize.y * aspect, windowSize.y)
            : new Vector2(windowSize.x, windowSize.x / aspect);
        // Favour the top of tall art (the plate covers the bottom) without opening a gap.
        ar.anchoredPosition = new Vector2(0f, Mathf.Min(10f, (ar.sizeDelta.y - windowSize.y) * 0.5f));
        if (artSprite == null) art.color = new Color(0.12f, 0.11f, 0.10f, 1f);
        if (hidden)
        {
            TextMeshProUGUI q = CreateText(window, "Unknown", 64f, TextAlignmentOptions.Center);
            UiTheme.StyleTitle(q, 64f, new Color(0.86f, 0.71f, 0.40f, 0.6f));
            q.text = "?";
            Stretch(q.rectTransform);
        }

        // Name plate: dark gradient over the lower art, type eyebrow + name in the display face.
        var plate = CreateGraphic<Image>(window, "Plate");
        plate.sprite = HudSprites.FadeUp;
        plate.color = new Color(1f, 1f, 1f, 0.96f);
        var pr = plate.rectTransform;
        pr.anchorMin = new Vector2(0f, 0f);
        pr.anchorMax = new Vector2(1f, 0f);
        pr.pivot = new Vector2(0.5f, 0f);
        pr.sizeDelta = new Vector2(0f, 84f);

        TextMeshProUGUI type = CreateText(visual, "Type", 9f, TextAlignmentOptions.Bottom);
        type.text = $"<cspace=0.12em>{TypeLabel(offer.Card)}</cspace>";
        type.color = isHand ? UiTheme.Gold : UiTheme.Good;
        type.textWrappingMode = TextWrappingModes.NoWrap;
        var tr = type.rectTransform;
        tr.anchorMin = new Vector2(0f, 0f);
        tr.anchorMax = new Vector2(1f, 0f);
        tr.pivot = new Vector2(0.5f, 0f);
        tr.offsetMin = new Vector2(10f, 46f);
        tr.offsetMax = new Vector2(-10f, 60f);

        TextMeshProUGUI name = CreateText(visual, "Name", 16f, TextAlignmentOptions.Top);
        UiTheme.StyleTitle(name, 16f, UiTheme.Text);
        name.enableAutoSizing = true;
        name.fontSizeMin = 11f;
        name.fontSizeMax = 16f;
        name.lineSpacing = -12f;
        name.textWrappingMode = TextWrappingModes.Normal;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.text = hidden ? "Unknown Encounter" : DisplayNames.Format(offer.Card.name);
        var nr = name.rectTransform;
        nr.anchorMin = new Vector2(0f, 0f);
        nr.anchorMax = new Vector2(1f, 0f);
        nr.pivot = new Vector2(0.5f, 0f);
        nr.offsetMin = new Vector2(9f, 9f);
        nr.offsetMax = new Vector2(-9f, 46f);

        // The game's token card is still created (hidden) because the play flight animates a
        // clone of it from the card's position to the board.
        Card token = null;
        GameObject template = DeckManager.Instance != null ? DeckManager.Instance.GetTokenCardPrefabTemplate() : null;
        if (template != null)
        {
            var holderGo = new GameObject("TokenHolder", typeof(RectTransform), typeof(CanvasGroup));
            var holder = holderGo.GetComponent<RectTransform>();
            holder.SetParent(visual, false);
            Stretch(holder);
            var holderGroup = holderGo.GetComponent<CanvasGroup>();
            holderGroup.alpha = 0f;
            holderGroup.blocksRaycasts = false;
            GameObject tokenGo = Instantiate(template, holder);
            tokenGo.SetActive(true);
            var rt = tokenGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(TokenWidth, TokenHeight);
            token = tokenGo.GetComponent<Card>();
            if (token != null)
            {
                token.UseCardArtFolderOnly = true;
                token.Initialize(offer.Card);
                token.SuppressHoverEffects = true;
            }
            var cg = tokenGo.GetOrAddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
        }

        // ---- Hit strip: fixed, unrotated, non-overlapping; positioned by LayoutFan.
        var hit = CreateGraphic<Image>(hitLayer, offer.Card.name + "Hit");
        hit.color = new Color(0, 0, 0, 0);
        hit.raycastTarget = true;
        var hitRect = hit.rectTransform;
        hitRect.anchorMin = hitRect.anchorMax = new Vector2(0.5f, 0f);
        hitRect.pivot = new Vector2(0.5f, 0f);

        var handler = visualGo.AddComponent<HandTraySlot>();
        handler.Init(this, offer, token, visual, hitRect, face, name, isHand);
        handler.TokenHolder = token != null ? token.transform.parent as RectTransform : null;
        hit.gameObject.AddComponent<HandTrayHitForwarder>().target = handler;
        handler.SetPlayable(offer.IsPlayable);
        return handler;
    }

    internal static Sprite ResolveArt(CardData data)
    {
        if (illustrations == null) illustrations = FindAnyObjectByType<Illustrations>();
        if (illustrations == null || data == null) return null;
        foreach (string candidate in new[] { data.spriteName, data.portraitName, data.name, data.actionClassName, data.action })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (illustrations.TryGetCardArtByName(candidate, out Sprite sprite) && sprite != null) return sprite;
        }
        foreach (string candidate in new[] { data.spriteName, data.portraitName, data.name })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (illustrations.TryGetIllustrationByName(candidate, out Sprite sprite) && sprite != null) return sprite;
        }
        return null;
    }

    internal static string TypeLabel(CardData data)
    {
        CardTypeEnum type = data.GetCardType();
        return type switch
        {
            CardTypeEnum.PC => "PLACE",
            CardTypeEnum.Land => "LAND",
            CardTypeEnum.Unknown => "CARD",
            _ => type.ToString().ToUpperInvariant(),
        };
    }

    // ------------------------------------------------------------------ Interaction

    internal void SetHovered(HandTraySlot slot, bool enter)
    {
        if (enter)
        {
            if (hovered == slot) return;
            hovered = slot;
            ApplyHoverOffsets();
            OnHover(slot, true);
            Sounds.Instance?.PlayUiHover();
        }
        else if (hovered == slot)
        {
            hovered = null;
            ApplyHoverOffsets();
            OnHover(slot, false);
        }
    }

    internal void OnHover(HandTraySlot slot, bool enter)
    {
        CardCenterPreview preview = CardCenterPreview.Instance;
        if (preview == null) return;
        if (!enter) { preview.HidePreview(); return; }
        // Re-evaluate so the preview's red "why not" lines match the selected character.
        IsPlayable(slot.Offer.Card, Board.Instance != null ? Board.Instance.selectedCharacter : null);
        preview.ShowPreview(slot.Offer.Card, hoverDriven: true);
    }

    internal async void OnClick(HandTraySlot slot, bool rightClick)
    {
        if (playing) return;
        Game game = Game.Instance;
        PlayableLeader player = game != null ? game.player : null;
        if (player == null || !game.IsPlayerCurrentlyPlaying()) return;
        CardCenterPreview.Instance?.HidePreview();

        if (rightClick)
        {
            if (slot.IsHand && !DeckManager.IsCampaignLeader(player)) await DiscardForResource(player, slot.Offer.Card);
            return;
        }
        if (slot.StackFamily != null)
        {
            Expand(slot.StackFamily);
            return;
        }

        Character selected = Board.Instance != null ? Board.Instance.selectedCharacter : null;
        if (selected == null || selected.GetOwner() != player)
        {
            MessageDisplay.ShowMessage("Select one of your characters first", Color.yellow);
            return;
        }
        if (!slot.Playable)
        {
            Sounds.Instance?.PlayUiDenied();
            OnHover(slot, true);
            return;
        }

        RectTransform holder = null;
        playing = true;
        try
        {
            // The token is handed to the flight animation (and destroyed by it); detach it from
            // the tray first so the tray can rebuild underneath.
            holder = slot.TokenHolder;
            if (holder != null) holder.SetParent(root.parent, true);
            Card token = slot.Token;
            slot.HideForPlay();
            Task play = SituationCardsUI.Instance != null
                ? SituationCardsUI.Instance.PlayOffer(slot.Offer, selected, token)
                : Task.CompletedTask;
            await play;
        }
        finally
        {
            playing = false;
            if (holder != null) Destroy(holder.gameObject, 3f);
            if (slot.Offer.Source == SituationCardOfferSource.Situation)
            {
                opportunities.Remove(opportunities.FirstOrDefault(o => o.Card == slot.Offer.Card));
                playedThisTurn.Add(slot.Offer.Card.name);
            }
            dirty = true;
            // Board refreshes the situational offers for the new state (resources, position).
            if (selected != null && !selected.killed) Board.Instance?.CheckSituationCardsForSelectedCharacter(selected);
        }
    }

    private async Task DiscardForResource(PlayableLeader player, CardData card)
    {
        bool confirm = await ConfirmationDialog.AskImmediate($"Discard {DisplayNames.Format(card.name)} for a random resource?", "Discard", "Keep");
        if (!confirm || !DeckManager.Instance.DiscardFromHand(player, card)) return;
        string[] names = { "gold", "timber", "leather", "mounts", "iron", "steel" };
        string resource = names[Random.Range(0, names.Length)];
        switch (resource)
        {
            case "gold": player.AddGold(1); break;
            case "timber": player.AddTimber(1); break;
            case "leather": player.AddLeather(1); break;
            case "mounts": player.AddMounts(1); break;
            case "iron": player.AddIron(1); break;
            case "steel": player.AddSteel(1); break;
        }
        MessageDisplay.ShowMessage($"{DisplayNames.Format(card.name)} traded for 1 {resource}", Color.yellow);
        dirty = true;
    }

    private static T CreateGraphic<T>(RectTransform parent, string name) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var g = go.GetComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        UiTheme.StyleBody(text, size);
        text.alignment = alignment;
        text.raycastTarget = false;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }
}

/// <summary>One card in the hand tray: owns its visual (animated toward a target pose) and its
/// fixed hit strip. The visual never receives raycasts.</summary>
public class HandTraySlot : MonoBehaviour
{
    private static readonly Color HandLine = new(0.86f, 0.70f, 0.40f, 0.85f);
    private static readonly Color HereLine = new(0.55f, 0.80f, 0.55f, 0.85f);
    private static readonly Color HoverLine = new(1f, 0.88f, 0.58f, 1f);

    public SituationCardOffer Offer { get; private set; }
    public Card Token { get; private set; }
    public bool IsHand { get; private set; }
    public bool Playable { get; private set; }
    public RectTransform TokenHolder { get; set; }
    /// <summary>The family this card stands for in the fan (null for a single card): clicking
    /// it opens the family instead of playing.</summary>
    public string StackFamily { get; private set; }

    public void MakeStack(string family, int count)
    {
        StackFamily = family;
        // Two card edges peeking out behind, and a tab naming the family and its size.
        for (int i = 2; i >= 1; i--)
        {
            var back = new GameObject($"StackEdge{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel));
            var br = back.GetComponent<RectTransform>();
            br.SetParent(visual, false);
            br.SetAsFirstSibling();
            br.anchorMin = Vector2.zero;
            br.anchorMax = Vector2.one;
            br.offsetMin = new Vector2(4f * i, 5f * i);
            br.offsetMax = new Vector2(4f * i, 5f * i);
            var edge = back.GetComponent<CinematicPanel>();
            edge.ornament = 0f;
            edge.fillTop = new Color(0.12f, 0.10f, 0.08f, 1f);
            edge.fillBottom = new Color(0.05f, 0.045f, 0.04f, 1f);
            edge.line = new Color(0.86f, 0.70f, 0.40f, 0.6f);
            edge.raycastTarget = false;
        }
        var tab = new GameObject("StackTab", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var tr = tab.GetComponent<RectTransform>();
        tr.SetParent(visual, false);
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.sizeDelta = new Vector2(118f, 20f);
        tr.anchoredPosition = new Vector2(0f, -2f);
        var tabImage = tab.GetComponent<Image>();
        tabImage.color = UiTheme.Gold;
        tabImage.raycastTarget = false;
        var text = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(tr, false);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        UiTheme.StyleTitle(text, 11f, new Color(0.08f, 0.06f, 0.03f, 1f));
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = HandTray.StackLabel(family, count);
    }

    private HandTray tray;
    private RectTransform visual;
    private RectTransform hit;
    private CinematicPanel face;
    private TextMeshProUGUI label;
    private CanvasGroup group;
    private float restX, restAngle, restDrop;
    private bool lifted;
    private float push;
    private float liftAnim; // 0..1
    private float pushAnim;
    private bool snapped;

    public void Init(HandTray owner, SituationCardOffer offer, Card token, RectTransform visualRect, RectTransform hitRect, CinematicPanel facePanel, TextMeshProUGUI nameLabel, bool isHand)
    {
        tray = owner;
        Offer = offer;
        Token = token;
        visual = visualRect;
        hit = hitRect;
        face = facePanel;
        label = nameLabel;
        IsHand = isHand;
        group = GetComponent<CanvasGroup>();
        face.line = isHand ? HandLine : HereLine;
    }

    public void SetPlayable(bool playable)
    {
        Playable = playable;
        if (group != null) group.alpha = playable ? 1f : 0.55f;
        if (label != null) label.color = playable ? UiTheme.Text : UiTheme.Muted;
    }

    public void SetRest(float x, float angle, float drop, int index)
    {
        restX = x;
        restAngle = angle;
        restDrop = drop;
        if (!snapped) { snapped = true; Apply(); }
    }

    public void SetHitStrip(float left, float right, float height)
    {
        hit.sizeDelta = new Vector2(Mathf.Max(8f, right - left), height);
        hit.anchoredPosition = new Vector2((left + right) * 0.5f, 0f);
    }

    public void SetTarget(bool isLifted, float pushX)
    {
        if (isLifted != lifted)
        {
            lifted = isLifted;
            face.line = lifted ? HoverLine : IsHand ? HandLine : HereLine;
            face.SetVerticesDirty();
        }
        push = pushX;
    }

    public void HideForPlay()
    {
        if (group != null) group.alpha = 0f;
        if (hit != null) hit.gameObject.SetActive(false);
    }

    public void DestroyParts()
    {
        if (hit != null) Destroy(hit.gameObject);
        Destroy(gameObject);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime * 14f;
        float newLift = Mathf.MoveTowards(liftAnim, lifted ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        float newPush = Mathf.Lerp(pushAnim, push, Mathf.Clamp01(dt));
        if (Mathf.Approximately(newLift, liftAnim) && Mathf.Abs(newPush - pushAnim) < 0.01f) return;
        liftAnim = newLift;
        pushAnim = newPush;
        Apply();
    }

    private void Apply()
    {
        if (visual == null) return;
        float e = liftAnim * liftAnim * (3f - 2f * liftAnim);
        visual.anchoredPosition = new Vector2(restX + pushAnim, Mathf.Lerp(restDrop, HandTray.HoverLift, e));
        visual.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(restAngle, 0f, e));
        float s = Mathf.Lerp(1f, HandTray.HoverScale, e);
        visual.localScale = new Vector3(s, s, 1f);
    }

    public void Hover(bool enter) => tray.SetHovered(this, enter);
    public void Click(bool right) => tray.OnClick(this, right);
}

public class HandTrayHitForwarder : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public HandTraySlot target;
    public void OnPointerEnter(PointerEventData eventData) { if (target != null) target.Hover(true); }
    public void OnPointerExit(PointerEventData eventData) { if (target != null) target.Hover(false); }
    public void OnPointerClick(PointerEventData eventData) { if (target != null) target.Click(eventData.button == PointerEventData.InputButton.Right); }
}
