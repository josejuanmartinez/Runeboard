using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroLOTR.Scenarios;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Save / load for a campaign in progress.
///
/// Saving snapshots the parts of the world that decide the war: turn, leaders' treasuries and
/// command points, every character (position, health, skills, statuses, objects, army), every
/// PC (owner, size, fort, loyalty), the human's fog of war and their hand/deck, and campaign
/// objective progress.
///
/// Loading restarts the same scenario with the same leader variants (the normal spawn path),
/// skips the intro video/tutorial, then lays the snapshot over the freshly spawned board.
/// Hidden objects and encounters are re-rolled, and AI planners rebuild their plans.
/// </summary>
public static class SaveSystem
{
    public const int Version = 1;
    public static string SavePath => Path.Combine(Application.persistentDataPath, "campaign.save.json");

    /// <summary>The save being restored during the current scene's startup, if any.</summary>
    public static SaveGame PendingLoad { get; private set; }

    public static bool HasSave => File.Exists(SavePath);

    public static bool CanSaveNow(out string reason)
    {
        Game game = Game.Instance;
        reason = null;
        if (game == null || !game.started || game.player == null) { reason = "No campaign is running."; return false; }
        if (game.GameOver) { reason = "The war is over."; return false; }
        if (!game.IsPlayerCurrentlyPlaying() || !game.IsAcceptingPlayerInput) { reason = "You can only save during your own turn."; return false; }
        return true;
    }

    // ------------------------------------------------------------------ Save

