using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Dramatic full-screen "TURN X" cinematic banner shown at the start of each player turn.
/// Lives on a prefab placed in the scene (Canvas, letterbox bars, side banners, center text
/// all authored there) — this script only drives the show/hide animation. Call
/// TurnBanner.Show(turnNumber, bannerSprite) to trigger.
/// </summary>
public class TurnBanner : MonoBehaviour
{
    public static TurnBanner Instance { get; private set; }

    [Header("Root (CanvasGroup on this prefab's root, alpha starts at 0)")]
    [SerializeField] private CanvasGroup rootGroup;

    [Header("Letterbox Bars")]
    [SerializeField] private RectTransform topBarRect;
    [SerializeField] private RectTransform bottomBarRect;

    [Header("Side Banners (rightBannerRect authored mirrored, localScale.x = -1)")]
    [SerializeField] private Image leftBannerImg;
    [SerializeField] private Image rightBannerImg;
    [SerializeField] private RectTransform leftBannerRect;
    [SerializeField] private RectTransform rightBannerRect;

    [Header("Center Text")]
    [SerializeField] private RectTransform textRect;
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private RectTransform lineLeftRect;
    [SerializeField] private RectTransform lineRightRect;
    [SerializeField] private RectTransform dateRect;
    [SerializeField] private TextMeshProUGUI dateText;
    [SerializeField] private RectTransform infoRect;
    [SerializeField] private TextMeshProUGUI infoText;

    private const float BarHeight = 88f;
    private const float LineThickness = 1.5f;
    private const float LineMaxHalfWidth = 320f;
    private const float BannerRestX = 460f;
    private const float BannerStartX = 700f;
    private const float EnterDuration = 0.38f;
    private const float FlashDuration = 0.22f;
    private const float HoldDuration = 1.1f;
    private const float ExitDuration = 0.28f;

    public Color GoldColor = new(0.83f, 0.70f, 0.46f);

    // Whether PlayAnimation currently holds CenterDisplayLock. Show() below calls
    // StopAllCoroutines() to interrupt any run already in flight, which abandons that
    // coroutine without running its cleanup — this flag lets the next Show() release a
    // lock left behind by an interrupted run instead of wedging every other center
    // display (opportunity cards, PC/region grant preview) shut forever.
    private static bool holdsCenterLock;

    // Counts Turn/GatheringResources banner runs that have been requested but not yet fully
    // finished (queued or actively animating). Grants (Board.TriggerOwnPcGrantIfStandingOnOne
    // / TriggerRegionLandGrant) poll IsShowing and hold off calling CenterDisplayLock.WaitAsync
    // until this drops back to zero — SemaphoreSlim always hands a released permit to an
    // already-queued async waiter before a later synchronous Wait(0) poll (what this class's
    // coroutines use) ever gets a look-in, so a grant that queued while "TURN X" was still
    // playing would otherwise cut in front of "Gathering Resources" the instant the lock frees.
    private static int activeBannerCount;
    public static bool IsShowing => activeBannerCount > 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Enter Play mode, select this GameObject, then right-click the TurnBanner component
    // header (or its ⋮ menu) in the Inspector to fire these — no Board/Game session needed,
    // CenterDisplayLock starts unlocked so the animation runs standalone.
    [ContextMenu("Test: Show Turn Banner")]
    private void TestShowTurnBanner() => Show(7);

    [ContextMenu("Test: Show Gathering Resources")]
    private void TestShowGatheringResources() => ShowGatheringResources();

