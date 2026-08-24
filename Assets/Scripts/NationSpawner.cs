using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using System;
using RetroLOTR.Scenarios;

[RequireComponent(typeof(Board))]
public class NationSpawner : MonoBehaviour
{
    private sealed class RegionSeed
    {
        public readonly Vector2Int position;
        public readonly string region;

        public RegionSeed(Vector2Int position, string region)
        {
            this.position = position;
            this.region = region;
        }
    }

    public Board board;

    private PlayableLeaders playableLeaders;
    private NonPlayableLeaders nonPlayableLeaders;
    private List<Vector2Int> placedPositions;
    private CharacterInstantiator characterInstantiator;
    private Dictionary<TerrainEnum, List<Vector2Int>> terrainHexCache;
    private Dictionary<FeaturesEnum, List<Vector2Int>> featuresHexCache;
    private Dictionary<Vector2Int, Vector3Int> cubeCoordinateCache;
    private int currentCharacterCount;
    private int currentPcCount;
    private bool isInitialized = false;
    private readonly Dictionary<string, Vector2Int> leaderPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Vector2Int>> startingCityPositionsByRegion = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TerrainEnum[] StartFallbackTerrains =
        { TerrainEnum.plains, TerrainEnum.grasslands, TerrainEnum.hills, TerrainEnum.shore };
    // Every land (non-water) terrain a leader may legitimately stand on. SelectClosestPosition
    // searches all of these so a leader always lands next to its own anchor, even if the only
    // hexes immediately around the anchor are an "off-list" terrain like mountains or desert.
    private static readonly TerrainEnum[] LandTerrains =
    {
        TerrainEnum.plains, TerrainEnum.grasslands, TerrainEnum.shore, TerrainEnum.hills,
        TerrainEnum.forest, TerrainEnum.swamp, TerrainEnum.desert, TerrainEnum.wastelands,
        TerrainEnum.mountains
    };
    private bool landRegionsAssigned;

    // Scenario PCs/characters authored with an ownerVariantId, recorded at spawn time and
    // resolved once every playable leader's variant choice is final. See
    // ReconcileScenarioVariantOwnership, called from Game.StartGame(). fallbackOwnerName mirrors
    // ScenarioPC/ScenarioCharacter.fallbackOwnerName: empty = destroy on mismatch, otherwise the
    // Non-Playable Leader name to reassign as owner instead.
    private readonly List<(PC pc, string requiredVariantId, string fallbackOwnerName)> pcVariantOwnership = new();
    private readonly List<(Character character, string requiredVariantId, string fallbackOwnerName)> characterVariantOwnership = new();
    // Characters/armies authored with an independent spawnCondition (any leader + variant, not
    // necessarily this entity's owner). Recorded at spawn time and resolved once every playable
    // leader's variant choice is final, alongside pcVariantOwnership/characterVariantOwnership
    // above. Unlike ownerVariantId there is no fallback on mismatch — see
    // ReconcileScenarioSpawnConditions.
    private readonly List<(Character character, string requiredLeaderName, string requiredVariantId, bool exclude)> characterSpawnConditions = new();
    private readonly List<(Character commander, string requiredLeaderName, string requiredVariantId, bool exclude)> armySpawnConditions = new();
    // Leaders spawned so far while resolving a scenario, keyed by name — populated during
    // SpawnFromScenario and reused by ReconcileScenarioVariantOwnership to find or lazily spawn a
    // fallback Non-Playable Leader once variant choices are final.
    private readonly Dictionary<string, Leader> scenarioLeadersByName = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(Board board)
    {
        if (board == null)
        {
            Debug.LogError("Board is null in NationSpawner.Initialize!");
            return;
        }

        this.board = board;
        placedPositions = new List<Vector2Int>(20); // Pre-allocate for typical number of leaders
        terrainHexCache = new Dictionary<TerrainEnum, List<Vector2Int>>();
        featuresHexCache = new Dictionary<FeaturesEnum, List<Vector2Int>>();
        cubeCoordinateCache = new Dictionary<Vector2Int, Vector3Int>();

        characterInstantiator = FindFirstObjectByType<CharacterInstantiator>();
        if (characterInstantiator == null)
        {
            Debug.LogError("CharacterInstantiator not found!");
            return;
        }

        playableLeaders = FindFirstObjectByType<PlayableLeaders>();
        if (playableLeaders == null)
        {
            Debug.LogError("PlayableLeaders not found!");
            return;
        }
        playableLeaders.Initialize();

        nonPlayableLeaders = FindFirstObjectByType<NonPlayableLeaders>();
        if (nonPlayableLeaders == null)
        {
            Debug.LogError("NonPlayableLeaders not found!");
            return;
        }
        nonPlayableLeaders.Initialize();

        isInitialized = true;
    }

    public void BuildTerrainHexCache(TerrainEnum[,] terrainGrid)
    {
        featuresHexCache[FeaturesEnum.river] = board.boardGenerator.riverCoastHexes.ToList();
        featuresHexCache[FeaturesEnum.lake] = board.boardGenerator.lakeCoastHexes.ToList();

        if (terrainGrid == null)
        {
            Debug.LogError("terrainGrid is null in BuildTerrainHexCache!");
            return;
        }

        terrainHexCache.Clear();
        for (int x = 0; x < board.GetHeight(); x++)
        {
            for (int y = 0; y < board.GetWidth(); y++)
            {
                var terrain = terrainGrid[x, y];
                if (!terrainHexCache.ContainsKey(terrain))
                {
                    terrainHexCache[terrain] = new List<Vector2Int>();
                }
                terrainHexCache[terrain].Add(new Vector2Int(x, y));
            }
        }
    }

    private void RecountExistingEntities()
    {
        currentCharacterCount = 0;
        currentPcCount = 0;

        if (board?.hexes == null)
            return;

        foreach (var hex in board.hexes.Values)
        {
            if (hex == null) continue;
            currentCharacterCount += hex.characters?.Count ?? 0;
            if (hex.GetPC() != null) currentPcCount++;
        }
    }

    private bool EnsureCharacterCapacity(string context)
    {
        if (currentCharacterCount >= Game.MAX_CHARACTERS)
        {
            Debug.LogWarning($"Max characters reached. {context}");
            return false;
        }
        return true;
    }

    private bool EnsurePcCapacity()
    {
        if (currentPcCount >= Game.MAX_PCS)
        {
            Debug.LogWarning("Max PCs reached. Skipping PC instantiation.");
            return false;
        }
        return true;
    }

    public void Spawn()
    {
        if (!isInitialized)
        {
            Debug.LogError("NationSpawner not initialized!");
            return;
        }

        if (board.terrainGrid == null)
        {
            Debug.LogError("terrainGrid is not initialized!");
            return;
        }

        RecountExistingEntities();
        leaderPositions.Clear();
        startingCityPositionsByRegion.Clear();
        // placedPositions is only allocated in Initialize(); clear it here so a board
        // regeneration in the same session doesn't spread new nations against stale,
        // last-board coordinates.
        placedPositions.Clear();

        InstantiateLeadersAndCharacters(playableLeaders.playableLeaders.biomes, placedPositions);
        InstantiateLeadersAndCharacters(nonPlayableLeaders.nonPlayableLeaders.biomes, placedPositions);
        AssignLandRegions();
    }

