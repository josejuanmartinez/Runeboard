// Tilt-shift for the board camera (see TabletopLens): a horizontal band through the middle of the
// screen stays sharp and the blur grows toward the top and bottom edges, like a close-up photo of
// a model. A 16-tap golden-angle disc, run before URP's post (bloom, grade, vignette, grain).
Shader "Hidden/RetroLOTR/TabletopLens"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Tabletop Lens"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _FocusCenter;    // screen height fraction
            float _FocusHalfWidth; // screen height fraction
            float _FocusFalloff;   // screen height fraction
            float _MaxBlur;        // pixels at 1080p

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 centre = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                float away = abs(uv.y - _FocusCenter) - _FocusHalfWidth;
                float amount = smoothstep(0.0, _FocusFalloff, away);
                float radius = amount * _MaxBlur * _BlitTexture_TexelSize.w / 1080.0;
                if (radius < 0.5) return centre;

                float3 sum = centre.rgb;
                [unroll] for (int i = 0; i < 16; i++)
                {
                    float r = sqrt((i + 0.5) / 16.0) * radius;
                    float a = i * 2.3999632;
                    float2 offset = float2(cos(a), sin(a)) * r * _BlitTexture_TexelSize.xy;
                    sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + offset, 0).rgb;
                }
                return half4(sum / 17.0, centre.a);
            }
            ENDHLSL
        }
    }
}
