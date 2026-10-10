using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class PlayableLeaderIcons : MonoBehaviour
{
    // public GameObject playerLeaderIconPrefab;
    public PlayableLeaderIcon currentPlayerPlayableIcon;
    public PlayableLeaderIcon competitor1PlayableIcon;
    public PlayableLeaderIcon competitor2PlayableIcon;

    public List<PlayableLeaderIcon> playableLeaderIcons;
    public Game game;

    private void Awake()
    {
        EnsurePlayableLeaderIcons();
    }

    void Start()
    {
        if(game == null) game = Game.Instance;
        EnsurePlayableLeaderIcons();
        if (game != null && game.currentlyPlaying != null)
        {
            HighlightCurrentlyPlaying(game.currentlyPlaying);
        }
    }

    public void Instantiate(PlayableLeader leader)
    {
        if(game.player == leader)
        {            
            currentPlayerPlayableIcon.Initialize(leader);    
        } 
        else
        {
            if(!competitor1PlayableIcon.IsInitialized())
            {
                competitor1PlayableIcon.Initialize(leader);    
            } else
            {
                competitor2PlayableIcon.Initialize(leader);    
            }
        }

        if (game != null && game.currentlyPlaying != null)
        {
            HighlightCurrentlyPlaying(game.currentlyPlaying);
        }
    }

    public void HighlightCurrentlyPlaying(PlayableLeader currentlyPlaying)
    {
        EnsurePlayableLeaderIcons();
        for(int i=0;i<playableLeaderIcons.Count;i++)
        {
            PlayableLeaderIcon playableLeaderIcon = playableLeaderIcons[i];
            if (playableLeaderIcon == null) continue;
            PlayableLeader playableLeader = playableLeaderIcon.playableLeader;
            if (playableLeader == currentlyPlaying)
            {
                playableLeaderIcon.SetCurrentlyPlayingEffect();
            } else
            {
                playableLeaderIcon.RemoveCurrentlyPlayingEffect();
            }
        }
    }

    public void AddDeadIcon(PlayableLeader deadPlayer)
    {
        EnsurePlayableLeaderIcons();
        for (int i = 0; i < playableLeaderIcons.Count; i++)
        {
            PlayableLeaderIcon playableLeaderIcon = playableLeaderIcons[i];
            if (playableLeaderIcon != null && playableLeaderIcon.playableLeader != null && playableLeaderIcon.playableLeader == deadPlayer)
            {
                playableLeaderIcon.SetDead();
                return;
            }
        }
    }

    public void RefreshVictoryPointsFor(PlayableLeader leader, int points)
    {
        if (leader == null) return;
        EnsurePlayableLeaderIcons();
        for (int i = 0; i < playableLeaderIcons.Count; i++)
        {
            PlayableLeaderIcon playableLeaderIcon = playableLeaderIcons[i];
            if (playableLeaderIcon != null && playableLeaderIcon.playableLeader != null && !playableLeaderIcon.playableLeader.killed && playableLeaderIcon.playableLeader == leader)
            {
                playableLeaderIcon.RefreshVictoryPoints(points);
                return;
            }
        }
    }

    public void RefreshVictoryPointsForAll()
    {
        EnsurePlayableLeaderIcons();
        foreach (PlayableLeaderIcon icon in playableLeaderIcons) if (icon != null) icon.EnsurePortrait();
        for (int i = 0; i < playableLeaderIcons.Count; i++)
        {
            PlayableLeaderIcon icon = playableLeaderIcons[i];
            if (icon != null && icon.playableLeader != null && !icon.playableLeader.killed && icon.playableLeader.victoryPoints != null)
            {
                icon.RefreshVictoryPoints(icon.playableLeader.victoryPoints.RelativeScore);
            }
        }
    }

    public void UpdateVictoryPointColors()
    {
        EnsurePlayableLeaderIcons();
        if (playableLeaderIcons == null || playableLeaderIcons.Count == 0) return;

        for (int i = 0; i < playableLeaderIcons.Count; i++)
        {
            PlayableLeaderIcon icon = playableLeaderIcons[i];
            if (icon != null && icon.victoryPoints != null)
            {
                icon.victoryPoints.color = Color.white;
            }
        }

        List<PlayableLeaderIcon> ranked = playableLeaderIcons
            .Where(icon => icon != null && icon.playableLeader != null && !icon.playableLeader.killed && icon.victoryPoints != null)
            .OrderByDescending(icon => icon.DisplayScore(icon.playableLeader.victoryPoints != null ? icon.playableLeader.victoryPoints.RelativeScore : int.MinValue))
            .ThenByDescending(icon => icon.playableLeader.victoryPoints != null ? icon.playableLeader.victoryPoints.RelativeScore : int.MinValue)
            .ToList();

        if (ranked.Count > 0) ranked[0].victoryPoints.color = new Color(0.12f, 0.55f, 0.23f, 1f);
        if (ranked.Count > 1) ranked[1].victoryPoints.color = new Color(1f, 0.6f, 0.2f, 1f);
        if (ranked.Count > 2) ranked[2].victoryPoints.color = new Color(0.9f, 0.2f, 0.2f, 1f);
    }

    private void EnsurePlayableLeaderIcons()
    {
        if (playableLeaderIcons != null && playableLeaderIcons.Count > 0) return;
        playableLeaderIcons = new() {
            currentPlayerPlayableIcon,
            competitor1PlayableIcon,
            competitor2PlayableIcon
        };
    }
}
