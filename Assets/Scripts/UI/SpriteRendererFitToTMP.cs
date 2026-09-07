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

        if (string.IsNullOrWhiteSpace(label.text))
        {
            _sr.enabled = false;
            return;
        }

        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;

        // preferredWidth is position-independent — avoids feedback loops from textBounds.
        float textWidth  = label.preferredWidth;
        float textHeight = label.preferredHeight;
        if (textWidth < 0.001f) { _sr.enabled = false; return; }

        float newWidth  = textWidth  + padding.x;
        float newHeight = textHeight + padding.y;

        _sr.drawMode = SpriteDrawMode.Sliced;
        _sr.size = new Vector2(newWidth, newHeight);
        _sr.enabled = drawBackground;
        if (hoverCollider != null) hoverCollider.size = _sr.size;

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
