using UnityEngine;
using UnityEngine.EventSystems;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    public Texture2D clickableCursor;
    public Texture2D draggableCursor;
    public Texture2D defaultCursor;
    public Texture2D waitingCursor;
    public Texture2D disableCursor;
    public Vector2 clickableHotSpot = Vector2.zero;
    public Vector2 draggableHotSpot = Vector2.zero;
    public Vector2 defaultHotSpot = Vector2.zero;
    public Vector2 waitingHotSpot = Vector2.zero;
    public Vector2 disableHotSpot = Vector2.zero;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void SetDraggableCursor()
    {
        if (draggableCursor != null)
            Cursor.SetCursor(draggableCursor, draggableHotSpot, CursorMode.Auto);
    }

    public void SetClickableCursor()
    {
        if (clickableCursor != null)
            Cursor.SetCursor(clickableCursor, clickableHotSpot, CursorMode.Auto);
    }

    public void SetDisableCursor()
    {
        if (disableCursor != null)
            Cursor.SetCursor(disableCursor, disableHotSpot, CursorMode.Auto);
    }

    public void SetWaitingCursor()
    {
        if (waitingCursor != null)
            Cursor.SetCursor(waitingCursor, waitingHotSpot, CursorMode.Auto);
    }

    public void SetDefaultCursor()
    {
        if (defaultCursor != null)
            Cursor.SetCursor(defaultCursor, defaultHotSpot, CursorMode.Auto);
        else
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
