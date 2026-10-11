using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

// Resolves the baked per-facing spritesheets/atlases produced by the Animation Spritesheet
// Baker (see Assets/Editor/AnimationSpritesheetBaker.cs) for a given character, walking the
// same name -> race -> fallback tiers CharacterAnimatorControllers used for the Animator-driven
// system this replaces. Layout on disk (see AnimationSpritesheetBaker._outFolder / SaveAtlas):
//   Assets/Art/Characters/AnimationSpritesheets/[RaceOrName]/[RaceOrName]_[Back|Left|Forward|Right].png
//   Assets/Art/Characters/AnimationSpritesheets/[RaceOrName]/[RaceOrName]_[Back|Left|Forward|Right].atlas.json
// The atlas manifest TextAssets must be marked Addressable with the "default" label — that's
// what the initial scan finds them by (AnimationSpritesheetBaker does this automatically for
// anything it bakes from now on). The sliced frame Sprites don't need that scan at all: each
// manifest's own "texture" field gives the exact address of its PNG, which is loaded directly
// via Addressables.LoadAssetAsync<IList<Sprite>> — it just needs to BE Addressable (any label).
public static class CharacterSpritesheets
{
    private const string SpritesheetsLabel = "default";
    public const string AddressRoot = "Assets/Art/Characters/AnimationSpritesheets/";
    private static readonly string[] Facings = { "Forward", "Back", "Left", "Right" };

    [System.Serializable]
    public class AtlasFrame
    {
        public int index;
        public int x, y, width, height;
    }

    [System.Serializable]
    public class AtlasState
    {
        public string name;
        public string spriteNamePrefix;
        public int row;
        public int frameCount;
        public bool loop;
        public float clipLength;
        public float fps;
        public List<AtlasFrame> frames;
    }

    [System.Serializable]
    public class AtlasManifest
    {
        public string texture;
        public int textureWidth, textureHeight;
        public int cellWidth, cellHeight;
        public int framesPerState;
        public List<AtlasState> states;
    }

    // Keyed by normalized "{raceOrName}_{facing}", e.g. "gandalf_forward".
    private static readonly Dictionary<string, AtlasManifest> manifestsByKey = new();
    // Every baked frame Sprite, keyed by its exact sub-asset name (e.g. "Gandalf_Forward_Action_00").
    private static readonly Dictionary<string, Sprite> spritesByName = new();
    // Every raceOrName folder with at least one baked facing, lowercased.
    private static readonly HashSet<string> availableRaceOrNames = new();
    // Every raceOrName with a manifest (sheets may not be loaded yet), lowercased.
    private static readonly HashSet<string> knownRaceOrNames = new();
    private static readonly HashSet<string> requestedRaceOrNames = new();
    private static readonly Dictionary<string, int> sheetsPending = new();
    private static readonly Dictionary<string, string> folderByKey = new();

    private static bool loadStarted;
    private static bool manifestLocationsScanned;
    private static int pendingManifestLoads;
    private static int pendingSpriteLoads;

    // manifestLocationsScanned guards against a false "loaded" reading in the gap between the
    // initial location scan completing and the first manifest's OnManifestLoaded callback
    // actually running (which is what increments pendingSpriteLoads) — without it, IsLoaded
    // could read true for one frame while pendingManifestLoads/pendingSpriteLoads are still both
    // sitting at their pre-scan value of 0.
    public static bool IsLoaded => manifestLocationsScanned && pendingManifestLoads == 0 && pendingSpriteLoads == 0;

    public static void EnsureLoading()
    {
        if (loadStarted) return;
        loadStarted = true;
        Addressables.LoadResourceLocationsAsync(SpritesheetsLabel, typeof(TextAsset)).Completed += OnManifestLocationsLoaded;
    }

    private static void OnManifestLocationsLoaded(AsyncOperationHandle<IList<IResourceLocation>> handle)
    {
        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            manifestLocationsScanned = true;
            Debug.LogError($"CharacterSpritesheets: failed to load Addressables label '{SpritesheetsLabel}' for atlas manifests.");
            return;
        }

