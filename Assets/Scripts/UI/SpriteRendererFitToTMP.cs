using TMPro;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteRendererFitToTMP : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Vector2 padding = new(0.2f, 0.1f);
    [SerializeField] private bool growRight = true;
    [SerializeField] private bool drawBackground = true;
    [Tooltip("Optional: kept in sync with the fitted sprite size, so a hover collider on the same object tracks the text width instead of using a fixed size.")]
    [SerializeField] private BoxCollider2D hoverCollider;

    // Sprite's initial localPosition.x in parent space — the fixed left-edge anchor.
    [SerializeField] private bool anchorSet;
    [SerializeField] private float anchorInParent;

    private SpriteRenderer _sr;
    private bool settlementPresentation;
    private static Sprite settlementBandSprite;
    private static Material settlementBandMaterial;

    public void ConfigureSettlementPresentation()
    {
        settlementPresentation = true;
        // A softly feathered backing keeps small lettering readable against detailed terrain.
        drawBackground = true;
        growRight = false;
        padding = new Vector2(.08f, .05f);
        if (label != null)
        {
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0, -.23f);
            rect.sizeDelta = new Vector2(.94f, .3f);
            label.alignment = TextAlignmentOptions.Top;
            label.enableAutoSizing = true;
            label.fontSizeMin = .7f;
            label.fontSizeMax = 1.15f;
            label.lineSpacing = 0;
            label.extraPadding = true;
            label.fontStyle = FontStyles.Normal;
            label.enableVertexGradient = false;
            label.color = new Color(.94f, .92f, .84f);
            // Outline goes straight onto the instanced font material: TMP's outlineColor/outlineWidth
            // setters route through renderer.material, which leaks a material per call in edit mode.
            var fontMaterial = label.fontMaterial;
            fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
            fontMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            fontMaterial.DisableKeyword("UNDERLAY_ON");
            fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.035f, .04f, .035f, 1f));
            fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, .14f);
            label.UpdateMeshPadding();
            label.SetAllDirty();
        }
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        EnsureSettlementBand();
        _sr.sprite = settlementBandSprite;
        _sr.sharedMaterial = settlementBandMaterial;
        _sr.color = new Color(.045f, .055f, .063f, .88f);
        Fit();
    }

    private static void EnsureSettlementBand()
    {
        if (settlementBandSprite != null && settlementBandMaterial != null) return;
        var texture = new Texture2D(64, 16, TextureFormat.RGBA32, false)
        {
            name = "Settlement label fade", hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        var pixels = new Color[64 * 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 64; x++)
            {
                float edgeX = Mathf.SmoothStep(0, 1, Mathf.Min(x, 63 - x) / 5f);
                float edgeY = Mathf.SmoothStep(0, 1, Mathf.Min(y, 15 - y) / 3f);
                pixels[y * 64 + x] = new Color(1, 1, 1, edgeX * edgeY);
            }
        texture.SetPixels(pixels); texture.Apply(false, true);
        settlementBandSprite = Sprite.Create(texture, new Rect(0, 0, 64, 16), new Vector2(.5f, .5f), 64, 0, SpriteMeshType.FullRect);
        settlementBandSprite.hideFlags = HideFlags.HideAndDontSave;
        settlementBandMaterial = new Material(Shader.Find("Sprites/Default"))
        { name = "Settlement label backing", hideFlags = HideFlags.HideAndDontSave };
    }

    private void Awake() => _sr = GetComponent<SpriteRenderer>();
    private void OnEnable() => Fit();

#if UNITY_EDITOR
    private void Update() => Fit();

    [ContextMenu("Reset Anchor")]
    private void ResetAnchor() => anchorSet = false;
#endif

    public void Fit()
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (label == null) return;

        if (!label.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(label.text))
        {
            _sr.enabled = false;
            if (hoverCollider != null) hoverCollider.enabled = false;
            return;
        }

        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = settlementPresentation ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;

        // preferredWidth is position-independent — avoids feedback loops from textBounds.
        if (settlementPresentation) label.ForceMeshUpdate();
        float textWidth  = settlementPresentation ? label.textBounds.size.x : label.preferredWidth;
        float textHeight = settlementPresentation ? label.textBounds.size.y : label.preferredHeight;
        if (textWidth < 0.001f) { _sr.enabled = false; return; }

        Vector3 textScale = label.transform.lossyScale;
        Vector3 bandScale = transform.lossyScale;
        float newWidth = textWidth * (settlementPresentation ? Mathf.Abs(textScale.x) / Mathf.Max(.0001f, Mathf.Abs(bandScale.x)) : 1f) + padding.x;
        float newHeight = textHeight * (settlementPresentation ? Mathf.Abs(textScale.y) / Mathf.Max(.0001f, Mathf.Abs(bandScale.y)) : 1f) + padding.y;

        _sr.drawMode = SpriteDrawMode.Sliced;
        _sr.size = new Vector2(newWidth, newHeight);
        _sr.enabled = drawBackground;
        if (hoverCollider != null)
        {
            hoverCollider.size = _sr.size;
            hoverCollider.enabled = label.gameObject.activeInHierarchy;
        }

        if (settlementPresentation)
        {
            label.ForceMeshUpdate();
            transform.position = label.transform.TransformPoint(label.textBounds.center);
        }

        if (!growRight) return;

        // Anchor = sprite left edge in parent space, captured once.
        if (!anchorSet)
        {
            anchorInParent = transform.localPosition.x - newWidth * 0.5f;
            anchorSet = true;
        }

        // Sprite grows rightward; left edge stays fixed at anchorInParent.
        Vector3 pos = transform.localPosition;
        pos.x = anchorInParent + newWidth * 0.5f;
        transform.localPosition = pos;

        // Label pivot.x = 0 (left-center pivot), so label.localPosition.x IS where text starts
        // in sprite-local space. Place it at sprite-left + pad/2.
        Vector3 labelPos = label.transform.localPosition;
        labelPos.x = -newWidth * 0.5f + padding.x * 0.5f;
        label.transform.localPosition = labelPos;
    }
}
