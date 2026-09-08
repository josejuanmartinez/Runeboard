using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Video;
public enum Skins
{
    Bakshi,
    Default
}

public class SkinManager : MonoBehaviour
{
    [SerializeField] private Camera skinCamera;
    [SerializeField] private MaterialManager materialManagerPrefab;
    [SerializeField] private FontManager fontManagerPrefab;
    [SerializeField] private HexMaterialSkin hexMaterialSkinPrefab;

    private Videos videos;
    private Renderer2DManager render2dManager;
    private MaterialManager materialManager;
    private FontManager fontManager;
    private HexMaterialSkin hexMaterialSkin;
    private Skins currentSkin = Skins.Default;

    public Skins CurrentSkin => currentSkin;

    void Awake()
    {
        videos = FindFirstObjectByType<Videos>();
        render2dManager = FindFirstObjectByType<Renderer2DManager>();
        materialManager = FindFirstObjectByType<MaterialManager>();
        if (materialManager == null && materialManagerPrefab != null)
            materialManager = Instantiate(materialManagerPrefab);

        fontManager = FindFirstObjectByType<FontManager>();
        if (fontManager == null && fontManagerPrefab != null)
            fontManager = Instantiate(fontManagerPrefab);

        hexMaterialSkin = FindFirstObjectByType<HexMaterialSkin>();
        if (hexMaterialSkin == null && hexMaterialSkinPrefab != null)
            hexMaterialSkin = Instantiate(hexMaterialSkinPrefab);
    }
    
    public void ChangeSkin(Skins skin)
    {
        currentSkin = skin;
        string rendererName = $"Renderer2D{GetSkinSuffix(skin)}";
        int rendererIndex = render2dManager.GetRendererIndexByName(rendererName);
        if (rendererIndex < 0)
        {
            // Not fatal to the rest of the skin: materials/fonts/hex skin still apply below, just
            // with whatever URP 2D renderer was already active (e.g. while a skin's dedicated
            // Renderer2D asset hasn't been authored yet).
            Debug.LogWarning($"SkinManager: renderer '{rendererName}' is not registered; keeping the current renderer.");
        }
        else
        {
            Camera targetCamera = skinCamera != null ? skinCamera : Camera.main;
            UniversalAdditionalCameraData cameraData = targetCamera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(rendererIndex);
        }

        materialManager.ApplySkin(skin);

        fontManager.ApplyFont(fontManager.GetFontForSkin(skin));

        hexMaterialSkin.ApplySkin(skin);
    }

    public VideoClip GetIntroVideo()
    {
        return videos.GetClipForSkin(currentSkin);
    }

    // Cycles through the skins in enum declaration order, so UI toggle buttons reach every skin
    // without hardcoding the set.
    public Skins GetNextSkin()
    {
        Skins[] values = (Skins[])System.Enum.GetValues(typeof(Skins));
        int index = System.Array.IndexOf(values, currentSkin);
        return values[(index + 1) % values.Length];
    }

    private static string GetSkinSuffix(Skins skin)
    {
        return skin == Skins.Default ? string.Empty : skin.ToString();
    }

}
