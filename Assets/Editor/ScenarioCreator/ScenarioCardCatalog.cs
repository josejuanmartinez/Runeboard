using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RetroLOTR.Scenarios.EditorTools
{
    /// <summary>
    /// Editor-side lookups for the scenario creator: pulls leader names, card names (PC /
    /// Character / Army / Land regions) and representative terrain sprites from the same
    /// Resources content the game ships with, so the authoring dropdowns always match runtime.
    /// </summary>
    public static class ScenarioCardCatalog
    {
        private static List<string> _playableLeaders;
        private static List<string> _nonPlayableLeaders;
        private static Dictionary<string, List<LeaderVariantConfig>> _playableVariantsByName;
        // Reverse of _playableVariantsByName: a variant's own characterName (e.g. "The Iron
        // Crown") -> its base leader's characterName (e.g. "Sauron"), for GetCharacterIdleSprite's
        // fallback tier.
        private static Dictionary<string, string> _baseLeaderNameByVariantName;
        private static List<string> _pcCards;
        private static List<string> _characterCards;
        private static List<string> _armyCards;
        private static List<string> _regions;
        private static List<string> _objectCardNames;
        private static Dictionary<TerrainEnum, Sprite> _terrainSprites;
        private static List<CardData> _allCards;
        private static Dictionary<string, CardData> _cardsByName;
        private static Dictionary<string, List<string>> _deckLabelsByCardName;
        private static readonly Dictionary<string, Sprite> _artCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> _pcHexCache = new(StringComparer.Ordinal);
        private static bool _pcHexLoaded;
        private static HexTextureMapping _mapping;
        private static readonly Dictionary<string, Sprite> _characterIdleCache = new(StringComparer.OrdinalIgnoreCase);
        private const string AnimationSpritesheetsRoot = "Assets/Art/Characters/AnimationSpritesheets";

        public static IReadOnlyList<string> PlayableLeaders => _playableLeaders ??= LoadLeaderNames("PlayableLeaderBiomes");
        public static IReadOnlyList<string> NonPlayableLeaders => _nonPlayableLeaders ??= LoadLeaderNames("NonPlayableLeaderBiomes");
        public static IReadOnlyList<string> PcCards => _pcCards ??= NamesOfType(CardTypeEnum.PC);
        public static IReadOnlyList<string> CharacterCards => _characterCards ??= NamesOfType(CardTypeEnum.Character);
        public static IReadOnlyList<string> ArmyCards => _armyCards ??= NamesOfType(CardTypeEnum.Army);
        public static IReadOnlyList<string> Regions => _regions ??= NamesOfType(CardTypeEnum.Land);

        /// <summary>Names of every card in the Object-card catalog (Cards/Modular/ObjectsDeck.json),
        /// for the scenario creator's hex-placement picker (Board.PlaceScenarioObjects) and the
        /// per-character starting-objects picker (NationSpawner.SpawnScenarioCharacter).</summary>
        public static IReadOnlyList<string> ObjectCardNames => _objectCardNames ??= NamesOfType(CardTypeEnum.Object);

        private static List<string> _allCardNames;

        /// <summary>Every card name across all decks (any type) — pool for the scenario's
        /// representative-card picker.</summary>
        public static IReadOnlyList<string> AllCardNames
        {
            get
            {
                if (_allCardNames == null)
                {
                    EnsureCardsLoaded();
                    _allCardNames = _cardsByName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                }
                return _allCardNames;
            }
        }

        /// <summary>The card behind a name (first match across all decks), for previews. May be null.</summary>
        public static CardData GetCard(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            EnsureCardsLoaded();
            return _cardsByName.TryGetValue(name.Trim(), out CardData card) ? card : null;
        }

        /// <summary>
        /// Deck ids of every deck that contains this card, either directly (a named card) or
        /// via a reference copy (referenceDeckId/referenceCardId resolved to the original name).
        /// </summary>
        public static IReadOnlyList<string> GetDecksContaining(string cardName)
        {
            if (string.IsNullOrWhiteSpace(cardName)) return Array.Empty<string>();
            EnsureCardsLoaded();
            return _deckLabelsByCardName.TryGetValue(cardName.Trim(), out List<string> labels)
                ? labels
                : Array.Empty<string>();
        }

        public static List<Sprite> GetTerrainVariations(TerrainEnum terrain)
        {
            EnsureMapping();
            return _mapping != null ? _mapping.GetTerrainVariations(terrain) : null;
        }

        public static Sprite GetTerrainSpriteByName(string spriteName)
        {
            EnsureMapping();
            return _mapping != null ? _mapping.GetTerrainSpriteByName(spriteName) : null;
        }

        // Card artwork, resolved the same way DeckExplorer does (search the card/UI art folders).
        public static Sprite GetCardArtwork(CardData card)
        {
            if (card == null) return null;
            foreach (string candidate in new[] { card.spriteName, card.portraitName, card.name })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (_artCache.TryGetValue(candidate, out Sprite cached)) { if (cached != null) return cached; continue; }
                Sprite found = FindSprite(candidate);
                _artCache[candidate] = found;
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// The hex-map artwork for a placed PC (city), loaded from Assets/Art/Hexes/PCs.
        /// The card name (e.g. "HelmDeep", "FangornSCamp") and the art filename
        /// (e.g. "Helm Deep.png", "Fangorns Camp.png") often differ only in spacing/casing,
        /// so we match on the same normalized key the runtime uses (HexTextureMapping.NormalizeKey),
        /// not an exact string compare. May be null when no art exists.
        /// </summary>
        public static Sprite GetPcHexSprite(string pcName)
        {
            if (string.IsNullOrWhiteSpace(pcName)) return null;
            string key = HexTextureMapping.NormalizeKey(pcName);
            if (string.IsNullOrEmpty(key)) return null;

            EnsurePcHexLoaded();
            return _pcHexCache.TryGetValue(key, out Sprite sprite) ? sprite : null;
        }

        // Indexes every sprite under Assets/Art/Hexes/PCs by its normalized name, mirroring the
        // runtime lookup so the same card name resolves to the same art in editor and game.
        private static void EnsurePcHexLoaded()
        {
            if (_pcHexLoaded) return;
            _pcHexLoaded = true;
            _pcHexCache.Clear();

            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/Art/Hexes/PCs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path)) continue;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is not Sprite sprite) continue;
                    string spriteKey = HexTextureMapping.NormalizeKey(sprite.name);
                    if (!string.IsNullOrEmpty(spriteKey) && !_pcHexCache.ContainsKey(spriteKey))
                        _pcHexCache[spriteKey] = sprite;
                }
            }
        }

        /// <summary>
        /// The variants of a playable leader (from PlayableLeaderBiomes.json), used by the
        /// scenario creator to let an author pin a leader start to one variant. Empty when the
        /// name is not a known playable leader or it has no variants.
        /// </summary>
        public static IReadOnlyList<LeaderVariantConfig> GetPlayableLeaderVariants(string leaderName)
        {
            if (string.IsNullOrWhiteSpace(leaderName)) return Array.Empty<LeaderVariantConfig>();
            EnsurePlayableVariantsLoaded();
            return _playableVariantsByName.TryGetValue(leaderName.Trim(), out List<LeaderVariantConfig> variants)
                ? variants
                : Array.Empty<LeaderVariantConfig>();
        }

        private static void EnsurePlayableVariantsLoaded()
        {
            if (_playableVariantsByName != null) return;
            _playableVariantsByName = new Dictionary<string, List<LeaderVariantConfig>>(StringComparer.OrdinalIgnoreCase);
            _baseLeaderNameByVariantName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            TextAsset asset = Resources.Load<TextAsset>("PlayableLeaderBiomes");
            if (asset == null) return;
            LeaderBiomeConfigCollection collection = JsonUtility.FromJson<LeaderBiomeConfigCollection>(asset.text);
            if (collection?.biomes == null) return;

            foreach (LeaderBiomeConfig biome in collection.biomes)
            {
                if (biome == null || string.IsNullOrWhiteSpace(biome.characterName)) continue;
                List<LeaderVariantConfig> variants = biome.variants ?? new List<LeaderVariantConfig>();
                _playableVariantsByName[biome.characterName.Trim()] = variants;

                foreach (LeaderVariantConfig variant in variants)
                {
                    if (variant == null || string.IsNullOrWhiteSpace(variant.characterName)) continue;
                    if (string.Equals(variant.characterName.Trim(), biome.characterName.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    _baseLeaderNameByVariantName[variant.characterName.Trim()] = biome.characterName.Trim();
                }
            }
        }

        /// <summary>The base playable leader's characterName for one of its variant characterNames
        /// (e.g. "The Iron Crown" -> "Sauron"), or null if the given name isn't a known variant.</summary>
        private static string GetBaseLeaderNameForVariant(string variantCharacterName)
        {
            if (string.IsNullOrWhiteSpace(variantCharacterName)) return null;
            EnsurePlayableVariantsLoaded();
            return _baseLeaderNameByVariantName.TryGetValue(variantCharacterName.Trim(), out string baseName) ? baseName : null;
        }

        /// <summary>
        /// First frame of the baked "standing idle", Forward-facing spritesheet for a character
        /// or leader name — the same art CharacterAnimationController shows in-game — so the
        /// Scenario Creator can preview spawned characters on the map instead of leaving them as
        /// bare markers. Resolution mirrors CharacterSpritesheets.TryResolveRaceOrName's tiers
        /// (exact name, then — for a playable leader variant — its base leader's name, then that
        /// name's Character-card race); unlike the runtime resolver this reads the Editor
        /// AssetDatabase directly (synchronous, no Addressables load) since it only ever runs
        /// in-editor. Null if nothing is baked for any tier.
        /// </summary>
        public static Sprite GetCharacterIdleSprite(string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName)) return null;
            string key = characterName.Trim();
            if (_characterIdleCache.TryGetValue(key, out Sprite cached)) return cached;

            // Baked spritesheet folders are named after the character with spaces stripped (e.g.
            // "TomBombadil" for "Tom Bombadil") — match CharacterSpritesheets' own normalization.
            Sprite sprite = LoadIdleSprite(key.Replace(" ", ""));

            // A leader variant (e.g. "The Iron Crown") rarely has its own baked spritesheet — try
            // the base leader's ("Sauron") before falling all the way back to race, same as the
            // runtime CharacterSpritesheets.TryResolveRaceOrName order.
            if (sprite == null)
            {
                string baseLeaderName = GetBaseLeaderNameForVariant(key);
                if (baseLeaderName != null)
                    sprite = LoadIdleSprite(baseLeaderName.Replace(" ", ""));
            }

            if (sprite == null)
            {
                RacesEnum race = GetCard(key)?.race ?? RacesEnum.Common;
                sprite = LoadIdleSprite(race.ToString());
            }
            _characterIdleCache[key] = sprite;
            return sprite;
        }

        // Loads the first "standing idle" Forward-facing frame for one AnimationSpritesheets
        // folder (a character name or a race name). Layout matches CharacterSpritesheets'
        // AddressRoot convention: [root]/[raceOrName]/[raceOrName]_Forward.{atlas.json,png}.
        private static Sprite LoadIdleSprite(string raceOrName)
        {
            if (string.IsNullOrWhiteSpace(raceOrName)) return null;
            string stem = $"{AnimationSpritesheetsRoot}/{raceOrName}/{raceOrName}_Forward";

            TextAsset manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{stem}.atlas.json");
            if (manifestAsset == null) return null;
            CharacterSpritesheets.AtlasManifest manifest = JsonUtility.FromJson<CharacterSpritesheets.AtlasManifest>(manifestAsset.text);
            CharacterSpritesheets.AtlasState idle = manifest?.states?.Find(s => s != null && s.name == "standing idle");
            if (idle == null || string.IsNullOrWhiteSpace(idle.spriteNamePrefix)) return null;

            string frameName = $"{idle.spriteNamePrefix}_00";
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath($"{stem}.png"))
                if (asset is Sprite sprite && sprite.name == frameName) return sprite;
            return null;
        }

        /// <summary>All leader names (playable first), used for owner dropdowns.</summary>
        public static List<string> AllLeaders()
        {
            var list = new List<string>();
            list.AddRange(PlayableLeaders);
            list.AddRange(NonPlayableLeaders);
            return list.Distinct().ToList();
        }

        public static void Invalidate()
        {
            _playableLeaders = _nonPlayableLeaders = _pcCards = _characterCards = _armyCards = _regions = null;
            _objectCardNames = null;
            _allCardNames = null;
            _playableVariantsByName = null;
            _terrainSprites = null;
            _allCards = null;
            _cardsByName = null;
            _deckLabelsByCardName = null;
            _artCache.Clear();
            _pcHexCache.Clear();
            _pcHexLoaded = false;
            _mapping = null;
            _characterIdleCache.Clear();
        }

        private static List<string> NamesOfType(CardTypeEnum type)
        {
            EnsureCardsLoaded();
            return _allCards
                .Where(c => c != null && c.GetCardType() == type && !string.IsNullOrWhiteSpace(c.name))
                .Select(c => c.name.Trim())
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        private static void EnsureCardsLoaded()
        {
            if (_allCards != null) return;
            _allCards = new List<CardData>();
            _cardsByName = new Dictionary<string, CardData>(StringComparer.OrdinalIgnoreCase);
            _deckLabelsByCardName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            TextAsset manifestAsset = Resources.Load<TextAsset>("Cards");
            if (manifestAsset == null) return;
            CardsManifest manifest = JsonUtility.FromJson<CardsManifest>(manifestAsset.text);
            if (manifest?.decks == null) return;

            var loadedDecks = new List<(string deckId, DeckData deck)>();
            var cardsByKey = new Dictionary<string, CardData>(StringComparer.OrdinalIgnoreCase);
            var parentByDeckId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DeckManifestEntry entry in manifest.decks)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.resourcePath)) continue;
                TextAsset deckAsset = Resources.Load<TextAsset>(entry.resourcePath);
                if (deckAsset == null) continue;
                DeckData deck = JsonUtility.FromJson<DeckData>(deckAsset.text);
                if (deck?.cards == null) continue;

                string deckId = !string.IsNullOrWhiteSpace(entry.deckId) ? entry.deckId.Trim()
                              : deck.deckId?.Trim() ?? string.Empty;
                loadedDecks.Add((deckId, deck));
                if (!string.IsNullOrEmpty(deckId))
                    parentByDeckId[deckId] = entry.parentDeckId?.Trim() ?? string.Empty;

                foreach (CardData card in deck.cards)
                {
                    if (card == null) continue;
                    if (!string.IsNullOrEmpty(deckId)) cardsByKey[$"{deckId}::{card.cardId}"] = card;
                    if (string.IsNullOrWhiteSpace(card.name)) continue;
                    _allCards.Add(card);
                    string key = card.name.Trim();
                    if (!_cardsByName.ContainsKey(key)) _cardsByName[key] = card;
                }
            }

            // Deck membership: a named card counts for its own deck; a reference copy counts
            // for the deck that holds the reference, under the original card's resolved name.
            foreach ((string deckId, DeckData deck) in loadedDecks)
            {
                string label = string.IsNullOrEmpty(deckId) ? "(no id)" : deckId;
                foreach (CardData card in deck.cards)
                {
                    if (card == null) continue;
                    string name = !string.IsNullOrWhiteSpace(card.name)
                        ? card.name.Trim()
                        : ResolveReferencedCardName(card.referenceDeckId, card.referenceCardId, cardsByKey, parentByDeckId);
                    if (string.IsNullOrEmpty(name)) continue;

                    if (!_deckLabelsByCardName.TryGetValue(name, out List<string> labels))
                        _deckLabelsByCardName[name] = labels = new List<string>();
                    if (!labels.Contains(label)) labels.Add(label);
                }
            }
        }

        // Follows a reference (deckId + cardId) to the original card's name. Missing cards fall
        // back to the parent-deck chain (references may point at a subdeck that inherits the
        // card), and chained references are followed with cycle protection.
        private static string ResolveReferencedCardName(
            string deckId, int cardId,
            Dictionary<string, CardData> cardsByKey,
            Dictionary<string, string> parentByDeckId)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrWhiteSpace(deckId) && cardId > 0)
            {
                CardData found = null;
                string currentDeckId = deckId.Trim();
                var seenDecks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (!string.IsNullOrEmpty(currentDeckId) && seenDecks.Add(currentDeckId))
                {
                    if (cardsByKey.TryGetValue($"{currentDeckId}::{cardId}", out found) && found != null) break;
                    found = null;
                    if (!parentByDeckId.TryGetValue(currentDeckId, out string parent)) break;
                    currentDeckId = parent?.Trim();
                }

                if (found == null) return null;
                if (!string.IsNullOrWhiteSpace(found.name)) return found.name.Trim();
                if (!visited.Add($"{found.referenceDeckId}::{found.referenceCardId}")) return null;
                deckId = found.referenceDeckId;
                cardId = found.referenceCardId;
            }
            return null;
        }

        private static void EnsureMapping()
        {
            if (_mapping != null) return;
            _mapping = FindHexTextureMapping();
        }

        // Card art lookup mirroring DeckExplorerWindow.FindSprite.
        private static Sprite FindSprite(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string[] roots = { "Assets/Art/Cards", "Assets/Art/UI" };
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:Sprite", roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path)) continue;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Sprite sprite && string.Equals(sprite.name, name, StringComparison.OrdinalIgnoreCase))
                        return sprite;
                }
            }
            return null;
        }

        public static Sprite GetTerrainSprite(TerrainEnum terrain)
        {
            _terrainSprites ??= LoadTerrainSprites();
            return _terrainSprites.TryGetValue(terrain, out Sprite s) ? s : null;
        }

        private static List<string> LoadLeaderNames(string resource)
        {
            TextAsset asset = Resources.Load<TextAsset>(resource);
            if (asset == null) return new List<string>();
            // Both files share the LeaderBiomeConfigCollection shape (a 'biomes' array).
            LeaderBiomeConfigCollection collection = JsonUtility.FromJson<LeaderBiomeConfigCollection>(asset.text);
            if (collection?.biomes == null) return new List<string>();
            return collection.biomes
                .Where(b => b != null && !string.IsNullOrWhiteSpace(b.characterName))
                .Select(b => b.characterName.Trim())
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // Reads the first sprite variation per terrain off whichever prefab carries the
        // HexTextureMapping component (the Hex prefab), so the editor grid shows real tiles.
        private static Dictionary<TerrainEnum, Sprite> LoadTerrainSprites()
        {
            var result = new Dictionary<TerrainEnum, Sprite>();
            HexTextureMapping mapping = FindHexTextureMapping();
            if (mapping == null) return result;

            void Add(TerrainEnum t, List<Sprite> variations)
            {
                if (variations != null && variations.Count > 0 && variations[0] != null)
                    result[t] = variations[0];
            }

            Add(TerrainEnum.deepWater, mapping.deepWaterVariations);
            Add(TerrainEnum.shallowWater, mapping.shallowWaterVariations);
            Add(TerrainEnum.desert, mapping.desertVariations);
            Add(TerrainEnum.forest, mapping.forestVariations);
            Add(TerrainEnum.grasslands, mapping.grassVariations);
            Add(TerrainEnum.hills, mapping.hillsVariations);
            Add(TerrainEnum.plains, mapping.plainsVariations);
            Add(TerrainEnum.shore, mapping.shoreVariations);
            Add(TerrainEnum.swamp, mapping.swampVariations);
            Add(TerrainEnum.wastelands, mapping.wastelandsVariations);
            Add(TerrainEnum.mountains, mapping.mountainsVariations);
            Add(TerrainEnum.snow, mapping.snowVariations);
            return result;
        }

        private static HexTextureMapping FindHexTextureMapping()
        {
            // The component may live on the Hex prefab, on another prefab (e.g. the
            // Board), nested inside one, or only on a scene object.
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                HexTextureMapping mapping = prefab.GetComponentInChildren<HexTextureMapping>(true);
                if (mapping != null) return mapping;
            }
            return UnityEngine.Object.FindAnyObjectByType<HexTextureMapping>(FindObjectsInactive.Include);
        }
    }
}
