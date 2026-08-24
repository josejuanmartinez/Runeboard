using UnityEngine;
using UnityEngine.UI;

// Mirrors CharacterSpriteHover, but for the HexPcText label/Band instead of a character sprite.
// Hovering this (not the hex tile at large) is what shows the PC/Region card preview.
public class HexPcTextHover : MonoBehaviour
{
    // Baseline (unhovered) alpha the Band sprite is authored with — restored on mouse-exit
    // rather than snapping to full opacity so it settles back to its normal dimmed look.
    private const float UnhoveredAlpha = 0.5f;
    private const float HoveredAlpha = 1f;

    public Hex hex;
    public SpriteRenderer band;

    private void OnMouseEnter()
    {
        if (hex == null) return;
        if (BoardNavigator.IsNavigationInputLocked()) return;
        if (BoardNavigator.IsPointerOverVisibleUIElement()) return;

        Sounds.Instance?.PlayUiHover();
        hex.SetPcTextHovered(true);
        band.color = new Color(band.color.r, band.color.g, band.color.b, HoveredAlpha);
    }

    private void OnMouseExit()
    {
        if (hex == null) return;
        hex.SetPcTextHovered(false);
        band.color = new Color(band.color.r, band.color.g, band.color.b, UnhoveredAlpha);
    }

    private void OnDisable()
    {
        if (band != null) band.color = new Color(band.color.r, band.color.g, band.color.b, UnhoveredAlpha);
        if (hex == null) return;
        hex.SetPcTextHovered(false);
    }
}
