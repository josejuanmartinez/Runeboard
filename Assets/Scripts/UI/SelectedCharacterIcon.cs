using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Image))]
[RequireComponent(typeof(CanvasGroup))]
public class SelectedCharacterIcon : MonoBehaviour
{
    [Header("Game Objects")]
    public GameObject levelsGameObject;
    public GameObject moved;
    public GameObject actioned;
    public GameObject unactionedIcon;
    public GameObject actionedIcon;
    public GameObject border;
    public GameObject otherCharacters;

    [Header("Images")]
    public Image cards;

    [Header("Banner")]
    public Image bannerImage;

    [Header("Leader")]
    public Image icon;
    public TextMeshProUGUI nameWidget;
    public TextMeshProUGUI descriptionWidget;
    public Image animatedCharacter;

    [Header("Health")]
    public Image health;

    [Header("Levels")]
    public TextMeshProUGUI commander;
    public TextMeshProUGUI agent;
    public TextMeshProUGUI emmissary; 
    public TextMeshProUGUI mage;
    public TextMeshProUGUI movementLeft;

    [Header("Artifact-Status Items")]
    [FormerlySerializedAs("artifactPrefab")]
    public GameObject artifactStatusPrefab;
    [FormerlySerializedAs("artifactsGridLayoutTransform")]
    public Transform artifactStatusGridLayoutTransform;

    [Header("Card Bloom Hint")]
    [Tooltip("Idle pulse range (0-255 alpha) of the concentric circles, inviting the mouse over.")]
    [Range(0, 255)][SerializeField] private int circlesPulseAlphaMin = 56;
    [Range(0, 255)][SerializeField] private int circlesPulseAlphaMax = 78;
    [Tooltip("Alpha (0-255) the circles hold while the mouse is over the icon / the bloom is open.")]
    [Range(0, 255)][SerializeField] private int circlesHoverAlpha = 255;
    [Tooltip("Idle pulse cycles per second.")]
    [SerializeField] private float circlesPulseSpeed = 0.8f;
    [Tooltip("Screen-pixel offset of the bloom loading icon from the cursor tip.")]
    [SerializeField] private Vector2 loadingIconCursorOffset = new(26f, -26f);
    [SerializeField] private float loadingIconSize = 30f;

    // private Videos videos;
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

    private Illustrations illustrations;
    private CanvasGroup canvasGroup;
    private Material bannerMaterialInstance;

    // Card-bloom affordance state (see UpdateBloomHint).
    private CardBloomWheel bloomWheel;
    private Canvas hintCanvas;
    private RectTransform loadingIconRect;
    private Image loadingIconImage;
    private bool clickableCursorSet;

    public Character CurrentCharacter { get; private set; }
    private readonly List<ArtifactRenderer> artifactStatusRenderers = new();
    private string hoveredPreviewCardName;

    // Whose hex sprite animatedCharacter mirrors — separate from CurrentCharacter because
    // RefreshForArmy shows an army's commander without setting CurrentCharacter.
    private Character animatedSourceCharacter;

    private void Awake()
    {
        if (icon != null && icon.GetComponent<CharacterShineEffect>() == null)
            icon.gameObject.AddComponent<CharacterShineEffect>();
    }

    private void OnDisable()
    {
        SetLoadingIconVisible(false);
        SetClickableCursor(false);
        HideCardHoverPreview();
        StopAllCoroutines();
    }

    private void Update()
    {
        UpdateBloomHint();
        UpdateCardHoverPreview();
        UpdateAnimatedCharacterSprite();
    }

