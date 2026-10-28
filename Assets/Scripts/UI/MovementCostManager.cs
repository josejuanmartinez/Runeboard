using System;
using TMPro;
using UnityEngine;

// The movement left at each hex of a planned route: a small dark medallion with a coloured rim
// (gold while the move is comfortable, amber when it runs low, red at nothing left) and the
// number in the middle. The route's last hex gets a larger medallion so the destination reads
// at a glance. The medallion art is drawn once in code and shared by every hex.
public class MovementCostManager : MonoBehaviour
{
    public TextMeshPro movementText;
    public SpriteRenderer dot;
    public TextMeshPro terrainText;

    private const float Diameter = 0.38f;        // local units; a hex is about 1 wide
    private const float DestinationScale = 1.25f;
    private const float TextScale = 0.27f;

    private static readonly Color Plenty = new(0.88f, 0.72f, 0.40f);
    private static readonly Color Low = new(0.96f, 0.56f, 0.24f);
    private static readonly Color Spent = new(0.86f, 0.26f, 0.20f);
    private static readonly Color Number = new(0.98f, 0.94f, 0.84f);
    private static readonly Color NumberSpent = new(1f, 0.66f, 0.50f);

    private static Sprite plateSprite;
    private static Sprite rimSprite;

    private SpriteRenderer rim;

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

        Restyle();
    }

    // Swaps the prefab's flat oval for the medallion: the number moves out from under the dot (so
    // it keeps its proportions whatever the medallion's size) and a tinted rim sits on the plate.
    private void Restyle()
    {
        if (dot == null) return;
        EnsureSprites();

        if (movementText != null && movementText.transform.parent == dot.transform)
        {
            movementText.transform.SetParent(transform, false);
            movementText.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        }
        if (movementText != null)
        {
            movementText.transform.localScale = new Vector3(TextScale, TextScale, 1f);
            movementText.fontStyle = FontStyles.Bold;
        }

        dot.drawMode = SpriteDrawMode.Simple;
        dot.sprite = plateSprite;
        dot.color = Color.white;

        var rimGo = new GameObject("Rim");
        rimGo.layer = dot.gameObject.layer;
        rimGo.transform.SetParent(dot.transform, false);
        rimGo.transform.localPosition = new Vector3(0f, 0f, -0.005f);
        rim = rimGo.AddComponent<SpriteRenderer>();
        rim.sprite = rimSprite;
        rim.sharedMaterial = dot.sharedMaterial;
        rim.sortingLayerID = dot.sortingLayerID;
        rim.sortingOrder = dot.sortingOrder + 1;

        SetSize(false);
    }

    public void ShowMovementLeft(int movementLeft, Character character, string terrainSpriteTags = "", bool destination = false)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (movementText != null && !movementText.gameObject.activeSelf) movementText.gameObject.SetActive(true);
        if (dot != null && !dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        SetSize(destination);
        // terrainSpriteTags are inline TMP <sprite> tags for the hex terrain (+ chasm marker), shown beside the cost.
        movementText.text = movementLeft.ToString();
        movementText.color = movementLeft > 0 ? Number : NumberSpent;
        if (terrainText != null)
        {
            terrainText.text = terrainSpriteTags;
            terrainText.gameObject.SetActive(!string.IsNullOrEmpty(terrainSpriteTags));
        }
        Color tint = RimColor(movementLeft, character);
        if (destination) tint = Color.Lerp(tint, Color.white, 0.25f);
        if (rim != null) rim.color = tint;
    }

    // A step the unit cannot reach this turn: a red X instead of the movement left.
    public void ShowBlocked()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (movementText != null)
        {
            if (!movementText.gameObject.activeSelf) movementText.gameObject.SetActive(true);
            movementText.text = "X";
            movementText.color = new Color(1f, 0.42f, 0.34f);
        }
        if (terrainText != null) terrainText.gameObject.SetActive(false);
        if (dot != null && !dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        SetSize(false);
        if (rim != null) rim.color = new Color(0.55f, 0.12f, 0.10f);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void SetSize(bool destination)
    {
        float scale = destination ? DestinationScale : 1f;
        if (dot != null) dot.transform.localScale = new Vector3(Diameter * scale, Diameter * scale, 1f);
        if (movementText != null) movementText.transform.localScale = new Vector3(TextScale * scale, TextScale * scale, 1f);
    }

    // Gold while at least a third of the move is left, then amber, then red at nothing.
    private static Color RimColor(int movementLeft, Character character)
    {
        if (movementLeft <= 0) return Spent;
        int maxMovement = character != null ? character.GetMaxMovement() : 0;
        float ratio = maxMovement > 0 ? Mathf.Clamp01(movementLeft / (float)maxMovement) : 1f;
        return ratio >= 0.34f ? Plenty : Color.Lerp(Low, Plenty, ratio / 0.34f * 0.35f);
    }

    // ── Medallion art ───────────────────────────────────────────────────────────────────────

    private const int Px = 128;

    private static void EnsureSprites()
    {
        if (plateSprite != null && rimSprite != null) return;
        plateSprite = MakeSprite("MovementCostPlate", PlatePixel);
        rimSprite = MakeSprite("MovementCostRim", RimPixel);
    }

    private static Sprite MakeSprite(string name, Func<float, float, Color> pixel)
    {
        var tex = new Texture2D(Px, Px, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color[Px * Px];
        for (int y = 0; y < Px; y++)
            for (int x = 0; x < Px; x++)
            {
                // Centred coordinates, 1 at the sprite's edge.
                float u = (x + 0.5f) / Px * 2f - 1f;
                float v = (y + 0.5f) / Px * 2f - 1f;
                pixels[y * Px + x] = pixel(u, v);
            }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, Px, Px), new Vector2(0.5f, 0.5f), Px);
    }

    private static float Edge(float distance, float radius, float feather = 0.025f) =>
        Mathf.Clamp01((radius - distance) / feather + 0.5f);

    // A soft drop shadow, then the disc: lit from above, darker below, with a faint inner hairline.
    private static Color PlatePixel(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float shadowR = Mathf.Sqrt(u * u + (v + 0.07f) * (v + 0.07f));
        float shadow = 0.55f * Mathf.Clamp01((0.98f - shadowR) / 0.22f);
        float disc = Edge(r, 0.80f);
        float light = Mathf.InverseLerp(-0.8f, 0.8f, v);
        Color body = Color.Lerp(new Color(0.035f, 0.032f, 0.03f), new Color(0.17f, 0.15f, 0.12f), light * light);
        float hairline = Edge(Mathf.Abs(r - 0.63f), 0.012f, 0.02f) * 0.35f;
        body = Color.Lerp(body, new Color(0.55f, 0.47f, 0.33f), hairline);
        float alpha = disc * 0.97f + (1f - disc) * shadow;
        Color colour = disc > 0f ? body : Color.black;
        return new Color(colour.r * disc, colour.g * disc, colour.b * disc, alpha);
    }

    // The rim: a ring brightest at its top, tinted per state by the renderer's colour.
    private static Color RimPixel(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float ring = Edge(Mathf.Abs(r - 0.755f), 0.05f, 0.03f);
        float shine = Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(-0.8f, 0.8f, v));
        return new Color(shine, shine, shine, ring);
    }
}
