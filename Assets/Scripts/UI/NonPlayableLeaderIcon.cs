using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Linq;
using System.Text;

public class NonPlayableLeaderIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("References")]
    public GraphicRaycaster raycaster;
    public CanvasGroup canvasGroup;
    public Image image;
    public CanvasGroup deadCanvasGroup;
    public Image border;
    public Image alignmentImage;
    
    [HideInInspector]
    public NonPlayableLeader nonPlayableLeader;
    
    private AlignmentEnum alignment;
    private string text = string.Empty;
    private bool isUnrevealed = true;

    private Game game;

    private Sprite leaderSprite;
    private bool tempRevealQueued = false;
    private Illustrations illustrations;

    void Awake()
    {
        illustrations = FindAnyObjectByType<Illustrations>();
    }

    public void Initialize(NonPlayableLeader leader)
    {
        game = Game.Instance;
        nonPlayableLeader = leader;
        leaderSprite = illustrations.GetIllustrationByName(leader.characterName);
        alignment = leader.alignment;
        text = $"<sprite name=\"{alignment}\">{leader.characterName}";
        alignmentImage.sprite = illustrations.GetIllustrationByName(leader.GetAlignment().ToString());
        raycaster.enabled = false;
        canvasGroup.alpha = 0;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isUnrevealed || PopupManager.IsShowing || BoardNavigator.IsNavigationInputLocked()) return;
        Sounds.Instance?.PlayUiHover();
        PlayableLeaderIcon leader = FindObjectsByType<PlayableLeaderIcon>().First(x => x.alignment == alignment);
        if (leader) leader.HighlighNonPlayableLeader(nonPlayableLeader.characterName, text);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Sounds.Instance?.PlayUiExit();
        PlayableLeaderIcon leader = FindObjectsByType<PlayableLeaderIcon>().First(x => x.alignment == alignment);
        if (leader) leader.Restore(nonPlayableLeader.characterName);
    }

    public void SetDead()
    {
        deadCanvasGroup.alpha = 1;
    }

    public AlignmentEnum GetAlignmentValue()
    {
        return alignment;
    }
    public void SetHired()
    {
        border.color = Color.white;
    }
    public void RevealToPlayer(bool ignoreIfAlreadyRevealed = false)
    {
        if(!ignoreIfAlreadyRevealed && !isUnrevealed) return;
        if (!game.IsPlayerCurrentlyPlaying()) return;
        PlayableLeader player = game.player;
        if (player != null)
        {
            Hex capitalHex = nonPlayableLeader.controlledPcs.FirstOrDefault(pc => pc != null && pc.isCapital)?.hex;
            if (capitalHex != null)
            {
                bool playerCanSee = player.visibleHexes.Contains(capitalHex) && capitalHex.IsHexSeen();
                if (!playerCanSee)
                {
                    player.AddTemporarySeenHexes(new[] { capitalHex });
                    if (!tempRevealQueued)
                    {
                        tempRevealQueued = true;
                        StartCoroutine(RefreshVisibleNextFrame(player));
                    }
                }
            }
        }
        canvasGroup.alpha = 1;
        raycaster.enabled = true;
        image.sprite = leaderSprite;
        image.color = Color.white;
        string alignment = nonPlayableLeader.alignment == AlignmentEnum.freePeople ? "a free people" : nonPlayableLeader.alignment == AlignmentEnum.darkServants ? "a dark servant" : "a neutral";
        StringBuilder sb = new($"You discovered {nonPlayableLeader.characterName}, {alignment} nation");
        sb.Append("<br><br>");
        bool hasHiddenCapital = nonPlayableLeader.controlledPcs.Any(pc => pc.isHidden && !pc.hiddenButRevealed);
        if (hasHiddenCapital)
        {
            sb.Append("We found their nation but cannot find a way into their capital. Issue `Reveal PC` to possibly reveal a path.<br><br>");
        }
        if (nonPlayableLeader.alignment == game.currentlyPlaying.alignment)
        {
            sb.Append(nonPlayableLeader.GetJoiningConditionsText(game.currentlyPlaying));
            sb.Append("<br><br>");
        }
        if (nonPlayableLeader.alignment != game.currentlyPlaying.alignment)
        {
            sb.Append("Only a leader of the same alignment can recruit this nation. You can still attack to weaken their forces.");
        }
        string popupTitle = $"{nonPlayableLeader.characterName} reveals themselves!";
        Illustrations popupIllustrations = FindAnyObjectByType<Illustrations>();
        Sprite playerPortrait = popupIllustrations != null ? popupIllustrations.GetIllustrationByName(player) : null;
        Sprite nationPortrait = popupIllustrations != null ? popupIllustrations.GetIllustrationByName(nonPlayableLeader) : null;
        EventIconsManager eventIcons = EventIconsManager.FindManager();
        if (eventIcons != null)
        {
            EventIcon icon = null;
            icon = eventIcons.AddEventIcon(
                EventIconType.LeaderRevealed,
                discardable: false,
                onOpen: () =>
                {
                    icon?.ConsumeAndDestroy();
                    PopupManager.ShowImmediate(popupTitle, playerPortrait, nationPortrait, sb.ToString(), true);
                },
                onRemove: null,
                characterPortrait: nationPortrait);
        }
        else
        {
            Debug.LogWarning($"Could not queue the discovery of '{nonPlayableLeader.characterName}': EventIconsManager was not found.");
        }

        LogManager.Log(LogCategory.Event, nonPlayableLeader.characterName, player.characterName, popupTitle);

        isUnrevealed = false;
    }

    private IEnumerator RefreshVisibleNextFrame(PlayableLeader player)
    {
        yield return null;
        if (player != null) player.RefreshVisibleHexesImmediate();
        tempRevealQueued = false;
    }
}
