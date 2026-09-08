using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.Video;
using System.Linq;
using System;
using System.Globalization;
using System.Collections;
using UnityEngine.UI;
using RetroLOTR.Scenarios;

public class LeaderSelector : SearcherByName
{
    private class LeaderSelectionEntry
    {
        // The exact instance this entry was built from. A scenario can author the same playable
        // leader at several hexes (one per variant), each its own GameObject/starting point, so
        // selection must resolve back to *this* instance — never re-look-up by name, which would
        // pick an arbitrary sibling instead of the one the player actually clicked.
        public PlayableLeader leaderInstance;
        public string baseLeaderName;
        public string characterName;
        public AlignmentEnum alignment;
        public string displayName;
        public string variantName;
        public string assetName;
        public string baseDescription;
        public string variantDescription;
        public string deckIdentity;
        public string subdeckId;
    }

    public CircularCardCarousel leaderCarousel;
    public GameObject carouselItemPrefab;
    public VideoPlayer introVideo;
    // public VideoPlayer leaderVideo;
    public TypewriterEffect typewriterEffect;
    public TextMeshProUGUI textUI;
    public TextMeshProUGUI variantTextUI;
    public TextMeshProUGUI deckTextUI;
    public GameObject progress;
    public GameObject progressText;
    public GameObject leaderSelectionFullScreen;
    public Image bannerImage;
    public Button startGameButton;

    readonly List<LeaderSelectionEntry> selectionEntries = new();
    readonly List<GameObject> carouselItems = new();

    // Tracked per-instance (not by name): a scenario can spawn several sibling instances sharing
    // the same leader name, one per authored variant/hex, and each must get its own carousel entry.
    readonly HashSet<PlayableLeader> loadedLeaders = new();
    bool loadedFirst = false;
    bool selectionScreenShown = false;
    bool selectionScreenQueued = false;
    Coroutine deferredRefreshRoutine;
    LeaderSelectionEntry lastVoicedSelection;
    bool startConfirmationPending;
    Canvas rootCanvas;
    void Awake()
    {
        rootCanvas = GetComponentInParent<Canvas>(true);
        if (introVideo != null)
        {
            introVideo.loopPointReached += OnIntroVideoFinished;
        }

        if (leaderCarousel != null)
        {
            leaderCarousel.RegisterOnSelectionChanged(SelectLeader);
        }

        ConfigureStartGameButton();
    }

    void OnDestroy()
    {
        if (introVideo != null)
        {
            introVideo.loopPointReached -= OnIntroVideoFinished;
        }

        if (leaderCarousel != null)
        {
            leaderCarousel.UnregisterOnSelectionChanged(SelectLeader);
        }

        startGameButton?.onClick.RemoveListener(ConfirmStartGame);
    }

    void Update()
    {
        // FindObjectsByType scales with total scene object count, not just matching-type
        // count — with thousands of hexes now in the scene this isn't free (measured ~13ms/
        // call in profiling). Its only job is populating the initial carousel, so once the
        // selection screen is up there are no more leaders left to discover — stop scanning.
        if (!selectionScreenShown)
        {
            List<PlayableLeader> playableLeaders = FindObjectsByType<PlayableLeader>(FindObjectsSortMode.None).ToList();
            if (loadedLeaders.Count < playableLeaders.Count)
            {
                for(int i=0; i<playableLeaders.Count; i++)
                {
                    PlayableLeader playableLeader = playableLeaders[i];
                    if (loadedLeaders.Contains(playableLeader)) continue;
                    loadedLeaders.Add(playableLeader);

                    if (!IsLeaderAllowedForActiveSelection(playableLeader)) continue;

                    AddLeaderOptions(playableLeader);

                    if (!loadedFirst)
                    {
                        loadedFirst = true;
                        if (leaderCarousel != null)
                        {
                            leaderCarousel.SetIndex(0);
                        }
                        SelectLeader(0);
                        ShowLeaderSelectionIfReady();
                    }

                    if (selectionScreenShown || selectionScreenQueued)
                    {
                        RequestLeaderSelectionRefresh();
                    }
                }
            }
        }

        if (loadedFirst)
        {
            ShowLeaderSelectionIfReady();
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && (selectionScreenShown || selectionScreenQueued))
        {
            RequestLeaderSelectionRefresh();
        }
    }

