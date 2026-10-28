using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The campaign's spine (Resources/Campaign/CampaignObjectives.json). Each playable leader
/// starts with the basic cards of every role and walks one short track of campaign steps (the
/// "milestones") per role: Emissary, Commander, Agent, Mage, Heroes. Steps are measured as a gain
/// over where the leader started, and only the first unreached step of each track is open.
/// Reaching one unlocks more cards of that same role (CardUnlockRule). Cards no step names (events
/// for anyone, environments) join when every track is done. Then the deck is complete and the
/// war's final objectives open:
/// a decisive objective wins at once (only for a leader whose chain is complete); otherwise the
/// leader with the most objective points when the Age ends wins (victory points break ties).
/// Eliminating every rival still wins. Each leader also names a champion who acts twice a turn.
/// </summary>
public class CampaignObjectives : MonoBehaviour
{
    public static CampaignObjectives Instance { get; private set; }

    public const string StartUnlockLabel = "Your starting deck";
    public const string CompleteUnlockLabel = "Every campaign track done";

    public event Action Changed;
    // Bumped whenever the set of unlocked cards may have changed (steps reached, load).
    public int UnlockVersion { get; private set; }
    // Bumped only when the player reaches a step in play (not on a load): the cards it grants,
    // and the tools of the steps it opens, are what the deck viewer shows as new.
    public int PlayerGrantVersion { get; private set; }
    /// <summary>The step the player reached last, for the deck viewer's "new cards" page.</summary>
    public CampaignObjective LastPlayerStep { get; private set; }

    public int AgeLength => data != null && data.ageLength > 0 ? data.ageLength : 60;
    public int TurnsLeft => Mathf.Max(0, AgeLength - (game != null ? game.turn : 0));

    private CampaignObjectivesData data;
    private Game game;
    private readonly Dictionary<string, Hex> pcHexByName = new(StringComparer.Ordinal);
    private readonly HashSet<string> achievedOnce = new();
    // Each leader's measures when the war began (milestones count the gain since).
    private readonly Dictionary<string, int> baselines = new();
    // A step's measure when it opened, per leader and step: a step asks for a gain from the
    // moment it is set ("gather 10 gold"), not since the war began. Saved with the reached steps.
    private readonly Dictionary<string, int> openedAt = new();
    private const string OpenedAtPrefix = "@";
    // Which unlock stage each card belongs to, per leader: -1 the starting deck, else the index
    // of the campaign step whose rules first name it (cards no rule names fall to the last step).
    private readonly Dictionary<string, int> unlockStageByCard = new(StringComparer.OrdinalIgnoreCase);
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
        openedAt.Clear();
        pcHexByName.Clear();
        unlockStageByCard.Clear();
        toolStageByCard.Clear();
        openOrMetCache.Clear();
        CampaignAI.Reset();
        nplByName.Clear();
        guardByName.Clear();
        UnlockVersion++;
        data = LoadData();
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

    // Each flavour (Mithrandir, Gandalf the White, Sharkey...) has its own campaign; an entry
    // without a variant is the leader-wide fallback.
    private CampaignLeaderObjectives Entry(PlayableLeader leader) => FindEntry(data, leader);

