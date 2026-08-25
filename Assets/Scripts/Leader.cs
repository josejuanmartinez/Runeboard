using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Leader : Character
{
    [Header("Visuals")]
    public Color nationColor = Color.white;

    [Header("Nation data")]
    public List<Character> controlledCharacters = new();
    public List<PC> controlledPcs = new();
    // HashSet: membership is checked per hex on every fog refresh and per hop during
    // walks — List.Contains made those passes O(board × visible). Not Unity-serialized,
    // which is fine: fog state is rebuilt at runtime.
    public HashSet<Hex> visibleHexes = new();
    public bool playedLandThisTurn;
    // Per-turn cap: only one environmental card may be played per leader per turn (the board
    // only has a single active-environment slot — see EnvironmentalCardManager). Stored on
    // Leader so it covers both playable leaders and NPL AI turns.
    // lastEnvironmentalCardPlayedTurn additionally feeds UtilityAI's
    // EnvironmentalPenalty decay (see UtilityAIContext.GetEnvironmentalPenaltyScore).
    public bool playedEnvironmentalCardThisTurn;
    public int lastEnvironmentalCardPlayedTurn = -999;
    // Last two cards this leader has paid the cost of, most recent last — capped at 2 since
    // the only reader (Vaire's Loom) only ever needs "the card played immediately before this
    // one", not a full history. Updated in DeckManager.ApplyCardCosts, not per-card-type, so it
    // stays generic rather than hardcoding any specific card's name here.
    public readonly List<CardData> recentlyPlayedCards = new();
    private readonly Dictionary<Hex, int> tempSeenHexes = new();
    private readonly Dictionary<Hex, int> tempScoutCenters = new();

    [Header("Stores")]
    public int leatherAmount = 0;
    public int mountsAmount = 0;
    public int timberAmount = 0;
    public int ironAmount = 0;
    public int steelAmount = 0;
    public int mithrilAmount = 0;
    public int goldAmount = 0;

    private Game game;
    private LeaderBiomeConfig leaderBiome;

    // Founding-opportunity mechanic: once every 5 turns, if an emissary is standing in a
    // region with an unfounded PC, surface it as an opportunity card (SituationCardsUI).
    // If shown but not accepted, the cooldown is held in abeyance (re-offered every turn,
    // see the pending flag) until one is actually founded — NotifyPcFounded resets both.
    private int turnsSinceLastPcFoundingOffer = 999;
    private bool pcFoundingOfferPending;

    public void NotifyPcFounded()
    {
        turnsSinceLastPcFoundingOffer = 0;
        pcFoundingOfferPending = false;
    }

    public void Initialize(Hex hex, LeaderBiomeConfig leaderBiome, bool showSpawnMessage = true, bool applyNoScenarioStart = false)
    {
        game = Game.Instance;
        this.leaderBiome = leaderBiome;
		InitializeFromBiome(this, hex, leaderBiome, showSpawnMessage, applyNoScenarioStart);
    }

    public LeaderBiomeConfig GetBiome()
    {
        return leaderBiome;
    }

    public bool HasCharacterSlot() => true;
    public bool HasPcSlot() => true;

    public bool TryConsumeCharacterSlot() => true;
    public bool TryConsumePcSlot() => true;

    public int GetLeatherPerTurn()
    {
        return 0;
    }

    public int GetMountsPerTurn()
    {
        return 0;
    }

    public int GetTimberPerTurn()
    {
        return 0;
    }

    public int GetIronPerTurn()
    {
        return 0;
    }

    public int GetSteelPerTurn()
    {
        return 0;
    }

    public int GetMithrilPerTurn()
    {
        return 0;
    }

    new public AlignmentEnum GetAlignment()
    {
        return leaderBiome.alignment;
    }
    // Everything NewTurn does except kicking off WaitUntilEndOfTurn — split out so
    // alignment-timed NonPlayableLeader processing (see Game) can reuse
    // this safely. WaitUntilEndOfTurn unconditionally calls game.NextPlayer() for any leader
    // that isn't game.player, which is always true for an NPL — looping that per NPL would
    // corrupt the real PlayableLeader turn rotation, so NPLs must never reach it.
    public void RefreshForNewTurn()
    {
        playedLandThisTurn = false;
        playedEnvironmentalCardThisTurn = false;
        DecrementTemporarySeenHexes();
        DecrementTemporaryScoutCenters();
        if (!killed && goldAmount < -10) Killed(this);

        if (killed) return;

        Army.ResolveStartOfTurnRangedVolleysForLeader(this);

        // Make all characters in nation act!
        controlledCharacters.FindAll(c => !c.killed).ForEach(x => x.NewTurn());

        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        if (deckManager != null && this is PlayableLeader playable)
        {
            deckManager.RecycleDiscardPileIfExhausted(playable);
        }

        RunTurnStartResourceGrants();

        turnsSinceLastPcFoundingOffer++;
        TryOfferPcFoundingOpportunity();
    }

    new public void NewTurn()
    {
        RefreshForNewTurn();
        if (killed) return;
        StartCoroutine(WaitUntilEndOfTurn());
    }

    // At most one PC grant and one region (Land card) grant per leader per turn, even if
    // several characters share the same PC or region — dedup keys are built synchronously
    // in this loop (before the fire-and-forget grant calls run their async show/spin), so
    // ordering across controlledCharacters is what decides which character "gets credit".
    private void RunTurnStartResourceGrants()
    {
        Board board = Board.Instance;
        if (board == null)
        {
            Debug.LogWarning("[PCGrant] RunTurnStartResourceGrants aborted — no Board found.");
            return;
        }
        Debug.Log($"[PCGrant] RunTurnStartResourceGrants for {name}, {controlledCharacters.Count(c => c != null && !c.killed)} live character(s).");

        board.TriggerTurnStartResourceGrants(controlledCharacters);
    }

    private void TryOfferPcFoundingOpportunity()
    {
        if (killed) return;
        if (this is not PlayableLeader playable) return;
        if (!pcFoundingOfferPending && turnsSinceLastPcFoundingOffer < 5) return;

        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        if (deckManager == null) return;

        foreach (Character c in controlledCharacters)
        {
            if (c == null || c.killed || c.hex == null || c.GetEmmissary() <= 0) continue;

            // GetUnfoundedOwnRegionPcCards only checks region + not-yet-founded — it doesn't
            // know whether the leader can currently afford/qualify to found each PC, so filter
            // on the same EvaluatePlayability check GetSituationCards uses before offering any.
            List<CardData> candidates = deckManager.GetUnfoundedOwnRegionPcCards(playable, c.hex)
                .Where(card => card.EvaluatePlayability(c))
                .ToList();
            if (candidates.Count == 0) continue;

            pcFoundingOfferPending = true;
            if (game != null && game.player == playable && !game.IsPlayerAutoplayEnabledFor(playable))
            {
                SituationCardsUI.Instance?.Show(candidates, c);
            }
            return;
        }
    }

    private IEnumerator WaitUntilEndOfTurn()
    {
        yield return new WaitForEndOfFrame();

        // AI: Act if not player
        if (game.player != this)
        {
            yield return AITurnController.ExecuteLeaderTurn(this as PlayableLeader);
            game.NextPlayer();
            yield break;
        }
        else
        {
            if(this.killed) {
                game.EndGame(false);
                yield break;
            }
            // Refresh UI
            FindFirstObjectByType<StoresManager>()?.RefreshStores();
            // Refresh hexes
            StartCoroutine(RevealVisibleHexesAsync(() =>
            {
                // Prompt for action to the player - skip if already selected (e.g. Game.
                // SelectFirstPlayerCharacter already selected it earlier this same turn-start
                // sequence): Board.SelectHex's single-character branch unconditionally replays
                // the character's voice bark on every call, so a redundant call here would
                // double-play it.
                if (Board.Instance != null && Board.Instance.selectedCharacter != this)
                {
                    Board.Instance.SelectCharacter(this, true, 1.0f, 0.0f);
                }
            }
            ));
        }
    }

    public void RecordPlayedLandThisTurn()
    {
        playedLandThisTurn = true;
    }

    public bool HasPlayedLandThisTurn()
    {
        return playedLandThisTurn;
    }

    public void RecordEnvironmentalCardPlayed(int turn)
    {
        playedEnvironmentalCardThisTurn = true;
        lastEnvironmentalCardPlayedTurn = turn;
    }

    public bool HasPlayedEnvironmentalCardThisTurn()
    {
        return playedEnvironmentalCardThisTurn;
    }

    // Shared prelude for the two visibility-refresh passes below. A single manual sweep
    // over the board replaces the old LINQ chains whose per-hex List.Contains made each
    // refresh O(board × visible) and allocated several whole-board lists — the main frame
    // spike when fog was reconciled.
    private void CollectVisibilityRefreshSets(Board board, out List<Hex> radiusHexes, out List<Hex> scoutedOnly, out List<Hex> spiedHexes)
    {
        HashSet<Hex> radiusSet = new(visibleHexes);
        foreach (Hex center in GetTemporaryScoutCenters()) radiusSet.Add(center);

        radiusHexes = new List<Hex>(radiusSet);
        scoutedOnly = new List<Hex>();
        spiedHexes = new List<Hex>();

        foreach (Hex hex in board.hexes.Values)
        {
            bool scouted = IsScoutedForLeader(hex);
            if (scouted && !radiusSet.Contains(hex)) scoutedOnly.Add(hex);

            List<Character> hexCharacters = hex.characters;
            for (int i = 0; i < hexCharacters.Count; i++)
            {
                Character ch = hexCharacters[i];
                if (ch != null && ch.doubledBy.Contains(this))
                {
                    spiedHexes.Add(hex);
                    break;
                }
            }

            if (!visibleHexes.Contains(hex) && !scouted && !IsTemporarilySeen(hex)) hex.Hide();
        }
    }

    // The async version of RevealVisibleHexes
    public IEnumerator RevealVisibleHexesAsync(System.Action onComplete = null)
    {
        if (game == null) game = Game.Instance;
        if (game == null || game.player != this) yield break; // This will exit without calling onComplete

        Board board = Board.Instance;
        if (board == null || board.hexes == null) yield break;

        CollectVisibilityRefreshSets(board, out List<Hex> radiusHexes, out List<Hex> scoutedOnly, out List<Hex> spiedHexes);

        int batchSize = 15;
        for (int i = 0; i < radiusHexes.Count; i += batchSize)
        {
            int endIndex = Mathf.Min(i + batchSize, radiusHexes.Count);
            for (int j = i; j < endIndex; j++) radiusHexes[j].RevealArea(1, false);
            yield return null;
        }
        for (int i = 0; i < scoutedOnly.Count; i++)
        {
            scoutedOnly[i].Reveal();
        }
        for (int i = 0; i < spiedHexes.Count; i++)
        {
            spiedHexes[i].Reveal();
        }

        foreach (var hex in GetTemporarySeenHexes())
        {
            if (hex != null) hex.Reveal();
        }

        onComplete?.Invoke();
    }

    public void RefreshVisibleHexesImmediate()
    {
        Game g = Game.Instance;
        if (g == null || g.player != this) return;
        Board currentBoard = Board.Instance;
        if (currentBoard == null || currentBoard.hexes == null) return;

        CollectVisibilityRefreshSets(currentBoard, out List<Hex> radiusHexes, out List<Hex> scoutedOnly, out List<Hex> spiedHexes);

        for (int i = 0; i < radiusHexes.Count; i++) radiusHexes[i].RevealArea(1, false);
        for (int i = 0; i < scoutedOnly.Count; i++) scoutedOnly[i].Reveal();
        for (int i = 0; i < spiedHexes.Count; i++) spiedHexes[i].Reveal();
        foreach (var hex in GetTemporarySeenHexes())
        {
            if (hex != null) hex.Reveal();
        }
    }

    public void AddTemporarySeenHexes(IEnumerable<Hex> hexes)
    {
        AddTemporarySeenHexes(hexes, 2);
    }

    public void AddTemporarySeenHexes(IEnumerable<Hex> hexes, int turns)
    {
        if (hexes == null) return;
        turns = Math.Max(1, turns);
        foreach (var hex in hexes)
        {
            if (hex == null) continue;
            if (tempSeenHexes.TryGetValue(hex, out int current))
            {
                tempSeenHexes[hex] = Math.Max(current, turns);
            }
            else
            {
                tempSeenHexes[hex] = turns;
            }
        }
    }

    public void AddTemporaryScoutCenters(IEnumerable<Hex> hexes)
    {
        if (hexes == null) return;
        foreach (var hex in hexes)
        {
            if (hex == null) continue;
            if (tempScoutCenters.TryGetValue(hex, out int current))
            {
                tempScoutCenters[hex] = Math.Max(current, 2);
            }
            else
            {
                tempScoutCenters[hex] = 2;
            }
        }
    }

    private bool IsTemporarilySeen(Hex hex)
    {
        return hex != null && tempSeenHexes.TryGetValue(hex, out int turns) && turns > 0;
    }

    private bool IsScoutCenter(Hex hex)
    {
        return hex != null && tempScoutCenters.TryGetValue(hex, out int turns) && turns > 0;
    }

    private IEnumerable<Hex> GetTemporarySeenHexes()
    {
        return tempSeenHexes.Where(entry => entry.Value > 0).Select(entry => entry.Key);
    }

    private IEnumerable<Hex> GetTemporaryScoutCenters()
    {
        return tempScoutCenters.Where(entry => entry.Value > 0).Select(entry => entry.Key);
    }

    private void DecrementTemporarySeenHexes()
    {
        if (tempSeenHexes.Count == 0) return;
        List<Hex> keys = tempSeenHexes.Keys.ToList();
        for (int i = 0; i < keys.Count; i++)
        {
            Hex hex = keys[i];
            tempSeenHexes[hex] = tempSeenHexes[hex] - 1;
            if (tempSeenHexes[hex] <= 0) tempSeenHexes.Remove(hex);
        }
    }

    private void DecrementTemporaryScoutCenters()
    {
        if (tempScoutCenters.Count == 0) return;
        List<Hex> keys = tempScoutCenters.Keys.ToList();
        for (int i = 0; i < keys.Count; i++)
        {
            Hex hex = keys[i];
            tempScoutCenters[hex] = tempScoutCenters[hex] - 1;
            if (tempScoutCenters[hex] <= 0) tempScoutCenters.Remove(hex);
        }
    }

    private bool IsScoutedForLeader(Hex hex)
    {
        if (hex == null) return false;
        if (this is not PlayableLeader playable) return false;
        return hex.IsScouted(playable);
    }

    override public Leader GetOwner()
    {
        return owner != null ? owner : this;
    }

    public bool LeaderSeesHex(Hex hex)
    {
        if (hex == null) return false;
        if (hex.GetPC() != null && hex.GetPC().owner == GetOwner()) return true;
        if (hex.characters.Find(x => x.GetOwner() == GetOwner())) return true;
        if (hex.HasAnchoredWarshipsForLeader(GetOwner())) return true;
        return false;
    }

    public void AddLeather(int amount, bool showMessage = true) 
    {
        leatherAmount += amount;
        TryPulseStoreResourceGain(ProducesEnum.leather, amount);
    }
    public void AddTimber(int amount, bool showMessage = true)
    {
        timberAmount += amount;
        TryPulseStoreResourceGain(ProducesEnum.timber, amount);
    }
    public void AddMounts(int amount, bool showMessage = true)
    {
        mountsAmount += amount;
        TryPulseStoreResourceGain(ProducesEnum.mounts, amount);
    }
    public void AddIron(int amount, bool showMessage = true)
    {
        ironAmount += amount;
        TryPulseStoreResourceGain(ProducesEnum.iron, amount);
    }

    public void AddSteel(int amount, bool showMessage = true)
    {
        steelAmount += amount;
        TryPulseStoreResourceGain(ProducesEnum.steel, amount);
    }

    public void AddMithril(int amount, bool showMessage = true)
    {
        mithrilAmount += amount;
        TryPulseStoreResourceGain(ProducesEnum.mithril, amount);
    }
    public void AddGold(int amount, bool showMessage = true)
    {
        goldAmount += amount;
        TryPulseStoreGoldGain(amount);
    }
    public void RemoveLeather(int leatherCost, bool showMessage = true)
    {
        leatherAmount -= leatherCost;
        if (showMessage && leatherCost > 0) MessageDisplay.ShowMessage($"{characterName}: -{leatherCost} <sprite name=\"leather\">", Color.red);
    }
    public void RemoveTimber(int timberCost, bool showMessage = true)
    {
        timberAmount -= timberCost;
        if (showMessage && timberCost > 0) MessageDisplay.ShowMessage($"{characterName}: -{timberCost} <sprite name=\"timber\">", Color.red);
    }
    public void RemoveMounts(int mountsCost, bool showMessage = true)
    {
        mountsAmount -= mountsCost;
        if (showMessage && mountsCost > 0) MessageDisplay.ShowMessage($"{characterName}: -{mountsCost} <sprite name=\"mounts\">", Color.red);
    }
    public void RemoveIron(int ironCost, bool showMessage = true)
    {
        ironAmount -= ironCost;
        if (showMessage && ironCost > 0) MessageDisplay.ShowMessage($"{characterName}: -{ironCost} <sprite name=\"iron\">", Color.red);
    }

    public void RemoveSteel(int steelCost, bool showMessage = true)
    {
        steelAmount -= steelCost;
        if (showMessage && steelCost > 0) MessageDisplay.ShowMessage($"{characterName}: -{steelCost} <sprite name=\"steel\">", Color.red);
    }

    public void RemoveMithril(int mithrilCost, bool showMessage = true)
    {
        mithrilAmount -= mithrilCost;
        if (showMessage && mithrilCost > 0) MessageDisplay.ShowMessage($"{characterName}: -{mithrilCost} <sprite name=\"mithril\">", Color.red);
    }
    public void RemoveGold(int goldCost, bool showMessage = true)
    {
        goldAmount -= goldCost;
        if (showMessage && goldCost > 0) MessageDisplay.ShowMessage($"{characterName}: -{goldCost} <sprite name=\"gold\">", Color.red);
    }

    public int GetResourceAmount(ProducesEnum resourceType)
    {
        return resourceType switch
        {
            ProducesEnum.leather => leatherAmount,
            ProducesEnum.mounts => mountsAmount,
            ProducesEnum.timber => timberAmount,
            ProducesEnum.iron => ironAmount,
            ProducesEnum.steel => steelAmount,
            ProducesEnum.mithril => mithrilAmount,
            ProducesEnum.gold => goldAmount,
            _ => 0
        };
    }

    public void AddResource(ProducesEnum resourceType, int amount, bool showMessage = true)
    {
        if (amount <= 0) return;

        switch (resourceType)
        {
            case ProducesEnum.leather:
                AddLeather(amount, showMessage);
                break;
            case ProducesEnum.mounts:
                AddMounts(amount, showMessage);
                break;
            case ProducesEnum.timber:
                AddTimber(amount, showMessage);
                break;
            case ProducesEnum.iron:
                AddIron(amount, showMessage);
                break;
            case ProducesEnum.steel:
                AddSteel(amount, showMessage);
                break;
            case ProducesEnum.mithril:
                AddMithril(amount, showMessage);
                break;
            case ProducesEnum.gold:
                AddGold(amount, showMessage);
                break;
        }
    }

    public void RemoveResource(ProducesEnum resourceType, int amount, bool showMessage = true)
    {
        if (amount <= 0) return;

        switch (resourceType)
        {
            case ProducesEnum.leather:
                RemoveLeather(amount, showMessage);
                break;
            case ProducesEnum.mounts:
                RemoveMounts(amount, showMessage);
                break;
            case ProducesEnum.timber:
                RemoveTimber(amount, showMessage);
                break;
            case ProducesEnum.iron:
                RemoveIron(amount, showMessage);
                break;
            case ProducesEnum.steel:
                RemoveSteel(amount, showMessage);
                break;
            case ProducesEnum.mithril:
                RemoveMithril(amount, showMessage);
                break;
            case ProducesEnum.gold:
                RemoveGold(amount, showMessage);
                break;
        }
    }

    private void TryPulseStoreResourceGain(ProducesEnum resourceType, int amount)
    {
        if (amount <= 0) return;

        Game game = Game.Instance;
        Leader owner = GetOwner();
        if (game == null || owner == null || owner != game.player) return;

        StoresManager storesManager = FindFirstObjectByType<StoresManager>();
        if (storesManager == null) return;

        storesManager.RefreshStores();
        storesManager.PulseResourceGain(resourceType, amount);
    }

    private void TryPulseStoreGoldGain(int amount)
    {
        if (amount <= 0) return;

        Game game = Game.Instance;
        Leader owner = GetOwner();
        if (game == null || owner == null || owner != game.player) return;

        StoresManager storesManager = FindFirstObjectByType<StoresManager>();
        if (storesManager == null) return;

        storesManager.RefreshStores();
        storesManager.PulseGoldGain(amount);
    }

    public int GetCharacterPoints()
    {
        if (killed) return 0;
        return controlledCharacters.FindAll(x => !x.killed).Select(x => x.GetCommander() + x.GetAgent() + x.GetEmmissary() + x.GetMage() + x.objects.Count * 10 + x.health).Sum();
    }

    public int GetPCPoints()
    {
        int points = controlledPcs.Select(x => x.GetDefense()).Sum();
        points -= controlledPcs.FindAll(x => x.hiddenButRevealed).Count() * 10;
        return points;
    }

    public int GetArmyPoints()
    {
        int offence = controlledCharacters.FindAll(x => x.IsArmyCommander()).Select(x => x.GetArmy().GetOffence()).Sum();
        int defence = controlledCharacters.FindAll(x => x.IsArmyCommander()).Select(x => x.GetArmy().GetDefence()).Sum();
        return offence + defence;
    }

    public int GetStorePoints()
    {
        return leatherAmount + timberAmount * 2 + mithrilAmount * 5 + ironAmount * 3 + steelAmount * 4 + mountsAmount * 2;
    }

    public int GetResourceProductionPoints()
    {
        return GetLeatherPerTurn() + GetTimberPerTurn() * 2 + GetMithrilPerTurn() * 5 + GetIronPerTurn() * 3 + GetSteelPerTurn() * 4 + GetMountsPerTurn() * 2;
    }

    public int GetAllPoints()
    {
        return GetCharacterPoints() + GetPCPoints() + GetArmyPoints() + GetStorePoints();
    }

    public override void Killed(Leader killedBy, bool onlyMask = false)
    {
        bool realmCollapsed = killedBy == this;
        if(realmCollapsed)
        {
            MessageDisplayNoUI.ShowMessage(hex, this, $"{name}'s realm collapsed!", Color.red);
        } else
        {
            MessageDisplayNoUI.ShowMessage(hex, this, $"{name} was killed by {killedBy.characterName}", Color.red);
        }

        // We just mark them (, true) as if we kill them, they will be removed from controlledCharacters array and change the size dynamically
        // Throwing an error
        controlledCharacters.ForEach(x =>
        {
            x.hex.characters.Remove(x);
            x.hex.armies.Remove(x.GetArmy());
            // Redraw in x.Killed
            if (x != this) x.Killed(killedBy, true);
        });

        List<Character> markedAsKilled = controlledCharacters.FindAll(x => x.killed);
        foreach (Character marked in markedAsKilled)
        {
            if (controlledCharacters.Contains(marked) && marked != this) marked.Killed(killedBy);
        }

        if (realmCollapsed)
        {
            // Autokilled (like bankrupt): collapsed holdings disappear from the map. PCs are
            // never left in an ownerless runtime state.
            foreach (PC pc in GetOwner().controlledPcs)
            {
                pc?.hex?.ClearPC();
            }
        }
        else        
        {
            foreach (PC pc in GetOwner().controlledPcs)
            {
                pc.owner = killedBy;
                pc.acquisitionType = PCAcquisitionType.CapturedByForce;
                killedBy.controlledPcs.Add(pc);
                killedBy.visibleHexes.Add(hex);
                pc.hex.RedrawPC();
            }
        }

        GetOwner().controlledCharacters.Clear();
        GetOwner().controlledPcs.Clear();
        visibleHexes.Clear();

        killedBy.leatherAmount += leatherAmount;
        killedBy.mountsAmount += mountsAmount;
        killedBy.timberAmount += timberAmount;
        killedBy.ironAmount += ironAmount;
        killedBy.steelAmount += steelAmount;
        killedBy.mithrilAmount += mithrilAmount;
        killedBy.goldAmount += goldAmount;

        leatherAmount = 0;
        mountsAmount = 0;
        timberAmount = 0;
        ironAmount = 0;
        steelAmount = 0;
        mithrilAmount = 0;
        goldAmount = 0;

        base.Killed(killedBy);
    }

}
