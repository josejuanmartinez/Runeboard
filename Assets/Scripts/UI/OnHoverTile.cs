using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class OnHoverTile : MonoBehaviour
{
    private Board board;
    private Hex hex;
    private Vector2Int hexCoordinates; // Store this hex's coordinates
    private static Board sharedBoard;
    private static HexPathRenderer pathRenderer;
    private static bool isRightMouseDown = false;
    private static Vector2Int currentHoverCoordinates = Vector2Int.one * -1;
    private static bool movementCursorDisabled = false;

    void Start()
    {
        // Cached statically and read straight off the Hex component: this used to scan
        // board.hexes (4550 entries) per hex to find "our own" coordinates by gameObject
        // identity — an O(n) search x n hexes, all triggered in the same instant when the
        // board enables every hex's OnHoverTile at once. hex.v2 is the exact same value.
        if (sharedBoard == null) sharedBoard = Board.Instance;
        board = sharedBoard;
        if (board == null)
        {
            Debug.LogError("Board component not found!");
        }

        // Get the Hex component
        hex = GetComponent<Hex>();
        if (hex != null) hexCoordinates = hex.v2;

        // Find or create the path renderer (only once)
        if (pathRenderer == null)
        {
            pathRenderer = FindAnyObjectByType<HexPathRenderer>();
            if (pathRenderer == null)
            {
                GameObject pathRendererObj = new("HexPathRenderer");
                pathRenderer = pathRendererObj.AddComponent<HexPathRenderer>();
            }
        }
    }

    private void OnMouseEnter()
    {
        if (board == null || PopupManager.IsShowing || IsPausedForBannerOrInstructions()) return;

        if (FindAnyObjectByType<Layout>() != null)
        {
            try
            {
                FindAnyObjectByType<Layout>().GetHexNumberManager().Show(hex);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
        

        if (IsPointerOverVisibleUIElement())
        {
            if (hex != null) hex.Unhover();
            return;
        }
        // Highlight this hex
        if (hex != null) hex.Hover();

        // Store current hover coordinates
        currentHoverCoordinates = hexCoordinates;

        // If right mouse is already being held down, update path immediately
        if (board.selectedCharacter != null && board.selectedCharacter.moved < board.selectedCharacter.GetMaxMovement())
        {
            UpdatePathRendering(board);
        }
            
    }

    private void OnMouseExit()
    {
        if (PopupManager.IsShowing || IsPausedForBannerOrInstructions()) return;
        if (IsPointerOverVisibleUIElement())
        {
            if (hex != null) hex.Unhover();
            return;
        }

        // Remove highlight
        if (hex != null) hex.Unhover();

        // Clear hover coordinates if this is the current one
        if (currentHoverCoordinates == hexCoordinates)
        {
            currentHoverCoordinates = Vector2Int.one * -1;
            // Hide path when mouse exits
            if (pathRenderer != null) pathRenderer.HidePath();
        }
    }

    // Static method called from Update in PathManager
    public static void UpdateMouseState(bool rightMouseDown)
    {
        Board board = Board.Instance;

        if (PopupManager.IsShowing || IsPausedForBannerOrInstructions())
        {
            if (pathRenderer) pathRenderer.HidePath();
            ResetMovementCursor();
            return;
        }

        if (IsPointerOverVisibleUIElement() || board.moving)
        {
            if (PopupManager.IsShowing)
            {
                if(pathRenderer) pathRenderer.HidePath();
                ResetMovementCursor();
                return;
            }
            if(pathRenderer) pathRenderer.HidePath();
            if (board)
            {
                Hex hex = board.GetHex(currentHoverCoordinates);
                if (hex) hex.Unhover();
            }
            ResetMovementCursor();
            return;
        }

        // If right mouse button state changed
        if (isRightMouseDown != rightMouseDown)
        {
            isRightMouseDown = rightMouseDown;

            if (!isRightMouseDown)
            {
                // Button released
                if (pathRenderer != null && currentHoverCoordinates != Vector2.one * -1)
                {
                    pathRenderer.HidePath();
                    if (board.selectedCharacter != null && board.selectedCharacter.moved < board.selectedCharacter.GetMaxMovement())
                    {
                        board.Move(board.selectedCharacter, currentHoverCoordinates);
                    }
                    else
                    {
                        pathRenderer.HidePath();
                    }
                }
                ResetMovementCursor();
            }
            else
            {
                // Button pressed down
                if (board.selectedCharacter != null && board.selectedCharacter.moved < board.selectedCharacter.GetMaxMovement())
                {
                    UpdatePathRendering(board);
                }
                else
                {
                    pathRenderer.HidePath();
                    if (board.selectedCharacter != null && board.selectedCharacter.moved >= board.selectedCharacter.GetMaxMovement())
                    {
                        CursorManager.Instance?.SetDisableCursor();
                        movementCursorDisabled = true;
                    }
                }
            }
        }
    }

    // Static method to handle path rendering
    private static void UpdatePathRendering(Board board)
    {
        if (IsPointerOverVisibleUIElement())
        {
            Debug.Log("IGNORING HEX");
            pathRenderer.HidePath();
            return;
        }

        if (pathRenderer == null) return;

        if (board == null) return;

        if (board.selectedCharacter == null) return;

        // Draw path only if right mouse is down, we have a hover position, and selected hex
        if (isRightMouseDown &&
            currentHoverCoordinates != Vector2.one * -1 &&
            board.selectedHex != Vector2.one * -1 &&
            board.selectedHex != currentHoverCoordinates)
        {
            pathRenderer.DrawPathBetweenHexes(
                board.selectedHex, 
                currentHoverCoordinates,
                board.selectedCharacter);
        }
        else
        {
            pathRenderer.HidePath();
        }
    }
    private static void ResetMovementCursor()
    {
        if (movementCursorDisabled)
        {
            CursorManager.Instance?.SetDefaultCursor();
            movementCursorDisabled = false;
        }
    }

    // The turn banner (which covers both "TURN X" and the Gathering Resources banner that
    // follows it) and the game-start onboarding instructions both mean the game is meant to
    // be fully paused — no hex hover, path preview, or movement should go through.
    private static bool IsPausedForBannerOrInstructions()
    {
        return TurnBanner.IsShowing || TutorialInstructionsManager.Instance.IsShowing;
    }

    private static bool IsPointerOverVisibleUIElement()
    {
        if (EventSystem.current == null)
            return false;

        // Set up the new Pointer Event
        PointerEventData eventData = new(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new();

        // Raycast using the Graphics Raycaster and the Event Data
        EventSystem.current.RaycastAll(eventData, results);

        // Only return true if we hit a visible UI element (not just the Canvas)
        foreach (var result in results)
        {
            // Skip the Canvas itself
            if (result.gameObject.GetComponent<Canvas>() != null)
                continue;

            // Check if it's an Image with non-zero alpha
            Image image = result.gameObject.GetComponent<Image>();
            if (image != null && image.color.a > 0.01f && image.raycastTarget)
                return true;

            // Check if it's Text with non-zero alpha
            TMPro.TextMeshProUGUI tmpText = result.gameObject.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmpText != null && tmpText.color.a > 0.01f)
                return true;
        }

        return false;
    }
}