    // ----------------------------------------------------------------------------------------
    // Scenario-driven spawning.
    //
    // Authored scenarios place everything at fixed coordinates instead of running the random
    // spread/closest placement above. Shared leaders and cards are referenced by name, so the
    // leader biomes and decks stay the single source of truth for stats/sprites/abilities.
    // ----------------------------------------------------------------------------------------
    public void SpawnFromScenario(ScenarioData scenario)
    {
        if (!isInitialized)
        {
            Debug.LogError("NationSpawner not initialized!");
            return;
        }
        if (scenario == null)
        {
            Debug.LogError("SpawnFromScenario called with a null scenario.");
            return;
        }
        if (board.terrainGrid == null)
        {
            Debug.LogError("terrainGrid is not initialized!");
            return;
        }

        RecountExistingEntities();
        leaderPositions.Clear();
        placedPositions.Clear();
        pcVariantOwnership.Clear();
        characterVariantOwnership.Clear();
        characterSpawnConditions.Clear();
        armySpawnConditions.Clear();
        scenarioLeadersByName.Clear();

        // An authored scenario's content is the author's call — never clamp it to the procedural
        // spawn caps (a hand-built map easily exceeds them, and EnsurePcCapacity/
        // EnsureCharacterCapacity were silently skipping every entry past the cap). Grow the
        // global caps to fit everything authored, plus headroom for the starting cities that
        // implicitly-spawned owner leaders bring and anything founded during play.
        const int ScenarioCapHeadroom = 64;
        Game.MAX_PCS = Mathf.Max(Game.MAX_PCS, (scenario.pcs?.Count ?? 0) + ScenarioCapHeadroom);
        Game.MAX_CHARACTERS = Mathf.Max(Game.MAX_CHARACTERS, (scenario.characters?.Count ?? 0) + ScenarioCapHeadroom);

        var phaseTimer = System.Diagnostics.Stopwatch.StartNew();
        long Lap() { long ms = phaseTimer.ElapsedMilliseconds; phaseTimer.Restart(); return ms; }

        // Apply authored tile variations first so each hex's features are set before placement.
        ApplyScenarioTerrainSprites(scenario);
        long spritesMs = Lap();

        DeckManager deckManager = ResolveDeckManager();
        long deckMs = Lap();

        // 1. Leaders — a leader's presence in a scenario is a character card that is either
        // self-owned (ownerLeaderName == characterName, optionally naming a variantId) or a
        // playable leader's VARIANT character owned by its base leader (e.g. "The White Hand"
        // owned by "Saruman"): that hex is where the instance starts, and its army (if any) is
        // its starting army. Whether the name is playable or non-playable is looked up from the
        // biome JSONs, never authored separately (see FindLeaderBiome / IsPlayableVariantCard).
        //
        // A scenario can therefore author the same playable leader at several hexes, one per
        // variant (e.g. five different Sauron starts) so each shows as its own carousel entry —
        // every one spawns here, immediately locked to its authored variant, and unselected
        // siblings are pruned down to a single survivor once selection is final
        // (see Game.PruneUnselectedLeaderVariants).
        foreach (ScenarioCharacter selfCard in scenario.characters ?? new List<ScenarioCharacter>())
        {
            LeaderVariantConfig authoredVariant = null;
            if (!IsSelfOwnedLeaderCard(selfCard, out LeaderBiomeConfig playableBiome, out NonPlayableLeaderBiomeConfig nplBiome)
                && !IsPlayableVariantCard(selfCard, out playableBiome, out authoredVariant)) continue;
            if (!TryGetScenarioHex(scenario, selfCard.row, selfCard.col, out Hex hex)) continue;
            if (!EnsureCharacterCapacity($"Skipping leader '{selfCard.characterName}'.")) continue;

            Leader leader = playableBiome != null
                ? characterInstantiator.InstantiatePlayableLeader(hex, playableBiome)
                : characterInstantiator.InstantiateNonPlayableLeader(hex, nplBiome);

            ApplyStartingObjects(leader, selfCard.startingObjects, deckManager);
            currentCharacterCount++;
            placedPositions.Add(hex.v2);
            // Multiple sibling instances can share this name pre-selection; PCs/characters that
            // reference it as an owner just need *a* representative, so last-one-wins is fine.
            scenarioLeadersByName[selfCard.characterName] = leader;
            leaderPositions[selfCard.characterName] = hex.v2;
            // A variant card is also keyed under its base leader name, so PCs/characters authored
            // with the base owner name (the common case) resolve to this instance instead of
            // lazily spawning a duplicate, unlocked leader in EnsureLeaderSpawned.
            if (playableBiome != null && !string.Equals(playableBiome.characterName, selfCard.characterName, StringComparison.OrdinalIgnoreCase))
            {
                scenarioLeadersByName[playableBiome.characterName] = leader;
                leaderPositions[playableBiome.characterName] = hex.v2;
            }

            if (playableBiome != null && leader is PlayableLeader playableInstance)
            {
                // Any authored card fixes this instance's identity/position for the scenario —
                // never a free variant pick or a random re-roll in RandomizeCompetitorVariants —
                // whether or not a specific variant was named (a self-owned card with a blank
                // variantId just means "the base flavor, at this hex" rather than "no scenario
                // opinion at all").
                playableInstance.scenarioVariantLocked = true;

                LeaderVariantConfig variant = authoredVariant;
                if (variant == null && !string.IsNullOrWhiteSpace(selfCard.variantId))
                {
                    variant = playableBiome.variants?.Find(v =>
                        v != null && string.Equals(v.variantId, selfCard.variantId, StringComparison.OrdinalIgnoreCase));
                }
                if (variant != null)
                {
                    string subdeckId = string.IsNullOrWhiteSpace(variant.subdeckId) ? playableBiome.subdeckId : variant.subdeckId;
                    playableInstance.SetDeckSelection(subdeckId, variant.deckIdentity, playableBiome.description, variant.displayName, variant.characterName);
                }
            }

            if (selfCard.army != null && !selfCard.army.IsEmpty())
                BuildScenarioArmy(leader, selfCard.army, deckManager);

            // Non-playable self-owned leader cards (e.g. Faramir) have no carousel/pruning
            // lifecycle to race — unlike playable self-owned/variant cards (nplBiome == null,
            // excluded here), it's safe to gate the whole identity on an independent
            // spawnCondition. See ReconcileScenarioSpawnConditions for how the removal (which
            // must also clean up anything this leader owns) differs from a plain character.
            if (nplBiome != null && !string.IsNullOrWhiteSpace(selfCard.spawnConditionLeaderName))
                characterSpawnConditions.Add((leader, selfCard.spawnConditionLeaderName, selfCard.spawnConditionVariantId, selfCard.spawnConditionExclude));
        }
        long leadersMs = Lap();

        // 2. PCs (cities). Owner may be null (ownerless anchor city).
        foreach (ScenarioPC spc in scenario.pcs ?? new List<ScenarioPC>())
        {
            if (spc == null || string.IsNullOrWhiteSpace(spc.pcName)) continue;
            if (!TryGetScenarioHex(scenario, spc.row, spc.col, out Hex hex)) continue;
            if (!EnsurePcCapacity()) continue;

            Leader owner = EnsureLeaderSpawned(scenarioLeadersByName, spc.ownerLeaderName, hex);
            PCSizeEnum size = (PCSizeEnum)Mathf.Clamp(spc.citySize, (int)PCSizeEnum.camp, (int)PCSizeEnum.city);
            FortSizeEnum fort = (FortSizeEnum)Mathf.Clamp(spc.fortSize, (int)FortSizeEnum.NONE, (int)FortSizeEnum.citadel);

            PC pc = new(owner, spc.pcName, size, fort, spc.hasPort, spc.isHidden, hex, spc.isCapital, spc.loyalty);
            pc.isUnderground = spc.isUnderground;
            hex.SetPC(pc, spc.pcFeature, spc.fortFeature, spc.isIsland);
            currentPcCount++;

            if (!string.IsNullOrWhiteSpace(spc.ownerVariantId) && owner is PlayableLeader)
                pcVariantOwnership.Add((pc, spc.ownerVariantId, spc.fallbackOwnerName));
        }
        long pcsMs = Lap();

        // 3. Characters and their armies.
        foreach (ScenarioCharacter sc in scenario.characters ?? new List<ScenarioCharacter>())
        {
            if (sc == null || string.IsNullOrWhiteSpace(sc.characterName)) continue;
            // Self-owned leader identity cards and playable variant cards were already folded
            // into that leader's own spawn (hex + army) in step 1 above — skip so we don't spawn
            // a duplicate leader/character instance.
            if (IsSelfOwnedLeaderCard(sc, out _, out _) || IsPlayableVariantCard(sc, out _, out _)) continue;
            if (!TryGetScenarioHex(scenario, sc.row, sc.col, out Hex hex)) continue;
            if (!EnsureCharacterCapacity($"Skipping character '{sc.characterName}'.")) continue;

            Leader owner = EnsureLeaderSpawned(scenarioLeadersByName, sc.ownerLeaderName, hex);
            if (owner == null)
            {
                Debug.LogWarning($"Scenario character '{sc.characterName}' has no resolvable owner '{sc.ownerLeaderName}'; skipping.");
                continue;
            }

            // A character entry whose name matches a Non-Playable Leader's identity is spawned
            // as a full NonPlayableLeader (not a generic Character) purely so it renders/behaves
            // like that NPL; what happens to it on an owner-variant mismatch is governed entirely
            // by fallbackOwnerName below, not by this identity check.
            Character character;
            NonPlayableLeaderBiomeConfig nplBiome = FindNplBiomeByCharacterName(sc.characterName);
            if (nplBiome != null)
            {
                NonPlayableLeader npl = characterInstantiator.InstantiateNonPlayableLeader(hex, nplBiome);
                npl.owner = owner;
                owner.controlledCharacters.Add(npl);
                character = npl;
            }
            else
            {
                character = SpawnScenarioCharacter(owner, hex, sc.characterName, deckManager);
            }
            if (character == null) continue;
            ApplyStartingObjects(character, sc.startingObjects, deckManager);
            currentCharacterCount++;

            if (sc.army != null && !sc.army.IsEmpty())
                BuildScenarioArmy(character, sc.army, deckManager);

            if (!string.IsNullOrWhiteSpace(sc.ownerVariantId) && owner is PlayableLeader)
                characterVariantOwnership.Add((character, sc.ownerVariantId, sc.fallbackOwnerName));

            // Companion characters only — a self-owned leader/variant card's presence is governed
            // by the selection carousel (step 1 above), not by this independent spawn gate.
            if (!string.IsNullOrWhiteSpace(sc.spawnConditionLeaderName))
                characterSpawnConditions.Add((character, sc.spawnConditionLeaderName, sc.spawnConditionVariantId, sc.spawnConditionExclude));
        }
        long charactersMs = Lap();

        // 4. Regions — authored paint wins; gaps are flood-filled from the painted hexes.
        ApplyScenarioRegions(scenario);
        long regionsMs = Lap();

        Debug.Log($"[ScenarioLoad] spawn phases — sprites {spritesMs} ms, decks {deckMs} ms, leaders {leadersMs} ms, PCs {pcsMs} ms, characters {charactersMs} ms, regions {regionsMs} ms");
        landRegionsAssigned = true;
    }

