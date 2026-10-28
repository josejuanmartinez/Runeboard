using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayableLeaderIcon : MonoBehaviour
{
    public Image image;
    public bool videoMode;
    public NonPlayableLeaderIcons nonPlayableLeaderIcons;
    public CanvasGroup deadCanvasGroup;
    // public TextMeshProUGUI joinedText;
    public TextMeshProUGUI textWidget;
    public Image alignmentImage;
    public Image border;
    public TextMeshProUGUI victoryPoints;

    [HideInInspector]
    public AlignmentEnum alignment;
    [HideInInspector]
    public PlayableLeader playableLeader;

    private Sprite leaderSprite = null;
    public Sprite LeaderSprite => leaderSprite != null ? leaderSprite : image != null ? image.sprite : null;
    private string text = string.Empty;
    private bool initialized = false;
    private Illustrations illustrations;
    private Sprite highlightedSprite;

    public void Initialize(PlayableLeader leader)
    {
        playableLeader = leader;
        NonPlayableLeaderIcons icons = ResolveNonPlayableLeaderIcons();
        if (icons != null) icons.SetPlayableLeader(leader);
        alignment = leader.alignment;
        if (illustrations == null) illustrations = FindAnyObjectByType<Illustrations>();
        leaderSprite = illustrations != null ? illustrations.GetCharacterArtByName(leader.characterName) : null;
        text = leader.GetHoverText(true, false, false, false, false, false);
        SetLeaderVisuals(leaderSprite);
        textWidget.text = text;
        // joinedText.text = $"<mark=#ffffff>{leader.GetBiome().joinedText}</mark>";

        alignmentImage.sprite = illustrations.GetIllustrationByName(leader.alignment.ToString());
        RefreshVictoryPoints(leader.victoryPoints != null ? leader.victoryPoints.RelativeScore : 0);
        RemoveCurrentlyPlayingEffect();
        Restyle();

        // Start the coroutine to hide the text after 6 seconds
        // StartCoroutine(HideJoinedTextAfterDelay(6f));
        
        initialized = true;
    }

    public bool IsInitialized() => initialized;

    /*private IEnumerator HideJoinedTextAfterDelay(float delay)
    {
        // Wait for the specified delay
        yield return new WaitForSeconds(delay);

        // Hide the text
        // joinedText.gameObject.SetActive(false);
    }*/

    public void SetDead()
    {
        deadCanvasGroup.alpha = 1;
    }

    public void AddNonPlayableLeader(NonPlayableLeader nonPlayableLeader)
    {
        NonPlayableLeaderIcons icons = ResolveNonPlayableLeaderIcons();
        if (icons == null)
        {
            Debug.LogWarning($"No NonPlayableLeaderIcons child is wired for playable leader '{playableLeader?.characterName ?? name}'.");
            return;
        }

        if (nonPlayableLeader == null || playableLeader == null || nonPlayableLeader.GetAlignment() != playableLeader.GetAlignment())
        {
            Debug.LogWarning(
                $"Refusing to add NPL '{nonPlayableLeader?.characterName ?? "null"}' to mismatched leader icon " +
                $"'{playableLeader?.characterName ?? name}'.");
            return;
        }

        icons.Instantiate(nonPlayableLeader, playableLeader);
    }

    private NonPlayableLeaderIcons ResolveNonPlayableLeaderIcons()
    {
        // The reference must belong to this leader panel. A global lookup here sends every
        // NPL to whichever NonPlayableLeaderIcons Unity happens to return first.
        if (nonPlayableLeaderIcons != null &&
            (nonPlayableLeaderIcons.transform == transform || nonPlayableLeaderIcons.transform.IsChildOf(transform)))
        {
            return nonPlayableLeaderIcons;
        }

        nonPlayableLeaderIcons = GetComponentInChildren<NonPlayableLeaderIcons>(true);
        return nonPlayableLeaderIcons;
    }

    public void HighlighNonPlayableLeader(string leaderName, string leaderText)
    {
        if (illustrations == null) illustrations = FindAnyObjectByType<Illustrations>();
        highlightedSprite = illustrations != null ? illustrations.GetCharacterArtByName(leaderName) : null;
        SetLeaderVisuals(highlightedSprite);
        textWidget.text = leaderText;
    }

    public void Restore(string leaderName)
    {
        if (illustrations == null) illustrations = FindAnyObjectByType<Illustrations>();
        Sprite expectedSprite = illustrations != null ? illustrations.GetCharacterArtByName(leaderName) : null;
        bool restoreFromImage = image != null && image.sprite == expectedSprite;
        if (!restoreFromImage) return;

        SetLeaderVisuals(leaderSprite);
        textWidget.text = text;
    }

    // Whose turn it is: full brightness. The others are dimmed, never see-through (a faded
    // portrait let the score badge and the board show through it).
    public void SetCurrentlyPlayingEffect()
    {
        image.color = Color.white;
    }
    public void RemoveCurrentlyPlayingEffect()
    {
        image.color = new Color(0.55f, 0.55f, 0.55f, 1f);
    }

    // A solid badge under the score, and the alliance view on hover (HudDirector sizes the icon).
    private bool restyled;

    private void Restyle()
    {
        if (restyled) return;
        restyled = true;
        // The nation's colour round the portrait, as on the map's borders.
        if (border != null) border.color = UiTheme.NationRing(playableLeader.nationColor);
        if (victoryPoints != null && victoryPoints.transform.parent is RectTransform vp)
        {
            // The score group (VPBg: a frame icon and the number, no backing of its own) drawn
            // after the portrait, on a solid disc, and out of the skin system's reach (a skin
            // texture swapped in made its frame see-through).
            vp.SetAsLastSibling();
            var back = new GameObject("ScoreBack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)back.transform;
            rt.SetParent(vp, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            float d = Mathf.Max(vp.rect.width, vp.rect.height) * 1.05f;
            rt.sizeDelta = new Vector2(d, d);
            rt.anchoredPosition = Vector2.zero;
            var backImage = back.GetComponent<Image>();
            backImage.sprite = HudSprites.Circle;
            backImage.color = new Color(0.06f, 0.05f, 0.04f, 1f);
            backImage.raycastTarget = false;
            foreach (Image part in vp.GetComponentsInChildren<Image>(true))
            {
                if (part.GetComponent<ImageUnaffectedBySkin>() == null) part.gameObject.AddComponent<ImageUnaffectedBySkin>();
                if (part.gameObject.name != "Hover") { Color c = part.color; c.a = 1f; part.color = c; }
            }
            foreach (CanvasGroup g in vp.GetComponentsInChildren<CanvasGroup>(true)) g.alpha = 1f;
            victoryPoints.fontStyle |= TMPro.FontStyles.Bold;
        }
        if (image != null)
        {
            image.raycastTarget = true;
            PlayableLeaderHover hover = image.GetComponent<PlayableLeaderHover>();
            if (hover == null) hover = image.gameObject.AddComponent<PlayableLeaderHover>();
            hover.icon = this;
        }
    }

    // The score badge sits just outside the portrait's ring, at its lower right, never over the
    // portrait (HudDirector rescales the icon, so this follows the ring as it is drawn).
    private Vector3 placedFor = new(float.NaN, 0f, 0f);

    private void LateUpdate()
    {
        if (victoryPoints == null || border == null || victoryPoints.transform.parent is not RectTransform vp) return;
        RectTransform ring = border.rectTransform;
        var c = new Vector3[4];
        ring.GetWorldCorners(c);
        Vector3 key = new(c[2].x, c[0].y, c[2].x - c[0].x);
        if (key == placedFor) return;
        placedFor = key;
        // The ring's drawn circle is the narrower side of its rect.
        float size = Mathf.Min(c[2].x - c[0].x, c[2].y - c[0].y);
        Vector3 centre = (c[0] + c[2]) * 0.5f;
        vp.GetWorldCorners(c);
        float badgeW = c[2].x - c[0].x, badgeH = c[2].y - c[0].y;
        Vector3 pivotOffset = vp.position - (c[0] + c[2]) * 0.5f;
        Vector3 badgeCentre = new(centre.x + size * 0.5f + badgeW * 0.42f, centre.y - size * 0.5f + badgeH * 0.5f, vp.position.z);
        vp.position = badgeCentre + pivotOffset;
    }

    // With a campaign running, the badge shows objective points (what actually decides the war);
    // raw victory points are only the tie-breaker.
    public int DisplayScore(int fallbackPoints) =>
        CampaignObjectives.Instance != null && playableLeader != null ? CampaignObjectives.Instance.Score(playableLeader) : fallbackPoints;

    public void RefreshVictoryPoints(int points)
    {
        if (victoryPoints != null) victoryPoints.text = DisplayScore(points).ToString();
        PlayableLeaderIcons icons = FindAnyObjectByType<PlayableLeaderIcons>();
        if (icons != null) icons.UpdateVictoryPointColors();
    }

    /// <summary>Re-resolves a portrait that wasn't available at Initialize (art still streaming in).</summary>
    public void EnsurePortrait()
    {
        if (image == null || image.sprite != null || playableLeader == null) return;
        if (illustrations == null) illustrations = FindAnyObjectByType<Illustrations>();
        if (illustrations == null) return;
        Sprite sprite = illustrations.GetCharacterArtByName(playableLeader.characterName);
        string baseName = playableLeader.GetBiome()?.characterName;
        if (sprite == null && !string.IsNullOrWhiteSpace(baseName)) sprite = illustrations.GetCharacterArtByName(baseName);
        if (sprite == null) return;
        leaderSprite = sprite;
        SetLeaderVisuals(sprite);
    }

    private void SetLeaderVisuals(Sprite fallbackSprite)
    {        
        if (image != null)
        {
            image.enabled = true;
            image.sprite = fallbackSprite;
        }    
        
    }
}
