using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The human player's always-visible card tray, replacing the hidden per-character bloom wheel.
/// Left group: the leader's hand, drawn from their deck each turn (DeckManager.DrawHandForTurn).
/// Right group: situational opportunities for the selected character where they stand.
/// Every play costs one of the leader's command points, and any character may spend them.
/// Hover = full card preview, left click = play with the selected character,
/// right click (hand only) = discard for a random resource.
/// </summary>
public class HandTray : MonoBehaviour
{
    public static HandTray Instance { get; private set; }

    private const int MaxOpportunities = 3;
    private const float TokenWidth = 120f;
    private const float TokenHeight = 170f;

    private RectTransform canvasRoot;
    private RectTransform root;
    // The HUD canvas runs at roughly 2 units per 1080p pixel; panels are authored in 1080p
    // pixels and the root is scaled by this so text and cards read at a sensible size.
    private float K => canvasRoot != null ? Mathf.Max(0.5f, canvasRoot.rect.height / 1080f) : 1f;
    private RectTransform cardsRow;
    private TextMeshProUGUI header;
    private readonly List<GameObject> slots = new();
    private readonly List<SituationCardOffer> opportunities = new();
    private readonly HashSet<string> playedThisTurn = new();
    private int playedTurn = -1;
    private Character opportunitiesFor;
    private Hex opportunitiesHex;
    private Character lastSelected;
    private int lastCommandPoints = -1;
    private int lastTurn = -1;
    private int handSignature;
    private bool dirty = true;
    private bool playing;
    private float nextPlayabilityRefresh;

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
        if (index >= slots.Count) return false;
        HandTraySlot slot = slots[index] != null ? slots[index].GetComponent<HandTraySlot>() : null;
        if (slot == null) return false;
        OnHover(slot, true);
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
        int cp = player.CommandPoints;
        if (cp != lastCommandPoints || game.turn != lastTurn) { lastCommandPoints = cp; lastTurn = game.turn; dirty = true; }
        int signature = ComputeHandSignature(deck.GetHand(player));
        if (signature != handSignature) { handSignature = signature; dirty = true; }

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
        Layout layout = FindFirstObjectByType<Layout>();
        Canvas canvas = layout != null ? layout.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return false;
        canvasRoot = canvas.rootCanvas.transform as RectTransform;