    public static bool Save(out string error)
    {
        error = null;
        if (!CanSaveNow(out error)) return false;
        try
        {
            SaveGame data = Capture(Game.Instance);
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: false));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            error = e.Message;
            return false;
        }
    }

    private static SaveGame Capture(Game game)
    {
        Board board = Board.Instance;
        var data = new SaveGame
        {
            version = Version,
            savedAtUtc = DateTime.UtcNow.ToString("u"),
            scenario = GameConfig.ScenarioToLoad,
            turn = game.turn,
            playerLeader = game.player.characterName,
            playerVariant = game.player.GetSelectedVariantName(),
            commandPoints = game.player.CommandPoints,
        };

        foreach (PlayableLeader competitor in game.competitors.Where(c => c != null))
            data.competitorVariants.Add(new LeaderVariantSave { leader = competitor.characterName, variant = competitor.GetSelectedVariantName() });

        List<Leader> leaders = AllLeaders(game).ToList();
        foreach (Leader leader in leaders)
        {
            data.leaders.Add(new LeaderSave
            {
                name = leader.characterName,
                killed = leader.killed,
                leather = leader.leatherAmount, mounts = leader.mountsAmount, timber = leader.timberAmount,
                iron = leader.ironAmount, steel = leader.steelAmount, mithril = leader.mithrilAmount, gold = leader.goldAmount,
            });
        }

        foreach (Leader leader in leaders)
        {
            foreach (Character c in leader.controlledCharacters.Where(c => c != null))
                data.characters.Add(CaptureCharacter(c));
        }

        foreach (Hex hex in board.hexes.Values)
        {
            if (hex == null) continue;
            PC pc = hex.GetPC();
            if (pc != null)
            {
                data.pcs.Add(new PcSave
                {
                    x = hex.v2.x, y = hex.v2.y, name = pc.pcName, owner = pc.owner != null ? pc.owner.characterName : null,
                    citySize = (int)pc.citySize, fortSize = (int)pc.fortSize, loyalty = pc.loyalty,
                    isHidden = pc.isHidden, hiddenButRevealed = pc.hiddenButRevealed, isCapital = pc.isCapital, hasPort = pc.hasPort,
                });
            }
            if (hex.IsHexRevealed()) data.revealedHexes.Add(new Vec { x = hex.v2.x, y = hex.v2.y });
        }

        DeckManager deck = DeckManager.Instance;
        if (deck != null)
        {
            data.hand = deck.GetHand(game.player).Select(c => c.name).ToList();
            data.lastHandDrawTurn = deck.GetLastHandDrawTurn(game.player);
        }
        if (CampaignObjectives.Instance != null) data.objectivesAchieved = CampaignObjectives.Instance.GetAchievedOnceKeys().ToList();
        return data;
    }

    private static CharacterSave CaptureCharacter(Character c)
    {
        var save = new CharacterSave
        {
            name = c.characterName,
            owner = c.GetOwner() != null ? c.GetOwner().characterName : null,
            isLeader = c is Leader,
            killed = c.killed,
            x = c.hex != null ? c.hex.v2.x : -1, y = c.hex != null ? c.hex.v2.y : -1,
            health = c.health,
            commander = c.GetCommander(), agent = c.GetAgent(), emmissary = c.GetEmmissary(), mage = c.GetMage(),
            moved = c.moved,
            acted = c.hasActionedThisTurn,
            objects = c.objects?.Where(o => o != null).Select(o => o.name).ToList() ?? new List<string>(),
            characterGroup = c.characterGroup,
        };
        Character.StatusSnapshot snapshot = c.CaptureStatusSnapshot();
        if (snapshot.statusEffectTurns != null)
            foreach (var kv in snapshot.statusEffectTurns.Where(kv => kv.Value > 0))
                save.statuses.Add(new StatusSave { status = kv.Key.ToString(), turns = kv.Value });

        Army army = c.IsArmyCommander() ? c.GetArmy() : null;
        if (army != null && !army.killed)
        {
            save.hasArmy = true;
            save.army = new ArmySave
            {
                ma = army.ma, ar = army.ar, li = army.li, hi = army.hi, lc = army.lc, hc = army.hc, ca = army.ca, ws = army.ws,
                xp = army.xp,
                groups = army.troopAbilityGroups?.Select(g => new ArmyGroupSave
                {
                    troopType = g.troopType.ToString(), amount = g.amount, troopName = g.troopName,
                    abilities = g.abilities?.Select(a => a.ToString()).ToList() ?? new List<string>()
                }).ToList() ?? new List<ArmyGroupSave>()
            };
        }
        return save;
    }

    private static IEnumerable<Leader> AllLeaders(Game game)
    {
        if (game.player != null) yield return game.player;
        foreach (PlayableLeader c in game.competitors.Where(c => c != null)) yield return c;
        if (game.npcs != null) foreach (NonPlayableLeader n in game.npcs.Where(n => n != null)) yield return n;
    }

    // ------------------------------------------------------------------ Load: restart

    /// <summary>Reads the save file and restarts the scene into it.</summary>
    public static bool BeginLoad(out string error)
    {
        error = null;
        if (!HasSave) { error = "There is no saved campaign."; return false; }
        try
        {
            SaveGame data = JsonUtility.FromJson<SaveGame>(File.ReadAllText(SavePath));
            if (data == null || data.version != Version) { error = "The saved campaign is from an incompatible version."; return false; }
            PendingLoad = data;
            GameConfig.ScenarioToLoad = data.scenario;
            GameConfig.ScenarioChosen = true;
            GameConfig.SkipIntro = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            PendingLoad = null;
            error = e.Message;
            return false;
        }
    }

    public static string PendingVariantFor(string leaderName)
    {
        if (PendingLoad == null || string.IsNullOrWhiteSpace(leaderName)) return null;
        if (string.Equals(PendingLoad.playerLeader, leaderName, StringComparison.OrdinalIgnoreCase)) return PendingLoad.playerVariant;
        return PendingLoad.competitorVariants.FirstOrDefault(v => string.Equals(v.leader, leaderName, StringComparison.OrdinalIgnoreCase))?.variant;
    }

    // ------------------------------------------------------------------ Load: apply

    /// <summary>Called by Game once the reloaded campaign has finished its normal startup.</summary>
    public static void ApplyPendingLoad(Game game)
    {
        SaveGame data = PendingLoad;
        PendingLoad = null;
        if (data == null || game == null) return;
        try
        {
            Apply(data, game);
            MessageDisplay.ShowMessage("Campaign loaded", Color.green);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            MessageDisplay.ShowMessage("The saved campaign could not be fully restored", Color.red);
        }
    }

    private static void Apply(SaveGame data, Game game)
    {
        Board board = Board.Instance;
        DeckManager deck = DeckManager.Instance;
        Dictionary<string, Leader> leadersByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (Leader l in AllLeaders(game)) leadersByName.TryAdd(l.characterName, l);

        game.turn = data.turn;

        // Treasuries.
        foreach (LeaderSave ls in data.leaders)
        {
            if (!leadersByName.TryGetValue(ls.name, out Leader l)) continue;
            l.leatherAmount = ls.leather; l.mountsAmount = ls.mounts; l.timberAmount = ls.timber;
            l.ironAmount = ls.iron; l.steelAmount = ls.steel; l.mithrilAmount = ls.mithril; l.goldAmount = ls.gold;
        }

        // PCs: ownership and state, without capture side effects (eliminations, messages).
        foreach (PcSave ps in data.pcs)
        {
            Hex hex = board.GetHex(new Vector2Int(ps.x, ps.y));
            PC pc = hex != null ? hex.GetPC() : null;
            if (pc == null) continue;
            Leader owner = ps.owner != null && leadersByName.TryGetValue(ps.owner, out Leader o) ? o : null;
            pc.RestoreOwnerForLoad(owner);
            pc.citySize = (PCSizeEnum)ps.citySize;
            pc.fortSize = (FortSizeEnum)ps.fortSize;
            pc.loyalty = ps.loyalty;
            pc.isHidden = ps.isHidden;
            pc.hiddenButRevealed = ps.hiddenButRevealed;
            pc.isCapital = ps.isCapital;
            pc.hasPort = ps.hasPort;
            hex.RedrawPC();
        }

        // Characters: match by (name, owner) in order; recruit the ones that joined later.
        List<Character> live = AllLeaders(game).SelectMany(l => l.controlledCharacters).Where(c => c != null).Distinct().ToList();
        HashSet<Character> matched = new();
        foreach (CharacterSave cs in data.characters)
        {
            Character c = live.FirstOrDefault(x => !matched.Contains(x)
                && string.Equals(x.characterName, cs.name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.GetOwner()?.characterName, cs.owner, StringComparison.OrdinalIgnoreCase));
            Leader owner = cs.owner != null && leadersByName.TryGetValue(cs.owner, out Leader o) ? o : null;
            Hex hex = board.GetHex(new Vector2Int(cs.x, cs.y));
            if (c == null && !cs.killed && owner != null && hex != null && !cs.isLeader) c = Recruit(cs, owner, hex);
            if (c == null) continue;
            matched.Add(c);
            ApplyCharacter(c, cs, hex, game);
        }
        // Characters alive at the fresh start but gone from the save have fallen since.
        foreach (Character c in live.Where(c => !matched.Contains(c) && !c.killed && !(c is Leader)))
            c.Killed(null, true);

        foreach (LeaderSave ls in data.leaders.Where(l => l.killed))
            if (leadersByName.TryGetValue(ls.name, out Leader l) && !l.killed) l.Killed(null, true);

        // The human's fog of war.
        foreach (Vec v in data.revealedHexes)
        {
            Hex hex = board.GetHex(new Vector2Int(v.x, v.y));
            if (hex != null && !hex.IsHexRevealed()) hex.Reveal(game.player);
        }

        // Hand and command points.
        if (deck != null && data.hand != null)
        {
            List<CardData> hand = data.hand.Select(n => deck.FindAnyCardByName(n)).Where(c => c != null).Select(DeckManager.CloneCard).ToList();
            deck.RestoreHand(game.player, hand, data.lastHandDrawTurn);
        }
        game.player.RestoreCommandPoints(data.commandPoints, data.turn);
        CampaignObjectives.Instance?.RestoreAchievedOnce(data.objectivesAchieved);

        board.RefreshRelevantHexes();
        foreach (Hex hex in board.hexes.Values) { if (hex == null) continue; hex.RedrawCharacters(); hex.RedrawArmies(); }
        VictoryPoints.RecalculateAndAssign(game);
        game.RefreshAfterLoad();
    }

    private static Character Recruit(CharacterSave cs, Leader owner, Hex hex)
    {
        CharacterInstantiator instantiator = UnityEngine.Object.FindFirstObjectByType<CharacterInstantiator>();
        CardData card = DeckManager.Instance?.FindAnyCardByName(cs.name);
        if (instantiator == null) return null;
        BiomeConfig config = new()
        {
            characterName = cs.name,
            alignment = card != null ? (AlignmentEnum)card.alignment : owner.GetAlignment(),
            race = card != null ? card.race : default,
            sex = card != null ? card.sex : default,
            commander = cs.commander, agent = cs.agent, emmissary = cs.emmissary, mage = cs.mage,
            artifacts = new List<string>()
        };
        Character recruited = instantiator.InstantiateCharacter(owner, hex, config);
        if (recruited != null)
        {
            recruited.startingCharacter = false;
            recruited.characterGroup = cs.characterGroup;
        }
        return recruited;
    }

    private static void ApplyCharacter(Character c, CharacterSave cs, Hex hex, Game game)
    {
        if (cs.killed)
        {
            if (!c.killed) c.Killed(null, true);
            return;
        }

        if (hex != null && c.hex != hex) Teleport(c, hex);
        c.health = cs.health;
        c.SetCommander(cs.commander); c.SetAgent(cs.agent); c.SetEmmissary(cs.emmissary); c.SetMage(cs.mage);

        DeckManager deck = DeckManager.Instance;
        if (deck != null && cs.objects != null)
            c.objects = cs.objects.Select(n => deck.FindAnyCardByName(n)).Where(o => o != null).Select(DeckManager.CloneCard).ToList();

        Character.StatusSnapshot snapshot = c.CaptureStatusSnapshot();
        snapshot.statusEffectTurns = new Dictionary<StatusEffectEnum, int>();
        foreach (StatusSave st in cs.statuses)
            if (Enum.TryParse(st.status, out StatusEffectEnum e)) snapshot.statusEffectTurns[e] = st.turns;
        foreach (StatusEffectEnum e in Enum.GetValues(typeof(StatusEffectEnum)))
            if (!snapshot.statusEffectTurns.ContainsKey(e)) snapshot.statusEffectTurns[e] = 0;
        snapshot.moved = cs.moved;
        snapshot.hasActionedThisTurn = cs.acted;
        c.RestoreStatusSnapshot(snapshot);

        ApplyArmy(c, cs);
    }

    private static void ApplyArmy(Character c, CharacterSave cs)
    {
        Army current = c.IsArmyCommander() ? c.GetArmy() : null;
        if (!cs.hasArmy || cs.army == null)
        {
            if (current != null) c.DisbandArmy(false);
            return;
        }
        if (current == null)
        {
            c.CreateArmy(TroopsTypeEnum.ma, 1, false, showSpawnMessage: false);
            current = c.GetArmy();
            if (current == null) return;
        }
        ArmySave a = cs.army;
        current.ma = a.ma; current.ar = a.ar; current.li = a.li; current.hi = a.hi;
        current.lc = a.lc; current.hc = a.hc; current.ca = a.ca; current.ws = a.ws;
        current.xp = a.xp;
        current.troopAbilityGroups = a.groups.Select(g => new ArmyTroopAbilityGroup
        {
            troopType = Enum.TryParse(g.troopType, out TroopsTypeEnum t) ? t : TroopsTypeEnum.ma,
            amount = g.amount,
            troopName = g.troopName,
            abilities = g.abilities.Select(s => Enum.TryParse(s, out ArmySpecialAbilityEnum ab) ? (ArmySpecialAbilityEnum?)ab : null)
                .Where(ab => ab.HasValue).Select(ab => ab.Value).ToList()
        }).ToList();
    }

    private static void Teleport(Character c, Hex target)
    {
        Hex from = c.hex;
        Army army = c.IsArmyCommander() ? c.GetArmy() : null;
        if (from != null)
        {
            from.characters.Remove(c);
            if (army != null) from.armies.Remove(army);
        }
        if (!target.characters.Contains(c)) target.characters.Add(c);
        if (army != null && !target.armies.Contains(army)) target.armies.Add(army);
        c.hex = target;
        c.RefreshKidnappedCharactersPosition();
    }
}