    void OnIntroVideoFinished(VideoPlayer player)
    {
        ShowLeaderSelectionIfReady();
    }

    // Leader selection no longer waits on the intro video: the video used to take about
    // as long as board loading, so gating on it cost nothing. Now that loading is fast,
    // waiting for the full video would just add dead time on top of an already-ready
    // board — so this fires purely off loadedFirst. ShowLeaderSelectionDeferred() below
    // deactivates the video's GameObject when the screen appears, cutting it short
    // instead of waiting for it to finish naturally.
    void ShowLeaderSelectionIfReady()
    {
        if (!loadedFirst)
        {
            return;
        }

        if (selectionScreenShown || selectionScreenQueued)
        {
            return;
        }

        selectionScreenQueued = true;
        StartCoroutine(ShowLeaderSelectionDeferred());
    }

    void ForceLeaderSelectionRefresh()
    {
        if (!selectionScreenShown && !selectionScreenQueued)
        {
            return;
        }

        if (rootCanvas == null)
        {
            rootCanvas = GetComponentInParent<Canvas>(true);
        }

        if (rootCanvas != null && rootCanvas.gameObject != null && !rootCanvas.gameObject.activeSelf)
        {
            rootCanvas.gameObject.SetActive(true);
        }

        if (rootCanvas != null)
        {
            rootCanvas.enabled = false;
            rootCanvas.enabled = true;
        }

        if (leaderSelectionFullScreen != null)
        {
            if (!leaderSelectionFullScreen.activeSelf)
            {
                leaderSelectionFullScreen.SetActive(true);
            }

            leaderSelectionFullScreen.transform.SetAsLastSibling();

            CanvasGroup group = leaderSelectionFullScreen.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }

            Canvas fullScreenCanvas = leaderSelectionFullScreen.GetComponent<Canvas>();
            if (fullScreenCanvas != null)
            {
                fullScreenCanvas.enabled = false;
                fullScreenCanvas.enabled = true;
            }
        }

        if (leaderCarousel != null)
        {
            if (!leaderCarousel.gameObject.activeSelf)
            {
                leaderCarousel.gameObject.SetActive(true);
            }

            leaderCarousel.gameObject.SetActive(false);
            leaderCarousel.gameObject.SetActive(true);
            leaderCarousel.enabled = false;
            leaderCarousel.enabled = true;
            leaderCarousel.Refresh();
            leaderCarousel.SetIndex(leaderCarousel.GetCurrentIndex());
        }

        ApplyCurrentSkinFont();

        if (textUI != null)
        {
            textUI.ForceMeshUpdate();
        }

        RectTransform fullScreenRect = leaderSelectionFullScreen != null
            ? leaderSelectionFullScreen.transform as RectTransform
            : null;
        if (fullScreenRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(fullScreenRect);
        }

        RectTransform carouselRect = leaderCarousel != null
            ? leaderCarousel.transform as RectTransform
            : null;
        if (carouselRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(carouselRect);
        }

        for (int i = 0; i < carouselItems.Count; i++)
        {
            if (carouselItems[i] == null) continue;
            RectTransform itemRect = carouselItems[i].transform as RectTransform;
            if (itemRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(itemRect);
            }
        }

