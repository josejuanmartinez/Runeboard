using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class KeyManager : MonoBehaviour
{
    public Board board;
    public HexPathRenderer pathRenderer;
    public BoardNavigator boardNavigator;
    public float keyboardMoveSpeed = 10f;

    private Game game;
    private ActionsManager actionsManager;
    private List<(KeyCode key, char letter)> actionHotkeys;

    private void Start()
    {
        if(!board) board = Board.Instance;
        if(!game) game = Game.Instance;
        if(!pathRenderer) pathRenderer = FindAnyObjectByType<HexPathRenderer>();
        if(!boardNavigator) boardNavigator = FindAnyObjectByType<BoardNavigator>();
        if(!actionsManager) actionsManager = ActionsManager.Instance;
        BuildActionHotkeys();
    }
    void Update()
    {
        if (BoardNavigator.IsNavigationInputLocked()) return;

        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (ctrlHeld && shiftHeld && Input.GetKeyDown(KeyCode.Tab))
        {
            game?.TogglePlayerAutoplay();
            return;
        }

        if (ctrlHeld && shiftHeld)
        {
            Debug.Log("CTRL+Shift shortcuts: CTRL+Shift+Tab toggle autoplay, CTRL+Shift+R reveal map, CTRL+Shift+M reset movement, CTRL+Shift+S +100 stores, CTRL+Shift+A list artifacts");
        }

        if (ctrlHeld && shiftHeld && Input.GetKeyDown(KeyCode.R))
        {
            RevealEntireMap();
            return;
        }

        if (ctrlHeld && shiftHeld && Input.GetKeyDown(KeyCode.M))
        {
            ResetAllMovement();
            return;
        }

        if (ctrlHeld && shiftHeld && Input.GetKeyDown(KeyCode.S))
        {
            BoostPlayerStores();
            return;
        }

        if (ctrlHeld && shiftHeld && Input.GetKeyDown(KeyCode.A))
        {
            LogAllArtifacts();
            return;
        }

        // Check if ESC key was pressed
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (SituationCardsUI.Instance != null && SituationCardsUI.Instance.TryDismissActiveBloom())
                return;

            CardBloomWheel cardBloom = DeckManager.Instance?.GetCardBloomWheel();
            if (cardBloom != null && cardBloom.TryClose())
                return;

            if (game != null && game.started)
            {
                PauseMenuController.Toggle();
                return;
            }

            if (PopupManager.IsShowing)
            {
                PopupManager.HidePopup();
                return;
            }

            if (board != null)
            {
                board.UnselectHex();
                SituationCardsUI.Instance?.RefreshBloomForCharacterSelection(null);
                return;
            }

            // Hide the path
            if (pathRenderer != null)
            {
                pathRenderer.HidePath();
            }

        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (board != null && game != null)
            {
                game.SelectNextCharacterInPriorityCycle();
                return;
            }

            if (pathRenderer != null)
                pathRenderer.HidePath();
        }

        HandleCharacterMovementHotkeys(ctrlHeld, shiftHeld);
        HandleKeyboardCameraMovement();
    }

    private void RevealEntireMap()
    {
        if (!board) board = Board.Instance;
        if (!game) game = Game.Instance;

        if (board == null || board.hexes == null)
        {
            Debug.LogWarning("Cannot reveal map: board or hexes are missing.");
            return;
        }

        Leader playerLeader = game != null ? game.player : null;
        
        foreach (Hex hex in board.hexes.Values)
        {
            if (hex != null) {
                if(hex.HasAnyPC() && !hex.IsPCRevealed()) hex.RevealPC();
                hex.Reveal(playerLeader);
            }

        }

        MinimapManager.RefreshMinimap();
        Debug.Log("CTRL+Shift+R -> Revealed the entire map");
    }

    private void ResetAllMovement()
    {
        Character[] characters = FindObjectsByType<Character>();
        foreach (Character character in characters)
        {
            if (character == null || character.killed) continue;
            character.moved = 0;
        }
        Debug.Log("CTRL+Shift+M -> Reset movement for all characters");
    }

    private void BoostPlayerStores(int amount = 100)
    {
        if (!game) game = Game.Instance;
        var player = game != null ? game.player : null;
        if (player == null)
        {
            Debug.LogWarning("Cannot boost stores: player not found.");
            return;
        }

        player.leatherAmount += amount;
        player.mountsAmount += amount;
        player.timberAmount += amount;
        player.ironAmount += amount;
        player.steelAmount += amount;
        player.mithrilAmount += amount;
        player.goldAmount += amount;

        var storesManager = FindAnyObjectByType<StoresManager>();
        if (storesManager != null) storesManager.RefreshStores();

        Debug.Log($"CTRL+Shift+S -> Added {amount} to all player stores");
    }

    private void LogAllArtifacts()
    {
        List<string> entries = new();

        foreach (var c in FindObjectsByType<Character>())
        {
            if (c == null || c.objects == null || c.objects.Count == 0) continue;
            string ownerName = string.IsNullOrWhiteSpace(c.characterName) ? c.name : c.characterName;
            foreach (var art in c.objects)
            {
                if (art == null) continue;
                entries.Add($"{art.name} held by {ownerName}");
            }
        }

        foreach (var hex in FindObjectsByType<Hex>())
        {
            if (hex == null || hex.hiddenObjects == null || hex.hiddenObjects.Count == 0) continue;
            string hexLabel = hex.GetHoverV2();
            foreach (var art in hex.hiddenObjects)
            {
                if (art == null) continue;
                entries.Add($"{art.name} hidden at {hexLabel}");
            }
        }

        if (entries.Count == 0)
        {
            Debug.Log("CTRL+Shift+A -> No artifacts found");
        }
        else
        {
            Debug.Log("CTRL+Shift+A -> Artifacts:\n" + string.Join("\n", entries));
        }
    }

    private void HandleKeyboardCameraMovement()
    {
        if (boardNavigator == null) return;
        if (BoardNavigator.IsNavigationInputLocked()) return;
        if (BoardNavigator.IsPointerOverVisibleUIElement()) return;

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += Vector3.up;
        if (Input.GetKey(KeyCode.S)) move += Vector3.down;
        if (Input.GetKey(KeyCode.A)) move += Vector3.left;
        if (Input.GetKey(KeyCode.D)) move += Vector3.right;

        if (move.sqrMagnitude > 0f)
        {
            move.Normalize();
            boardNavigator.Pan(move * keyboardMoveSpeed * Time.deltaTime);
        }
    }

    private void HandleCharacterMovementHotkeys(bool ctrlHeld, bool shiftHeld)
    {
        if (ctrlHeld || shiftHeld) return;
        if (!board) board = Board.Instance;
        if (board == null || board.selectedCharacter == null || board.selectedCharacter.hex == null) return;
        if (board.moving) return;

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Keypad4))
        {
            MoveSelectedCharacter(HexMoveDirection.Left);
            return;
        }

        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Keypad6))
        {
            MoveSelectedCharacter(HexMoveDirection.Right);
            return;
        }

        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Keypad7))
        {
            MoveSelectedCharacter(HexMoveDirection.UpLeft);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Keypad9))
        {
            MoveSelectedCharacter(HexMoveDirection.UpRight);
            return;
        }

        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            MoveSelectedCharacter(HexMoveDirection.DownLeft);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Keypad3))
        {
            MoveSelectedCharacter(HexMoveDirection.DownRight);
        }
    }

    private void BuildActionHotkeys()
    {
        actionHotkeys = new List<(KeyCode key, char letter)>();
        if (ActionsManager.ActionHotkeyLetters == null) return;

        foreach (char letter in ActionsManager.ActionHotkeyLetters)
        {
            if (Enum.TryParse(letter.ToString(), out KeyCode key))
            {
                actionHotkeys.Add((key, letter));
            }
        }
    }

    private enum HexMoveDirection
    {
        UpRight,
        Right,
        DownRight,
        DownLeft,
        Left,
        UpLeft
    }

    private void MoveSelectedCharacter(HexMoveDirection direction)
    {
        Character character = board.selectedCharacter;
        Hex currentHex = character != null ? character.hex : null;
        if (currentHex == null) return;

        Vector2 directionVector = GetDirectionVector(direction);
        Vector2Int? target = GetNeighborInDirection(currentHex, directionVector);
        if (!target.HasValue) return;

        board.Move(character, target.Value);
    }

    private Vector2Int? GetNeighborInDirection(Hex currentHex, Vector2 directionVector)
    {
        if (board == null || board.hexes == null) return null;

        Vector2Int[] neighbors = (currentHex.v2.x & 1) == 0 ? board.evenRowNeighbors : board.oddRowNeighbors;
        Vector2 currentPos = currentHex.transform.position;
        float bestDot = -1f;
        Vector2Int? best = null;

        foreach (Vector2Int offset in neighbors)
        {
            Vector2Int neighborPos = currentHex.v2 + offset;
            if (!board.hexes.TryGetValue(neighborPos, out Hex neighborHex)) continue;

            Vector2 delta = (Vector2)neighborHex.transform.position - currentPos;
            if (delta.sqrMagnitude <= Mathf.Epsilon) continue;

            float dot = Vector2.Dot(delta.normalized, directionVector);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = neighborPos;
            }
        }

        return bestDot >= 0.7f ? best : null;
    }

    private static Vector2 GetDirectionVector(HexMoveDirection direction)
    {
        return direction switch
        {
            HexMoveDirection.Left => Vector2.left,
            HexMoveDirection.Right => Vector2.right,
            HexMoveDirection.UpLeft => (Vector2.up + Vector2.left).normalized,
            HexMoveDirection.UpRight => (Vector2.up + Vector2.right).normalized,
            HexMoveDirection.DownLeft => (Vector2.down + Vector2.left).normalized,
            HexMoveDirection.DownRight => (Vector2.down + Vector2.right).normalized,
            _ => Vector2.zero
        };
    }
}
