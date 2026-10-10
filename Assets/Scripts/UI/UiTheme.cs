using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Shared look for runtime-built HUD panels. IM Fell (the TMP default) is a display face: it
/// reads beautifully large but turns to mush below ~18px, especially with faux-bold. Small
/// text therefore uses Inter, the HUD's existing UI sans, and titles keep IM Fell.
/// </summary>
public static class UiTheme
{
    public static readonly Color Text = new(0.93f, 0.89f, 0.80f, 1f);
    public static readonly Color Muted = new(0.66f, 0.62f, 0.54f, 1f);
    public static readonly Color Gold = new(0.86f, 0.71f, 0.40f, 1f);
    public static readonly Color Good = new(0.61f, 0.83f, 0.48f, 1f);
    public static readonly Color Bad = new(0.88f, 0.48f, 0.37f, 1f);

    public const string TextHex = "#EDE3CC";
    public const string MutedHex = "#A89E8A";
    public const string GoldHex = "#DBB566";
    public const string GoodHex = "#9BD37A";
    public const string BadHex = "#E07A5F";

    private static TMP_FontAsset body;
    private static TMP_FontAsset bodyBold;

    /// <summary>Inter Regular, already loaded by the HUD; falls back to the TMP default.</summary>
    public static TMP_FontAsset Body => body != null ? body : body = Find("Inter-Regular SDF") ?? TMP_Settings.defaultFontAsset;

    /// <summary>Inter Bold when available (real bold weight, not faux-bold).</summary>
    public static TMP_FontAsset BodyBold => bodyBold != null ? bodyBold : bodyBold = Find("Inter-Bold SDF") ?? Body;

    /// <summary>IM Fell English — for titles at 18px and up only.</summary>
    public static TMP_FontAsset Display => TMP_Settings.defaultFontAsset;

    private static TMP_FontAsset Find(string name) =>
        Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name == name);

    public static void StyleBody(TMP_Text text, float size, Color? color = null)
    {
        if (text == null) return;
        text.font = Body;
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.color = color ?? Text;
        text.characterSpacing = 0f;
    }

    public static void StyleTitle(TMP_Text text, float size, Color? color = null)
    {
        if (text == null) return;
        text.font = Display;
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.color = color ?? Gold;
    }

    /// <summary>Small-caps section label in the body face ("THE WAR OF THE RING").</summary>
    public static string Eyebrow(string label) => $"<size=78%><color={GoldHex}><cspace=0.08em>{label.ToUpperInvariant()}</cspace></color></size>";
}
