
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PlayableLeader : Leader
{
    [System.NonSerialized] public VictoryPoints victoryPoints;
    private readonly HashSet<string> discoveredRegions = new(StringComparer.OrdinalIgnoreCase);
    private string selectedSubdeckId;
    private string selectedDeckIdentity;
    private string selectedLeaderDescription;
    private string selectedVariantName;
    private string selectedVariantCharacterName;
    private string baseCharacterName;

    // The base leader name this instance was spawned as, before any variant transformation
    // overwrote characterName — null if no transformation has happened yet. Used by
    // CharacterAnimationController to fall back to the base leader's sprites when the variant
    // itself has no baked spritesheet of its own.
    public override string SpriteVariantBaseName =>
        string.IsNullOrWhiteSpace(baseCharacterName) || string.Equals(baseCharacterName, characterName, StringComparison.OrdinalIgnoreCase)
            ? null
            : baseCharacterName;

    // Set by NationSpawner when this instance was spawned from a scenario's self-owned character
    // card — its hex *is* an authored starting point (whether or not a specific variant was
    // named), so Game.RandomizeCompetitorVariants must not override it with a random pick, and
    // LeaderSelector must show exactly one carousel entry for it instead of the full variant menu.
    public bool scenarioVariantLocked;

    // ---- Command points: the human leader's per-turn action budget (replaces "one card per
    // character per turn"). Any character may spend them; refreshed lazily whenever the game
    // turn changes so no turn-start hook ordering matters.
    public const int BaseCommandPoints = 3;
    public const int MaxCommandPoints = 6;
    private int commandPoints;
    private int commandPointsTurn = -1;
    public event Action CommandPointsChanged;

    public int CommandPointsMax => Mathf.Clamp(BaseCommandPoints + controlledPcs.Count(pc => pc != null && pc.owner == this) / 4, BaseCommandPoints, MaxCommandPoints);

    public int CommandPoints
    {
        get { SyncCommandPoints(); return commandPoints; }
    }

    private void SyncCommandPoints()
    {
        int turn = Game.Instance != null ? Game.Instance.turn : 0;
        if (turn == commandPointsTurn) return;
        commandPointsTurn = turn;
        commandPoints = CommandPointsMax;
    }

    public void SpendCommandPoint()
    {
        SyncCommandPoints();
        commandPoints = Mathf.Max(0, commandPoints - 1);
        CommandPointsChanged?.Invoke();
    }

    public void GrantCommandPoint()
    {
        SyncCommandPoints();
        commandPoints = Mathf.Min(MaxCommandPoints, commandPoints + 1);
        CommandPointsChanged?.Invoke();
    }

    public void RestoreCommandPoints(int points, int turn)
    {
        commandPoints = points;
        commandPointsTurn = turn;
        CommandPointsChanged?.Invoke();
    }

    private static string NormalizeCardName(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return string.Empty;
        return new string(cardName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    new public void Initialize(Hex hex, LeaderBiomeConfig playableLeaderBiome, bool showSpawnMessage = true, bool applyNoScenarioStart = false)
    {
        base.Initialize(hex, playableLeaderBiome, showSpawnMessage, applyNoScenarioStart);
        victoryPoints = null;
        discoveredRegions.Clear();
        selectedSubdeckId = playableLeaderBiome?.subdeckId;
        selectedDeckIdentity = playableLeaderBiome?.deckIdentity;
        selectedLeaderDescription = playableLeaderBiome?.description;
        selectedVariantName = null;
        baseCharacterName = null;
        RefreshStatsFromCard();
    }

    public void SetDeckSelection(string subdeckId, string deckIdentity = null, string leaderDescription = null, string variantName = null, string variantCharacterName = null)
    {
        selectedSubdeckId = subdeckId;
        selectedDeckIdentity = deckIdentity;
        selectedLeaderDescription = leaderDescription;
        selectedVariantName = variantName;
        selectedVariantCharacterName = variantCharacterName;
    }

    public void RefreshStatsFromCard(string name = null)
    {
        string lookup = string.IsNullOrWhiteSpace(name) ? characterName : name;
        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        CardData card = deckManager?.cards?.Find(c =>
            string.Equals(c.name, lookup, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.type, "Character", StringComparison.OrdinalIgnoreCase));
        ApplyStatsFromCard(card);
    }

    public void ApplyVariantTransformation()
    {
        if (string.IsNullOrWhiteSpace(selectedVariantCharacterName)) return;
        if (string.Equals(characterName, selectedVariantCharacterName, StringComparison.OrdinalIgnoreCase)) return;

        string fromName = characterName;
        baseCharacterName = fromName;
        characterName = selectedVariantCharacterName;
        RefreshStatsFromCard();
        Sounds.Instance?.PlayArtifactFound();
        CharacterIcons.RefreshForHumanPlayerOf(this);
    }

    public string GetSelectedSubdeckId()
    {
        return string.IsNullOrWhiteSpace(selectedSubdeckId) ? GetBiome()?.subdeckId : selectedSubdeckId;
    }

    public string GetSelectedDeckIdentity()
    {
        return string.IsNullOrWhiteSpace(selectedDeckIdentity) ? GetBiome()?.deckIdentity : selectedDeckIdentity;
    }

    public string GetSelectedLeaderDescription()
    {
        return string.IsNullOrWhiteSpace(selectedLeaderDescription) ? GetBiome()?.description : selectedLeaderDescription;
    }

    public string GetSelectedVariantName()
    {
        return selectedVariantName; 
    }

    public bool TryDiscoverRegion(string region)
    {
        if (string.IsNullOrWhiteSpace(region)) return false;
        return discoveredRegions.Add(NormalizeCardName(region));
    }

    public bool HasDiscoveredRegion(string region)
    {
        if (string.IsNullOrWhiteSpace(region)) return false;
        return discoveredRegions.Contains(NormalizeCardName(region));
    }

    public bool HasPlayedLandCardThisTurn()
    {
        return HasPlayedLandThisTurn();
    }

    override public void Killed(Leader killedBy, bool onlyMask = false)
    {
        if (killed) return;

        PlayableLeaderIcons leaderIcons = FindAnyObjectByType<PlayableLeaderIcons>();
        leaderIcons?.AddDeadIcon(this);

        health = 0;
        killed = true;

        if (Game.Instance.player == this)
        {
            Game.Instance.EndGame(false);
            return;
        }

        Game.Instance.competitors.Remove(this);

        base.Killed(killedBy);
    }
    new public void NewTurn()
    {
        PlayableLeaderIcons leaderIcons = FindAnyObjectByType<PlayableLeaderIcons>();
        leaderIcons?.HighlightCurrentlyPlaying(this);

        base.NewTurn();
    }

}