[Serializable] public class SaveGame
{
    public int version;
    public string savedAtUtc;
    public string scenario;
    public int turn;
    public string playerLeader;
    public string playerVariant;
    public int commandPoints;
    public int lastHandDrawTurn = -1;
    public List<LeaderVariantSave> competitorVariants = new();
    public List<LeaderSave> leaders = new();
    public List<CharacterSave> characters = new();
    public List<PcSave> pcs = new();
    public List<Vec> revealedHexes = new();
    public List<string> hand = new();
    public List<string> objectivesAchieved = new();
}

[Serializable] public class LeaderVariantSave { public string leader; public string variant; }
[Serializable] public class LeaderSave { public string name; public bool killed; public int leather, mounts, timber, iron, steel, mithril, gold; }
[Serializable] public class PcSave { public int x, y; public string name, owner; public int citySize, fortSize, loyalty; public bool isHidden, hiddenButRevealed, isCapital, hasPort; }
[Serializable] public class Vec { public int x, y; }
[Serializable] public class StatusSave { public string status; public int turns; }
[Serializable] public class ArmyGroupSave { public string troopType; public int amount; public string troopName; public List<string> abilities = new(); }
[Serializable] public class ArmySave { public int ma, ar, li, hi, lc, hc, ca, ws, xp; public List<ArmyGroupSave> groups = new(); }
[Serializable] public class CharacterSave
{
    public string name, owner, characterGroup;
    public bool isLeader, killed, acted, hasArmy;
    public int x, y, health, commander, agent, emmissary, mage, moved;
    public List<string> objects = new();
    public List<StatusSave> statuses = new();
    public ArmySave army;
}
