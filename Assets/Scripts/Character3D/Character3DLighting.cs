using UnityEngine;

// The scene lighting for live 3D units (prototype, see Character3D.shader). The board has no real
// lights — its light is painted into the map art, from the front-left, and the sprite cast shadows
// (CharacterCastShadow) are slanted to agree with it. So instead of URP lights, one key light is
// defined in the units' own board frame (ground plane = the pitched plane a model stands on, so
// "above" leans toward the camera with the model) and published as shader globals once a frame.
// Azimuth 45° / elevation 40° is the light that the sprite shadows' back-right slant implies; the
// 3D cast shadow itself reuses CharacterCastShadow's exact shear, so 2D and 3D units cast alike.
public static class Character3DLighting
{
    private static readonly int LightDirId = Shader.PropertyToID("_C3D_LightDir");
    private static readonly int KeyColorId = Shader.PropertyToID("_C3D_KeyColor");
    private static readonly int SkyColorId = Shader.PropertyToID("_C3D_SkyColor");
    private static readonly int GroundColorId = Shader.PropertyToID("_C3D_GroundColor");
    private static readonly int ShadowColorId = Shader.PropertyToID("_C3D_ShadowColor");
    private static readonly int ParamsId = Shader.PropertyToID("_C3D_Params");
    private static readonly int ShadowShapeId = Shader.PropertyToID("_C3D_ShadowShape");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_C3D_OutlineWidth");

    // The pitched frame every model stands in: up leans toward the camera by `pitch`.
    public static Quaternion BoardFrame(Character3DSettings settings) => Quaternion.Euler(-settings.pitch, 0f, 0f);

    // World-space direction toward the key light.
    public static Vector3 LightDirection(Character3DSettings settings)
    {
        float azimuth = settings.lightAzimuth * Mathf.Deg2Rad;
        float elevation = settings.lightElevation * Mathf.Deg2Rad;
        // Board frame: +Y up, +Z away from the viewer, so "from the viewer" is -Z, "from the left" -X.
        var towardLight = new Vector3(
            -Mathf.Sin(azimuth) * Mathf.Cos(elevation),
            Mathf.Sin(elevation),
            -Mathf.Cos(azimuth) * Mathf.Cos(elevation));
        return (BoardFrame(settings) * towardLight).normalized;
    }

    public static void Publish(Character3DSettings settings)
    {
        Shader.SetGlobalVector(LightDirId, LightDirection(settings));
        Shader.SetGlobalColor(KeyColorId, settings.keyColor);
        Shader.SetGlobalColor(SkyColorId, settings.skyColor);
        Shader.SetGlobalColor(GroundColorId, settings.groundColor);
        Shader.SetGlobalColor(ShadowColorId, settings.shadowColor);
        Shader.SetGlobalFloat(OutlineWidthId, settings.outlineWidth);
        Shader.SetGlobalVector(ShadowShapeId, new Vector4(CharacterCastShadow.Shear, CharacterCastShadow.Squash, 0f, 0f));
        Shader.SetGlobalVector(ParamsId, new Vector4(
            settings.lit, settings.bandSoftness, settings.shadowFarOpacity, settings.bandOffset));
    }
}