    // Resolves every scenario PC/character authored with an ownerVariantId, once each playable
    // leader's variant choice is final (the human's pick plus Game.RandomizeCompetitorVariants
    // for AI-controlled leaders). Called from Game.StartGame(). No-op in procedural/campaign play,
    // since nothing gets added to these lists outside SpawnFromScenario.
    public void ReconcileScenarioVariantOwnership()
    {
        foreach ((PC pc, string requiredVariantId, string fallbackOwnerName) in pcVariantOwnership)
        {
            if (pc == null || pc.owner is not PlayableLeader playableOwner) continue;
            if (string.Equals(playableOwner.GetSelectedSubdeckId(), requiredVariantId, StringComparison.OrdinalIgnoreCase)) continue;

            Leader fallback = EnsureFallbackNplSpawned(fallbackOwnerName, pc.hex);
            if (fallback == null)
            {
                RemoveUnresolvedScenarioPc(pc);
                continue;
            }
            playableOwner.controlledPcs.Remove(pc);
            playableOwner.visibleHexes.Remove(pc.hex);
            pc.owner = fallback;
            fallback.controlledPcs.Add(pc);
            fallback.visibleHexes.Add(pc.hex);
        }
        pcVariantOwnership.Clear();

        foreach ((Character character, string requiredVariantId, string fallbackOwnerName) in characterVariantOwnership)
        {
            if (character == null || character.owner is not PlayableLeader playableOwner) continue;
            if (string.Equals(playableOwner.GetSelectedSubdeckId(), requiredVariantId, StringComparison.OrdinalIgnoreCase)) continue;

            if (character is NonPlayableLeader &&
                string.Equals(character.characterName, fallbackOwnerName, StringComparison.OrdinalIgnoreCase))
            {
                // The character already IS this Non-Playable Leader identity — let it self-own
                // instead of spawning a second instance under the same name.
                playableOwner.controlledCharacters.Remove(character);
                character.owner = null;
                continue;
            }

            Leader fallback = EnsureFallbackNplSpawned(fallbackOwnerName, character.hex);
            if (fallback == null)
            {
                RemoveUnresolvedScenarioCharacter(character);
                continue;
            }
            playableOwner.controlledCharacters.Remove(character);
            character.owner = fallback;
            fallback.controlledCharacters.Add(character);
        }
        characterVariantOwnership.Clear();
    }

