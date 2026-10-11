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

    public void SetCurrentlyPlayingEffect()
    {
        image.color = new Color(image.color.r, image.color.g, image.color.b, 1.0f);
    }
    public void RemoveCurrentlyPlayingEffect()
    {
        image.color = new Color(image.color.r, image.color.g, image.color.b, 0.25f);
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
