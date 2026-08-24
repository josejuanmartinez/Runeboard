using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using Unity.Collections;
using UnityEngine;

public class Hex : MonoBehaviour
{
    private enum SharedParticleType
    {
        Fire,
        Ice,
        Poison,
        Courage,
        Hope,
        PcReveal
    }

    private sealed class SharedParticlePoolState
    {
        public GameObject template;
        public readonly List<GameObject> instances = new();
        public Vector3 localPosition = Vector3.zero;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
    }

    public Vector2Int v2;
    [Header("References")]
    public TextMeshPro messageNoUI;
    public SpriteRenderer hexRegion;
    public FrameColors framesColors;

    [Header("Character")]
    public SpriteRenderer characterSpriteRenderer;
    public SpriteRenderer bannerSpriteRenderer;
    [SerializeField] private CharacterAnimationController characterAnimationController;
    [Tooltip("Local-space offset applied per extra army-size duplicate, stacking toward the top-right behind the main character sprite.")]
    [SerializeField] private Vector2 armyStackOffset = new Vector2(0.08f, 0.08f);
    [Tooltip("Army.GetSize() breakpoints mapping troop count to stack copies 2-5: a size at or above thresholds[i] shows i+3 total copies.")]
    [SerializeField] private int[] armyStackSizeThresholds = { 150, 350, 650, 1100 };

    [Header("PC Name")]
    public TextMeshPro pcName;
    private SpriteRendererFitToTMP pcTextBandFit;

    [Header("Reveal")]
    [SerializeField] private float revealDuration = 1f;

    [Header("Hover")]
    public GameObject artifact;
    public GameObject encounter;
    public GameObject artifactBg;
    public HoverNoUI artifactHover;
    public HoverNoUI encounterHover;

    [Header("Hex Info Panel")]
    [SerializeField] private float hexInfoHoverDelay = 2f;
    [Tooltip("Seconds the cursor must stay on a PC hex, uninterrupted, before its PC/region card preview appears.")]
    [SerializeField] private float pcCardPreviewHoverDelay = 0.5f;

    [Header("Lazily attached sub-prefabs (Resources/HexParts)")]
    [Tooltip("Instantiated ONCE per scene as the shared particle-system pool templates (fire/ice/poison/courage/hope + selection).")]
    [SerializeField] private GameObject sharedParticlesPrefab;
    [Tooltip("Attached to a hex on its first floating message (MessageNoUIText.prefab): TMP text + fitted SpriteRenderer band.")]
    [SerializeField] private GameObject messageNoUITextPrefab;
    [Tooltip("Attached to a hex when a character needs to show: character sprite/Animator, banner, class icons.")]
    [SerializeField] private GameObject characterLayerPrefab;
    [Tooltip("Attached to a hex when its label needs to show PC name, armies, and/or scouted characters.")]
    [SerializeField] private GameObject pcTextPrefab;
    [Tooltip("Attached to a hex when a movement cost bubble needs to show.")]
    [SerializeField] private GameObject movementCostPrefab;

    [Header("Grid Sprite Rendereres")]
    public GameObject spriteRendererLayoutIcon;
    // public SpriteRendererGridLayout characterClassesIconGrid;

    public SpriteRenderer terrainTexture;
    public SpriteRenderer pcTexture;
    [Tooltip("Marker shown when this hex is an entrance to the Underground (chasm tiles or underground PCs).")]
    public GameObject underground;
    public GameObject cliffGameObject;
    public GameObject hexTextureWater;

    public GameObject movement;
    public MovementCostManager movementCostManager;



    private int darknessTurnsRemaining = 0;

    [Header("Data")]
    [SerializeField] private PC pc;
    [SerializeField] private string assignedLandRegion;
    [SerializeField] private bool isRevealed;
    [SerializeField] private bool mapOnlyRevealed;
    [SerializeField] private bool isCurrentlyUnseen;
    public TerrainEnum terrainType;
    // True when this hex's tile art depicts a chasm (an Underground entrance, see ChasmTiles).
    private bool isChasm;
    public List<Army> armies = new();
    public List<Character> characters = new();
    public List<CardData> hiddenObjects = new();
    private readonly List<CardData> _pendingEncounters = new();
    // Extra duplicate SpriteRenderers stacked behind characterSpriteRenderer, one per army-size
    // tier above 1 (see UpdateArmyStackVisual) — kept even when hidden so redraws just toggle them.
    private readonly List<SpriteRenderer> armyStackExtraRenderers = new();
    private const int MaxArmyStackExtraRenderers = 4;
    private static readonly int ArmyStackOutlineSizeShaderId = Shader.PropertyToID("_OutlineSize");


    private Coroutine classArrangeCoroutine;
    private Coroutine hexInfoShowCoroutine;
    private Coroutine pcCardPreviewCoroutine;
    private string _hoverTextCache;   // hover text lives here even when this hex doesn't own the shared HexUIHover panel
    private bool _hoverTextDirty;     // rebuild deferred during movement; Hover() rebuilds on demand
    private bool _iconGridsPendingRebuild;   // grids were cleared while SuppressHexIconGrids was on; defeats RevealInternal's seen-hex early-out until rebuilt
    private DeckManager _deckManager;
    private bool _showingPcCardPreview;
    private bool artifactRevealed = false;
    private static Hex s_hexInfoActiveHex;

    // Use HashSet for O(1) contains
    private HashSet<Leader> scoutedBy = new();
    private readonly Dictionary<Leader, int> scoutedByTurns = new();
    private readonly HashSet<Leader> persistentScoutedBy = new();
    private readonly Dictionary<Leader, int> anchoredWarships = new();
    private int anchoredWarshipsTotal = 0;
    private Coroutine revealPulseCoroutine;
    private Vector3 terrainBaseScale;
    private bool terrainBaseScaleCaptured;
    private Coroutine pcRevealPulseCoroutine;
    private bool terrainOverdrawApplied;
    // Last reveal state pushed to the seamless-blend rebuild queue; neighbors must re-blend
    // whenever this flips (their rims either fade into fog toward us or blend our art).
    private bool seamlessRevealedLast;

    public bool isSelected = false;

    // Cached for speed
    private PlayableLeader leader;

    // Cached singletons/components (avoid repeated global lookups)
    private Board board;
    private BoardNavigator navigator;
    private Game game;

    private Illustrations illustrations;
    private HexTextureMapping hexTextureMapping;
    private Sprite baseTerrainSprite;
    private Coroutine bannerRetryCoroutine;

    // Reused buffers to avoid GC in UI building / raycasts
    private static readonly StringBuilder sbChars = new(256);
    private static readonly Queue<Vector2Int> areaQueue = new(64);
    private static readonly HashSet<Vector2Int> areaVisited = new();
    private static GameObject sharedSelectedParticles;
    private static Hex sharedSelectedParticlesOwner;
    private static Vector3 sharedSelectedParticlesLocalPosition = Vector3.zero;
    private static Quaternion sharedSelectedParticlesLocalRotation = Quaternion.identity;
    private static Vector3 sharedSelectedParticlesLocalScale = Vector3.one;
    private static readonly Dictionary<SharedParticleType, SharedParticlePoolState> sharedParticlePools = new();
    private static Transform sharedParticlePoolRoot;

    private const string Unknown = "Unknown character(s)";
    private const int DarknessTurnsDefault = 2;
    private const int SharedOneShotParticlePoolSize = 3;
    private bool isCharacterHovered = false;
    private bool isPcTextHovered = false;
    // Counts hexes whose character sprite is currently hovered (should only ever be 0 or 1, but a
    // counter sidesteps any transient overlap between one OnMouseExit and the next OnMouseEnter).
    // Lets every hex's UpdateCharacterSpriteAlpha tell "someone else is hovered" apart from
    // "nobody is hovered" instead of only knowing about its own character.
    private static int hoveredCharacterCount = 0;

    // Scene singletons shared by every hex. Cached statically because Awake runs once
    // per instantiated hex and FindFirstObjectByType scans the whole scene — per-hex
    // lookups made board creation O(hexes²) and took minutes on large maps. Unity's
    // overloaded null-check makes destroyed references re-resolve on the next scene.
    private static Game sharedGame;
    private static Board sharedBoard;
    private static BoardNavigator sharedNavigator;
    private static Illustrations sharedIllustrations;

    // The terrain-sprite catalog is identical for every hex, so one shared instance
    // serves them all. Resolved from this hex if the component is still on the prefab,
    // otherwise from anywhere in the scene (e.g. the Board object) — which allows the
    // heavy serialized component to be REMOVED from the hex prefab entirely.
    private static HexTextureMapping sharedTextureMapping;

    private HexTextureMapping ResolveTextureMapping()
    {
        if (sharedTextureMapping == null)
        {
            sharedTextureMapping = GetComponent<HexTextureMapping>();
            if (sharedTextureMapping == null) sharedTextureMapping = FindFirstObjectByType<HexTextureMapping>();
        }
        return sharedTextureMapping;
    }

    private void ApplyCurrentFont(TMP_Text text)
    {
        if (FontManager.Instance != null) FontManager.Instance.ApplyCurrentFont(text);
    }

    void Awake()
    {
        pc = null;

        // Cache singletons once per scene, not once per hex
        if (sharedGame == null) sharedGame = Game.Instance;
        if (sharedBoard == null) sharedBoard = Board.Instance;
        if (sharedNavigator == null) sharedNavigator = FindFirstObjectByType<BoardNavigator>();
        if (sharedIllustrations == null) sharedIllustrations = FindFirstObjectByType<Illustrations>();
        game = sharedGame;
        board = sharedBoard;
        navigator = sharedNavigator;
        illustrations = sharedIllustrations;
        hexTextureMapping = ResolveTextureMapping();
        EnsureSharedParticleTemplates(sharedParticlesPrefab);
        /*if (characterIcon != null)
        {
            characterIconZoom = characterIcon.GetComponent<ZoomSpriteRenderer>();
            if (characterIconZoom != null)
            {
                characterIconZoomDefault = characterIconZoom.zoomFactor;
                characterIconOffsetDefault = characterIconZoom.verticalOffset;
            }
        }*/
        // Fog/particle visuals are NOT refreshed here: Awake fires when a pooled hex is
        // created (possibly thousands per board), before the hex is placed anywhere.
        // Initialize(row, col) runs the fog pass once the hex actually joins the board.
    }

    void Update()
    {
        UpdateCharacterSpriteAlpha();
        if (s_hexInfoActiveHex == this)
        {
            if (!IsMouseOverHexOrPanel() || BoardNavigator.IsPointerOverVisibleUIElement())
                Unhover();
        }
    }

    // Pooled hexes get deactivated/reused rather than destroyed — if this hex owned the shared
    // hover panel when that happens, nothing else would ever hide it or hand it back.
    void OnDisable()
    {
        if (s_hexInfoActiveHex == this)
        {
            board?.UIHover?.Hide();
            s_hexInfoActiveHex = null;
        }
    }

    #region Lazily attached sub-prefabs

    // The hex prefab used to carry ~40 GameObjects (6 ParticleSystems, a 3-TMP hover
    // panel, character visuals with an Animator). Cloning that 4550 times froze
    // scenario loads for minutes, so the heavy parts live in separate prefabs
    // (assigned in the Inspector — see Resources/HexParts for the assets) and are
    // attached per hex only when something actually needs them:
    //   sharedParticlesPrefab — instantiated ONCE per scene as the shared pool templates
    //   characterLayerPrefab  — per hex, when a character shows (sprite/Animator/banner/class icons)
    //   pcTextPrefab          — per hex, when the PC name, armies, or scouted characters show
    //   movementCostPrefab    — per hex, when a movement cost bubble shows
    // The hover info panel is no longer a per-hex sub-prefab: it's a single shared UI element
    // (see HexUIHover, referenced by Board.UIHover) that every hex repositions/refills.

    private static GameObject FindPart(Transform root, string name)
    {
        Transform t = root.Find(name);
        if (t == null) Debug.LogError($"Hex sub-prefab '{root.name}' is missing child '{name}'.");
        return t != null ? t.gameObject : null;
    }

    private static T FindPart<T>(Transform root, string name) where T : Component
    {
        GameObject go = FindPart(root, name);
        return go != null ? go.GetComponent<T>() : null;
    }

    private bool EnsureMessageNoUIText()
    {
        if (messageNoUI != null) return true;
        if (messageNoUITextPrefab == null)
        {
            Debug.LogError("Hex.messageNoUITextPrefab is not assigned in the Inspector; floating message text disabled.");
            return false;
        }

        Transform textRoot = Instantiate(messageNoUITextPrefab, transform, false).transform;
        textRoot.name = "MessageNoUIText";
        messageNoUI = textRoot.GetComponent<TextMeshPro>();
        ApplyCurrentFont(messageNoUI);
        return messageNoUI != null;
    }

    /// <summary>Used by MessageDisplayNoUI for per-hex floating text.</summary>
    public TextMeshPro GetOrCreateMessageText()
    {
        if (messageNoUI == null) EnsureMessageNoUIText();
        return messageNoUI;
    }

    private bool EnsureCharacterLayer()
    {
        if (characterSpriteRenderer != null) return true;
        if (characterLayerPrefab == null)
        {
            Debug.LogError("Hex.characterLayerPrefab is not assigned in the Inspector; character visuals disabled.");
            return false;
        }

        Transform characterBg = Instantiate(characterLayerPrefab, transform, false).transform;
        characterBg.name = "CharacterLayer";

        characterSpriteRenderer = FindPart<SpriteRenderer>(characterBg, "character");
        bannerSpriteRenderer = FindPart<SpriteRenderer>(characterBg, "banner");
        // characterClassesIconGrid = FindPart<SpriteRendererGridLayout>(characterBg, "ClassesSpriteRendererLayout");
        CharacterSpriteHover hover = characterBg.GetComponent<CharacterSpriteHover>();
        if (hover != null) hover.hex = this;   // serialized ref could not cross the prefab split
        characterAnimationController = characterSpriteRenderer != null
            ? characterSpriteRenderer.GetComponent<CharacterAnimationController>()
            : null;

        // Everything starts hidden; each caller applies the state it needs right after.
        if (characterSpriteRenderer != null) SetActiveFast(characterSpriteRenderer.gameObject, false);
        if (bannerSpriteRenderer != null) SetActiveFast(bannerSpriteRenderer.gameObject, false);
        // if (characterClassesIconGrid != null) SetActiveFast(characterClassesIconGrid.gameObject, false);

        return characterSpriteRenderer != null;
    }

