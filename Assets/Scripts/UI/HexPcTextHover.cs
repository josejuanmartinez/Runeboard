using UnityEngine;
using UnityEngine.UI;

// Mirrors CharacterSpriteHover, but for the HexPcText label/Band instead of a character sprite.
// Hovering this (not the hex tile at large) is what shows the PC/Region card preview.
public class HexPcTextHover : MonoBehaviour
{
    public Hex hex;
    public SpriteRenderer band;

    private void OnMouseEnter()
    {
        if (hex == null) return;
        if (BoardNavigator.IsNavigationInputLocked()) return;
        if (BoardNavigator.IsPointerOverVisibleUIElement()) return;

        Sounds.Instance?.PlayUiHover();
        hex.SetPcTextHovered(true);
        band.color = new Color(band.color.r, band.color.g, band.color.b, 0.8f);
    }

    private void OnMouseExit()
    {
        if (hex == null) return;
        hex.SetPcTextHovered(false);
        band.color = new Color(band.color.r, band.color.g, band.color.b, 1f);
    }

    private void OnDisable()
    {
        if (hex == null) return;
        hex.SetPcTextHovered(false);
    }
}
