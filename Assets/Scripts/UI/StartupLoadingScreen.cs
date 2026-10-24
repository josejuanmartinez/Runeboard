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
    [Tooltip("How much of its share of the Cards row each card fills. 1 = as large as the row allows.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float cardFillFraction = 1f;

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

        // A Continue / Load / Return-to-title reload brings the scene (and this screen) back,
        // but SessionLoadingScreen is already covering it and tracks the same readiness. Showing
        // the boot screen again would ask for "press any key" in the middle of a load.
        if (SessionLoadingScreen.IsActive)
        {
            SetVisible(false);
            gameObject.SetActive(false);
            yield break;
        }

        SetVisible(true);
        SetContinuePromptVisible(false);
        if (progressBar != null) progressBar.value = 0.02f;
        SetStatus("Preparing your journey");

        // Must happen before the first yield: the frame rendered at that yield is the one left
        // on screen for as long as the synchronous deck parse blocks the main thread, so
        // building the in-game card face any later means the player watches the legacy prefab
        // frame for seconds and then sees it pop into the real design mid-load.
        EnsureGroupsInitialized();

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
        while (!Input.anyKeyDown && !AutoTour.Active)
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

        Vector2 footprint = ResolveCardFootprint();

        foreach (CardRotationSlot slot in cardSlots)
        {
            if (slot?.provider == null || slotGroups.ContainsKey(slot)) continue;

            List<CardDataProvider> group = new() { slot.provider };
            if (slot.bakedAlternates != null) group.AddRange(slot.bakedAlternates.Where(p => p != null));

            for (int i = 0; i < group.Count; i++)
            {
                ApplyInGameCardPresentation(group[i], footprint);
                group[i].gameObject.SetActive(i == 0);
            }

            slotGroups[slot] = group;
            slotActiveIndex[slot] = 0;
        }
    }

    // "Bake Preload Cards" only bakes the card's data and artwork; the face it leaves behind is
    // the one authored in Card.prefab, which is the legacy card frame. The card the player sees in
    // game is built at runtime by Card.ApplyCenterPreviewPresentation (that is what
    // CardCenterPreview instantiates), so rebuild the same face here, scaled into the footprint
    // ResolveCardFootprint measured off the Cards row, and the loading screen stops showing an
    // older card design than the game. Cheap enough to do once per baked sibling at startup, and
    // it keeps the two in step automatically whenever the in-game card design changes.
    private void ApplyInGameCardPresentation(CardDataProvider provider, Vector2 footprint)
    {
        Card card = provider != null ? provider.Card : null;
        if (card == null) return;

        // TMP only creates the per-label fontMaterial the presentation restyles once the object
        // has been enabled, and every sibling past the first is baked inactive.
        bool wasActive = card.gameObject.activeSelf;
        card.gameObject.SetActive(true);
        card.ApplyCenterPreviewPresentationScaledTo(footprint);
        card.gameObject.SetActive(wasActive);
    }

    // How much room one slot's card gets. Deriving this from the slot's own authored rect is
    // what made the cards read as tiny: those rects are the legacy 200x250 card frame, and
    // fitting the much taller 340x570 in-game face inside one is height-limited, so the card
    // ended up well short of the row it lives in. Measure the row instead — every slot's card
    // shares one HorizontalLayoutGroup — so cards fill the band's height and simply share its
    // width, and adding or removing a slot re-fits them automatically.
    private Vector2 ResolveCardFootprint()
    {
        int slotCount = cardSlots?.Count(slot => slot?.provider != null) ?? 0;
        if (slotCount == 0) return Card.CenterPreviewSize;

        RectTransform row = cardSlots.First(slot => slot?.provider != null).provider.transform.parent as RectTransform;
        if (row == null) return Card.CenterPreviewSize;

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        float spacing = layout != null ? layout.spacing : 0f;
        float horizontalPadding = layout != null ? layout.padding.horizontal : 0f;
        float verticalPadding = layout != null ? layout.padding.vertical : 0f;

        float width = (row.rect.width - horizontalPadding - spacing * (slotCount - 1)) / slotCount;
        float height = row.rect.height - verticalPadding;
        return new Vector2(
            Mathf.Max(1f, width) * Mathf.Clamp01(cardFillFraction),
            Mathf.Max(1f, height) * Mathf.Clamp01(cardFillFraction));
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
        DeckManager[] deckManagers = FindObjectsByType<DeckManager>();
        Illustrations[] illustrationServices = FindObjectsByType<Illustrations>();
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

/// <summary>
/// Full-screen "please wait" veil for the heavy transitions the player triggers from a menu:
/// Continue / Load (scene reload + board rebuild + save restore), choosing a campaign (scenario
/// parse + board generation), starting as a leader (Game.StartGame) and returning to the title.
/// It is shown the instant the choice is made and rendered for a couple of frames before the
/// heavy work runs, so the player never stares at a frozen menu. It survives the scene reload
/// (DontDestroyOnLoad), tracks the real readiness of the new scene, and fades out once the board
/// is playable. A hard timeout and an exception watchdog make sure it can never stay stuck.
/// Built at runtime in the cinematic HUD language (dark ink panel, gold hairline, IM Fell title).
/// </summary>
public sealed class SessionLoadingScreen : MonoBehaviour
{
    public enum Kind { RestoreCampaign, NewCampaign, StartCampaign, ReturnToTitle }

    private const int SortingOrder = 32760; // above StartupLoadingScreen (32686)
    private const float FadeOutSeconds = 0.35f;
    private const float PollInterval = 0.2f;
    private const float RailWidth = 520f;
    private const float ExceptionGraceSeconds = 10f;
    private const float StallSeconds = 90f;

    private static SessionLoadingScreen instance;

    /// <summary>True from the click until the fade-out starts.</summary>
    public static bool IsActive => instance != null && instance.active;

    private Kind kind;
    private bool active;
    private bool sceneReloading;   // a scene load is part of this session
    private bool sceneReady;       // ...and the new scene has finished loading
    private float shownAt;
    private float lastStageChangeAt;
    private float lastProgressAt;
    private float lastExceptionAt = -1f;
    private float nextPollAt;
    private float targetProgress;
    private float shownProgress;
    private bool indeterminate;
    private string stage;
    private AsyncOperation sceneOp;

    // Cached lookups (FindAnyObjectByType scans the whole scene, which holds thousands of hexes).
    private Illustrations illustrations;
    private LeaderSelector leaderSelector;
    private UnityEngine.Video.VideoPlayer introVideo;
    private bool lookedUpIntro;
    private float nextSelectorLookupAt;

    private CanvasGroup group;
    private TextMeshProUGUI title;
    private TextMeshProUGUI status;
    private RectTransform fill;
    private RectTransform glint;
    private Image[] pips;
    private Coroutine fadeRoutine;

    // ------------------------------------------------------------------ API

    /// <summary>Shows the veil immediately (it renders at the end of this frame).</summary>
    public static void Show(Kind kind)
    {
        SessionLoadingScreen s = Ensure();
        if (s.fadeRoutine != null) { s.StopCoroutine(s.fadeRoutine); s.fadeRoutine = null; }
        s.kind = kind;
        s.active = true;
        s.sceneReloading = false;
        s.sceneReady = false;
        s.sceneOp = null;
        s.shownAt = Time.realtimeSinceStartup;
        s.lastExceptionAt = -1f;
        s.nextPollAt = 0f;
        s.targetProgress = s.shownProgress = 0.02f;
        s.indeterminate = kind == Kind.StartCampaign;
        s.illustrations = null;
        s.leaderSelector = null;
        s.introVideo = null;
        s.lookedUpIntro = false;
        s.nextSelectorLookupAt = 0f;
        s.gameObject.SetActive(true);
        s.title.text = TitleFor(kind);
        s.SetStage(FirstStageFor(kind));
        s.group.alpha = 1f;
        s.group.blocksRaycasts = true;
        s.group.interactable = true;
        s.ApplyProgressVisuals();
        Canvas.ForceUpdateCanvases();
        Debug.Log($"[Loading] {kind}: loading screen shown.");
    }

    /// <summary>Shows the veil, lets it render, then runs <paramref name="work"/>. An exception in
    /// the work is logged and takes the veil down rather than leaving it up forever.</summary>
    public static void ShowThen(Kind kind, System.Action work)
    {
        Show(kind);
        instance.StartCoroutine(instance.RunAfterRender(work));
    }

    /// <summary>Shows the veil, then reloads <paramref name="buildIndex"/> asynchronously.
    /// <paramref name="applyOnLoaded"/> runs once the new scene's objects exist (after their
    /// Awake/OnEnable, before any Start), so static state such as GameConfig only changes once
    /// the old scene can no longer react to it.</summary>
    public static void ReloadScene(Kind kind, int buildIndex, System.Action applyOnLoaded)
    {
        Show(kind);
        instance.sceneReloading = true;
        instance.StartCoroutine(instance.LoadSceneAfterRender(buildIndex, applyOnLoaded));
    }

    /// <summary>Updates the status line from game code (e.g. a startup hook).</summary>
    public static void SetStatus(string text)
    {
        if (IsActive) instance.SetStage(text);
    }

    /// <summary>Takes the veil down if it is currently showing for <paramref name="forKind"/>.</summary>
    public static void Complete(Kind forKind)
    {
        if (IsActive && instance.kind == forKind) instance.Finish(null);
    }

    /// <summary>Takes the veil down whatever it is showing; logs <paramref name="failure"/> as a warning.</summary>
    public static void Hide(string failure = null)
    {
        if (IsActive) instance.Finish(failure);
    }

    // ------------------------------------------------------------------ Flow

    private IEnumerator RunAfterRender(System.Action work)
    {
        // The veil was laid out this frame and is presented at its end; one more frame covers
        // first-use costs (font material, canvas build) so it is on screen before work blocks.
        yield return null;
        yield return null;
        if (!active) yield break;
        try
        {
            work?.Invoke();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Finish($"loading failed: {e.Message}");
        }
    }

    private IEnumerator LoadSceneAfterRender(int buildIndex, System.Action applyOnLoaded)
    {
        yield return null;
        yield return null;
        if (!active) yield break;

        void OnLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.buildIndex != buildIndex) return;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnLoaded;
            illustrations = null;
            leaderSelector = null;
            introVideo = null;
            lookedUpIntro = false;
            try
            {
                applyOnLoaded?.Invoke();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            sceneReady = true;
        }

        // From the title nothing is running, so load in the background and keep the indicator
        // animating. Mid-campaign (pause menu) the old game would keep simulating during an
        // async load — AI turns, banners and messages on the persistent managers would leak
        // into the new scene — so that case keeps the original synchronous load, now behind
        // the already-rendered loading screen.
        bool campaignRunning = Game.Instance != null && Game.Instance.started;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnLoaded;
        bool started;
        try
        {
            if (campaignRunning)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(buildIndex);
                started = true;
            }
            else
            {
                sceneOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(buildIndex);
                started = sceneOp != null;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            sceneOp = null;
            started = false;
        }
        if (!started)
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnLoaded;
            Finish($"scene {buildIndex} could not be loaded");
        }
    }

    private void Update()
    {
        if (active)
        {
            if (Time.realtimeSinceStartup >= nextPollAt)
            {
                nextPollAt = Time.realtimeSinceStartup + PollInterval;
                Poll();
            }
            if (active) CheckWatchdogs();
        }

        float t = Time.unscaledTime;
        shownProgress = Mathf.MoveTowards(shownProgress, targetProgress, Time.unscaledDeltaTime * 0.9f);
        ApplyProgressVisuals();

        if (pips != null)
        {
            for (int i = 0; i < pips.Length; i++)
            {
                float phase = Mathf.Repeat(t * 1.4f - i * 0.22f, 1f);
                float pulse = Mathf.Pow(Mathf.Sin(phase * Mathf.PI), 3f);
                Color c = UiTheme.Gold;
                c.a = 0.22f + 0.78f * pulse;
                pips[i].color = c;
                pips[i].rectTransform.localScale = Vector3.one * (0.8f + 0.35f * pulse);
            }
        }
        if (glint != null)
        {
            glint.gameObject.SetActive(indeterminate);
            if (indeterminate)
            {
                float x = Mathf.PingPong(t * 0.6f, 1f);
                x = x * x * (3f - 2f * x);
                glint.anchoredPosition = new Vector2(x * (RailWidth - glint.sizeDelta.x), 0f);
            }
        }
    }

    // Reads the real state of the world to advance the bar/label and to decide when the board
    // is ready. Each stage's progress maps into its own band of the bar.
    private void Poll()
    {
        if (sceneReloading && !sceneReady)
        {
            float p = sceneOp != null ? Mathf.Clamp01(sceneOp.progress / 0.9f) : 0f;
            Advance("Opening the map", Band(0f, kind == Kind.ReturnToTitle ? 0.35f : 0.15f, p));
            return;
        }

        switch (kind)
        {
            case Kind.ReturnToTitle:
            {
                if (!StartupAssetsReady(out float assets)) { Advance(AssetStage(), Band(0.35f, 1f, assets)); return; }
                Finish(null);
                return;
            }
            case Kind.RestoreCampaign:
            {
                if (!StartupAssetsReady(out float assets)) { Advance(AssetStage(), Band(0.15f, 0.40f, assets)); return; }
                Board board = Board.Instance;
                if (board == null || board.hexes == null || board.hexes.Count == 0)
                {
                    float bp = board != null && board.progressBar != null ? board.progressBar.value : 0f;
                    Advance("Raising Middle-earth", Band(0.40f, 0.78f, bp));
                    return;
                }
                Game game = Game.Instance;
                if (game == null || !game.started) { Advance("Mustering the armies", 0.82f); return; }
                if (SaveSystem.PendingLoad != null) { Advance("Laying out your campaign", 0.92f); return; }
                if (game.GameOver || game.IsAcceptingPlayerInput || game.PlayerAutoplayEnabled) Finish(null);
                else Advance("Laying out your campaign", 0.97f);
                return;
            }
            case Kind.NewCampaign:
            {
                if (IntroVideoPlaying() || LeaderSelectionShown()) { Finish(null); return; }
                Board board = Board.Instance;
                bool built = board != null && board.hexes != null && board.hexes.Count > 0;
                float bp = built ? 1f : board != null && board.progressBar != null ? board.progressBar.value : 0f;
                Advance(built ? "Gathering the leaders" : "Raising Middle-earth", Band(0.05f, 0.95f, bp));
                return;
            }
            case Kind.StartCampaign:
                // Finished explicitly by Game once StartGame has run.
                return;
        }
    }

    private void CheckWatchdogs()
    {
        float now = Time.realtimeSinceStartup;
        float limit = kind switch
        {
            Kind.RestoreCampaign => 300f,
            Kind.NewCampaign => 240f,
            Kind.StartCampaign => 90f,
            _ => 180f,
        };
        if (now - shownAt > limit)
        {
            Finish($"timed out after {limit:0}s at stage '{stage}'");
            return;
        }
        // An exception during loading usually means the next stage will never arrive. Give the
        // game a grace period to recover (some exceptions are harmless), then let the player in.
        if (lastExceptionAt > 0f && now - lastExceptionAt > ExceptionGraceSeconds && now - lastStageChangeAt > ExceptionGraceSeconds)
        {
            Finish($"an exception interrupted loading at stage '{stage}'");
            return;
        }
        // Nothing moving at all for a long time (e.g. a restore whose saved leader never shows
        // up in the selector): stop hiding the game and let the player see where it stands.
        if (kind != Kind.StartCampaign && now - lastProgressAt > StallSeconds)
            Finish($"no loading progress for {StallSeconds:0}s at stage '{stage}'");
    }

    private void Finish(string failure)
    {
        if (!active) return;
        active = false;
        sceneOp = null;
        float seconds = Time.realtimeSinceStartup - shownAt;
        if (failure != null)
        {
            Debug.LogWarning($"[Loading] {kind}: hiding loading screen after {seconds:0.0}s - {failure}");
            if (kind == Kind.RestoreCampaign)
            {
                try { MessageDisplay.ShowMessage("The saved campaign may not have loaded fully", Color.red); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }
        else
        {
            Debug.Log($"[Loading] {kind}: ready after {seconds:0.0}s.");
        }
        targetProgress = 1f;
        group.blocksRaycasts = false;
        group.interactable = false;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        float start = group.alpha;
        float elapsed = 0f;
        while (elapsed < FadeOutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, 0f, Mathf.Clamp01(elapsed / FadeOutSeconds));
            yield return null;
        }
        group.alpha = 0f;
        fadeRoutine = null;
        if (!active) gameObject.SetActive(false);
    }

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (active && type == LogType.Exception) lastExceptionAt = Time.realtimeSinceStartup;
    }

    // ------------------------------------------------------------------ Readiness probes

    private bool StartupAssetsReady(out float progress)
    {
        bool decks = DeckManager.Instance != null && DeckManager.Instance.IsLoaded;
        if (illustrations == null) illustrations = FindAnyObjectByType<Illustrations>();
        bool art = illustrations == null || illustrations.IsLoaded;
        float artProgress = illustrations == null ? 1f : illustrations.LoadProgress;
        progress = Mathf.Clamp01((decks ? 0.25f : 0.02f) + artProgress * 0.75f);
        return decks && art;
    }

    private static string AssetStage() =>
        DeckManager.Instance != null && DeckManager.Instance.IsLoaded ? "Loading the painted world" : "Preparing the decks";

    private bool LeaderSelectionShown()
    {
        // The selector may only become active with the game UI; retry, but at most once a
        // second, since the scene-wide search competes with board generation.
        if (leaderSelector == null && Time.realtimeSinceStartup >= nextSelectorLookupAt)
        {
            nextSelectorLookupAt = Time.realtimeSinceStartup + 1f;
            leaderSelector = FindAnyObjectByType<LeaderSelector>(FindObjectsInactive.Include);
        }
        return leaderSelector != null && leaderSelector.SelectionScreenShown;
    }

    private bool IntroVideoPlaying()
    {
        if (!lookedUpIntro)
        {
            lookedUpIntro = true;
            IntroVideoManager intro = FindAnyObjectByType<IntroVideoManager>(FindObjectsInactive.Include);
            introVideo = intro != null ? intro.GetComponent<UnityEngine.Video.VideoPlayer>() : null;
        }
        return introVideo != null && introVideo.isActiveAndEnabled && introVideo.isPlaying;
    }

    // ------------------------------------------------------------------ Presentation

    private static float Band(float from, float to, float t) => Mathf.Lerp(from, to, Mathf.Clamp01(t));

    private void Advance(string label, float progress)
    {
        if (label != stage) SetStage(label);
        if (progress > targetProgress + 0.0005f)
        {
            targetProgress = Mathf.Min(progress, 0.99f);
            lastProgressAt = Time.realtimeSinceStartup;
        }
    }

    private void SetStage(string label)
    {
        stage = label;
        lastStageChangeAt = lastProgressAt = Time.realtimeSinceStartup;
        if (status != null) status.text = label + "…";
    }

    private void ApplyProgressVisuals()
    {
        if (fill == null) return;
        fill.gameObject.SetActive(!indeterminate);
        fill.sizeDelta = new Vector2(RailWidth * Mathf.Clamp01(shownProgress), fill.sizeDelta.y);
    }

    private static string TitleFor(Kind kind) => kind switch
    {
        Kind.RestoreCampaign => "Restoring your campaign",
        Kind.NewCampaign => "Raising Middle-earth",
        Kind.StartCampaign => "The war begins",
        _ => "Returning to the title",
    };

    private static string FirstStageFor(Kind kind) => kind switch
    {
        Kind.RestoreCampaign => "Reading the chronicle",
        Kind.NewCampaign => "Unrolling the map",
        Kind.StartCampaign => "Mustering the armies",
        _ => "Closing the campaign",
    };

    private static SessionLoadingScreen Ensure()
    {
        if (instance != null) return instance;

        var go = new GameObject("SessionLoadingScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        DontDestroyOnLoad(go);
        go.SetActive(false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var s = go.AddComponent<SessionLoadingScreen>();
        s.group = go.GetComponent<CanvasGroup>();
        var root = (RectTransform)go.transform;

        // Dark ink veil over everything; it also swallows clicks while loading.
        Image veil = NewImage(root, "Veil", new Color(0.018f, 0.017f, 0.015f, 0.97f));
        Stretch(veil.rectTransform);
        veil.raycastTarget = true;

        // Soft vertical shading so the veil isn't a flat slab.
        Image shadeTop = NewImage(root, "ShadeTop", new Color(0f, 0f, 0f, 0.55f));
        shadeTop.sprite = HudSprites.FadeDown;
        RectTransform st = shadeTop.rectTransform;
        st.anchorMin = new Vector2(0f, 0.6f); st.anchorMax = Vector2.one; st.offsetMin = st.offsetMax = Vector2.zero;
        Image shadeBottom = NewImage(root, "ShadeBottom", new Color(0f, 0f, 0f, 0.55f));
        shadeBottom.sprite = HudSprites.FadeUp;
        RectTransform sb = shadeBottom.rectTransform;
        sb.anchorMin = Vector2.zero; sb.anchorMax = new Vector2(1f, 0.4f); sb.offsetMin = sb.offsetMax = Vector2.zero;

        // Ink panel with the gold hairline, as used by the title plaques.
        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel));
        var panel = panelGo.GetComponent<RectTransform>();
        panel.SetParent(root, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(720f, 250f);
        var face = panelGo.GetComponent<CinematicPanel>();
        face.fillTop = new Color(0.07f, 0.065f, 0.05f, 0.92f);
        face.fillBottom = new Color(0.025f, 0.024f, 0.02f, 0.96f);
        face.line = new Color(0.80f, 0.64f, 0.36f, 0.65f);
        face.inset = 6f;
        face.ornament = 6f;
        face.bottomOrnament = true;
        face.raycastTarget = false;

        s.title = NewText(panel, "Title", 50f, true);
        RectTransform tr = s.title.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
        tr.anchoredPosition = new Vector2(0f, -34f); tr.sizeDelta = new Vector2(-60f, 64f);

        // Three pulsing gold diamonds: the "still working" indicator, alive even when progress
        // stalls on a long stage.
        var pipRow = new GameObject("Pips", typeof(RectTransform)).GetComponent<RectTransform>();
        pipRow.SetParent(panel, false);
        pipRow.anchorMin = pipRow.anchorMax = new Vector2(0.5f, 1f);
        pipRow.anchoredPosition = new Vector2(0f, -122f);
        pipRow.sizeDelta = new Vector2(90f, 16f);
        s.pips = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            Image pip = NewImage(pipRow, $"Pip{i}", UiTheme.Gold);
            RectTransform pr = pip.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(9f, 9f);
            pr.anchoredPosition = new Vector2((i - 1) * 26f, 0f);
            pr.localRotation = Quaternion.Euler(0f, 0f, 45f);
            s.pips[i] = pip;
        }

        // Hairline progress rail.
        Image rail = NewImage(panel, "Rail", new Color(0.80f, 0.64f, 0.36f, 0.22f));
        RectTransform rr = rail.rectTransform;
        rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0f);
        rr.pivot = new Vector2(0.5f, 0.5f);
        rr.anchoredPosition = new Vector2(0f, 82f);
        rr.sizeDelta = new Vector2(RailWidth, 3f);

        Image fillImg = NewImage(rr, "Fill", UiTheme.Gold);
        s.fill = fillImg.rectTransform;
        s.fill.anchorMin = new Vector2(0f, 0f); s.fill.anchorMax = new Vector2(0f, 1f); s.fill.pivot = new Vector2(0f, 0.5f);
        s.fill.anchoredPosition = Vector2.zero; s.fill.sizeDelta = Vector2.zero;

        Image glintImg = NewImage(rr, "Glint", UiTheme.Gold);
        s.glint = glintImg.rectTransform;
        s.glint.anchorMin = new Vector2(0f, 0f); s.glint.anchorMax = new Vector2(0f, 1f); s.glint.pivot = new Vector2(0f, 0.5f);
        s.glint.sizeDelta = new Vector2(110f, 0f);

        s.status = NewText(panel, "Status", 22f, false);
        RectTransform sr = s.status.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0f); sr.pivot = new Vector2(0.5f, 0f);
        sr.anchoredPosition = new Vector2(0f, 30f); sr.sizeDelta = new Vector2(-60f, 34f);

        instance = s;
        return s;
    }

    private static Image NewImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI NewText(RectTransform parent, string name, float size, bool display)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (display) UiTheme.StyleTitle(t, size); else UiTheme.StyleBody(t, size, UiTheme.Muted);
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