    private bool EnsurePcText()
    {
        if (pcName != null) return true;
        if (pcTextPrefab == null)
        {
            Debug.LogError("Hex.pcTextPrefab is not assigned in the Inspector; PC name labels disabled.");
            return false;
        }

        Transform pcTextRoot = Instantiate(pcTextPrefab, transform, false).transform;
        pcTextRoot.name = "PCText";

        // The visible name label lives on the "Label" child (not the root anymore) so it can be
        // shown/hidden independently of the "Band" hover target, a root-level sibling: Band stays
        // hoverable even on hexes with armies/characters but no PC, where Label never shows at all.
        Transform labelTransform = pcTextRoot.Find("Label");
        pcName = labelTransform != null ? labelTransform.GetComponent<TextMeshPro>() : null;
        if (pcName == null)
        {
            Debug.LogError("Hex sub-prefab 'HexPcText' is missing its 'Label' child with a TextMeshPro component.");
            return false;
        }
        ApplyCurrentFont(pcName);

        HexPcTextHover hover = pcTextRoot.GetComponentInChildren<HexPcTextHover>(true);
        if (hover != null) hover.hex = this;   // serialized ref could not cross the prefab split

        // Fit() only auto-runs every frame in the Editor (its Update() is #if UNITY_EDITOR); in a
        // build nothing re-fits the Band when the label text changes, so it must be called
        // explicitly whenever pcName.text is set (see HideHexLabel/RefreshHexLabel) or the Band
        // sprite is left showing its last size/visibility after the text that sized it is gone.
        pcTextBandFit = pcTextRoot.Find("Band")?.GetComponent<SpriteRendererFitToTMP>();

        // Starts hidden; each caller applies the state it needs right after.
        // Band (the hover target) is deliberately left active — see comment above.
        SetActiveFast(pcName.gameObject, false);
        pcTextBandFit?.Fit();
        return true;
    }

    private bool EnsureMovementCost()
    {
        if (movementCostManager != null) return true;
        if (movementCostPrefab == null)
        {
            Debug.LogError("Hex.movementCostPrefab is not assigned in the Inspector; movement cost bubbles disabled.");
            return false;
        }

        movement = Instantiate(movementCostPrefab, transform, false);
        movement.name = "MovementCost";
        movementCostManager = movement.GetComponent<MovementCostManager>();
        if (movementCostManager == null)
        {
            Debug.LogError("Hex sub-prefab 'HexMovementCost' is missing its MovementCostManager component.");
            return false;
        }

        // Starts hidden; ShowMovementLeft applies the state it needs right after.
        SetActiveFast(movement, false);
        return true;
    }

    #endregion

