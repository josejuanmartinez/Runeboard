using TMPro;
using UnityEngine;

// UI replacement for the old world-space HexHoverPanel (a SpriteRenderer + world-space
// TextMeshPro prefab instantiated per hex). A single instance of this lives in the scene's UI
// canvas — Board holds the reference (see Board.UIHover) and every Hex shares it. It's a
// fixed/"sticky" UI element: it sits wherever it's placed in the canvas and is only
// shown/hidden/filled with text, never repositioned.
public class HexUIHover : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("Root of the info panel (background + text). Toggled on/off by Show()/Hide().")]
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private TextMeshProUGUI infoText;

    public bool IsShown => panelRoot != null && panelRoot.gameObject.activeSelf;

    private Canvas canvas;

    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        if (FontManager.Instance != null) FontManager.Instance.ApplyCurrentFont(infoText);
        Hide();
    }

    private Camera ResolveCanvasCamera()
    {
        if (canvas == null) return null;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    public void SetText(string text)
    {
        if (infoText != null) infoText.text = text ?? string.Empty;
    }

    public void Show()
    {
        if (panelRoot != null) panelRoot.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.gameObject.SetActive(false);
    }

    // Mirrors the old IsMouseOverSprites bounds check, but against the panel's UI rect.
    public bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!IsShown) return false;
        Camera cam = ResolveCanvasCamera();
        return panelRoot != null && RectTransformUtility.RectangleContainsScreenPoint(panelRoot, screenPoint, cam);
    }
}
