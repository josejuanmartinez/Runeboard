using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cinematic full-screen banner for combat/duel/assassination/wound events — the combat
/// counterpart to TurnBanner (same Canvas/CanvasGroup root, no Animator, hand-rolled coroutine
/// tween, same CenterDisplayLock). Lives on a prefab placed in the scene. Unlike TurnBanner
/// (which just interrupts/replaces whatever's mid-flight), several battles can resolve in one
/// AI turn and each deserves to be seen, so this queues requests instead of interrupting.
/// Call CombatBanner.Show(...) to trigger.
/// </summary>
public class CombatBanner : MonoBehaviour
{
    public static CombatBanner Instance { get; private set; }

    [Header("Root (CanvasGroup on this prefab's root, alpha starts at 0)")]
    [SerializeField] private CanvasGroup rootGroup;

    [Header("Letterbox Bars")]
    [SerializeField] private RectTransform topBarRect;
    [SerializeField] private RectTransform bottomBarRect;

    [Header("Actors (rects authored at rest position; neither is mirrored via localScale — the baked Left/Right facing atlases already face the right way)")]
    [SerializeField] private Image attackerImage;
    [SerializeField] private Image defenderImage;
    [SerializeField] private RectTransform attackerRect;
    [SerializeField] private RectTransform defenderRect;

    [Header("Nation Banners (small crest above each actor; hidden if that side has no banner sprite)")]
    [SerializeField] private Image attackerBannerImage;
    [SerializeField] private Image defenderBannerImage;

    [Header("Center Text")]
    [SerializeField] private RectTransform textRect;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private RectTransform lineLeftRect;
    [SerializeField] private RectTransform lineRightRect;
    [SerializeField] private RectTransform subtitleRect;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private RectTransform nationsRect;
    [SerializeField] private TextMeshProUGUI nationsText;

    [Header("Transient Notices (army abilities / status effects, faded in-out on their own)")]
    [SerializeField] private TextMeshProUGUI noticeText;
    [SerializeField] private TextMeshProUGUI attackerStatusText;
    [SerializeField] private TextMeshProUGUI defenderStatusText;

    private const float BarHeight = 88f;
    private const float LineThickness = 1f;
    private const float LineMaxHalfWidth = 320f;
    private const float ActorRestX = 460f;
    private const float ActorStartX = 700f;
    private const float EnterDuration = 0.38f;
    private const float HoldBeforeClashDuration = 0.5f;
    private const float PostClashHoldDuration = 1.8f;
    private const float ExitDuration = 0.28f;
    private const float NoticeFadeDuration = 0.25f;
    // Notice hold scales with how much there is to read (ability triggers + status lines can
    // run long), rather than a fixed beat that's fine for one line and too short for five.
    private const float NoticeHoldBase = 1.0f;
    private const float NoticeHoldPerLine = 0.55f;
    private const float NoticeHoldMax = 4f;
    private const float StatusCalloutFadeDuration = 0.2f;
    private const float StatusCalloutHoldDuration = 1.4f;

    private static readonly Color GoldColor = new(.79f, .64f, .40f);
    private static readonly Color PositiveModifierColor = new(0.45f, 0.85f, 0.45f);
    private static readonly Color NegativeModifierColor = new(0.9f, 0.35f, 0.35f);

    // Wraps a notice line in TMP rich-text color (NoticeText has rich text enabled). Callers
    // building battle-modifier lines (fortification bonus, ally contributions, artifact bonuses,
    // etc.) should use ColorizeModifier instead — this is for reusing an already-meaningful
    // color, e.g. an army ability's own message color.
    public static string Colorize(string text, Color color) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

    // Green for a bonus, red for a penalty. Callers should only pass lines that are actually
    // non-zero — a "+0" or "-0" modifier isn't worth a notice line.
    public static string ColorizeModifier(string text, float value) =>
        Colorize(text, value >= 0 ? PositiveModifierColor : NegativeModifierColor);

