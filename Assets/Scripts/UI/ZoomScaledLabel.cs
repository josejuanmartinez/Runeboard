using UnityEngine;

/// <summary>
/// Civ-style world label sizing: board labels (place names, objective markers) grow as the camera
/// zooms out and shrink as it zooms in, so they stay readable at any zoom instead of tracking the
/// terrain 1:1. The response is softened (exponent below 1) so zooming still changes their size a
/// little, which keeps the sense of depth. Scales around <see cref="pivot"/> (local position of the
/// label's anchor) so the label stays attached to its spot on the board.
/// </summary>
public class ZoomScaledLabel : MonoBehaviour
{
    public const float ReferenceZoom = 6.2f;   // BoardLook.DefaultZoom: labels are authored for it
    private const float Response = 0.7f;
    private const float MinScale = 0.75f;
    private const float MaxScale = 1.8f;

    public Vector3 pivot;
    private Vector3 baseScale;
    private Vector3 basePosition;
    private float applied = -1f;

    private static int factorFrame = -1;
    private static float factor = 1f;

    /// <summary>Current label scale factor for the board camera's zoom.</summary>
    public static float Factor
    {
        get
        {
            if (factorFrame == Time.frameCount) return factor;
            factorFrame = Time.frameCount;
            Camera cam = Camera.main;
            float zoom = cam != null && cam.orthographic ? cam.orthographicSize : ReferenceZoom;
            factor = Mathf.Clamp(Mathf.Pow(zoom / ReferenceZoom, Response), MinScale, MaxScale);
            return factor;
        }
    }

    public static ZoomScaledLabel Attach(GameObject go, Vector3 pivot)
    {
        var label = go.GetOrAddComponent<ZoomScaledLabel>();
        label.pivot = pivot;
        return label;
    }

    private void Awake()
    {
        baseScale = transform.localScale;
        basePosition = transform.localPosition;
    }

    private void LateUpdate()
    {
        float f = Factor;
        if (Mathf.Abs(f - applied) < 0.002f) return;
        applied = f;
        transform.localScale = baseScale * f;
        // Keep the pivot point fixed: p' = pivot + (p - pivot) * f, applied to the root's origin.
        transform.localPosition = basePosition + (pivot - basePosition) * (1f - f);
    }
}
