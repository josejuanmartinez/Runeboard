// Directional cast shadow for baked character sprites. The renderer draws the character's current
// animation frame, flattened and sheared onto the ground by its transform hierarchy (see
// CharacterCastShadow). This shader turns that frame into a shadow: the silhouette from the
// sprite's alpha, sharp and dark at the feet, softer (higher mip) and fainter toward the far end,
// like a real contact-hardening shadow. Needs mipmapped sheets for the softening.
Shader "RetroLOTR/SpriteCastShadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0.02, 0.025, 0.04, 1)
        _Opacity ("Opacity At Feet", Range(0, 1)) = 0.8
        _FarOpacity ("Opacity At Far End (x feet)", Range(0, 1)) = 0.45
        _BlurNear ("Blur At Feet (mip bias)", Range(0, 6)) = 0.2
        _BlurFar ("Blur At Far End (mip bias)", Range(0, 6)) = 1.6
        [HideInInspector] _UVRect ("Frame UV Rect (xMin,yMin,xMax,yMax)", Vector) = (0,0,1,1)
        [HideInInspector] _FootV ("Feet height within frame (0-1)", Float) = 0.17
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment ShadowFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnitySprites.cginc"

            fixed4 _ShadowColor;
            float _Opacity;
            float _FarOpacity;
            float _BlurNear;
            float _BlurFar;
            float4 _UVRect;
            float _FootV;

            fixed4 ShadowFrag(v2f IN) : SV_Target
            {
                // 0 at the feet, 1 at the top of the frame (the far end of the shadow).
                float v = saturate((IN.texcoord.y - _UVRect.y) / max(_UVRect.w - _UVRect.y, 1e-5));
                float h = saturate((v - _FootV) / max(1.0 - _FootV, 1e-3));
                float bias = lerp(_BlurNear, _BlurFar, h);
                float2 uv = clamp(IN.texcoord, _UVRect.xy, _UVRect.zw);
                float a = tex2Dbias(_MainTex, float4(uv, 0, bias)).a;
                a *= lerp(_Opacity, _Opacity * _FarOpacity, h * h * (3.0 - 2.0 * h));
                a *= step(_FootV - 0.04, v); // nothing below the feet
                a *= IN.color.a;
                return fixed4(_ShadowColor.rgb * a, a);
            }
            ENDCG
        }
    }
}
