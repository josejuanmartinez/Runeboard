using UnityEngine;

/// <summary>
/// A real cast shadow for baked character sprites: every frame it draws the character's current
/// animation frame flattened onto the ground and slanted away from the light (light from the
/// front-left, so the shadow falls back and to the right), with the SpriteCastShadow shader making
/// it sharp at the feet and soft toward its far end. Because it is the actual silhouette, the
/// shadow walks, swings and attacks with the unit, which grounds it on the board far better than
/// a round blob.
///
/// The ground projection is the shear  M = [[1, Shear], [0, Squash]]  about the feet. Unity
/// transforms can't shear directly, so M is decomposed (2×2 SVD) into rotate · scale · rotate and
/// spread over three nested transforms.
/// </summary>
public class CharacterCastShadow : MonoBehaviour
{
    public const float Shear = 0.85f;     // sideways lean per unit of height (also Character3DLighting)
    public const float Squash = 0.42f;    // how flat the shadow lies
    private static readonly Vector2 Foot = new(0.04f, -1.70f); // character-local, same spot as the GoldRing
    private const float FootV = 0.17f;    // feet height inside a 512px frame (pivot is the frame centre)

    private static Material material;
    private static readonly int UvRectId = Shader.PropertyToID("_UVRect");
    private static readonly int FootVId = Shader.PropertyToID("_FootV");

    private SpriteRenderer source;
    private SpriteRenderer shadow;
    private MaterialPropertyBlock block;
    private Sprite lastSprite;

    public static void Attach(SpriteRenderer character)
    {
        if (character == null || character.transform.Find("CastShadow") != null) return;
        if (material == null) material = Resources.Load<Material>("Materials/CharacterCastShadow");
        if (material == null) return;

        Decompose(Shear, Squash, out float phi, out Vector2 sigma, out float theta);

        var root = new GameObject("CastShadow");
        root.transform.SetParent(character.transform, false);
        // Slightly further from the camera than the ring and contact shadow, so both draw over it.
        root.transform.localPosition = new Vector3(Foot.x, Foot.y, 0.3f);
        root.transform.localRotation = Quaternion.Euler(0f, 0f, phi);

        var scale = new GameObject("Scale");
        scale.transform.SetParent(root.transform, false);
        scale.transform.localScale = new Vector3(sigma.x, sigma.y, 1f);

        var spriteGo = new GameObject("Silhouette", typeof(SpriteRenderer));
        spriteGo.transform.SetParent(scale.transform, false);
        Quaternion rot = Quaternion.Euler(0f, 0f, theta);
        spriteGo.transform.localRotation = rot;
        spriteGo.transform.localPosition = -(rot * new Vector3(Foot.x, Foot.y, 0f));

        var cast = root.AddComponent<CharacterCastShadow>();
        cast.source = character;
        cast.shadow = spriteGo.GetComponent<SpriteRenderer>();
        cast.shadow.sharedMaterial = material;
        cast.shadow.sortingLayerID = character.sortingLayerID;
        cast.block = new MaterialPropertyBlock();
    }

    // M = R(phi) · diag(sigma) · R(theta), angles in degrees.
    private static void Decompose(float shear, float squash, out float phi, out Vector2 sigma, out float theta)
    {
        float a = 1f, b = shear, c = 0f, d = squash;
        float e = (a + d) * 0.5f, f = (a - d) * 0.5f, g = (c + b) * 0.5f, h = (c - b) * 0.5f;
        float q = Mathf.Sqrt(e * e + h * h), r = Mathf.Sqrt(f * f + g * g);
        sigma = new Vector2(q + r, q - r);
        float a1 = Mathf.Atan2(g, f), a2 = Mathf.Atan2(h, e);
        theta = (a2 - a1) * 0.5f * Mathf.Rad2Deg;
        phi = (a2 + a1) * 0.5f * Mathf.Rad2Deg;
    }

    private void LateUpdate()
    {
        if (source == null) { Destroy(gameObject); return; }
        bool visible = source.enabled && source.sprite != null && source.color.a > 0.02f;
        if (shadow.enabled != visible) shadow.enabled = visible;
        if (!visible) return;

        shadow.sprite = source.sprite;
        shadow.flipX = source.flipX;
        shadow.flipY = source.flipY;
        shadow.sortingLayerID = source.sortingLayerID;
        shadow.sortingOrder = source.sortingOrder - 1;
        Color c = shadow.color;
        if (!Mathf.Approximately(c.a, source.color.a)) { c.a = source.color.a; shadow.color = c; }

        if (source.sprite != lastSprite)
        {
            lastSprite = source.sprite;
            Rect rect = lastSprite.textureRect;
            Texture tex = lastSprite.texture;
            shadow.GetPropertyBlock(block);
            block.SetVector(UvRectId, new Vector4(rect.xMin / tex.width, rect.yMin / tex.height, rect.xMax / tex.width, rect.yMax / tex.height));
            block.SetFloat(FootVId, FootV);
            shadow.SetPropertyBlock(block);
        }
    }
}
