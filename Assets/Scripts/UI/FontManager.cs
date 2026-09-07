using System.Linq;
using TMPro;
using UnityEngine;

[System.Serializable]
public class SkinFont
{
    public Skins skin;
    public TMP_FontAsset font;
}

public class FontManager : SearcherByName
{
    public static FontManager Instance { get; private set; }

    [SerializeField] private SkinFont[] skinFonts;
    private TMP_FontAsset currentFont;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public TMP_FontAsset GetFontForSkin(Skins skin)
    {
        return skinFonts.FirstOrDefault(entry => entry.skin == skin)?.font;
    }

    public TMP_FontAsset GetCurrentFont()
    {
        return currentFont != null ? currentFont : TMP_Settings.defaultFontAsset;
    }

    public void ApplyCurrentFont(TMP_Text text)
    {
        TMP_FontAsset font = GetCurrentFont();
        // Authored body and numeric fonts are deliberate; only swap skin display faces.
        if (text != null && font != null && (text.font == null || skinFonts.Any(entry => entry.font == text.font)))
            text.font = font;
    }

    public void ApplyFont(TMP_FontAsset font)
    {
        if (font == null) return;

        currentFont = font;
        TMP_Settings.defaultFontAsset = font;
        foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (text.font == null || skinFonts.Any(entry => entry.font == text.font))
                text.font = font;
        }
    }
}