        pendingManifestLoads = 0;
        foreach (IResourceLocation location in handle.Result)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.PrimaryKey)) continue;
            if (!location.PrimaryKey.StartsWith(AddressRoot) || !location.PrimaryKey.EndsWith(".atlas.json")) continue;

            pendingManifestLoads++;
            Addressables.LoadAssetAsync<TextAsset>(location).Completed += OnManifestLoaded;
        }
        manifestLocationsScanned = true;
    }

    private static void OnManifestLoaded(AsyncOperationHandle<TextAsset> handle)
    {
        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
        {
            RegisterManifest(handle.Result);
        }

        pendingManifestLoads = Mathf.Max(0, pendingManifestLoads - 1);
        if (pendingManifestLoads == 0)
        {
            Debug.Log($"CharacterSpritesheets: loaded {manifestsByKey.Count} atlas manifest(s), {pendingSpriteLoads} spritesheet(s) still loading.");
        }
    }

    private static void RegisterManifest(TextAsset asset)
    {
        AtlasManifest manifest = JsonUtility.FromJson<AtlasManifest>(asset.text);
        if (manifest?.states == null) return;

        // Unity's importer strips only the final ".json", so a source file named
        // "Gandalf_Forward.atlas.json" becomes a TextAsset named "Gandalf_Forward.atlas" — strip
        // the remaining ".atlas" too before splitting off the facing suffix.
        string stem = asset.name.EndsWith(".atlas") ? asset.name.Substring(0, asset.name.Length - ".atlas".Length) : asset.name;
        if (!TrySplitFacing(stem, out string raceOrName, out string facing)) return;

        manifestsByKey[NormalizeKey(raceOrName, facing)] = manifest;
        // Only the manifest is registered here. The sheets themselves (171 of them, each a
        // multi-megabyte texture) used to ALL load at boot — gigabytes of textures for characters
        // that never appear, which made startup take minutes and pushed the editor into swap.
        // Now a raceOrName's sheets load the first time something asks for it (RequestSheets).
        knownRaceOrNames.Add(raceOrName.ToLowerInvariant());
        folderByKey[raceOrName.ToLowerInvariant()] = raceOrName; // exact casing, for addresses
    }

    // Loads every facing sheet of one raceOrName, once. availableRaceOrNames is only filled when
    // all its frames are in, so a character never resolves to a name whose frames are missing.
    private static void RequestSheets(string name)
    {
        string key = name.ToLowerInvariant();
        string raceOrName = folderByKey.TryGetValue(key, out string folder) ? folder : name;
        if (!requestedRaceOrNames.Add(key)) return;

        // Addressables.LoadResourceLocationsAsync(label, typeof(Sprite)) does NOT reliably
        // enumerate the individual sliced sub-sprites of a multi-sprite texture as separate
        // locations — in practice it only ever surfaced one location per PNG here. Loading
        // IList<Sprite> against the TEXTURE's own address is the documented way to pull every
        // sub-sprite of one multi-sprite texture at once; manifest.texture gives that filename.
        int sheets = 0;
        foreach (string facing in Facings)
        {
            AtlasManifest manifest = GetManifest(raceOrName, facing);
            if (manifest == null || string.IsNullOrEmpty(manifest.texture)) continue;
            string pngAddress = $"{AddressRoot}{raceOrName}/{manifest.texture}";
            sheets++;
            pendingSpriteLoads++;
            Addressables.LoadAssetAsync<IList<Sprite>>(pngAddress).Completed += handle => OnSpriteSheetLoaded(handle, key);
        }
        if (sheets == 0) { availableRaceOrNames.Add(key); return; }
        sheetsPending[key] = sheets;
    }

    private static void OnSpriteSheetLoaded(AsyncOperationHandle<IList<Sprite>> handle, string key)
    {
        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
        {
            foreach (Sprite sprite in handle.Result)
                if (sprite != null) spritesByName[sprite.name] = sprite;
        }
        else
        {
            Debug.LogError($"CharacterSpritesheets: failed to load sprite frames for '{handle.DebugName}'.");
        }

        pendingSpriteLoads = Mathf.Max(0, pendingSpriteLoads - 1);
        int left = sheetsPending.TryGetValue(key, out int n) ? n - 1 : 0;
        sheetsPending[key] = left;
        if (left <= 0)
        {
            // Only NOW is this raceOrName safe to resolve to — GetSprite(...) can return its frames.
            availableRaceOrNames.Add(key);
            Debug.Log($"CharacterSpritesheets: '{key}' ready ({spritesByName.Count} frames loaded in total).");
        }
    }

    private static bool TrySplitFacing(string stem, out string raceOrName, out string facing)
    {
        foreach (string candidate in Facings)
        {
            string suffix = "_" + candidate;
            if (stem.EndsWith(suffix))
            {
                raceOrName = stem.Substring(0, stem.Length - suffix.Length);
                facing = candidate;
                return true;
            }
        }
        raceOrName = null;
        facing = null;
        return false;
    }

    private static string NormalizeKey(string raceOrName, string facing) =>
        $"{raceOrName.Trim().ToLowerInvariant()}_{facing.Trim().ToLowerInvariant()}";

    // Tries characterName, then (if this is a leader variant) the base leader name it was
    // transformed from, then race, then fallback (in that order), returning the first one with
    // at least one baked facing. The specific facing requested later via GetManifest can still
    // come back null if only some of that raceOrName's facings were baked.
    public static bool TryResolveRaceOrName(string characterName, string variantBaseName, RacesEnum race, string fallback, out string raceOrName)
    {
        EnsureLoading();
        raceOrName = null;
        // All manifests (small JSON) must be in before any tier can be judged: otherwise a
        // less-specific tier (variant base / race / fallback) could win just because the
        // character's own manifest hadn't arrived yet, locking in the wrong look (e.g. Galadriel
        // rendering as generic "Elf") — nothing re-resolves an already-"showing" character.
        if (!ManifestsReady) return false;

        // Tiers in order. The first tier that HAS baked sheets wins; if its sheets aren't loaded
        // yet they are requested and resolution waits for them (never falls through to a lower
        // tier, for the same reason as above). While anything is loading IsLoaded stays false,
        // so callers keep retrying.
        foreach (string candidate in new[] { characterName, variantBaseName, race.ToString(), fallback })
        {
            if (TryTier(candidate, out raceOrName, out bool waiting)) return true;
            if (waiting) return false;
        }
        raceOrName = null;
        return false;
    }

    private static bool ManifestsReady => manifestLocationsScanned && pendingManifestLoads == 0;

    // Baked folder names never contain spaces (e.g. "TomBombadil"), but a Character's given name
    // can (e.g. "Tom Bombadil") — strip spaces before matching so those still resolve to their
    // own art instead of falling through to the race tier, and return the stripped form so later
    // GetManifest/GetSprite lookups (which use it verbatim) line up with the folder name too.
    private static bool TryTier(string candidate, out string raceOrName, out bool waiting)
    {
        raceOrName = null;
        waiting = false;
        string stripped = candidate?.Replace(" ", "").Trim();
        if (string.IsNullOrEmpty(stripped)) return false;
        string key = stripped.ToLowerInvariant();
        if (!knownRaceOrNames.Contains(key)) return false;
        if (HasAny(stripped)) { raceOrName = stripped; return true; }
        RequestSheets(stripped);
        waiting = !HasAny(stripped);
        if (!waiting) { raceOrName = stripped; return true; }
        return false;
    }

    private static bool HasAny(string raceOrName) =>
        !string.IsNullOrWhiteSpace(raceOrName) && availableRaceOrNames.Contains(raceOrName.Trim().ToLowerInvariant());

    public static AtlasManifest GetManifest(string raceOrName, string facing) =>
        manifestsByKey.TryGetValue(NormalizeKey(raceOrName, facing), out AtlasManifest manifest) ? manifest : null;

    public static Sprite GetSprite(string exactName) =>
        spritesByName.TryGetValue(exactName, out Sprite sprite) ? sprite : null;
}
