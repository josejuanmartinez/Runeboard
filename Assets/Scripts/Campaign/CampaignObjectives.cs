using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The campaign's spine: each playable leader pursues a handful of authored objectives
/// (Resources/Campaign/CampaignObjectives.json) across an Age of a fixed number of turns.
/// A decisive objective wins the war at once; otherwise the leader with the most objective
/// points when the Age ends wins (victory points break ties). Eliminating every rival still wins.
/// </summary>
public class CampaignObjectives : MonoBehaviour
{
    public static CampaignObjectives Instance { get; private set; }

    public event Action Changed;

    public int AgeLength => data != null && data.ageLength > 0 ? data.ageLength : 60;
    public int TurnsLeft => Mathf.Max(0, AgeLength - (game != null ? game.turn : 0));

    private CampaignObjectivesData data;
    private Game game;
    private readonly Dictionary<string, Hex> pcHexByName = new(StringComparer.Ordinal);
    private readonly HashSet<string> achievedOnce = new();
    private bool resolved;
    private float nextDecisiveCheck;

    public static void Begin(Game game)
    {
        if (game == null) return;
        if (Instance == null) Instance = game.gameObject.AddComponent<CampaignObjectives>();
        Instance.Initialize(game);
    }

    private void Initialize(Game owner)
    {
        game = owner;
        resolved = false;
        achievedOnce.Clear();
        pcHexByName.Clear();
        TextAsset json = Resources.Load<TextAsset>("Campaign/CampaignObjectives");
        data = json != null ? JsonUtility.FromJson<CampaignObjectivesData>(json.text) : new CampaignObjectivesData();
        game.NewTurnStarted -= OnNewTurn;
        game.NewTurnStarted += OnNewTurn;
        CampaignHud.Ensure(this);
        Changed?.Invoke();
    }

    private void OnDestroy()
    {
        if (game != null) game.NewTurnStarted -= OnNewTurn;
        if (Instance == this) Instance = null;
    }

    // Decisive objectives (reaching Mount Doom, taking Minas Tirith) end the war the moment
    // they happen rather than waiting for the next turn boundary.
    private void Update()
    {
        if (resolved || game == null || !game.started || Time.unscaledTime < nextDecisiveCheck) return;
        nextDecisiveCheck = Time.unscaledTime + 0.5f;
        CheckDecisive();
    }

    private void OnNewTurn(int turn)
    {
        if (resolved) return;
        RecordStickyObjectives();
        Changed?.Invoke();
        if (CheckDecisive()) return;
        if (turn >= AgeLength) ResolveEndOfAge();
    }

    public IReadOnlyList<CampaignObjective> GetObjectives(PlayableLeader leader)
    {
        string key = LeaderKey(leader);
        CampaignLeaderObjectives entry = data?.leaders?.FirstOrDefault(l => string.Equals(l.leader, key, StringComparison.OrdinalIgnoreCase));
        return entry?.objectives ?? (IReadOnlyList<CampaignObjective>)Array.Empty<CampaignObjective>();
    }

    public bool IsMet(PlayableLeader leader, CampaignObjective objective)
    {
        if (leader == null || objective == null || leader.killed) return false;
        if (achievedOnce.Contains(StickyKey(leader, objective))) return true;
        switch (objective.type)
        {
            case "HoldPC": return Holds(leader, objective.pc, objective.side);
            case "HoldAllPCs": return objective.pcs != null && objective.pcs.Length > 0 && objective.pcs.All(pc => Holds(leader, pc, objective.side));
            case "PCCount": return leader.controlledPcs.Count(pc => pc != null && pc.owner == leader) >= objective.count;
            case "ReachPC": return Reached(leader, objective.pc);
            default: return false;
        }
    }

    public int Score(PlayableLeader leader) =>
        GetObjectives(leader).Where(o => !o.decisive && IsMet(leader, o)).Sum(o => o.points);

    public int MaxScore(PlayableLeader leader) => GetObjectives(leader).Where(o => !o.decisive).Sum(o => o.points);

