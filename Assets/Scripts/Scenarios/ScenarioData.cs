using System;
using System.Collections.Generic;

namespace RetroLOTR.Scenarios
{
    /// <summary>
    /// Serializable description of a hand-authored map: terrain, region paint, and every
    /// starting placement (leaders, PCs, characters and their armies). Saved as JSON under
    /// Resources/Scenarios and loaded by <see cref="ScenarioLoader"/> at runtime.
    ///
    /// Coordinates use the same convention as <c>Hex.v2</c>: <c>row</c> is the vertical axis
    /// (Board height index) and <c>col</c> is the horizontal axis (Board width index). The
    /// flat <see cref="terrain"/> array is row-major: <c>index = row * width + col</c>.
    ///
    /// Shared content (PlayableLeaderBiomes.json / NonPlayableLeaderBiomes.json and the card
    /// decks) is NEVER embedded here — placements only reference it by name so a single edit to
    /// a leader or card propagates to every scenario.
    /// </summary>
    [Serializable]
    public class ScenarioData
    {
        // v2 added ScenarioLeaderStart.variantId (playable-leader variant restriction).
        // v3 added ScenarioPC.isUnderground (PC marks its hex as an Underground entrance).
        // v4 added ScenarioPC/ScenarioCharacter.ownerVariantId (owner-variant-locked ownership).
        // v5 removed ScenarioLeaderStart/leaderStarts: a leader's presence, hex and starting army
        // are entirely determined by its self-owned ScenarioCharacter (ownerLeaderName ==
        // characterName). Whether that name is playable or non-playable is looked up from
        // PlayableLeaderBiomes.json/NonPlayableLeaderBiomes.json, never stored. ScenarioCharacter
        // gained variantId (moved from ScenarioLeaderStart) for the playable-variant-carousel
        // restriction. Pre-v5 scenarios must be migrated (see ScenarioMigration) since there is no
        // in-place default for a removed list.
        // v6 added ScenarioPC/ScenarioCharacter.fallbackOwnerName: an explicit author choice for
        // what happens to a variant-locked PC/character when its owner's variant isn't the one
        // actually chosen — become independent under a named Non-Playable Leader, or (empty,
        // the default) be destroyed. Replaces the old implicit "NPL-identity characters revert to
        // independent, everything else is destroyed" rule.
        // Older scenarios deserialize with the new fields at their defaults, so they keep working.
        // v7 added ScenarioData.description: the author-written blurb the campaign-selection
        // screen shows under the scenario's name. Optional; empty keeps the button template's text.
        // v8 added displayTitle (campaign-selection title, distinct from the file name) and
        // representativeCardName (the card whose token art represents the scenario on the
        // campaign-selection screen). Both optional; empty falls back to v7 behavior.
        // v9 added ScenarioCharacter/ScenarioArmy.spawnConditionLeaderName + spawnConditionVariantId:
        // an independent spawn gate, unrelated to ownership — the character/army is only created if
        // the NAMED leader (any playable leader in the scenario, not necessarily this entity's own
        // owner) ends up with the given variant selected. Empty leader name = always spawn (the
        // default, so older scenarios are unaffected). Unlike ownerVariantId there is no fallback
        // owner on mismatch — the character/army is simply never created. See
        // NationSpawner.ReconcileScenarioSpawnConditions.
        // v10 added ScenarioCharacter/ScenarioArmy.spawnConditionExclude: flips the v9 spawn gate
        // from "requires" to "excludes" — spawns only when the named leader is NOT currently
        // playing with the given variant (absent from the game entirely, or present with a
        // different variant). Defaults to false (the v9 "requires" behavior), so older scenarios
        // are unaffected.
        // v11 added ScenarioData.artifacts: author-pinned hexes for named hidden artifacts (from
        // Artifacts.json's hidden pool). An artifact named here is placed at its given hex instead
        // of a random one; any hidden artifact not named here still gets placed randomly exactly
        // as before. See Board.PlaceScenarioArtifacts. Empty list = fully random (unaffected).
        // v12: artifacts were merged into Object-type cards (Cards/Modular/ObjectsDeck.json).
        // ScenarioArtifact -> ScenarioObject (artifactName -> objectName), ScenarioData.artifacts
        // -> ScenarioData.objects, still resolved by name but now against the card catalog instead
        // of Artifacts.json (see Board.PlaceScenarioObjects). Also added
        // ScenarioCharacter.startingObjects: Object-card names this character starts holding —
        // leaders no longer get automatic starting items (Game.GrantStartingArtifacts was
        // removed); every scenario character, leader or companion, is assigned objects explicitly
        // here instead. Empty list = holds nothing, same as before.
        // v13 added ScenarioData.zoneOfControl: sparse per-hex nation ownership (row/col/leaderName)
        // for hexes revealed to that nation from the very start of the game, regardless of unit
        // presence, and never re-hidden. Resolved against the named leader once scenario leader
        // identity is final (see NationSpawner.ApplyScenarioZoneOfControl). For the human player
        // this means the hex's fog is cleared (Hex.RevealMapOnlyArea) — discovered, not scouted:
        // the terrain is known but units there aren't under live watch. For an AI leader it instead
        // uses Hex.EnsurePersistentScouting, the same self-knowledge mechanism already used for a
        // Non-Playable Leader's own founded PCs. Empty list = no scenario-authored ZoC (fully
        // backward compatible).
        // v14 added ScenarioZoneOfControlCell.variantId: a playable leader's ZoC is now per-variant
        // rather than shared across every variant of that leader — a cell only applies when its
        // variantId matches the variant actually selected/surviving for that leader this game
        // ("" = Base flavor specifically). Missing on older files deserializes to "" (Base), which
        // is only correct for scenarios that never authored more than one variant's worth of ZoC
        // for the same leader; those with genuinely shared, variant-agnostic ZoC should re-paint it
        // once per variant in the Scenario Creator.
        public const int CurrentVersion = 14;