        Canvas.ForceUpdateCanvases();
    }

    void RequestLeaderSelectionRefresh()
    {
        if (!isActiveAndEnabled) return;
        if (deferredRefreshRoutine != null) return;
        deferredRefreshRoutine = StartCoroutine(DeferredLeaderSelectionRefresh());
    }

    IEnumerator DeferredLeaderSelectionRefresh()
    {
        for (int i = 0; i < 8; i++)
        {
            yield return null;
            ForceLeaderSelectionRefresh();
            yield return new WaitForEndOfFrame();
            ForceLeaderSelectionRefresh();
            yield return new WaitForSecondsRealtime(0.02f);
        }

        deferredRefreshRoutine = null;
    }

    IEnumerator ShowLeaderSelectionDeferred()
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        Board.Instance?.HideGenerationProgressUi();

        if (introVideo != null)
        {
            introVideo.gameObject.SetActive(false);
        }

        if (progress != null)
        {
            progress.SetActive(false);
        }

        if (progressText != null)
        {
            progressText.SetActive(false);
        }

        if (leaderSelectionFullScreen != null)
        {
            leaderSelectionFullScreen.SetActive(false);
            yield return null;
            leaderSelectionFullScreen.SetActive(true);
            leaderSelectionFullScreen.transform.SetAsLastSibling();
        }

        selectionScreenShown = true;
        selectionScreenQueued = false;
        Music.Instance?.PlayLeaderSelectionAmbience();
        ApplyCurrentSkinFont();
        SelectLeader(leaderCarousel != null ? leaderCarousel.GetCurrentIndex() : 0);
        Board.Instance?.HideGenerationProgressUi();
        ForceLeaderSelectionRefresh();
        RequestLeaderSelectionRefresh();
        StartCoroutine(ForceLeaderSelectionVisibleAcrossFrames());
    }

    IEnumerator ForceLeaderSelectionVisibleAcrossFrames()
    {
        for (int i = 0; i < 12; i++)
        {
            Board.Instance?.HideGenerationProgressUi();
            yield return null;
            ForceLeaderSelectionRefresh();
            yield return new WaitForEndOfFrame();
            ForceLeaderSelectionRefresh();
        }
    }

    void AddLeaderOptions(PlayableLeader playableLeader)
    {
        if (playableLeader == null) return;

        LeaderBiomeConfig biome = FindAnyObjectByType<PlayableLeaders>()?.playableLeaders?.biomes?
            .Find(x => x.characterName.ToLower() == playableLeader.characterName.ToLower());
        if (biome == null) return;

        // A scenario leader's hex *is* one specific starting option — a self-owned character card
        // with (if playable) a variant baked in at spawn (see NationSpawner). Siblings sharing its
        // name each got their own instance/hex, so this offers exactly one entry per instance,
        // never the full variant menu. Procedural/campaign leaders aren't hex-bound at all, so they
        // keep the old behaviour: one base entry plus every variant as free choices.
        if (playableLeader.scenarioVariantLocked)
        {
            string variantName = playableLeader.GetSelectedVariantName();
            LeaderVariantConfig variant = biome.variants?.Find(v => v != null && string.Equals(v.displayName, variantName, StringComparison.OrdinalIgnoreCase));
            string displayName = BuildLeaderDisplayName(playableLeader, biome.alignment, variantName);
            string description = NormalizeLeaderDescription(playableLeader.GetSelectedLeaderDescription());
            // The variant's own description fills the variant text panel, exactly like the
            // campaign variant entries below — without it the panel sits empty for scenario picks.
            string variantDescription = NormalizeLeaderDescription(variant?.description);
            string assetName = variant != null ? ResolveVariantAssetName(playableLeader.characterName, variant.displayName, variant.variantId) : null;
            AddSelectionEntry(playableLeader, biome.alignment, displayName, description, variantDescription,
                playableLeader.GetSelectedDeckIdentity(), playableLeader.GetSelectedSubdeckId(), variantName, assetName, variant?.characterName);
            return;
        }

        string baseDescription = NormalizeLeaderDescription(biome.description);
        AddSelectionEntry(playableLeader, biome.alignment, BuildLeaderDisplayName(playableLeader, biome.alignment), baseDescription, string.Empty, biome.deckIdentity, biome.subdeckId);

        foreach (LeaderVariantConfig variant in biome.variants)
        {
            string variantName = GetVariantName(playableLeader.characterName, variant.displayName, variant.variantId);
            string displayName = BuildLeaderDisplayName(playableLeader, biome.alignment, variantName);
            string assetName = ResolveVariantAssetName(playableLeader.characterName, variant.displayName, variant.variantId);
            string variantDescription = NormalizeLeaderDescription(variant.description);
            string subdeckId = string.IsNullOrWhiteSpace(variant.subdeckId) ? biome.subdeckId : variant.subdeckId;
            AddSelectionEntry(playableLeader, biome.alignment, displayName, baseDescription, variantDescription, variant.deckIdentity, subdeckId, variantName, assetName, variant.characterName);
        }
    }

    // Campaign play (no scenario chosen) offers every playable leader found in the scene. An
    // authored scenario restricts the carousel to instances it explicitly placed a starting card
    // for — self-owned leader cards and playable variant cards, both spawned variant-locked in
    // NationSpawner step 1. Leaders spawned implicitly as mere owners of scenario PCs/characters
    // (EnsureLeaderSpawned) are never locked, so they stay out of the carousel.
    bool IsLeaderAllowedForActiveSelection(PlayableLeader playableLeader)
    {
        if (!GameConfig.HasScenario) return true;
        return playableLeader != null && playableLeader.scenarioVariantLocked;
    }

    string BuildLeaderDisplayName(PlayableLeader playableLeader, AlignmentEnum alignment, string variantName = null)
    {
        if (playableLeader == null) return string.Empty;

        string alignmentSprite = GetAlignmentSpriteTag(alignment);
        if (string.IsNullOrWhiteSpace(variantName))
        {
            return $"{alignmentSprite} {playableLeader.characterName}";
        }

        return $"{alignmentSprite} {playableLeader.characterName}<br>({variantName})";
    }

    string GetAlignmentSpriteTag(AlignmentEnum alignment)
    {
        string spriteName = alignment switch
        {
            AlignmentEnum.freePeople => "freePeople",
            AlignmentEnum.darkServants => "darkServants",
            _ => "neutral"
        };

        return $"<sprite name=\"{spriteName}\">";
    }

    string GetVariantName(string baseLeaderName, string displayName, string variantId)
    {
        string suffix = displayName ?? string.Empty;

        suffix = suffix.Replace("_", " ");
        List<char> chars = new();
        for (int i = 0; i < suffix.Length; i++)
        {
            char c = suffix[i];
            if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(suffix[i - 1]) && !char.IsUpper(suffix[i - 1]))
            {
                chars.Add(' ');
            }
            chars.Add(c);
        }

        string formatted = new string(chars.ToArray()).Trim().ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(formatted);
    }

    string ResolveVariantAssetName(string baseLeaderName, string displayName, string variantId)
    {
        if (string.IsNullOrWhiteSpace(baseLeaderName))
        {
            return displayName;
        }

        Illustrations illustrations = FindFirstObjectByType<Illustrations>();
        string[] candidates =
        {
            $"{baseLeaderName} {displayName}",
            $"{baseLeaderName} {variantId}",
            displayName,
            variantId
        };

        HashSet<string> seen = new();
        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !seen.Add(Normalize(candidate)))
            {
                continue;
            }

            if (illustrations == null || illustrations.TryGetIllustrationByName(candidate, out _))
            {
                return candidate;
            }
        }

        return $"{baseLeaderName} {displayName}";
    }

    void AddSelectionEntry(PlayableLeader playableLeader, AlignmentEnum alignment, string displayName, string baseDescription, string variantDescription, string deckIdentity, string subdeckId, string variantName = null, string assetName = null, string variantCharacterName = null)
    {
        Illustrations illustrations = FindFirstObjectByType<Illustrations>();
        Sprite carouselSprite = illustrations != null ? illustrations.GetIllustrationByName(assetName) ?? illustrations.GetIllustrationByName(playableLeader.characterName) : null;

        LeaderSelectionEntry selectionEntry = new()
        {
            leaderInstance = playableLeader,
            baseLeaderName = playableLeader.characterName,
            characterName = string.IsNullOrWhiteSpace(variantCharacterName) ? playableLeader.characterName : variantCharacterName,
            alignment = alignment,
            displayName = displayName,
            variantName = variantName,
            assetName = assetName,
            baseDescription = baseDescription,
            variantDescription = variantDescription,
            deckIdentity = deckIdentity,
            subdeckId = subdeckId
        };
        selectionEntries.Add(selectionEntry);
        CreateCarouselItem(selectionEntry, carouselSprite);
        RequestLeaderSelectionRefresh();
    }

    string BuildLeaderText(LeaderSelectionEntry selection)
    {
        if (selection == null) return string.Empty;

        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(selection.baseDescription))
        {
            parts.Add(selection.baseDescription);
        }
        if (!string.IsNullOrWhiteSpace(selection.variantDescription))
        {
            parts.Add(selection.variantDescription);
        }
        if (!string.IsNullOrWhiteSpace(selection.deckIdentity))
        {
            parts.Add(selection.deckIdentity);
        }

        return string.Join("\n\n", parts);
    }

    void CreateCarouselItem(LeaderSelectionEntry selection, Sprite sprite)
    {
        if (leaderCarousel == null || carouselItemPrefab == null || selection == null)
        {
            return;
        }

        GameObject item = Instantiate(carouselItemPrefab, leaderCarousel.transform);
        item.name = $"{selection.displayName} Carousel Item";
        carouselItems.Add(item);
        PopulateCarouselItem(item, selection, sprite);
        leaderCarousel.AddItem(item);
    }

    void PopulateCarouselItem(GameObject item, LeaderSelectionEntry selection, Sprite sprite)
    {
        if (item == null || selection == null)
        {
            return;
        }

        CarouselItem carouselItem = item.GetComponent<CarouselItem>();
        if (carouselItem != null)
        {
            carouselItem.SetSprite(sprite);
            carouselItem.SetLabel(BuildCarouselLabel(selection), selection.alignment);
            carouselItem.SetClickHandler(() => HandleCarouselItemClick(item));
        }

        ApplyCurrentSkinFont(item);
    }

    void ConfigureStartGameButton()
    {
        if (startGameButton == null)
        {
            startGameButton = GetComponentsInChildren<Button>(true)
                .FirstOrDefault(button => button.name == "StartGame");
        }

        if (startGameButton == null)
        {
            Debug.LogError("Leader selection is missing its StartGame button.");
            return;
        }

        // Replace the old serialized actions, which started the game and hid this screen
        // immediately. Both entry points must now wait for the same confirmation dialog.
        startGameButton.onClick = new Button.ButtonClickedEvent();
        startGameButton.onClick.AddListener(ConfirmStartGame);
    }

    void HandleCarouselItemClick(GameObject clickedItem)
    {
        if (leaderCarousel == null || clickedItem == null || startConfirmationPending)
        {
            return;
        }

        int clickedIndex = carouselItems.IndexOf(clickedItem);
        if (clickedIndex < 0)
        {
            return;
        }

        if (clickedIndex != leaderCarousel.GetCurrentIndex())
        {
            leaderCarousel.SetIndex(clickedIndex);
            return;
        }

        ConfirmStartGame();
    }

    public async void ConfirmStartGame()
    {
        if (startConfirmationPending || selectionEntries.Count == 0)
        {
            return;
        }

        int selectedIndex = leaderCarousel != null ? leaderCarousel.GetCurrentIndex() : 0;
        if (selectedIndex < 0 || selectedIndex >= selectionEntries.Count)
        {
            return;
        }

        LeaderSelectionEntry selection = selectionEntries[selectedIndex];
        string selectedName = string.IsNullOrWhiteSpace(selection.variantName)
            ? selection.baseLeaderName
            : $"{selection.baseLeaderName} ({selection.variantName})";

        startConfirmationPending = true;
        bool confirmed;
        try
        {
            Sprite portrait = selectedIndex < carouselItems.Count && carouselItems[selectedIndex] != null
                ? carouselItems[selectedIndex].GetComponent<CarouselItem>()?.image?.sprite : null;
            confirmed = await ConfirmationDialog.AskYesNo($"Do you want to start as {selectedName}?", image: portrait);
        }
        finally
        {
            startConfirmationPending = false;
        }

        if (!confirmed || this == null || Game.Instance == null)
        {
            return;
        }

        if (leaderCarousel != null)
        {
            leaderCarousel.SetIndex(selectedIndex);
        }
        else
        {
            SelectLeader(selectedIndex);
        }

        Game.Instance.StartGame();
    }

    string BuildCarouselLabel(LeaderSelectionEntry selection)
    {
        if (selection == null) return string.Empty;

        string alignmentSprite = GetAlignmentSpriteTag(selection.alignment);
        if (string.IsNullOrWhiteSpace(selection.variantName))
        {
            return $"{alignmentSprite} {selection.baseLeaderName}";
        }

        return $"{alignmentSprite} {selection.baseLeaderName}<br><size=75%><color=#BEB6A2>{selection.variantName}</color></size>";
    }

    public void SelectLeader(int value)
    {
        if (selectionEntries.Count > value)
        {
            LeaderSelectionEntry selection = selectionEntries[value];
            string leaderText = BuildLeaderText(selection);
            ApplyLeaderTexts(selection);

            // Resolve to the exact instance this entry was built from — never re-look-up by name,
            // which would pick an arbitrary sibling when a scenario spawns several instances of the
            // same leader (one per variant/hex).
            PlayableLeader player = selection.leaderInstance;
            if (player != null)
            {
                player.SetDeckSelection(selection.subdeckId, selection.deckIdentity, leaderText, selection.variantName, selection.characterName);
            }

            UpdateBannerImage(player);
            Game.Instance.SelectPlayer(player);

            if (selectionScreenShown && selection != lastVoicedSelection)
            {
                string voiceName = string.IsNullOrWhiteSpace(selection.characterName)
                    ? selection.baseLeaderName
                    : selection.characterName;
                bool played = Sounds.Instance?.PlayNamedVoiceExpression(voiceName, true) ?? false;
                if (!played && !string.Equals(voiceName, selection.baseLeaderName, StringComparison.OrdinalIgnoreCase))
                {
                    played = Sounds.Instance?.PlayNamedVoiceExpression(selection.baseLeaderName, true) ?? false;
                }
                if (played) lastVoicedSelection = selection;
            }
        }
    }

    void ApplyCurrentSkinFont(GameObject root = null)
    {
        FontManager fontManager = FontManager.Instance;
        if (fontManager == null) return;

        GameObject target = root != null ? root : leaderSelectionFullScreen;
        if (target == null) target = gameObject;
        foreach (TMP_Text text in target.GetComponentsInChildren<TMP_Text>(true))
        {
            fontManager.ApplyCurrentFont(text);
        }
    }

    private void UpdateBannerImage(PlayableLeader player)
    {
        if (bannerImage == null)
        {
            return;
        }

        Illustrations illustrations = FindFirstObjectByType<Illustrations>();
        Sprite bannerSprite = ResolveBannerSprite(player, illustrations);
        bannerImage.sprite = bannerSprite;
        bannerImage.enabled = bannerSprite != null;
    }

    private Sprite ResolveBannerSprite(Leader owner, Illustrations illustrations)
    {
        string bannerName = ResolveBannerName(owner);
        if (string.IsNullOrWhiteSpace(bannerName) || illustrations == null)
        {
            return null;
        }

        return illustrations.GetIllustrationByName(bannerName, false);
    }

    private static string ResolveBannerName(Leader owner)
    {
        if (owner == null)
        {
            return null;
        }

        LeaderBiomeConfig biome = owner.GetBiome();
        if (biome == null)
        {
            return null;
        }

        if (owner is PlayableLeader playableLeader)
        {
            string selectedSubdeckId = playableLeader.GetSelectedSubdeckId();
            if (!string.IsNullOrWhiteSpace(selectedSubdeckId) && biome.variants != null)
            {
                LeaderVariantConfig variant = biome.variants.Find(entry =>
                    entry != null
                    && ((!string.IsNullOrWhiteSpace(entry.variantId) && string.Equals(entry.variantId, selectedSubdeckId, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(entry.subdeckId) && string.Equals(entry.subdeckId, selectedSubdeckId, StringComparison.OrdinalIgnoreCase))));

                if (!string.IsNullOrWhiteSpace(variant?.banner))
                {
                    return variant.banner;
                }
            }
        }

        return biome.banner;
    }

    void ApplyLeaderTexts(LeaderSelectionEntry selection)
    {
        if (selection == null)
        {
            if (typewriterEffect) typewriterEffect.StartWriting(string.Empty);
            else if (textUI != null) textUI.text = string.Empty;
            if (variantTextUI != null) variantTextUI.text = string.Empty;
            if (deckTextUI != null) deckTextUI.text = string.Empty;
            return;
        }

        string mainText = selection.baseDescription ?? string.Empty;
        string variantText = selection.variantDescription ?? string.Empty;

        if (string.IsNullOrWhiteSpace(selection.variantName) && string.IsNullOrWhiteSpace(variantText))
        {
            SplitBaseDescription(mainText, out string topHalf, out string bottomHalf);
            mainText = topHalf;
            variantText = bottomHalf;
        }

        if (typewriterEffect) typewriterEffect.StartWriting(mainText);
        else if (textUI != null) textUI.text = mainText;

        if (variantTextUI != null)
        {
            variantTextUI.text = variantText;
        }

        if (deckTextUI != null)
        {
            deckTextUI.text = selection.deckIdentity ?? string.Empty;
        }
    }

    string NormalizeLeaderDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;
        return description.Replace("<br><br>", "\n\n").Replace("<br>", "\n").Trim();
    }

    void SplitBaseDescription(string description, out string firstHalf, out string secondHalf)
    {
        firstHalf = description ?? string.Empty;
        secondHalf = string.Empty;

        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        string[] paragraphs = description
            .Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (paragraphs.Length >= 2)
        {
            int splitIndex = Mathf.CeilToInt(paragraphs.Length / 2f);
            firstHalf = string.Join("\n\n", paragraphs.Take(splitIndex));
            secondHalf = string.Join("\n\n", paragraphs.Skip(splitIndex));
            return;
        }

        string[] sentences = description
            .Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (sentences.Length >= 2)
        {
            int splitIndex = Mathf.CeilToInt(sentences.Length / 2f);
            firstHalf = string.Join(". ", sentences.Take(splitIndex)).Trim();
            secondHalf = string.Join(". ", sentences.Skip(splitIndex)).Trim();
            if (!string.IsNullOrWhiteSpace(firstHalf) && !firstHalf.EndsWith(".") && !firstHalf.EndsWith("!") && !firstHalf.EndsWith("?")) firstHalf += ".";
            if (!string.IsNullOrWhiteSpace(secondHalf) && !secondHalf.EndsWith(".") && !secondHalf.EndsWith("!") && !secondHalf.EndsWith("?")) secondHalf += ".";
            return;
        }

        string[] words = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
        {
            secondHalf = string.Empty;
            return;
        }

        int wordSplitIndex = Mathf.CeilToInt(words.Length / 2f);
        firstHalf = string.Join(" ", words.Take(wordSplitIndex));
        secondHalf = string.Join(" ", words.Skip(wordSplitIndex));
    }

    public void ApplyCurrentSelection()
    {
        if (leaderCarousel == null)
        {
            SelectLeader(0);
            return;
        }

        SelectLeader(leaderCarousel.GetCurrentIndex());
    }
}