    private static CampaignLeaderObjectives FindEntry(CampaignObjectivesData source, PlayableLeader leader)
    {
        if (source?.leaders == null || leader == null) return null;
        string key = LeaderKey(leader);
        string variant = VariantKey(leader);
        CampaignLeaderObjectives fallback = null;
        foreach (CampaignLeaderObjectives l in source.leaders)
        {
            if (l == null || !string.Equals(l.leader, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(l.variant)) { fallback ??= l; continue; }
            if (string.Equals(l.variant, variant, StringComparison.OrdinalIgnoreCase)) return l;
        }
        return fallback;
    }

    public static string VariantKey(PlayableLeader leader) => leader?.GetSelectedSubdeckId() ?? string.Empty;

    private static CampaignObjectivesData LoadData()
    {
        TextAsset json = Resources.Load<TextAsset>("Campaign/CampaignObjectives");
        return json != null ? JsonUtility.FromJson<CampaignObjectivesData>(json.text) : new CampaignObjectivesData();
    }

    /// <summary>Every playable leader begins as their avatar alone (see NationSpawner.StripToAvatars).</summary>
    public static bool AvatarStart => avatarStartCached ??= LoadData().avatarStart;
    private static bool? avatarStartCached;

    /// <summary>The flavour's campaign entry straight from the data, before the campaign begins
    /// (used at setup for the avatar's starting place).</summary>
    public static CampaignLeaderObjectives LoadEntry(PlayableLeader leader) => FindEntry(LoadData(), leader);

    public string CampaignTitle(PlayableLeader leader) => Entry(leader)?.title;

    // ---------------------------------------------------------------- campaign chain

    /// <summary>Whether this leader plays the campaign chain (starts with part of the deck).</summary>
    public bool HasCampaign(PlayableLeader leader) => leader != null && GetMilestones(leader).Count > 0;

    /// <summary>The open campaign steps: the first unreached step of each track, in track order.</summary>
    public List<CampaignObjective> ActiveMilestones(PlayableLeader leader)
    {
        var result = new List<CampaignObjective>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // tracks with an open step found
        foreach (CampaignObjective m in GetMilestones(leader))
        {
            if (done.Contains(TrackOf(m)) || IsMet(leader, m)) continue;
            done.Add(TrackOf(m));
            // A chapter opens only once the steps it follows are reached.
            if (!PrerequisitesMet(leader, m)) continue;
            result.Add(m);
        }
        return result;
    }

    public bool PrerequisitesMet(PlayableLeader leader, CampaignObjective m)
    {
        if (m?.after == null || m.after.Length == 0) return true;
        foreach (string id in m.after)
        {
            CampaignObjective before = GetMilestones(leader).FirstOrDefault(s => string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase));
            if (before != null && !IsMet(leader, before)) return false;
        }
        return true;
    }

