using UnityEngine;

/// <summary>
/// Decides when the camera cuts in for a close-up (BoardNavigator.PlayCloseUp): army battles,
/// duels and killing blows, big spells, and deaths — only in sight, only once a campaign runs, and
/// no more often than BoardLookSettings.closeUpCooldown. Called from UnitAnimations' event hooks.
/// </summary>
public static class CloseUps
{
    private static float nextAllowed;

    public static void Request(Hex hex, Hex other = null)
    {
        BoardLookSettings settings = BoardLookSettings.Instance;
        if (!settings.closeUps || hex == null || !hex.IsHexSeen()) return;
        if (Game.Instance == null || !Game.Instance.started) return;
        BoardNavigator navigator = BoardNavigator.Instance;
        if (navigator == null || navigator.IsPlayingCloseUp || Time.unscaledTime < nextAllowed) return;

        Vector3 target = hex.transform.position;
        if (other != null && other != hex && other.IsHexSeen()) target = (target + other.transform.position) * 0.5f;
        nextAllowed = Time.unscaledTime + settings.closeUpCooldown;
        navigator.PlayCloseUp(target, settings.closeUpZoom, settings.closeUpHold);
    }
}
