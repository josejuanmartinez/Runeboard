using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// Plays a leader's intro video (one clip per playable-leader variant, plus one generic clip for
// when no variant was picked) as a full-screen popup. Shown once, at the very start of turn 0,
// before any other startup popup or camera movement — see Game.StartGame()'s first line and
// PopupManager.ShouldDelayPopup(), which holds off ordinary popups while this is showing.
public class VideoPopupManager : MonoBehaviour
{
    public static VideoPopupManager Instance { get; private set; }
    public static bool IsShowing { get; private set; }

    [Serializable]
    public class LeaderVideoEntry
    {
        // Generic entries are keyed by the base leader's characterName (e.g. "Gandalf").
        // Variant entries are keyed by the variant's subdeckId/variantId (e.g. "stormcrow").
        public string key;
        public VideoClip clip;
    }

    [Header("References")]
    public GameObject container;
    public CanvasGroup canvasGroup;
    public VideoPlayer videoPlayer;
    public RawImage videoDisplay;
    public TypewriterEffect scrollableText;
    public Button closeButton;

    [Header("Videos - Generic (no variant chosen)")]
    public List<LeaderVideoEntry> genericVideos = new();

    [Header("Videos - Playable Leader Variants")]
    public List<LeaderVideoEntry> variantVideos = new();

    private GameObject visibilityRoot;
    private CanvasGroup visibilityCanvasGroup;
    private readonly HashSet<string> permanentlyHiddenKeys = new(StringComparer.OrdinalIgnoreCase);
    private string currentKey;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        visibilityRoot = ResolveVisibilityRoot();
        visibilityCanvasGroup = canvasGroup != null ? canvasGroup : (visibilityRoot != null ? visibilityRoot.GetComponent<CanvasGroup>() : null);
        if (visibilityCanvasGroup == null && visibilityRoot != null)
        {
            visibilityCanvasGroup = visibilityRoot.AddComponent<CanvasGroup>();
        }
        SetContainerVisible(false);
        if (visibilityRoot != null && !visibilityRoot.activeSelf)
        {
            visibilityRoot.SetActive(true);
        }
        IsShowing = false;

        // Continue first completes the narration, then closes (see Continue()).
        if (!PopupContinue.Rewire(this, "Hide", Continue) && closeButton != null)
            closeButton.onClick.AddListener(Continue);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static void ShowForLeader(PlayableLeader leader)
        => Instance?.ShowForLeaderInternal(leader);

    private void ShowForLeaderInternal(PlayableLeader leader)
    {
        if (leader == null) return;

        string key = ResolveKey(leader, out string introText);
        if (string.IsNullOrWhiteSpace(key) || permanentlyHiddenKeys.Contains(key)) return;

        VideoClip clip = FindClip(key);
        if (clip == null) return;

        // Clip and intro text are authored in two different places — the clip lists on this
        // component, the text in PlayableLeaderBiomes.json — so either half can be missing on
        // its own. A clip without text played as a video over a permanently blank text panel;
        // treat the pair as one unit and skip the popup until both halves exist.
        if (string.IsNullOrWhiteSpace(introText)) return;

        currentKey = key;
        Game.Instance?.NotifyStartupPopupShown();

        Stylize(DisplayNames.Format(leader.characterName));
        if (scrollableText != null)
        {
            scrollableText.StartWriting(introText);
        }

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.clip = clip;
            videoPlayer.Play();
        }

