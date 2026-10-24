using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class CameraCurtainScaler : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool followCamera = true;
    [SerializeField] private bool preserveZPosition = true;
    [SerializeField] private float overscan = 1.05f;

    private SpriteRenderer spriteRenderer;
    private Sprite lastSprite;
    private float lastOrthoSize = -1f;
    private float lastAspect = -1f;
    private Vector3 lastCameraPosition;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        Refresh();
    }

    private void OnEnable()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        Refresh();
    }

    private void LateUpdate()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        Camera cam = GetTargetCamera();
        if (cam == null || spriteRenderer == null || spriteRenderer.sprite == null) return;

        bool cameraChanged = !Mathf.Approximately(cam.orthographicSize, lastOrthoSize)
            || !Mathf.Approximately(cam.aspect, lastAspect)
            || cam.transform.position != lastCameraPosition;
        bool spriteChanged = spriteRenderer.sprite != lastSprite;

        if (cameraChanged || spriteChanged)
        {
            Refresh();
        }
    }

    private void OnValidate()
    {
        overscan = Mathf.Max(1f, overscan);

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        Refresh();
    }

    public void Refresh()
    {
        Camera cam = GetTargetCamera();
        if (cam == null || spriteRenderer == null || spriteRenderer.sprite == null) return;

        Vector2 spriteWorldSize = GetSpriteWorldSize(spriteRenderer.sprite);
        if (spriteWorldSize.x <= 0.0001f || spriteWorldSize.y <= 0.0001f) return;

        // The visible board area: the orthographic rectangle, or — with the board camera tilted
        // into perspective (BoardNavigator) — the bounds of where the screen corners meet the board.
        Vector2 centre = cam.transform.position;
        float visibleHeight = cam.orthographicSize * 2f;
        float visibleWidth = visibleHeight * cam.aspect;
        if (!cam.orthographic)
        {
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            foreach (Vector2 corner in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
            {
                Vector3 p = BoardNavigator.ScreenToBoard(cam, new Vector3(corner.x * cam.pixelWidth, corner.y * cam.pixelHeight, 0f));
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            centre = (min + max) * 0.5f;
            visibleWidth = max.x - min.x;
            visibleHeight = max.y - min.y;
        }
        float scale = Mathf.Max(visibleWidth / spriteWorldSize.x, visibleHeight / spriteWorldSize.y) * overscan;

        transform.localScale = new Vector3(scale, scale, transform.localScale.z);

        if (followCamera)
        {
            float z = preserveZPosition ? transform.position.z : cam.transform.position.z;
            if (!cam.orthographic)
            {
                // A child of the camera, but it must stay flat on the board plane behind the board:
                // left to tilt and pull back with the camera it ends up close to the lens and fills
                // the view with a magnified piece of its picture.
                z = 0f;
                transform.rotation = Quaternion.identity;
            }
            else if (transform.parent == cam.transform)
            {
                transform.localRotation = Quaternion.identity;
            }
            transform.position = new Vector3(centre.x, centre.y, z);
        }

        lastSprite = spriteRenderer.sprite;
        lastOrthoSize = cam.orthographicSize;
        lastAspect = cam.aspect;
        lastCameraPosition = cam.transform.position;
    }

    private Camera GetTargetCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        return targetCamera;
    }

    private static Vector2 GetSpriteWorldSize(Sprite sprite)
    {
        Rect rect = sprite.rect;
        float pixelsPerUnit = Mathf.Max(0.0001f, sprite.pixelsPerUnit);
        return new Vector2(rect.width / pixelsPerUnit, rect.height / pixelsPerUnit);
    }
}