    // Mirrors whatever frame CharacterAnimationController is currently drawing on the
    // character's own hex sprite (same sprite reference, no separate animation state to
    // keep in sync), so this panel's portrait always matches the board exactly.
    private void UpdateAnimatedCharacterSprite()
    {
        if (animatedCharacter == null) return;

        SpriteRenderer source = animatedSourceCharacter != null && animatedSourceCharacter.hex != null
            ? animatedSourceCharacter.hex.characterSpriteRenderer
            : null;
        Sprite sprite = source != null ? source.sprite : null;

        animatedCharacter.enabled = sprite != null;
        if (sprite != null)
        {
            animatedCharacter.sprite = sprite;
            animatedCharacter.color = Color.white;
        }
    }

    private void UpdateCardHoverPreview()
    {
        if (CurrentCharacter == null || nameWidget == null || descriptionWidget == null) return;
        if (CardCenterPreview.Instance == null) return;

        string cardName = null;
        if (RectTransformUtility.RectangleContainsScreenPoint(
            nameWidget.rectTransform, Input.mousePosition, ResolveTriggerCamera(nameWidget.rectTransform)))
        {
            cardName = CurrentCharacter.characterName;
        }
        else
        {
            Canvas canvas = descriptionWidget.canvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(descriptionWidget, Input.mousePosition, camera);
            if (linkIndex >= 0)
            {
                string linkId = descriptionWidget.textInfo.linkInfo[linkIndex].GetLinkID();
                const string armyPrefix = "army:";
                if (linkId.StartsWith(armyPrefix, System.StringComparison.Ordinal))
                    cardName = linkId.Substring(armyPrefix.Length);
            }
        }

        if (string.Equals(cardName, hoveredPreviewCardName, System.StringComparison.OrdinalIgnoreCase)) return;
        HideCardHoverPreview();
        if (string.IsNullOrWhiteSpace(cardName)) return;

        DeckManager deckManager = DeckManager.Instance;
        CardData card = string.Equals(cardName, CurrentCharacter.characterName, System.StringComparison.OrdinalIgnoreCase)
            ? deckManager?.FindAnyCardByName(cardName)
            : deckManager?.FindArmyCardByName(cardName);
        if (card == null) return;

        hoveredPreviewCardName = cardName;
        CardCenterPreview.Instance.ShowPreview(card, hoverDriven: true);
    }

    private void HideCardHoverPreview()
    {
        hoveredPreviewCardName = null;
        CardCenterPreview.Instance?.HidePreview();
    }

