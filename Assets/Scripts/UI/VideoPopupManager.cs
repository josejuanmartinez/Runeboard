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

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Hide);
        }
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

        currentKey = key;
        Game.Instance?.NotifyStartupPopupShown();

        if (scrollableText != null)
        {
            scrollableText.StartWriting(introText ?? string.Empty);
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