        SetContainerVisible(true);
        IsShowing = true;
    }

    private static string ResolveKey(PlayableLeader leader, out string introText)
    {
        introText = null;
        LeaderBiomeConfig biome = leader.GetBiome();
        string variantName = leader.GetSelectedVariantName();

        if (string.IsNullOrWhiteSpace(variantName))
        {
            introText = biome?.introVideoText;
            return leader.characterName;
        }

        string subdeckId = leader.GetSelectedSubdeckId();
        LeaderVariantConfig variant = biome?.variants?.Find(v =>
            v != null && (string.Equals(v.subdeckId, subdeckId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(v.variantId, subdeckId, StringComparison.OrdinalIgnoreCase)));
        introText = variant?.introVideoText;
        return subdeckId;
    }

    private VideoClip FindClip(string key)
    {
        LeaderVideoEntry entry = variantVideos.Find(e => e != null && string.Equals(e.key, key, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            entry = genericVideos.Find(e => e != null && string.Equals(e.key, key, StringComparison.OrdinalIgnoreCase));
        }
        return entry?.clip;
    }

    /// <summary>First press reveals the rest of the narration; the next one closes.</summary>
    public void Continue()
    {
        if (scrollableText != null && scrollableText.IsTyping)
        {
            scrollableText.Complete();
            return;
        }
        Hide();
    }

    // Story presentation: narration in the display serif at reading size, a speaker line above
    // it, and a gold hairline framing the illustration. Runtime-only so the prefab stays as is.
    private bool stylized;
    private TMPro.TextMeshProUGUI speakerLine;

    private void Stylize(string speaker)
    {
        TMPro.TMP_Text body = scrollableText != null ? scrollableText.textMeshPro : null;
        if (!stylized)
        {
            stylized = true;
            if (body != null)
            {
                UiTheme.StyleTitle(body, 23f, UiTheme.Text);
                body.lineSpacing = 8f;
                body.paragraphSpacing = 10f;
                body.alignment = TMPro.TextAlignmentOptions.TopLeft;
            }
            if (scrollableText != null && scrollableText.transform is RectTransform textRoot)
            {
                RectTransform scroll = textRoot.GetComponentInParent<ScrollRect>() is ScrollRect sr ? sr.transform as RectTransform : textRoot;
                var go = new GameObject("Speaker", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(scroll, false);
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(0f, 6f);
                rt.sizeDelta = new Vector2(0f, 40f);
                speakerLine = go.GetComponent<TMPro.TextMeshProUGUI>();
                speakerLine.raycastTarget = false;
                speakerLine.alignment = TMPro.TextAlignmentOptions.BottomLeft;
                speakerLine.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                UiTheme.StyleTitle(speakerLine, 26f);
            }
            if (videoDisplay != null && videoDisplay.transform is RectTransform art)
            {
                var frameGo = new GameObject("ArtFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(CinematicPanel));
                var frame = frameGo.GetComponent<RectTransform>();
                frame.SetParent(art.parent, false);
                frame.SetSiblingIndex(art.GetSiblingIndex() + 1);
                frame.anchorMin = art.anchorMin;
                frame.anchorMax = art.anchorMax;
                frame.pivot = art.pivot;
                frame.anchoredPosition = art.anchoredPosition;
                frame.sizeDelta = art.sizeDelta + new Vector2(14f, 14f);
                var panel = frameGo.GetComponent<CinematicPanel>();
                panel.fill = false;
                panel.inset = 0f;
                panel.ornament = 4f;
                panel.bottomOrnament = true;
                panel.raycastTarget = false;
            }
        }
        if (speakerLine != null)
            speakerLine.text = $"<size=55%><color={UiTheme.GoldHex}><cspace=0.14em>PROLOGUE</cspace></color></size>   {speaker}";
    }

    public void Hide()
    {
        if (!string.IsNullOrWhiteSpace(currentKey))
        {
            permanentlyHiddenKeys.Add(currentKey);
        }
        currentKey = null;

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.clip = null;
        }

        scrollableText?.Clear();

        SetContainerVisible(false);
        IsShowing = false;
        Game.Instance?.NotifyStartupPopupClosed();
    }

    private void SetContainerVisible(bool visible)
    {
        if (visibilityCanvasGroup == null) return;
        visibilityCanvasGroup.alpha = visible ? 1f : 0f;
        visibilityCanvasGroup.interactable = visible;
        visibilityCanvasGroup.blocksRaycasts = visible;
    }

    private GameObject ResolveVisibilityRoot()
    {
        if (container == null) return null;

        Transform candidate = container.transform;
        while (candidate.parent != null && candidate.parent != transform)
        {
            candidate = candidate.parent;
        }

        return candidate.gameObject;
    }
}