    private struct Request
    {
        public string title;
        public string verb;
        public string locationLabel;
        public Character attacker;
        public Character defender;
        public bool attackerWounded;
        public bool attackerKilled;
        public bool defenderWounded;
        public bool defenderKilled;
        // Pre-formatted transient notice lines: army special-ability trigger text and/or battle
        // modifier callouts (fortification, ally contributions, artifact bonuses, etc, usually
        // pre-colored via Colorize/ColorizeModifier) — only army-vs-army combat populates these;
        // null/empty for duels, assassinations, wounds.
        public List<string> noticeMessages;
        // Status effects each side already carried into this fight, and (separately) any
        // newly applied as a result of it — see Show()'s parameter comments.
        public List<StatusEffectEnum> attackerExistingStatusEffects;
        public List<StatusEffectEnum> defenderExistingStatusEffects;
        public List<StatusEffectEnum> attackerNewStatusEffects;
        public List<StatusEffectEnum> defenderNewStatusEffects;
        public Action onComplete;
    }

    // Queued rather than interrupted (contrast TurnBanner.Show's StopAllCoroutines): several
    // battles can resolve back-to-back during AI turn processing and none should be skipped.
    private readonly Queue<Request> queue = new();
    private bool isPlaying;

    private ActorAnimator attackerAnimator;
    private ActorAnimator defenderAnimator;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // This is a cinematic display, not an interactive modal. The scene used to override
        // blocksRaycasts=true while alpha was zero, leaving an invisible sorting-order-32767
        // canvas above every menu and swallowing all pointer input.
        if (rootGroup != null)
        {
            rootGroup.interactable = false;
            rootGroup.blocksRaycasts = false;
        }
        attackerAnimator = new ActorAnimator(attackerImage);
        defenderAnimator = new ActorAnimator(defenderImage);
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        Color paper = new(.91f, .89f, .81f);
        void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
        void TextStyle(TMP_Text label, float size, Color tint)
        {
            if (label == null) return;
            label.fontSize = label.fontSizeMax = size;
            label.fontSizeMin = size * .65f;
            label.enableAutoSizing = true;
            label.color = tint;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Normal;
            label.overrideColorTags = false;
            label.enableVertexGradient = false;
            label.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
            label.fontMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            label.fontMaterial.DisableKeyword("UNDERLAY_ON");
            label.outlineColor = new Color(.025f, .03f, .035f, 1f);
            label.outlineWidth = .06f;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
        }
        Image backdrop = transform.Find("Bg")?.GetComponent<Image>();
        if (backdrop != null) backdrop.color = new Color(.018f, .024f, .03f, .82f);
        var surfaceObject = new GameObject("Combat Surface", typeof(RectTransform), typeof(CanvasRenderer), typeof(RuneboardPanel));
        surfaceObject.transform.SetParent(transform, false);
        surfaceObject.transform.SetSiblingIndex(backdrop != null ? backdrop.transform.GetSiblingIndex() + 1 : 0);
        Place(surfaceObject.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1320, 780));
        var panel = surfaceObject.GetComponent<RuneboardPanel>();
        panel.topColor = new Color(.10f, .115f, .125f, .98f);
        panel.bottomColor = new Color(.028f, .037f, .044f, .98f);
        panel.edgeColor = new Color(GoldColor.r, GoldColor.g, GoldColor.b, .65f);
        panel.corner = 12;
        panel.raycastTarget = false;

        Place(textRect, new Vector2(0, 307), new Vector2(1160, 76));
        TextStyle(titleText, 48, GoldColor);
        Place(subtitleRect, new Vector2(0, -260), new Vector2(1160, 56));
        TextStyle(subtitleText, 29, paper);
        Place(nationsRect, new Vector2(0, -312), new Vector2(1140, 42));
        TextStyle(nationsText, 21, new Color(.66f, .69f, .67f));
        Place(noticeText.rectTransform, new Vector2(0, 5), new Vector2(560, 290));
        TextStyle(noticeText, 23, paper);
        noticeText.lineSpacing = 8;
        TextStyle(attackerStatusText, 22, paper);
        TextStyle(defenderStatusText, 22, paper);
        attackerImage.preserveAspect = defenderImage.preserveAspect = true;
        attackerBannerImage.preserveAspect = defenderBannerImage.preserveAspect = true;
        foreach (RectTransform line in new[] { lineLeftRect, lineRightRect })
        {
            if (line == null) continue;
            line.anchoredPosition = new Vector2(line.anchoredPosition.x, 252);
            if (line.TryGetComponent(out Image image)) image.color = GoldColor;
        }
        // These decorative images should never receive skin replacement textures or pointer input.
        foreach (Image image in GetComponentsInChildren<Image>(true))
        {
            image.raycastTarget = false;
            if (image.GetComponent<ImageUnaffectedBySkin>() == null) image.gameObject.AddComponent<ImageUnaffectedBySkin>();
        }
    }

    // Enter Play mode, select this GameObject, then right-click the CombatBanner component
    // header in the Inspector to fire this — grabs the first two Characters found in the
    // scene, no real battle needed.
    [ContextMenu("Test: Show Combat Banner")]
    private void TestShowCombatBanner()
    {
        Character[] characters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        if (characters.Length < 2)
        {
            Debug.LogWarning("[CombatBanner] Need at least 2 Characters in the scene to test.");
            return;
        }
        string location = characters[0].hex != null ? characters[0].hex.GetBattleLocationLabel() : "0,0";
        Show("Combat", "attacks", characters[0], characters[1], true, false, true, false, location);
    }

    // noticeMessages: pre-formatted transient notice lines — army special-ability trigger text
    // and/or battle modifier callouts (fortification, ally contributions, artifact bonuses),
    // usually pre-colored via Colorize/ColorizeModifier. Army combat only; leave null elsewhere.
    // attacker/defenderExistingStatusEffects: status effects that character already had going
    // into this fight (a snapshot — pass a copy taken BEFORE any of this event's own status
    // effects were applied, e.g. before Army's TriggerBattleSpecialAbilities runs). Shown
    // alongside noticeMessages as a transient notice before the clash animation.
    // attacker/defenderNewStatusEffects: status effects newly applied as a direct result of
    // this event (e.g. an army ability poisoning the enemy commander). Shown as a callout next
    // to that side's portrait right after its Hit/Block/Death reaction plays.
    public static void Show(
        string title, string verb,
        Character attacker, Character defender,
        bool attackerWounded, bool attackerKilled,
        bool defenderWounded, bool defenderKilled,
        string locationLabel,
        List<string> noticeMessages = null,
        List<StatusEffectEnum> attackerExistingStatusEffects = null,
        List<StatusEffectEnum> defenderExistingStatusEffects = null,
        List<StatusEffectEnum> attackerNewStatusEffects = null,
        List<StatusEffectEnum> defenderNewStatusEffects = null,
        Action onComplete = null)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[CombatBanner] Instance is null — no CombatBanner in the scene.");
            onComplete?.Invoke();
            return;
        }
        if (attacker == null || defender == null)
        {
            onComplete?.Invoke();
            return;
        }

        Instance.queue.Enqueue(new Request
        {
            title = title,
            verb = verb,
            locationLabel = locationLabel,
            attacker = attacker,
            defender = defender,
            attackerWounded = attackerWounded,
            attackerKilled = attackerKilled,
            defenderWounded = defenderWounded,
            defenderKilled = defenderKilled,
            // Copied rather than held by reference — this request may sit queued behind other
            // banners for a while, during which the caller's own lists could keep changing.
            noticeMessages = noticeMessages != null ? new List<string>(noticeMessages) : null,
            attackerExistingStatusEffects = attackerExistingStatusEffects != null ? new List<StatusEffectEnum>(attackerExistingStatusEffects) : null,
            defenderExistingStatusEffects = defenderExistingStatusEffects != null ? new List<StatusEffectEnum>(defenderExistingStatusEffects) : null,
            attackerNewStatusEffects = attackerNewStatusEffects != null ? new List<StatusEffectEnum>(attackerNewStatusEffects) : null,
            defenderNewStatusEffects = defenderNewStatusEffects != null ? new List<StatusEffectEnum>(defenderNewStatusEffects) : null,
            onComplete = onComplete
        });

        if (!Instance.isPlaying) Instance.StartCoroutine(Instance.ProcessQueue());
    }

    private IEnumerator ProcessQueue()
    {
        isPlaying = true;
        while (queue.Count > 0)
        {
            Request request = queue.Dequeue();
            yield return PlayRequest(request);
            request.onComplete?.Invoke();
        }
        isPlaying = false;
    }

    private IEnumerator PlayRequest(Request request)
    {
        Debug.Log($"[CenterLock] CombatBanner '{request.title}' waiting for CenterDisplayLock...");
        yield return CenterDisplayLock.WaitCoroutine();
        Debug.Log($"[CenterLock] CombatBanner '{request.title}' acquired lock, playing.");

        Leader attackerOwner = request.attacker.GetOwner();
        Leader defenderOwner = request.defender.GetOwner();
        string attackerNation = attackerOwner != null ? attackerOwner.characterName : "Unaligned";
        string defenderNation = defenderOwner != null ? defenderOwner.characterName : "Unaligned";

        Sprite attackerBanner = ResolveBannerSprite(attackerOwner);
        Sprite defenderBanner = ResolveBannerSprite(defenderOwner);
        attackerBannerImage.enabled = attackerBanner != null;
        defenderBannerImage.enabled = defenderBanner != null;
        attackerBannerImage.sprite = attackerBanner;
        defenderBannerImage.sprite = defenderBanner;

        titleText.text = $"{request.title} at {request.locationLabel}";
        titleText.color = GoldColor;
        subtitleText.text = $"{request.attacker.characterName} {request.verb} {request.defender.characterName}";
        nationsText.text = $"{attackerNation}    /    {defenderNation}";

        // Attacker faces screen-right (baked "Left" atlas — see CharacterAnimationController.
        // ResolveDirectionOrientation's comment: facing names are inverted from the on-screen
        // result), defender faces screen-left (baked "Right" atlas).
        attackerAnimator.Setup(request.attacker, "Left");
        defenderAnimator.Setup(request.defender, "Right");

        rootGroup.alpha = 1f;
        textRect.localScale = Vector3.zero;
        subtitleRect.localScale = Vector3.zero;
        nationsRect.localScale = Vector3.zero;
        topBarRect.anchoredPosition = new Vector2(0, BarHeight);
        bottomBarRect.anchoredPosition = new Vector2(0, -BarHeight);
        lineLeftRect.sizeDelta = new Vector2(0, LineThickness);
        lineRightRect.sizeDelta = new Vector2(0, LineThickness);
        Color hidden = new(1f, 1f, 1f, 0f);
        attackerImage.color = hidden;
        defenderImage.color = hidden;
        attackerBannerImage.color = hidden;
        defenderBannerImage.color = hidden;
        attackerRect.anchoredPosition = new Vector2(-ActorStartX, 0f);
        defenderRect.anchoredPosition = new Vector2(ActorStartX, 0f);
        noticeText.alpha = 0f;
        attackerStatusText.alpha = 0f;
        defenderStatusText.alpha = 0f;

        // Phase 1: bars slide in, text punches in, actors sweep in from opposite sides.
        float t = 0f;
        while (t < EnterDuration)
        {
            float p = t / EnterDuration;
            float barEase = EaseOutCubic(p);
            topBarRect.anchoredPosition = new Vector2(0, Mathf.Lerp(BarHeight, 0f, barEase));
            bottomBarRect.anchoredPosition = new Vector2(0, Mathf.Lerp(-BarHeight, 0f, barEase));

            float textP = EaseOutBack(Mathf.Clamp01((p - 0.25f) / 0.75f));
            textRect.localScale = Vector3.one * textP;

            float lineP = EaseOutCubic(Mathf.Clamp01((p - 0.55f) / 0.45f));
            lineLeftRect.sizeDelta = new Vector2(lineP * LineMaxHalfWidth, LineThickness);
            lineRightRect.sizeDelta = new Vector2(lineP * LineMaxHalfWidth, LineThickness);
            subtitleRect.localScale = Vector3.one * lineP;
            nationsRect.localScale = Vector3.one * lineP;

            float actorP = EaseOutCubic(Mathf.Clamp01(p / 0.8f));
            float actorX = Mathf.Lerp(ActorStartX, ActorRestX, actorP);
            attackerRect.anchoredPosition = new Vector2(-actorX, 0f);
            defenderRect.anchoredPosition = new Vector2(actorX, 0f);
            Color actorColor = new(1f, 1f, 1f, actorP);
            attackerImage.color = actorColor;
            defenderImage.color = actorColor;
            attackerBannerImage.color = actorColor;
            defenderBannerImage.color = actorColor;

            t += Time.deltaTime;
            yield return null;
        }
        topBarRect.anchoredPosition = Vector2.zero;
        bottomBarRect.anchoredPosition = Vector2.zero;
        textRect.localScale = Vector3.one;
        subtitleRect.localScale = Vector3.one;
        nationsRect.localScale = Vector3.one;
        lineLeftRect.sizeDelta = new Vector2(LineMaxHalfWidth, LineThickness);
        lineRightRect.sizeDelta = new Vector2(LineMaxHalfWidth, LineThickness);
        attackerRect.anchoredPosition = new Vector2(-ActorRestX, 0f);
        defenderRect.anchoredPosition = new Vector2(ActorRestX, 0f);
        attackerImage.color = Color.white;
        defenderImage.color = Color.white;
        attackerBannerImage.color = Color.white;
        defenderBannerImage.color = Color.white;

        // Phase 1.5: army abilities + status effects either side already carried into this
        // fight, as a transient notice that fades out again before any clash animation plays.
        // Skipped (just the usual short beat) when there's nothing to report.
        List<string> notices = BuildNoticeLines(request);
        if (notices.Count > 0)
        {
            yield return PlayNotice(string.Join("\n", notices), notices.Count);
        }
        else
        {
            yield return new WaitForSeconds(HoldBeforeClashDuration);
        }

        // Phase 2: clash. Attacker always attacks; defender attacks back only if it actually
        // took damage this exchange, otherwise it blocks (see CombatBanner design notes: Block
        // vs Attack is decided purely by "did the defender take zero damage").
        Coroutine attackerClash = StartCoroutine(attackerAnimator.PlayAndHold("Attack"));
        Coroutine defenderClash = StartCoroutine(defenderAnimator.PlayAndHold(request.defenderWounded ? "Attack" : "Block"));
        yield return attackerClash;
        yield return defenderClash;

        // Phase 3: outcome. Death takes priority over Hit; a side that took no damage plays neither.
        List<Coroutine> outcomeRoutines = new();
        if (request.attackerKilled) outcomeRoutines.Add(StartCoroutine(attackerAnimator.PlayAndHold("Death")));
        else if (request.attackerWounded) outcomeRoutines.Add(StartCoroutine(attackerAnimator.PlayAndHold("Hit")));
        if (request.defenderKilled) outcomeRoutines.Add(StartCoroutine(defenderAnimator.PlayAndHold("Death")));
        else if (request.defenderWounded) outcomeRoutines.Add(StartCoroutine(defenderAnimator.PlayAndHold("Hit")));
        foreach (Coroutine routine in outcomeRoutines) yield return routine;

        // Phase 3.5: status effects newly applied by this fight (e.g. an army ability poisoning
        // the enemy commander), called out next to whichever side actually received one — unless
        // that side died in this fight, in which case there's no one left to carry the effect.
        List<Coroutine> statusRoutines = new();
        if (!request.attackerKilled && request.attackerNewStatusEffects != null && request.attackerNewStatusEffects.Count > 0)
        {
            statusRoutines.Add(StartCoroutine(PlayStatusCallout(attackerStatusText, request.attackerNewStatusEffects)));
        }
        if (!request.defenderKilled && request.defenderNewStatusEffects != null && request.defenderNewStatusEffects.Count > 0)
        {
            statusRoutines.Add(StartCoroutine(PlayStatusCallout(defenderStatusText, request.defenderNewStatusEffects)));
        }
        foreach (Coroutine routine in statusRoutines) yield return routine;

        yield return new WaitForSeconds(PostClashHoldDuration);

        // Phase 4: fade everything out.
        t = 0f;
        while (t < ExitDuration)
        {
            rootGroup.alpha = 1f - EaseInCubic(t / ExitDuration);
            t += Time.deltaTime;
            yield return null;
        }
        rootGroup.alpha = 0f;

        CenterDisplayLock.Release();
        Debug.Log($"[CenterLock] CombatBanner '{request.title}' released lock.");
    }

    // Same lookup SelectedCharacterIcon/Game/Hex/LeaderSelector already use for a leader's
    // nation banner: a PlayableLeader's selected variant can override the base biome's banner,
    // everyone else (NonPlayableLeader included) just uses the biome's own. Returns null (and
    // the caller hides the image) if this leader has no banner set at all.
    private static Sprite ResolveBannerSprite(Leader owner)
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
                    && ((!string.IsNullOrWhiteSpace(v.variantId) && string.Equals(v.variantId, subdeckId, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(v.subdeckId) && string.Equals(v.subdeckId, subdeckId, StringComparison.OrdinalIgnoreCase))));
                if (!string.IsNullOrWhiteSpace(variant?.banner))
                    bannerName = variant.banner;
            }
        }

        if (string.IsNullOrWhiteSpace(bannerName))
            bannerName = biome.banner;
        if (string.IsNullOrWhiteSpace(bannerName)) return null;

        Illustrations illustrations = GameObject.FindFirstObjectByType<Illustrations>();
        return illustrations != null ? illustrations.GetIllustrationByName(bannerName, false) : null;
    }

    // Army ability/modifier notice lines, then one line per side listing status effects it
    // already had going into this fight (skips a side with none, or one that dies in this
    // fight — a status effect on a corpse isn't worth reporting).
    private static List<string> BuildNoticeLines(Request request)
    {
        List<string> lines = new();
        if (request.noticeMessages != null) lines.AddRange(request.noticeMessages);

        if (!request.attackerKilled && request.attackerExistingStatusEffects != null && request.attackerExistingStatusEffects.Count > 0)
        {
            lines.Add($"{request.attacker.characterName}: {string.Join(", ", request.attackerExistingStatusEffects.ConvertAll(FormatStatusEffect))}");
        }
        if (!request.defenderKilled && request.defenderExistingStatusEffects != null && request.defenderExistingStatusEffects.Count > 0)
        {
            lines.Add($"{request.defender.characterName}: {string.Join(", ", request.defenderExistingStatusEffects.ConvertAll(FormatStatusEffect))}");
        }
        return lines;
    }

    private IEnumerator PlayNotice(string text, int lineCount)
    {
        noticeText.text = text;
        float hold = Mathf.Min(NoticeHoldMax, NoticeHoldBase + NoticeHoldPerLine * lineCount);
        yield return FadeText(noticeText, 0f, 1f, NoticeFadeDuration);
        yield return new WaitForSeconds(hold);
        yield return FadeText(noticeText, 1f, 0f, NoticeFadeDuration);
    }

    private IEnumerator PlayStatusCallout(TextMeshProUGUI label, List<StatusEffectEnum> effects)
    {
        label.text = string.Join(", ", effects.ConvertAll(FormatStatusEffect));
        yield return FadeText(label, 0f, 1f, StatusCalloutFadeDuration);
        yield return new WaitForSeconds(StatusCalloutHoldDuration);
        yield return FadeText(label, 1f, 0f, StatusCalloutFadeDuration);
    }

    private static IEnumerator FadeText(TextMeshProUGUI text, float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            text.alpha = Mathf.Lerp(from, to, t / duration);
            t += Time.deltaTime;
            yield return null;
        }
        text.alpha = to;
    }

    // "RefusingDuels" -> "Refusing Duels", "ArcaneInsight" -> "Arcane Insight".
    private static string FormatStatusEffect(StatusEffectEnum effect)
    {
        string raw = effect.ToString();
        System.Text.StringBuilder sb = new();
        for (int i = 0; i < raw.Length; i++)
        {
            if (i > 0 && char.IsUpper(raw[i])) sb.Append(' ');
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
    private static float EaseInCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
    private static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // Drives one side's Image by hand-swapping baked spritesheet frames from CharacterSpritesheets
    // — the same data CharacterAnimationController reads for the on-map SpriteRenderer, but
    // simpler: facing is fixed for the whole banner (no turn-cycling) and each requested state
    // plays once through and holds its last frame rather than looping.
    private class ActorAnimator
    {
        private const string StandingIdleStateName = "standing idle";

        private readonly Image image;
        private string raceOrName;
        private string facing;
        private bool hasSpritesheet;
        private Sprite staticFallback;

        public ActorAnimator(Image image) { this.image = image; }

        // No retry-on-Addressables-still-loading here (unlike CharacterAnimationController's
        // pendingCharacter mechanism) — by the time any real combat can happen in a game
        // session, CharacterSpritesheets has long since finished its boot-time load, and this
        // banner is a one-shot event rather than a persistent scene fixture that would benefit
        // from catching up later.
        public void Setup(Character character, string facingName)
        {
            facing = facingName;
            hasSpritesheet = CharacterSpritesheets.TryResolveRaceOrName(
                character.characterName, character.SpriteVariantBaseName, character.race, null, out raceOrName);

            Illustrations illustrations = GameObject.FindFirstObjectByType<Illustrations>();
            staticFallback = illustrations != null ? illustrations.GetIllustrationByName(character) : null;
            image.sprite = staticFallback;
            if (!hasSpritesheet) return;

            SetFrame(StandingIdleStateName, 0);
        }

        public IEnumerator PlayAndHold(string stateName)
        {
            if (!hasSpritesheet) yield break;

            CharacterSpritesheets.AtlasManifest manifest = CharacterSpritesheets.GetManifest(raceOrName, facing);
            CharacterSpritesheets.AtlasState state = FindState(manifest, stateName);
            if (state == null)
            {
                // Not baked for this character/facing — fall back to idle rather than freeze
                // or stay on a stale frame from the previous phase.
                SetFrame(StandingIdleStateName, 0);
                yield break;
            }

            float frameDuration = 1f / Mathf.Max(state.fps, 0.01f);
            for (int frame = 0; frame < state.frameCount; frame++)
            {
                Sprite sprite = CharacterSpritesheets.GetSprite($"{state.spriteNamePrefix}_{frame:D2}");
                if (sprite != null) image.sprite = sprite;
                yield return new WaitForSeconds(frameDuration);
            }
        }

        private void SetFrame(string stateName, int frameIndex)
        {
            CharacterSpritesheets.AtlasManifest manifest = CharacterSpritesheets.GetManifest(raceOrName, facing);
            CharacterSpritesheets.AtlasState state = FindState(manifest, stateName);
            if (state == null) return;
            Sprite sprite = CharacterSpritesheets.GetSprite($"{state.spriteNamePrefix}_{frameIndex:D2}");
            if (sprite != null) image.sprite = sprite;
        }

        private static CharacterSpritesheets.AtlasState FindState(CharacterSpritesheets.AtlasManifest manifest, string stateName)
        {
            if (manifest?.states == null) return null;
            foreach (CharacterSpritesheets.AtlasState state in manifest.states)
                if (state.name == stateName) return state;
            return null;
        }
    }
}