    public CampaignObjective FindMilestone(PlayableLeader leader, string id) =>
        GetMilestones(leader).FirstOrDefault(s => string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase));

    public static string TrackOf(CampaignObjective m) => string.IsNullOrWhiteSpace(m?.track) ? "Campaign" : m.track;

    /// <summary>Whether a character's skills suit a track (Heroes and untracked steps suit anyone).</summary>
    public static bool SuitsTrack(Character c, CampaignObjective m)
    {
        if (c == null) return false;
        return TrackOf(m).ToLowerInvariant() switch
        {
            "commander" => c.GetCommander() > 0,
            "agent" => c.GetAgent() > 0,
            "emissary" => c.GetEmmissary() > 0,
            "mage" => c.GetMage() > 0,
            _ => true,
        };
    }

    public int TrackLength(PlayableLeader leader, CampaignObjective m) =>
        GetMilestones(leader).Count(s => string.Equals(TrackOf(s), TrackOf(m), StringComparison.OrdinalIgnoreCase));

    public int TrackPosition(PlayableLeader leader, CampaignObjective m) =>
        GetMilestones(leader).Where(s => string.Equals(TrackOf(s), TrackOf(m), StringComparison.OrdinalIgnoreCase)).ToList().IndexOf(m) + 1;

    public bool IsActive(PlayableLeader leader, CampaignObjective m) => ActiveMilestones(leader).Contains(m);

    /// <summary>Every step reached: the deck is complete and the war's final objectives are open.</summary>
    public bool IsChainComplete(PlayableLeader leader) => GetMilestones(leader).All(m => IsMet(leader, m));

    public int ReachedCount(PlayableLeader leader) => GetMilestones(leader).Count(m => IsMet(leader, m));

    /// <summary>
    /// The nation's champion may act twice a turn: the leader named as "champion" in the
    /// campaign data, or the leader themselves when none is named.
    /// </summary>
    public static bool IsChampion(Character character)
    {
        if (Instance == null || character == null) return false;
        if (character.GetOwner() is not PlayableLeader owner) return false;
        CampaignLeaderObjectives entry = Instance.Entry(owner);
        if (entry == null) return false;
        if (string.IsNullOrWhiteSpace(entry.champion)) return character == owner;
        return CardNameUtility.Equals(character.characterName, entry.champion);
    }

    /// <summary>
    /// The unlock stage a card belongs to for this leader: -1 for the starting deck, the index
    /// of the campaign step that unlocks it, or the step count for cards that join once every
    /// track is done. <paramref name="source"/> is where the
    /// card comes from in the leader's deck setup: "base", "variant" or "shared".
    /// </summary>
    public int UnlockStage(PlayableLeader leader, CardData card, string source)
    {
        IReadOnlyList<CampaignObjective> steps = GetMilestones(leader);
        if (card == null || steps.Count == 0) return -1;
        string key = $"{LeaderKey(leader)}/{source}/{card.name}";
        if (unlockStageByCard.TryGetValue(key, out int stage)) return stage;
        CampaignLeaderObjectives entry = Entry(leader);
        stage = steps.Count;
        if (entry.start != null && entry.start.Any(r => r != null && r.Matches(card, source))) stage = -1;
        else
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].unlocks == null || !steps[i].unlocks.Any(r => r != null && r.Matches(card, source))) continue;
                stage = i;
                break;
            }
        }
        unlockStageByCard[key] = stage;
        return stage;
    }

    /// <summary>Whether the leader may play this card yet. Leaders without a campaign chain have every card.</summary>
    public bool IsCardUnlocked(PlayableLeader leader, CardData card, string source)
    {
        if (!HasCampaign(leader) || card == null) return true;
        return IsAlreadyInEffect(leader, card) || IsGranted(leader, card, source);
    }

    /// <summary>Whether the campaign has handed this card out: the starting deck, a tool of a
    /// step that is open or reached, or the reward of a reached step.</summary>
    public bool IsGranted(PlayableLeader leader, CardData card, string source)
    {
        if (!HasCampaign(leader) || card == null) return true;
        IReadOnlyList<CampaignObjective> steps = GetMilestones(leader);
        int tool = ToolStage(leader, card, source);
        if (tool >= 0 && IsOpenOrMet(leader, tool)) return true;
        int stage = UnlockStage(leader, card, source);
        if (stage >= steps.Count) return IsChainComplete(leader) && Released(leader, card);
        return stage < 0 || IsMet(leader, steps[stage]);
    }

    /// <summary>What unlocks a card: the starting deck or a campaign step's title.</summary>
    public string UnlockedByLabel(PlayableLeader leader, CardData card, string source)
    {
        if (!HasCampaign(leader) || card == null) return StartUnlockLabel;
        if (IsAlreadyInEffect(leader, card)) return StartUnlockLabel;
        IReadOnlyList<CampaignObjective> steps = GetMilestones(leader);
        int stage = UnlockStage(leader, card, source);
        int tool = ToolStage(leader, card, source);
        if (stage < 0) return StartUnlockLabel;
        // A step's tools arrive when it opens, before any later reward that also names the card.
        if (tool >= 0 && tool <= stage) return $"Comes with {TrackOf(steps[tool])}: {steps[tool].title}";
        if (stage >= steps.Count) return CompleteUnlockLabel;
        return $"{TrackOf(steps[stage])}: {steps[stage].title}";
    }

    // The first step whose tools name this card (they join the deck while that step is open), or -1.
    private readonly Dictionary<string, int> toolStageByCard = new(StringComparer.OrdinalIgnoreCase);

    public int ToolStage(PlayableLeader leader, CardData card, string source)
    {
        IReadOnlyList<CampaignObjective> steps = GetMilestones(leader);
        if (card == null || steps.Count == 0) return -1;
        string key = $"{LeaderKey(leader)}/{source}/{card.name}";
        if (toolStageByCard.TryGetValue(key, out int stage)) return stage;
        stage = -1;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].tools == null || !steps[i].tools.Any(r => r != null && r.Matches(card, source))) continue;
            stage = i;
            break;
        }
        toolStageByCard[key] = stage;
        return stage;
    }

    // Steps open or reached, per leader, recomputed whenever the unlock state moves on.
    private readonly Dictionary<PlayableLeader, (int version, HashSet<int> steps)> openOrMetCache = new();

    private bool IsOpenOrMet(PlayableLeader leader, int index)
    {
        if (!openOrMetCache.TryGetValue(leader, out var cached) || cached.version != UnlockVersion)
        {
            IReadOnlyList<CampaignObjective> steps = GetMilestones(leader);
            List<CampaignObjective> open = ActiveMilestones(leader);
            var set = new HashSet<int>();
            for (int i = 0; i < steps.Count; i++) if (open.Contains(steps[i]) || IsMet(leader, steps[i])) set.Add(i);
            cached = (UnlockVersion, set);
            openOrMetCache[leader] = cached;
        }
        return cached.steps.Contains(index);
    }

    // Cards whose subject is already yours are always granted: the PCs you hold and the lands
    // they stand in (applied automatically), and the characters in your service (whose card
    // then works as their Inspiration at their home PC).
    private static bool IsAlreadyInEffect(PlayableLeader leader, CardData card)
    {
        switch (card.GetCardType())
        {
            case CardTypeEnum.PC:
                return leader.controlledPcs.Any(pc => pc != null && pc.owner == leader && CardNameUtility.Equals(pc.pcName, card.name));
            case CardTypeEnum.Land:
            {
                DeckManager deck = DeckManager.Instance;
                return deck != null && leader.controlledPcs.Any(pc => pc != null && pc.owner == leader
                    && CardNameUtility.Equals(deck.ResolveRegionForPc(pc), card.name));
            }
            case CardTypeEnum.Character:
                return leader.controlledCharacters.Any(c => c != null && !c.killed && CardNameUtility.Equals(c.characterName, card.name));
            default:
                return false;
        }
    }

    /// <summary>The place worth flying to for a campaign step: its authored place; for recruiting,
    /// the nearest home of a hero whose card you hold; otherwise your capital.</summary>
    public Hex GetFocusHex(PlayableLeader leader, CampaignObjective o)
    {
        if (o != null && o.type == "Recruit") { Hex at = RecruitHex(leader, o); if (at != null) return at; }
        List<Hex> targets = GetTargetHexes(o);
        if (targets.Count > 0) return targets[0];
        if (leader == null) return null;
        if (o != null && o.type == "Characters" && DeckManager.Instance != null && leader.hex != null)
        {
            Vector3 from = leader.hex.transform.position;
            Hex nearest = DeckManager.Instance.GetHand(leader)
                .Where(c => c != null && c.GetCardType() == CardTypeEnum.Character && !string.IsNullOrWhiteSpace(c.startingPC)
                    && !leader.controlledCharacters.Any(ch => ch != null && !ch.killed && CardNameUtility.Equals(ch.characterName, c.name)))
                .Select(c => FindPcHex(c.startingPC))
                .Where(h => h != null)
                .OrderBy(h => (h.transform.position - from).sqrMagnitude)
                .FirstOrDefault();
            if (nearest != null) return nearest;
        }
        return leader.hex;
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
        if (o.type == "Recruit")
        {
            Hex at = RecruitHex(game != null ? game.player : null, o);
            if (at != null) result.Add(at);
            return result;
        }
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

    /// <summary>How far a leader has come on a milestone: the gain in its measure since the step opened (or since the war began, before it is first checked).</summary>
    public int Progress(PlayableLeader leader, CampaignObjective m)
    {
        if (leader == null || m == null) return 0;
        if (achievedOnce.Contains(StickyKey(leader, m))) return Mathf.Max(1, m.count);
        if (IsAbsolute(m)) return AbsoluteMeasure(leader, m);
        int start = openedAt.TryGetValue(StickyKey(leader, m), out int o) ? o
            : baselines.TryGetValue(BaselineKey(leader, m), out int b) ? b : Measure(leader, m);
        return Mathf.Max(0, Measure(leader, m) - start);
    }

    public string ProgressText(PlayableLeader leader, CampaignObjective m)
    {
        if (IsAbsolute(m)) return Progress(leader, m) >= 1 ? "done" : m.type switch
        {
            "ReachPC" => "not reached",
            "HoldPC" => "not held",
            "Defeat" => "still stands",
            _ => "not yet",
        };
        return $"{Mathf.Min(Progress(leader, m), Mathf.Max(1, m.count))}/{Mathf.Max(1, m.count)} {MeasureUnit(m)}";
    }

    // Story steps are about a place or a person, not a gain since the start of the war:
    // ReachPC (any of your characters stands there), HoldPC (you own it), Recruit (that
    // character serves you: recruited from their card, or their whole realm won over).
    public static bool IsAbsolute(CampaignObjective m) => m != null && (m.type == "ReachPC" || m.type == "HoldPC" || m.type == "Recruit" || m.type == "Defeat");

    private int AbsoluteMeasure(PlayableLeader leader, CampaignObjective m) => m.type switch
    {
        "ReachPC" => Reached(leader, m.pc) ? 1 : 0,
        "HoldPC" => Holds(leader, m.pc, false) ? 1 : 0,
        // A realm step is also done once its capital is yours (the realm joined you), even when
        // the realm's leader is a different instance than the characters it fields.
        "Recruit" => Serves(leader, m.character) || !string.IsNullOrWhiteSpace(m.pc) && Holds(leader, m.pc, false) ? 1 : 0,
        "Defeat" => IsDefeated(FindGuard(m.character)) ? 1 : 0,
        _ => 0,
    };

    public static bool Serves(PlayableLeader leader, string characterName) =>
        leader != null && !string.IsNullOrWhiteSpace(characterName)
        && leader.controlledCharacters.Any(c => c != null && !c.killed && CardNameUtility.Equals(c.characterName, characterName));

    /// <summary>The non-playable leader a Recruit step names, when it is one (won over at their
    /// capital with A Friend or Three) rather than a character recruited from their card.</summary>
    public static NonPlayableLeader FindNpl(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (nplByName.TryGetValue(name, out NonPlayableLeader cached) && cached != null && !cached.killed) return cached;
        // A realm can field several characters under its leader's name (two Beoraborns); the one
        // holding the capital is the realm.
        NonPlayableLeader found = FindObjectsByType<NonPlayableLeader>(FindObjectsSortMode.None)
            .Where(n => n != null && !n.killed && CardNameUtility.Equals(n.characterName, name))
            .OrderByDescending(n => n.controlledPcs.Any(pc => pc != null && pc.isCapital && pc.owner == n))
            .ThenByDescending(n => n.controlledPcs.Count)
            .FirstOrDefault();
        nplByName[name] = found;
        return found;
    }

    private static readonly Dictionary<string, NonPlayableLeader> nplByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The guard a Defeat step names: the enemy the scenario set by this flavour's
    /// start (it holds its post). Null once it is gone from the board.</summary>
    public static Character FindGuard(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (guardByName.TryGetValue(name, out Character cached)) return cached;
        Character found = FindObjectsByType<Character>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c != null && c.holdsPost && !c.killed && CardNameUtility.Equals(c.characterName, name));
        // Remember only a guard actually found: until then (and after a load where it already
        // fell) the step reads as done.
        if (found != null) guardByName[name] = found;
        return found;
    }

    private static readonly Dictionary<string, Character> guardByName = new(StringComparer.OrdinalIgnoreCase);

    // Beaten: killed, or its host destroyed (a captain without an army no longer bars the way).
    private static bool IsDefeated(Character guard) =>
        guard == null || guard.killed || guard.hex == null || !guard.IsArmyCommander();

    /// <summary>Where a Recruit step is done: the realm's capital for a leader, else the
    /// character card's home PC.</summary>
    public Hex RecruitHex(PlayableLeader leader, CampaignObjective m)
    {
        if (m == null || string.IsNullOrWhiteSpace(m.character)) return null;
        if (!string.IsNullOrWhiteSpace(m.pc)) return FindPcHex(m.pc);
        NonPlayableLeader npl = FindNpl(m.character);
        if (npl != null)
        {
            PC capital = npl.controlledPcs.FirstOrDefault(pc => pc != null && pc.isCapital && pc.owner == npl)
                ?? npl.controlledPcs.FirstOrDefault(pc => pc != null && pc.owner == npl);
            if (capital?.hex != null) return capital.hex;
            return npl.hex;
        }
        CardData card = RecruitCard(leader, m);
        return card != null ? FindPcHex(card.startingPC) : null;
    }

    /// <summary>The character card a Recruit step plays, if the leader's deck has one.</summary>
    public CardData RecruitCard(PlayableLeader leader, CampaignObjective m)
    {
        DeckManager deck = DeckManager.Instance;
        if (deck == null || leader == null || m == null || string.IsNullOrWhiteSpace(m.character)) return null;
        return deck.GetCatalog(leader)
            .FirstOrDefault(c => c != null && c.GetCardType() == CardTypeEnum.Character && CardNameUtility.Equals(c.name, m.character));
    }

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
            case "CommanderLevels": return living.Sum(c => c.GetCommander());
            case "AgentLevels": return living.Sum(c => c.GetAgent());
            case "EmissaryLevels": return living.Sum(c => c.GetEmmissary());
            case "MageLevels": return living.Sum(c => c.GetMage());
            case "Loyalty": return pcs.Sum(pc => pc.loyalty);
            case "Captured": return pcs.Count(pc => pc.acquisitionType == PCAcquisitionType.CapturedByForce);
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
        "CommanderLevels" => "commander levels gained",
        "AgentLevels" => "agent levels gained",
        "EmissaryLevels" => "emissary levels gained",
        "MageLevels" => "mage levels gained",
        "Loyalty" => "loyalty gained",
        "Captured" => "PCs taken by force",
        "Artifacts" => "new artifacts",
        "PCs" => "new PCs",
        "Fortifications" => "fortification levels",
        "Aspect" => $"{ScoreAspects.Name(AspectOf(m))} gained",
        _ => string.Empty,
    };

    /// <summary>Records the open campaign steps reached since the last check and unlocks their
    /// cards. Called whenever victory points are recalculated (after every action, at turn start,
    /// after a load) and at each new turn.</summary>
    public void CheckMilestones()
    {
        if (resolved || game == null || !game.started) return;
        bool any = false;
        foreach (PlayableLeader leader in AllLeaders())
        {
            if (leader.killed) continue;
            // Only the open steps can be reached; reaching one opens the next.
            foreach (CampaignObjective m in ActiveMilestones(leader))
            {
                string key = StickyKey(leader, m);
                if (!IsAbsolute(m) && !openedAt.ContainsKey(key))
                {
                    openedAt[key] = Measure(leader, m);
                    if (m.type == "Gold") BringGoodsToMarket(leader);
                    if (m.type == "Armies") OpenArmoury(leader, m);
                }
                if (Progress(leader, m) < Mathf.Max(1, m.count)) continue;
                achievedOnce.Add(key);
                any = true;
                UnlockVersion++;
                if (m.reward > 0) leader.AddGold(m.reward, showMessage: false);
                if (leader == game.player)
                {
                    PlayerGrantVersion++;
                    LastPlayerStep = m;
                    int cards = DeckManager.Instance != null ? DeckManager.Instance.CountCardsUnlockedBy(leader, m) : 0;
                    string unlocked = cards > 0 ? $"  ·  {cards} card{(cards == 1 ? "" : "s")} join your deck" : string.Empty;
                    string done = IsChainComplete(leader) ? "  ·  The campaign is done: the war's final objectives are open, and the rest of your deck joins over the coming turns" : string.Empty;
                    MessageDisplay.ShowMessage($"{TrackOf(m)} step reached: {m.title}{unlocked}{done}", UiTheme.Gold);
                    // Name what joined: a short list reads better than a count.
                    if (cards > 0 && DeckManager.Instance != null)
                    {
                        var names = DeckManager.Instance.GetCardsUnlockedBy(leader, m).Select(c => DisplayNames.Format(c.name)).Distinct().ToList();
                        string list = string.Join(", ", names.Take(6)) + (names.Count > 6 ? $" and {names.Count - 6} more" : string.Empty);
                        MessageDisplay.ShowMessage($"New cards: {list}. Press C to see them.", UiTheme.Text);
                    }
                    Sounds.Instance?.PlayActionSuccess(m.title);
                }
            }
        }
        if (ReleaseRestOfDeck()) any = true;
        if (any) Changed?.Invoke();
    }

    // ── The rest of the deck ────────────────────────────────────────────────────────────────
    // Cards no step names join once the campaign is done, not all at once: a share each turn
    // for ReleaseTurns turns (which share is fixed per card name, so it is the same after a load).
    private const int ReleaseTurns = 10;
    private readonly Dictionary<PlayableLeader, int> releasedOnTurn = new();

    private string DoneKey(PlayableLeader leader) => $"{LeaderKey(leader)}/__done";

    private int TurnsSinceDone(PlayableLeader leader) =>
        openedAt.TryGetValue(DoneKey(leader), out int doneTurn) && game != null ? game.turn - doneTurn + 1 : ReleaseTurns;

    private bool Released(PlayableLeader leader, CardData card)
    {
        int turns = TurnsSinceDone(leader);
        if (turns >= ReleaseTurns) return true;
        uint hash = 2166136261;
        foreach (char ch in (card.name ?? string.Empty).ToLowerInvariant()) hash = (hash ^ ch) * 16777619;
        return (hash % 1000) / 1000f < turns / (float)ReleaseTurns;
    }

    // Notes the turn each leader's campaign was done, and each new turn of the release moves the
    // unlock state on (the deck viewer then shows the cards that joined).
    private bool ReleaseRestOfDeck()
    {
        if (game == null) return false;
        bool moved = false;
        foreach (PlayableLeader leader in AllLeaders())
        {
            if (leader.killed || !HasCampaign(leader) || !IsChainComplete(leader)) continue;
            if (!openedAt.ContainsKey(DoneKey(leader))) openedAt[DoneKey(leader)] = game.turn;
            if (TurnsSinceDone(leader) > ReleaseTurns) continue;
            if (releasedOnTurn.TryGetValue(leader, out int last) && last == game.turn) continue;
            releasedOnTurn[leader] = game.turn;
            if (TurnsSinceDone(leader) <= 1) continue; // the first share came with the last step
            UnlockVersion++;
            moved = true;
            if (leader == game.player)
            {
                PlayerGrantVersion++;
                LastPlayerStep = null;
            }
        }
        return moved;
    }

    // A trade step opens: if nothing can be sold yet (a Sell card takes 5 of one good), a
    // cartload of what the realm has most of arrives, so the step starts with a card to play.
    private const int SellLot = 5;
    private static readonly ProducesEnum[] Goods = { ProducesEnum.leather, ProducesEnum.timber, ProducesEnum.mounts, ProducesEnum.iron, ProducesEnum.steel };

    // A host step opens: the armoury tops the stores up to what the step's cheapest army card
    // costs, so the first army can be raised at once (later ones are paid for as usual).
    private void OpenArmoury(PlayableLeader leader, CampaignObjective m)
    {
        DeckManager deck = DeckManager.Instance;
        if (deck == null) return;
        int index = GetMilestones(leader).ToList().IndexOf(m);
        CardData army = deck.GetGrantedCards(leader)
            .Where(c => c.GetCardType() == CardTypeEnum.Army && ToolStage(leader, c, deck.CardSource(c)) == index)
            .OrderBy(c => c.GetTotalGoldCost() + c.leatherRequired + c.timberRequired + c.ironRequired + c.mountsRequired + c.steelRequired)
            .FirstOrDefault();
        if (army == null) return;
        var added = new List<string>();
        void Top(ProducesEnum good, int need)
        {
            int lack = need - leader.GetResourceAmount(good);
            if (lack <= 0) return;
            leader.AddResource(good, lack, showMessage: false);
            added.Add($"{lack}<sprite name=\"{good}\">");
        }
        Top(ProducesEnum.gold, army.GetTotalGoldCost());
        Top(ProducesEnum.leather, army.leatherRequired);
        Top(ProducesEnum.timber, army.timberRequired);
        Top(ProducesEnum.iron, army.ironRequired);
        Top(ProducesEnum.mounts, army.mountsRequired);
        Top(ProducesEnum.steel, army.steelRequired);
        if (leader == game.player && added.Count > 0)
            MessageDisplay.ShowMessage($"The armoury opens: +{string.Join(" ", added)}, enough to raise {army.name}.", UiTheme.Text);
    }

    private void BringGoodsToMarket(PlayableLeader leader)
    {
        if (Goods.Any(g => leader.GetResourceAmount(g) >= SellLot) || leader.mithrilAmount >= SellLot) return;
        ProducesEnum good = Goods.OrderByDescending(leader.GetResourceAmount).First();
        int amount = SellLot - leader.GetResourceAmount(good);
        leader.AddResource(good, amount, showMessage: false);
        if (leader == game.player)
            MessageDisplay.ShowMessage($"Traders bring {amount} <sprite name=\"{good}\"> to your markets: play a Sell card at one of your PCs.", UiTheme.Text);
    }

    private static string BaselineKey(PlayableLeader leader, CampaignObjective m) => $"{LeaderKey(leader)}/{m.type}/{m.aspect}";

    private bool Holds(PlayableLeader leader, string pcName, bool sideCounts)
    {
        PC pc = FindPcHex(pcName)?.GetPCData();
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
            // The war's final objectives only open once a leader's campaign chain is complete.
            if (leader.killed || !IsChainComplete(leader)) continue;
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
        if (pcHexByName.TryGetValue(pcName, out Hex cached) && cached != null && cached.GetPCData() != null) return cached;
        Board board = Board.Instance;
        if (board?.hexes == null) return null;
        Hex found = board.hexes.Values.FirstOrDefault(h => h != null && h.GetPCData() != null && CardNameUtility.Equals(h.GetPCData().pcName, pcName));
        if (found != null) pcHexByName[pcName] = found;
        return found;
    }

    public static string LeaderKey(PlayableLeader leader) =>
        leader?.GetBiome()?.characterName ?? leader?.characterName ?? string.Empty;

    public static string DisplayLeader(PlayableLeader leader) => LeaderKey(leader);

    private static string StickyKey(PlayableLeader leader, CampaignObjective o) => LeaderKey(leader) + "/" + o.id;

    // The reached steps, then each open step's starting measure as "@leader/step=value".
    public IEnumerable<string> GetAchievedOnceKeys() =>
        achievedOnce.Concat(openedAt.Select(kv => $"{OpenedAtPrefix}{kv.Key}={kv.Value}"));

    public void RestoreAchievedOnce(IEnumerable<string> keys)
    {
        achievedOnce.Clear();
        openedAt.Clear();
        if (keys != null)
            foreach (string k in keys)
            {
                int eq = k.LastIndexOf('=');
                if (k.StartsWith(OpenedAtPrefix) && eq > 0 && int.TryParse(k.Substring(eq + 1), out int value))
                    openedAt[k.Substring(OpenedAtPrefix.Length, eq - OpenedAtPrefix.Length)] = value;
                else achievedOnce.Add(k);
            }
        UnlockVersion++;
        Changed?.Invoke();
    }
}

