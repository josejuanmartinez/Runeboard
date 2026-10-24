using System.Collections.Generic;
using UnityEngine;

// A unit's token for the strategic zoom (see Character3DView): zoomed far out, a live 3D figure is
// too small to read, so it dithers away and a round medallion of the unit's card illustration in a
// nation-coloured ring (RetroLOTR/UnitToken) stands on its miniature base instead, like a counter
// in a board game. Character3DSettings.tokenZoom sets where the cross-fade starts and ends.
public class UnitToken : MonoBehaviour
{
    private static readonly int UVRectId = Shader.PropertyToID("_UVRect");
    private static readonly int RingColorId = Shader.PropertyToID("_RingColor");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    private static readonly Dictionary<Texture, Material> materials = new();
    private static Mesh quad;
    private static Illustrations illustrations;
    private static float nextIllustrationsLookup;

    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock block;
    private Character shownFor;
    private bool hasArt;

    /// <summary>How far the view has turned into tokens: 0 = full 3D figures, 1 = tokens only.</summary>
    public static float Amount(Character3DSettings settings)
    {
        Camera cam = Camera.main;
        if (settings.tokenShader == null || cam == null) return 0f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(settings.tokenZoom.x, settings.tokenZoom.y, cam.orthographicSize));
    }

    public static UnitToken Create(Transform parent)
    {
        var go = new GameObject("UnitToken", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = Quad();
        var token = go.AddComponent<UnitToken>();
        token.meshRenderer = go.GetComponent<MeshRenderer>();
        token.meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        token.meshRenderer.receiveShadows = false;
        token.block = new MaterialPropertyBlock();
        return token;
    }

    // Stands the medallion on the base: diameter a little wider than the base, bottom at the feet.
    public void Show(Character character, Color nation, Vector3 feet, float baseRadius, float alpha,
        int sortingLayerId, int sortingOrder, Character3DSettings settings)
    {
        if (character != shownFor || !hasArt) // retried until the illustrations have loaded
        {
            shownFor = character;
            hasArt = Bind(character, settings);
        }
        bool visible = hasArt && alpha > 0.01f && baseRadius > 0f;
        if (meshRenderer.enabled != visible) meshRenderer.enabled = visible;
        if (!visible) return;

        float diameter = baseRadius * 2.2f;
        transform.position = feet + Vector3.up * (diameter * 0.5f) + Vector3.back * 0.01f;
        transform.rotation = Quaternion.identity;
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(diameter / parentScale.x, diameter / parentScale.y, 1f);

        meshRenderer.sortingLayerID = sortingLayerId;
        meshRenderer.sortingOrder = sortingOrder;
        meshRenderer.GetPropertyBlock(block);
        block.SetColor(RingColorId, nation.a > 0f ? nation : new Color(0.36f, 0.33f, 0.28f));
        block.SetFloat(AlphaId, alpha);
        meshRenderer.SetPropertyBlock(block);
    }

    public void Hide()
    {
        if (meshRenderer != null && meshRenderer.enabled) meshRenderer.enabled = false;
    }

    private bool Bind(Character character, Character3DSettings settings)
    {
        Sprite art = Illustration(character);
        if (art == null || art.texture == null) return false;

        if (!materials.TryGetValue(art.texture, out Material material) || material == null)
        {
            material = new Material(settings.tokenShader) { name = $"UnitToken {art.texture.name}", mainTexture = art.texture };
            materials[art.texture] = material;
        }
        meshRenderer.sharedMaterial = material;

        // A square around the face: card art puts it in the upper part, horizontally centred.
        Rect rect = art.textureRect;
        float side = Mathf.Min(rect.width, rect.height * 0.62f);
        float x = rect.center.x - side * 0.5f;
        float y = rect.yMax - side - rect.height * 0.07f;
        float w = art.texture.width, h = art.texture.height;
        meshRenderer.GetPropertyBlock(block);
        block.SetVector(UVRectId, new Vector4(x / w, y / h, side / w, side / h));
        meshRenderer.SetPropertyBlock(block);
        return true;
    }

    private static Sprite Illustration(Character character)
    {
        if (character == null) return null;
        if (illustrations == null && Time.unscaledTime >= nextIllustrationsLookup)
        {
            nextIllustrationsLookup = Time.unscaledTime + 2f;
            illustrations = FindAnyObjectByType<Illustrations>();
        }
        return illustrations != null ? illustrations.GetIllustrationByName(character) : null;
    }

    private static Mesh Quad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "UnitTokenQuad" };
        quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) });
        quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
        quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        quad.RecalculateBounds();
        return quad;
    }
}
