using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// When a character is selected, every reachable hex where it could play a card on arrival (an
// opportunity card offered there, or a hand card that only works there) gets a small, still chip
// above it that says what can be done there and which side of the realm it works on: "<sword> Siege Population", "<gold> Steal Gold +2". Hovering a chip lists the cards
// there and draws the route to it; nothing pulses or cycles. The icons are the score aspects of
// the campaign HUD (ScoreAspects), so a hint reads as "this raises your Military".
public static class OpportunityHexHinter
{
    public static bool HintsEnabled { get; private set; } = true;

    // Call with false to suppress opportunity hints (e.g. while some other sequence owns the
    // board's attention) and with true to show them again at once for whatever character is
    // currently selected, without waiting on the next selection-change event.
    public static void SetHintsEnabled(bool enabled)
    {
        HintsEnabled = enabled;
        if (enabled)
        {
            Refresh(Board.Instance?.selectedCharacter);
        }
        else
        {
            ClearAll();
        }
    }

    public static void Refresh(Character character)
    {
        if (!HintsEnabled || !CanHint(character)) { ClearAll(); return; }
        OpportunityHintChips.Ensure().Follow(character);
    }

    public static void ClearAll() => OpportunityHintChips.Instance?.Follow(null);

    internal static bool CanHint(Character character)
    {
        if (character == null || character.killed || character.hex == null) return false;
        Game game = Game.Instance;
        if (game == null || game.player == null || character.GetOwner() != game.player) return false;
        // Not while an AI is making the moves for this character (Autoplay): hints while
        // nobody's hand is on the wheel are only noise.
        if (!game.IsPlayerCurrentlyPlaying() || game.IsPlayerAutoplayEnabledFor(character.GetOwner())) return false;
        return character.GetOwner() is PlayableLeader;
    }

    internal readonly struct Opportunity
    {
        public readonly Hex Hex;
        public readonly List<(CardData card, bool fromHand)> Cards;
        public Opportunity(Hex hex, List<(CardData card, bool fromHand)> cards) { Hex = hex; Cards = cards; }
    }

    // Reachable hexes where the character could act on arrival, nearest first. Only cards it
    // could really play there count (DeckManager.GetPlayableCardsAtHex), so following a hint
    // never wastes the move.
    internal static List<Opportunity> Find(Character character, int max)
    {
        var result = new List<Opportunity>();
        if (character?.GetOwner() is not PlayableLeader leader) return result;
        DeckManager deckManager = DeckManager.Instance;
        HexPathRenderer pathRenderer = Object.FindAnyObjectByType<HexPathRenderer>();
        if (deckManager == null || pathRenderer == null) return result;

        // Each card is hinted once, at the nearest place it can be played: a card that works on
        // every hex around would otherwise label them all.
        var hinted = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (Hex hex in pathRenderer.FindAllHexesInRemainingRange(character)
            .Where(h => h != null && h.v2 != character.hex.v2)
            .OrderBy(h => Vector3.SqrMagnitude(h.transform.position - character.hex.transform.position)))
        {
            List<(CardData card, bool fromHand)> cards = deckManager.GetPlayableCardsAtHex(leader, character, hex)
                .Where(c => hinted.Add(c.card.name ?? string.Empty))
                .ToList();
            if (cards.Count == 0) continue;
            result.Add(new Opportunity(hex, cards));
            if (result.Count >= max) break;
        }
        return result;
    }
}

/// <summary>The screen-space chips of OpportunityHexHinter, on their own overlay canvas just
/// above the city banners and below the HUD; pinned above their hexes like the banners.</summary>
public class OpportunityHintChips : MonoBehaviour
{
    private const float ReferenceHeight = 1080f;
    private const int MaxChips = 10;
    private const float RecheckInterval = 0.4f;
    private static readonly Vector3 AnchorOffset = new(0f, 0.2f, 0f); // above the tile's centre, clear of the banner

    public static OpportunityHintChips Instance { get; private set; }

    private Canvas canvas;
    private RectTransform root;
    private Character character;
    private string stateKey; // what the hints were built for: place, moves left, hand, purse, whether it can act
    private float nextCheck;
    private readonly List<Chip> chips = new();
    private Chip hovered;

    private sealed class Chip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public OpportunityHintChips owner;
        public Hex hex;
        public RectTransform rect;
        public TextMeshProUGUI label;
        public string shortText, longText;