    private bool Holds(PlayableLeader leader, string pcName, bool sideCounts)
    {
        PC pc = FindPcHex(pcName)?.GetPC();
        if (pc?.owner == null) return false;
        if (pc.owner == leader) return true;
        return sideCounts && pc.owner is NonPlayableLeader && !pc.owner.killed && pc.owner.GetAlignment() == leader.GetAlignment();
    }

    private bool Reached(PlayableLeader leader, string pcName)
    {
        Hex target = FindPcHex(pcName);
        return target != null && leader.controlledCharacters.Any(c => c != null && !c.killed && c.hex == target);
    }

    private void RecordStickyObjectives()
    {
        foreach (PlayableLeader leader in AllLeaders())
            foreach (CampaignObjective o in GetObjectives(leader))
                if (o.type == "ReachPC" && Reached(leader, o.pc)) achievedOnce.Add(StickyKey(leader, o));
    }

    private bool CheckDecisive()
    {
        foreach (PlayableLeader leader in AllLeaders())
        {
            if (leader.killed) continue;
            CampaignObjective decisive = GetObjectives(leader).FirstOrDefault(o => o.decisive && IsMet(leader, o));
            if (decisive == null) continue;
            Resolve(leader, $"{DisplayLeader(leader)} achieved \"{decisive.title}\".");
            return true;
        }
        return false;
    }

    private void ResolveEndOfAge()
    {
        PlayableLeader winner = AllLeaders()
            .Where(l => !l.killed)
            .OrderByDescending(Score)
            .ThenByDescending(l => l.victoryPoints != null ? l.victoryPoints.RawScore : 0)
            .FirstOrDefault();
        Resolve(winner, winner != null
            ? $"The Age has ended. {DisplayLeader(winner)} holds the most objectives ({Score(winner)} points)."
            : "The Age has ended.");
    }

    private void Resolve(PlayableLeader winner, string reason)
    {
        if (resolved) return;
        resolved = true;
        game.EndGame(winner != null && winner == game.player, reason);
    }

    /// <summary>Called by Game when the war ends by other means (elimination) so no objective fires afterwards.</summary>
    public void MarkResolved() => resolved = true;

    public IEnumerable<PlayableLeader> AllLeaders()
    {
        if (game == null) yield break;
        if (game.player != null) yield return game.player;
        if (game.competitors == null) yield break;
        foreach (PlayableLeader c in game.competitors) if (c != null) yield return c;
    }

    private Hex FindPcHex(string pcName)
    {
        if (string.IsNullOrWhiteSpace(pcName)) return null;
        if (pcHexByName.TryGetValue(pcName, out Hex cached) && cached != null && cached.GetPC() != null) return cached;
        Board board = Board.Instance;
        if (board?.hexes == null) return null;
        Hex found = board.hexes.Values.FirstOrDefault(h => h != null && h.GetPC() != null && CardNameUtility.Equals(h.GetPC().pcName, pcName));
        if (found != null) pcHexByName[pcName] = found;
        return found;
    }

    public static string LeaderKey(PlayableLeader leader) =>
        leader?.GetBiome()?.characterName ?? leader?.characterName ?? string.Empty;

    public static string DisplayLeader(PlayableLeader leader) => LeaderKey(leader);

    private static string StickyKey(PlayableLeader leader, CampaignObjective o) => LeaderKey(leader) + "/" + o.id;

    public IEnumerable<string> GetAchievedOnceKeys() => achievedOnce;
    public void RestoreAchievedOnce(IEnumerable<string> keys)
    {
        achievedOnce.Clear();
        if (keys != null) foreach (string k in keys) achievedOnce.Add(k);
        Changed?.Invoke();
    }
}

[Serializable]
public class CampaignObjectivesData
{
    public int ageLength = 60;
    public List<CampaignLeaderObjectives> leaders = new();
}

[Serializable]
public class CampaignLeaderObjectives
{
    public string leader;
    public List<CampaignObjective> objectives = new();
}

[Serializable]
public class CampaignObjective
{
    public string id;
    public string title;
    public string description;
    public string type;
    public string pc;
    public string[] pcs;
    public int count;
    public bool side;
    public int points;
    public bool decisive;
}
