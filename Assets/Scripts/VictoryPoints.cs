using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class VictoryPoints
{
    public PlayableLeader Leader { get; }
    public VictoryPointsBreakdown Breakdown { get; }
    public int RawScore => Breakdown.Total;
    public int RelativeScore { get; }

    public VictoryPoints(PlayableLeader leader, VictoryPointsBreakdown breakdown, int relativeScore)
    {
        Leader = leader;
        Breakdown = breakdown;
        RelativeScore = relativeScore;
    }

    public static Dictionary<PlayableLeader, VictoryPoints> CalculateForAll(Game game)
    {
        Dictionary<PlayableLeader, VictoryPoints> results = new();
        if (game == null) return results;

        List<PlayableLeader> leaders = new();
        if (game.player != null) leaders.Add(game.player);
        if (game.competitors != null) leaders.AddRange(game.competitors.Where(c => c != null));

        Dictionary<PlayableLeader, VictoryPointsBreakdown> breakdowns = leaders.ToDictionary(l => l, CalculateBreakdown);

        foreach (PlayableLeader leader in leaders)
        {
            IEnumerable<int> otherScores = breakdowns.Where(kvp => kvp.Key != leader).Select(kvp => kvp.Value.Total);
            int adjusted = ApplyRelativeAdjustment(breakdowns[leader].Total, otherScores);
            results[leader] = new VictoryPoints(leader, breakdowns[leader], adjusted);
        }

        return results;
    }

    public static void RecalculateAndAssign(Game game)
    {
        if (game == null) return;
        Dictionary<PlayableLeader, VictoryPoints> scores = CalculateForAll(game);
        foreach (var kvp in scores)
        {
            if (kvp.Key != null)
            {
                kvp.Key.victoryPoints = kvp.Value;
                RefreshIconVictoryPoints(kvp.Key, kvp.Value.RelativeScore);
            }
        }
    }

    public static VictoryPointsBreakdown CalculateBreakdown(PlayableLeader leader)
    {
        VictoryPointsBreakdown breakdown = new();
        if (leader == null || leader.killed) return breakdown;

        CalculatePcAndFortificationPoints(leader, breakdown);
        CalculateCharactersPoints(leader, breakdown);
        CalculateProductionPoints(leader, breakdown);
        CalculateStorePoints(leader, breakdown);
        CalculateArmyPoints(leader, breakdown);
        CalculateArtifactPoints(leader, breakdown);
        CalculateMapControlPoints(leader, breakdown);

        return breakdown;
    }

    private static void CalculatePcAndFortificationPoints(PlayableLeader leader, VictoryPointsBreakdown breakdown)
    {
        foreach (PC pc in leader.controlledPcs)
        {
            if (pc == null) continue;
            int pcLevel = Mathf.Max(0, (int)pc.citySize);

            switch (pc.acquisitionType)
            {
                case PCAcquisitionType.StartingOwned when pc.originType == PCOriginType.PlayableLeader:
                    breakdown.PcPoints += 5 * pcLevel;
                    break;
                case PCAcquisitionType.Joined when pc.originType == PCOriginType.NonPlayableLeader:
                    breakdown.PcPoints += 25 * pcLevel;
                    break;
                case PCAcquisitionType.CapturedByForce:
                    breakdown.PcPoints += 35 * pcLevel;
                    break;
            }

            breakdown.FortificationPoints += Mathf.Max(0, (int)pc.fortSize);
        }
    }

    private static void CalculateCharactersPoints(PlayableLeader leader, VictoryPointsBreakdown breakdown)
    {
        foreach (Character character in leader.controlledCharacters)
        {
            if (character == null || character.killed) continue;
            breakdown.CharacterLevels += character.GetCommander() + character.GetAgent() + character.GetEmmissary() + character.GetMage();
        }
    }

    private static void CalculateProductionPoints(PlayableLeader leader, VictoryPointsBreakdown breakdown)
    {
        int lightProduction = leader.GetLeatherPerTurn() + leader.GetTimberPerTurn() + leader.GetIronPerTurn();
        int heavyProduction = leader.GetSteelPerTurn() + leader.GetMithrilPerTurn() + leader.GetMountsPerTurn();
        breakdown.ProductionPoints += lightProduction * 2;
        breakdown.ProductionPoints += heavyProduction * 4;

        int goldProduction = leader.controlledPcs.Sum(pc => pc != null ? (int)pc.citySize : 0);
        breakdown.GoldProductionPoints += goldProduction * 2;
    }

    private static void CalculateStorePoints(PlayableLeader leader, VictoryPointsBreakdown breakdown)
    {
        int lightStores = leader.leatherAmount + leader.timberAmount + leader.ironAmount;
        int heavyStores = leader.steelAmount + leader.mithrilAmount + leader.mountsAmount;

        breakdown.StorePoints += lightStores;
        breakdown.StorePoints += heavyStores * 2;
        breakdown.GoldStorePoints += leader.goldAmount * 2;
    }

    private static void CalculateArmyPoints(PlayableLeader leader, VictoryPointsBreakdown breakdown)
    {
        foreach (Character commander in leader.controlledCharacters)
        {
            if (commander == null || commander.killed || !commander.IsArmyCommander()) continue;

            Army army = commander.GetArmy();
            if (army == null || army.killed) continue;

            int basePoints = army.ma + army.ar + army.li;
            basePoints += (army.hi + army.lc + army.ws) * 2;
            basePoints += (army.hc + army.ca) * 3;

            float xpMultiplier = 1f + Mathf.Clamp01(army.xp / 100f) * 0.5f;
            int adjustedPoints = Mathf.RoundToInt(basePoints * xpMultiplier);

            breakdown.ArmyPoints += basePoints;
            breakdown.ArmyXpBonusPoints += Mathf.Max(0, adjustedPoints - basePoints);
        }
    }

    private static void CalculateArtifactPoints(PlayableLeader leader, VictoryPointsBreakdown breakdown)
    {
        int artifacts = leader.controlledCharacters.Sum(c => c != null && !c.killed ? c.objects.Count : 0);
        breakdown.ArtifactPoints += artifacts * 3;
    }

    private static void CalculateMapControlPoints(PlayableLeader leader, VictoryPointsBreakdown breakdown)
    {
        Board board = Board.Instance;
        if (board == null || board.hexes == null) return;

        HashSet<Hex> revealed = new(leader.visibleHexes.Where(h => h != null));
        HashSet<Hex> scouted = new(board.hexes.Values.Where(h => h != null && h.IsScouted(leader)));

        int scoutedCount = scouted.Count;
        int revealedOnly = revealed.Count(hex => !scouted.Contains(hex));

        breakdown.MapControlPoints += revealedOnly;
        breakdown.MapControlPoints += scoutedCount * 2;
    }

    private static int ApplyRelativeAdjustment(int rawScore, IEnumerable<int> otherScores)
    {
        if (otherScores == null) return rawScore;
        List<int> others = otherScores.Where(x => x > 0).ToList();
        if (others.Count == 0) return rawScore;

        float average = (float) others.Average();
        float spread = Mathf.Max(average * 0.5f, 25f);
        float normalizedDelta = (rawScore - average) / spread;
        float modifier = Mathf.Clamp(normalizedDelta * 0.25f, -0.25f, 0.25f);

        return Mathf.RoundToInt(rawScore * (1f + modifier));
    }

    private static void RefreshIconVictoryPoints(PlayableLeader leader, int points)
    {
        if (leader == null) return;
        PlayableLeaderIcons icons = Object.FindAnyObjectByType<PlayableLeaderIcons>();
        if (icons == null) return;
        icons.RefreshVictoryPointsFor(leader, points);
    }
}

public class VictoryPointsBreakdown
{
    public int PcPoints;
    public int FortificationPoints;
    public int CharacterLevels;
    public int ProductionPoints;
    public int GoldProductionPoints;
    public int StorePoints;
    public int GoldStorePoints;
    public int ArmyPoints;
    public int ArmyXpBonusPoints;
    public int ArtifactPoints;
    public int MapControlPoints;

    public int Total =>
        PcPoints +
        FortificationPoints +
        CharacterLevels +
        ProductionPoints +
        GoldProductionPoints +
        StorePoints +
        GoldStorePoints +
        ArmyPoints +
        ArmyXpBonusPoints +
        ArtifactPoints +
        MapControlPoints;
}
