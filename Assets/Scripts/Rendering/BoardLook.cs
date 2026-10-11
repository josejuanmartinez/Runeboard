using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The board's overall rendering look, applied once a campaign starts:
///  • post: a soft vignette to frame the play area, gentle bloom so gold rings, sparkles and lit
///    windows glow, slightly richer colour, and no motion blur (camera pans used to smear the map)
///  • grounding: a soft contact shadow under every unit so figures stand on the tile instead of
///    floating over it
///  • framing: the camera opens a little further out so a starting realm reads as a place
/// </summary>
public static class BoardLook
{
    public const float DefaultZoom = 6.2f;

    /// <summary>
    /// "Night shroud" for unexplored land: terrain is drawn as dark, cool silhouettes instead of
    /// a black void, so the continent's shape reads under the fog. Units, PCs and labels stay
    /// hidden. "-look-classic" on the command line restores the old hidden-terrain look.
    /// </summary>
    public static readonly bool Shroud = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-look-classic") < 0;
    public static readonly Color ShroudTint = new(0.24f, 0.26f, 0.31f, 1f);

    /// <summary>Shroud only once a campaign is running (menus and leader selection keep the board hidden).</summary>
    public static bool ShroudActive => Shroud && Game.Instance != null && Game.Instance.started;

    private static Sprite shadowSprite;

    public static void Apply()
    {
        ApplyPost();
        if (Shroud)
        {
            // Hexes evaluated their fog state before the game started; re-apply with the shroud on.
            Board board = Board.Instance;
            if (board?.hexes != null)
                foreach (Hex hex in board.hexes.Values) hex?.RefreshFogVisuals();
            FogOfWarOverlay.Ensure();
        }
        BoardNavigator nav = BoardNavigator.Instance;
        if (nav != null) nav.DebugSetZoom(DefaultZoom);
    }

    private static void ApplyPost()
    {
        Volume volume = null;
        foreach (Volume v in Object.FindObjectsByType<Volume>())
        {
            if (v.isGlobal && v.sharedProfile != null && v.gameObject.layer != LayerMask.NameToLayer("UI")) { volume = v; break; }
        }
        if (volume == null) return;
        VolumeProfile profile = volume.profile; // instance copy; the asset stays untouched

        if (profile.TryGet(out MotionBlur motionBlur)) motionBlur.active = false;

        Vignette vignette = profile.TryGet(out Vignette existingVignette) ? existingVignette : profile.Add<Vignette>(true);
        vignette.active = true;
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.55f);
        vignette.color.Override(new Color(0.02f, 0.02f, 0.03f));

        Bloom bloom = profile.TryGet(out Bloom existingBloom) ? existingBloom : profile.Add<Bloom>(true);
        bloom.active = true;
        // Above 1: only HDR highlights (additive sparkles, rings) bloom — never plain white city art.
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.6f);

        if (profile.TryGet(out ColorAdjustments color))
        {
            color.saturation.Override(10f);
        }
    }

    /// <summary>Adds a soft contact shadow under a unit sprite (child of its renderer, under the ring).</summary>
    public static void AttachShadow(SpriteRenderer character)
    {
        if (character == null || character.transform.Find("ContactShadow") != null) return;
        var go = new GameObject("ContactShadow", typeof(SpriteRenderer));
        go.transform.SetParent(character.transform, false);
        // Same spot as the prefab's GoldRing (character-local units), nudged up a hair so the
        // ring still sorts in front of it on the Y sort axis.
        go.transform.localPosition = new Vector3(0.04f, -1.64f, 0.15f);
        go.transform.localScale = new Vector3(4.6f, 4.6f, 1f);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = ShadowSprite();
        sr.color = new Color(0f, 0f, 0f, 0.42f); // ambient occlusion only; CastShadow carries the shape
        sr.sortingLayerID = character.sortingLayerID;
        sr.sortingOrder = character.sortingOrder - 1;
        CharacterCastShadow.Attach(character);
    }

    /// <summary>Soft-edged ellipse sprite (also used as a scrim behind banner text).</summary>
    public static Sprite SoftEllipseSprite() => ShadowSprite();

    private static Sprite ringSprite;

    /// <summary>Thin soft-edged elliptical ring (objective markers), generated once.</summary>
    public static Sprite SoftRingSprite()
    {
        if (ringSprite != null) return ringSprite;
        const int w = 256, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f) / w * 2f - 1f;
                float dy = (y + 0.5f) / h * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Crisp core at d=0.9 with a short inner halo.
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.035f) + 0.35f * Mathf.Clamp01(1f - (0.9f - d) / 0.18f) * (d < 0.9f ? 1f : 0f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        ringSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w / 1.2f);
        ringSprite.name = "ObjectiveRing";
        return ringSprite;
    }

    // Soft-edged ellipse, generated once (no art asset needed).
    private static Sprite ShadowSprite()
    {
        if (shadowSprite != null) return shadowSprite;
        const int w = 128, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f) / w * 2f - 1f;
                float dy = (y + 0.5f) / h * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.35f, 1f, d));
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        shadowSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        shadowSprite.name = "ContactShadow";
        return shadowSprite;
    }
}