        public int version = CurrentVersion;
        public string scenarioName = "New Scenario";

        /// <summary>Author-written blurb shown on the campaign-selection screen's scenario button.</summary>
        public string description = "";

        /// <summary>Title shown on the campaign-selection screen; the file name is used when empty.</summary>
        public string displayTitle = "";

        /// <summary>Card whose art (token form) represents this scenario on the campaign-selection screen.</summary>
        public string representativeCardName = "";

        public int width;
        public int height;

        /// <summary>Row-major terrain grid, each entry cast from <see cref="TerrainEnum"/>.</summary>
        public int[] terrain = Array.Empty<int>();

        /// <summary>Sparse per-hex land region overrides (only hexes the author painted a region on).</summary>
        public List<ScenarioRegionCell> regions = new();

        /// <summary>Sparse per-hex terrain-sprite overrides. When set, the loader applies this exact
        /// tile variation (by sprite name), which also drives whether that hex is an Underground
        /// entrance (chasm tile). Hexes without an override fall back to the terrain's
        /// default/random variation.</summary>
        public List<ScenarioSpriteCell> terrainSprites = new();

        public List<ScenarioPC> pcs = new();
        public List<ScenarioCharacter> characters = new();

        /// <summary>Author-pinned hex for a named hidden object (see v11/v12 notes above). Sparse —
        /// only objects the author deliberately placed appear here.</summary>
        public List<ScenarioObject> objects = new();

        /// <summary>Sparse per-hex Zone of Control ownership: hexes revealed to the named nation
        /// from game start onward, regardless of unit presence (see v13 notes above).</summary>
        public List<ScenarioZoneOfControlCell> zoneOfControl = new();

        public int Index(int row, int col) => row * width + col;

        public bool InBounds(int row, int col) => row >= 0 && row < height && col >= 0 && col < width;

        public TerrainEnum GetTerrain(int row, int col)
        {
            if (!InBounds(row, col) || terrain == null) return TerrainEnum.deepWater;
            int i = Index(row, col);
            return (i >= 0 && i < terrain.Length) ? (TerrainEnum)terrain[i] : TerrainEnum.deepWater;
        }
    }

    [Serializable]
    public class ScenarioRegionCell
    {
        public int row;
        public int col;
        public string region;
    }

    [Serializable]
    public class ScenarioSpriteCell
    {
        public int row;
        public int col;
        public string spriteName;
    }

    [Serializable]
    public class ScenarioZoneOfControlCell
    {
        public int row;
        public int col;
        public string leaderName;

        /// <summary>Which of leaderName's authored variants this cell was painted for ("" = the
        /// Base flavor specifically, not "any variant"). Only ever non-empty for a playable leader;
        /// see NationSpawner.ApplyScenarioZoneOfControl for how this is matched against the variant
        /// actually selected/surviving for that leader this game.</summary>
        public string variantId = "";
    }

    /// <summary>Pins one hidden object (matched by name against the Object-card catalog) to a
    /// specific hex, instead of leaving its placement to the random pass.</summary>
    [Serializable]
    public class ScenarioObject
    {
        public int row;
        public int col;
        public string objectName;
    }

    [Serializable]
    public class ScenarioPC
    {
        public int row;
        public int col;
        public string pcName;            // from a PC card
        public string ownerLeaderName;   // a leaderStart's leaderName, or empty for ownerless
        /// <summary>When ownerLeaderName is a playable leader, restricts ownership to a single
        /// variant of that leader (matched against LeaderVariantConfig.variantId). Empty ("Base")
        /// means ownership holds regardless of which variant (or the base leader) was actually
        /// chosen; otherwise the PC is removed entirely if a different variant (or the base) ends
        /// up in play. See NationSpawner.ReconcileScenarioVariantOwnership.</summary>
        public string ownerVariantId = "";
        /// <summary>Only consulted when ownerVariantId mismatches. Empty = destroy the PC
        /// (the default). Otherwise the name of a Non-Playable Leader to reassign as owner instead
        /// (spawned lazily at this PC's hex if not already present). See
        /// NationSpawner.ReconcileScenarioVariantOwnership.</summary>
        public string fallbackOwnerName = "";
        public int citySize = (int)PCSizeEnum.village;
        public int fortSize = (int)FortSizeEnum.NONE;
        public bool hasPort;
        public bool isHidden;
        public bool isCapital;
        /// <summary>The PC marks its hex as an entrance to the Underground (see Hex.IsUnderground).</summary>
        public bool isUnderground;
        public int loyalty = 100;
        public string region = "";
        public bool isIsland;
        public string pcFeature = "";
        public string fortFeature = "";
    }