        var go = new GameObject("HandTrayPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(RuneboardPanel), typeof(VerticalLayoutGroup));
        root = go.GetComponent<RectTransform>();
        root.SetParent(canvasRoot, false);
        root.SetSiblingIndex(Mathf.Min(1, canvasRoot.childCount - 1));
        root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        var panel = go.GetComponent<RuneboardPanel>();
        panel.topColor = new Color(0.08f, 0.085f, 0.08f, 0.92f);
        panel.bottomColor = new Color(0.02f, 0.025f, 0.025f, 0.95f);
        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 6, 8);
        vlg.spacing = 4f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        header = CreateText(root, "Header", 14f, TextAlignmentOptions.Left);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        var rowGo = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        cardsRow = rowGo.GetComponent<RectTransform>();
        cardsRow.SetParent(root, false);
        var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.LowerCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        return true;
    }

    // Fit between the selected-character panel (left) and the minimap column (right).
    private void PlaceRoot(float slotWidth, float slotHeight)
    {
        Rect r = canvasRoot.rect;
        float left = r.width * 0.33f, right = r.width * 0.855f;
        float dockRight = HudDirector.DockRightColumnLeft(canvasRoot);
        if (dockRight > 0f) right = dockRight - 12f;
        RectTransform leftPanel = FindFirstObjectByType<SelectedCharacterIcon>()?.transform as RectTransform;
        if (leftPanel != null && leftPanel.gameObject.activeInHierarchy) left = Mathf.Max(left * 0.8f, CanvasX(leftPanel, right: true) + 12f);
        float width = Mathf.Max(200f, right - left);
        root.localScale = new Vector3(K, K, 1f);
        root.anchoredPosition = new Vector2(left + width * 0.5f, 12f);
        root.sizeDelta = new Vector2(width / K, slotHeight + 40f);
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
        foreach (GameObject s in slots) if (s != null) Destroy(s);
        slots.Clear();

        DeckManager deck = DeckManager.Instance;
        IReadOnlyList<CardData> hand = deck.GetHand(player);
        int count = hand.Count + opportunities.Count;

        Rect r = canvasRoot.rect;
        float available = (r.width * 0.52f) / K - 40f - (opportunities.Count > 0 ? 30f : 0f);
        float slotWidth = Mathf.Clamp(available / Mathf.Max(6, count) - 6f, 58f, 92f);
        float slotHeight = slotWidth * TokenHeight / TokenWidth;
        PlaceRoot(slotWidth, slotHeight + 26f);

        string selectedName = selected != null && selected.GetOwner() == player ? selected.characterName : null;
        int cp = player.CommandPoints;
        header.text = $"{UiTheme.Eyebrow("Command")} <color={UiTheme.GoldHex}>{cp}</color>/{player.CommandPointsMax}"
            + (selectedName != null ? $"    <color={UiTheme.MutedHex}>Playing as</color> {selectedName}" : $"    <color={UiTheme.MutedHex}>Select a character to play cards</color>")
            + $"    <size=82%><color={UiTheme.MutedHex}>Right-click a hand card to trade it for a resource</color></size>";

        foreach (CardData card in hand)
            slots.Add(CreateSlot(new SituationCardOffer(card, SituationCardOfferSource.Hand, IsPlayable(card, selected)), slotWidth, slotHeight, isHand: true));

        if (opportunities.Count > 0)
        {
            slots.Add(CreateDivider(slotHeight));
            foreach (SituationCardOffer offer in opportunities)
                slots.Add(CreateSlot(new SituationCardOffer(offer.Card, offer.Source, IsPlayable(offer.Card, selected)), slotWidth, slotHeight, isHand: false));
        }
        nextPlayabilityRefresh = Time.unscaledTime + 0.5f;
    }

    private static bool IsPlayable(CardData card, Character selected)
    {
        if (card == null || selected == null || !selected.isPlayerControlled) return false;
        return SituationCardsUI.IsOfferPlayable(card, selected);
    }

    // Playability changes as resources, positions and command points change mid-turn.
    private void RefreshTints(Character selected)
    {
        nextPlayabilityRefresh = Time.unscaledTime + 0.5f;
        foreach (GameObject slot in slots)
        {
            HandTraySlot handler = slot != null ? slot.GetComponent<HandTraySlot>() : null;
            if (handler == null) continue;
            bool playable = IsPlayable(handler.Offer.Card, selected);
            if (playable != handler.Playable) handler.SetPlayable(playable);
        }
    }

    private GameObject CreateDivider(float height)
    {
        var go = new GameObject("Divider", typeof(RectTransform), typeof(LayoutElement));
        go.GetComponent<RectTransform>().SetParent(cardsRow, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 24f;
        le.preferredHeight = height;
        TextMeshProUGUI label = CreateText(go.GetComponent<RectTransform>(), "Here", 12f, TextAlignmentOptions.Center);
        label.text = UiTheme.Eyebrow("Here");
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.rectTransform.localEulerAngles = new Vector3(0, 0, 90f);
        return go;
    }

    private GameObject CreateSlot(SituationCardOffer offer, float width, float height, bool isHand)
    {
        var slotGo = new GameObject(offer.Card.name, typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        var slotRect = slotGo.GetComponent<RectTransform>();
        slotRect.SetParent(cardsRow, false);
        var le = slotGo.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height + 26f;
        var vlg = slotGo.GetComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 2f;

        // Card art frame: hosts the scaled TokenCard instance.
        var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(LayoutElement));
        var frame = frameGo.GetComponent<RectTransform>();
        frame.SetParent(slotRect, false);
        frameGo.GetComponent<LayoutElement>().preferredHeight = height;

        Card token = null;
        GameObject template = DeckManager.Instance != null ? DeckManager.Instance.GetTokenCardPrefabTemplate() : null;
        if (template != null)
        {
            GameObject tokenGo = Instantiate(template, frame);
            tokenGo.SetActive(true);
            var rt = tokenGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(TokenWidth, TokenHeight);
            float scale = Mathf.Min(width / TokenWidth, height / TokenHeight);
            rt.localScale = new Vector3(scale, scale, 1f);
            token = tokenGo.GetComponent<Card>();
            if (token != null)
            {
                token.UseCardArtFolderOnly = true;
                token.Initialize(offer.Card);
                token.SuppressHoverEffects = true;
            }
            var cg = tokenGo.GetComponent<CanvasGroup>() ?? tokenGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
        }

        TextMeshProUGUI name = CreateText(slotRect, "Name", 11f, TextAlignmentOptions.Top);
        name.enableAutoSizing = true;
        name.fontSizeMin = 8f;
        name.fontSizeMax = 11f;
        name.text = DisplayNames.Format(offer.Card.name);
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        // Transparent hit area over the whole slot drives hover/click/right-click.
        var hitGo = new GameObject("Hit", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        var hit = hitGo.GetComponent<RectTransform>();
        hit.SetParent(slotRect, false);
        hitGo.GetComponent<LayoutElement>().ignoreLayout = true;
        hit.anchorMin = Vector2.zero;
        hit.anchorMax = Vector2.one;
        hit.offsetMin = hit.offsetMax = Vector2.zero;
        hitGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);

        var handler = slotGo.AddComponent<HandTraySlot>();
        handler.Init(this, offer, token, name, isHand);
        hitGo.AddComponent<HandTrayHitForwarder>().target = handler;
        handler.SetPlayable(offer.IsPlayable);
        return slotGo;
    }

    // ------------------------------------------------------------------ Interaction

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
            if (slot.IsHand) await DiscardForResource(player, slot.Offer.Card);
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

        playing = true;
        try
        {
            // The token is handed to the flight animation (and destroyed by it); detach it from
            // the layout first so the tray can rebuild underneath.
            Card token = slot.Token;
            if (token != null) token.transform.SetParent(root.parent, true);
            Task play = SituationCardsUI.Instance != null
                ? SituationCardsUI.Instance.PlayOffer(slot.Offer, selected, token)
                : Task.CompletedTask;
            await play;
        }
        finally
        {
            playing = false;
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

public class HandTraySlot : MonoBehaviour
{
    public SituationCardOffer Offer { get; private set; }
    public Card Token { get; private set; }
    public bool IsHand { get; private set; }
    public bool Playable { get; private set; }
    private HandTray tray;
    private TextMeshProUGUI label;

    public void Init(HandTray owner, SituationCardOffer offer, Card token, TextMeshProUGUI nameLabel, bool isHand)
    {
        tray = owner;
        Offer = offer;
        Token = token;
        label = nameLabel;
        IsHand = isHand;
    }

    public void SetPlayable(bool playable)
    {
        Playable = playable;
        if (Token != null) Token.SetTokenTint(playable ? 0f : 0.18f, 0f);
        if (label != null) label.color = playable ? UiTheme.Text : UiTheme.Muted;
    }

    public void Hover(bool enter)
    {
        transform.localScale = enter ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;
        tray.OnHover(this, enter);
    }

    public void Click(bool right) => tray.OnClick(this, right);
}

public class HandTrayHitForwarder : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public HandTraySlot target;
    public void OnPointerEnter(PointerEventData eventData) => target?.Hover(true);
    public void OnPointerExit(PointerEventData eventData) => target?.Hover(false);
    public void OnPointerClick(PointerEventData eventData) => target?.Click(eventData.button == PointerEventData.InputButton.Right);
}