[Serializable]
public class CampaignObjectivesData
{
    public int ageLength = 60;
    // Every playable leader starts as their avatar alone; the campaign grows the rest.
    public bool avatarStart;
    public List<CampaignLeaderObjectives> leaders = new();
}

[Serializable]
public class CampaignLeaderObjectives
{
    public string leader;
    // The flavour this campaign is for (the variant's subdeck id, e.g. "gandalf_the_white");
    // empty is the leader-wide fallback.
    public string variant;
    // The campaign's name, shown on the tracker ("The Elven Road").
    public string title;
    // Where the avatar begins (a PC name); empty keeps the scenario's hex.
    public string startPc;
    // The character who may act twice a turn; empty means the leader themselves.
    public string champion;
    // The cards the leader starts the war with; every other deck card waits for a campaign step.
    public List<CardUnlockRule> start = new();
    public List<CampaignObjective> objectives = new();
    // The campaign chain, in order (see CampaignObjectives.ActiveMilestones).
    public List<CampaignObjective> milestones = new();
}

/// <summary>
/// Names a group of deck cards. Every condition given must hold: types (CardTypeEnum names),
/// names, situation prefixes ("Agent", "EmmissaryAtOwnPC"...), skills (the role the card needs:
/// Commander, Agent, Emissary, Mage, or None for cards anyone plays) with maxLevel capping the
/// level required, and from ("base", "variant" or "shared" — where the card sits in the
/// leader's deck setup). "all" matches every card.
/// </summary>
[Serializable]
public class CardUnlockRule
{
    public bool all;
    public string[] types;
    public string[] names;
    public string[] situations;
    public string[] skills;
    public int maxLevel;
    public string from;

