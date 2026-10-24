using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The campaign's spine: each playable leader pursues a handful of authored objectives
/// (Resources/Campaign/CampaignObjectives.json) across an Age of a fixed number of turns.
/// A decisive objective wins the war at once; otherwise the leader with the most objective
/// points when the Age ends wins (victory points break ties). Eliminating every rival still wins.
/// Each leader also has milestones: smaller steps in their own style (raise an army, fill the
/// treasury, gather companions), measured as a gain over where they started, each feeding one
/// score aspect (ScoreAspects) and paying a little gold when first reached. They don't count
/// toward the objective score.
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
    // Each leader's measures when the war began (milestones count the gain since).
    private readonly Dictionary<string, int> baselines = new();
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
        foreach (CampaignLeaderObjectives entry in data.leaders)
            if (entry?.milestones != null) foreach (CampaignObjective m in entry.milestones) if (m != null) m.milestone = true;
        // Begin runs once the starting realms are on the board (a loaded game rebuilds the same
        // start before restoring the save), so this is where every leader began.
        baselines.Clear();
        foreach (PlayableLeader leader in AllLeaders())
            foreach (CampaignObjective m in GetMilestones(leader))
                baselines[BaselineKey(leader, m)] = Measure(leader, m);
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
        CheckMilestones();
        Changed?.Invoke();
        if (CheckDecisive()) return;
        if (turn >= AgeLength) ResolveEndOfAge();
    }

    public IReadOnlyList<CampaignObjective> GetObjectives(PlayableLeader leader) =>
        Entry(leader)?.objectives ?? (IReadOnlyList<CampaignObjective>)Array.Empty<CampaignObjective>();

    public IReadOnlyList<CampaignObjective> GetMilestones(PlayableLeader leader) =>
        Entry(leader)?.milestones ?? (IReadOnlyList<CampaignObjective>)Array.Empty<CampaignObjective>();

    private CampaignLeaderObjectives Entry(PlayableLeader leader)
    {
        string key = LeaderKey(leader);
        return data?.leaders?.FirstOrDefault(l => string.Equals(l.leader, key, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsMet(PlayableLeader leader, CampaignObjective objective)
    {
        if (leader == null || objective == null || leader.killed) return false;
        if (achievedOnce.Contains(StickyKey(leader, objective))) return true;
        if (objective.milestone) return false; // reached only once CheckMilestones records it
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

    /// <summary>Map hexes an objective is about (empty for count objectives).</summary>
    public System.Collections.Generic.List<Hex> GetTargetHexes(CampaignObjective o)
    {
        var result = new System.Collections.Generic.List<Hex>();
        if (o == null) return result;
        if (!string.IsNullOrWhiteSpace(o.pc)) { Hex h = FindPcHex(o.pc); if (h != null) result.Add(h); }
        if (o.pcs != null) foreach (string pc in o.pcs) { Hex h = FindPcHex(pc); if (h != null && !result.Contains(h)) result.Add(h); }
        return result;
    }

    /// <summary>One line telling the player how to make progress on an objective.</summary>
    public string HowTo(PlayableLeader leader, CampaignObjective o)
    {
        if (o == null) return string.Empty;
        bool met = IsMet(leader, o);
        if (o.milestone) return met ? "Reached." : $"{ProgressText(leader, o)}. {o.how}";
        switch (o.type)
        {
            case "ReachPC": return met ? "Reached." : "Move any of your characters onto it.";
            case "HoldPC":
            case "HoldAllPCs":
                if (met) return "Held. Keep an army there to defend it.";
                return o.side ? "Bring an army to its gates and play Siege — or help the allied realm that holds it." : "Bring an army to its gates and play Siege.";
            case "PCCount":
            {
                int have = leader.controlledPcs.Count(pc => pc != null && pc.owner == leader);
                return met ? $"Done ({have}/{o.count}). Hold on to them." : $"{have}/{o.count}. Found new PCs from PC cards, or take them with Siege.";
            }
            default: return string.Empty;
        }
    }

    // ---------------------------------------------------------------- milestones

    /// <summary>How far a leader has come on a milestone: the gain in its measure since the war began.</summary>
    public int Progress(PlayableLeader leader, CampaignObjective m)
    {
        if (leader == null || m == null) return 0;
        if (achievedOnce.Contains(StickyKey(leader, m))) return Mathf.Max(1, m.count);
        int start = baselines.TryGetValue(BaselineKey(leader, m), out int b) ? b : Measure(leader, m);
        return Mathf.Max(0, Measure(leader, m) - start);
    }

    public string ProgressText(PlayableLeader leader, CampaignObjective m) =>
        $"{Mathf.Min(Progress(leader, m), Mathf.Max(1, m.count))}/{Mathf.Max(1, m.count)} {MeasureUnit(m)}";

    public static ScoreAspect AspectOf(CampaignObjective m) =>
        Enum.TryParse(m?.aspect, true, out ScoreAspect aspect) ? aspect : ScoreAspect.Realm;

    // What a milestone counts, now.
    private static int Measure(PlayableLeader leader, CampaignObjective m)
    {
        if (leader == null || m == null) return 0;
        IEnumerable<Character> living = leader.controlledCharacters.Where(c => c != null && !c.killed);
        IEnumerable<Army> armies = living.Where(c => c.IsArmyCommander()).Select(c => c.GetArmy()).Where(a => a != null && !a.killed);
        IEnumerable<PC> pcs = leader.controlledPcs.Where(pc => pc != null && pc.owner == leader);
        switch (m.type)
        {
            case "Armies": return armies.Count();
            case "Troops": return armies.Sum(a => a.ma + a.ar + a.li + a.hi + a.lc + a.hc + a.ca + a.ws);
            case "Gold": return leader.goldAmount;
            case "Production": return leader.GetLeatherPerTurn() + leader.GetTimberPerTurn() + leader.GetIronPerTurn()
                + leader.GetSteelPerTurn() + leader.GetMithrilPerTurn() + leader.GetMountsPerTurn();
            case "Characters": return living.Count();
            case "Levels": return living.Sum(c => c.GetCommander() + c.GetAgent() + c.GetEmmissary() + c.GetMage());
            case "Artifacts": return living.Sum(c => c.objects.Count);
            case "PCs": return pcs.Count();
            case "Fortifications": return pcs.Sum(pc => Mathf.Max(0, (int)pc.fortSize));
            case "Aspect": return ScoreAspects.Score(leader, AspectOf(m));
            default: return 0;
        }
    }

    private static string MeasureUnit(CampaignObjective m) => m.type switch
    {
        "Armies" => "new armies",
        "Troops" => "more troops",
        "Gold" => "more gold",
        "Production" => "more goods a turn",
        "Characters" => "new characters",
        "Levels" => "skill levels gained",
        "Artifacts" => "new artifacts",
        "PCs" => "new PCs",
        "Fortifications" => "fortification levels",
        "Aspect" => $"{ScoreAspects.Name(AspectOf(m))} gained",
        _ => string.Empty,
    };

    /// <summary>Records milestones reached since the last check and pays their reward. Called
    /// whenever victory points are recalculated (after every action, at turn start, after a
    /// load) and at each new turn.</summary>
    public void CheckMilestones()
    {
        if (resolved || game == null || !game.started) return;
        bool any = false;
        foreach (PlayableLeader leader in AllLeaders())
        {
            if (leader.killed) continue;
            foreach (CampaignObjective m in GetMilestones(leader))
            {
                string key = StickyKey(leader, m);
                if (achievedOnce.Contains(key) || Progress(leader, m) < Mathf.Max(1, m.count)) continue;
                achievedOnce.Add(key);
                any = true;
                if (m.reward > 0) leader.AddGold(m.reward, showMessage: false);
                if (leader == game.player)
                {
                    string reward = m.reward > 0 ? $"  +{m.reward} <sprite name=\"gold\">" : string.Empty;
                    MessageDisplay.ShowMessage($"Milestone reached: {ScoreAspects.Icon(AspectOf(m))} {m.title}{reward}", UiTheme.Gold);
                }
            }
        }
        if (any) Changed?.Invoke();
    }

    private static string BaselineKey(PlayableLeader leader, CampaignObjective m) => $"{LeaderKey(leader)}/{m.type}/{m.aspect}";

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
    public List<CampaignObjective> milestones = new();
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
    // Milestones only (CampaignLeaderObjectives.milestones): type is the measure (Armies, Troops,
    // Gold, Production, Characters, Levels, Artifacts, PCs, Fortifications, Aspect), count the
    // gain over the start of the war, aspect the score aspect it feeds (and, for type Aspect, the
    // one measured), how the advice shown with it, reward the gold paid when first reached.
    [NonSerialized] public bool milestone;
    public string aspect;
    public string how;
    public int reward;
}
