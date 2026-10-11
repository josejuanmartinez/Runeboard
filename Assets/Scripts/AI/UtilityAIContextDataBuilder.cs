using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using UnityEngine;

public static class UtilityAIContextDataBuilder
{
    public static UtilityAIContext.PrecomputedData Build(Leader leader, Character character, float maxMilliseconds = -1f)
    {
        Stopwatch stopwatch = null;
        if (maxMilliseconds > 0f)
        {
            stopwatch = Stopwatch.StartNew();
        }

        Board board = UtilityAIContext.GetSharedBoard();
        StoresManager stores = Object.FindAnyObjectByType<StoresManager>();
        var data = new UtilityAIContext.PrecomputedData
        {
            LiquidWealth = UtilityAIContext.CalculateLiquidWealth(leader, stores),
            NationPercentageArtifacts = CalculateNationArtifacts(leader),
            HiddenArtifactsRemaining = CountHiddenArtifacts(board),
            // Leader-wide (not character-position-dependent), so computed here rather than
            // gated behind the character/hex early-return below.
            UnrecruitedSameAlignmentNplCount = CountUnrecruitedSameAlignmentNpls(leader),
            DuelAdvantage = 0f,
            SongDuelAdvantage = 0f,
            ClosestEnemy = new UtilityAIContext.EnemyTarget(null, float.MaxValue, false, 0f),
            ClosestNonNeutralEnemy = new UtilityAIContext.EnemyTarget(null, float.MaxValue, false, 0f),
            NearestUnrevealedNpcDistance = float.MaxValue,
            NearestEnemyCharacterDistance = float.MaxValue,
            NearestEnemyPcOpportunityDistance = float.MaxValue,
            NearestOwnPcLoyaltyRiskDistance = float.MaxValue,
            NearestEnemyPcVulnerabilityDistance = float.MaxValue,
            NearestHighValueEnemyCharacterDistance = float.MaxValue,
            NearestOwnPcFortificationNeedDistance = float.MaxValue,
            NearestNplRecruitmentDistance = float.MaxValue,
            ArtifactTransferCandidates = new List<UtilityAIContext.ArtifactTransferCandidate>(),
            BestArtifactTransferScore = 0f
        };
        CacheLeaderRoleStrengths(leader, ref data);

        if (board == null || character == null || character.hex == null) return data;
        if (ShouldStop(stopwatch, maxMilliseconds)) return data;

        CacheExplorationTarget(board, leader, character, ref data, stopwatch, maxMilliseconds);
        if (ShouldStop(stopwatch, maxMilliseconds)) return data;

        CacheEnemyTargets(board, character, leader, ref data, stopwatch, maxMilliseconds);
        if (ShouldStop(stopwatch, maxMilliseconds)) return data;

        CacheNpcTargets(board, character, leader, ref data, stopwatch, maxMilliseconds);
        if (ShouldStop(stopwatch, maxMilliseconds)) return data;

        CacheOwnPcSignals(leader, character, ref data);

        CacheDuelSignal(character, ref data);
        CacheSongDuelSignal(character, ref data);

        BuildArtifactTransfers(board, leader, character, ref data, stopwatch, maxMilliseconds);

        return data;
    }

    private static void CacheExplorationTarget(Board board, Leader leader, Character character, ref UtilityAIContext.PrecomputedData data, Stopwatch stopwatch, float maxMilliseconds)
    {
        if (board?.hexes == null || leader == null || character?.hex == null) return;

        float nearestDistance = float.MaxValue;
        int landHexCount = 0;
        foreach (Hex hex in board.hexes.Values)
        {
            if (ShouldStop(stopwatch, maxMilliseconds)) break;
            if (hex == null || hex.IsWaterTerrain()) continue;
            landHexCount++;
            if (leader.visibleHexes.Contains(hex)) continue;

            data.UnrevealedLandHexCount++;
            float distance = Vector2.Distance(character.hex.v2, hex.v2);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                data.NearestUnrevealedLandHex = hex;
            }
        }

