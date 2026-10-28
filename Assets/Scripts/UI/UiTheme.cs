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

    /// <summary>A nation's colour lifted to read as a ring or border on the dark HUD.</summary>
    public static Color NationRing(Color nation)
    {
        Color.RGBToHSV(nation, out float h, out float s, out float v);
        Color c = Color.HSVToRGB(h, Mathf.Max(s, 0.4f), Mathf.Max(v, 0.8f));
        c.a = 1f;
        return c;
    }

    // One clean material per font: white face (the text colour shows true), no glow or HDR
    // boost for the bloom to catch.
    private static readonly System.Collections.Generic.Dictionary<TMP_FontAsset, Material> plainMaterials = new();

    public static Material PlainMaterial(TMP_FontAsset font)
    {
        if (font == null || font.material == null) return null;
        if (plainMaterials.TryGetValue(font, out Material m) && m != null) return m;
        m = new Material(font.material) { name = font.name + " (plain)" };
        m.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
        if (m.HasProperty(ShaderUtilities.ID_GlowPower)) m.SetFloat(ShaderUtilities.ID_GlowPower, 0f);
        m.DisableKeyword(ShaderUtilities.Keyword_Glow);
        // No outline or dilation: on the display face either one read as heavy bold.
        if (m.HasProperty(ShaderUtilities.ID_OutlineWidth)) m.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
        if (m.HasProperty(ShaderUtilities.ID_FaceDilate)) m.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
        m.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        plainMaterials[font] = m;
        return m;
    }

    /// <summary>Small-caps section label in the body face ("THE WAR OF THE RING").</summary>
    public static string Eyebrow(string label) => $"<size=78%><color={GoldHex}><cspace=0.08em>{label.ToUpperInvariant()}</cspace></color></size>";
}
