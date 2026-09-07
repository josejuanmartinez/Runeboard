using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Blocks input while the startup card catalog and illustration cache become ready.</summary>
public sealed class StartupLoadingScreen : MonoBehaviour
{
    // Pairs one of the UI's card slots with a small authored pool of card names. "provider" is
    // the slot's original, already-positioned card (kept as index 0); "bakedAlternates" are the
    // extra sibling Card GameObjects the editor's "Bake Preload Cards" button builds for
    // cardNames[1..] — fully initialized with their artwork baked in as a normal serialized
    // sprite reference. Rotation only ever toggles which one of these pre-built siblings is
    // active; it never calls Card.Initialize (and therefore never touches the runtime
    // Illustrations/Addressables lookup) at all, so it can't lose the load race that caused the
    // original empty-image bug.
    [System.Serializable]
    private class CardRotationSlot
    {
        public CardDataProvider provider;
        [Tooltip("Card names to bake for this slot (3 recommended, including this slot's own card as #1). Press 'Bake Preload Cards' in the Inspector after editing.")]
        public string[] cardNames = new string[3];
        [Tooltip("Built by 'Bake Preload Cards': one pre-baked sibling Card per extra name above. Do not edit by hand.")]
        public List<CardDataProvider> bakedAlternates = new();
    }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private List<CardRotationSlot> cardSlots = new();
    [SerializeField] private TextMeshProUGUI continuePromptText;
    [SerializeField] private float fadeSeconds = 0.25f;
    [Tooltip("How often (in seconds) each card slot re-rolls to a new random baked card.")]
    [SerializeField] private float rotationSeconds = 3f;
    [Tooltip("Total duration of the flip animation played when a card slot changes card.")]
    [SerializeField] private float cardFlipSeconds = 0.4f;

    private readonly Dictionary<CardRotationSlot, List<CardDataProvider>> slotGroups = new();
    private readonly Dictionary<CardRotationSlot, int> slotActiveIndex = new();
    private readonly Dictionary<CardRotationSlot, Coroutine> activeFlips = new();
    private readonly Dictionary<CardDataProvider, Vector3> cardRestScales = new();
    private Illustrations illustrations;
    private Coroutine cardRotationCoroutine;
    private int testCardIndex = -1;

    /// <summary>Index last shown by <see cref="TestNextCardSet"/>, for the custom Inspector button's label.</summary>
    public int TestCardIndex => testCardIndex;

    private IEnumerator Start()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        SetVisible(true);
        SetContinuePromptVisible(false);
        if (progressBar != null) progressBar.value = 0.02f;
        SetStatus("Preparing your journey");

        // Ensure this canvas is actually presented before synchronous deck parsing begins.
        yield return null;

        cardRotationCoroutine = cardSlots != null && cardSlots.Count > 0 ? StartCoroutine(RotateCardArt()) : null;

        while (!StartupReady())
        {
            float deckProgress = DeckManager.Instance != null && DeckManager.Instance.IsLoaded ? 1f : 0.08f;
            float artProgress = illustrations != null ? illustrations.LoadProgress : 0.02f;
            float combined = Mathf.Clamp01(deckProgress * 0.25f + artProgress * 0.75f);
            if (progressBar != null) progressBar.value = combined;
            SetStatus(deckProgress < 1f
                ? "Preparing the decks"
                : "Loading the painted world");
            yield return null;
        }

        if (progressBar != null) progressBar.value = 1f;
        SetStatus("Your journey awaits");
        SetContinuePromptVisible(true);

        // Keep cycling card art and wait for the player to press a key or click, rather than
        // auto-advancing the instant assets are ready.
        while (!Input.anyKeyDown)
        {
            yield return null;
        }

        if (cardRotationCoroutine != null) StopCoroutine(cardRotationCoroutine);
        SetContinuePromptVisible(false);

