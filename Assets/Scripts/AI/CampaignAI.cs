using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The AI's campaign play: an AI leader works its own flavour's open campaign steps the way a
/// player would. Each open step goes to the nearest living character suited to it (one step per
/// character); that character travels to the step's place and, once there, plays the card that
/// does it — the PC's card to claim or found it, the hero's card to recruit them, A Friend or
/// Three to win a realm over, an army card to raise a first host. Everything else is left to the
/// utility AI. A step that makes no progress for a few turns at its place is set aside for a
/// while, so a character never stands stuck on a goal it cannot reach.
/// </summary>
public static class CampaignAI
{
    private const int StuckTurnsBeforeBench = 4;
    private const int BenchTurns = 8;
    private const string AllegianceAction = "StateAllegiance";
    private const string AttackAction = "Attack";

    private static readonly Dictionary<string, int> turnsAtTarget = new();
    private static readonly Dictionary<string, int> benchedUntilTurn = new();
    private static readonly Dictionary<string, int> lastCountedTurn = new();

    private static int Turn => Game.Instance != null ? Game.Instance.turn : 0;
    private static string Key(PlayableLeader leader, CampaignObjective step) => $"{CampaignObjectives.LeaderKey(leader)}/{step.id}";

    /// <summary>Where the character's campaign step wants them this turn, or null (no step for them).</summary>
    public static Hex TargetFor(PlayableLeader leader, Character character)
    {
        CampaignObjective step = StepFor(leader, character);
        return step != null ? TargetHex(leader, character, step) : null;
    }

    /// <summary>Where the character should walk this turn toward its step: the furthest point of
    /// the whole route it can reach now. The board's pathing only looks one turn ahead and stalls
    /// behind mountains and gulfs on long journeys.</summary>
    public static Hex TravelTargetFor(PlayableLeader leader, Character character)
    {
        Hex goal = TargetFor(leader, character);
        if (goal == null || character?.hex == null || goal == character.hex) return goal;
        return Waypoint(character, goal) ?? goal;
    }

    private static readonly Dictionary<Character, (int turn, Hex goal, List<Vector2Int> route)> routes = new();

    private static Hex Waypoint(Character c, Hex goal)
    {
        Board board = Board.Instance;
        HexPathRenderer pathing = Object.FindAnyObjectByType<HexPathRenderer>();
        if (board == null || pathing == null) return null;

        if (!routes.TryGetValue(c, out var cached) || cached.turn != Turn || cached.goal != goal)
        {
            cached = (Turn, goal, pathing.FindPath(c.hex.v2, goal.v2, c, ignoreMovement: true));
            routes[c] = cached;
        }
        List<Vector2Int> route = cached.route;
        int from = route != null ? route.IndexOf(c.hex.v2) : -1;
        if (from < 0 || route[route.Count - 1] != goal.v2) return StepAshore(c, goal, pathing);

        // Every hex costs at least one point of movement, so only the next few route hexes can be
        // in reach; take the furthest one this turn's pathing can actually arrive at.
        int movement = c.GetMovementLeft();
        for (int i = Mathf.Min(route.Count - 1, from + movement); i > from; i--)
        {
            List<Vector2Int> leg = pathing.FindPath(c.hex.v2, route[i], c);
            if (leg != null && leg.Count > 0 && leg[leg.Count - 1] == route[i] && board.hexes.TryGetValue(route[i], out Hex waypoint))
                return waypoint;
        }
        return null;
    }

    // No whole route: the pathing never carries a land/water crossing past its first hex, so an
    // island hold (Tol Buruth) or a strait cuts every long route. Wade one hex at a time instead:
    // the neighbour nearest the goal that can be stepped onto, as long as it gets closer.
    private static Hex StepAshore(Character c, Hex goal, HexPathRenderer pathing)
    {
        float here = (c.hex.transform.position - goal.transform.position).sqrMagnitude;
        foreach (Hex next in c.hex.GetHexesInRadius(1)
            .Where(h => h != null && h != c.hex && (h.transform.position - goal.transform.position).sqrMagnitude < here)
            .OrderBy(h => (h.transform.position - goal.transform.position).sqrMagnitude))
        {
            List<Vector2Int> leg = pathing.FindPath(c.hex.v2, next.v2, c);
            if (leg != null && leg.Count > 0 && leg[leg.Count - 1] == next.v2) return next;
        }
        return null;
    }

