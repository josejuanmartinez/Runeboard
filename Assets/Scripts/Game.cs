using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using System.Threading.Tasks;
using RetroLOTR.Scenarios;

public class Game : MonoBehaviour
{
    [Header("Sound")]
    public AudioSource soundPlayer;
    public AudioSource musicPlayer;

    [Header("Playable Leader (Player)")]
    public PlayableLeader player;
    [Header("Other Playable Leaders")]
    public List<PlayableLeader> competitors;
    [Header("Non Playable Leaders")]
    public List<NonPlayableLeader> npcs;
    [Header("Currently Playing")]
    public PlayableLeader currentlyPlaying;

    [Header("Movement")]
    public int characterMovement = 5;
    public int armyMovement = 5;
    public int cavalryMovement = 7;

    [Header("Caps")]
    public static int MAX_OBJECTS = 100;
    public static int MAX_CHARACTERS = 100;
    public static int MAX_PCS = 100;
    public static int MAX_TURNS = 999;

    [Header("References")]
    public StoresManager storesManager;
    public Board board;
    public CharacterIcons icons;
    public CanvasGroup selectedCharacterIconCanvasGroup;
    public CanvasGroup actionsCanvasGroup;
    public Button nextTurnButton;


    [Header("Starting info")]
    public int turn = 0;
    public bool started = false;

    [Header("AI")]
    [SerializeField] private AIDifficulty aiDifficulty = AIDifficulty.Normal;

    public bool PlayerAutoplayEnabled => playerAutoplayEnabled;

    public event Action<int> NewTurnStarted;

