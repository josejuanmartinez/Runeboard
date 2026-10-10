using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Makes the HUD minimap interactive: click (or drag) on it to move the board camera there.
/// Attached at runtime to the RawImage that displays the minimap camera's render texture.
/// </summary>
public class MinimapNavigator : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    private RawImage image;
    private Camera minimapCamera;
    private MinimapManager manager;

    public static void Attach()
    {
        MinimapManager manager = FindFirstObjectByType<MinimapManager>();
        Camera cam = manager != null ? manager.minimapCamera : null;
        if (cam == null || cam.targetTexture == null) return;
        foreach (RawImage raw in FindObjectsByType<RawImage>(FindObjectsSortMode.None))
        {
            if (raw.texture != cam.targetTexture || raw.GetComponent<MinimapNavigator>() != null) continue;
            var nav = raw.gameObject.AddComponent<MinimapNavigator>();
            nav.image = raw;
            nav.minimapCamera = cam;
            raw.raycastTarget = true;
            nav.manager = manager;
            // The image's old Button opened the full-screen overview on every click; that moves
            // to double-click so a single click can jump the camera.
            if (raw.TryGetComponent(out Button button)) button.enabled = false;
        }
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.clickCount >= 2 && manager != null) { manager.ToggleMinimapOverview(); return; }
        Jump(e);
    }
    public void OnDrag(PointerEventData e) => Jump(e);

    private void Jump(PointerEventData e)
    {
        if (image == null || minimapCamera == null || BoardNavigator.Instance == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(image.rectTransform, e.position, e.pressEventCamera, out Vector2 local)) return;
        Rect r = image.rectTransform.rect;
        Rect uv = image.uvRect;
        Vector2 n = new((local.x - r.x) / r.width, (local.y - r.y) / r.height);
        Vector2 viewport = new(uv.x + n.x * uv.width, uv.y + n.y * uv.height);
        Vector3 world = minimapCamera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, 0f));
        BoardNavigator.Instance.LookAt(new Vector3(world.x, world.y, 0f), e.dragging ? 0.05f : 0.35f);
    }
}