    public bool Matches(CardData card, string source)
    {
        if (card == null) return false;
        if (all) return true;
        if (!string.IsNullOrWhiteSpace(from) && !string.Equals(from, source, StringComparison.OrdinalIgnoreCase)) return false;
        if (types != null && types.Length > 0
            && !types.Any(t => string.Equals(t, card.GetCardType().ToString(), StringComparison.OrdinalIgnoreCase))) return false;
        if (names != null && names.Length > 0 && !names.Any(n => CardNameUtility.Equals(n, card.name))) return false;
        if (situations != null && situations.Length > 0
            && !situations.Any(s => !string.IsNullOrWhiteSpace(card.situation) && card.situation.StartsWith(s, StringComparison.OrdinalIgnoreCase))) return false;
        if (skills != null && skills.Length > 0
            && !skills.Any(s => RequiredLevel(card, s) > 0 || (string.Equals(s, "None", StringComparison.OrdinalIgnoreCase) && NeedsNoSkill(card)))) return false;
        if (maxLevel > 0 && skills != null && skills.Any(s => RequiredLevel(card, s) > maxLevel)) return false;
        return true;
    }

    private static int RequiredLevel(CardData card, string skill) => skill?.ToLowerInvariant() switch
    {
        "commander" => card.commanderSkillRequired,
        "agent" => card.agentSkillRequired,
        "emissary" => card.emissarySkillRequired,
        "mage" => card.mageSkillRequired,
        _ => 0,
    };

