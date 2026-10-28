using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tilt-shift on the board camera (shader Hidden/RetroLOTR/TabletopLens, tuned in
/// BoardLookSettings): the middle of the screen stays sharp and the top and bottom soften, so the
/// map reads as a physical board with miniatures on it. Injected from script once a campaign
/// starts, only into the main camera (not the minimap's RenderTexture camera or the Scene view).
/// The HUD is a Screen Space Overlay canvas, drawn after this, so it is never blurred.
/// </summary>
public static class TabletopLens
{
    private static readonly int FocusCenterId = Shader.PropertyToID("_FocusCenter");
    private static readonly int FocusHalfWidthId = Shader.PropertyToID("_FocusHalfWidth");
    private static readonly int FocusFalloffId = Shader.PropertyToID("_FocusFalloff");
    private static readonly int MaxBlurId = Shader.PropertyToID("_MaxBlur");

    private static Material material;
    private static LensPass pass;

    /// <summary>0..1 while a close-up plays (BoardNavigator.PlayCloseUp): a narrower sharp band and more blur.</summary>
    public static float CloseUp;

    public static void Install()
    {
        BoardLookSettings settings = BoardLookSettings.Instance;
        if (settings.tiltShiftShader == null) return;
        if (material == null) material = CoreUtils.CreateEngineMaterial(settings.tiltShiftShader);
        pass ??= new LensPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        BoardLookSettings settings = BoardLookSettings.Instance;
        if (!settings.tiltShift || settings.maxBlur <= 0f || material == null) return;
        if (camera.cameraType != CameraType.Game || camera.targetTexture != null || (camera != Camera.main && camera != TitleDiorama.Camera)) return;
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        if (data == null || data.scriptableRenderer == null) return;

        material.SetFloat(FocusCenterId, settings.focusCenter);
        material.SetFloat(FocusHalfWidthId, settings.focusHalfWidth * Mathf.Lerp(1f, 0.45f, CloseUp));
        material.SetFloat(FocusFalloffId, settings.focusFalloff);
        material.SetFloat(MaxBlurId, settings.maxBlur * Mathf.Lerp(1f, 2.2f, CloseUp));
        pass.material = material;
        data.scriptableRenderer.EnqueuePass(pass);
    }

    private sealed class LensPass : ScriptableRenderPass
    {
        private const string PassName = "Tabletop Lens";
        public Material material;

        public LensPass()
        {
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null) return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            TextureHandle source = resources.activeColorTexture;
            TextureDesc descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "_TabletopLensColor";
            descriptor.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);

            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0), PassName);
            resources.cameraColor = destination;
        }
    }
}
