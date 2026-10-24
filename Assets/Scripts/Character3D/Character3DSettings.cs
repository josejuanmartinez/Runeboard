using UnityEngine;

// Tunables for the live-3D board character prototype (see Character3DView). Lives in Resources so
// the shader and AnimatorController it references ship with the build without being Addressable.
[CreateAssetMenu(menuName = "RetroLOTR/Character 3D Settings")]
public class Character3DSettings : ScriptableObject
{
    public enum Mode { Off, Replace, SideBySide }

    public Shader shader;
    [Tooltip("RetroLOTR/Character3DOutline — the alignment outline drawn around each model.")]
    public Shader outlineShader;
    [Tooltip("Alignment outline width, in screen pixels.")]
    [Range(0f, 6f)] public float outlineWidth = 2f;

    [Tooltip("How board characters are drawn: live 3D instead of the baked sprites (Replace), next to them, or not at all.")]
    public Mode startMode = Mode.Replace;

    [Tooltip("Degrees the model leans its top toward the camera, so the flat top-down board reads it from slightly above like the baked sprites.")]
    [Range(0f, 80f)] public float pitch = 25f;
    [Tooltip("Fallback only, until the sprite's own figure has been measured: model height as a fraction of the sprite cell height.")]
    public float heightFraction = 0.53f;
    [Tooltip("Multiplier on the measured sprite figure height (1 = exactly as tall on screen as the baked sprite).")]
    public float figureScale = 1f;
    [Tooltip("Extra feet offset, in sprite cell heights, on top of the GoldRing anchor.")]
    public float feetOffset;
    [Tooltip("Extra size when zoomed out: at 2.5× the default zoom, units are this much larger again (0.4 = +40%), so they still read at strategic zoom.")]
    [Range(0f, 1.5f)] public float zoomedOutBoost = 0.4f;
    [Header("Strategic zoom tokens (see UnitToken)")]
    [Tooltip("RetroLOTR/UnitToken. Empty = no tokens, figures at every zoom.")]
    public Shader tokenShader;
    [Tooltip("Orthographic sizes where figures start turning into tokens, and where they are tokens only.")]
    public Vector2 tokenZoom = new(10f, 13f);
    [Header("Miniature base (see Character3DBase)")]
    [Tooltip("Stand every unit on a bevelled base with a nation-coloured rim, like a painted miniature.")]
    public bool showBase = true;
    [Tooltip("Base radius as a fraction of the model's height.")]
    [Range(0.1f, 0.6f)] public float baseRadius = 0.3f;
    [Tooltip("Side By Side mode: how far right of the sprite the model stands, in sprite cell heights.")]
    public float sideBySideOffset = 0.45f;
    [Tooltip("Moving faster than this (sprite cells per second) switches the walk to a run.")]
    public float runSpeed = 2.2f;
    [Tooltip("Degrees per second the model turns toward its facing.")]
    public float turnSpeed = 720f;
    [Header("Scene lighting (see Character3DLighting)")]
    [Tooltip("0 = flat unlit, exactly how the spritesheets were baked; 1 = fully lit.")]
    [Range(0f, 1f)] public float lit = 1f;
    [Tooltip("Where the key light comes from, around the unit: 0 = from the viewer, + = from the left. The painted map and the sprite cast shadows are lit from the front-left.")]
    [Range(-180f, 180f)] public float lightAzimuth = 45f;
    [Tooltip("Key light height above the ground. With the default pitch, 40 lays 3D shadows on the same slant as the sprite cast shadows.")]
    [Range(5f, 89f)] public float lightElevation = 40f;
    public Color keyColor = new(0.45f, 0.42f, 0.36f);
    [Tooltip("Ambient from above (cool sky).")]
    public Color skyColor = new(0.62f, 0.64f, 0.7f);
    [Tooltip("Ambient from below (warm, earthy bounce off the painted board).")]
    public Color groundColor = new(0.42f, 0.39f, 0.33f);
    [Tooltip("Width of the soft edge between the lit and shaded sides; small = more painterly two-tone.")]
    [Range(0.01f, 1f)] public float bandSoftness = 0.12f;
    [Tooltip("Shifts the light/shade boundary; + puts more of the figure in shade.")]
    [Range(-0.5f, 0.5f)] public float bandOffset = 0.05f;
    [Tooltip("Cast shadow colour; alpha = opacity at the feet. Matches the sprite cast shadows.")]
    public Color shadowColor = new(0.02f, 0.025f, 0.04f, 0.8f);
    [Tooltip("Cast shadow opacity at the top of the figure, relative to the feet.")]
    [Range(0f, 1f)] public float shadowFarOpacity = 0.45f;
    [Tooltip("Scales the alignment outline strength taken from the sprite controller.")]
    [Range(0f, 3f)] public float rimStrength = 1f;


    private static Character3DSettings instance;
    private static bool looked;

    public static Character3DSettings Instance
    {
        get
        {
            if (!looked)
            {
                looked = true;
                instance = Resources.Load<Character3DSettings>("Character3DSettings");
            }
            return instance;
        }
    }
}
