using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Image))]
public class ImageFitToTMP : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Vector2 padding = new(0.2f, 0.1f);
    [SerializeField] private bool growRight = true;

    // Image's initial anchoredPosition.x in parent space — the fixed left-edge anchor.
    [SerializeField] private bool anchorSet;
    [SerializeField] private float anchorInParent;

    private Image _image;
    private RectTransform _rt;
    // Decoration drawn on top of (or instead of) the fitted Image itself - e.g. the message
    // widget's "Polish Surface" RuneboardPanel child, which is what actually paints the pill
    // now that the Image is only a sizing rect. These have to follow the Image's own
    // enabled state, otherwise an empty label leaves a naked background floating on screen.
    private Graphic[] _decorations;

    private void Awake()
    {
        _image = GetComponent<Image>();
        _rt = _image.rectTransform;
        CacheDecorations();
    }

    private void OnEnable() => Fit();

    private void CacheDecorations()
    {
        Transform labelTransform = label != null ? label.transform : null;
        _decorations = GetComponentsInChildren<Graphic>(true)
            .Where(g => g != _image && (labelTransform == null || !g.transform.IsChildOf(labelTransform)))
            .ToArray();
    }

    // Single place that decides whether the backing plate is drawn at all, so the Image and
    // every decorative child can never disagree.
    private void SetBackgroundVisible(bool visible)
    {
        if (_image != null) _image.enabled = visible;
        if (_decorations == null) CacheDecorations();
        for (int i = 0; i < _decorations.Length; i++)
        {
            if (_decorations[i] != null) _decorations[i].enabled = visible;
        }
    }

#if UNITY_EDITOR
    private void Update() => Fit();

    [ContextMenu("Reset Anchor")]
    private void ResetAnchor() => anchorSet = false;
#endif

    public void Fit()
    {
        if (_image == null) _image = GetComponent<Image>();
        if (_rt == null) _rt = _image.rectTransform;
        if (label == null) return;

        if (string.IsNullOrWhiteSpace(label.text))
        {
            SetBackgroundVisible(false);
            return;
        }

        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;

        // preferredWidth is position-independent — avoids feedback loops from textBounds.
        float textWidth  = label.preferredWidth;
        float textHeight = label.preferredHeight;
        if (textWidth < 0.001f) { SetBackgroundVisible(false); return; }

        float newWidth  = textWidth  + padding.x;
        float newHeight = textHeight + padding.y;

        _image.type = Image.Type.Sliced;
        _rt.sizeDelta = new Vector2(newWidth, newHeight);
        SetBackgroundVisible(true);

        if (!growRight) return;

        // Anchor = image left edge in parent space, captured once.
        if (!anchorSet)
        {
            anchorInParent = _rt.anchoredPosition.x - newWidth * 0.5f;
            anchorSet = true;
        }

        // Image grows rightward; left edge stays fixed at anchorInParent.
        Vector2 pos = _rt.anchoredPosition;
        pos.x = anchorInParent + newWidth * 0.5f;
        _rt.anchoredPosition = pos;

        // Label pivot.x = 0 (left-center pivot), so label.anchoredPosition.x IS where text
        // starts in image-local space. Place it at image-left + pad/2.
        RectTransform labelRt = label.rectTransform;
        Vector2 labelPos = labelRt.anchoredPosition;
        labelPos.x = -newWidth * 0.5f + padding.x * 0.5f;
        labelRt.anchoredPosition = labelPos;
    }
}