    private static bool NeedsNoSkill(CardData card) =>
        card.commanderSkillRequired <= 0 && card.agentSkillRequired <= 0 && card.emissarySkillRequired <= 0 && card.mageSkillRequired <= 0;
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
    // Campaign steps only (CampaignLeaderObjectives.milestones): type is the measure (Armies,
    // Troops, Gold, Production, Characters, Levels, Artifacts, PCs, Fortifications, Aspect), count
    // the gain over the start of the war, aspect a label (and, for type Aspect, the one measured),
    // how the advice shown with it, unlocks the deck cards it grants, pc (optional) the place the
    // camera flies to, reward any gold paid when first reached.
    [NonSerialized] public bool milestone;
    public string aspect;
    public string how;
    // What is happening in the tale at this step, told above the instruction in the guide.
    public string story;
    public int reward;
    public List<CardUnlockRule> unlocks = new();
    // The role track the step belongs to (Emissary, Commander, Agent, Mage, Heroes).
    public string track;
    // Steps (ids) that must be reached before this one opens: the campaign's chapters.
    public string[] after;
    // Recruit steps: the character who must serve you (a card's hero, or a realm's leader).
    public string character;
    // Cards that join the deck while the step is open, so it can be done (rewards come on reaching it).
    public List<CardUnlockRule> tools = new();
}
