using UnityEngine;

// Tunables for the "tabletop" board look (see BoardLook, TabletopLens, FogOfWarOverlay): the board
// reads as a painted map on a lamp-lit table with miniatures standing on it. Lives in Resources so
// the lens shader ships with the build. Grade and cloud values apply when a campaign starts; the
// tilt-shift values are read every frame, so they can be tuned live in Play mode.
[CreateAssetMenu(menuName = "RetroLOTR/Board Look Settings")]
public class BoardLookSettings : ScriptableObject
{
    [Header("Tilt-shift (TabletopLens)")]
    [Tooltip("Blurs the top and bottom of the screen like a close-up photo of a model, so the board reads as a physical table.")]
    public bool tiltShift = true;
    [Tooltip("Hidden/RetroLOTR/TabletopLens")]
    public Shader tiltShiftShader;
    [Tooltip("Centre of the sharp band, as a fraction of screen height.")]
    [Range(0f, 1f)] public float focusCenter = 0.5f;
    [Tooltip("Half the height of the sharp band, as a fraction of screen height.")]
    [Range(0f, 0.5f)] public float focusHalfWidth = 0.2f;
    [Tooltip("Distance past the sharp band over which the blur ramps up to full, as a fraction of screen height.")]
    [Range(0.01f, 0.5f)] public float focusFalloff = 0.25f;
    [Tooltip("Blur radius at the screen edges, in pixels at 1080p.")]
    [Range(0f, 12f)] public float maxBlur = 4f;

    [Header("Grade (BoardLook, applied when a campaign starts)")]
    [Tooltip("White balance temperature: + = warmer, like a lamp-lit table.")]
    [Range(-30f, 30f)] public float warmth = 0f; // 0: the seasons (WorldLight) already warm autumn; +8 stacked on them turned the board yellow
    [Range(0f, 1f)] public float vignette = 0.34f;
    [Tooltip("Film grain, so the 2D map and the 3D units share one surface texture.")]
    [Range(0f, 1f)] public float grain = 0.18f;

    [Header("Fog (FogOfWarOverlay, applied when a campaign starts)")]
    [Tooltip("The warm rim where the fog thins at the edge of sight. The shader's default (0.35) read as an orange halo round the visible land.")]
    [Range(0f, 1f)] public float fogEdgeGlow = 0.12f;

    [Header("Cloud shadows (FogOfWarOverlay)")]
    [Tooltip("How dark the drifting cloud shadows are on land in sight.")]
    [Range(0f, 0.6f)] public float cloudShadow = 0.2f;
    [Tooltip("Cloud size: noise frequency per world unit (smaller = larger clouds).")]
    [Range(0.01f, 0.5f)] public float cloudShadowScale = 0.07f;
    [Tooltip("Drift, in world units per second.")]
    public Vector2 cloudShadowDrift = new(0.12f, 0.04f);

    [Header("Ambient life (AmbientLife; FogOfWar.shader for water)")]
    [Tooltip("RetroLOTR/AmbientParticle — soft puffs for smoke and dust.")]
    public Shader particleShader;
    [Tooltip("Chimney smoke over settlements in sight.")]
    public bool smoke = true;
    [Tooltip("Small flocks of birds crossing the view now and then.")]
    public bool birds = true;
    [Tooltip("Seconds between flocks (min, max).")]
    public Vector2 birdInterval = new(14f, 35f);
    [Tooltip("Dust puffs when a miniature sets off or stops.")]
    public bool dust = true;
    [Tooltip("Sun glints on water in sight (applied when a campaign starts).")]
    [Range(0f, 1f)] public float waterShimmer = 0f; // off: on river hexes the glints read as stray white puffs

