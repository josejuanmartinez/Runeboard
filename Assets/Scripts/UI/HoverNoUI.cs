using System;
using TMPro;
using UnityEngine;

public class HoverNoUI : MonoBehaviour
{
    [Header("Tooltip (World-space GameObject)")]
    public GameObject tooltipPanel;      // a normal GameObject, not UI
    public TextMeshPro textWidget;       // 3D TextMeshPro, not UGUI
    public Vector3 offset = new(0, 1.5f, 0);
    public bool mark = true;

    [Header("Raycast")]
    public Camera rayCamera;             // defaults to Camera.main
    public LayerMask hoverMask = ~0;

    private bool wasHovering;
    const string markStart = "<mark color=#ffffff>";
    const string markEnd = "</mark>";
    void Awake()
    {
        if (rayCamera == null) rayCamera = Camera.main;
        tooltipPanel.SetActive(false);
    }

    private void OnDisable()
    {
        HideTooltip();
    }

    private void OnDestroy()
    {
        HideTooltip();
    }

    void Update()
    {
        if (rayCamera == null) rayCamera = Camera.main;

        if (textWidget == null || string.IsNullOrWhiteSpace(textWidget.text))
        {
            HideTooltip();
            return;
        }

        bool isHovering = IsMouseOverThis();
        // if (isHovering) Debug.Log($"Hovering over {gameObject.transform.parent.name}->{transform.name}");

        // Handle show/hide transitions
        if (isHovering && !wasHovering)
        {
            tooltipPanel.SetActive(true);
        }
        else if (!isHovering && wasHovering)
        {
            tooltipPanel.SetActive(false);
        }

        // Update tooltip position/orientation
        if (isHovering && tooltipPanel.activeSelf)
        {
            tooltipPanel.transform.position = transform.position + offset;
            /*tooltipPanel.transform.rotation = Quaternion.LookRotation(
                tooltipPanel.transform.position - rayCamera.transform.position
            );*/
        }

        wasHovering = isHovering;
    }

    public void Initialize(string text, float fontSize = 2f)
    {
        bool textNull = string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text);
        if (textWidget == null)
        {
            HideTooltip();
            return;
        }

        if (textNull)
        {
            textWidget.text = string.Empty;
            HideTooltip();
            return;
        }

        string markStartStr = mark ? markStart : "";
        string markEndStr = mark ? markEnd : "";
        textWidget.text = $"{markStartStr}{text}{markEndStr}";
        textWidget.fontSize = fontSize;
    }

    public void HideTooltip()
    {
        wasHovering = false;
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(false);
        }
    }

    private bool IsMouseOverThis()
    {
        if (rayCamera == null) return false;
        
        try
        {
            var sp = Input.mousePosition;

            if (!rayCamera.pixelRect.Contains(new Vector2(sp.x, sp.y)))
                return false;

            // Get the mouse position in world space (XY for 2D)
            // On the board plane: right for the straight-down camera and the tilted one alike.
            Vector3 mouseWorld = BoardNavigator.ScreenToBoard(rayCamera, Input.mousePosition);
            Vector2 point = (Vector2)mouseWorld;

            // 2D colliders: get ALL colliders at this point
            if (TryGetComponent(out Collider2D col2d))
            {
                var hits = Physics2D.OverlapPointAll(point, hoverMask);
                for (int i = 0; i < hits.Length; i++)
                    if (hits[i] == col2d) return true;
            }
        } catch (Exception) { }

        return false;
    }
}