    /// <summary>The open step this character works on, or null.</summary>
    public static CampaignObjective StepFor(PlayableLeader leader, Character character)
    {
        CampaignObjectives campaign = CampaignObjectives.Instance;
        if (campaign == null || leader == null || character == null || character.killed || !campaign.HasCampaign(leader)) return null;
        List<Character> free = leader.controlledCharacters.Where(c => c != null && !c.killed && c.hex != null).ToList();
        foreach (CampaignObjective step in campaign.ActiveMilestones(leader))
        {
            if (IsBenched(leader, step)) continue;
            Character best = free
                .Where(c => CanWork(leader, c, step))
                .Select(c => (c, target: TargetHex(leader, c, step)))
                .Where(x => x.target != null)
                .OrderBy(x => (x.target.transform.position - x.c.hex.transform.position).sqrMagnitude)
                .Select(x => x.c)
                .FirstOrDefault();
            if (best == null) continue;
            free.Remove(best);
            if (best == character) return step;
        }
        return null;
    }

    /// <summary>The character best placed to work a step (suited to it, nearest its place), or null.</summary>
    public static Character BestFor(PlayableLeader leader, CampaignObjective step)
    {
        if (leader == null || step == null || CampaignObjectives.Instance == null) return null;
        return leader.controlledCharacters
            .Where(c => c != null && !c.killed && c.hex != null && CanWork(leader, c, step))
            .Select(c => (c, target: TargetHex(leader, c, step)))
            .Where(x => x.target != null)
            .OrderBy(x => (x.target.transform.position - x.c.hex.transform.position).sqrMagnitude)
            .Select(x => x.c)
            .FirstOrDefault();
    }

    /// <summary>Where a character works a step (the same place the AI travels to), or null.</summary>
    public static Hex PlaceFor(PlayableLeader leader, Character c, CampaignObjective step) =>
        leader != null && c?.hex != null && step != null && CampaignObjectives.Instance != null ? TargetHex(leader, c, step) : null;

    private static bool CanWork(PlayableLeader leader, Character c, CampaignObjective step)
    {
        switch (step.type)
        {
            case "HoldPC":
            case "ReachPC":
                return true;
            case "Recruit":
                // Winning a realm over takes A Friend or Three (an emissary); a hero's card anyone can play.
                return CampaignObjectives.FindNpl(step.character) == null || c.GetEmmissary() >= 1;
            case "Armies":
                // A new army needs a commander who does not lead one yet.
                return c.GetCommander() > 0 && !c.IsArmyCommander();
            case "Troops":
                return c.GetCommander() > 0;
            case "Defeat":
                // Only a host can break the guard's; a captain without one (a first host lost)
                // raises another at home first.
                return c.IsArmyCommander() || c.GetCommander() > 0;
            case "Gold":
            case "Production":
                // The realm's income fills these on its own (selling goods speeds it up); nobody
                // needs to stand anywhere for them.
                return false;
            default:
                return CampaignObjectives.SuitsTrack(c, step);
        }
    }

    private static Hex TargetHex(PlayableLeader leader, Character c, CampaignObjective step)
    {
        CampaignObjectives campaign = CampaignObjectives.Instance;
        switch (step.type)
        {
            case "HoldPC":
            {
                Hex pcHex = campaign.GetTargetHexes(step).FirstOrDefault();
                return pcHex != null ? pcHex : FoundingHex(leader, c, step.pc);
            }
            case "ReachPC":
                return campaign.GetTargetHexes(step).FirstOrDefault();
            case "Recruit":
                return campaign.RecruitHex(leader, step);
            case "Defeat":
                return c.IsArmyCommander() ? CampaignObjectives.FindGuard(step.character)?.hex : NearestOwnPc(leader, c);
            case "Armies":
            case "Troops":
            case "Gold":
            case "Loyalty":
            case "Production":
            case "Fortifications":
                return NearestOwnPc(leader, c);
            default:
            {
                Hex focus = campaign.GetFocusHex(leader, step);
                return focus != null && focus != leader.hex ? focus : null;
            }
        }
    }

    private static Hex NearestOwnPc(PlayableLeader leader, Character c) =>
        leader.controlledPcs.Where(pc => pc != null && pc.owner == leader && pc.hex != null)
            .Select(pc => pc.hex)
            .OrderBy(h => (h.transform.position - c.hex.transform.position).sqrMagnitude)
            .FirstOrDefault();