    /// <summary>
    /// A companion character, OR — when <c>ownerLeaderName</c> equals <c>characterName</c> — a
    /// self-owned card standing in for a shared leader (playable or non-playable) itself. This
    /// self-owned form is the *only* record of that leader in the scenario: its mere presence at a
    /// hex is the leader's starting position, and <see cref="army"/> is the leader's starting army.
    /// Whether <c>characterName</c> is a playable or non-playable leader is looked up from
    /// PlayableLeaderBiomes.json/NonPlayableLeaderBiomes.json (see NationSpawner.FindLeaderBiome) —
    /// never stored here.
    /// </summary>
    [Serializable]
    public class ScenarioCharacter
    {
        public int row;
        public int col;
        public string characterName;     // from a Character card, or (self-owned) a leader's name
        public string ownerLeaderName;   // a leader's name; self-owned when equal to characterName
        /// <summary>Same variant-lock as ScenarioPC.ownerVariantId. On mismatch the fallback
        /// depends on what characterName represents — see NationSpawner.ReconcileScenarioVariantOwnership.</summary>
        public string ownerVariantId = "";
        /// <summary>Only consulted when ownerVariantId mismatches. Empty = destroy the character
        /// (the default). Otherwise the name of a Non-Playable Leader to reassign as owner instead
        /// (spawned lazily at this character's hex if not already present). See
        /// NationSpawner.ReconcileScenarioVariantOwnership.</summary>
        public string fallbackOwnerName = "";
        /// <summary>Self-owned playable-leader cards only: restricts the leader-selection carousel
        /// to a single variant (matched against <c>LeaderVariantConfig.variantId</c> in
        /// PlayableLeaderBiomes.json). Empty = no restriction (every variant offered).</summary>
        public string variantId = "";
        /// <summary>Independent spawn gate for a COMPANION character (ignored on a self-owned
        /// leader/variant card, whose presence is governed by the selection carousel instead): only
        /// created if this named playable leader (any leader in the scenario, not necessarily
        /// characterName's own owner) ends up with spawnConditionVariantId selected. Empty = always
        /// spawn (the default). See NationSpawner.ReconcileScenarioSpawnConditions.</summary>
        public string spawnConditionLeaderName = "";
        /// <summary>Only consulted when spawnConditionLeaderName is set. Empty means that leader's
        /// Base flavor; otherwise a <c>LeaderVariantConfig.variantId</c> of that leader.</summary>
        public string spawnConditionVariantId = "";
        /// <summary>Flips the spawn gate: false (default) = "Requires" (spawn only if the named
        /// leader IS playing with spawnConditionVariantId); true = "Excludes" (spawn only if that
        /// leader is NOT playing with it — either absent from the game entirely, or present with a
        /// different variant). Ignored when spawnConditionLeaderName is empty.</summary>
        public bool spawnConditionExclude = false;
        public ScenarioArmy army;        // null when the character bears no army
        /// <summary>Object-card names this character starts holding — resolved against the
        /// Object catalog and cloned onto Character.objects at spawn. See
        /// NationSpawner.SpawnScenarioCharacter. Applies to companions and self-owned
        /// leaders/NPLs alike ("every character can hold objects").</summary>
        public List<string> startingObjects = new();
    }

    /// <summary>An army described as a set of army-card stacks plus shared XP.</summary>
    [Serializable]
    public class ScenarioArmy
    {
        public int xp = 25;
        public List<ScenarioArmyStack> stacks = new();
        /// <summary>Independent spawn gate for this army specifically — its commander can still
        /// spawn without it. Only created if this named playable leader (any leader in the
        /// scenario) ends up with spawnConditionVariantId selected. Empty = always spawn (the
        /// default). See NationSpawner.ReconcileScenarioSpawnConditions.</summary>
        public string spawnConditionLeaderName = "";
        /// <summary>Only consulted when spawnConditionLeaderName is set. Empty means that leader's
        /// Base flavor; otherwise a <c>LeaderVariantConfig.variantId</c> of that leader.</summary>
        public string spawnConditionVariantId = "";
        /// <summary>Same "Requires"/"Excludes" flip as ScenarioCharacter.spawnConditionExclude.</summary>
        public bool spawnConditionExclude = false;

        public bool IsEmpty()
        {
            if (stacks == null) return true;
            foreach (ScenarioArmyStack s in stacks)
                if (s != null && s.amount > 0 && !string.IsNullOrWhiteSpace(s.armyCardName)) return false;
            return true;
        }
    }

    [Serializable]
    public class ScenarioArmyStack
    {
        public string armyCardName;      // from an Army card (supplies troop type + abilities)
        public int amount;
    }
}