    [Header("Light (WorldLight; relief in HexSeamlessBlend, live)")]
    [Tooltip("Grade follows the calendar's seasons: fresh spring, warm summer, golden autumn, pale winter.")]
    public bool seasons = true;
    [Tooltip("Dusk falls while the other nations take their turns; morning returns with yours.")]
    public bool duskDuringOtherTurns = true;
    [Range(0f, 1f)] public float duskStrength = 0.8f;
    [Tooltip("Relief lighting on the painted terrain: slopes facing the front-left light brighten, the others shade.")]
    [Range(0f, 1.5f)] public float terrainRelief = 0.5f;
    [Tooltip("How far into each hex the terrain blends with its neighbours (fraction of centre-to-edge). " +
             "1 blends all the way to the centre — one soft painted surface, but every tile smeared toward its " +
             "neighbours with mirrored repeats; ~0.35 keeps each painting's own detail and contrast while the seams " +
             "still melt together.")]
    [Range(0.05f, 1f)] public float terrainBlendBand = 0.35f;

    [Header("Camera (BoardNavigator rig, live)")]
    [Tooltip("Degrees the board camera looks up the board, in perspective, like a photo of a tabletop. 0 = straight down, orthographic.")]
    [Range(0f, 35f)] public float cameraTilt = 15f;
    [Tooltip("Field of view while tilted: narrow keeps the map undistorted, wide exaggerates depth.")]
    [Range(10f, 60f)] public float cameraFov = 25f;

    [Header("Close-ups (CloseUps → BoardNavigator.PlayCloseUp)")]
    [Tooltip("Ease the camera in on battles, duels, big spells and deaths in sight, then back.")]
    public bool closeUps = true;
    [Tooltip("Orthographic size during a close-up (smaller = closer). Never zooms out.")]
    [Range(1f, 6f)] public float closeUpZoom = 2.8f;
    [Tooltip("Seconds the camera holds on the scene.")]
    [Range(0.3f, 4f)] public float closeUpHold = 1.4f;
    [Tooltip("Minimum seconds between close-ups, so a busy AI turn doesn't become a slideshow.")]
    [Range(0f, 30f)] public float closeUpCooldown = 8f;

    [Header("3D settlements (City3D, live)")]
    [Tooltip("Settlements as procedural 3D miniatures instead of their painted PC tiles.")]
    public bool cities3D = true;
    [Tooltip("Size of the 3D settlements relative to the hex.")]
    [Range(0.5f, 1.6f)] public float cityScale = 1f;
    [Tooltip("How far the settlements' colours are pulled onto the terrain tiles' own palette (Resources/TerrainPalette.json), " +
             "so their greens, stones and slates are the map's. 0 = the recipes' colours as written.")]
    [Range(0f, 1f)] public float cityPaletteMatch = 0.8f;
    [Tooltip("Fine-tune after the palette match: brightness of the settlements' colours.")]
    [Range(0.4f, 1.6f)] public float cityBrightness = 1f;
    [Tooltip("Fine-tune after the palette match: saturation of the settlements' colours.")]
    [Range(0f, 2f)] public float citySaturation = 1f;

    [Header("City banners (HexNameplates)")]
    [Tooltip("Font for the city banners' names and numbers (Jacquard 12, a pixel blackletter). Empty = the hex label's font.")]
    public TMPro.TMP_FontAsset bannerFont;

    [Header("Settlement art (RetroLOTR/PCArt, applied by HexSeamlessTerrain; live)")]
    [Tooltip("RetroLOTR/PCArt — grades PC art toward the blended terrain and dissolves its lower ground rim. Empty = plain sprites.")]
    public Shader pcArtShader;
    [Range(0f, 1.5f)] public float pcSaturation = 0.8f;
    [Range(0.5f, 1.5f)] public float pcContrast = 0.85f;
    [Range(0.5f, 1.5f)] public float pcBrightness = 0.9f;
    [Tooltip("Where the ground rim starts fading along the hex's lower edges (1 = the hex edge; lower = a wider fade).")]
    [Range(0.3f, 1f)] public float pcRimStart = 0.72f;

    private static BoardLookSettings instance;
    private static bool looked;

    public static BoardLookSettings Instance
    {
        get
        {
            if (!looked)
            {
                looked = true;
                instance = Resources.Load<BoardLookSettings>("BoardLookSettings");
                if (instance == null) instance = CreateInstance<BoardLookSettings>();
            }
            return instance;
        }
    }
}