    // lockAlreadyHeld: pass true when the caller already reserved CenterDisplayLock itself
    // (see Game.ShowTurnZeroBanner, which must reserve it immediately — before anything
    // else can grab it — well ahead of the delay before this actually gets called).
    public static void Show(int turnNumber, Sprite bannerSprite = null, bool lockAlreadyHeld = false)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[CenterLock] TurnBanner.Instance is null — no TurnBanner in the scene.");
            // No banner instance in the scene to hand the lock off to (or animate). If the
            // caller already reserved the lock/slot (ReserveSlot + WaitCoroutine, see
            // Game.ShowTurnZeroBanner), undo both here — otherwise a missing/unwired prefab
            // permanently starves every other CenterDisplayLock consumer (situation cards,
            // grant previews) since nothing would ever call PlayAnimation's Release().
            if (lockAlreadyHeld)
            {
                CenterDisplayLock.Release();
                activeBannerCount = Mathf.Max(0, activeBannerCount - 1);
            }
            return;
        }
        Instance.StopAllCoroutines();
        if (holdsCenterLock)
        {
            holdsCenterLock = false;
            CenterDisplayLock.Release();
        }
        // If the caller already reserved (lockAlreadyHeld), it also already called
        // ReserveSlot() for the activeBannerCount bookkeeping below - don't stomp on that.
        if (!lockAlreadyHeld) activeBannerCount = 1;

        MiddleEarthDate today = MiddleEarthCalendar.GetDateFromTurn(turnNumber);
        Instance.StartCoroutine(Instance.PlayAnimation(
            $"TURN {turnNumber}", today.ToString(), BuildInfoText(today), bannerSprite, lockAlreadyHeld));
        Sounds.Instance?.PlayPositive();
    }

    // Called by Game.ShowTurnZeroBanner synchronously, at the same moment it reserves
    // CenterDisplayLock itself, so IsShowing already reads true for the whole ~1.1s delay
    // before Show(..., lockAlreadyHeld: true) actually runs - otherwise a grant's IsShowing
    // check right after StartGame kicks off that delayed coroutine would see false and queue
    // for CenterDisplayLock too early, defeating the point of the early reservation below.
    public static void ReserveSlot() => activeBannerCount++;

    // Shown right after the Turn banner and before the PC/region resource grants begin -
    // same cinematic treatment (queues behind the Turn banner via CenterDisplayLock, same as
    // the grant previews would), just announcing the handoff rather than a turn number.
    public static void ShowGatheringResources(bool playSound = true)
    {
        if (Instance == null) return;
        activeBannerCount++;
        Instance.StartCoroutine(Instance.PlayAnimation(
            "Gathering Resources", "from controlled Population Centers and Regions", string.Empty, null, lockAlreadyHeld: false));
        if (playSound) Sounds.Instance?.PlayPositive();
    }

    // This is THE exclusive center-screen display — it must block opportunity cards and
    // the PC/region grant preview from appearing underneath/alongside it, and must itself
    // wait if one of those happens to already be up (e.g. a grant preview still mid-flight
    // the instant a new turn starts).
    private IEnumerator PlayAnimation(string title, string subtitle, string info, Sprite bannerSprite, bool lockAlreadyHeld)
    {
        if (!lockAlreadyHeld)
        {
            Debug.Log($"[CenterLock] TurnBanner '{title}' waiting for CenterDisplayLock...");
            yield return CenterDisplayLock.WaitCoroutine();
        }
        Debug.Log($"[CenterLock] TurnBanner '{title}' acquired lock, playing.");
        holdsCenterLock = true;

        bool hasBanner = bannerSprite != null;

        turnText.text = title;
        turnText.color = GoldColor;
        dateText.text = subtitle;
        infoText.text = info;
        infoText.gameObject.SetActive(!string.IsNullOrEmpty(infoText.text));
        rootGroup.alpha = 1f;
        textRect.localScale = Vector3.zero;
        dateRect.localScale = Vector3.zero;
        infoRect.localScale = Vector3.zero;
        topBarRect.anchoredPosition = new Vector2(0, BarHeight);
        bottomBarRect.anchoredPosition = new Vector2(0, -BarHeight);
        lineLeftRect.sizeDelta = new Vector2(0, LineThickness);
        lineRightRect.sizeDelta = new Vector2(0, LineThickness);

        // Reset banners
        leftBannerImg.sprite = bannerSprite;
        rightBannerImg.sprite = bannerSprite;
        Color bannerHidden = new(1f, 1f, 1f, 0f);
        leftBannerImg.color = bannerHidden;
        rightBannerImg.color = bannerHidden;
        leftBannerRect.anchoredPosition = new Vector2(-BannerStartX, 0f);
        rightBannerRect.anchoredPosition = new Vector2(BannerStartX, 0f);
        leftBannerImg.enabled = hasBanner;
        rightBannerImg.enabled = hasBanner;

        // Phase 1: bars slide in, text punches in, banners sweep in from sides
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
            dateRect.localScale = Vector3.one * lineP;
            infoRect.localScale = Vector3.one * lineP;

            if (hasBanner)
            {
                float bannerP = EaseOutCubic(Mathf.Clamp01(p / 0.8f));
                float bannerX = Mathf.Lerp(BannerStartX, BannerRestX, bannerP);
                leftBannerRect.anchoredPosition = new Vector2(-bannerX, 0f);
                rightBannerRect.anchoredPosition = new Vector2(bannerX, 0f);
                Color bannerColor = new(1f, 1f, 1f, bannerP);
                leftBannerImg.color = bannerColor;
                rightBannerImg.color = bannerColor;
            }

            t += Time.deltaTime;
            yield return null;
        }
        topBarRect.anchoredPosition = Vector2.zero;
        bottomBarRect.anchoredPosition = Vector2.zero;
        textRect.localScale = Vector3.one;
        dateRect.localScale = Vector3.one;
        infoRect.localScale = Vector3.one;
        lineLeftRect.sizeDelta = new Vector2(LineMaxHalfWidth, LineThickness);
        lineRightRect.sizeDelta = new Vector2(LineMaxHalfWidth, LineThickness);
        if (hasBanner)
        {
            leftBannerRect.anchoredPosition = new Vector2(-BannerRestX, 0f);
            rightBannerRect.anchoredPosition = new Vector2(BannerRestX, 0f);
            leftBannerImg.color = Color.white;
            rightBannerImg.color = Color.white;
        }

        // Phase 2: gold shimmer flash
        t = 0f;
        while (t < FlashDuration)
        {
            float arc = Mathf.Sin((t / FlashDuration) * Mathf.PI);
            turnText.color = Color.Lerp(GoldColor, Color.white, arc * 0.65f);
            t += Time.deltaTime;
            yield return null;
        }
        turnText.color = GoldColor;

        // Phase 3: hold
        yield return new WaitForSeconds(HoldDuration);

        // Phase 4: fade everything out
        t = 0f;
        while (t < ExitDuration)
        {
            rootGroup.alpha = 1f - EaseInCubic(t / ExitDuration);
            t += Time.deltaTime;
            yield return null;
        }
        rootGroup.alpha = 0f;

        holdsCenterLock = false;
        CenterDisplayLock.Release();
        activeBannerCount = Mathf.Max(0, activeBannerCount - 1);
        Debug.Log($"[CenterLock] TurnBanner '{title}' released lock (activeBannerCount={activeBannerCount}).");
    }

    // Calendar-entry descriptions for the day, plus whichever environmental card is (or is about
    // to become) active — read directly rather than via EnvironmentalCardManager.ActiveCard alone,
    // since Game.NewTurn() shows the banner before NewTurnStarted fires and updates it.
    private static string BuildInfoText(MiddleEarthDate today)
    {
        var lines = new List<string>();

        IEnumerable<CalendarEntry> calendarEntries = DateEventManager.Instance?.GetEntriesForDate(today);
        if (calendarEntries != null)
        {
            string calendarText = string.Join(" · ", calendarEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.description))
                .Select(e => e.description));
            if (!string.IsNullOrEmpty(calendarText)) lines.Add(calendarText);
        }

        CardData envCard = DateEventManager.Instance?.GetEnvironmentCardForDate(today)
            ?? EnvironmentalCardManager.Instance?.ActiveCard;
        if (envCard != null)
        {
            lines.Add(string.IsNullOrWhiteSpace(envCard.description)
                ? envCard.name
                : $"{envCard.name}: {envCard.description}");
        }

        return string.Join("\n", lines);
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
}