    // Resolves every scenario character/army authored with an independent spawnCondition, once
    // every playable leader's variant choice is final (same timing as
    // ReconcileScenarioVariantOwnership, called right after it from Game.StartGame). Unlike
    // ownerVariantId this isn't about ownership — a failed condition (including a referenced
    // leader that was never authored/spawned, or an NPL, which has no variants) simply removes
    // the character or kills the army outright, no fallback owner.
    public void ReconcileScenarioSpawnConditions()
    {
        // Precomputed once: SpawnFromScenario step 1 lets a scenario author the SAME
        // non-playable-leader identity at several hexes as mutually exclusive alternates (e.g.
        // "Beoraborn lives at hex A unless playing the Necromancer, in which case he's at hex B
        // instead" — two self-owned "Beoraborn" cards with opposite Requires/Excludes
        // spawnConditions). But spawning always binds that identity's PC/character ownership to
        // whichever same-named self-owned card happened to be registered LAST in
        // scenarioLeadersByName ("last one wins", see the comment at that assignment) — entirely
        // independent of which sibling's own spawnCondition ultimately passes. Without accounting
        // for that here, the sibling whose condition passes can still be the empty one, while the
        // sibling actually holding the identity's PCs/characters gets destroyed for failing its
        // condition. So: look up each failing self-owned leader's same-named sibling that DID pass,
        // and hand ownership off to it (mirroring PruneUnselectedLeaderVariants' survivor transfer)
        // instead of destroying everything that identity owns.
        Dictionary<Character, bool> satisfied = characterSpawnConditions.ToDictionary(
            e => e.character,
            e => SpawnConditionSatisfied(e.requiredLeaderName, e.requiredVariantId, e.exclude));

        Dictionary<string, List<Leader>> selfOwnedSiblingsByName = characterSpawnConditions
            .Where(e => e.character is Leader l && l.owner == null && !string.IsNullOrWhiteSpace(l.characterName))
            .GroupBy(e => e.character.characterName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Select(e => (Leader)e.character).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach ((Character character, string requiredLeaderName, string requiredVariantId, bool exclude) in characterSpawnConditions)
        {
            if (character == null || character.hex == null) continue; // already removed by something else
            if (satisfied[character]) continue;

            // A non-playable self-owned leader card (e.g. Faramir) registers itself here too (see
            // SpawnFromScenario step 1) — unlike a plain companion, it can own PCs/characters of
            // its own, so removing it must cascade the same way a pruned leader-variant sibling
            // does (RemoveUnselectedScenarioLeader's survivor transfer), not just detach it from
            // its own owner and destroy it.
            if (character is Leader selfOwnedLeader && character.owner == null)
            {
                Leader survivor = null;
                if (selfOwnedSiblingsByName.TryGetValue(character.characterName, out List<Leader> siblings))
                    survivor = siblings.FirstOrDefault(sib => sib != selfOwnedLeader && satisfied.TryGetValue(sib, out bool ok) && ok);

                RemoveUnselectedScenarioLeader(selfOwnedLeader, survivor);
                if (survivor != null) scenarioLeadersByName[character.characterName] = survivor;
            }
            else
            {
                RemoveUnresolvedScenarioCharacter(character);
            }
        }
        characterSpawnConditions.Clear();

        foreach ((Character commander, string requiredLeaderName, string requiredVariantId, bool exclude) in armySpawnConditions)
        {
            if (commander == null) continue;
            if (SpawnConditionSatisfied(requiredLeaderName, requiredVariantId, exclude)) continue;
            if (commander.IsArmyCommander() && commander.GetArmy() != null && !commander.GetArmy().killed)
                commander.GetArmy().Killed(null, false);
        }
        armySpawnConditions.Clear();
    }

    // "Requires" (exclude = false, the default): satisfied only when the named leader IS playing
    // with requiredVariantId. "Excludes" (exclude = true): satisfied only when it is NOT — either
    // that leader is absent from the game entirely, or present with a different variant.
    private bool SpawnConditionSatisfied(string requiredLeaderName, string requiredVariantId, bool exclude)
    {
        bool met = SpawnConditionMet(requiredLeaderName, requiredVariantId);
        return exclude ? !met : met;
    }

    // True when the named leader (the human player or an AI competitor currently in the game,
    // looked up by characterName) ended up with the required variant selected. An empty
    // requiredVariantId means that leader's Base flavor. A leader that can't be found — typo, or
    // a Non-Playable Leader, which has no variants — never satisfies a named condition.
    private bool SpawnConditionMet(string requiredLeaderName, string requiredVariantId)
    {
        return ResolvePlayableLeaderInstance(requiredLeaderName, requiredVariantId) != null;
    }

    // Mirrors Game.ResolveBannerSprite's subdeckId -> variant lookup: returns the variantId of the
    // leader's currently selected variant, or "" for the Base flavor / an unresolved subdeck.
    private static string ResolveSelectedVariantId(PlayableLeader leader)
    {
        LeaderBiomeConfig biome = leader.GetBiome();
        string subdeckId = leader.GetSelectedSubdeckId();
        if (biome?.variants == null || string.IsNullOrWhiteSpace(subdeckId)) return "";

        LeaderVariantConfig variant = biome.variants.Find(v =>
            v != null &&
            ((!string.IsNullOrWhiteSpace(v.variantId) && string.Equals(v.variantId, subdeckId, StringComparison.OrdinalIgnoreCase)) ||
             (!string.IsNullOrWhiteSpace(v.subdeckId) && string.Equals(v.subdeckId, subdeckId, StringComparison.OrdinalIgnoreCase))));
        return variant?.variantId ?? "";
    }

    // Shared by SpawnConditionMet and ApplyScenarioZoneOfControl: finds the live playable-leader
    // instance (the human or an AI competitor currently in the game, looked up by characterName)
    // whose currently selected variant equals variantId ("" = Base), or null if no leader by that
    // name is in this game at all, or it's in with a different variant.
    //
    // Deliberately searches Game.player/competitors rather than scanning every PlayableLeader in
    // the scene (as this used to via FindObjectsByType). Game.StartGame calls
    // PruneUnselectedLeaderVariants — which Destroy()s the losing variant siblings of any leader
    // authored at several hexes — and then, synchronously in the same frame, calls
    // ReconcileScenarioSpawnConditions/ApplyScenarioZoneOfControl (which reach here). Unity defers
    // Destroy() to end of frame, so a scene-wide scan can still find, and non-deterministically
    // prefer, an already-pruned sibling authored with a different variant — silently and
    // intermittently reporting the wrong variant (or none) for a leader that's actually still in
    // play. player/competitors don't have this problem: PruneUnselectedLeaderVariants removes a
    // losing sibling from competitors before destroying it, so only real survivors ever appear here.
    private PlayableLeader ResolvePlayableLeaderInstance(string characterName, string variantId)
    {
        Game game = Game.Instance;
        if (game == null) return null;

        IEnumerable<PlayableLeader> candidates = game.competitors != null
            ? new[] { game.player }.Concat(game.competitors)
            : new[] { game.player };

        foreach (PlayableLeader leader in candidates)
        {
            if (leader == null || !string.Equals(leader.characterName, characterName, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(ResolveSelectedVariantId(leader), variantId ?? "", StringComparison.OrdinalIgnoreCase)) return leader;
        }
        return null;
    }

    // Resolves the Non-Playable Leader an unresolved PC/character should fall back to on an
    // owner-variant mismatch (ScenarioPC/ScenarioCharacter.fallbackOwnerName), spawning it lazily
    // at the given hex the first time this scenario needs it. Empty name (the default, meaning
    // "destroy instead") or an unrecognized name both return null.
    private Leader EnsureFallbackNplSpawned(string nplName, Hex hex)
    {
        if (string.IsNullOrWhiteSpace(nplName)) return null;
        if (scenarioLeadersByName.TryGetValue(nplName, out Leader existing)) return existing;

        NonPlayableLeaderBiomeConfig nplBiome = FindNplBiomeByCharacterName(nplName);
        if (nplBiome == null)
        {
            Debug.LogWarning($"Scenario fallbackOwnerName '{nplName}' is not a known Non-Playable Leader.");
            return null;
        }
        if (!EnsureCharacterCapacity($"Skipping fallback owner '{nplName}'.")) return null;

        Leader leader = characterInstantiator.InstantiateNonPlayableLeader(hex, nplBiome);
        currentCharacterCount++;
        scenarioLeadersByName[nplName] = leader;
        leaderPositions[nplName] = hex.v2;
        return leader;
    }

    // Removes a scenario character that turned out to belong to a leader-variant that wasn't
    // chosen this game — as if it had never been spawned, mirroring the existing "no resolvable
    // owner" skip in SpawnFromScenario. Bookkeeping mirrors Character.Killed() minus the death
    // messaging/effects, since the game hasn't visibly started yet.
    private void RemoveUnresolvedScenarioCharacter(Character character)
    {
        Leader owner = character.GetOwner();
        Hex formerHex = character.hex;

        if (character.IsArmyCommander() && character.GetArmy() != null && !character.GetArmy().killed)
            character.GetArmy().Killed(null, false);

        owner?.controlledCharacters.Remove(character);
        if (formerHex != null && formerHex.characters.Contains(character))
            formerHex.characters.Remove(character);

        // Conditional scenario characters are spawned before every variant choice is final.
        // Their constructor grants their owner visibility from the starting hex, so a failed
        // Requires/Excludes condition must revoke that center too. Otherwise the removed
        // character leaves a ghost reveal radius behind (for example Urzahil at @36,45 makes
        // Morannon at @37,45 visible to The Necromancer).
        if (owner != null && formerHex != null && !owner.LeaderSeesHex(formerHex))
            owner.visibleHexes.Remove(formerHex);

        currentCharacterCount = Mathf.Max(0, currentCharacterCount - 1);
        Destroy(character.gameObject);
    }

    // Removes a scenario PC that turned out to belong to a leader-variant that wasn't chosen this
    // game. There's no supported "ownerless PC" state outside the pre-spawned starting anchor
    // cities, so — unlike an NPL-identity character, which can safely self-own — a mismatched PC
    // is removed entirely rather than left ownerless.
    private void RemoveUnresolvedScenarioPc(PC pc)
    {
        pc.owner.controlledPcs.Remove(pc);
        pc.owner.visibleHexes.Remove(pc.hex);
        pc.hex?.ClearPC();
        currentPcCount = Mathf.Max(0, currentPcCount - 1);
    }

    // Called by Game.PruneUnselectedLeaderVariants once every playable leader's variant is final:
    // a scenario can author the same leader at several hexes (one per variant), each spawned as its
    // own instance in step 1 above, but only one should remain in play. The leader's shared nation
    // (PCs/characters authored against the base owner name all attached to ONE arbitrary sibling
    // representative at spawn) transfers to the surviving instance; only the losing sibling itself
    // and its own authored starting army vanish, quietly — as if that start had never spawned (no
    // death messaging/effects, since the game hasn't visibly started). Variant-restricted assets
    // (ownerVariantId) are re-checked against the survivor afterwards in
    // ReconcileScenarioVariantOwnership, which runs after pruning in Game.StartGame.
    //
    // Also reused by ReconcileScenarioSpawnConditions for a non-playable self-owned leader whose
    // own spawnCondition failed: survivor is a same-named sibling whose spawnCondition passed
    // instead (if the scenario authored one — see that method), transferring ownership exactly
    // like a pruned variant sibling; survivor == null (no such sibling authored) routes every
    // owned PC/character through RemoveUnresolvedScenarioPc/RemoveUnresolvedScenarioCharacter
    // instead, which is "this leader never existed" semantics.
    public void RemoveUnselectedScenarioLeader(Leader leader, Leader survivor)
    {
        if (leader == null) return;

        foreach (PC pc in leader.controlledPcs.ToList())
        {
            if (survivor != null)
            {
                leader.controlledPcs.Remove(pc);
                leader.visibleHexes.Remove(pc.hex);
                pc.owner = survivor;
                survivor.controlledPcs.Add(pc);
                survivor.visibleHexes.Add(pc.hex);
            }
            else
            {
                RemoveUnresolvedScenarioPc(pc);
            }
        }

        foreach (Character character in leader.controlledCharacters.ToList())
        {
            if (character == leader) continue;
            if (survivor != null)
            {
                leader.controlledCharacters.Remove(character);
                character.owner = survivor;
                survivor.controlledCharacters.Add(character);
                // Mirror the PC branch: the survivor must see the hexes its characters stand on,
                // or fog hides them and Board.SelectHex refuses to select them (dead Tab cycling).
                if (character.hex != null && !survivor.visibleHexes.Contains(character.hex))
                    survivor.visibleHexes.Add(character.hex);
            }
            else
            {
                RemoveUnresolvedScenarioCharacter(character);
            }
        }

        // The losing start's own army dies with it — it belongs to that hex/variant, not the nation.
        if (leader.IsArmyCommander() && leader.GetArmy() != null && !leader.GetArmy().killed)
            leader.GetArmy().Killed(null, false);

        if (leader.hex != null && leader.hex.characters.Contains(leader))
            leader.hex.characters.Remove(leader);
        currentCharacterCount = Mathf.Max(0, currentCharacterCount - 1);
        Debug.Log($"[Scenario] pruned unselected sibling of '{leader.characterName}' at {leader.hex?.v2}; " +
                  $"survivor '{survivor?.characterName}' now owns {survivor?.controlledCharacters?.Count ?? 0} characters / {survivor?.controlledPcs?.Count ?? 0} PCs.");
        Destroy(leader.gameObject);
    }

    // Applies authored per-hex tile variations. Re-running SetTerrain with the chosen sprite
    // also refreshes that hex's Underground-entrance state (ChasmTiles reads the sprite name).
    private void ApplyScenarioTerrainSprites(ScenarioData scenario)
    {
        if (scenario.terrainSprites == null || scenario.terrainSprites.Count == 0) return;

        HexTextureMapping mapping = null;
        foreach (ScenarioSpriteCell cell in scenario.terrainSprites)
        {
            if (cell == null || string.IsNullOrWhiteSpace(cell.spriteName)) continue;
            if (!board.hexes.TryGetValue(new Vector2Int(cell.row, cell.col), out Hex hex) || hex == null) continue;
            if (mapping == null) mapping = hex.GetComponent<HexTextureMapping>();
            if (mapping == null) mapping = FindFirstObjectByType<HexTextureMapping>();
            if (mapping == null) return;

            Sprite sprite = mapping.GetTerrainSpriteByName(cell.spriteName);
            if (sprite == null)
            {
                Debug.LogWarning($"Scenario tile '{cell.spriteName}' at ({cell.row},{cell.col}) could not be resolved.");
                continue;
            }
            hex.SetTerrain(hex.terrainType, sprite, Color.white);
        }
    }

    private DeckManager ResolveDeckManager()
    {
        // Scenario spawning runs during board generation, before the game HUD is necessarily
        // active — the DeckManager may not have Awoken yet (Instance unset) and a default
        // FindFirstObjectByType skips inactive objects, so search inactive ones too.
        // InitializeFromResources only touches Resources, so it is safe on an inactive object.
        DeckManager deckManager = DeckManager.Instance != null
            ? DeckManager.Instance
            : FindFirstObjectByType<DeckManager>(FindObjectsInactive.Include);
        if (deckManager != null && (deckManager.cards == null || deckManager.cards.Count == 0))
        {
            deckManager.InitializeFromResources();
        }
        return deckManager;
    }

    private bool TryGetScenarioHex(ScenarioData scenario, int row, int col, out Hex hex)
    {
        hex = null;
        if (!scenario.InBounds(row, col)) return false;
        return board.hexes != null && board.hexes.TryGetValue(new Vector2Int(row, col), out hex) && hex != null;
    }

    // Resolves the owning leader for a PC/character. The owner can be any shared leader (the
    // editor lists them all), so if it has no explicit leader-start in the scenario we spawn it
    // lazily at this placement's hex (its capital) the first time it is referenced.
    private Leader EnsureLeaderSpawned(Dictionary<string, Leader> leadersByName, string ownerName, Hex hex)
    {
        if (string.IsNullOrWhiteSpace(ownerName)) return null;
        if (leadersByName.TryGetValue(ownerName, out Leader existing)) return existing;
        if (!EnsureCharacterCapacity($"Skipping owner leader '{ownerName}'.")) return null;

        (LeaderBiomeConfig playable, NonPlayableLeaderBiomeConfig nonPlayable) = FindLeaderBiome(ownerName);
        Leader leader = playable != null
            ? characterInstantiator.InstantiatePlayableLeader(hex, playable)
            : nonPlayable != null ? characterInstantiator.InstantiateNonPlayableLeader(hex, nonPlayable) : null;

        if (leader == null)
        {
            Debug.LogWarning($"Scenario references unknown owner leader '{ownerName}'.");
            return null;
        }

        currentCharacterCount++;
        placedPositions.Add(hex.v2);
        leadersByName[ownerName] = leader;
        leaderPositions[ownerName] = hex.v2;
        return leader;
    }

    private NonPlayableLeaderBiomeConfig FindNplBiomeByCharacterName(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return null;
        return nonPlayableLeaders.nonPlayableLeaders.biomes
            .FirstOrDefault(b => b != null && string.Equals(b.characterName, characterName, StringComparison.OrdinalIgnoreCase));
    }

    // Looks a name up in both leader-identity catalogs — the only place "is this leader playable?"
    // gets decided, since it's never authored in the scenario itself. At most one of the two
    // returned configs is non-null.
    private (LeaderBiomeConfig playable, NonPlayableLeaderBiomeConfig nonPlayable) FindLeaderBiome(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return (null, null);
        LeaderBiomeConfig playable = playableLeaders.playableLeaders.biomes
            .FirstOrDefault(b => b != null && string.Equals(b.characterName, characterName, StringComparison.OrdinalIgnoreCase));
        if (playable != null) return (playable, null);
        return (null, FindNplBiomeByCharacterName(characterName));
    }

    // A ScenarioCharacter whose characterName matches its own ownerLeaderName (self-owned) is a
    // record of that leader (playable or non-playable) in the scenario: its hex is where this
    // instance starts and its army is its starting army — see SpawnFromScenario step 1. A scenario
    // may author several such cards for the same leader name (one per variant/hex); each is its own
    // instance, disambiguated later by Game.PruneUnselectedLeaderVariants.
    private bool IsSelfOwnedLeaderCard(ScenarioCharacter sc, out LeaderBiomeConfig playable, out NonPlayableLeaderBiomeConfig nonPlayable)
    {
        playable = null;
        nonPlayable = null;
        if (sc == null || string.IsNullOrWhiteSpace(sc.characterName)) return false;
        if (!string.Equals(sc.characterName, sc.ownerLeaderName, StringComparison.OrdinalIgnoreCase)) return false;
        (playable, nonPlayable) = FindLeaderBiome(sc.characterName);
        return playable != null || nonPlayable != null;
    }

    // A ScenarioCharacter whose characterName matches one of a playable leader's variant
    // characterNames AND is owned by that leader (e.g. "The White Hand" owned by "Saruman") is
    // that variant's starting point in the scenario. It spawns in step 1 as its own
    // variant-locked PlayableLeader instance (= one carousel entry), never as a plain character.
    private bool IsPlayableVariantCard(ScenarioCharacter sc, out LeaderBiomeConfig playable, out LeaderVariantConfig variant)
    {
        playable = null;
        variant = null;
        if (sc == null || string.IsNullOrWhiteSpace(sc.characterName) || string.IsNullOrWhiteSpace(sc.ownerLeaderName)) return false;

        foreach (LeaderBiomeConfig biome in playableLeaders.playableLeaders.biomes)
        {
            if (biome == null || biome.variants == null) continue;
            if (!string.Equals(biome.characterName, sc.ownerLeaderName, StringComparison.OrdinalIgnoreCase)) continue;

            LeaderVariantConfig match = biome.variants.Find(v =>
                v != null && !string.IsNullOrWhiteSpace(v.characterName)
                && string.Equals(v.characterName, sc.characterName, StringComparison.OrdinalIgnoreCase));
            if (match == null) continue;

            playable = biome;
            variant = match;
            return true;
        }

        return false;
    }

    // "Every character can hold objects" — resolves each authored name against the Object
    // catalog and clones it onto the character, whether it's a companion (SpawnScenarioCharacter),
    // an NPL-identity spawn (InstantiateNonPlayableLeader), or a self-owned leader/variant card
    // (InstantiatePlayableLeader) — all three converge on a plain Character here, so one helper
    // covers every scenario-authored spawn path uniformly.
    private static void ApplyStartingObjects(Character character, List<string> objectNames, DeckManager deckManager)
    {
        if (character == null || objectNames == null || objectNames.Count == 0 || deckManager == null) return;
        foreach (string objectName in objectNames)
        {
            if (string.IsNullOrWhiteSpace(objectName)) continue;
            if (character.objects.Count >= Character.MAX_OBJECTS) break;
            CardData resolved = deckManager.FindObjectCardByName(objectName)?.Clone();
            if (resolved != null) character.objects.Add(resolved);
        }
    }

    // Mirrors how a Character card is turned into a unit (see Card.HandleCharacterCardPlayed):
    // a minimal biome carries identity, and InitializeFromBiome pulls levels/sprite from the card.
    private Character SpawnScenarioCharacter(Leader owner, Hex hex, string characterName, DeckManager deckManager)
    {
        CardData card = deckManager?.cards?.FirstOrDefault(c =>
            c != null && c.GetCardType() == CardTypeEnum.Character &&
            string.Equals(c.name, characterName, StringComparison.OrdinalIgnoreCase));

        BiomeConfig config = new()
        {
            characterName = characterName,
            alignment = card != null ? (AlignmentEnum)card.alignment : owner.GetAlignment(),
            race = card != null ? card.race : RacesEnum.Common,
            sex = card?.sex ?? SexEnum.Male,
            commander = card?.commander ?? 0,
            agent = card?.agent ?? 0,
            emmissary = card?.emmissary ?? 0,
            mage = card?.mage ?? 0,
            artifacts = card?.artifacts != null ? new List<string>(card.artifacts) : new List<string>()
        };

        Character character = characterInstantiator.InstantiateCharacter(owner, hex, config);
        if (character == null) return null;
        character.startingCharacter = true;
        if (card != null) character.characterGroup = card.characterGroup;
        return character;
    }

    private void BuildScenarioArmy(Character commander, ScenarioArmy army, DeckManager deckManager)
    {
        if (deckManager == null)
        {
            Debug.LogWarning($"Cannot build scenario army for '{commander.characterName}': no DeckManager.");
            return;
        }

        bool created = false;
        foreach (ScenarioArmyStack stack in army.stacks)
        {
            if (stack == null || stack.amount <= 0 || string.IsNullOrWhiteSpace(stack.armyCardName)) continue;

            CardData card = deckManager.FindArmyCardByName(stack.armyCardName);
            if (card == null)
            {
                Debug.LogWarning($"Scenario army references unknown army card '{stack.armyCardName}'.");
                continue;
            }

            List<ArmySpecialAbilityEnum> abilities = card.specialAbilities != null
                ? new List<ArmySpecialAbilityEnum>(card.specialAbilities)
                : null;

            if (!created)
            {
                // showSpawnMessage: false — this runs during scenario spawn, before
                // player.visibleHexes is populated (see Character.CreateArmy), so the "is this
                // enemy spotted" reveal roll would always miss and misreport a fully-visible
                // starting leader as "unspotted enemy".
                commander.CreateArmy(card.troopType, stack.amount, false, 0, abilities, card.name, showSpawnMessage: false);
                created = true;
            }
            else
            {
                commander.GetArmy()?.Recruit(card.troopType, stack.amount, abilities, card.name, showMessage: false);
            }
        }

        if (created)
        {
            Army result = commander.GetArmy();
            if (result != null) result.xp = Mathf.Clamp(army.xp, 0, 100);

            // Applies to armies belonging to ANY character, including self-owned leader cards —
            // unlike the character-level spawnCondition, an army's own gate isn't tied to the
            // carousel lifecycle, so it's honored everywhere BuildScenarioArmy runs.
            if (!string.IsNullOrWhiteSpace(army.spawnConditionLeaderName))
                armySpawnConditions.Add((commander, army.spawnConditionLeaderName, army.spawnConditionVariantId, army.spawnConditionExclude));
        }
    }

    // Applies painted region overrides, then flood-fills any unpainted land hex with the
    // nearest painted region so region labels and region-gated cards behave as in a normal game.
    private void ApplyScenarioRegions(ScenarioData scenario)
    {
        if (board?.hexes == null) return;

        foreach (Hex hex in board.hexes.Values)
            hex?.SetLandRegion(null);

        Queue<Vector2Int> frontier = new();
        foreach (ScenarioRegionCell cell in scenario.regions ?? new List<ScenarioRegionCell>())
        {
            if (cell == null || string.IsNullOrWhiteSpace(cell.region)) continue;
            Vector2Int v2 = new(cell.row, cell.col);
            if (!board.hexes.TryGetValue(v2, out Hex hex) || hex == null) continue;
            hex.SetLandRegion(cell.region.Trim());
            frontier.Enqueue(v2);
        }

        if (frontier.Count == 0) return;

        // Multi-source BFS: spread each painted region outward over unassigned non-water hexes.
        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            if (!board.hexes.TryGetValue(current, out Hex currentHex) || currentHex == null) continue;
            string region = currentHex.GetLandRegion();
            if (string.IsNullOrWhiteSpace(region)) continue;

            Vector2Int[] neighbors = ((current.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2Int next = new(current.x + neighbors[i].x, current.y + neighbors[i].y);
                if (!board.hexes.TryGetValue(next, out Hex neighbor) || neighbor == null) continue;
                if (neighbor.IsWaterTerrain()) continue;
                if (!string.IsNullOrWhiteSpace(neighbor.GetLandRegion())) continue;
                neighbor.SetLandRegion(region);
                frontier.Enqueue(next);
            }
        }
    }

    // Marks each scenario-authored Zone of Control hex as discovered (map/terrain revealed, fog
    // cleared) from game start, for the human player's ZoC. Uses Hex.RevealMapOnlyArea — the same
    // "known but not currently watched" reveal used by rumour-style events — rather than
    // Hex.EnsurePersistentScouting, which grants live unit-level visibility ("scouted") and is
    // reserved for a Non-Playable Leader's own self-knowledge of its own PCs (see PC.CapturePC /
    // ClaimByAllegiance and Hex.SetPC). A ZoC cell owned by an AI leader instead uses that NPL
    // self-knowledge mechanism, since there is no separate human-facing fog to clear for it.
    // Unlike ApplyScenarioRegions this never spreads — ZoC is exactly the painted set, nothing
    // more. Must run after scenario leader identity is final (see
    // ReconcileScenarioSpawnConditions) so a playable leader's ZoC resolves against the variant
    // that actually survived selection, not just whichever sibling happens to be spawned.
    public void ApplyScenarioZoneOfControl(ScenarioData scenario)
    {
        if (board?.hexes == null || scenario == null) return;

        PlayableLeader humanPlayer = Game.Instance != null ? Game.Instance.player : null;

        foreach (ScenarioZoneOfControlCell cell in scenario.zoneOfControl ?? new List<ScenarioZoneOfControlCell>())
        {
            if (cell == null || string.IsNullOrWhiteSpace(cell.leaderName)) continue;
            if (!board.hexes.TryGetValue(new Vector2Int(cell.row, cell.col), out Hex hex) || hex == null) continue;

            Leader leader;
            (LeaderBiomeConfig playableBiome, _) = FindLeaderBiome(cell.leaderName);
            if (playableBiome != null)
            {
                // A playable leader's authored variant siblings all register under the same key in
                // scenarioLeadersByName ("last one wins" — see the comment at that assignment in
                // step 1), which isn't necessarily the sibling that actually survived
                // PruneUnselectedLeaderVariants. Resolve straight against game.player/competitors
                // instead (same lookup SpawnConditionMet uses) so a cell painted for one variant
                // never leaks onto a different one — or a losing, about-to-be-destroyed sibling.
                leader = ResolvePlayableLeaderInstance(cell.leaderName, cell.variantId);
            }
            else
            {
                // Non-playable leaders have no variant siblings to disambiguate.
                scenarioLeadersByName.TryGetValue(cell.leaderName, out leader);
            }
            if (leader == null) continue;

            if (leader == humanPlayer)
            {
                hex.RevealMapOnlyArea(0, false, false);
            }
            else
            {
                hex.EnsurePersistentScouting(leader);
            }
        }
    }

    public bool EnsureLandRegionsAssigned()
    {
        if (landRegionsAssigned) return true;
        AssignLandRegions();
        return landRegionsAssigned;
    }

    private void AssignLandRegions()
    {
        if (board == null || board.hexes == null || board.hexes.Count == 0) return;

        DeckManager deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        List<string> allLandRegions;
        Dictionary<string, string> pcRegionsByName = new(StringComparer.OrdinalIgnoreCase);

        if (deckManager != null)
        {
            if (deckManager.cards == null || deckManager.cards.Count == 0)
            {
                deckManager.InitializeFromResources();
            }

            allLandRegions = deckManager.cards != null
                ? deckManager.cards
                    .Where(card => card != null && card.GetCardType() == CardTypeEnum.Land && !string.IsNullOrWhiteSpace(card.name))
                    .Select(card => card.name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            foreach (CardData card in deckManager.cards ?? new List<CardData>())
            {
                if (card == null || string.IsNullOrWhiteSpace(card.name) || string.IsNullOrWhiteSpace(card.region)) continue;
                if (card.GetCardType() != CardTypeEnum.PC) continue;
                string key = card.name.Trim();
                if (!pcRegionsByName.ContainsKey(key))
                {
                    pcRegionsByName[key] = card.region.Trim();
                }
            }
        }
        else if (!TryLoadRegionDataFromResources(out allLandRegions, out pcRegionsByName))
        {
            Debug.LogWarning("NationSpawner: Could not load card data for land region assignment.");
            return;
        }

        if (allLandRegions.Count == 0) return;

        foreach (Hex hex in board.GetHexes())
        {
            hex?.SetLandRegion(null);
        }

        HashSet<Vector2Int> assignedPositions = new();
        Queue<RegionSeed> seedQueue = new();
        HashSet<string> seededRegions = new(StringComparer.OrdinalIgnoreCase);

        foreach (Hex hex in board.GetHexes())
        {
            if (hex == null) continue;
            PC pc = hex.GetPCData();
            if (pc == null) continue;

            string region = deckManager != null
                ? deckManager.ResolveRegionForPc(pc)
                : ResolveRegionForPcFromLookup(pc, pcRegionsByName);
            if (string.IsNullOrWhiteSpace(region)) continue;

            seedQueue.Enqueue(new RegionSeed(hex.v2, region.Trim()));
            seededRegions.Add(region.Trim());
        }

        List<string> fallbackRegions = allLandRegions
            .Where(region => !seededRegions.Contains(region))
            .ToList();

        List<Hex> unassignedHexes = board.GetHexes()
            .Where(hex => hex != null && string.IsNullOrWhiteSpace(hex.GetLandRegion()))
            .ToList();

        // Collect all PC seed positions so fallback seeds can be spread away from them
        var existingSeedPositions = seedQueue.Select(s => s.position).ToList();

        foreach (string region in fallbackRegions)
        {
            if (unassignedHexes.Count == 0) break;

            Hex startHex = PickFurthestUnassignedHex(unassignedHexes, existingSeedPositions);
            if (startHex == null) continue;

            startHex.SetLandRegion(region.Trim());
            seedQueue.Enqueue(new RegionSeed(startHex.v2, region.Trim()));
            existingSeedPositions.Add(startHex.v2);

            unassignedHexes = board.GetHexes()
                .Where(hex => hex != null && string.IsNullOrWhiteSpace(hex.GetLandRegion()))
                .ToList();
        }

        FloodAssignRegions(seedQueue, assignedPositions);

        if (board.GetHexes().Any(hex => hex != null && string.IsNullOrWhiteSpace(hex.GetLandRegion())))
        {
            string defaultRegion = allLandRegions[0];
            foreach (Hex hex in board.GetHexes())
            {
                if (hex == null || !string.IsNullOrWhiteSpace(hex.GetLandRegion())) continue;
                hex.SetLandRegion(defaultRegion);
            }
        }

        landRegionsAssigned = board.GetHexes().All(hex => hex != null && !string.IsNullOrWhiteSpace(hex.GetLandRegion()));
    }

    private static bool TryLoadRegionDataFromResources(out List<string> landRegions, out Dictionary<string, string> pcRegionsByName)
    {
        landRegions = new List<string>();
        pcRegionsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        TextAsset manifestAsset = Resources.Load<TextAsset>("Cards");
        if (manifestAsset == null) return false;

        CardsManifest manifest = JsonUtility.FromJson<CardsManifest>(manifestAsset.text);
        if (manifest?.decks == null || manifest.decks.Count == 0) return false;

        foreach (DeckManifestEntry entry in manifest.decks)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.resourcePath)) continue;

            TextAsset deckAsset = Resources.Load<TextAsset>(entry.resourcePath);
            if (deckAsset == null) continue;

            DeckData deckData = JsonUtility.FromJson<DeckData>(deckAsset.text);
            if (deckData?.cards == null || deckData.cards.Count == 0) continue;

            foreach (CardData card in deckData.cards)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.name)) continue;
                if (card.GetCardType() == CardTypeEnum.Land)
                {
                    landRegions.Add(card.name.Trim());
                    continue;
                }