        yield return FadeOut();
        SetVisible(false);
        gameObject.SetActive(false);
        Debug.Log("StartupLoadingScreen: startup complete; menu input unblocked.");
    }

    // Builds each slot's group of pre-baked siblings (the original "provider" plus whatever
    // "Bake Preload Cards" generated for it) once, and makes sure exactly the first one is
    // active. Safe to call repeatedly — a slot already known is left alone.
    private void EnsureGroupsInitialized()
    {
        if (cardSlots == null) return;

        foreach (CardRotationSlot slot in cardSlots)
        {
            if (slot?.provider == null || slotGroups.ContainsKey(slot)) continue;

            List<CardDataProvider> group = new() { slot.provider };
            if (slot.bakedAlternates != null) group.AddRange(slot.bakedAlternates.Where(p => p != null));

            for (int i = 0; i < group.Count; i++) group[i].gameObject.SetActive(i == 0);

            slotGroups[slot] = group;
            slotActiveIndex[slot] = 0;
        }
    }

    // Switches one slot to the given index within its pre-baked group via the flip animation.
    // Never calls Card.Initialize — the target is already fully initialized, art included, so
    // this is just a GameObject.SetActive toggle either side of the flip.
    private void AdvanceSlot(CardRotationSlot slot, int targetIndex)
    {
        if (!slotGroups.TryGetValue(slot, out List<CardDataProvider> group) || group.Count == 0) return;

        targetIndex = ((targetIndex % group.Count) + group.Count) % group.Count;
        int currentIndex = slotActiveIndex.TryGetValue(slot, out int idx) ? idx : 0;
        if (targetIndex == currentIndex) return;

        if (activeFlips.TryGetValue(slot, out Coroutine running) && running != null)
        {
            StopCoroutine(running);
        }
        activeFlips[slot] = StartCoroutine(FlipGroupTo(group[currentIndex], group[targetIndex]));
        slotActiveIndex[slot] = targetIndex;
    }

    // Re-rolls each of the loading screen's card slots to a random *other* card within that
    // slot's own pre-baked group (see CardRotationSlot / EnsureGroupsInitialized).
    private IEnumerator RotateCardArt()
    {
        EnsureGroupsInitialized();

        while (true)
        {
            yield return new WaitForSeconds(rotationSeconds);

            foreach (CardRotationSlot slot in cardSlots)
            {
                if (!slotGroups.TryGetValue(slot, out List<CardDataProvider> group) || group.Count <= 1) continue;

                int current = slotActiveIndex[slot];
                int next = Random.Range(0, group.Count - 1);
                if (next >= current) next++; // uniformly pick an index different from current
                AdvanceSlot(slot, next);
            }
        }
    }

    // QA helper wired to a button in the custom Inspector: steps every slot to its own Nth
    // pre-baked card in lockstep (all slots' first, then all slots' second, ...), so each of the
    // baked cards can be eyeballed on demand instead of waiting on the timed random rotation to
    // happen to land on it. Takes over from the automatic rotation once used.
    public void TestNextCardSet()
    {
        if (!Application.isPlaying || cardSlots == null) return;

        EnsureGroupsInitialized();

        if (cardRotationCoroutine != null)
        {
            StopCoroutine(cardRotationCoroutine);
            cardRotationCoroutine = null;
        }

        testCardIndex++;
        foreach (CardRotationSlot slot in cardSlots)
        {
            AdvanceSlot(slot, testCardIndex);
        }
    }

    // Card-flip transition: shrinks the outgoing sibling flat (scale.x -> 0) then deactivates it,
    // activates the incoming sibling flat and unfolds it back out — so the reveal lands exactly
    // on the "edge-on" frame, same as the original single-object version, just spread across two
    // pre-baked GameObjects instead of reinitializing one in place.
    private IEnumerator FlipGroupTo(CardDataProvider outgoing, CardDataProvider incoming)
    {
        if (outgoing == null || incoming == null || outgoing == incoming) yield break;

        Vector3 outgoingRest = GetRestScale(outgoing);
        Vector3 incomingRest = GetRestScale(incoming);
        float halfDuration = Mathf.Max(0.01f, cardFlipSeconds * 0.5f);

        float elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float scaleX = Mathf.Lerp(outgoingRest.x, 0f, elapsed / halfDuration);
            outgoing.transform.localScale = new Vector3(scaleX, outgoingRest.y, outgoingRest.z);
            yield return null;
        }
        outgoing.gameObject.SetActive(false);
        outgoing.transform.localScale = outgoingRest;

        incoming.transform.localScale = new Vector3(0f, incomingRest.y, incomingRest.z);
        incoming.gameObject.SetActive(true);

        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float scaleX = Mathf.Lerp(0f, incomingRest.x, elapsed / halfDuration);
            incoming.transform.localScale = new Vector3(scaleX, incomingRest.y, incomingRest.z);
            yield return null;
        }
        incoming.transform.localScale = incomingRest;
    }

    private Vector3 GetRestScale(CardDataProvider provider)
    {
        if (!cardRestScales.TryGetValue(provider, out Vector3 restScale))
        {
            // Snapshot whatever scale was authored/baked onto it (the "Cards" row scales these
            // down to fit 3 side by side), so flips always end back there.
            restScale = provider.transform.localScale;
            cardRestScales[provider] = restScale;
        }
        return restScale;
    }

    private bool StartupReady()
    {
        DeckManager[] deckManagers = FindObjectsByType<DeckManager>(FindObjectsSortMode.None);
        Illustrations[] illustrationServices = FindObjectsByType<Illustrations>(FindObjectsSortMode.None);
        illustrations = illustrationServices.FirstOrDefault(service => service.IsLoaded)
            ?? illustrationServices.FirstOrDefault();
        return deckManagers.Any(manager => manager.IsLoaded)
            && illustrationServices.Any(service => service.IsLoaded);
    }

    private IEnumerator FadeOut()
    {
        if (canvasGroup == null || fadeSeconds <= 0f) yield break;
        float elapsed = 0f;
        while (elapsed < fadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeSeconds);
            yield return null;
        }
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    private void SetStatus(string value)
    {
        if (statusText != null) statusText.text = value;
    }

    private void SetContinuePromptVisible(bool visible)
    {
        if (continuePromptText != null) continuePromptText.gameObject.SetActive(visible);
    }
}
