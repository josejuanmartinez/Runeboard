using System;
using TMPro;
using UnityEngine;

public class MovementCostManager : MonoBehaviour
{
    public TextMeshPro movementText;
    public SpriteRenderer dot;
    private Color dotBaseColor = Color.white;
    private static Colors sharedColors;

    private void Awake()
    {
        if (movementText == null)
        {
            movementText = GetComponentInChildren<TextMeshPro>(true);
        }

        if (FontManager.Instance != null) FontManager.Instance.ApplyCurrentFont(movementText);

        if (dot == null)
        {
            SpriteRenderer[] spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null && string.Equals(spriteRenderers[i].gameObject.name, "dot", StringComparison.OrdinalIgnoreCase))
                {
                    dot = spriteRenderers[i];
                    break;
                }
            }
        }

        if (dot != null)
        {
            dotBaseColor = dot.color;
        }
    }

    public void ShowMovementLeft(int movementLeft, Character character, string terrainSpriteTags = "")
    {
        //string spr = "movement";
        // if(character.IsArmyCommander()) spr = character.GetAlignment().ToString();
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (movementText != null && !movementText.gameObject.activeSelf) movementText.gameObject.SetActive(true);
        if (dot != null && !dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        // terrainSpriteTags are inline TMP <sprite> tags for the hex terrain (+ chasm marker), shown beside the cost.
        Color costColor = GetMovementCostColor(movementLeft, character);
        string coloredCost = $"<color=#{ColorUtility.ToHtmlStringRGBA(costColor)}>{movementLeft}</color>";
        movementText.text = string.IsNullOrEmpty(terrainSpriteTags)
            ? movementLeft.ToString()
            : $"{terrainSpriteTags}\n{coloredCost}";
        

        /*if (dot != null)
        {
            dot.color = costColor;
        }
        */
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private static Color GetMovementCostColor(int movementLeft, Character character)
    {
        int maxMovement = character != null ? character.GetMaxMovement() : 0;
        float ratio = maxMovement > 0 ? Mathf.Clamp01(movementLeft / (float)maxMovement) : 0f;

        if (sharedColors == null) sharedColors = FindFirstObjectByType<Colors>();
        Color low = sharedColors != null ? sharedColors.movementStart : new Color(0.72f, 0.16f, 0.2f, 1f);
        Color high = sharedColors != null ? sharedColors.movementEnd : new Color(0.08f, 0.58f, 0.52f, 1f);
        return Color.Lerp(low, high, ratio);
    }
}