        public void OnPointerEnter(PointerEventData eventData) => owner.Hover(this, true);
        public void OnPointerExit(PointerEventData eventData) => owner.Hover(this, false);
    }

    public static OpportunityHintChips Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("OpportunityHintChips", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Instance = go.AddComponent<OpportunityHintChips>();
        Instance.canvas = go.GetComponent<Canvas>();
        Instance.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Instance.canvas.sortingOrder = -45; // over the city banners (-50), under every HUD canvas
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
        scaler.matchWidthOrHeight = 1f;
        Instance.root = (RectTransform)go.transform;
        return Instance;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Follow(Character target)
    {
        character = target;
        stateKey = null;
        Rebuild();
    }

    private void Update()
    {
        if (character == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + RecheckInterval;
        if (!OpportunityHexHinter.HintsEnabled || !OpportunityHexHinter.CanHint(character)) { character = null; Rebuild(); return; }
        // A move changes where the character stands and how far it can go; a played card changes
        // the hand, the purse and whether it can still act.
        if (StateKey() != stateKey) Rebuild();
    }

    private void Rebuild()
    {
        if (hovered != null) Hover(hovered, false);
        foreach (Chip chip in chips) if (chip != null) Destroy(chip.gameObject);
        chips.Clear();
        if (character == null) return;
        stateKey = StateKey();

        foreach (OpportunityHexHinter.Opportunity o in OpportunityHexHinter.Find(character, MaxChips))
            chips.Add(CreateChip(o));
        LateUpdate();
    }

    private string StateKey()
    {
        Leader owner = character.GetOwner();
        int hand = owner is PlayableLeader leader && DeckManager.Instance != null ? DeckManager.Instance.GetHand(leader).Count : 0;
        return $"{character.hex?.v2}|{character.moved}|{hand}|{owner?.goldAmount}|{character.CanPlayerActNow()}";
    }

    private Chip CreateChip(OpportunityHexHinter.Opportunity o)
    {
        CardData best = o.Cards[0].card;
        ScoreAspect aspect = ScoreAspects.ForCard(best);
        string more = o.Cards.Count > 1 ? $" <color={UiTheme.MutedHex}>+{o.Cards.Count - 1}</color>" : string.Empty;

        var go = new GameObject($"Hint {o.Hex.v2}", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var rect = (RectTransform)go.transform;
        rect.SetParent(root, false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0f);
        var panel = go.GetComponent<CinematicPanel>();
        panel.ornament = 0f;
        panel.inset = 1.5f;
        panel.topOrnament = false;
        panel.fillTop = new Color(0.05f, 0.045f, 0.04f, 0.9f);
        panel.fillBottom = new Color(0.03f, 0.027f, 0.024f, 0.92f);
        panel.line = new Color(0.55f, 0.42f, 0.24f, 0.8f);
        panel.raycastTarget = true;
        var row = go.GetComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(8, 9, 3, 4);
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(rect, false);
        var label = textGo.GetComponent<TextMeshProUGUI>();
        UiTheme.StyleBody(label, 14f, UiTheme.Text);
        label.richText = true;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;

        var chip = go.AddComponent<Chip>();
        chip.owner = this;
        chip.hex = o.Hex;
        chip.rect = rect;
        chip.label = label;
        chip.shortText = $"{ScoreAspects.Icon(aspect)} {best.name}{more}";
        chip.longText = Describe(o.Cards);
        label.text = chip.shortText;
        return chip;
    }

    // The hover text: every card that could be played there, with its aspect and where it comes from.
    private static string Describe(List<(CardData card, bool fromHand)> cards)
    {
        var lines = new List<string>();
        foreach ((CardData card, bool fromHand) in cards.Take(4))
        {
            ScoreAspect aspect = ScoreAspects.ForCard(card);
            string source = fromHand ? "from your hand" : "offered there";
            lines.Add($"{ScoreAspects.Icon(aspect)} {card.name}  <color={UiTheme.MutedHex}><size=85%>{ScoreAspects.Name(aspect)} · {source}</size></color>");
        }
        if (cards.Count > 4) lines.Add($"<color={UiTheme.MutedHex}><size=85%>and {cards.Count - 4} more</size></color>");
        return string.Join("\n", lines);
    }

    private void Hover(Chip chip, bool on)
    {
        if (chip == null) return;
        HexPathRenderer pathRenderer = FindAnyObjectByType<HexPathRenderer>();
        if (on)
        {
            hovered = chip;
            chip.label.text = chip.longText;
            chip.rect.SetAsLastSibling();
            if (character != null && chip.hex != null) pathRenderer?.ShowHintRoute(character, chip.hex.v2);
        }
        else
        {
            if (hovered == chip) hovered = null;
            chip.label.text = chip.shortText;
            pathRenderer?.StopOpportunityHintCycle();
        }
    }

    private void LateUpdate()
    {
        bool live = Game.Instance != null && Game.Instance.started && chips.Count > 0;
        if (canvas.enabled != live) canvas.enabled = live;
        if (!live) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        float toCanvas = root.rect.height / Mathf.Max(1f, Screen.height);
        float scale = Mathf.Clamp(Mathf.Pow(ZoomScaledLabel.ReferenceZoom / cam.orthographicSize, 0.55f), 0.6f, 1.05f);
        foreach (Chip chip in chips)
        {
            if (chip == null || chip.hex == null) continue;
            Vector3 screen = cam.WorldToScreenPoint(chip.hex.transform.position + AnchorOffset);
            bool onScreen = screen.z > 0f && screen.x > -100f && screen.x < Screen.width + 100f && screen.y > -50f && screen.y < Screen.height + 50f;
            float s = onScreen ? (chip == hovered ? Mathf.Max(scale, 1f) : scale) : 0f;
            chip.rect.localScale = new Vector3(s, s, 1f);
            chip.rect.anchoredPosition = new Vector2(Mathf.Round(screen.x * toCanvas), Mathf.Round(screen.y * toCanvas));
        }
    }
}