    private bool skipNextTurnPrompt = false;
    private bool playerAutoplayEnabled;
    private bool playerAutoplayTurnRunning;
    private bool playerTurnAcceptingInput;
    private readonly List<NpcFocusEntry> npcFocusEntries = new();
    private readonly HashSet<NonPlayableLeader> nplsActedThisRound = new();
    private bool alignedNplTurnsRunning;
    private bool leaderTransitionRunning;
    private bool blockLookAtUntilStartupPopupCloses;
    private bool startupPopupShown;
    public bool IsInitialTurnStarting => started && turn == 0 && !playerTurnAcceptingInput;
    // Same rationale as Board.Instance: dozens of card actions' condition/effect closures were
    // each independently resolving Game via FindFirstObjectByType<Game>() — a scene-wide search
    // repeated for every card scored, every pick, every character, every AI turn. Cached here.
    public static Game Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        if (!board) board = FindAnyObjectByType<Board>();
        if (!storesManager) storesManager = FindAnyObjectByType<StoresManager>();
        if (UtilityAIContextCacheManager.Instance == null) gameObject.AddComponent<UtilityAIContextCacheManager>();
        AIDifficultySettings.CurrentDifficulty = aiDifficulty;
    }

    private void RandomizeCompetitorVariants()
    {
        if (competitors == null || competitors.Count == 0) return;

        PlayableLeaders playableLeaders = FindAnyObjectByType<PlayableLeaders>();
        if (playableLeaders?.playableLeaders?.biomes == null) return;

        foreach (PlayableLeader competitor in competitors)
        {
            // A leader spawned from a scenario's self-owned character card already has its
            // authored variant applied (see NationSpawner step 1) — its hex *is* that variant's
            // start point, so a random re-pick here would desync the deck from the position.
            if (competitor == null || competitor.scenarioVariantLocked) continue;

            LeaderBiomeConfig biome = playableLeaders.playableLeaders.biomes
                .Find(b => string.Equals(b.characterName, competitor.characterName, StringComparison.OrdinalIgnoreCase));
            if (biome == null) continue;

            int variantCount = biome.variants?.Count ?? 0;
            int pick = UnityEngine.Random.Range(0, variantCount + 1);

            if (pick == 0 || variantCount == 0)
            {
                competitor.SetDeckSelection(biome.subdeckId, biome.deckIdentity, biome.description, null);
            }
            else
            {
                LeaderVariantConfig variant = biome.variants[pick - 1];
                string subdeckId = string.IsNullOrWhiteSpace(variant.subdeckId) ? biome.subdeckId : variant.subdeckId;
                competitor.SetDeckSelection(subdeckId, variant.deckIdentity, biome.description, variant.displayName, variant.characterName);
            }
        }
    }

    private void AssignAIandHumans()
    {
        // Find all ML-Agents in the scene
        List<Character> allCharacters = FindObjectsByType<Character>(FindObjectsSortMode.None).ToList();
        foreach(Character character in allCharacters)
        {
            character.isPlayerControlled = character.GetOwner() == player && !playerAutoplayEnabled;
        }
    }

    public void SelectPlayer(PlayableLeader playableLeader)
    {
        competitors = new();
        npcs = new();
        player = playableLeader;
        foreach (PlayableLeader otherPlayableLeader in FindObjectsByType<PlayableLeader>(FindObjectsSortMode.None))
        {
            if (otherPlayableLeader == playableLeader) continue;
            competitors.Add(otherPlayableLeader);
        }
        foreach (NonPlayableLeader nonPlayableLeader in FindObjectsByType<NonPlayableLeader>(FindObjectsSortMode.None))
        {
            npcs.Add(nonPlayableLeader);
        }
    }

    // A scenario can author the same playable leader at several hexes, one per variant (e.g. five
    // different starting hexes for Sauron), so each shows as its own carousel entry — but only one
    // of those sibling instances should remain in play once a variant is finally chosen: the
    // human's pick if it belongs to this leader name, otherwise a random survivor among the
    // AI-controlled siblings. Must run after SelectPlayer (player/competitors populated) and before
    // RandomizeCompetitorVariants/AssignAIandHumans/VictoryPoints tally.
    private void PruneUnselectedLeaderVariants()
    {
        if (player == null) return;

        List<PlayableLeader> all = new() { player };
        if (competitors != null) all.AddRange(competitors.Where(c => c != null));

        foreach (var group in all.Where(l => !string.IsNullOrWhiteSpace(l.characterName))
                                 .GroupBy(l => l.characterName, StringComparer.OrdinalIgnoreCase))
        {
            List<PlayableLeader> siblings = group.ToList();
            if (siblings.Count <= 1) continue;

            PlayableLeader survivor = siblings.Contains(player) ? player : siblings[UnityEngine.Random.Range(0, siblings.Count)];

            foreach (PlayableLeader sibling in siblings)
            {
                if (sibling == survivor) continue;
                competitors?.Remove(sibling);
                // One broken sibling must never abort StartGame — everything after this
                // (currentlyPlaying, turn start, AI assignment) would silently stay dead.
                try
                {
                    board.nationSpawner?.RemoveUnselectedScenarioLeader(sibling, survivor);
                }
                catch (Exception e)
                {
                    Debug.LogError($"PruneUnselectedLeaderVariants: failed to remove sibling '{sibling.characterName}' ({sibling.GetSelectedVariantName()}): {e}");
                }
            }
        }
    }

    public void StartGame()
    {
        FindFirstObjectByType<LeaderSelector>()?.ApplyCurrentSelection();
        // Must play before any other startup popup, camera pan, or tutorial step — SelectLeader
        // (driven by the carousel) has already finalized player's chosen variant by this point.
        VideoPopupManager.ShowForLeader(player);
        PruneUnselectedLeaderVariants();
        RandomizeCompetitorVariants();
        // Every playable leader's variant (human pick + AI randomization above) is final now, so
        // scenario PCs/characters authored with an owner-variant restriction can be resolved.
        board.nationSpawner?.ReconcileScenarioVariantOwnership();
        // Same timing: resolve independent spawnCondition characters/armies (any leader + variant,
        // not necessarily an ownership relationship).
        board.nationSpawner?.ReconcileScenarioSpawnConditions();
        // Same timing again: scenario Zone of Control hexes need the final leader instance to
        // mark as permanently revealed, so this must come after the reconciliation above.
        board.nationSpawner?.ApplyScenarioZoneOfControl(board.ActiveScenario);
        FindFirstObjectByType<Initialize>()?.UndoInitialState();
        PauseMenuController.PrepareForGameplay();

        turn = 0;
        nplsActedThisRound.Clear();
        alignedNplTurnsRunning = false;
        leaderTransitionRunning = false;
        started = true;
        Music.Instance?.PlayGameMusic();
        blockLookAtUntilStartupPopupCloses = true;
        startupPopupShown = false;

        currentlyPlaying = player;
        MessageDisplay.ClearPersistent();

        AssignNationColors();
        InitializePlayableLeaderIcons();
        if (player != null)
        {
            var layout = FindFirstObjectByType<Layout>();
            if (layout != null)
            {
                layout.SetNationColor(player.nationColor);
                layout.SetNationName(player.GetBiome()?.nationName);
            }
        }
        board.StartGame();
        DiscoverStartingRegions();
        HookBoardSelectionRefresh();
        AssignAIandHumans();
        VictoryPoints.RecalculateAndAssign(this);
        RefreshPlayableLeaderIconVictoryPoints();
        BuildPlayerCharacterIcons();
        SelectFirstPlayerCharacter();
        // SelectFirstPlayerCharacter() just re-enabled it as part of revealing the human's
        // widgets — hold it off until the turn actually starts below, otherwise the player
        // could end turn 0 while the game is supposed to be fully paused for the wait/instructions.
        if (nextTurnButton != null) nextTurnButton.enabled = false;
        StartCoroutine(BeginTurnZeroSequence());
    }

    // Camera is already panning to the player's first character (SelectFirstPlayerCharacter,
    // called just before this). Give that a moment to land, then — with the turn still not
    // started — run any onboarding instructions and hold here until every one of them is
    // closed. Only once the queue is empty does the turn actually begin (Turn 0 banner,
    // NewTurn(), etc.), matching every subsequent turn's flow.
    private IEnumerator BeginTurnZeroSequence()
    {
        // The leader intro video (shown at the top of StartGame()) must finish/close before the
        // Turn 0 banner, tutorial instructions, or the resource-gathering NewTurn() effects appear.
        while (VideoPopupManager.IsShowing) yield return null;

        yield return new WaitForSeconds(2f);

        TutorialInstructionsManager instructions = TutorialInstructionsManager.Instance;
        instructions.OpenNext();
        while (instructions.IsShowing) yield return null;

        NewTurnStarted?.Invoke(turn);
        FinalizeCampaignStart();
        // Reserves CenterDisplayLock immediately (synchronously, before anything below can
        // grab it first) even though the banner itself only appears 1.1s later — otherwise
        // currentlyPlaying.NewTurn() below (which can trigger the PC/region grant preview)
        // would win the race and the turn banner would wait behind it instead of the other
        // way around, unlike every subsequent turn where the banner already goes first.
        StartCoroutine(ShowTurnZeroBanner());
        HideHumanPlayerWidgetsWidgets();
        UtilityAIContextCacheManager.Instance?.BeginPlayerTurnPrecompute(this);
        currentlyPlaying.NewTurn();
        // NPLs don't act on turn 0 — they start from turn 1 (see BeginPlayerTurnSequence, which
        // calls StartAlignedNplTurns for every turn after this one).
        yield return RefreshDeckUiAfterStartup();
        // Recommendations and NPL contexts are optional background work. Never withhold the
        // human turn while the cache processes a large campaign's leaders.
        if (playerAutoplayEnabled)
        {
            RefreshPlayerControlState();
            StartCoroutine(RunPlayerAutoplayTurn(resumingHumanTurn: true));
            yield break;
        }

        ShowHumanPlayerWidgetsWidgets();
        if (nextTurnButton != null) nextTurnButton.enabled = true;
        playerTurnAcceptingInput = true;
        MessageDisplay.ClearPersistent();
    }

    // There is no tutorial gating game start anymore — every campaign begins with everything
    // a leader is entitled to: (for the human) their chosen variant identity. Nobody gets
    // automatic starting objects anymore — those are assigned per-character in the Scenario
    // Creator (ScenarioCharacter.startingObjects, see NationSpawner.SpawnScenarioCharacter),
    // so there's nothing left to grant for a non-scenario campaign start either.
    private void FinalizeCampaignStart()
    {
        player?.ApplyVariantTransformation();
    }

    private IEnumerator ShowTurnZeroBanner()
    {
        // Reserved synchronously (before anything below, including currentlyPlaying.NewTurn()
        // back in StartGame, can grab it first) for the same reason CenterDisplayLock itself
        // is pre-reserved below: otherwise a resource grant would see TurnBanner.IsShowing as
        // false during this ~1.1s delay and queue for CenterDisplayLock too early.
        TurnBanner.ReserveSlot();
        yield return CenterDisplayLock.WaitCoroutine();
        yield return new WaitForSeconds(1.1f);
        TurnBanner.Show(turn, ResolveBannerSprite(player), lockAlreadyHeld: true);
        TurnBanner.ShowGatheringResources(playSound: false);
    }

    private static Sprite ResolveBannerSprite(PlayableLeader leader)
    {
        if (leader == null) return null;
        LeaderBiomeConfig biome = leader.GetBiome();
        if (biome == null) return null;

        string bannerName = null;
        string subdeckId = leader.GetSelectedSubdeckId();
        if (!string.IsNullOrWhiteSpace(subdeckId) && biome.variants != null)
        {
            LeaderVariantConfig variant = biome.variants.Find(v =>
                v != null &&
                ((!string.IsNullOrWhiteSpace(v.variantId) && string.Equals(v.variantId, subdeckId, System.StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrWhiteSpace(v.subdeckId) && string.Equals(v.subdeckId, subdeckId, System.StringComparison.OrdinalIgnoreCase))));
            if (!string.IsNullOrWhiteSpace(variant?.banner))
                bannerName = variant.banner;
        }
        if (string.IsNullOrWhiteSpace(bannerName))
            bannerName = biome.banner;
        if (string.IsNullOrWhiteSpace(bannerName)) return null;

        Illustrations illustrations = FindFirstObjectByType<Illustrations>();
        return illustrations != null ? illustrations.GetIllustrationByName(bannerName, false) : null;
    }

    private void DiscoverStartingRegions()
    {
        if (player == null || board?.regionLabelManager == null) return;
        foreach (var character in player.controlledCharacters)
        {
            string region = character.hex?.GetLandRegion();
            if (string.IsNullOrWhiteSpace(region)) continue;
            player.TryDiscoverRegion(region);
            board.regionLabelManager.ShowLabel(region.Trim());
        }
    }

    private void HookBoardSelectionRefresh()
    {
        if (board == null) return;
        board.SelectedCharacterChanged -= HandleSelectedCharacterChanged;
        board.SelectedCharacterChanged += HandleSelectedCharacterChanged;
    }

    private void HandleSelectedCharacterChanged(Character previous, Character current)
    {
        Card.RequestInteractionRefreshAll();
        FindFirstObjectByType<ActionsManager>()?.RefreshInteractableState();
        OpportunityHexHinter.Refresh(current);
    }

    private IEnumerator RefreshDeckUiAfterStartup()
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : FindFirstObjectByType<DeckManager>();
        if (deckManager == null) yield break;

        deckManager.InitializeHandsForCurrentGame();
        FindFirstObjectByType<ActionsManager>()?.RefreshInteractableState();
    }

    private void InitializePlayableLeaderIcons()
    {
        PlayableLeaderIcons leaderIcons = FindFirstObjectByType<PlayableLeaderIcons>();
        if (leaderIcons == null) return;

        if (player != null) leaderIcons.Instantiate(player);
        if (competitors == null) return;

        foreach (PlayableLeader competitor in competitors)
        {
            if (competitor != null) leaderIcons.Instantiate(competitor);
        }
    }

    private void AssignNationColors()
    {
        List<Leader> leaders = GetAllLeadersForNationColors();
        Colors colors = FindFirstObjectByType<Colors>();
        Color[] palette = colors != null ? colors.nationColors : null;
        for (int i = 0; i < leaders.Count; i++)
        {
            Leader leader = leaders[i];
            if (leader == null) continue;
            leader.nationColor = GetNationColorForIndex(i, palette);
        }
    }

    private List<Leader> GetAllLeadersForNationColors()
    {
        List<Leader> leaders = new();
        if (player != null) leaders.Add(player);
        if (competitors != null) leaders.AddRange(competitors.Where(c => c != null));
        if (npcs != null) leaders.AddRange(npcs.Where(n => n != null));
        return leaders;
    }

    private static Color GetNationColorForIndex(int index, Color[] palette)
    {
        if (palette != null && index < palette.Length)
        {
            return palette[index];
        }

        float hue = Mathf.Repeat(0.137508f * index, 1f);
        return Color.HSVToRGB(hue, 0.65f, 0.95f);
    }

    private void RefreshPlayableLeaderIconVictoryPoints()
    {
        PlayableLeaderIcons leaderIcons = FindFirstObjectByType<PlayableLeaderIcons>();
        if (leaderIcons == null) return;
        leaderIcons.RefreshVictoryPointsForAll();
        leaderIcons.UpdateVictoryPointColors();
    }

    public bool PointToCharacterWithMissingActions()
    {
        // Don't steal the camera/selection out from under an open opportunity-card offer —
        // the player needs the selected character and camera to stay put long enough to
        // actually read and click it (bloom mode in particular anchors to that character's
        // hex, so panning away mid-offer makes it unreachable).
        if (SituationCardsUI.IsShowing) return false;

        // Make sure all characters have actioned
        Character stillNotActioned = player.controlledCharacters.Find(x => !x.hasActionedThisTurn && !x.killed && board.selectedCharacter != x);
        if ( stillNotActioned != null) board.SelectCharacter(stillNotActioned, true, 1.0f, 2.0f);
        return stillNotActioned != null;
    }

    public async void SelectNextCharacterOrFinishTurnPrompt()
    {
        if (!IsPlayerCurrentlyPlaying() || player == null || board == null) return;

        List<Character> characters = player.controlledCharacters;
        if (characters == null || characters.Count == 0) return;

        await WaitForNoUiMessagesAsync();
        if (!IsPlayerCurrentlyPlaying() || player == null || board == null) return;

        Character current = board.selectedCharacter;
        Character nextCharacter = null;

        // Try to find the next character without an action, cycling from current selection
        int startIndex = characters.IndexOf(current);
        if (startIndex < 0) startIndex = -1;
        for (int offset = 1; offset <= characters.Count; offset++)
        {
            int i = (startIndex + offset) % characters.Count;
            var c = characters[i];
            if (c != null && !c.killed && !c.hasActionedThisTurn)
            {
                nextCharacter = c;
                break;
            }
        }

        if (nextCharacter != null)
        {
            board.SelectCharacter(nextCharacter);
            return;
        }

        // No characters with free actions; offer to finish turn
        bool finish = await ConfirmationDialog.AskImmediate(
            "No more characters with free actions are available. You may still have movement left. Finish the turn?",
            "Finish Turn",
            "Cancel");
        if (finish)
        {
            skipNextTurnPrompt = true;
            NextPlayer();
        }
    }

    private async Task WaitForNoUiMessagesAsync()
    {
        if (!MessageDisplayNoUI.IsBusy()) return;
        int safety = 0;
        while (MessageDisplayNoUI.IsBusy() && safety < 200)
        {
            await Task.Delay(50);
            safety++;
        }
    }

    public void SelectNextCharacterInPriorityCycle()
    {
        if (!IsPlayerCurrentlyPlaying() || player == null || board == null) return;

        List<Character> characters = player.controlledCharacters;
        if (characters == null || characters.Count == 0) return;

        List<Character> ordered = new();
        ordered.AddRange(characters.Where(c => c != null && !c.killed && !c.hasActionedThisTurn));
        ordered.AddRange(characters.Where(c => c != null && !c.killed && c.hasActionedThisTurn && c.moved < c.GetMaxMovement()));
        ordered.AddRange(characters.Where(c => c != null && !c.killed && c.hasActionedThisTurn && c.moved >= c.GetMaxMovement()));

        if (ordered.Count == 0) return;

        Character current = board.selectedCharacter;
        int currentIndex = ordered.IndexOf(current);
        int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % ordered.Count : 0;
        Character next = ordered[nextIndex];
        if (next == null) return;

        // An own character must always be selectable: if its hex is fogged (e.g. it was handed
        // over by a path that skipped visibility bookkeeping), Board.SelectHex would silently
        // no-op and Tab would appear dead on that character forever. Reveal it first.
        if (next.hex != null && next.hex.IsHidden())
        {
            if (!player.visibleHexes.Contains(next.hex)) player.visibleHexes.Add(next.hex);
            next.hex.RevealArea(1, false, null);
        }
        board.SelectCharacter(next);
    }

    public async void NextPlayer()
    {
        if (leaderTransitionRunning) return;
        PopupManager.CloseAll();
        ConfirmationDialog.CloseAll();
        SelectionDialog.CloseAll();
        // Close any opportunity-card bloom left open for the human before handing control to
        // AI leaders (same call RunPlayerAutoplayTurn uses at Game.cs:969) - it must never sit
        // open over the board while AI turns run.
        SituationCardsUI.Instance?.DismissForAutoplay();

        bool shouldPrompt = currentlyPlaying == player && !skipNextTurnPrompt;
        if (currentlyPlaying == player) playerTurnAcceptingInput = false;
        skipNextTurnPrompt = false;

        if (shouldPrompt)
        {
            bool hasPendingActions = player.controlledCharacters.Any(x => !x.killed && !x.hasActionedThisTurn);
            bool hasPlayableCards = HasPlayableCardsInHand();
            string message = (hasPendingActions && hasPlayableCards)
                ? "Some characters have not actioned yet. End turn?"
                : "End turn?";

            bool finishTurn = await ConfirmationDialog.AskImmediate(message, "Finish Turn", "Cancel");
            if (!finishTurn)
            {
                Character nextCharacter = player.controlledCharacters.Find(x => !x.killed && !x.hasActionedThisTurn);
                if (nextCharacter != null)
                {
                    board.SelectCharacter(nextCharacter, true, 1.0f, 0.0f);
                }
                else
                {
                    Character firstAlive = player.controlledCharacters.Find(x => !x.killed);
                    if (firstAlive != null) board.SelectCharacter(firstAlive, true, 1.0f, 0.0f);
                }
                return;
            }
        }

        PlayableLeader next = FindNextTurnLeader(currentlyPlaying);

        // If no one else alive and player alive, victory; otherwise defeat
        if (next == null)
        {
            EndGame(player != null && !player.killed);
            return;
        }

        // If the only remaining leader is the player and there are no competitors left, end game as win
        if (next == player && !player.killed && competitors.All(c => c == null || c.killed))
        {
            EndGame(true);
            return;
        }

        leaderTransitionRunning = true;
        HideHumanPlayerWidgetsWidgets();
        StartCoroutine(TransitionToLeader(next));
    }

    // Finish any alignment-matched NPL work before another playable AI begins. This lets a
    // human end their turn immediately without allowing shared AI/action state to overlap.
    private IEnumerator TransitionToLeader(PlayableLeader next)
    {
        while (alignedNplTurnsRunning) yield return null;

        currentlyPlaying = next;

        if (currentlyPlaying == player)
        {
            // Warm the frame-budgeted context cache while the human-turn handoff runs. The
            // human-aligned NPL queue and player recommendations can both reuse it.
            UtilityAIContextCacheManager.Instance?.BeginPlayerTurnPrecompute(this);
            StartCoroutine(BeginPlayerTurnSequence());
        }
        else
        {
            HideHumanPlayerWidgetsWidgets();
            MessageDisplay.ShowPersistent($"{GetAlignmentDisplayName(currentlyPlaying.alignment)} nations are playing", Color.yellow);
            MessageDisplayNoUI.SetPaused(true);
            board.RefreshRelevantHexes();
            currentlyPlaying.NewTurn();
            StartAlignedNplTurns(currentlyPlaying.GetAlignment());
        }
        leaderTransitionRunning = false;
    }

    private static string GetAlignmentDisplayName(AlignmentEnum alignment)
    {
        return alignment switch
        {
            AlignmentEnum.freePeople => "Free People",
            AlignmentEnum.darkServants => "Dark Servants",
            _ => "Neutral"
        };
    }

    // RefreshForNewTurn() is the WaitUntilEndOfTurn()-free half of Leader.NewTurn(), allowing an
    // NPL to act in its alignment's window without advancing the playable-leader rotation.
    private void StartAlignedNplTurns(AlignmentEnum alignment)
    {
        if (alignedNplTurnsRunning) return;
        if (npcs == null || !npcs.Any(n => n != null && !n.killed && !nplsActedThisRound.Contains(n) && n.GetAlignment() == alignment)) return;
        StartCoroutine(ProcessAlignedNonPlayableLeaderTurns(alignment));
    }

    private IEnumerator ProcessAlignedNonPlayableLeaderTurns(AlignmentEnum alignment)
    {
        alignedNplTurnsRunning = true;
        try
        {
            foreach (NonPlayableLeader npl in npcs.Where(n => n != null && !n.killed && n.GetAlignment() == alignment).ToList())
            {
                if (!nplsActedThisRound.Add(npl)) continue;
                npl.RefreshForNewTurn();
                if (npl.killed) continue;
                yield return AITurnController.ExecuteLeaderTurn(npl);
                if (currentlyPlaying == player)
                {
                    FindFirstObjectByType<ActionsManager>()?.RefreshInteractableState();
                }
                // Explicit frame boundary between leaders in addition to the AI scoring budget.
                yield return null;
            }
        }
        finally
        {
            alignedNplTurnsRunning = false;
        }
    }

    private void NewTurn()
    {
        MessageDisplay.ClearPersistent();
        PopupManager.CloseAll();
        ConfirmationDialog.CloseAll();
        SelectionDialog.CloseAll();
        turn++;
        nplsActedThisRound.Clear();
        if (turn >= MAX_TURNS)
        {
            EndGame(false);
            return;
        }
        AdvanceTemporaryPcVisibility();
        board?.ClearAllScouting();
        AnnounceScoutingStatus();
        TurnBanner.Show(turn, ResolveBannerSprite(player));
        TurnBanner.ShowGatheringResources();
        NewTurnStarted?.Invoke(turn);
        storesManager.AdvanceTurn();
    }

    public void QueueNpcFocus(Leader leader, Hex hex)
    {
        if (leader == null || hex == null) return;
        if (player != null && leader == player) return;
        npcFocusEntries.Add(new NpcFocusEntry { leader = leader, hex = hex });
    }

    private IEnumerator BeginPlayerTurnSequence()
    {
        HideHumanPlayerWidgetsWidgets();
        MessageDisplayNoUI.SetPaused(true);

        yield return PlayNpcFocusSequence();

        MessageDisplayNoUI.SetPaused(false);
        MessageDisplay.ClearPersistent();

        NewTurn();
        board.RefreshRelevantHexes();

        RefreshPlayerControlState();
        if (playerAutoplayEnabled)
        {
            currentlyPlaying.RefreshForNewTurn();
            StartAlignedNplTurns(currentlyPlaying.GetAlignment());
            yield return RunPlayerAutoplayTurn();
            yield break;
        }

        currentlyPlaying.NewTurn();
        StartAlignedNplTurns(currentlyPlaying.GetAlignment());

        // Precompute was already kicked off in NextPlayer() and continues within its frame
        // budget. Human input must not depend on the size of that background queue.

        yield return WaitForCameraAndMessages();

        ShowHumanPlayerWidgetsWidgets();
        SelectFirstPlayerCharacter();
        playerTurnAcceptingInput = true;
    }

    private IEnumerator PlayNpcFocusSequence()
    {
        if (npcFocusEntries.Count == 0) yield break;
        BoardNavigator navigator = BoardNavigator.Instance;
        if (navigator == null)
        {
            npcFocusEntries.Clear();
            yield break;
        }

        List<Leader> leaderOrder = new();
        for (int i = 0; i < npcFocusEntries.Count; i++)
        {
            Leader leader = npcFocusEntries[i].leader;
            if (leader != null && !leaderOrder.Contains(leader)) leaderOrder.Add(leader);
        }

        for (int i = 0; i < leaderOrder.Count; i++)
        {
            Leader leader = leaderOrder[i];
            for (int j = 0; j < npcFocusEntries.Count; j++)
            {
                if (npcFocusEntries[j].leader != leader) continue;
                navigator.EnqueueNpcPlaybackFocus(npcFocusEntries[j].hex);
            }
            while (navigator.HasPendingFocus())
            {
                yield return null;
            }
        }

        npcFocusEntries.Clear();
    }

    private IEnumerator WaitForCameraAndMessages()
    {
        BoardNavigator navigator = BoardNavigator.Instance;
        while (MessageDisplayNoUI.IsBusy() || MessageDisplay.IsBusy() || (navigator != null && navigator.HasPendingFocus()))
        {
            yield return null;
        }
    }

    private struct NpcFocusEntry
    {
        public Leader leader;
        public Hex hex;
    }

    private void AnnounceScoutingStatus()
    {
        if (board == null || player == null || board.hexes == null) return;

        foreach (Hex hex in board.hexes.Values)
        {
            if (hex == null) continue;
            int scoutedTurns = hex.GetScoutedTurnsRemaining(player);
            if (scoutedTurns <= 0) continue;

            PC pc = hex.GetPCData();
            string message = null;
            if (pc != null && pc.temporaryRevealTurns > 0)
            {
                message = $"<sprite name=\"light\">light Light fades ({pc.temporaryRevealTurns} turn{(pc.temporaryRevealTurns == 1 ? "" : "s")} left)";
            }
            else if (pc != null && pc.temporaryHiddenTurns > 0)
            {
                message = $"<sprite name=\"darkness\">darkness Darkness fades ({pc.temporaryHiddenTurns} turn{(pc.temporaryHiddenTurns == 1 ? "" : "s")} left)";
            }
            else
            {
                message = $"<sprite name=\"scout\">scout Scouted: {scoutedTurns} turn{(scoutedTurns == 1 ? "" : "s")} left";
            }

            MessageDisplayNoUI.ShowMessage(hex, player, message, Color.yellow, false);
        }
    }

    private void AdvanceTemporaryPcVisibility()
    {
        List<Leader> leaders = new();
        if (player != null) leaders.Add(player);
        if (competitors != null) leaders.AddRange(competitors.Where(c => c != null));
        if (npcs != null) leaders.AddRange(npcs.Where(n => n != null));

        foreach (Leader leader in leaders)
        {
            foreach (PC pc in leader.controlledPcs)
            {
                if (pc == null) continue;
                pc.TickTemporaryVisibility();
                if (pc.hex != null) pc.hex.RedrawPC();
            }
        }
    }

    private void HideHumanPlayerWidgetsWidgets()
    {
        SetCanvasGroupVisible(selectedCharacterIconCanvasGroup, false);
        SetCanvasGroupVisible(actionsCanvasGroup, false);
        nextTurnButton.enabled = false;
    }
    private void ShowHumanPlayerWidgetsWidgets()
    {
        SetCanvasGroupVisible(selectedCharacterIconCanvasGroup, true);
        SetCanvasGroupVisible(actionsCanvasGroup, true);
        nextTurnButton.enabled = true;
    }

    private static void SetCanvasGroupVisible(CanvasGroup canvasGroup, bool visible)
    {
        if (canvasGroup == null) return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    private void BuildPlayerCharacterIcons()
    {
        if (player == null) return;
        icons?.BuildIconsForPlayer(player);
    }

    private void SelectFirstPlayerCharacter()
    {
        if (player == null || board == null) return;

        if (!startupPopupShown)
        {
            blockLookAtUntilStartupPopupCloses = false;
        }

        Character firstAlive = player.controlledCharacters
            .FirstOrDefault(c => c != null && !c.killed);

        // Every controlled character's hex — not just the first — must be visible, or scattered
        // scenario starts leave characters in fog that Board.SelectHex refuses to select.
        foreach (Character c in player.controlledCharacters)
        {
            if (c == null || c.killed || c.hex == null) continue;
            if (!player.visibleHexes.Contains(c.hex)) player.visibleHexes.Add(c.hex);
            if (c != firstAlive) c.hex.RevealArea(1, false, null);
        }

        if (firstAlive != null)
        {
            if (firstAlive.hex != null)
            {
                firstAlive.hex.RevealArea(1, true, null);
            }
            ShowHumanPlayerWidgetsWidgets();
            // Skip re-selecting if this character is already the selection (e.g. Leader.
            // WaitUntilEndOfTurn already selected it earlier this same turn-start sequence) -
            // Board.SelectHex's single-character branch unconditionally replays the character's
            // voice bark on every call, so a redundant call here double-plays it.
            if (board.selectedCharacter != firstAlive)
            {
                board.SelectCharacter(firstAlive, true, 1.0f, 0.0f);
            }
        }
        else
        {
            HideHumanPlayerWidgetsWidgets();
        }
    }

    // Helper method to find the next alive leader in turn order
    private PlayableLeader FindNextTurnLeader(PlayableLeader current)
    {
        List<PlayableLeader> order = new();
        if (player != null && !player.killed) order.Add(player);
        if (competitors != null) order.AddRange(competitors.Where(c => c != null && !c.killed));

        if (order.Count == 0) return null;

        int currentIndex = order.IndexOf(current);
        int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % order.Count : 0;
        return order[nextIndex];
    }

    // Add this method to handle game ending
    public void EndGame(bool win)
    {
        HideHumanPlayerWidgetsWidgets();
        if (win) MessageDisplay.ShowMessage("Victory!", Color.green); else MessageDisplay.ShowMessage("Defeat!", Color.red);

        Application.Quit();
        Debug.Log("Game Ended!");
    }

    public bool IsPlayerCurrentlyPlaying()
    {
        return currentlyPlaying == player;
    }

    // IsPlayerCurrentlyPlaying() alone stays true through a NonPlayableLeader's aligned sub-turn
    // (ProcessAlignedNonPlayableLeaderTurns runs nested inside the human's own turn window, with
    // currentlyPlaying == player the whole time) — so camera-follow/reveal-pan code that gates on
    // it fires for NPL moves too, visibly panning the human's camera as if it were their own
    // character. Use this instead wherever a live camera pan is about to happen; NPL moves are
    // meant to be queued (see QueueNpcFocus/PlayNpcFocusSequence) and replayed as a batch instead.
    //
    // Gated on AITurnController.CurrentExecutingLeader rather than the aligned-NPL-batch flag:
    // ExecuteLeaderTurn also drives the human's own Watch Mode/autoplay turn (leaderTurnInProgress
    // makes it a mutex — only one leader's turn ever executes at a time), so checking "is it an NPL
    // specifically, not just any AI-driven turn" is what keeps the camera panning during the
    // player's own autoplay even if it happens to be scheduled back-to-back with an NPL's turn.
    public bool IsHumanActivelyActing()
    {
        Leader executing = AITurnController.CurrentExecutingLeader;
        return IsPlayerCurrentlyPlaying() && (executing == null || executing == player);
    }

    public bool IsPlayerAutoplayEnabledFor(Leader leader)
    {
        return (playerAutoplayEnabled || playerAutoplayTurnRunning) && leader != null && leader == player;
    }

    public void TogglePlayerAutoplay()
    {
        if (!started || player == null)
        {
            Debug.LogWarning("Player autoplay is only available after a game has started.");
            return;
        }

        playerAutoplayEnabled = !playerAutoplayEnabled;
        RefreshPlayerControlState();

        if (!playerAutoplayEnabled)
        {
            CardCenterPreview.Instance?.HidePreview();
            MessageDisplay.ShowPersistent(
                playerAutoplayTurnRunning
                    ? "Autoplay will stop after the current turn"
                    : "Autoplay disabled",
                Color.yellow);
            return;
        }

        PopupManager.CloseAll();
        ConfirmationDialog.CloseAll();
        SelectionDialog.CloseAll();
        SituationCardsUI.Instance?.DismissForAutoplay();
        OpportunityHexHinter.ClearAll();
        MessageDisplay.ShowPersistent("Autoplay enabled — Ctrl+Shift+Tab to stop", Color.yellow);

        if (currentlyPlaying == player && playerTurnAcceptingInput && !playerAutoplayTurnRunning)
        {
            StartCoroutine(RunPlayerAutoplayTurn(resumingHumanTurn: true));
        }
    }

    /// <summary>Runs exactly the current human player's turn under AI control, then returns to normal turn flow.</summary>
    public void AutoplayOneTurn()
    {
        if (!started || player == null || currentlyPlaying != player || !playerTurnAcceptingInput || playerAutoplayTurnRunning)
        {
            Debug.LogWarning("Autoplay one turn is only available during the active player's turn.");
            return;
        }

        StartCoroutine(RunPlayerAutoplayTurn(resumingHumanTurn: true));
    }

    // Keeps ownership/viewpoint (game.player) separate from who makes this leader's decisions.
    // Existing actions already use isPlayerControlled to select their human or AI branches.
    public void RefreshPlayerControlState()
    {
        if (player?.controlledCharacters == null) return;
        bool aiControlsPlayer = playerAutoplayEnabled || playerAutoplayTurnRunning;
        foreach (Character character in player.controlledCharacters)
        {
            if (character != null) character.isPlayerControlled = !aiControlsPlayer;
        }
    }

    private IEnumerator RunPlayerAutoplayTurn(bool resumingHumanTurn = false)
    {
        if (playerAutoplayTurnRunning || player == null || currentlyPlaying != player) yield break;
        if (player.killed)
        {
            EndGame(false);
            yield break;
        }

        playerAutoplayTurnRunning = true;
        playerTurnAcceptingInput = false;
        RefreshPlayerControlState();
        HideHumanPlayerWidgetsWidgets();
        PopupManager.CloseAll();
        ConfirmationDialog.CloseAll();
        SelectionDialog.CloseAll();
        SituationCardsUI.Instance?.DismissForAutoplay();
        OpportunityHexHinter.ClearAll();

        yield return AITurnController.ExecuteLeaderTurn(
            player,
            presentChosenCards: true,
            skipAlreadyActionedCharacters: resumingHumanTurn);

        CardCenterPreview.Instance?.HidePreview();
        playerAutoplayTurnRunning = false;
        RefreshPlayerControlState();

        if (currentlyPlaying == player && !player.killed)
        {
            skipNextTurnPrompt = true;
            NextPlayer();
        }
        else if (player.killed)
        {
            EndGame(false);
        }
    }

    public bool ShouldBlockLookAtUntilStartupPopupCloses()
    {
        return started && blockLookAtUntilStartupPopupCloses;
    }

    public void NotifyStartupPopupShown()
    {
        if (!blockLookAtUntilStartupPopupCloses || startupPopupShown) return;
        startupPopupShown = true;
    }

    public void NotifyStartupPopupClosed()
    {
        if (!blockLookAtUntilStartupPopupCloses || !startupPopupShown) return;
        blockLookAtUntilStartupPopupCloses = false;
    }

    private bool HasPlayableCardsInHand()
    {
        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : FindFirstObjectByType<DeckManager>();
        if (deckManager == null || player == null) return false;

        IReadOnlyList<CardData> hand = deckManager.GetFullDeck(player);
        if (hand == null || hand.Count == 0) return false;

        List<Character> unactioned = player.controlledCharacters
            .Where(c => c != null && !c.killed && !c.hasActionedThisTurn)
            .ToList();

        if (unactioned.Count == 0) return false;

        foreach (Character character in unactioned)
        {
            foreach (CardData card in hand)
            {
                if (card == null) continue;
                if (card.EvaluatePlayability(character, null, null))
                {
                    return true;
                }
            }
        }
        return false;
    }

}
