using System;
using UnityEngine;

/// <summary>
/// Resolves a card by name and applies its CardData to the Card on this GameObject. The custom
/// inspector exposes Apply for prefab/edit-time previews; Initialize is also available at runtime.
/// </summary>
[RequireComponent(typeof(Card))]
[DisallowMultipleComponent]
public sealed class CardDataProvider : MonoBehaviour
{
    [Header("Card Data")]
    public string cardName;
    public bool startAsToken = true;
    [Tooltip("Optional deck whose badge should be shown at the card's top right. Leave empty to use the card's own deck.")]
    public string deckId;

    [Header("Runtime")]
    [Tooltip("Initialize the Card from the serialized fields when this component starts in Play Mode.")]
    public bool initializeOnStart;

    [Header("Presentation")]
    public bool suppressHoverEffects;
    public bool useCardArtFolderOnly;
    public bool showRequirementWarnings = true;
    public bool showCloseIcon = true;

    private Card card;

    public Card Card
    {
        get
        {
            if (card == null) card = GetComponent<Card>();
            return card;
        }
    }

    private void Start()
    {
        if (initializeOnStart) Apply();
    }

    /// <summary>Applies the card name and token setting currently stored on the component.</summary>
    public bool Apply()
    {
        return Initialize(cardName, startAsToken);
    }

    /// <summary>
    /// Looks up card data by display name and passes it to Card.Initialize.
    /// </summary>
    public bool Initialize(string requestedCardName, bool showAsToken)
    {
        if (string.IsNullOrWhiteSpace(requestedCardName))
        {
            Debug.LogWarning($"CardDataProvider on '{name}' needs a card name.", this);
            return false;
        }

        Card target = Card;
        if (target == null)
        {
            Debug.LogError($"CardDataProvider on '{name}' could not find its required Card component.", this);
            return false;
        }

        CardData sourceData = ResolveCardData(requestedCardName.Trim());
        if (sourceData == null)
        {
            Debug.LogWarning($"CardDataProvider on '{name}' could not find card data named '{requestedCardName}'.", this);
            return false;
        }

        CardData data = sourceData.Clone();
        if (!string.IsNullOrWhiteSpace(deckId))
        {
            if (TryResolveDeck(deckId.Trim(), out DeckManifestEntry deck))
            {
                data.deckId = deck.deckId;
                data.deckSpriteName = deck.deckSpriteName;
            }
            else
            {
                Debug.LogWarning($"CardDataProvider on '{name}' could not find deck '{deckId}'.", this);
            }
        }

        cardName = data.name;
        startAsToken = showAsToken;
        target.SuppressHoverEffects = suppressHoverEffects;
        target.UseCardArtFolderOnly = useCardArtFolderOnly;
        target.ShowRequirementWarnings = showRequirementWarnings;
        target.ShowCloseIcon = showCloseIcon;
        target.Initialize(data, showAsToken);
        return true;
    }

    private static CardData ResolveCardData(string requestedCardName)
    {
        // In Play Mode, prefer the live catalog so the Card receives the same resolved data used
        // by the rest of the game. Edit-time prefab previews generally have no live DeckManager.
        if (Application.isPlaying)
        {
            DeckManager manager = DeckManager.Instance != null
                ? DeckManager.Instance
                : FindAnyObjectByType<DeckManager>();
            CardData runtimeCard = manager?.FindAnyCardByName(requestedCardName);
            if (runtimeCard != null) return runtimeCard;
        }

        TextAsset manifestAsset = Resources.Load<TextAsset>("Cards");
        if (manifestAsset == null) return null;

        CardsManifest manifest = JsonUtility.FromJson<CardsManifest>(manifestAsset.text);
        if (manifest?.decks == null) return null;

        foreach (DeckManifestEntry entry in manifest.decks)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.resourcePath)) continue;

            TextAsset deckAsset = Resources.Load<TextAsset>(entry.resourcePath);
            if (deckAsset == null) continue;

            DeckData deck = JsonUtility.FromJson<DeckData>(deckAsset.text);
            if (deck?.cards == null) continue;

            foreach (CardData candidate in deck.cards)
            {
                if (candidate != null &&
                    string.Equals(candidate.name, requestedCardName, StringComparison.OrdinalIgnoreCase))
                {
                    candidate.deckId = deck.deckId;
                    candidate.alignment = deck.alignment;
                    candidate.deckSpriteName = entry.deckSpriteName;
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool TryResolveDeck(string requestedDeckId, out DeckManifestEntry resolvedDeck)
    {
        resolvedDeck = null;
        if (string.IsNullOrWhiteSpace(requestedDeckId)) return false;

        TextAsset manifestAsset = Resources.Load<TextAsset>("Cards");
        if (manifestAsset == null) return false;

        CardsManifest manifest = JsonUtility.FromJson<CardsManifest>(manifestAsset.text);
        if (manifest?.decks == null) return false;

        foreach (DeckManifestEntry candidate in manifest.decks)
        {
            if (candidate != null &&
                string.Equals(candidate.deckId, requestedDeckId, StringComparison.OrdinalIgnoreCase))
            {
                resolvedDeck = candidate;
                return true;
            }
        }

        return false;
    }
}