    public bool IsHexRevealed() => isRevealed;
    public bool IsHexSeen() => IsHexRevealed() && !mapOnlyRevealed && !isCurrentlyUnseen;
    public List<Hex> GetHexesInRadius(int radius)
    {
        if (board == null) board = Board.Instance;
        List<Hex> results = new();
        if (board == null) return results;

        results.Add(this);
        if (radius <= 0) return results;

        var queue = new Queue<Vector2Int>(32);
        var visited = new HashSet<Vector2Int>();
        queue.Enqueue(v2);
        visited.Add(v2);

        int currentRadius = 0;
        while (queue.Count > 0 && currentRadius < radius)
        {
            int hexCount = queue.Count;
            for (int i = 0; i < hexCount; i++)
            {
                var currentHex = queue.Dequeue();
                var neighbors = ((currentHex.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                for (int j = 0; j < neighbors.Length; j++)
                {
                    var offset = neighbors[j];
                    var neighborPos = new Vector2Int(currentHex.x + offset.x, currentHex.y + offset.y);
                    if (!visited.Add(neighborPos)) continue;

                    if (board.hexes.TryGetValue(neighborPos, out Hex neighborHex))
                    {
                        results.Add(neighborHex);
                        queue.Enqueue(neighborPos);
                    }
                }
            }
            currentRadius++;
        }

        return results;
    }

    public string GetText()
    {
        if (v2.x < 0 || v2.y < 0) return "";
        return $" at {v2.x}, {v2.y}";
    }

    public PlayableLeader GetPlayer()
    {
        if (leader != null) return leader;
        leader = game.player;
        return leader;
    }

    public bool IsPCRevealed(PlayableLeader overrideLeader = null)
    {
        if(pc == null) return false;
        return pc.IsRevealed(overrideLeader);        
    }

    public bool IsScouted(PlayableLeader overrideLeader = null)
    {
        var l = overrideLeader ? overrideLeader : GetPlayer();
        return l != null && scoutedBy.Contains(l);
    }

    public bool IsScoutedBy(Leader leader)
    {
        return leader != null && scoutedBy.Contains(leader);
    }

    public int GetScoutedTurnsRemaining(Leader leader)
    {
        if (leader == null) return 0;
        return scoutedByTurns.TryGetValue(leader, out int turns) ? turns : 0;
    }

    public bool IsFriendlyPC(PlayableLeader overrideLeader = null)
    {
        var l = overrideLeader ? overrideLeader : GetPlayer();
        if (l == null || pc == null || pc.owner == null) return false;
        if (!IsPCRevealed(l)) return false;
        if(l == pc.owner) return true;
        var a = pc.owner.GetAlignment();
        return a != AlignmentEnum.neutral && a == l.GetAlignment();
    }

    public bool IsFriendlyCharacter(Character character, PlayableLeader overrideLeader = null)
    {
        var l = overrideLeader ? overrideLeader : GetPlayer();
        if (l == character.GetOwner()) return true;
        if (l == null || !character || character.killed) return false;
        var a = character.GetOwner().GetAlignment();
        return a != AlignmentEnum.neutral && a == l.GetAlignment();
    }

    public void Initialize(int row, int col)
    {
        v2 = new Vector2Int(row, col);
        assignedLandRegion = null;
        if (hexRegion != null) hexRegion.enabled = false;
        if (game == null) game = Game.Instance;
        // SpriteRenderer.sortingOrder is effectively signed 16-bit, so keep
        // terrain in (-9999, 0): above the hexRegionFrame underlay (-9999),
        // below every fixed-order hex child. Row decides front-to-back —
        // rows grow downward on screen (row 0 = north), and the visually
        // lower hex must draw over the one behind it so tall art (mountains)
        // occludes correctly; the col parity bit breaks same-row neighbor
        // ties so their overlap is cut deterministically instead of z-fighting.
        int rowsBelow = (board != null ? board.GetHeight() : 200) - 1 - row;
        terrainTexture.sortingOrder = -1 - (rowsBelow * 2) - (col & 1);
        if (pcTexture != null) pcTexture.sortingOrder = terrainTexture.sortingOrder + 1;
        if (terrainTexture != null) terrainTexture.gameObject.SetActive(true);
        ApplyTerrainOverdraw();

        // Placed on the board now — apply the fog/visibility visuals Awake skipped
        // (also covers particles; the minimap variant is currently a no-op).
        UpdateVisibilityForFog();
    }

    public SpriteRenderer GetCharacterSpriteRendererOnHex()
    {
        // Callers use this to animate a character onto the hex, so the layer is needed now.
        EnsureCharacterLayer();
        return characterSpriteRenderer;
    }

    public SpriteRenderer GetArmySpriteRendererOnHex(Character character)
    {
        // Deprecated: army icons are now dynamically instantiated per commander.
        // Returning null disables the legacy army-mover animation in Board.cs.
        return null;
    }


    public SpriteRenderer GetPortSpriteRenderer()
    {
        return null;
    }

    public bool HasPcPort() => pc != null && pc.hasPort;

    public bool ShouldShowWarshipPort()
    {
        return false;
    }

    public bool TryGetKnownCharacterForIcon(out Character known)
    {
        known = null;
        if (board == null) board = Board.Instance;

        PlayableLeader player = GetPlayer();
        bool isSeen = IsHexSeen();
        if (!isSeen) return false;

        bool isScouted = IsScouted(player);
        Character selected = board != null ? board.selectedCharacter : null;

        if (selected != null && selected.hex == this &&
            (isScouted || IsFriendlyCharacter(selected, player) || selected.IsArmyCommander()))
        {
            known = selected;
            return true;
        }

        if (isScouted)
        {
            for (int i = 0, n = characters.Count; i < n; i++)
            {
                Character candidate = characters[i];
                if (candidate == null || candidate.killed || candidate.hex != this) continue;
                known = candidate;
                return true;
            }
        }

        for (int i = 0, n = characters.Count; i < n; i++)
        {
            Character candidate = characters[i];
            if (candidate == null || candidate.killed || candidate.hex != this) continue;
            if (IsFriendlyCharacter(candidate, player))
            {
                known = candidate;
                return true;
            }
        }

        for (int i = 0, n = characters.Count; i < n; i++)
        {
            Character candidate = characters[i];
            if (candidate == null || candidate.killed || candidate.hex != this) continue;
            if (candidate.IsArmyCommander())
            {
                known = candidate;
                return true;
            }
        }

        return false;
    }

    public bool TryGetPreviewTextForCharacter(Character character, out string text)
    {
        text = null;
        if (character == null || character.hex != this) return false;
        PlayableLeader viewer = GetPlayer();
        bool canSee = IsScouted(viewer) || IsFriendlyCharacter(character, viewer);
        bool isSeen = IsHexSeen();
        bool viewerHasCharacter = viewer != null && HasCharacterOfLeader(viewer);
        if (!canSee && !(character.IsArmyCommander() && (viewerHasCharacter || isSeen))) return false;

        // Troop composition used to be appended here as plain text; it now shows as its own
        // card gallery inside SelectedCharacterIcon (see RefreshArmyCardGallery), driven
        // directly off the Character it's given rather than off this string.
        text = character.characterName;
        return true;
    }

    public void SetTerrain(TerrainEnum terrainType, Sprite terrainTexture, Color terrainColor)
    {
        this.terrainType = terrainType;
        if (terrainTexture != null)
        {
            baseTerrainSprite = terrainTexture;
        }
        else
        {
            if (hexTextureMapping == null) hexTextureMapping = ResolveTextureMapping();
            baseTerrainSprite = hexTextureMapping != null ? hexTextureMapping.GetTerrainBaseSprite(terrainType) : null;
        }
        // The Underground-entrance state is read off whichever variant sprite we just assigned.
        isChasm = ChasmTiles.Contains(baseTerrainSprite?.name);
        ApplyHexTextureSprite();
        UpdateUndergroundMarker();
        // this.terrainTexture.color = terrainColor;
        // if(terrainType == TerrainEnum.mountains) this.terrainTexture.sortingOrder += 1000;
    }

    // Armies/characters no longer get their own icon grid — they're appended as inline
    // <sprite> tags on the same HexPcText label used for the PC name (see RefreshHexLabel),
    // so a hex with no PC can still show them. RedrawArmies is called far more broadly than
    // RedrawPC (every army/character move, reveal, etc., often on PC-less hexes), so it owns
    // the general "recompute and show whatever belongs on this hex's label" duty.
    public void RedrawArmies(bool refreshHoverText = true)
    {
        RefreshHexLabel();
        if (refreshHoverText) RefreshHoverText();
    }

    // Force-hides the HexPcText label without evaluating content — used when the whole hex just
    // became unrevealed (fog), where nothing should show regardless of what pc/armies/characters
    // exist underneath.
    private void HideHexLabel()
    {
        if (pcName != null)
        {
            pcName.text = string.Empty;
            SetActiveFast(pcName.gameObject, false);
            pcTextBandFit?.Fit();
        }
    }

    // Rebuilds the shared HexPcText label from current pc/armies/characters state and shows or
    // hides it accordingly. Called by RedrawArmies, RedrawPC, ClearPC, and the reveal/unreveal
    // paths — whichever one fires must recompute the FULL combined text, since any of
    // pc/armies/characters may have changed independently of the others since the last call.
    private void RefreshHexLabel()
    {
        bool seen = IsHexSeen();
        if (!seen) { HideHexLabel(); return; }

        bool shouldShowPc = ShouldShowPcVisual();

        // Armies and scouted characters are each bucketed by alignment (a leaderless army/character
        // counts as neutral) — one sprite per alignment present, followed by its count, rather than
        // repeating a sprite per unit.
        int[] armyCounts = new int[3];
        int[] characterCounts = new int[3];
        PlayableLeader viewer = GetPlayer();
        bool isScouted = IsScouted(viewer);

        for (int i = 0, n = armies.Count; i < n; i++)
        {
            Character commander = armies[i]?.GetCommander();
            if (commander == null) continue;
            AlignmentEnum align = commander.GetOwner() != null ? commander.GetOwner().GetAlignment() : AlignmentEnum.neutral;
            armyCounts[(int)align]++;
        }

        for (int i = 0, n = characters.Count; i < n; i++)
        {
            Character ch = characters[i];
            if (ch == null || ch.killed || ch.hex != this || ch.IsArmyCommander()) continue;

            bool isFriendly = IsFriendlyCharacter(ch, viewer);
            bool canSee = isFriendly || (isScouted && !ch.IsHidden());
            if (!canSee) continue;

            AlignmentEnum align = ch.GetOwner() != null ? ch.GetOwner().GetAlignment() : AlignmentEnum.neutral;
            characterCounts[(int)align]++;
        }

        bool hasArmiesOrCharacters = armyCounts[0] > 0 || armyCounts[1] > 0 || armyCounts[2] > 0
            || characterCounts[0] > 0 || characterCounts[1] > 0 || characterCounts[2] > 0;

        bool hasContent = shouldShowPc || hasArmiesOrCharacters;
            
        if (!hasContent) { HideHexLabel(); return; }
        if (!EnsurePcText()) return;

        StringBuilder builder = new();
        if (shouldShowPc) builder.Append(BuildPcNameLabel());

        if(hasArmiesOrCharacters)
        {
            if(shouldShowPc) builder.Append("<br>");
            void AppendGroup(string spriteName, int count)
            {
                if (count <= 0) return;
                if (builder.Length > 0) builder.Append(' ');
                builder.Append("<sprite name=\"").Append(spriteName).Append("\">").Append(count);
            }

            AppendGroup("freePeople", armyCounts[(int)AlignmentEnum.freePeople]);
            AppendGroup("darkServants", armyCounts[(int)AlignmentEnum.darkServants]);
            AppendGroup("neutral", armyCounts[(int)AlignmentEnum.neutral]);
            AppendGroup("freePeopleCharacter", characterCounts[(int)AlignmentEnum.freePeople]);
            AppendGroup("darkServantsCharacter", characterCounts[(int)AlignmentEnum.darkServants]);
            AppendGroup("neutralCharacter", characterCounts[(int)AlignmentEnum.neutral]);
    
        }
        
        if(!shouldShowPc) builder.Append($" @ {v2.x},{v2.y}");        
        pcName.text = builder.ToString();
        pcName.color = shouldShowPc && pc.owner != null ? pc.owner.nationColor : Color.white;
        SetActiveFast(pcName.gameObject, true);
        pcTextBandFit?.Fit();
    }

    public void RedrawCharacters(bool refreshHoverText = true)
    {
        bool seen = IsHexSeen();
        bool hasCharacter = false;
        for (int i = 0, n = characters.Count; i < n; i++)
        {
            if (characters[i] != null)
            {
                hasCharacter = true;
                break;
            }
        }

        if (seen && hasCharacter) EnsureCharacterLayer();

        // Pre-apply the outline colour before the renderer becomes visible so there
        // is no one-frame flash of the previous (possibly white/cleared) colour.
        Character known = null;
        bool hasKnown = seen && hasCharacter && TryGetKnownCharacterForIcon(out known);
        if (hasKnown)
            GetCharacterAnimationController()?.SetOutlineForCharacter(known);
        else
            GetCharacterAnimationController()?.ClearOutline();

        SetActiveFast(characterSpriteRenderer != null ? characterSpriteRenderer.gameObject : null, seen && hasCharacter);
        if (seen && hasCharacter)
        {
            UpdateCharacterIconSprite();
            // UpdateClassIcons();
        }
        else
        {
            GetCharacterAnimationController()?.Clear();
            // ClearClassIcons();
        }
        UpdateArmyStackVisual(hasKnown ? known : null);
        UpdateBannerSpriteForKnownCharacter();
        if (refreshHoverText) RefreshHoverText();
    }

    // Shows army size as a stack of duplicate silhouettes peeking from behind the main character
    // sprite: 1 copy for a non-commander (or an empty army), up to 5 for a large one. The
    // duplicates don't run their own animation state — CharacterAnimationController mirrors its
    // resolved frame onto them each tick (see stackMirrorRenderers), so the only added GPU cost
    // is a few extra batchable quads, not extra animation logic.
    private void UpdateArmyStackVisual(Character known)
    {
        if (characterSpriteRenderer == null) return;

        bool visible = characterSpriteRenderer.gameObject.activeSelf;
        int extrasNeeded = visible ? Mathf.Clamp(GetArmyStackCount(known) - 1, 0, MaxArmyStackExtraRenderers) : 0;

        for (int i = 0; i < MaxArmyStackExtraRenderers; i++)
        {
            bool active = i < extrasNeeded;
            if (active && i >= armyStackExtraRenderers.Count) armyStackExtraRenderers.Add(CreateArmyStackExtraRenderer());
            if (i >= armyStackExtraRenderers.Count) continue;

            SpriteRenderer extra = armyStackExtraRenderers[i];
            if (extra == null) continue;
            SetActiveFast(extra.gameObject, active);
            if (!active) continue;

            extra.transform.localScale = characterSpriteRenderer.transform.localScale;
            extra.transform.localRotation = characterSpriteRenderer.transform.localRotation;
            extra.transform.localPosition = characterSpriteRenderer.transform.localPosition + (Vector3)(armyStackOffset * (i + 1));
            extra.sortingLayerID = characterSpriteRenderer.sortingLayerID;
            // Stacked behind the main sprite, but never below the terrain art beneath it —
            // terrainTexture/characterSpriteRenderer's own sortingOrder is assigned at runtime
            // (see Hex.ApplyTerrainOverdraw), so this can't just assume fixed prefab numbers.
            int desiredOrder = characterSpriteRenderer.sortingOrder - (i + 1);
            if (hexRegion != null) desiredOrder = Mathf.Max(desiredOrder, hexRegion.sortingOrder + 1);
            extra.sortingOrder = desiredOrder;
            extra.sprite = characterSpriteRenderer.sprite;
        }

        CharacterAnimationController controller = GetCharacterAnimationController();
        if (controller == null) return;
        controller.stackMirrorRenderers.Clear();
        for (int i = 0; i < extrasNeeded; i++)
            controller.stackMirrorRenderers.Add(armyStackExtraRenderers[i]);
    }

    private int GetArmyStackCount(Character character)
    {
        if (character == null || !character.IsArmyCommander()) return 1;

        Army army = character.GetArmy();
        int size = army != null ? army.GetSize() : 0;
        if (size <= 0) return 1;

        int count = 2;
        for (int i = 0; i < armyStackSizeThresholds.Length; i++)
            if (size >= armyStackSizeThresholds[i]) count = i + 3;
        return count;
    }

    private SpriteRenderer CreateArmyStackExtraRenderer()
    {
        GameObject extraObject = new GameObject("ArmyStackExtra");
        extraObject.transform.SetParent(characterSpriteRenderer.transform.parent, false);

        SpriteRenderer extra = extraObject.AddComponent<SpriteRenderer>();
        extra.sharedMaterial = characterSpriteRenderer.sharedMaterial;

        var propertyBlock = new MaterialPropertyBlock();
        propertyBlock.SetFloat(ArmyStackOutlineSizeShaderId, 0f);
        extra.SetPropertyBlock(propertyBlock);

        SetActiveFast(extraObject, false);
        return extra;
    }

    public void RedrawPC(bool refreshHoverText = true)
    {
        if(pc == null) return;
        if (game == null) game = Game.Instance;

        bool seen = IsHexSeen();
        PlayableLeader viewingLeader = game != null ? game.currentlyPlaying : null;
        bool isHuman = viewingLeader != null && game != null && viewingLeader == game.player;

        if (seen) RevealNonPlayableLeadersOnHex(viewingLeader, isHuman);

        ApplyHexTextureSprite();
        RefreshHexLabel();

        if (refreshHoverText) RefreshHoverText();
    }

    public void RefreshVisibilityRendering()
    {
        UpdateVisibilityForFog();
        UpdateMinimapTerrain(IsHexRevealed());
        RedrawArmies(false);
        RedrawCharacters(false);
        RedrawPC(false);
        RefreshHoverText();
    }

    public void RefreshHoverText()
    {
        // A walk redraws the from/to hexes plus the reveal ring every hop, and each redraw
        // lands here — the string build below is the per-hop GC spike. The panel is
        // hover-only, so while a unit is in transit defer the rebuild to Hover(), unless
        // the panel is on screen right now and would go stale.
        if (Board.SuppressHexIconGrids && s_hexInfoActiveHex != this)
        {
            _hoverTextDirty = true;
            return;
        }
        BuildHoverText();
    }

    private void BuildHoverText()
    {
        _hoverTextDirty = false;
        bool seen = IsHexSeen();
        PlayableLeader viewer = GetPlayer();
        bool isScouted = IsScouted(viewer);
        bool viewerHasCharacter = viewer != null && HasCharacterOfLeader(viewer);

        sbChars.Clear();
        bool unkCharsShown = false;

        for (int i = 0, n = characters.Count; i < n; i++)
        {
            var ch = characters[i];
            if (ch == null || ch.killed || ch.hex != this)
            {
                continue;
            }
            bool isFriendly = IsFriendlyCharacter(ch, viewer);
            bool canSeeNonCommander = isFriendly || (isScouted && !ch.IsHidden());
            bool canSeeCommander = canSeeNonCommander || viewerHasCharacter || seen;
            bool canSee = ch.IsArmyCommander() ? canSeeCommander : canSeeNonCommander;

            if (canSee)
            {
                bool canRevealNpl = seen || isScouted;
                if (canRevealNpl && game.IsPlayerCurrentlyPlaying() && ch.GetOwner() is NonPlayableLeader && !(ch.GetOwner() as NonPlayableLeader).IsRevealedToPlayer())
                {
                    NonPlayableLeader npl = ch.GetOwner() as NonPlayableLeader;
                    npl.RevealToPlayer();
                }
                else if (canRevealNpl && ch.GetOwner() is NonPlayableLeader && !(ch.GetOwner() as NonPlayableLeader).IsRevealedToLeader(FindAnyObjectByType<Game>().currentlyPlaying))
                {
                    NonPlayableLeader npl = ch.GetOwner() as NonPlayableLeader;
                    Game g = FindAnyObjectByType<Game>();
                    bool isHuman = g != null && g.currentlyPlaying == g.player;
                    npl.RevealToLeader(g.currentlyPlaying, isHuman);
                }

                Leader charLeader = ch.GetOwner();
                bool isFollower = charLeader != null && charLeader != ch;
                // withHealth=false: no heart-block indicator in the description.
                string charDisplay = ch.GetHoverText(true, true, true, false, false, false);

                sbChars.Append(charDisplay);
                if (isFollower) sbChars.Append(", following ").Append(charLeader.characterName);
                sbChars.Append(" is here");
                if (ch.IsArmyCommander())
                    sbChars.Append(" (leading an army of ").Append(BuildArmyTroopDescription(ch.GetArmy())).Append(')');
                sbChars.Append('\n');
            }
            else
            {
                if (!unkCharsShown) { sbChars.Append(Unknown).Append('\n'); unkCharsShown = true; }
            }
        }

        // Trim trailing newlines and always push an explicit refresh, even when the hex is empty.
        string charText = sbChars.ToString().TrimEnd('\n');

        string terrainText = IsHexRevealed() ? BuildTerrainDescription() : string.Empty;
        string pcText = IsHexRevealed() ? BuildPcDescription() : string.Empty;

        string hoverText = string.Join("\n\n", new[] { terrainText, pcText, charText }.Where(s => !string.IsNullOrEmpty(s)));

        _hoverTextCache = hoverText;
        if (s_hexInfoActiveHex == this) board?.UIHover?.SetText(hoverText);
    }

    // "{amount} {troopName} <sprite troopType>, {amount} {troopName} <sprite troopType>, ..."
    private static string BuildArmyTroopDescription(Army army)
    {
        if (army == null) return string.Empty;

        List<ArmyTroopAbilityGroup> groups = army.GetTroopGroups();
        List<(string troopName, string line)> linkableLines = army.GetLinkableTroopHoverLines();
        var parts = new List<string>(groups.Count);
        for (int i = 0; i < groups.Count && i < linkableLines.Count; i++)
        {
            string spriteName = groups[i].troopType.ToString().ToLowerInvariant();
            parts.Add($"{groups[i].amount} {linkableLines[i].troopName} <sprite name=\"{spriteName}\">");
        }
        return string.Join(", ", parts);
    }

    private string BuildPcDescription()
    {
        PC pcData = GetPC();
        if (pcData == null) return string.Empty;

        return pcData.owner != null
            ? $"The PC of {pcData.pcName} waving the flag of {pcData.owner.characterName} is here"
            : $"The PC of {pcData.pcName} is here";
    }

    private string BuildTerrainDescription()
    {
        string terrainName = TerrainData.GetDisplayName(terrainType);
        StringBuilder sb = new();
        sb.Append(SpriteTag(terrainName)).Append(' ').Append(terrainName);

        // Chasm is the only landmark feature: shown when this tile's art depicts one.
        if (isChasm)
            sb.Append('\n').Append(SpriteTag("Chasm")).Append(" Chasm");

        return sb.ToString();
    }

    // Sprite from environment_terrain_features_spritesheet, looked up by the normalized name
    // (matches CardNameUtility.Normalize, the scheme the sprite m_Name fields use).
    private static string SpriteTag(string name) => $"<sprite name=\"{CardNameUtility.Normalize(name)}\">";


    public void Hover()
    {
        if (BoardNavigator.IsPointerOverVisibleUIElement())
        {
            Unhover();
            return;
        }

        // Any discovered hex can be hovered to read its terrain/features; character & army
        // details inside the panel are still gated on the hex being currently seen/scouted.
        if (!IsHexRevealed())
        {
            Unhover();
            return;
        }

        framesColors?.SetHovered(true);

        if (s_hexInfoActiveHex != null && s_hexInfoActiveHex != this && s_hexInfoActiveHex.IsMouseOverHexOrPanel())
            return;

        if (hexInfoShowCoroutine == null && s_hexInfoActiveHex != this)
        {
            hexInfoShowCoroutine = StartCoroutine(ShowHexInfoAfterDelay());
        }
    }

    // Shows the PC's card alongside its region's Land card while hovering the HexPcText
    // label/Band of a hex that holds a currently-visible PC (see SetPcTextHovered) — mirrors
    // the character/army hover preview, but for hexes. Only appears after
    // pcCardPreviewHoverDelay seconds of uninterrupted hover (see ShowPcCardPreviewAfterDelay)
    // rather than immediately, so glancing across PC hexes while moving the cursor doesn't
    // spam the preview.
    private void TryShowPcCardPreview()
    {
        // A hovered character sprite sits visually on top of the hex and drives its own
        // card preview (see CharacterSpriteHover) — don't let the hex's PC/region cards
        // show underneath/behind it while it's the thing actually under the cursor.
        if (isCharacterHovered) { CancelPcCardPreview(); return; }
        if (!ShouldShowPcVisual()) { CancelPcCardPreview(); return; }
        PC pcData = GetPC();
        if (pcData == null || CardCenterPreview.Instance == null) { CancelPcCardPreview(); return; }

        // Already shown, or already counting down — nothing to do (Hover() can be re-invoked
        // while the cursor sits still, e.g. by other per-frame hover updates).
        if (_showingPcCardPreview || pcCardPreviewCoroutine != null) return;

        pcCardPreviewCoroutine = StartCoroutine(ShowPcCardPreviewAfterDelay(pcData));
    }

    private IEnumerator ShowPcCardPreviewAfterDelay(PC pcData)
    {
        yield return new WaitForSeconds(pcCardPreviewHoverDelay);
        pcCardPreviewCoroutine = null;

        if (_deckManager == null) _deckManager = DeckManager.Instance != null ? DeckManager.Instance : DeckManager.Instance;
        if (_deckManager == null || CardCenterPreview.Instance == null) yield break;

        List<CardData> previewCards = new();
        CardData pcCard = _deckManager.FindPcCardByPcName(pcData.pcName);
        if (pcCard != null) previewCards.Add(pcCard);
        string region = GetLandRegion();
        CardData landCard = !string.IsNullOrWhiteSpace(region) ? _deckManager.FindLandCardByRegion(region) : null;
        if (landCard != null) previewCards.Add(landCard);
        if (previewCards.Count == 0) yield break;

        _showingPcCardPreview = true;
        CardCenterPreview.Instance.ShowPreview(previewCards, hoverDriven: true);
    }

    // Cancels a pending (not-yet-shown) delayed preview and hides one already on screen —
    // covers both cases so every early-out in TryShowPcCardPreview and Unhover() can call
    // just this one method.
    private void CancelPcCardPreview()
    {
        if (pcCardPreviewCoroutine != null) { StopCoroutine(pcCardPreviewCoroutine); pcCardPreviewCoroutine = null; }
        if (!_showingPcCardPreview) return;
        _showingPcCardPreview = false;
        CardCenterPreview.Instance?.HidePreview();
    }

    private IEnumerator ShowHexInfoAfterDelay()
    {
        yield return new WaitForSeconds(hexInfoHoverDelay);
        if (_hoverTextDirty) BuildHoverText();
        bool hasText = !string.IsNullOrWhiteSpace(_hoverTextCache);
        HexUIHover hover = board != null ? board.UIHover : null;
        if (hasText && hover != null)
        {
            s_hexInfoActiveHex = this;
            hover.SetText(_hoverTextCache);
            hover.Show();
        }
        hexInfoShowCoroutine = null;
    }

    public void Unhover()
    {
        if (IsMouseOverHexOrPanel()) return;
        framesColors?.SetHovered(false);
        if (hexInfoShowCoroutine != null) { StopCoroutine(hexInfoShowCoroutine); hexInfoShowCoroutine = null; }
        if (s_hexInfoActiveHex == this)
        {
            board?.UIHover?.Hide();
            s_hexInfoActiveHex = null;
        }
        CancelPcCardPreview();
    }


    private bool IsMouseOverHexOrPanel()
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        // 3D raycast — hex uses BoxCollider (not Collider2D)
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        var hits3d = Physics.RaycastAll(ray);
        for (int i = 0; i < hits3d.Length; i++)
        {
            Transform t = hits3d[i].transform;
            while (t != null)
            {
                if (t == transform) return true;
                t = t.parent;
            }
        }

        HexUIHover hover = board != null ? board.UIHover : null;
        return hover != null && hover.ContainsScreenPoint(Input.mousePosition);
    }

    public void Select(bool lookAt = true, float duration = 1.0f, float delay = 0.0f)
    {
        if (!IsHidden())
        {
            isSelected = true;
            if (lookAt) LookAt(duration, delay);
        }
    }

    public void Unselect()
    {
        isSelected = false;
    }

    public void LookAt(float duration = 1.0f, float delay = 0.0f)
    {
        if (!game.IsHumanActivelyActing()) return;
        if (!IsHexSeen()) return;
        // Avoid GameObject.Find/string allocs; use our own transform
        if (navigator == null) navigator = FindFirstObjectByType<BoardNavigator>();
        if (navigator != null) navigator.LookAt(transform.position, duration, delay);
    }

    public bool HasCharacter(Character c) => characters.Contains(c);

    public bool HasPcOfLeader(Leader c)
    {
        if (pc == null || pc.citySize == PCSizeEnum.NONE) return false;
        return pc.owner == c;
    }

    public bool HasArmyOfLeader(Leader c)
    {
        for (int i = 0, n = armies.Count; i < n; i++)
        {
            var cmd = armies[i].GetCommander();
            if (cmd == c || cmd.GetOwner() == c) return true;
        }
        return false;
    }

    public bool HasCharacterOfLeader(Leader c)
    {
        for (int i = 0, n = characters.Count; i < n; i++)
        {
            var ch = characters[i];
            if (ch == c || ch.GetOwner() == c) return true;
        }
        return false;
    }

    public bool LeaderSeesHex(Leader c) => HasArmyOfLeader(c) || HasPcOfLeader(c) || HasCharacterOfLeader(c);

    public void Reveal(Leader scoutedByPlayer = null)
    {
        RevealInternal(scoutedByPlayer, game.IsPlayerCurrentlyPlaying());
    }

    public void RevealPC()
    {
        if (pc == null || pc.IsRevealed()) return;
        pc.Reveal();
    }

    public void Unreveal(Leader unrevealedPlayer = null)
    {
        if (unrevealedPlayer)
        {
            AlignmentEnum unreleavedPlayerAlignment = unrevealedPlayer.GetAlignment();
            List<Leader> toRemove = scoutedBy
                .Where(ch => ch != null && ch.GetAlignment() != unreleavedPlayerAlignment)
                .ToList();
            for (int i = 0; i < toRemove.Count; i++)
            {
                Leader leader = toRemove[i];
                scoutedBy.Remove(leader);
                scoutedByTurns.Remove(leader);
            }
            RebuildScoutingCache();
        }
        isRevealed = true;
        if (!mapOnlyRevealed && game.currentlyPlaying == game.player && characters.Find(x => x.GetOwner() == game.player) == null)
        {
            if (IsHexRevealed()) isCurrentlyUnseen = true;
        }
        UpdateMinimapTerrain(IsHexRevealed());
        
        RedrawArmies(false);
        RedrawCharacters(false);
        RedrawPC(false);
        RefreshHoverText();
    }

    public void Obscure(Leader obscuredBy = null)
    {
        Unreveal(obscuredBy);
        isRevealed = false;
        mapOnlyRevealed = false;
        isCurrentlyUnseen = false;
        UpdateVisibilityForFog();
        UpdateMinimapTerrain(IsHexRevealed());
        RedrawArmies(false);
        RedrawCharacters(false);
        RedrawPC(false);
        RefreshHoverText();
    }

    public void RevealArea(int radius = 1, bool lookAt = true, Leader scoutedByPlayer = null)
    {
        if (board == null) board = Board.Instance;

        bool isPlayerTurn = game.IsPlayerCurrentlyPlaying();
        RevealInternal(scoutedByPlayer, isPlayerTurn);
        if (radius <= 0 || board == null) { if (isPlayerTurn && lookAt) LookAt(); return; }

        var queue = areaQueue;
        var visited = areaVisited;
        queue.Clear();
        visited.Clear();
        queue.Enqueue(v2);
        visited.Add(v2);

        int currentRadius = 0;
        while (queue.Count > 0 && currentRadius < radius)
        {
            int hexCount = queue.Count;
            for (int i = 0; i < hexCount; i++)
            {
                var currentHex = queue.Dequeue();
                var neighbors = ((currentHex.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                for (int j = 0; j < neighbors.Length; j++)
                {
                    var offset = neighbors[j];
                    var neighborPos = new Vector2Int(currentHex.x + offset.x, currentHex.y + offset.y);
                    if (!visited.Add(neighborPos)) continue;

                    if (board.hexes.TryGetValue(neighborPos, out Hex neighborHex))
                    {
                        neighborHex.RevealInternal(scoutedByPlayer, isPlayerTurn);
                        queue.Enqueue(neighborPos);
                    }
                }
            }
            currentRadius++;
        }
        
        if(game.IsPlayerCurrentlyPlaying()) {
            if (lookAt) LookAt();
            MinimapManager.RefreshMinimap();
        }
    }

    public void RevealMapOnlyArea(int radius = 1, bool lookAt = true, bool refreshMinimap = true)
    {
        if (board == null) board = Board.Instance;

        RevealMapOnlyInternal();
        if (radius <= 0 || board == null)
        {
            if (game.IsPlayerCurrentlyPlaying() && lookAt) LookAt();
            return;
        }

        var queue = areaQueue;
        var visited = areaVisited;
        queue.Clear();
        visited.Clear();
        queue.Enqueue(v2);
        visited.Add(v2);

        int currentRadius = 0;
        while (queue.Count > 0 && currentRadius < radius)
        {
            int hexCount = queue.Count;
            for (int i = 0; i < hexCount; i++)
            {
                var currentHex = queue.Dequeue();
                var neighbors = ((currentHex.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                for (int j = 0; j < neighbors.Length; j++)
                {
                    var offset = neighbors[j];
                    var neighborPos = new Vector2Int(currentHex.x + offset.x, currentHex.y + offset.y);
                    if (!visited.Add(neighborPos)) continue;

                    if (board.hexes.TryGetValue(neighborPos, out Hex neighborHex))
                    {
                        neighborHex.RevealMapOnlyInternal();
                        queue.Enqueue(neighborPos);
                    }
                }
            }
            currentRadius++;
        }

        if (game.IsPlayerCurrentlyPlaying())
        {
            if (lookAt) LookAt();
            if (refreshMinimap) MinimapManager.RefreshMinimap();
        }
    }

    private void RevealNonPlayableLeadersOnHex(PlayableLeader leader, bool showPopup)
    {
        if (leader == null) return;
        if (game == null) game = Game.Instance;

        if (!IsHexSeen()) return;
        bool isScouted = IsScouted(leader);
        NonPlayableLeader npl = null;

        if (pc != null && pc.owner is NonPlayableLeader pcOwner && pc.IsRevealed(leader) && !pcOwner.IsRevealedToLeader(leader))
        {
            npl = pcOwner;
        }

        if (npl == null && characters != null)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                Character ch = characters[i];
                if (ch == null) continue;
                if (ch.GetOwner() is not NonPlayableLeader owner) continue;
                if (owner.IsRevealedToLeader(leader)) continue;
                bool canReveal = !ch.IsHidden() && (ch.IsArmyCommander() || isScouted);
                if (!canReveal) continue;
                npl = owner;
                break;
            }
        }

        if (npl != null)
        {
            bool shouldPopup = showPopup && leader == game.player && game.currentlyPlaying == leader;
            npl.RevealToLeader(leader, shouldPopup);
            return;
        }

        if (showPopup && leader == game.player && game.currentlyPlaying == leader)
        {
            NonPlayableLeader pending = null;
            if (pc != null && pc.owner is NonPlayableLeader pendingOwner && pc.IsRevealed(leader) && pendingOwner.ShouldShowPlayerRevealPopup())
            {
                pending = pendingOwner;
            }
            if (pending == null && characters != null)
            {
                for (int i = 0; i < characters.Count; i++)
                {
                    Character ch = characters[i];
                    if (ch == null) continue;
                    if (ch.GetOwner() is not NonPlayableLeader owner) continue;
                    if (!owner.ShouldShowPlayerRevealPopup()) continue;
                    bool canReveal = !ch.IsHidden() && (ch.IsArmyCommander() || isScouted);
                    if (!canReveal) continue;
                    pending = owner;
                    break;
                }
            }
            if (pending != null)
            {
                pending.RevealToLeader(leader, true);
            }
        }
    }

    private void SetHexSpriteAlpha(float alpha)
    {
        SetSpriteAlpha(terrainTexture, alpha);
        SetSpriteAlpha(pcTexture, alpha);
    }

    private static void SetSpriteAlpha(SpriteRenderer sr, float alpha)
    {
        if (!sr) return;
        var c = sr.color;
        c.a = alpha;
        sr.color = c;
    }

    private void UpdateMinimapTerrain(bool revealed)
    {
        /* Terrain or None logic commented out — using hexRegion only.
        if (!terrainOrNoneMinimapTexture) return;
        terrainOrNoneMinimapTexture.sprite = terrainTexture ? terrainTexture.sprite : null;
        if (revealed)
        {
            SetSpriteAlpha(terrainOrNoneMinimapTexture, isCurrentlyUnseen ? 0.1f : 1f);
        }
        else
        {
            SetSpriteAlpha(terrainOrNoneMinimapTexture, 0f);
        }
        */
    }

    private void UpdateVisibilityForFog()
    {
        bool revealed = IsHexRevealed();
        bool seen = IsHexSeen();
        ApplyRegionColor();
        if (terrainTexture != null)
        {
            SetActiveFast(terrainTexture.gameObject, revealed);
            if (revealed != seamlessRevealedLast)
            {
                seamlessRevealedLast = revealed;
                HexSeamlessTerrain.MarkDirty(this);
            }
        }
        UpdateTerrainVisualAlpha();
        UpdateUndergroundMarker();
        if (revealed)
        {
            UpdateArtifactVisibility();
            UpdateEncounterVisibility();
            UpdateParticles();
            RefreshFrontierRowVisuals();
            RefreshHexLabel();
            return;
        }

        SetActiveFast(characterSpriteRenderer != null ? characterSpriteRenderer.gameObject : null, false);
        SetActiveFast(artifact, false);
        if (artifactHover) SetActiveFast(artifactHover.gameObject, false);
        SetActiveFast(encounter, false);
        if (encounterHover) SetActiveFast(encounterHover.gameObject, false);
        if (artifactBg) SetActiveFast(artifactBg, false);

        SetActiveFast(movement, false);
        if (sharedSelectedParticlesOwner == this)
        {
            SetSharedSelectedParticlesActive(false);
        }
        StopSharedOneShotParticlesOnThisHex();
        if (framesColors != null)
        {
            framesColors.SetScouted(false);
            framesColors.SetDarkness(false);
        }

        HideHexLabel();

        UpdateParticles();
        RefreshFrontierRowVisuals();
    }

    private bool ShouldShowPcPort()
    {
        if (pc == null || !pc.hasPort) return false;
        bool seen = IsHexSeen();
        if (!seen) return false;
        if (game == null) game = Game.Instance;

        PlayableLeader viewingLeader = game != null ? game.currentlyPlaying : null;
        bool ownerIsNonPlayableLeader = pc.owner is NonPlayableLeader;
        bool nplKnownByViewer = ownerIsNonPlayableLeader && viewingLeader != null && (pc.owner as NonPlayableLeader).IsRevealedToLeader(viewingLeader);
        bool pcRevealed = IsPCRevealed();
        return pcRevealed || (ownerIsNonPlayableLeader && nplKnownByViewer);
    }

    private void UpdatePortIcon(bool? shouldShowPcOverride = null)
    {
        return;
    }

    private void UpdateCharacterIconSprite()
    {
        if (characterSpriteRenderer == null) return;

        CharacterAnimationController animationController = GetCharacterAnimationController();

        if (TryGetKnownCharacterForIcon(out Character known))
        {
            if (animationController != null && animationController.Show(known))
            {
                // The animator now drives the sprite renderer's sprite each frame.
                animationController.SetOutlineForCharacter(known);
                return;
            }

            Sprite sprite = null;
            if (illustrations != null)
            {
                // A leader variant (e.g. "Strider") often has no static illustration of its own
                // and previously fell straight through to knownBiome.characterSprite/race,
                // skipping the base leader's art entirely — GetIllustrationByName(Character)
                // already tries illustrationName, characterName, then SpriteVariantBaseName in
                // that order, so try it first.
                sprite = illustrations.GetIllustrationByName(known);

                if (sprite == null)
                {
                    Leader knownAsLeader = known as Leader;
                    LeaderBiomeConfig knownBiome = knownAsLeader != null ? knownAsLeader.GetBiome() : null;
                    string spriteName = knownBiome?.characterSprite;
                    if (!string.IsNullOrEmpty(spriteName))
                        sprite = illustrations.GetIllustrationByName(spriteName, false);
                }
                if (sprite == null)
                    sprite = illustrations.GetIllustrationByName(known.race.ToString(), false);
            }
            characterSpriteRenderer.sprite = sprite;
            animationController?.SetOutlineForCharacter(known);
        }
        else
        {
            animationController?.Clear();
            characterSpriteRenderer.sprite = null;
            animationController?.ClearOutline();
        }
    }

    public CharacterAnimationController GetCharacterAnimationController()
    {
        if (characterAnimationController == null && characterSpriteRenderer != null)
        {
            characterAnimationController = characterSpriteRenderer.GetComponent<CharacterAnimationController>();
            if (characterAnimationController == null)
                characterAnimationController = characterSpriteRenderer.gameObject.AddComponent<CharacterAnimationController>();
        }
        return characterAnimationController;
    }

    public void PlayCharacterActionAnimation(Character character)
    {
        if (character == null) return;
        if (characterSpriteRenderer == null || !characterSpriteRenderer.gameObject.activeInHierarchy) return;
        GetCharacterAnimationController()?.PlayAction(character);
    }

    /*private void UpdateClassIcons()
    {
        ClearClassIcons();

        // Suppressed during movement; ClearClassIcons already hid the grid, so just bail.
        if (Board.SuppressHexIconGrids) { _iconGridsPendingRebuild = true; return; }

        if (characterClassesIconGrid == null || spriteRendererLayoutIcon == null) return;
        if (!TryGetKnownCharacterForIcon(out Character known)) return;

        void AddClassIcon(string className, int level)
        {
            if (level <= 0) return;
            GameObject icon = Instantiate(spriteRendererLayoutIcon, characterClassesIconGrid.transform);
            SpriteRendererIconManager manager = icon.GetComponent<SpriteRendererIconManager>();
            if (manager != null)
            {
                Sprite sprite = illustrations != null ? illustrations.GetIllustrationByName(className, false) : null;
                if (manager.armySprite != null) manager.armySprite.sprite = sprite;
                if (manager.nationText != null) manager.nationText.text = level.ToString();
            }
        }

        AddClassIcon("commander", known.GetCommander());
        AddClassIcon("agent", known.GetAgent());
        AddClassIcon("emmissary", known.GetEmmissary());
        AddClassIcon("mage", known.GetMage());

        if (classArrangeCoroutine != null) StopCoroutine(classArrangeCoroutine);
        classArrangeCoroutine = StartCoroutine(DelayedArrangeClasses());

        // Content is always (re)built here, but it only stays hidden/shown by hover state —
        // see SetCharacterHovered, which CharacterSpriteHover drives on OnMouseEnter/Exit.
        SetActiveFast(characterClassesIconGrid.gameObject, isCharacterHovered);
    }*/

    /*private void ClearClassIcons()
    {
        if (characterClassesIconGrid == null) return;
        if (classArrangeCoroutine != null)
        {
            StopCoroutine(classArrangeCoroutine);
            classArrangeCoroutine = null;
        }
        Transform gridTransform = characterClassesIconGrid.transform;
        for (int i = gridTransform.childCount - 1; i >= 0; i--)
        {
            Destroy(gridTransform.GetChild(i).gameObject);
        }
        SetActiveFast(characterClassesIconGrid.gameObject, false);
    }*/

    /*private IEnumerator DelayedArrangeClasses()
    {
        yield return null;
        if (characterClassesIconGrid != null) characterClassesIconGrid.Arrange();
        classArrangeCoroutine = null;
    }*/

    private void UpdateBannerSpriteForKnownCharacter()
    {
        if (bannerSpriteRenderer == null)
        {
            return;
        }

        if (!IsHexSeen())
        {
            ClearBannerSprite();
            return;
        }

        if (!TryGetKnownArmyCommanderForBanner(out Character known))
        {
            ClearBannerSprite();
            return;
        }

        UpdateBannerSprite(known);
    }

    // Same "known" resolution as TryGetKnownCharacterForIcon, but a banner is only ever worth
    // showing for a character who actually commands an army — narrower than icon visibility.
    private bool TryGetKnownArmyCommanderForBanner(out Character known)
    {
        known = null;
        if (TryGetKnownCharacterForIcon(out Character iconCharacter) && iconCharacter.IsArmyCommander())
        {
            known = iconCharacter;
            return true;
        }

        if (board == null) board = Board.Instance;

        PlayableLeader player = GetPlayer();
        bool isScouted = IsScouted(player);
        Character selected = board != null ? board.selectedCharacter : null;

        if (selected != null && selected.hex == this && selected.IsArmyCommander() &&
            (isScouted || IsFriendlyCharacter(selected, player) || selected.GetOwner() == player))
        {
            known = selected;
            return true;
        }

        for (int i = 0, n = characters.Count; i < n; i++)
        {
            Character candidate = characters[i];
            if (candidate == null || candidate.killed || candidate.hex != this || !candidate.IsArmyCommander()) continue;
            if (IsFriendlyCharacter(candidate, player) || candidate.GetOwner() == player)
            {
                known = candidate;
                return true;
            }
            if (isScouted && candidate.GetOwner() != null)
            {
                known = candidate;
                return true;
            }
        }

        return false;
    }

    private void UpdateBannerSprite(Character character)
    {
        if (bannerSpriteRenderer == null) return;

        if (character == null)
        {
            ClearBannerSprite();
            return;
        }

        Leader owner = character.GetOwner();
        string bannerName = ResolveBannerName(owner);
        if (string.IsNullOrWhiteSpace(bannerName))
        {
            ClearBannerSprite();
            return;
        }

        if (illustrations == null) illustrations = FindFirstObjectByType<Illustrations>();
        if (illustrations == null)
        {
            ClearBannerSprite();
            return;
        }

        if (!illustrations.IsLoaded)
        {
            QueueBannerRetry();
            ClearBannerSprite();
            return;
        }

        Sprite ownerBannerSprite = illustrations != null ? illustrations.GetIllustrationByName(bannerName, false) : null;
        if (ownerBannerSprite == null)
        {
            ClearBannerSprite();
            return;
        }

        if (bannerSpriteRenderer.sprite != ownerBannerSprite)
        {
            bannerSpriteRenderer.sprite = ownerBannerSprite;
        }
        SetActiveFast(bannerSpriteRenderer.gameObject, true);
        CancelBannerRetry();
    }

    private void ClearBannerSprite()
    {
        if (bannerSpriteRenderer == null)
        {
            return;
        }

        SetActiveFast(bannerSpriteRenderer.gameObject, false);
    }

    private void QueueBannerRetry()
    {
        if (bannerRetryCoroutine != null)
        {
            return;
        }

        bannerRetryCoroutine = StartCoroutine(RetryBannerWhenIllustrationsReady());
    }

    private void CancelBannerRetry()
    {
        if (bannerRetryCoroutine == null)
        {
            return;
        }

        StopCoroutine(bannerRetryCoroutine);
        bannerRetryCoroutine = null;
    }

    private IEnumerator RetryBannerWhenIllustrationsReady()
    {
        while (illustrations == null || !illustrations.IsLoaded)
        {
            if (illustrations == null)
            {
                illustrations = FindFirstObjectByType<Illustrations>();
            }
            yield return null;
        }

        bannerRetryCoroutine = null;

        if (!this || !gameObject.activeInHierarchy)
        {
            yield break;
        }

        RedrawCharacters(false);
    }

    private static string ResolveBannerName(Leader owner)
    {
        if (owner == null)
        {
            return null;
        }

        LeaderBiomeConfig biome = owner.GetBiome();
        if (biome == null)
        {
            return null;
        }

        if (owner is PlayableLeader playableLeader)
        {
            string selectedSubdeckId = playableLeader.GetSelectedSubdeckId();
            if (!string.IsNullOrWhiteSpace(selectedSubdeckId) && biome.variants != null)
            {
                LeaderVariantConfig variant = biome.variants.Find(entry =>
                    entry != null
                    && ((!string.IsNullOrWhiteSpace(entry.variantId) && string.Equals(entry.variantId, selectedSubdeckId, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(entry.subdeckId) && string.Equals(entry.subdeckId, selectedSubdeckId, StringComparison.OrdinalIgnoreCase))));

                if (!string.IsNullOrWhiteSpace(variant?.banner))
                {
                    return variant.banner;
                }
            }
        }

        return biome.banner;
    }

    private static readonly Dictionary<Sprite, Color> dominantColorCache = new();

    /*private static bool TryGetDominantBannerColor(Sprite bannerSprite, out Color dominantColor)
    {
        dominantColor = Color.white;
        if (bannerSprite == null)
        {
            return false;
        }

        if (dominantColorCache.TryGetValue(bannerSprite, out Color cachedColor))
        {
            dominantColor = cachedColor;
            return true;
        }

        if (bannerSprite.texture == null)
        {
            return false;
        }

        try
        {
            Texture2D texture = bannerSprite.texture;
            Rect rect = bannerSprite.textureRect;
            int startX = Mathf.RoundToInt(rect.x);
            int startY = Mathf.RoundToInt(rect.y);
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            Color[] pixels = texture.GetPixels(startX, startY, width, height);
            Dictionary<int, (Vector3 sum, int count)> buckets = new();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                if (pixel.a < 0.2f) continue;

                Color.RGBToHSV(pixel, out float hue, out float saturation, out float value);
                if (saturation < 0.3f) continue;
                if (value < 0.2f || value > 0.95f) continue;

                int hueBucket = Mathf.Clamp(Mathf.FloorToInt(hue * 12f), 0, 11);
                int satBucket = Mathf.Clamp(Mathf.FloorToInt(saturation * 4f), 0, 3);
                int valBucket = Mathf.Clamp(Mathf.FloorToInt(value * 4f), 0, 3);
                int key = hueBucket | (satBucket << 8) | (valBucket << 16);

                if (buckets.TryGetValue(key, out var bucket))
                {
                    bucket.sum += new Vector3(pixel.r, pixel.g, pixel.b);
                    bucket.count++;
                    buckets[key] = bucket;
                }
                else
                {
                    buckets[key] = (new Vector3(pixel.r, pixel.g, pixel.b), 1);
                }
            }

            if (buckets.Count == 0)
            {
                dominantColorCache[bannerSprite] = Color.white;
                return false;
            }

            KeyValuePair<int, (Vector3 sum, int count)> bestBucket = buckets.OrderByDescending(entry => entry.Value.count).First();
            Vector3 average = bestBucket.Value.sum / Mathf.Max(1, bestBucket.Value.count);
            dominantColor = new Color(average.x, average.y, average.z, 1f);
            dominantColorCache[bannerSprite] = dominantColor;
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }*/


    /*private void UpdateCharacterIconZoom(Sprite sprite)
    {
        if (characterIconZoom == null && characterIcon != null)
        {
            characterIconZoom = characterIcon.GetComponent<ZoomSpriteRenderer>();
            if (characterIconZoom != null)
            {
                characterIconZoomDefault = characterIconZoom.zoomFactor;
                characterIconOffsetDefault = characterIconZoom.verticalOffset;
            }
        }
        if (characterIconZoom == null) return;

        bool useZoom = sprite != null && sprite != defaultCharacterSprite;
        if (useZoom)
        {
            characterIconZoom.zoomFactor = characterIconZoomDefault;
            characterIconZoom.verticalOffset = characterIconOffsetDefault;
        }
        else
        {
            characterIconZoom.zoomFactor = 1f;
            characterIconZoom.verticalOffset = 0f;
        }
        characterIconZoom.Refresh();
        characterIconZoom.enabled = useZoom;
    }
*/
    private void RevealInternal(Leader scoutedByPlayer, bool isPlayerTurn)
    {
        bool wasSeen = IsHexSeen();
        // Already fully seen and no scouting to record: every state write below is a no-op
        // and the redraws repaint identical visuals. This runs on 7 hexes per movement hop
        // (RevealArea) and on every visible hex during whole-board refreshes, so bail early.
        // Exception: if a walk cleared this hex's icon grids (SuppressHexIconGrids), the
        // redraws below are what repopulates them — don't skip until that has happened.
        if (wasSeen && scoutedByPlayer == null && !_iconGridsPendingRebuild) return;

        var g = game ?? Game.Instance;
        // isRevealed (and everything it drives below: fog/minimap/redraws) is a single,
        // global flag — there is no per-leader visibility state, only this one shared render
        // of the board on the human's screen. An action attributed to an AI leader
        // (scoutedByPlayer set, but not the human) must still record that leader's own
        // knowledge below, but must NOT flip what's rendered — otherwise any AI character's
        // scout/spell/patrol action during another leader's turn flashed newly-discovered
        // terrain onto the human's screen mid-AI-turn.
        bool revealsToHuman = scoutedByPlayer == null || g == null || scoutedByPlayer == g.player;

        if (scoutedByPlayer)
        {
            scoutedByTurns[scoutedByPlayer] = Math.Max(2, scoutedByTurns.TryGetValue(scoutedByPlayer, out int current) ? current : 0);
            scoutedBy.Add(scoutedByPlayer);
        }

        if (!revealsToHuman) return;

        isRevealed = true;
        mapOnlyRevealed = false;
        if (isPlayerTurn) isCurrentlyUnseen = false;
        UpdateVisibilityForFog();
        UpdateMinimapTerrain(IsHexRevealed());
        PlayableLeader viewer = scoutedByPlayer as PlayableLeader;
        if (viewer == null && game != null) viewer = game.currentlyPlaying;
        bool showPopup = viewer != null && g != null && viewer == g.player && isPlayerTurn;
        RevealNonPlayableLeadersOnHex(viewer, showPopup);
        // Cleared before the redraws: if grids are still suppressed (mid-walk) the
        // redraws re-set the flag, keeping this hex eligible for the post-walk rebuild.
        _iconGridsPendingRebuild = false;
        RedrawArmies(false);
        RedrawCharacters(false);
        RedrawPC(false);
        RefreshHoverText();
        if (!wasSeen)
        {
            PlayRevealPulse();
            PlayPcRevealPulse();
        }
    }

    private void RevealMapOnlyInternal()
    {
        bool wasSeen = IsHexSeen();
        if (wasSeen)
        {
            return;
        }

        isRevealed = true;
        mapOnlyRevealed = true;
        isCurrentlyUnseen = !(game != null && game.player != null && game.player.LeaderSeesHex(this));
        UpdateVisibilityForFog();
        UpdateMinimapTerrain(IsHexRevealed());
        RedrawArmies(false);
        RedrawCharacters(false);
        RedrawPC(false);
        RefreshHoverText();
        PlayRevealPulse();
        PlayPcRevealPulse();
    }

    private void PlayRevealPulse()
    {
        if (terrainTexture == null) return;

        if (revealPulseCoroutine != null)
        {
            StopCoroutine(revealPulseCoroutine);
        }

        if (hexRegion != null) hexRegion.enabled = false;
        revealPulseCoroutine = StartCoroutine(AnimateRevealPulse());
    }

    private IEnumerator AnimateRevealPulse()
    {
        if (terrainTexture == null)
        {
            revealPulseCoroutine = null;
            yield break;
        }

        Transform terrainTransform = terrainTexture.transform;
        if (!terrainBaseScaleCaptured)
        {
            // capture the prefab-defined scale before the first pulse touches
            // it, so interrupted pulses can't bake a mid-animation value
            terrainBaseScale = terrainTransform.localScale;
            terrainBaseScaleCaptured = true;
        }
        Vector3 endScale = terrainBaseScale;
        float scaleEffect = UnityEngine.Random.Range(0.3f, 0.9f);
        Vector3 startScale = new(endScale.x * scaleEffect, endScale.y * scaleEffect, endScale.z);

        terrainTransform.localScale = startScale;
        yield return null;

        float elapsed = 0f;
        while (elapsed < revealDuration)
        {
            if (terrainTexture == null)
            {
                revealPulseCoroutine = null;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / revealDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            terrainTransform.localScale = Vector3.Lerp(startScale, endScale, eased);
            yield return null;
        }

        if (terrainTexture != null)
        {
            terrainTransform.localScale = endScale;
        }

        revealPulseCoroutine = null;
        ApplyRegionColor();
    }

    // Distinct from the terrain's reveal pulse above: a Population Center is the important
    // discovery on this hex, so it gets its own showier "landmark found" beat — magic
    // sparkles play alone for a beat, THEN the PC icon paints in under them, so the
    // sparkles read as the thing revealing it rather than just decorating a pop-in.
    private const float PcRevealSparkleDelay = 2.5f;
    private const float PcRevealFadeDuration = 0.6f;

    private void PlayPcRevealPulse()
    {
        if (pc == null || pcTexture == null || !pcTexture.gameObject.activeInHierarchy) return;

        if (pcRevealPulseCoroutine != null) StopCoroutine(pcRevealPulseCoroutine);
        pcRevealPulseCoroutine = StartCoroutine(AnimatePcRevealPulse());
    }

    private IEnumerator AnimatePcRevealPulse()
    {
        if (pcTexture == null)
        {
            pcRevealPulseCoroutine = null;
            yield break;
        }

        float endAlpha = pcTexture.color.a;
        SetSpriteAlpha(pcTexture, 0f);

        if (ShouldShowPlayerParticles()) PlaySharedOneShotParticles(SharedParticleType.PcReveal);

        float delay = 0f;
        while (delay < PcRevealSparkleDelay)
        {
            if (pcTexture == null) { pcRevealPulseCoroutine = null; yield break; }
            delay += Time.unscaledDeltaTime;
            yield return null;
        }

        float elapsed = 0f;
        while (elapsed < PcRevealFadeDuration)
        {
            if (pcTexture == null) { pcRevealPulseCoroutine = null; yield break; }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / PcRevealFadeDuration);
            SetSpriteAlpha(pcTexture, Mathf.SmoothStep(0f, endAlpha, t));
            yield return null;
        }

        if (pcTexture != null) SetSpriteAlpha(pcTexture, endAlpha);

        pcRevealPulseCoroutine = null;
    }

    public void ClearScouting()
    {
        if (scoutedByTurns.Count == 0 && anchoredWarshipsTotal == 0 && persistentScoutedBy.Count == 0)
        {
            if (darknessTurnsRemaining > 0)
            {
                darknessTurnsRemaining--;
                UpdateParticles();
            }
            return;
        }
        if (scoutedByTurns.Count > 0)
        {
            List<Leader> leaders = scoutedByTurns.Keys.ToList();
            for (int i = 0; i < leaders.Count; i++)
            {
                Leader leader = leaders[i];
                scoutedByTurns[leader] = scoutedByTurns[leader] - 1;
                if (scoutedByTurns[leader] <= 0) scoutedByTurns.Remove(leader);
            }
        }
        RebuildScoutingCache();
        if (darknessTurnsRemaining > 0) darknessTurnsRemaining--;
        UpdateParticles();
        RefreshHoverText();
    }

    public void ClearScoutingAll()
    {
        if (scoutedByTurns.Count == 0 && scoutedBy.Count == 0) return;
        scoutedByTurns.Clear();
        scoutedBy.Clear();
        RebuildScoutingCache();
        RefreshHoverText();
    }

    public void UnrevealArea(int radius = 1, bool lookAt = true, Leader unrevealedBy = null)
    {
        if (board == null) board = Board.Instance;

        Unreveal(unrevealedBy);
        if (radius <= 0 || board == null) { if (lookAt) LookAt(); return; }

        var queue = new Queue<Vector2Int>(32);
        var visited = new HashSet<Vector2Int>();
        queue.Enqueue(v2);
        visited.Add(v2);

        int currentRadius = 0;
        while (queue.Count > 0 && currentRadius < radius)
        {
            int hexCount = queue.Count;
            for (int i = 0; i < hexCount; i++)
            {
                var currentHex = queue.Dequeue();
                var neighbors = ((currentHex.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                for (int j = 0; j < neighbors.Length; j++)
                {
                    var offset = neighbors[j];
                    var neighborPos = new Vector2Int(currentHex.x + offset.x, currentHex.y + offset.y);
                    if (!visited.Add(neighborPos)) continue;

                    if (board.hexes.TryGetValue(neighborPos, out Hex neighborHex))
                    {
                        neighborHex.Unreveal(unrevealedBy);
                        queue.Enqueue(neighborPos);
                    }
                }
            }
            currentRadius++;
        }
    }

    public void ObscureArea(int radius = 1, bool lookAt = true, Leader obscuredBy = null)
    {
        if (board == null) board = Board.Instance;

        Obscure(obscuredBy);
        if (radius <= 0 || board == null) { if (lookAt) LookAt(); return; }

        var queue = new Queue<Vector2Int>(32);
        var visited = new HashSet<Vector2Int>();
        queue.Enqueue(v2);
        visited.Add(v2);

        int currentRadius = 0;
        while (queue.Count > 0 && currentRadius < radius)
        {
            int hexCount = queue.Count;
            for (int i = 0; i < hexCount; i++)
            {
                var currentHex = queue.Dequeue();
                var neighbors = ((currentHex.x & 1) == 0) ? board.evenRowNeighbors : board.oddRowNeighbors;

                for (int j = 0; j < neighbors.Length; j++)
                {
                    var offset = neighbors[j];
                    var neighborPos = new Vector2Int(currentHex.x + offset.x, currentHex.y + offset.y);
                    if (!visited.Add(neighborPos)) continue;

                    if (board.hexes.TryGetValue(neighborPos, out Hex neighborHex))
                    {
                        neighborHex.Obscure(obscuredBy);
                        queue.Enqueue(neighborPos);
                    }
                }
            }
            currentRadius++;
        }
    }

    public void Hide()
    {
        bool shouldBeUnseen = IsHexRevealed() && mapOnlyRevealed;
        bool unseenChanged = isCurrentlyUnseen != shouldBeUnseen;
        isCurrentlyUnseen = shouldBeUnseen;
        if (game == null) game = Game.Instance;
        bool scoutingChanged = false;
        if (game != null)
        {
            scoutingChanged = scoutedBy.Remove(game.currentlyPlaying);
            scoutingChanged |= scoutedByTurns.Remove(game.currentlyPlaying);
            if (scoutingChanged) RebuildScoutingCache();
        }
        // The whole-board refresh sweeps every non-visible hex through here each pass;
        // for the vast majority nothing changed, so skip the per-hex visual work. The
        // terrain-sync check keeps the old self-healing behavior: if some code activated
        // this hex's art without revealing it, the next sweep still corrects it.
        bool terrainOutOfSync = terrainTexture != null && terrainTexture.gameObject.activeSelf != IsHexRevealed();
        if (!unseenChanged && !scoutingChanged && !terrainOutOfSync) return;
        UpdateVisibilityForFog();
        UpdateMinimapTerrain(IsHexRevealed());
        if (unseenChanged)
        {
            RedrawArmies(false);
            RedrawCharacters(false);
            RedrawPC(false);
            RefreshHoverText();
        }
    }

    public bool IsHidden() => !IsHexRevealed();

    /// <summary>
    /// True when this hex is an entrance to the Underground: either its tile art shows a
    /// chasm, or it holds a PC flagged as underground. Underground hexes are linked to each
    /// other through the Endless Stairs opportunity.
    /// </summary>
    public bool IsUnderground()
    {
        if (IsWaterTerrain()) return false;
        if (isChasm) return true;
        PC pcData = GetPCData();
        return pcData != null && pcData.isUnderground;
    }

    /// <summary>Shows/hides the underground marker sprite for this hex's current state.</summary>
    public void UpdateUndergroundMarker()
    {
        if (underground == null) return;
        SetActiveFast(underground, IsUnderground() && IsHexRevealed());
    }

    public int GetTerrainCost(Character character)
    {
        if (character != null && character.GetIgnoreTerrainMovementPenalty())
            return 1;
        if (!character.IsArmyCommander()) return 1;

        return Mathf.Max(1, TerrainData.terrainCosts[terrainType]);
    }

    public bool IsWaterTerrain()
    {
        return terrainType == TerrainEnum.shallowWater || terrainType == TerrainEnum.deepWater;
    }

    public bool HasAnchoredWarships() => anchoredWarshipsTotal > 0;

    public bool HasAnchoredWarshipsForLeader(Leader leader)
    {
        return leader != null && anchoredWarships.TryGetValue(leader, out int count) && count > 0;
    }

    public int GetAnchoredWarshipsTotal() => anchoredWarshipsTotal;

    public int GetAnchoredWarshipsForLeader(Leader leader)
    {
        if (leader == null) return 0;
        return anchoredWarships.TryGetValue(leader, out int count) ? count : 0;
    }

    public int AddAnchoredWarships(Leader leader, int amount)
    {
        if (leader == null || amount <= 0) return 0;
        if (anchoredWarships.TryGetValue(leader, out int current))
        {
            anchoredWarships[leader] = current + amount;
        }
        else
        {
            anchoredWarships.Add(leader, amount);
        }
        anchoredWarshipsTotal += amount;
        EnsureAnchoredVisibility(leader);
        UpdatePortIcon();
        RefreshHoverText();
        return amount;
    }

    public int RemoveAnchoredWarships(Leader leader, int amount)
    {
        if (leader == null || amount <= 0) return 0;
        if (!anchoredWarships.TryGetValue(leader, out int current) || current <= 0) return 0;
        int removed = Math.Min(amount, current);
        int remaining = current - removed;
        if (remaining > 0)
        {
            anchoredWarships[leader] = remaining;
        }
        else
        {
            anchoredWarships.Remove(leader);
        }
        anchoredWarshipsTotal -= removed;
        UpdatePortIcon();
        RefreshHoverText();
        UpdateAnchoredVisibilityAfterRemoval(leader);
        return removed;
    }

    public int TakeAnchoredWarships(Leader leader)
    {
        if (leader == null) return 0;
        if (!anchoredWarships.TryGetValue(leader, out int current) || current <= 0) return 0;
        anchoredWarships.Remove(leader);
        anchoredWarshipsTotal -= current;
        UpdatePortIcon();
        RefreshHoverText();
        UpdateAnchoredVisibilityAfterRemoval(leader);
        return current;
    }

    private void EnsureAnchoredVisibility(Leader leader)
    {
        if (leader == null) return;
        if (!leader.visibleHexes.Contains(this)) leader.visibleHexes.Add(this);
        scoutedBy.Add(leader);
        if (game == null) game = Game.Instance;
        if (game != null && game.player == leader && game.IsPlayerCurrentlyPlaying())
        {
            Reveal(leader);
        }
    }

    private void UpdateAnchoredVisibilityAfterRemoval(Leader leader)
    {
        if (leader == null) return;
        if (HasAnchoredWarshipsForLeader(leader)) return;
        if (leader.visibleHexes.Contains(this) && !leader.LeaderSeesHex(this))
        {
            leader.visibleHexes.Remove(this);
        }
    }

    public bool HasAnyPC()
    {
        return pc != null && pc.citySize != PCSizeEnum.NONE;
    }

    public bool ShouldShowPcVisual()
    {
        if (pc == null || pc.citySize == PCSizeEnum.NONE) return false;
        bool seen = IsHexSeen();
        if (!seen) return false;
        if (game == null) game = Game.Instance;

        PlayableLeader viewingLeader = game != null ? game.currentlyPlaying : null;
        bool pcRevealed = IsPCRevealed();
        bool ownerIsNonPlayableLeader = pc.owner is NonPlayableLeader;
        bool nplKnownByViewer = ownerIsNonPlayableLeader && viewingLeader != null && (pc.owner as NonPlayableLeader).IsRevealedToLeader(viewingLeader);
        return pcRevealed || (ownerIsNonPlayableLeader && nplKnownByViewer);
    }

    public PC GetPC()
    {
        if (pc == null || pc.citySize == PCSizeEnum.NONE) return null;
        if (pc.IsRevealed()) return pc;
        return null;
    }

    public PC GetPCData()
    {
        return pc != null && pc.citySize != PCSizeEnum.NONE ? pc : null;
    }

    public Sprite GetBaseTerrainSprite()
    {
        return baseTerrainSprite;
    }

    public void SetLandRegion(string region)
    {
        assignedLandRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        ApplyRegionColor();
    }

    private void ApplyRegionColor()
    {
        if (hexRegion == null) return;
        bool show = !string.IsNullOrWhiteSpace(assignedLandRegion) && IsHexRevealed() && revealPulseCoroutine == null;
        hexRegion.enabled = show;
        if (show) hexRegion.color = RegionColors.GetColor(assignedLandRegion, alpha: 1f);
    }

    public string GetLandRegion()
    {
        return assignedLandRegion;
    }

    // Removes this hex's PC entirely (as opposed to it changing hands) — used when a scenario PC
    // turns out to have no valid owner once the game actually starts (see
    // NationSpawner.ReconcileScenarioVariantOwnership). There is no supported "ownerless PC" state
    // outside the pre-spawned starting anchor cities, so a mismatched PC is removed rather than
    // left ownerless.
    public void ClearPC()
    {
        if (pc == null) return;
        pc = null;
        ApplyHexTextureSprite();
        RefreshHexLabel();
    }

    public void SetPC(PC pc, string pcFeature = "", string fortFeature = "", bool isIsland = false)
    {
        if (pc == null || pc.citySize == PCSizeEnum.NONE) return;
        this.pc = pc;
        if(isIsland)
        {
            if (hexTextureMapping == null) hexTextureMapping = ResolveTextureMapping();
            baseTerrainSprite = hexTextureMapping != null ? hexTextureMapping.GetIslandSprite() : baseTerrainSprite;
        }
        if (pc.owner is NonPlayableLeader)
        {
            EnsurePersistentScouting(pc.owner);
        }
        ApplyHexTextureSprite();
        UpdateUndergroundMarker();
    }

    public void ShowMovementLeft(int movementLeft, Character character)
    {
        if (!EnsureMovementCost()) return;
        SetActiveFast(movement, true);
        movementCostManager.ShowMovementLeft(Math.Max(0, movementLeft), character, BuildTerrainFeatureSpriteTags());
    }

    // Inline TMP sprite tags for this hex's terrain (plus the chasm marker), drawn alongside the
    // movement cost. Reuses the environment_terrain_features spritesheet wired on the movement text.
    private string BuildTerrainFeatureSpriteTags()
    {
        StringBuilder sb = new();
        sb.Append(SpriteTag(TerrainData.GetDisplayName(terrainType)));
        if (isChasm) sb.Append(SpriteTag("Chasm"));
        return sb.ToString();
    }

    public List<Character> GetEnemyCharacters(Leader leader)
    {
        if (ShouldIgnoreScouting(leader) || scoutedBy.Contains(leader))
            return characters.FindAll(x => !x.IsHidden() && x.GetOwner() != leader && (x.GetAlignment() != leader.GetAlignment() || x.GetAlignment() == AlignmentEnum.neutral)).ToList();
        return new(){};
    }

    public List<Character> GetFriendlyCharacters(Leader leader)
    {
        return characters.FindAll(x => x.GetOwner() == leader || (x.GetAlignment() == leader.GetAlignment() && x.GetAlignment() != AlignmentEnum.neutral)).ToList();
    }


    public List<Character> GetEnemyArmies(Leader leader)
    {
        if (ShouldIgnoreScouting(leader) || scoutedBy.Contains(leader))
            return characters.FindAll(x => x.IsArmyCommander() && x.GetOwner() != leader && (x.GetAlignment() != leader.GetAlignment() || x.GetAlignment() == AlignmentEnum.neutral)).ToList();
        return new(){};
    }

    public List<Character> GetFriendlyArmies(Leader leader)
    {
        return characters.FindAll(x => x.IsArmyCommander() && (x.GetOwner() == leader || (x.GetAlignment() == leader.GetAlignment() && x.GetAlignment() != AlignmentEnum.neutral))).ToList();
    }

    public string GetHoverV2()
    {
        return $"@{v2.x},{v2.y}";
    }

    // Location label for combat/duel/assassination narration: the PC's name if one is here and
    // revealed, otherwise the bare coordinate pair (no "@" prefix, unlike GetHoverV2).
    public string GetBattleLocationLabel()
    {
        return HasAnyPC() && IsPCRevealed() ? GetPC().pcName : $"{v2.x},{v2.y}";
    }

    public void RevealArtifact()
    {
        artifactRevealed = true;
        UpdateArtifactVisibility();
    }

    public void UpdateArtifactVisibility()
    {
        bool shouldShow = artifactRevealed && hiddenObjects != null && hiddenObjects.Count > 0 && IsHexSeen();
        SetActiveFast(artifact, shouldShow);
        if (artifactHover) SetActiveFast(artifactHover.gameObject, shouldShow);
    }

    public bool HasPendingEncounters => _pendingEncounters.Count > 0;

    /// <summary>
    /// Adds an encounter to this hex. A hex can hold at most one encounter at a time:
    /// if one is already pending, the card is rejected. Returns true only when the card
    /// was actually placed, so callers can skip raising an event icon for rejected cards.
    /// </summary>
    public bool AddPendingEncounter(CardData card)
    {
        if (card == null) return false;
        // A hex can hold at most one encounter at a time; reject any second one.
        if (_pendingEncounters.Count > 0) return false;
        _pendingEncounters.Add(card);
        UpdateEncounterVisibility();
        return true;
    }

    public CardData TakeFirstPendingEncounter()
    {
        if (_pendingEncounters.Count == 0) return null;
        CardData card = _pendingEncounters[0];
        _pendingEncounters.RemoveAt(0);
        UpdateEncounterVisibility();
        return card;
    }

    private void UpdateEncounterVisibility()
    {
        bool wasShowing = encounter != null && encounter.activeSelf;
        bool shouldShow = _pendingEncounters.Count > 0 && IsHexSeen();
        SetActiveFast(encounter, shouldShow);
        if (encounterHover) SetActiveFast(encounterHover.gameObject, shouldShow);
        if (artifactBg) SetActiveFast(artifactBg, shouldShow);
        if (shouldShow && !wasShowing)
            DeckManager.NotifyEncounterPlaced(this);
    }

    public void EnsurePersistentScouting(Leader leader)
    {
        if (leader == null) return;
        if (persistentScoutedBy.Add(leader))
        {
            scoutedBy.Add(leader);
        }
    }

    private void RebuildScoutingCache()
    {
        scoutedBy.Clear();
        foreach (Leader leader in persistentScoutedBy)
        {
            if (leader != null) scoutedBy.Add(leader);
        }
        foreach (var entry in scoutedByTurns)
        {
            if (entry.Key != null && entry.Value > 0) scoutedBy.Add(entry.Key);
        }
        foreach (var entry in anchoredWarships)
        {
            if (entry.Key != null) scoutedBy.Add(entry.Key);
        }
        UpdateParticles();
    }

    private bool ShouldIgnoreScouting(Leader leader)
    {
        if (leader == null) return false;
        if (leader is NonPlayableLeader) return true;
        if (leader is PlayableLeader pl && game != null && game.player != pl) return true;
        return false;
    }

    public void MarkDarknessByPlayer(int turns = DarknessTurnsDefault)
    {
        if (turns <= 0) return;
        darknessTurnsRemaining = Math.Max(darknessTurnsRemaining, turns);
        UpdateParticles();
    }

    public void PlayFireParticles()
    {
        if (!ShouldShowPlayerParticles()) return;
        PlaySharedOneShotParticles(SharedParticleType.Fire);
    }

    public void PlayIceParticles()
    {
        if (!ShouldShowPlayerParticles()) return;
        PlaySharedOneShotParticles(SharedParticleType.Ice);
    }

    public void PlayStatusEffectParticles(StatusEffectEnum effect)
    {
        if (!ShouldShowPlayerParticles()) return;

        switch (effect)
        {
            case StatusEffectEnum.Poisoned:
                PlaySharedOneShotParticles(SharedParticleType.Poison);
                break;
            case StatusEffectEnum.Encouraged:
                PlaySharedOneShotParticles(SharedParticleType.Courage);
                break;
            case StatusEffectEnum.Hope:
                PlaySharedOneShotParticles(SharedParticleType.Hope);
                break;
        }
    }

    private bool ShouldShowPlayerParticles()
    {
        if (!IsHexSeen()) return false;
        if (game == null) game = Game.Instance;
        return game != null && game.player != null;
    }

    private void UpdateParticles()
    {
        if (game == null) game = Game.Instance;
        PlayableLeader player = game != null ? game.player : null;
        bool seen = IsHexSeen();
        bool scoutedByPlayer = player != null && scoutedBy.Contains(player);
        if (framesColors != null)
        {
            framesColors.SetScouted(seen && scoutedByPlayer);
            framesColors.SetDarkness(seen && darknessTurnsRemaining > 0);
        }
    }

    // Safe SetActive that avoids redundant calls/dirtying the obj
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void SetActiveFast(GameObject go, bool state)
    {
        if (go && go.activeSelf != state) go.SetActive(state);
    }

    private void RefreshFrontierRowVisuals()
    {
        bool revealed = IsHexRevealed();
        bool isWaterHex = terrainType == TerrainEnum.shallowWater || terrainType == TerrainEnum.deepWater;
        float frontierAlpha = isCurrentlyUnseen ? 0.1f : 1f;

        if (!revealed)
        {
            SetActiveFast(cliffGameObject, false);
            SetActiveFast(hexTextureWater, false);
            return;
        }

        SetActiveFast(hexTextureWater, isWaterHex);
        SetActiveFast(cliffGameObject, !isWaterHex);
        SetFrontierRowAlpha(frontierAlpha);
    }

    // Builds the scene-wide particle templates from the assigned sharedParticlesPrefab.
    // Runs from every hex's Awake (passing its own serialized reference) but only does
    // work when the templates are missing (first hex of a scene, or after a scene
    // change destroyed the old ones).
    private static void EnsureSharedParticleTemplates(GameObject prefab)
    {
        // Must match by count against every enum member, not just "existing entries look valid" —
        // otherwise a new SharedParticleType added after the pools were already built (e.g. domain
        // reload disabled on Enter Play Mode, so statics survive across Play sessions) never gets
        // registered until something forces a real reload, and its one-shot silently no-ops forever.
        bool poolsAlive = sharedParticlePools.Count == Enum.GetValues(typeof(SharedParticleType)).Length;
        foreach (SharedParticlePoolState state in sharedParticlePools.Values)
        {
            if (state == null || state.template == null)
            {
                poolsAlive = false;
                break;
            }
        }
        if (sharedSelectedParticles != null && poolsAlive) return;

        if (prefab == null)
        {
            Debug.LogError("Hex.sharedParticlesPrefab is not assigned in the Inspector; hex particles disabled.");
            return;
        }

        if (sharedSelectedParticles == null)
        {
            Transform source = prefab.transform.Find("Particles/selectedParticles");
            if (source != null)
            {
                sharedSelectedParticlesLocalPosition = source.localPosition;
                sharedSelectedParticlesLocalRotation = source.localRotation;
                sharedSelectedParticlesLocalScale = source.localScale;
                sharedSelectedParticles = Instantiate(source.gameObject, GetSharedParticlePoolRoot());
                sharedSelectedParticles.name = "SharedSelectedParticles";
                SetActiveFast(sharedSelectedParticles, false);
                sharedSelectedParticlesOwner = null;
            }
        }

        RegisterSharedParticleTemplate(SharedParticleType.Fire, prefab.transform.Find("Particles/fireParticles"));
        RegisterSharedParticleTemplate(SharedParticleType.Ice, prefab.transform.Find("Particles/iceParticles"));
        RegisterSharedParticleTemplate(SharedParticleType.Poison, prefab.transform.Find("Particles/poisonParticles"));
        RegisterSharedParticleTemplate(SharedParticleType.Courage, prefab.transform.Find("Particles/courageParticles"));
        RegisterSharedParticleTemplate(SharedParticleType.Hope, prefab.transform.Find("Particles/hopeParticles"));
        RegisterSharedParticleTemplate(SharedParticleType.PcReveal, prefab.transform.Find("Particles/pcRevealParticles"));
    }

    private void SetSharedSelectedParticlesActive(bool active)
    {
        if (sharedSelectedParticles == null)
        {
            return;
        }

        if (!active)
        {
            if (sharedSelectedParticlesOwner == this)
            {
                sharedSelectedParticlesOwner = null;
                SetActiveFast(sharedSelectedParticles, false);
            }
            return;
        }

        sharedSelectedParticlesOwner = this;
        if (sharedSelectedParticles.transform.parent != transform)
        {
            sharedSelectedParticles.transform.SetParent(transform, false);
        }

        sharedSelectedParticles.transform.localPosition = sharedSelectedParticlesLocalPosition;
        sharedSelectedParticles.transform.localRotation = sharedSelectedParticlesLocalRotation;
        sharedSelectedParticles.transform.localScale = sharedSelectedParticlesLocalScale;
        SetActiveFast(sharedSelectedParticles, true);
    }

    private static void RegisterSharedParticleTemplate(SharedParticleType type, Transform source)
    {
        if (source == null)
        {
            Debug.LogError($"Hex: HexSharedParticles prefab is missing the '{type}' particles child.");
            return;
        }

        if (sharedParticlePools.TryGetValue(type, out SharedParticlePoolState state) && state.template != null)
        {
            return;
        }

        if (state == null)
        {
            state = new SharedParticlePoolState();
            sharedParticlePools[type] = state;
        }
        state.instances.Clear();   // instances from a previous scene are destroyed
        state.localPosition = source.localPosition;
        state.localRotation = source.localRotation;
        state.localScale = source.localScale;
        state.template = Instantiate(source.gameObject, GetSharedParticlePoolRoot());
        state.template.name = $"{type}SharedParticleTemplate";
        SetActiveFast(state.template, false);
    }

    private void PlaySharedOneShotParticles(SharedParticleType type)
    {
        GameObject particlesObject = AcquireSharedParticleInstance(type);
        if (particlesObject == null)
        {
            return;
        }

        ParticleSystem[] systems = particlesObject.GetComponentsInChildren<ParticleSystem>(true);
        if (systems.Length == 0)
        {
            SetActiveFast(particlesObject, false);
            return;
        }

        SetActiveFast(particlesObject, true);
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            systems[i].Clear(true);
            systems[i].Play(true);
        }

        StartCoroutine(DisableSharedParticlesWhenDone(particlesObject, systems));
    }

    private GameObject AcquireSharedParticleInstance(SharedParticleType type)
    {
        if (!sharedParticlePools.TryGetValue(type, out SharedParticlePoolState state) || state.template == null)
        {
            return null;
        }

        GameObject instance = null;
        for (int i = 0; i < state.instances.Count; i++)
        {
            GameObject candidate = state.instances[i];
            if (candidate != null && !candidate.activeSelf)
            {
                instance = candidate;
                break;
            }
        }

        if (instance == null)
        {
            if (state.instances.Count < SharedOneShotParticlePoolSize)
            {
                instance = Instantiate(state.template, transform);
                instance.name = $"{type}SharedParticle";
                state.instances.Add(instance);
            }
            else
            {
                instance = state.instances[0];
                SetActiveFast(instance, false);
            }
        }

        if (instance == null)
        {
            return null;
        }

        if (instance.transform.parent != transform)
        {
            instance.transform.SetParent(transform, false);
        }

        instance.transform.localPosition = state.localPosition;
        instance.transform.localRotation = state.localRotation;
        instance.transform.localScale = state.localScale;
        return instance;
    }

    private IEnumerator DisableSharedParticlesWhenDone(GameObject particlesObject, ParticleSystem[] systems)
    {
        if (!particlesObject || systems == null || systems.Length == 0) yield break;

        bool anyAlive = true;
        while (anyAlive)
        {
            anyAlive = false;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i].IsAlive(true))
                {
                    anyAlive = true;
                    break;
                }
            }
            if (anyAlive) yield return null;
        }

        SetActiveFast(particlesObject, false);
    }

    private void StopSharedOneShotParticlesOnThisHex()
    {
        foreach (SharedParticlePoolState state in sharedParticlePools.Values)
        {
            if (state == null || state.instances == null) continue;

            for (int i = 0; i < state.instances.Count; i++)
            {
                GameObject instance = state.instances[i];
                if (instance == null || instance.transform.parent != transform) continue;

                ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < systems.Length; j++)
                {
                    if (systems[j] == null) continue;
                    systems[j].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                SetActiveFast(instance, false);
            }
        }
    }

    private static Transform GetSharedParticlePoolRoot()
    {
        if (sharedParticlePoolRoot == null)
        {
            GameObject root = new("HexSharedParticlePool");
            sharedParticlePoolRoot = root.transform;
        }

        return sharedParticlePoolRoot;
    }

    private void ApplyHexTextureSprite()
    {
        if (terrainTexture == null) return;
        if (hexTextureMapping == null) hexTextureMapping = ResolveTextureMapping();

        // Respect fog: SetTerrain runs on every hex during board load, and unconditionally
        // activating the art here used to rely on the first whole-board Hide() sweep to put
        // the fog back — that sweep now early-outs, so apply the correct state directly.
        SetActiveFast(terrainTexture.gameObject, IsHexRevealed());
        terrainTexture.sprite = hexTextureMapping != null ? hexTextureMapping.GetTerrainSprite(this) : baseTerrainSprite;

        if (pcTexture != null)
        {
            Sprite pcSprite = hexTextureMapping != null ? hexTextureMapping.GetPcSprite(this) : null;
            pcTexture.sprite = pcSprite;
            pcTexture.gameObject.SetActive(pcSprite != null);
        }

        UpdateTerrainVisualAlpha();
        UpdateMinimapTerrain(IsHexRevealed());

        // The rim blend samples this hex's (possibly new) art — this hex and its neighbors
        // need their seamless-blend property blocks rebuilt at the end of the frame.
        HexSeamlessTerrain.MarkDirty(this);
    }

    // Terrain sprites draw slightly larger than their cell so the seamless-blend feather fades
    // over opaque neighbor art instead of exposing baked tile borders. Applied once per hex
    // (Initialize re-runs on pooled reuse); the captured base scale keeps the reveal pulse from
    // baking a pre-overdraw value back in.
    private void ApplyTerrainOverdraw()
    {
        if (terrainOverdrawApplied || terrainTexture == null) return;
        terrainOverdrawApplied = true;
        Transform terrainTransform = terrainTexture.transform;
        Vector3 scale = terrainTransform.localScale;
        scale.x *= HexSeamlessTerrain.TileOverdraw;
        scale.y *= HexSeamlessTerrain.TileOverdraw;
        terrainTransform.localScale = scale;
        terrainBaseScale = scale;
        terrainBaseScaleCaptured = true;
    }

    // Drawn terrain size in world units (overdraw included), measured from the captured base
    // scale so a running reveal-pulse animation can't skew the seamless-blend geometry.
    public Vector2 GetTerrainDrawnWorldSize()
    {
        if (terrainTexture == null || terrainTexture.sprite == null) return Vector2.zero;
        Transform terrainTransform = terrainTexture.transform;
        Vector3 scale = terrainBaseScaleCaptured ? terrainBaseScale : terrainTransform.localScale;
        Vector3 parentScale = terrainTransform.parent != null ? terrainTransform.parent.lossyScale : Vector3.one;
        Vector3 spriteSize = terrainTexture.sprite.bounds.size;
        return new Vector2(spriteSize.x * scale.x * parentScale.x, spriteSize.y * scale.y * parentScale.y);
    }

    private void UpdateTerrainVisualAlpha()
    {
        float terrainAlpha = isCurrentlyUnseen ? 0.1f : 1f;
        SetHexSpriteAlpha(terrainAlpha);
    }

    private void SetFrontierRowAlpha(float alpha)
    {
        if (cliffGameObject != null)
        {
            SetSpriteAlpha(cliffGameObject.GetComponent<SpriteRenderer>(), alpha);
        }

        if (hexTextureWater != null)
        {
            SetSpriteAlpha(hexTextureWater.GetComponent<SpriteRenderer>(), alpha);
        }
    }

    // Content only — the visual format (bold white text over a dark backing band, matching the
    // Scenario Creator's hex captions) lives in the HexPcText prefab (font style + Band child).
    private string BuildPcNameLabel()
    {
        if (pc == null) return string.Empty;

        StringBuilder builder = new();
        builder.Append(pc.pcName ?? string.Empty);
        if (pc.pcName != null) builder.Append("<br>");

        if (pc.citySize != PCSizeEnum.NONE)
        {
            builder.Append("<sprite name=\"pc\"><color=");
            builder.Append(GetGradientColorHex((int)pc.citySize, (int)PCSizeEnum.camp, (int)PCSizeEnum.city));
            builder.Append('>');
            builder.Append((int)pc.citySize);
            builder.Append("</color>");
        }

        if (pc.fortSize > FortSizeEnum.NONE)
        {
            if (pc.citySize != PCSizeEnum.NONE || pc.hasPort) builder.Append(' ');
            builder.Append("<sprite name=\"fort\"><color=");
            builder.Append(GetGradientColorHex((int)pc.fortSize, (int)FortSizeEnum.tower, (int)FortSizeEnum.citadel));
            builder.Append('>');
            builder.Append((int)pc.fortSize);
            builder.Append("</color>");
        }

        builder.Append("<sprite name=\"loyalty\"><color=");
        builder.Append(GetLoyaltyColorHex(pc.loyalty));
        builder.Append('>');
        builder.Append(Math.Max(0, pc.loyalty));
        builder.Append("</color>");

        if (pc.hasPort) builder.Append(" <sprite name=\"port\">");
        if (pc.isHidden) builder.Append(" <sprite name=\"hidden\">");

        return builder.ToString();
    }

    private static string GetGradientColorHex(int value, int minValue, int maxValue)
    {
        float t = Mathf.InverseLerp(minValue, maxValue, value);
        Color blended = Color.Lerp(new Color(1f, 0.85f, 0.2f, 1f), new Color(0.2f, 0.8f, 0.2f, 1f), t);
        return $"#{ColorUtility.ToHtmlStringRGB(blended)}";
    }

    private static string GetLoyaltyColorHex(int loyaltyValue)
    {
        if (loyaltyValue <= 33) return "#ff4d4d";
        if (loyaltyValue <= 66) return "#ffd54f";
        return "#00c853";
    }

    public void SetCharacterHovered(bool hovered)
    {
        if (isCharacterHovered == hovered) return;
        isCharacterHovered = hovered;
        hoveredCharacterCount = Mathf.Max(0, hoveredCharacterCount + (hovered ? 1 : -1));

        // Class icons (commander/agent/emmissary/mage) only show while the cursor is actually
        // on the character sprite — otherwise they sit on screen permanently and bury the map.
        /*
        if (characterClassesIconGrid != null)
            SetActiveFast(characterClassesIconGrid.gameObject, hovered && characterClassesIconGrid.transform.childCount > 0);  
        */
    }

    // Driven by HexPcTextHover (mouse over the HexPcText label/Band), not by hovering the hex at
    // large — the PC/Region card preview only shows up for that specific, small hover target
    // instead of blanketing the whole tile.
    public void SetPcTextHovered(bool hovered)
    {
        if (isPcTextHovered == hovered) return;
        isPcTextHovered = hovered;

        if (hovered) TryShowPcCardPreview();
        else CancelPcCardPreview();
    }

    private void UpdateCharacterSpriteAlpha()
    {
        if (characterSpriteRenderer == null || !characterSpriteRenderer.gameObject.activeSelf) return;

        CharacterAnimationController controller = characterAnimationController;
        float outlineSize = controller != null ? controller.outlineSize : 10f;
        Color color;

        if (isCharacterHovered)
        {
            color = controller != null ? controller.hoveredColor : Color.white;
            controller?.SetOutlineAlpha(1f);
            controller?.SetOutlineSize(outlineSize);
            ApplyCharacterAndStackColor(color);
            return;
        }

        bool someoneElseHovered = hoveredCharacterCount > 0;

        if (board == null) board = Board.Instance;
        Character selected = board != null ? board.selectedCharacter : null;

        Color idleColor = controller != null
            ? (someoneElseHovered ? controller.otherHoveredColor : controller.unhoveredColor)
            : Color.white;

        if (selected != null && selected.hex == this && characterSpriteRenderer.sprite != null)
        {
            // The selected character's outline stays steady like everyone else's — instead its
            // sprite color pulses between the idle color above and white to show it's the selection.
            float pulseSpeed = controller != null ? controller.selectionPulseSpeed : 1f;
            float colorT = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            color = Color.Lerp(idleColor, Color.white, colorT);
            controller?.SetOutlineSize(outlineSize);
        }
        else
        {
            color = idleColor;
            controller?.SetOutlineSize(outlineSize);
        }
        controller?.SetOutlineAlpha(1f);
        ApplyCharacterAndStackColor(color);
    }

    // Extras dim slightly more than the main sprite so the stack still reads as "one figure in
    // front, duplicates behind" rather than five identical, equally bright copies. They stay
    // outline-free (set once in CreateArmyStackExtraRenderer) so only the front figure gets one.
    private void ApplyCharacterAndStackColor(Color color)
    {
        characterSpriteRenderer.color = color;
        for (int i = 0; i < armyStackExtraRenderers.Count; i++)
        {
            SpriteRenderer extra = armyStackExtraRenderers[i];
            if (extra == null || !extra.gameObject.activeSelf) continue;
            Color extraColor = color;
            extraColor.a *= 0.85f;
            extra.color = extraColor;
        }
    }

}