        // Deterministic once-per-character/turn roll: a largely unknown map strongly invites
        // exploration, while a handful of remote unseen tiles only occasionally pull someone
        // away from normal build behavior. This avoids every character dog-piling the same fog.
        float explorationChance = landHexCount > 0 ? (float)data.UnrevealedLandHexCount / landHexCount : 0f;
        uint rollSeed = unchecked((uint)(((Game.Instance?.turn ?? 0) * 397) ^ character.GetEntityId().GetHashCode()));
        float roll = (rollSeed % 10000u) / 10000f;
        if (roll > explorationChance) data.NearestUnrevealedLandHex = null;
    }

    // Win-probability signal for the HTN's ImmediateDanger/Danger PersonalCombat pick: the
    // AI's own likely target choice (Duel.EstimateDuelScore(x, null), same ordering
    // Duel.PickBestTarget itself uses) compared against this character's score in that same
    // matchup — signed, so a losing opportunity contributes negatively rather than just failing
    // to help (see UtilityAIParameters.MilitaristicDuelAdvantage).
    private static void CacheDuelSignal(Character character, ref UtilityAIContext.PrecomputedData data)
    {
        data.DuelAdvantage = 0f;
        if (character == null || character.IsRefusingDuels()) return;

        ActionsManager actionsManager = ActionsManager.Instance;
        if (AITurnController.ResolveActionByRef("Duel", actionsManager) is not Duel duelAction) return;

        duelAction.Initialize(character);
        List<Character> candidates = duelAction.GetEligibleTargets(character);
        if (candidates.Count == 0) return;

        Character target = candidates.OrderByDescending(x => Duel.EstimateDuelScore(x, null)).First();
        data.DuelAdvantage = Duel.EstimateDuelScore(character, target) - Duel.EstimateDuelScore(target, character);
    }

    // Same shape as CacheDuelSignal, for Battle of Songs (mage-vs-mage) — see
    // UtilityAIParameters.MilitaristicSongDuelAdvantage.
    private static void CacheSongDuelSignal(Character character, ref UtilityAIContext.PrecomputedData data)
    {
        data.SongDuelAdvantage = 0f;
        if (character == null || character.GetMage() < 1) return;

        ActionsManager actionsManager = ActionsManager.Instance;
        if (AITurnController.ResolveActionByRef("BattleOfSongs", actionsManager) is not BattleOfSongs songAction) return;

        songAction.Initialize(character);
        List<Character> candidates = songAction.GetEligibleMageTargets(character);
        if (candidates.Count == 0) return;

        Character target = candidates.OrderByDescending(x => BattleOfSongs.EstimateSongScore(x)).First();
        data.SongDuelAdvantage = BattleOfSongs.EstimateSongScore(character) - BattleOfSongs.EstimateSongScore(target);
    }

    // Board-wide (not proximity-based) count of same-alignment NonPlayableLeaders this leader
    // could still recruit at all — NonPlayableLeader.joined is a single global "has joined
    // ANY leader" flag, not per-leader (see NonPlayableLeader.cs), so !joined + matching
    // alignment is the full "still available to recruit" test at this coarse, board-wide level.
    // Per-target eligibility additionally verifies the candidate is a capital that can accept
    // AFriendOrThree from this playable leader.
    private static int CountUnrecruitedSameAlignmentNpls(Leader leader)
    {
        if (leader == null) return 0;
        Game game = Game.Instance;
        if (game?.npcs == null) return 0;
        return game.npcs.Count(npc => npc != null && !npc.killed && !npc.joined && npc.GetAlignment() == leader.GetAlignment());
    }

    // Leader-wide (not character-position-dependent) Agent/Mage/Emissary skill totals across
    // controlled characters, computed once per character-turn here instead of by every one of
    // the ~100+ per-card UtilityAIContext instances scored per pick (see
    // UtilityAIContext.PrecomputedData.AgentRoleStrength etc.).
    private static void CacheLeaderRoleStrengths(Leader leader, ref UtilityAIContext.PrecomputedData data)
    {
        if (leader?.controlledCharacters == null) return;

        float agent = 0f, mage = 0f, emissary = 0f;
        foreach (Character c in leader.controlledCharacters)
        {
            if (c == null || c.killed) continue;
            agent += Mathf.Max(0, c.GetAgent());
            mage += Mathf.Max(0, c.GetMage());
            emissary += Mathf.Max(0, c.GetEmmissary());
        }
        data.AgentRoleStrength = agent;
        data.MageRoleStrength = mage;
        data.EmissaryRoleStrength = emissary;
    }

    private static void CacheEnemyTargets(Board board, Character character, Leader leader, ref UtilityAIContext.PrecomputedData data, Stopwatch stopwatch, float maxMilliseconds)
    {
        IEnumerable<Hex> hexes = board.hexes != null ? board.hexes.Values : Enumerable.Empty<Hex>();
        float myStrength = character.IsArmyCommander() && character.GetArmy() != null ? character.GetArmy().GetOffence() : 0f;

        // Target-quality thresholds for the enemy-PC/enemy-character signals below — read once
        // per turn rather than per hex.
        float enemyPcLoyaltyBelow = UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticEnemyPcLoyaltyBelow);
        float enemyPcDefenseBelow = UtilityAI.GetWeight(UtilityAI.Keys.IntelligenceEnemyPcDefenseBelow);
        float highValueSkillAtLeast = UtilityAI.GetWeight(UtilityAI.Keys.IntelligenceHighValueSkillAtLeast);

        foreach (Hex hex in hexes)
        {
            if (ShouldStop(stopwatch, maxMilliseconds)) return;

            List<Character> enemyCharactersOnHex = hex.characters.Where(c => c != null && c.GetOwner() != null && IsEnemy(c.GetOwner(), leader)).ToList();
            bool hasEnemyCharacter = enemyCharactersOnHex.Count > 0;
            Leader enemyLeader = GetEnemyLeaderOnHex(hex, leader);
            if (enemyLeader == null) continue;

            bool isNeutral = enemyLeader.GetAlignment() == AlignmentEnum.neutral;
            float distance = Vector2.Distance(character.hex.v2, hex.v2);
            float distanceScore = distance + (isNeutral ? 2f : 0f);
            float strength = EstimateEnemyStrength(hex, leader);

            if (distanceScore < data.ClosestEnemy.Score)
            {
                data.ClosestEnemy = new UtilityAIContext.EnemyTarget(hex, distance, isNeutral, strength);
            }

            if (!isNeutral && distance < data.ClosestNonNeutralEnemy.Distance)
            {
                data.ClosestNonNeutralEnemy = new UtilityAIContext.EnemyTarget(hex, distance, isNeutral, strength);
            }

            if (hasEnemyCharacter && distance < data.NearestEnemyCharacterDistance)
            {
                data.NearestEnemyCharacterDistance = distance;
                data.NearestEnemyCharacterHex = hex;
            }

            // Enemy-owned PC target quality: a good influence-out target (low loyalty) and/or a
            // good sabotage/theft target (weak defense) are independent qualities of the same PC.
            PC pc = hex.GetPC();
            if (pc != null && pc.owner != null && IsEnemy(pc.owner, leader))
            {
                if (pc.loyalty < enemyPcLoyaltyBelow && distance < data.NearestEnemyPcOpportunityDistance)
                {
                    data.NearestEnemyPcOpportunityDistance = distance;
                    data.NearestEnemyPcOpportunityHex = hex;
                }
                if (pc.GetDefense() < enemyPcDefenseBelow && distance < data.NearestEnemyPcVulnerabilityDistance)
                {
                    data.NearestEnemyPcVulnerabilityDistance = distance;
                    data.NearestEnemyPcVulnerabilityHex = hex;
                }
            }

            // High-value enemy character: worth a dedicated assassination/kidnap play, distinct
            // from "any enemy character is nearby" (Intelligence.EnemyCharacter above).
            foreach (Character enemyCharacter in enemyCharactersOnHex)
            {
                int skill = Mathf.Max(0, enemyCharacter.GetCommander()) + Mathf.Max(0, enemyCharacter.GetAgent())
                    + Mathf.Max(0, enemyCharacter.GetEmmissary()) + Mathf.Max(0, enemyCharacter.GetMage());
                if (skill >= highValueSkillAtLeast && distance < data.NearestHighValueEnemyCharacterDistance)
                {
                    data.NearestHighValueEnemyCharacterDistance = distance;
                    data.NearestHighValueEnemyCharacterHex = hex;
                }
            }
        }

        UtilityAIContext.EnemyTarget best = data.ClosestNonNeutralEnemy.Hex != null ? data.ClosestNonNeutralEnemy : data.ClosestEnemy;
        float outmatchedRatio = UtilityAI.GetWeight(UtilityAI.Keys.OutmatchedStrengthRatio);
        if (best.Hex != null && best.Strength > myStrength * outmatchedRatio) data.NeedsIndirectApproach = true;
    }

    // Own-PC signals: a small scan over this leader's own PCs (not the whole board) for the
    // nearest one falling below each "needs attention" threshold — loyalty (needs influencing
    // up) and defense (needs fortifying) are independent qualities of the same PC, same as the
    // enemy-side opportunity/vulnerability pair in CacheEnemyTargets above.
    private static void CacheOwnPcSignals(Leader leader, Character character, ref UtilityAIContext.PrecomputedData data)
    {
        if (leader?.controlledPcs == null || character == null || character.hex == null) return;

        float ownPcLoyaltyBelow = UtilityAI.GetWeight(UtilityAI.Keys.DiplomaticOwnPcLoyaltyBelow);
        float ownPcDefenseBelow = UtilityAI.GetWeight(UtilityAI.Keys.MilitaristicOwnPcDefenseBelow);
        foreach (PC pc in leader.controlledPcs)
        {
            if (pc == null || pc.hex == null) continue;
            float distance = Vector2.Distance(character.hex.v2, pc.hex.v2);

            if (pc.loyalty < ownPcLoyaltyBelow && distance < data.NearestOwnPcLoyaltyRiskDistance)
            {
                data.NearestOwnPcLoyaltyRiskDistance = distance;
                data.NearestOwnPcLoyaltyRiskHex = pc.hex;
            }
            if (pc.GetDefense() < ownPcDefenseBelow && distance < data.NearestOwnPcFortificationNeedDistance)
            {
                data.NearestOwnPcFortificationNeedDistance = distance;
                data.NearestOwnPcFortificationNeedHex = pc.hex;
            }
        }
    }

    private static void CacheNpcTargets(Board board, Character character, Leader leader, ref UtilityAIContext.PrecomputedData data, Stopwatch stopwatch, float maxMilliseconds)
    {
        Game game = Game.Instance;
        if (board == null || character == null || character.hex == null || game == null) return;

        foreach (Hex hex in board.hexes.Values)
        {
            if (ShouldStop(stopwatch, maxMilliseconds)) return;

            PC pc = hex.GetPC();
            if (pc == null) continue;
            if (pc.owner is not NonPlayableLeader npc) continue;

            float distance = Vector2.Distance(character.hex.v2, hex.v2);

            if (!npc.IsRevealedToLeader(game.currentlyPlaying) && distance < data.NearestUnrevealedNpcDistance)
            {
                data.NearestUnrevealedNpcDistance = distance;
                data.NearestUnrevealedNpcHex = hex;
            }

            // Recruitment eligibility, not proximity to reveal — CanJoinWithStateAllegiance
            // checks joined/killed/alignment, matching the gate AFriendOrThree uses before its
            // trivia gauntlet runs.
            if (pc.isCapital && leader is PlayableLeader recruitingLeader && npc.CanJoinWithStateAllegiance(recruitingLeader) && distance < data.NearestNplRecruitmentDistance)
            {
                data.NearestNplRecruitmentDistance = distance;
                data.NearestNplRecruitmentHex = hex;
            }
        }
    }

    private static void BuildArtifactTransfers(Board board, Leader leader, Character character, ref UtilityAIContext.PrecomputedData data, Stopwatch stopwatch, float maxMilliseconds)
    {
        if (board == null || character == null || character.hex == null || leader == null) return;

        List<CardData> transferable = character.objects.Where(a => a != null && a.transferable).ToList();
        if (transferable.Count == 0) return;

        List<Character> friendlies = board.hexes.Values
            .SelectMany(h => h.characters)
            .Where(ch => ch != null && ch.hex != null && ch != character &&
                         (ch.GetOwner() == character.GetOwner() ||
                          (ch.GetAlignment() == character.GetAlignment() && ch.GetAlignment() != AlignmentEnum.neutral)))
            .ToList();
        if (friendlies.Count == 0) return;

        data.ArtifactTransferCandidates.Clear();
        float bestScore = 0f;
        foreach (CardData art in transferable)
        {
            foreach (Character target in friendlies)
            {
                if (ShouldStop(stopwatch, maxMilliseconds)) return;

                float score = 0f;
                float distance = character.hex != null && target.hex != null
                    ? Vector2.Distance(character.hex.v2, target.hex.v2)
                    : float.MaxValue;

                score += art.commanderBonus > 0 ? art.commanderBonus * 2f + Mathf.Max(0, 5 - target.GetCommander()) : 0f;
                score += art.agentBonus > 0 ? art.agentBonus * 2f + Mathf.Max(0, 5 - target.GetAgent()) : 0f;
                score += art.emmissaryBonus > 0 ? art.emmissaryBonus * 2f + Mathf.Max(0, 5 - target.GetEmmissary()) : 0f;
                score += art.mageBonus > 0 ? art.mageBonus * 2f + Mathf.Max(0, 5 - target.GetMage()) : 0f;

                if (target.IsArmyCommander())
                {
                    score += art.GetAttackBonus() * 3f;
                    score += art.GetDefenseBonus() * 2f;
                }

                if (art.commanderBonus > 0 && target.GetCommander() > 3) score -= 2f;
                if (art.agentBonus > 0 && target.GetAgent() > 3) score -= 2f;
                if (art.emmissaryBonus > 0 && target.GetEmmissary() > 3) score -= 2f;
                if (art.mageBonus > 0 && target.GetMage() > 3) score -= 2f;

                if (distance < float.MaxValue)
                {
                    score -= distance * 2f;
                }
                else
                {
                    score -= 5f;
                }

                data.ArtifactTransferCandidates.Add(new UtilityAIContext.ArtifactTransferCandidate(art.name, target.characterName, score, distance));
                bestScore = Mathf.Max(bestScore, score);
            }
        }

        data.BestArtifactTransferScore = Mathf.Max(0f, bestScore / 3f);
    }

    private static bool IsEnemy(Leader other, Leader leader)
    {
        if (other == null || leader == null) return false;
        if (other == leader) return false;

        AlignmentEnum myAlignment = leader.GetAlignment();
        AlignmentEnum otherAlignment = other.GetAlignment();

        if (myAlignment == otherAlignment && myAlignment != AlignmentEnum.neutral) return false;

        return otherAlignment != myAlignment || otherAlignment == AlignmentEnum.neutral;
    }

    private static Leader GetEnemyLeaderOnHex(Hex hex, Leader leader)
    {
        if (hex == null) return null;

        PC pc = hex.GetPC();
        if (pc != null && pc.owner != null && IsEnemy(pc.owner, leader)) return pc.owner;

        Character enemyCharacter = hex.characters.FirstOrDefault(c => c != null && c.GetOwner() != null && IsEnemy(c.GetOwner(), leader));
        if (enemyCharacter != null) return enemyCharacter.GetOwner();

        return null;
    }

    private static float EstimateEnemyStrength(Hex hex, Leader leader)
    {
        if (hex == null) return 0f;

        int strength = 0;
        PC pc = hex.GetPC();
        if (pc != null && pc.owner != null && IsEnemy(pc.owner, leader))
        {
            strength = Mathf.Max(strength, pc.GetDefense());
        }

        if (hex.armies != null)
        {
            foreach (Army army in hex.armies)
            {
                if (army == null || army.commander == null) continue;
                if (army.commander.GetOwner() == null) continue;
                if (!IsEnemy(army.commander.GetOwner(), leader)) continue;
                strength = Mathf.Max(strength, army.GetDefence());
            }
        }

        return strength;
    }

    private static float CalculateNationArtifacts(Leader leader)
    {
        if (leader == null) return 0f;
        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        float totalArtifacts = (deckManager?.GetObjectCardCount() ?? 0) * 1f;
        return leader.controlledCharacters.Sum(ch => ch != null ? ch.objects.Count * 1f : 0f) / Mathf.Max(1f, totalArtifacts);
    }

    // This is the literal map count: each item still in Hex.hiddenObjects is
    // an artifact opportunity remaining for every leader. It is intentionally
    // not estimated from deck state or fog-of-war.
    private static int CountHiddenArtifacts(Board board)
    {
        return board?.hexes?.Values.Sum(hex => hex?.hiddenObjects?.Count ?? 0) ?? 0;
    }

    private static bool ShouldStop(Stopwatch stopwatch, float maxMilliseconds)
    {
        return stopwatch != null && maxMilliseconds > 0f && stopwatch.Elapsed.TotalMilliseconds >= maxMilliseconds;
    }
}
