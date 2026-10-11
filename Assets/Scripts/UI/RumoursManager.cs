using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public struct Rumour
{
    public Leader leader;
    public Character character;
    public string characterName;
    public string rumour;
    public Vector2Int v2;
    public bool seen;
}

public class RumoursManager : MonoBehaviour
{
    public static RumoursManager Instance { get; private set; }

    private List<Rumour> rumours = new();
    private List<Rumour> privateRumours = new();

    private Game game;
    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        game = Game.Instance;
    }

    public static void AddRumour(Rumour rumour, bool isPublic, bool logToWidget = true)
    {
        if (!EnsureInstance(nameof(AddRumour)))
            return;

        if (isPublic)
        {
            rumour.seen = false;
            Instance.rumours.Add(rumour);
            // Callers that already displayed this exact event another way (see
            // MessageDisplayNoUI.ShowMessage) pass logToWidget: false, so it's still recorded
            // here for GetRumours/spying without also duplicating that line in the log widget.
            if (logToWidget)
            {
                LogManager.Log(LogCategory.Rumour, rumour.leader?.characterName, rumour.characterName, rumour.rumour);
            }
        }
        else
        {
            // Strip location for private rumours from enemies so location isn't leaked
            Rumour sanitized = rumour;
            sanitized.v2 = default;
            sanitized.seen = false;
            Instance.privateRumours.Add(sanitized);
        }
    }

    /// <summary>
    /// Promote a rumour from the private pool into the public list, avoiding duplicates.
    /// Used for "doubled" characters that should always leak their actions.
    /// </summary>
    public static void PromoteRumourToPublic(Rumour rumour)
    {
        if (!EnsureInstance(nameof(PromoteRumourToPublic)))
            return;

        // Remove one matching private copy so we don't double-count later reveals
        int privateIndex = Instance.privateRumours.FindIndex(r =>
            r.leader == rumour.leader &&
            r.rumour == rumour.rumour);
        if (privateIndex >= 0)
        {
            Instance.privateRumours.RemoveAt(privateIndex);
        }

        // Skip if already public
        bool alreadyPublic = Instance.rumours.Exists(r =>
            r.leader == rumour.leader &&
            r.rumour == rumour.rumour &&
            r.v2 == rumour.v2);
        if (alreadyPublic) return;

        rumour.seen = false;
        Instance.rumours.Add(rumour);
        LogManager.Log(LogCategory.Rumour, rumour.leader?.characterName, rumour.characterName, rumour.rumour);
    }


    /// <summary>
    /// Moves the last `qty` private rumours into the public list,
    /// then updates the UI.
    /// </summary>
    public static int GetRumours(Leader requester, AlignmentEnum alignment, int enemyRumoursQty, int friendlyRumoursQty)
    {
        if (!EnsureInstance(nameof(GetRumours)))
            return 0;

        if (Instance.game == null) Instance.game = Game.Instance;
        // Rumours about leaders that no longer exist can't be attributed or shown.
        Instance.privateRumours.RemoveAll(r => r.leader == null);
        if (enemyRumoursQty + friendlyRumoursQty <= 0 || Instance.privateRumours.Count == 0)
            return 0;

        int enemyAvailable = 0;
        int friendlyAvailable = 0;
        foreach (Rumour rumour in Instance.privateRumours)
        {
            if (Instance.game != null && rumour.leader == Instance.game.player) continue;
            bool isFriendly = rumour.leader.alignment == alignment && rumour.leader.alignment != AlignmentEnum.neutral;
            if (isFriendly)
            {
                friendlyAvailable++;
            }
            else
            {
                enemyAvailable++;
            }
        }

        // Clamp qty so we don't ask for more than exist
        enemyRumoursQty = Mathf.Clamp(enemyRumoursQty, 0, enemyAvailable);
        friendlyRumoursQty = Mathf.Clamp(friendlyRumoursQty, 0, friendlyAvailable);

        int totalRumours = enemyRumoursQty + friendlyRumoursQty;

        // The public list and LogWidget are the human player's intelligence view. AI/NPL
        // RevealRumours actions may succeed, but must never promote their discoveries into it.
        if (Instance.game == null || requester != Instance.game.player) return totalRumours;

        List<int> toRemove = new();
        for(int i=Instance.privateRumours.Count-1; i>=0;i--)
        {
            if(enemyRumoursQty + friendlyRumoursQty <= 0) break;
            Rumour rumour = Instance.privateRumours[i];
            if(enemyRumoursQty > 0 && (rumour.leader.alignment != alignment || rumour.leader.alignment == AlignmentEnum.neutral))
            {
                AddRumour(rumour, true);
                toRemove.Add(i);
                enemyRumoursQty--;
            }
            else if(friendlyRumoursQty > 0 && rumour.leader.alignment == alignment && rumour.leader.alignment != AlignmentEnum.neutral)
            {
                AddRumour(rumour, true);
                toRemove.Add(i);
                friendlyRumoursQty--;
            }
        }
        
        toRemove.ForEach(x => Instance.privateRumours.RemoveAt(x));

        return totalRumours;
    }

    private static bool EnsureInstance(string caller)
    {
        if (Instance != null)
            return true;

        Instance = FindAnyObjectByType<RumoursManager>();
        if (Instance != null)
        {
            if (Instance.game == null)
            {
                Instance.game = Game.Instance;
            }
            return true;
        }

        Debug.LogWarning($"RumoursManager.{caller} called before the singleton instance was initialized.");
        return false;
    }
}