    // Card-bloom affordance: the concentric circles pulse softly while the icon is idle
    // (inviting the mouse over), hold full alpha under the mouse, the cursor turns clickable,
    // and a radial loading icon rides the cursor during the bloom's hover-dwell delay so the
    // wait reads as progress instead of a dead UI. Once the bloom opens the loader hides and
    // only the clickable cursor remains.
    private void UpdateBloomHint()
    {
        if (cards == null) return;
        if (bloomWheel == null) bloomWheel = FindFirstObjectByType<CardBloomWheel>();

        // Only the icon instance the wheel actually watches gets the affordance — hover
        // preview clones of this component must never pulse or steal the cursor.
        RectTransform trigger = bloomWheel != null ? bloomWheel.HoverTriggerRect : null;
        bool isBloomTrigger = trigger != null && trigger.IsChildOf(transform);

        bool available = isBloomTrigger
            && CurrentCharacter != null
            && !CurrentCharacter.hasActionedThisTurn
            && canvasGroup != null && canvasGroup.alpha > 0.5f;

        if (!available)
        {
            SetCirclesAlpha(circlesPulseAlphaMin);
            SetLoadingIconVisible(false);
            SetClickableCursor(false);
            return;
        }

        bool mouseOver = RectTransformUtility.RectangleContainsScreenPoint(
            trigger, Input.mousePosition, ResolveTriggerCamera(trigger));
        bool bloomOpen = bloomWheel.IsOpen;

        if (mouseOver || bloomOpen)
        {
            SetCirclesAlpha(circlesHoverAlpha);
            SetClickableCursor(true);

            // The loader only bridges the dwell time between entering the icon and the
            // bloom appearing; the open bloom itself is the feedback afterwards.
            bool loading = mouseOver && !bloomOpen;
            SetLoadingIconVisible(loading);
            if (loading) UpdateLoadingIcon(bloomWheel.HoverProgress);
        }
        else
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * circlesPulseSpeed * 2f * Mathf.PI);
            SetCirclesAlpha(Mathf.Lerp(circlesPulseAlphaMin, circlesPulseAlphaMax, pulse));
            SetLoadingIconVisible(false);
            SetClickableCursor(false);
        }
    }

    private void SetCirclesAlpha(float alpha255)
    {
        Color color = cards.color;
        color.a = alpha255 / 255f;
        cards.color = color;
    }

    private void SetClickableCursor(bool clickable)
    {
        if (clickable == clickableCursorSet) return;
        clickableCursorSet = clickable;
        if (CursorManager.Instance == null) return;
        if (clickable) CursorManager.Instance.SetClickableCursor();
        else CursorManager.Instance.SetDefaultCursor();
    }

    private static Camera ResolveTriggerCamera(RectTransform trigger)
    {
        Canvas canvas = trigger.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return canvas.worldCamera;
    }

    // Small radial-fill copy of the concentric-circles art that follows the cursor and fills
    // up over the bloom's hover-dwell window. Built lazily on the icon's root canvas so it
    // draws above everything.
    private void EnsureLoadingIcon()
    {
        if (loadingIconRect != null) return;
        if (hintCanvas == null)
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null) hintCanvas = parentCanvas.rootCanvas;
        }
        if (hintCanvas == null) return;

        GameObject go = new("BloomLoadingIcon", typeof(RectTransform));
        go.transform.SetParent(hintCanvas.transform, false);
        loadingIconRect = go.GetComponent<RectTransform>();
        loadingIconRect.sizeDelta = Vector2.one * loadingIconSize;

        loadingIconImage = go.AddComponent<Image>();
        loadingIconImage.sprite = cards != null ? cards.sprite : null;
        loadingIconImage.raycastTarget = false;
        loadingIconImage.type = Image.Type.Filled;
        loadingIconImage.fillMethod = Image.FillMethod.Radial360;
        loadingIconImage.fillOrigin = (int)Image.Origin360.Top;
        loadingIconImage.fillClockwise = true;
        go.SetActive(false);
    }

    private void SetLoadingIconVisible(bool visible)
    {
        if (visible) EnsureLoadingIcon();
        if (loadingIconRect == null) return;
        if (loadingIconRect.gameObject.activeSelf != visible)
            loadingIconRect.gameObject.SetActive(visible);
        if (visible) loadingIconRect.SetAsLastSibling();
    }

    private void UpdateLoadingIcon(float progress)
    {
        if (loadingIconRect == null || hintCanvas == null) return;

        Camera canvasCamera = hintCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hintCanvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                hintCanvas.transform as RectTransform,
                (Vector2)Input.mousePosition + loadingIconCursorOffset,
                canvasCamera, out Vector2 local))
        {
            loadingIconRect.localPosition = local;
        }

        if (loadingIconImage != null) loadingIconImage.fillAmount = Mathf.Clamp01(progress);
    }

    // Applied immediately (not deferred a frame) so it can never lose a race against the
    // other, already-synchronous entry points below (Hide/RefreshHoverPreview) — a deferred
    // restore that gets clobbered by an immediate Hide() before it runs was the cause of the
    // panel getting stuck blank after hovering off an already-selected character.
    public void Refresh(Character c)
    {
        if (c == null) { Hide(); return; }
        ApplyRefresh(c, isHover: false);
    }

    public void RefreshForHover(Character c)
    {
        if (c == null) { Hide(); return; }
        ApplyRefresh(c, isHover: true);
    }

    private void ApplyRefresh(Character c, bool isHover = false)
    {
        CurrentCharacter = c;
        animatedSourceCharacter = c;
        SetVisible(true);
        border.SetActive(true);
        SetBannerImage(c);
        SetCardsImage(c.GetOwner());
        SetCharacterVisuals(GetIllustrationByName(!string.IsNullOrWhiteSpace(c.illustrationName) ? c.illustrationName : c.characterName));
        string nameText = BuildSelectedCharacterTitle(c, true, false);
        string armyText = BuildSelectedCharacterTitle(c, false, true);
        string quoteText = BuildSelectedCharacterTitle(c, false, false, true);
        string descriptionText = string.IsNullOrWhiteSpace(quoteText)
            ? armyText
            : $"{quoteText}\n\n{armyText}";
        string kidnappingText = BuildKidnappingStatusText(c);
        nameWidget.text = nameText;
        descriptionWidget.text = $"<mark=#000000bb>{kidnappingText}\n{descriptionText}</mark>";
        levelsGameObject.SetActive(true);
        actioned.SetActive(true);
        moved.SetActive(true);
        commander.text = c.GetCommander().ToString();
        agent.text = c.GetAgent().ToString();
        emmissary.text = c.GetEmmissary().ToString();
        mage.text = c.GetMage().ToString();
        actionedIcon.SetActive(c.hasActionedThisTurn);
        unactionedIcon.SetActive(!actionedIcon.activeSelf);
        health.gameObject.SetActive(true);
        health.fillAmount = c.health / 100f;

        RefreshArtifactStatusItems(c.objects, c);

        RefreshMovementLeft(c);
        if (!isHover) RefreshOtherCharacters(c);
    }

    private string BuildKidnappingStatusText(Character c)
    {
        if (c == null) return string.Empty;

        List<string> parts = new();
        int captiveCount = c.kidnappedCharacters != null ? c.kidnappedCharacters.Count(x => x != null && x.character != null && !x.character.killed) : 0;
        if (captiveCount > 0)
        {
            parts.Add($"Captives: {captiveCount}");
        }

        if (c.IsKidnapped())
        {
            string kidnapperName = c.kidnappedBy != null ? c.kidnappedBy.characterName : "Unknown";
            parts.Add($"Captured by: {kidnapperName}");
        }

        return string.Join(" | ", parts);
    }

    public void RefreshHoverPreview(Character c, string hoverText, bool showHealth, bool showArtifacts)
    {
        if (c == null)
        {
            Hide();
            return;
        }

        CurrentCharacter = c;
        animatedSourceCharacter = c;
        SetVisible(true);
        border.SetActive(true);
        SetBannerImage(c);
        SetCardsImage(c.GetOwner());
        SetCharacterVisuals(GetIllustrationByName(!string.IsNullOrWhiteSpace(c.illustrationName) ? c.illustrationName : c.characterName));
        nameWidget.text = BuildSelectedCharacterTitle(c, returnName: true, returnArmy: false);
        string detailText = hoverText ?? string.Empty;
        if (detailText.StartsWith(c.characterName, System.StringComparison.OrdinalIgnoreCase))
            detailText = detailText.Substring(c.characterName.Length).TrimStart();

        // Match the selected-character panel's flavor content: quote is harmless public
        // flavor text so it always shows, same as when selected. Army/"(wandering)" status
        // can reveal troop composition, so — same as health/artifacts below — it's gated
        // behind showArtifacts (both parameters are driven by the same isScouted check at
        // the call sites, so an unscouted enemy still won't leak that here either).
        string quoteText = BuildSelectedCharacterTitle(c, returnName: false, returnArmy: false, returnQuote: true);
        string armyText = showArtifacts ? BuildSelectedCharacterTitle(c, returnName: false, returnArmy: true) : string.Empty;
        List<string> descriptionParts = new();
        if (!string.IsNullOrWhiteSpace(quoteText)) descriptionParts.Add(quoteText);
        if (!string.IsNullOrWhiteSpace(armyText)) descriptionParts.Add(armyText);
        if (!string.IsNullOrWhiteSpace(detailText)) descriptionParts.Add(detailText);
        descriptionWidget.text = $"<mark=#000000bb>{string.Join("\n\n", descriptionParts)}</mark>";

        actioned.SetActive(false);
        moved.SetActive(false);
        actionedIcon.SetActive(false);
        unactionedIcon.SetActive(false);

        commander.text = c.GetCommander().ToString();
        agent.text = c.GetAgent().ToString();
        emmissary.text = c.GetEmmissary().ToString();
        mage.text = c.GetMage().ToString();
        movementLeft.text = "-";

        health.gameObject.SetActive(showHealth);
        if (showHealth) health.fillAmount = c.health / 100f;

        RefreshArtifactStatusItems(showArtifacts ? c.objects : null, showArtifacts ? c : null);
    }


    public void RefreshForArmy(Army army)
    {
        if (army == null || army.commander == null) { Hide(); return; }

        animatedSourceCharacter = army.commander;
        SetVisible(true);
        border.SetActive(true);
        SetCardsImage(army.commander.GetOwner());
        SetCharacterVisuals(ResolveArmySprite(army));
        nameWidget.text = army.GetHoverTextNoXp();

        actioned.SetActive(false);
        moved.SetActive(false);
        actionedIcon.SetActive(false);
        unactionedIcon.SetActive(false);

        commander.text = army.GetSize().ToString();
        agent.text = "-";
        emmissary.text = "-";
        mage.text = "-";
        movementLeft.text = "-";

        health.gameObject.SetActive(false);
        RefreshArtifactStatusItems(null, null);
    }

    private Sprite ResolveArmySprite(Army army)
    {
        string troopName = army.GetTroopGroups().FirstOrDefault()?.troopName;
        if (!string.IsNullOrWhiteSpace(troopName))
        {
            DeckManager deckManager = DeckManager.Instance;
            if (deckManager != null)
            {
                CardData card = deckManager.cards.FirstOrDefault(c =>
                    string.Equals(c.name, troopName, System.StringComparison.OrdinalIgnoreCase));
                if (card != null)
                {
                    foreach (string candidate in new[] { card.spriteName, card.portraitName, card.name })
                    {
                        if (string.IsNullOrWhiteSpace(candidate)) continue;
                        Sprite s = GetIllustrationByName(candidate, false);
                        if (s != null) return s;
                    }
                }
            }
        }
        return null;
    }

    // Update is called once per frame
    public void Hide()
    {
        SetVisible(false);
        border.SetActive(false);
        Image targetImage = GetImageTarget();
        if (targetImage != null) targetImage.enabled = false;
        nameWidget.text = "";
        levelsGameObject.SetActive(false);
        actioned.SetActive(false);
        moved.SetActive(false);
        health.gameObject.SetActive(false);
        CurrentCharacter = null;
        animatedSourceCharacter = null;
        if (animatedCharacter != null) animatedCharacter.enabled = false;
        RefreshOtherCharacters(null);
    }

    public void RefreshMovementLeft(Character c)
    {
        movementLeft.text = c.GetMovementLeft().ToString();
    }

    private void SetBannerImage(Character c)
    {
        if (bannerImage == null) return;
        Leader owner = c?.GetOwner();
        Sprite sprite = ResolveBannerSprite(owner);
        bannerImage.sprite = sprite;
        bannerImage.enabled = sprite != null;
        ApplyBannerOutlineColor(owner);
    }

    // The BannerOutline material (Sprites/Outline shader) is a shared asset — other instances
    // of this component (e.g. CharacterSpriteHover's hover-preview clone) reference the same
    // material, so mutating it directly would leak one character's nation color onto every
    // banner using it. A lazily-created per-instance copy keeps this icon's outline isolated.
    private void ApplyBannerOutlineColor(Leader owner)
    {
        if (bannerImage == null || bannerImage.material == null) return;
        if (bannerMaterialInstance == null)
        {
            bannerMaterialInstance = new Material(bannerImage.material);
            bannerImage.material = bannerMaterialInstance;
        }
        bannerMaterialInstance.SetColor(OutlineColorId, owner != null ? owner.nationColor : Color.white);
    }

    // Swaps the pulsing card-stack icon to the leader's own deck art (e.g. Mithrandir's grey
    // star, the Necromancer's skull) instead of always showing the same generic stack.
    private void SetCardsImage(Leader owner)
    {
        if (cards == null) return;
        Sprite sprite = ResolveCardsSprite(owner);
        if (sprite != null) cards.sprite = sprite;
    }

    private Sprite ResolveCardsSprite(Leader owner)
    {
        if (owner == null) return null;
        string subdeckId = owner is PlayableLeader playableLeader
            ? playableLeader.GetSelectedSubdeckId()
            : owner.GetBiome()?.subdeckId;
        if (string.IsNullOrWhiteSpace(subdeckId)) return null;
        if (illustrations == null) illustrations = FindFirstObjectByType<Illustrations>();
        return illustrations != null ? illustrations.GetDeckArtByName(subdeckId, false) : null;
    }

    private Sprite ResolveBannerSprite(Leader owner)
    {
        if (owner == null) return null;
        LeaderBiomeConfig biome = owner.GetBiome();
        if (biome == null) return null;

        string bannerName = null;
        if (owner is PlayableLeader playableLeader)
        {
            string subdeckId = playableLeader.GetSelectedSubdeckId();
            if (!string.IsNullOrWhiteSpace(subdeckId) && biome.variants != null)
            {
                LeaderVariantConfig variant = biome.variants.Find(v =>
                    v != null
                    && ((!string.IsNullOrWhiteSpace(v.variantId) && string.Equals(v.variantId, subdeckId, System.StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(v.subdeckId) && string.Equals(v.subdeckId, subdeckId, System.StringComparison.OrdinalIgnoreCase))));
                if (!string.IsNullOrWhiteSpace(variant?.banner))
                    bannerName = variant.banner;
            }
        }

        if (string.IsNullOrWhiteSpace(bannerName))
            bannerName = biome.banner;

        if (string.IsNullOrWhiteSpace(bannerName)) return null;
        return GetIllustrationByName(bannerName, false);
    }

    private Sprite GetIllustrationByName(string name)
    {
        if (illustrations == null) illustrations = FindFirstObjectByType<Illustrations>();
        return illustrations != null ? illustrations.GetIllustrationByName(name) : null;
    }

    private Sprite GetIllustrationByName(string name, bool logMissing)
    {
        if (illustrations == null) illustrations = FindFirstObjectByType<Illustrations>();
        return illustrations != null ? illustrations.GetIllustrationByName(name, logMissing) : null;
    }

    // private VideoClip GetVideoByName(string name)
    // {
    //     if (videos == null) videos = FindFirstObjectByType<Videos>();
    //     return videos != null ? videos.GetVideoByName(name) : null;
    // }

    private Image GetImageTarget()
    {
        return icon;
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        Image rootImage = GetComponent<Image>();
        if (rootImage != null) rootImage.enabled = visible;
    }

    private void SetCharacterVisuals(Sprite fallbackSprite)
    {
        Image targetImage = GetImageTarget();
        if (targetImage != null)
        {
            targetImage.enabled = true;
            targetImage.sprite = fallbackSprite;
        }
    }

    private void RefreshOtherCharacters(Character c)
    {
        if (otherCharacters == null) return;
        if (!otherCharacters.TryGetComponent(out CharacterIcons icons)) return;
        PlayableLeader owner = c != null ? c.GetOwner() as PlayableLeader : null;
        if (owner == null)
        {
            Game game = Game.Instance;
            owner = game != null ? game.player : null;
        }
        if (owner == null) { icons.Clear(); return; }
        if (c != null)
            icons.BuildIconsForPlayerExcluding(owner, c);
        else
            icons.BuildIconsForPlayer(owner);
    }

    private void RefreshArtifactStatusItems(List<CardData> artifacts, Character c)
    {
        var items = new List<(string spriteName, string label)>();

        if (artifacts != null)
            foreach (CardData a in artifacts)
                if (a != null) items.Add((a.GetSpriteString(), a.GetHoverText()));

        if (c?.statusEffects != null)
            foreach (StatusEffectEnum effect in c.statusEffects)
            {
                string spriteName = effect.ToString().ToLowerInvariant();
                if (GetIllustrationByName(spriteName, false) != null)
                    items.Add((spriteName, effect.ToString()));
            }

        // Board-wide active environmental card (see EnvironmentalCardManager) — shown here only
        // when this character isn't exempt from it, using the same immunity check the card
        // scripts themselves use to skip protected characters (e.g. artifact-granted immunity).
        CardData activeEnvironmentalCard = EnvironmentalCardManager.Instance?.ActiveCard;
        if (activeEnvironmentalCard != null && c != null && !c.IsImmuneToNegativeEnvironmentalCards())
        {
            // Environmental cards carry their mechanical effect in actionEffect, not
            // description (which is typically blank for them) — GetDescriptionBody() is the
            // same resolver the card's own face/tooltip uses, so this stays in sync with it.
            string effectText = activeEnvironmentalCard.GetDescriptionBody();
            string hover = string.IsNullOrWhiteSpace(effectText)
                ? activeEnvironmentalCard.name
                : $"{activeEnvironmentalCard.name}: {effectText}";
            items.Add((activeEnvironmentalCard.GetSpriteString(), hover));
        }

        for (int i = artifactStatusRenderers.Count; i < items.Count; i++)
        {
            GameObject go = Instantiate(artifactStatusPrefab, artifactStatusGridLayoutTransform);
            artifactStatusRenderers.Add(go.GetComponent<ArtifactRenderer>());
        }

        for (int i = 0; i < artifactStatusRenderers.Count; i++)
        {
            ArtifactRenderer renderer = artifactStatusRenderers[i];
            if (renderer == null) continue;
            bool active = i < items.Count;
            renderer.gameObject.SetActive(active);
            if (!active) continue;
            renderer.gameObject.name = items[i].label;
            renderer.Initialize(items[i].spriteName, items[i].label);
        }
    }

    private string BuildSelectedCharacterTitle(Character c, bool returnName = true, bool returnArmy = true, bool returnQuote = false)
    {
        if (c == null || (!returnName && !returnArmy && !returnQuote)) return string.Empty;

        DeckManager deckManager = DeckManager.Instance;
        if (returnQuote)
        {
            CardData characterCard = deckManager?.FindAnyCardByName(c.characterName);
            return characterCard?.quote ?? string.Empty;
        }

        string result = "";
        if(returnName)
        {
            result += $"<u>{c.characterName}</u>";
        }

        if(returnArmy)
        {
            Army army = c.GetArmy();
            if (army != null && army.GetSize() > 0)
            {
                string armyText = army.GetHoverTextHexInfo();
                List<string> linkedArmyNames = army.GetTroopGroups()
                    .Where(group => group != null && !string.IsNullOrWhiteSpace(group.troopName))
                    .Select(group => group.troopName)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .Where(name => deckManager?.FindArmyCardByName(name) != null)
                    .OrderByDescending(name => name.Length)
                    .ToList();
                if (linkedArmyNames.Count > 0)
                {
                    string pattern = string.Join("|", linkedArmyNames.Select(System.Text.RegularExpressions.Regex.Escape));
                    armyText = System.Text.RegularExpressions.Regex.Replace(
                        armyText,
                        pattern,
                        match => $"<link=\"army:{match.Value}\"><u>{match.Value}</u></link>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                result += armyText;
            } else
            {
                result += " (wandering)";
            }
        }

        return result;
    }

}