                if (card.GetCardType() != CardTypeEnum.PC || string.IsNullOrWhiteSpace(card.region)) continue;
                string key = card.name.Trim();
                if (!pcRegionsByName.ContainsKey(key))
                {
                    pcRegionsByName[key] = card.region.Trim();
                }
            }
        }

        landRegions = landRegions
            .Where(region => !string.IsNullOrWhiteSpace(region))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return landRegions.Count > 0;
    }

    private static string ResolveRegionForPcFromLookup(PC pc, IReadOnlyDictionary<string, string> pcRegionsByName)
    {
        if (pc == null || pcRegionsByName == null) return null;
        if (!string.IsNullOrWhiteSpace(pc.pcName) && pcRegionsByName.TryGetValue(pc.pcName.Trim(), out string region))
        {
            return region;
        }

        return null;
    }

    private static Hex PickFurthestUnassignedHex(List<Hex> candidates, List<Vector2Int> existingSeeds)
    {
        if (candidates.Count == 0) return null;

        Hex best = null;
        float bestMinDist = float.MinValue;

        // Two passes: prefer non-PC land hexes, fall back to anything
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var hex in candidates)
            {
                if (hex == null) continue;
                if (pass == 0 && (hex.HasAnyPC() || hex.IsWaterTerrain())) continue;

                if (existingSeeds.Count == 0)
                    return hex;

                float minDist = float.MaxValue;
                foreach (var seed in existingSeeds)
                {
                    float dx = hex.v2.x - seed.x;
                    float dy = hex.v2.y - seed.y;
                    float d = dx * dx + dy * dy;
                    if (d < minDist) minDist = d;
                }

                if (minDist > bestMinDist) { bestMinDist = minDist; best = hex; }
            }

            if (best != null) return best;
        }

        return best;
    }

    private void FloodAssignRegions(Queue<RegionSeed> seeds, HashSet<Vector2Int> assignedPositions, int maxAssignmentsPerRegion = int.MaxValue)
    {
        if (board == null || board.hexes == null || seeds == null) return;

        Queue<RegionSeed> queue = new(seeds);
        Dictionary<string, int> regionCounts = new(StringComparer.OrdinalIgnoreCase);
        HashSet<Vector2Int> visited = new();

        while (queue.Count > 0)
        {
            RegionSeed current = queue.Dequeue();
            if (current == null || string.IsNullOrWhiteSpace(current.region)) continue;
            if (!board.hexes.TryGetValue(current.position, out Hex hex) || hex == null) continue;

            string regionKey = current.region.Trim();
            if (!regionCounts.TryGetValue(regionKey, out int count))
            {
                count = 0;
            }
            if (count >= maxAssignmentsPerRegion) continue;

            string existingRegion = hex.GetLandRegion();
            if (!string.IsNullOrWhiteSpace(existingRegion))
            {
                if (!string.Equals(existingRegion.Trim(), regionKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (assignedPositions.Add(current.position))
                {
                    regionCounts[regionKey] = count + 1;
                }

                var matchingNeighbors = ((current.position.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;
                for (int i = 0; i < matchingNeighbors.Length; i++)
                {
                    Vector2Int next = new(current.position.x + matchingNeighbors[i].x, current.position.y + matchingNeighbors[i].y);
                    if (!visited.Add(next)) continue;
                    if (!board.hexes.ContainsKey(next)) continue;
                    queue.Enqueue(new RegionSeed(next, regionKey));
                }

                continue;
            }

            hex.SetLandRegion(regionKey);
            assignedPositions.Add(current.position);
            regionCounts[regionKey] = count + 1;

            var neighbors = ((current.position.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2Int next = new(current.position.x + neighbors[i].x, current.position.y + neighbors[i].y);
                if (!visited.Add(next)) continue;
                if (!board.hexes.ContainsKey(next)) continue;
                queue.Enqueue(new RegionSeed(next, regionKey));
            }
        }
    }

    private void InstantiateLeadersAndCharacters(List<LeaderBiomeConfig> leaderBiomes, List<Vector2Int> placedPositions)
    {
        foreach (LeaderBiomeConfig leaderBiomeConfig in leaderBiomes)
        {
            Vector2Int? position = InstantiateLeaderAndCharacters(leaderBiomeConfig, placedPositions, true, null);
            if (position.HasValue && !string.IsNullOrWhiteSpace(leaderBiomeConfig.characterName))
            {
                leaderPositions[leaderBiomeConfig.characterName] = position.Value;
            }
        }
    }

    private void InstantiateLeadersAndCharacters(List<NonPlayableLeaderBiomeConfig> nonPlayableleaderBiomes, List<Vector2Int> placedPositions)
    {
        IEnumerable<NonPlayableLeaderBiomeConfig> orderedBiomes = nonPlayableleaderBiomes
            .OrderBy(b => b.characterName);

        foreach (NonPlayableLeaderBiomeConfig nonPlayableleaderBiomeConfig in orderedBiomes)
        {
            Vector2Int? position = InstantiateLeaderAndCharacters(nonPlayableleaderBiomeConfig, placedPositions, false, null);
            if (position.HasValue && !string.IsNullOrWhiteSpace(nonPlayableleaderBiomeConfig.characterName))
            {
                leaderPositions[nonPlayableleaderBiomeConfig.characterName] = position.Value;
            }
        }
    }
    
    private Vector2Int? InstantiateLeaderAndCharacters(LeaderBiomeConfig leaderBiomeConfig, List<Vector2Int> placedPositions, bool isPlayable, Vector2Int? preferredPosition, float minSeparation = 0f, float minDistanceFromPreferred = 0f)
    {
        /*if (FindObjectsByType<Leader>(FindObjectsSortMode.None).Length >= Game.MAX_LEADERS)
        {
            Debug.LogWarning("Max leaders reached. Skipping leader instantiation.");
            return;
        }*/
        if (!isPlayable && !EnsurePcCapacity())
        {
            string leaderName = string.IsNullOrWhiteSpace(leaderBiomeConfig.characterName) ? "Unknown" : leaderBiomeConfig.characterName;
            Debug.LogError($"Skipping non-playable leader instantiation for {leaderName} because max PCs reached.");
            return null;
        }
        preferredPosition ??= GetPreferredPositionForStartingCityRegion(leaderBiomeConfig);
        TerrainEnum chosenTerrain;
        Vector2Int bestPosition = preferredPosition.HasValue
            ? SelectClosestPosition(leaderBiomeConfig, preferredPosition.Value, out chosenTerrain, minDistanceFromPreferred)
            : SelectSpreadPosition(leaderBiomeConfig, placedPositions, minSeparation, out chosenTerrain);
        placedPositions.Add(bestPosition);

        Vector2Int v2 = new(bestPosition.x, bestPosition.y);
        Hex hex = board.hexes[v2];

        if (!EnsureCharacterCapacity("Skipping leader instantiation."))
            return null;

        // Only this procedural (non-scenario) placement path applies noScenarioStart — every
        // other call site instantiates leaders for/within an authored scenario, which supplies
        // its own army and city data and must never also get this default.
        Leader leader;
        if (isPlayable)
        {
            leader = characterInstantiator.InstantiatePlayableLeader(hex, leaderBiomeConfig, applyNoScenarioStart: true);
        }
        else if (leaderBiomeConfig is NonPlayableLeaderBiomeConfig nonPlayableConfig)
        {
            leader = characterInstantiator.InstantiateNonPlayableLeader(hex, nonPlayableConfig, applyNoScenarioStart: true);
        }
        else
        {
            Debug.LogError("Non playable leader biome config expected but not provided.");
            return null;
        }

        currentCharacterCount++;

        foreach (var character in leader.GetBiome().startingCharacters)
        {
            if (!EnsureCharacterCapacity("Skipping leader instantiation."))
                return null;

            characterInstantiator.InstantiateCharacter(leader, hex, character);
            currentCharacterCount++;
        }

        bool skipStartingPc = isPlayable && leaderBiomeConfig.startingCitySize == PCSizeEnum.NONE;
        if (!skipStartingPc)
        {
            if (!EnsurePcCapacity())
                return null;

            PC pc = new(leader, hex);
            hex.SetPC(pc, leaderBiomeConfig.pcFeature, leaderBiomeConfig.fortFeature, leaderBiomeConfig.isIsland);

            // If we fell back from a shore start and the PC was meant to have a port, strip the port on non-shore terrain.
            if (leaderBiomeConfig.noScenarioStart.startsWithPort && leaderBiomeConfig.noScenarioStart.terrain == TerrainEnum.shore && chosenTerrain != TerrainEnum.shore)
            {
                pc.hasPort = false;
                hex.RedrawPC();
            }

            // Non-playable leaders that start with a port but have no adjacent water lose the port and warships.
            if (!isPlayable && leaderBiomeConfig.noScenarioStart.startsWithPort && !HasNeighboringWater(hex))
            {
                if (pc != null && pc.hasPort)
                {
                    pc.hasPort = false;
                }
                RemoveWarshipsFromLeaderArmiesAtHex(leader, hex);
                hex.RedrawPC();
                hex.RedrawArmies();
            }

            currentPcCount++;
            RegisterStartingCityPosition(leaderBiomeConfig, bestPosition);
        }
        return bestPosition;
    }

    private Vector2Int? GetPreferredPositionForStartingCityRegion(LeaderBiomeConfig leaderBiomeConfig)
    {
        if (leaderBiomeConfig == null || string.IsNullOrWhiteSpace(leaderBiomeConfig.noScenarioStart.startingCityRegion))
        {
            return null;
        }

        if (!startingCityPositionsByRegion.TryGetValue(leaderBiomeConfig.noScenarioStart.startingCityRegion, out List<Vector2Int> positions) ||
            positions == null || positions.Count == 0)
        {
            return null;
        }

        int avgX = Mathf.RoundToInt((float)positions.Average(p => p.x));
        int avgY = Mathf.RoundToInt((float)positions.Average(p => p.y));
        return new Vector2Int(avgX, avgY);
    }

    private void RegisterStartingCityPosition(LeaderBiomeConfig leaderBiomeConfig, Vector2Int position)
    {
        if (leaderBiomeConfig == null || string.IsNullOrWhiteSpace(leaderBiomeConfig.noScenarioStart.startingCityRegion))
        {
            return;
        }

        if (!startingCityPositionsByRegion.TryGetValue(leaderBiomeConfig.noScenarioStart.startingCityRegion, out List<Vector2Int> positions))
        {
            positions = new List<Vector2Int>();
            startingCityPositionsByRegion[leaderBiomeConfig.noScenarioStart.startingCityRegion] = positions;
        }

        positions.Add(position);
    }

    // Minimum hex distance enforced between starting-nation anchor cities. Scaled to the
    // board so the three anchors land in different thirds of the map; never so large the
    // map can't satisfy it (callers relax gracefully when it can't be met).
    private float GetMinStartSeparation()
    {
        if (board == null) return 0f;
        int minDim = Mathf.Min(board.GetWidth(), board.GetHeight());
        return Mathf.Max(4f, minDim / 3f);
    }

    private List<TerrainEnum> BuildTerrainPreferenceOrder(TerrainEnum primary)
    {
        List<TerrainEnum> order = new(StartFallbackTerrains.Length + 1) { primary };
        foreach (TerrainEnum terrain in StartFallbackTerrains)
        {
            if (terrain != primary) order.Add(terrain);
        }
        return order;
    }

    // Cluster a leader onto its starting city. PROXIMITY TO THE ANCHOR DOMINATES TERRAIN:
    // a leader one hex from its own city on the "wrong" terrain is correct; the configured
    // terrain across the map (next to a rival's city) is the "Sauron starts in Orthanc" bug.
    // That bug happened because the old code returned the closest hex of the first preferred
    // terrain that had any free hex — and rare terrains like wastelands spawn in scattered
    // patches, so the only free wastelands hex could be beside another nation's anchor while
    // grassland sat one step from Barad-dur. We now search every land terrain at once, pick
    // the genuinely nearest available hex, and use the configured terrain order only to break
    // ties between hexes the same distance from the anchor.
    // minDistanceFromTarget > 0 places NEAR the target but never on top of it: a foreign capital
    // landing one hex from a starting nation reads in-game as "the leader started in Edoras". We
    // keep a buffer; if the map can't honour it we fall back to the unconstrained nearest hex
    // rather than throwing.
    private Vector2Int SelectClosestPosition(LeaderBiomeConfig config, Vector2Int target, out TerrainEnum chosenTerrain, float minDistanceFromTarget = 0f)
    {
        const float epsilon = 0.0001f;
        Vector3Int targetCube = GetCachedCubeCoordinate(target);
        List<TerrainEnum> preference = BuildTerrainPreferenceOrder(config.noScenarioStart.terrain);

        // Track the nearest hex overall, and (separately) the nearest hex at least
        // minDistanceFromTarget away. Prefer the buffered one when it exists.
        Candidate any = Candidate.Empty;
        Candidate buffered = Candidate.Empty;

        foreach (TerrainEnum terrain in LandTerrains)
        {
            List<Vector2Int> available = GetAvailableHexes(terrain, config.feature);
            if (available.Count == 0) continue;

            int rank = preference.IndexOf(terrain);
            if (rank < 0) rank = preference.Count; // non-preferred land terrain: ranked last, still eligible

            foreach (Vector2Int candidate in available)
            {
                float d = CubeDistance(GetCachedCubeCoordinate(candidate), targetCube);
                any.ConsiderNearest(candidate, d, rank, terrain, epsilon);
                if (d >= minDistanceFromTarget)
                    buffered.ConsiderNearest(candidate, d, rank, terrain, epsilon);
            }
        }

        Candidate chosen = (minDistanceFromTarget > 0f && buffered.found) ? buffered : any;
        if (!chosen.found)
            throw new Exception($"No suitable hexes found for '{config.characterName}' near its starting city.");

        // Only flag genuinely distant placements; a different terrain one hex from the anchor is fine
        // and expected, so it would just be log spam. "Far" means the neighbourhood was crowded.
        if (chosen.dist > minDistanceFromTarget + 6f)
            Debug.LogWarning($"NationSpawner: '{config.characterName}' placed {chosen.dist} hexes from its target (neighbourhood crowded; terrain {chosen.terrain}).");

        chosenTerrain = chosen.terrain;
        return chosen.position;
    }

    // Running "nearest acceptable hex" pick: closest distance wins; ties broken by terrain rank.
    private struct Candidate
    {
        public bool found;
        public Vector2Int position;
        public float dist;
        public int rank;
        public TerrainEnum terrain;

        public static Candidate Empty => new() { found = false, dist = float.MaxValue, rank = int.MaxValue };

        public void ConsiderNearest(Vector2Int pos, float d, int r, TerrainEnum t, float epsilon)
        {
            bool closer = d < dist - epsilon;
            bool tieBetterTerrain = Mathf.Abs(d - dist) <= epsilon && r < rank;
            if (!found || closer || tieBetterTerrain)
            {
                found = true;
                position = pos;
                dist = d;
                rank = r;
                terrain = t;
            }
        }
    }

    // Pick a well-separated position. Separation wins over terrain: a starting city on the
    // "wrong" terrain far from its neighbours is better than the correct terrain stacked on
    // top of another nation. Only relaxes separation when no terrain can satisfy it.
    private Vector2Int SelectSpreadPosition(LeaderBiomeConfig config, List<Vector2Int> placedPositions, float minSeparation, out TerrainEnum chosenTerrain)
    {
        if (minSeparation > 0f)
        {
            foreach (TerrainEnum terrain in BuildTerrainPreferenceOrder(config.noScenarioStart.terrain))
            {
                List<Vector2Int> available = GetAvailableHexes(terrain, config.feature);
                if (available.Count == 0) continue;
                List<Vector2Int> separated = FilterBySeparation(available, placedPositions, minSeparation);
                if (separated.Count == 0) continue;
                if (terrain != config.noScenarioStart.terrain)
                    Debug.LogWarning($"Relaxing terrain to {terrain} for '{config.characterName}' to keep starting nations apart.");
                chosenTerrain = terrain;
                return FindFarthestPosition(separated, placedPositions);
            }
            Debug.LogWarning($"Could not honor minimum start separation ({minSeparation}) for '{config.startingCityName ?? config.characterName}'; placing as far as the map allows.");
        }

        foreach (TerrainEnum terrain in BuildTerrainPreferenceOrder(config.noScenarioStart.terrain))
        {
            List<Vector2Int> available = GetAvailableHexes(terrain, config.feature);
            if (available.Count == 0) continue;
            chosenTerrain = terrain;
            return FindFarthestPosition(available, placedPositions);
        }
        throw new Exception($"No suitable hexes found for '{config.characterName}' with terrain {config.noScenarioStart.terrain} (including fallbacks).");
    }

    private List<Vector2Int> FilterBySeparation(List<Vector2Int> candidates, List<Vector2Int> placedPositions, float minSeparation)
    {
        if (placedPositions == null || placedPositions.Count == 0) return candidates;

        List<Vector2Int> result = new(candidates.Count);
        foreach (Vector2Int candidate in candidates)
        {
            Vector3Int candidateCube = GetCachedCubeCoordinate(candidate);
            bool farEnough = true;
            foreach (Vector2Int placed in placedPositions)
            {
                if (CubeDistance(candidateCube, GetCachedCubeCoordinate(placed)) < minSeparation)
                {
                    farEnough = false;
                    break;
                }
            }
            if (farEnough) result.Add(candidate);
        }
        return result;
    }

    private List<Vector2Int> GetAvailableHexes(TerrainEnum terrain, FeaturesEnum feature)
    {
        List<Vector2Int> suitableHexes = GetCachedHexesWithTerrain(terrain, feature);

        if (suitableHexes.Count == 0)
        {
            return new List<Vector2Int>();
        }

        return suitableHexes
            .Where(pos => board.hexes.TryGetValue(pos, out Hex h) && !h.HasAnyPC() && (h.characters == null || h.characters.Count == 0))
            .ToList();
    }

    private List<Vector2Int> GetCachedHexesWithTerrain(TerrainEnum terrain, FeaturesEnum feature)
    {
        if (!terrainHexCache.TryGetValue(terrain, out List<Vector2Int> suitableTerrain))
        {
            return new List<Vector2Int>();
        }

        List<Vector2Int> suitableFeature = suitableTerrain;
        switch (feature)
        {
            case FeaturesEnum.river:
            case FeaturesEnum.lake:
                suitableFeature = featuresHexCache[feature];
                break;
        }

        List<Vector2Int> union = suitableTerrain.Intersect(suitableFeature).ToList();
        if (union.Count < 1)
        {
            Debug.LogWarning($"Could not get hexes that have both {terrain.ToString()} and {feature.ToString()}. Ignoring terrain restriction.");
            union = suitableFeature;
        }

        return union;
    }

    private bool HasNeighboringWater(Hex hex)
    {
        if (hex == null || board == null) return false;
        if (hex.IsWaterTerrain()) return true;

        var neighbors = ((hex.v2.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;
        for (int i = 0; i < neighbors.Length; i++)
        {
            Vector2Int pos = new(hex.v2.x + neighbors[i].x, hex.v2.y + neighbors[i].y);
            if (board.hexes.TryGetValue(pos, out Hex neighbor) && neighbor != null && neighbor.IsWaterTerrain())
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveWarshipsFromLeaderArmiesAtHex(Leader leader, Hex hex)
    {
        if (leader == null || hex == null || hex.armies == null) return;
        for (int i = 0; i < hex.armies.Count; i++)
        {
            Army army = hex.armies[i];
            if (army == null || army.commander == null) continue;
            if (army.commander.GetOwner() != leader) continue;
            if (army.ws > 0) army.ws = 0;
        }
    }

    private Vector2Int FindFarthestPosition(List<Vector2Int> candidates, List<Vector2Int> existingPositions)
    {
        if (existingPositions.Count == 0)
        {
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        Vector2Int bestPosition = Vector2Int.zero;
        float maxMinDistance = -1;

        // Pre-calculate cube coordinates for existing positions
        var existingCubes = new Vector3Int[existingPositions.Count];
        for (int i = 0; i < existingPositions.Count; i++)
        {
            existingCubes[i] = GetCachedCubeCoordinate(existingPositions[i]);
        }

        foreach (var candidate in candidates)
        {
            float minDistance = float.MaxValue;
            var candidateCube = GetCachedCubeCoordinate(candidate);

            foreach (var existingCube in existingCubes)
            {
                float distance = CubeDistance(candidateCube, existingCube);
                minDistance = Mathf.Min(minDistance, distance);
            }

            if (minDistance > maxMinDistance)
            {
                maxMinDistance = minDistance;
                bestPosition = candidate;
            }
        }

        return bestPosition;
    }

    private Vector3Int GetCachedCubeCoordinate(Vector2Int offset)
    {
        if (!cubeCoordinateCache.TryGetValue(offset, out var cube))
        {
            cube = OffsetToCube(offset);
            cubeCoordinateCache[offset] = cube;
        }
        return cube;
    }

    private float CubeDistance(Vector3Int a, Vector3Int b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z)) / 2f;
    }

    private Vector3Int OffsetToCube(Vector2Int offset)
    {
        int x = offset.x;
        int z = offset.y - (offset.x - (offset.x & 1)) / 2;
        int y = -x - z;
        return new Vector3Int(x, y, z);
    }
}