    // A PC not yet on the map is founded by playing its card anywhere in its region.
    private static Hex FoundingHex(PlayableLeader leader, Character c, string pcName)
    {
        CardData card = PcCard(leader, pcName);
        Board board = Board.Instance;
        if (card == null || board?.hexes == null || string.IsNullOrWhiteSpace(card.region)) return null;
        string region = PcDescriptionBuilder.NormalizeLookupKey(card.region);
        return board.hexes.Values
            .Where(h => h != null && !h.IsWaterTerrain() && h.GetPCData() == null
                && PcDescriptionBuilder.NormalizeLookupKey(h.GetLandRegion() ?? string.Empty) == region)
            .OrderBy(h => (h.transform.position - c.hex.transform.position).sqrMagnitude)
            .FirstOrDefault();
    }

    private static CardData PcCard(PlayableLeader leader, string pcName) =>
        DeckManager.Instance?.GetCatalog(leader).FirstOrDefault(card => card != null
            && card.GetCardType() == CardTypeEnum.PC && CardNameUtility.Equals(card.name, pcName));

    /// <summary>Plays the card that does the character's step when they stand at its place.
    /// Returns whether a card was played.</summary>
    public static async Task<bool> TryActAsync(PlayableLeader leader, Character character, ActionsManager actionsManager)
    {
        CampaignObjectives campaign = CampaignObjectives.Instance;
        DeckManager deck = DeckManager.Instance;
        CampaignObjective step = StepFor(leader, character);
        if (step == null || campaign == null || deck == null || actionsManager == null) return false;
        Hex target = TargetHex(leader, character, step);
        if (target == null || character.hex != target) return false;

        bool played = false;
        try
        {
            switch (step.type)
            {
                case "HoldPC":
                {
                    PC pc = target.GetPCData();
                    if (pc == null || pc.owner == null) played = await PlayResourceCard(leader, character, PcCard(leader, step.pc), actionsManager);
                    if (!played && pc != null && pc.owner == null) played = await PlayAllegiance(leader, character, actionsManager);
                    break;
                }
                case "Recruit":
                    played = CampaignObjectives.FindNpl(step.character) != null
                        ? await PlayAllegiance(leader, character, actionsManager)
                        : Recruit(leader, character, campaign.RecruitCard(leader, step));
                    break;
                case "Armies":
                case "Troops":
                    if (!character.IsArmyCommander()) played = await PlayFirstArmy(leader, character, actionsManager);
                    break;
                case "Defeat":
                    if (!character.IsArmyCommander()) played = await PlayFirstArmy(leader, character, actionsManager);
                    // Only the guard: another host camped on its hex would join the fight.
                    else if (GuardStandsAlone(step, character)) played = await PlayAction(leader, character, AttackAction, actionsManager);
                    break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }

        campaign.CheckMilestones();
        if (step.type is "HoldPC" or "Recruit" or "Armies" or "Troops" or "Defeat") CountTurnAtTarget(leader, step);
        if (played) Debug.Log($"[CampaignAI] {leader.characterName}/{character.characterName} worked '{step.title}' at {target.v2}.");
        return played;
    }

    private static async Task<bool> PlayResourceCard(PlayableLeader leader, Character character, CardData card, ActionsManager actionsManager)
    {
        DeckManager deck = DeckManager.Instance;
        if (card == null || !deck.IsAvailable(leader, card) || !card.EvaluatePlayability(character)) return false;
        if (!deck.TryConsumeCard(leader, card.name, out CardData consumed)) return false;
        return await AITurnController.ExecuteCardEffectForAiAsync(consumed, character, actionsManager);
    }

    private static async Task<bool> PlayAllegiance(PlayableLeader leader, Character character, ActionsManager actionsManager)
    {
        DeckManager deck = DeckManager.Instance;
        CardData card = deck.GetCatalog(leader).FirstOrDefault(c => c != null && deck.IsAvailable(leader, c)
            && string.Equals(AITurnController.NormalizeActionRef(c.GetActionRef()), AllegianceAction, System.StringComparison.OrdinalIgnoreCase));
        if (card == null) { Debug.Log($"[CampaignAI] {character.characterName}: no A Friend or Three available"); return false; }
        bool playable = card.EvaluatePlayability(character);
        UtilityAIContext context = await AITurnController.ExecuteChosenCardAsync(leader, character, actionsManager, null, card, null, null);
        bool ok = context?.LastChosenAction != null && context.LastChosenAction.LastExecutionSucceeded;
        PC pc = character.hex?.GetPCData();
        Debug.Log($"[CampaignAI] {character.characterName} A Friend or Three at {pc?.pcName} (owner {pc?.owner?.characterName}, capital {pc?.isCapital}): playable={playable} executed={context?.LastChosenAction != null} ok={ok}");
        return ok;
    }

    private static bool GuardStandsAlone(CampaignObjective step, Character attacker)
    {
        Character guard = CampaignObjectives.FindGuard(step.character);
        Hex hex = guard?.hex;
        if (hex?.armies == null) return true;
        return !hex.armies.Any(a => a != null && !a.killed && a.commander != null && a.commander != guard
            && a.commander.GetOwner() != attacker.GetOwner());
    }

    // Plays the deck's card for an action (by its action ref) where the character stands.
    private static async Task<bool> PlayAction(PlayableLeader leader, Character character, string action, ActionsManager actionsManager)
    {
        DeckManager deck = DeckManager.Instance;
        CardData card = deck.GetCatalog(leader).FirstOrDefault(c => c != null && deck.IsAvailable(leader, c)
            && string.Equals(AITurnController.NormalizeActionRef(c.GetActionRef()), action, System.StringComparison.OrdinalIgnoreCase));
        if (card == null || !card.EvaluatePlayability(character)) return false;
        UtilityAIContext context = await AITurnController.ExecuteChosenCardAsync(leader, character, actionsManager, null, card, null, null);
        return context?.LastChosenAction != null && context.LastChosenAction.LastExecutionSucceeded;
    }

    private static async Task<bool> PlayFirstArmy(PlayableLeader leader, Character character, ActionsManager actionsManager)
    {
        DeckManager deck = DeckManager.Instance;
        CardData card = deck.GetCatalog(leader)
            .Where(c => c != null && c.GetCardType() == CardTypeEnum.Army && deck.IsAvailable(leader, c) && c.MeetsResourceRequirements(leader))
            .OrderBy(c => c.GetTotalGoldCost())
            .FirstOrDefault();
        if (card == null) return false;
        UtilityAIContext context = await AITurnController.ExecuteChosenCardAsync(leader, character, actionsManager, null, card, null, null);
        return context?.LastChosenAction != null && context.LastChosenAction.LastExecutionSucceeded;
    }

    private static bool Recruit(PlayableLeader leader, Character character, CardData card)
    {
        DeckManager deck = DeckManager.Instance;
        if (card == null || !deck.IsAvailable(leader, card)) return false;
        PC pc = character.hex?.GetPCData();
        if (pc == null || !CardNameUtility.Equals(pc.pcName, card.startingPC)) return false;
        if (Object.FindObjectsByType<Character>(FindObjectsSortMode.None).Any(c => c != null && !c.killed && CardNameUtility.Equals(c.characterName, card.name))) return false;
        if (!deck.TryConsumeCard(leader, card.name, out CardData consumed)) return false;
        return CharacterRecruitment.Spawn(leader, character.hex, consumed) != null;
    }

    // Once a turn per step: count turns spent at the place without the step being reached.
    private static void CountTurnAtTarget(PlayableLeader leader, CampaignObjective step)
    {
        string key = Key(leader, step);
        if (lastCountedTurn.TryGetValue(key, out int last) && last == Turn) return;
        lastCountedTurn[key] = Turn;
        if (CampaignObjectives.Instance.IsMet(leader, step)) { turnsAtTarget.Remove(key); return; }
        int turns = (turnsAtTarget.TryGetValue(key, out int t) ? t : 0) + 1;
        turnsAtTarget[key] = turns;
        if (turns < StuckTurnsBeforeBench) return;
        turnsAtTarget.Remove(key);
        benchedUntilTurn[key] = Turn + BenchTurns;
        Debug.Log($"[CampaignAI] {leader.characterName} sets '{step.title}' aside for {BenchTurns} turns (no progress at its place).");
    }

    /// <summary>Forgets every set-aside step (a new or loaded campaign starts clean).</summary>
    public static void Reset()
    {
        turnsAtTarget.Clear();
        benchedUntilTurn.Clear();
        lastCountedTurn.Clear();
        routes.Clear();
    }

    private static bool IsBenched(PlayableLeader leader, CampaignObjective step) =>
        benchedUntilTurn.TryGetValue(Key(leader, step), out int until) && Turn < until;
}
