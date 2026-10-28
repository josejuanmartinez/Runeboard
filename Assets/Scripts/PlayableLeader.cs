
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

    private static bool HasCharacterCard(string name) =>
        !string.IsNullOrWhiteSpace(name) && DeckManager.Instance?.cards != null
        && DeckManager.Instance.cards.Exists(c => string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.type, "Character", StringComparison.OrdinalIgnoreCase));

    public void ApplyVariantTransformation()
    {
        if (string.IsNullOrWhiteSpace(selectedVariantCharacterName)) return;
        if (string.Equals(characterName, selectedVariantCharacterName, StringComparison.OrdinalIgnoreCase)) return;

        string fromName = characterName;
        baseCharacterName = fromName;
        characterName = selectedVariantCharacterName;
        // The flavour's own card may be named after the flavour rather than the character
        // ("The Deceiver" is Annatar, "The Dark Eye" is Sauron): prefer it when it exists.
        RefreshStatsFromCard(HasCharacterCard(selectedVariantName) ? selectedVariantName : null);
        if (Game.Instance != null && Game.Instance.player == this) Sounds.Instance?.PlayArtifactFound();
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

    /// <summary>The selected flavour's own character name (e.g. "Mithrandir"), or null for the base.</summary>
    public string GetSelectedVariantCharacterName() => selectedVariantCharacterName;

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
        if (CampaignObjectives.AvatarStart && FallBack()) return;

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
    private const int FallBackHealth = 30;
    private const int FallBackHaltTurns = 2;

    // With the avatar start a playable leader is their avatar: the whole campaign rests on one
    // figure, so they are not slain but borne back to their nearest PC (or left where they lie)
    // to recover, halted for a while (their surviving army goes with them).
    private bool FallBack()
    {
        if (hex == null) return false;
        Hex refuge = controlledPcs
            .Where(pc => pc != null && pc.owner == this && pc.hex != null)
            .Select(pc => pc.hex)
            .OrderBy(h => (h.transform.position - hex.transform.position).sqrMagnitude)
            .FirstOrDefault() ?? hex;

        if (refuge != hex)
        {
            Hex from = hex;
            Army army = IsArmyCommander() && !GetArmy().killed ? GetArmy() : null;
            from.characters.Remove(this);
            if (!refuge.characters.Contains(this)) refuge.characters.Add(this);
            if (army != null)
            {
                from.armies.Remove(army);
                if (!refuge.armies.Contains(army)) refuge.armies.Add(army);
            }
            hex = refuge;
            from.RedrawCharacters();
            refuge.RedrawCharacters();
            if (army != null) { from.RedrawArmies(); refuge.RedrawArmies(); }
        }
        health = FallBackHealth;
        Halt(FallBackHaltTurns);
        string place = refuge.GetPCData()?.pcName;
        MessageDisplayNoUI.ShowMessage(refuge, this, string.IsNullOrWhiteSpace(place)
            ? $"{characterName} falls, but is not slain"
            : $"{characterName} falls, and is borne back to {place} to recover", Color.yellow);
        CharacterIcons.RefreshForHumanPlayerCharacter(this);
        Debug.Log($"[AvatarStart] {characterName} fell and recovers at {refuge.v2}.");
        return true;
    }

    new public void NewTurn()
    {
        PlayableLeaderIcons leaderIcons = FindAnyObjectByType<PlayableLeaderIcons>();
        leaderIcons?.HighlightCurrentlyPlaying(this);

        base.NewTurn();
    }

}
