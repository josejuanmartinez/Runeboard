Shader "Sprites/Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineSize ("Outline Size", Float) = 1.0
        _AuraColor ("Aura Color", Color) = (1,1,1,1)
        _AuraSize ("Aura Radius", Float) = 0
        _AuraStrength ("Aura Opacity", Range(0,1)) = 0
        [HideInInspector] _OutlineUVRect ("Sprite UV Bounds", Vector) = (0,0,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment OutlineSpriteFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnitySprites.cginc"

            fixed4 _OutlineColor;
            float _OutlineSize;
            fixed4 _AuraColor;
            float _AuraSize;
            float _AuraStrength;
            float4 _OutlineUVRect;
            float4 _MainTex_TexelSize;

            float Coverage(float2 uv)
            {
                float2 inside = step(_OutlineUVRect.xy, uv) * step(uv, _OutlineUVRect.zw);
                return SampleSpriteTexture(clamp(uv, _OutlineUVRect.xy, _OutlineUVRect.zw)).a * inside.x * inside.y;
            }

            float RingCoverage(float2 uv, float2 radius)
            {
                float coverage = Coverage(uv + float2(radius.x, 0));
                coverage = max(coverage, Coverage(uv - float2(radius.x, 0)));
                coverage = max(coverage, Coverage(uv + float2(0, radius.y)));
                coverage = max(coverage, Coverage(uv - float2(0, radius.y)));
                // Rounded character rims; retain the original width on outline-only materials.
                float2 diagonal = radius * (_AuraSize > 0.0 ? 0.707107 : 1.0);
                coverage = max(coverage, Coverage(uv + diagonal));
                coverage = max(coverage, Coverage(uv - diagonal));
                coverage = max(coverage, Coverage(uv + float2(diagonal.x, -diagonal.y)));
                return max(coverage, Coverage(uv + float2(-diagonal.x, diagonal.y)));
            }

            float SoftCoverage(float2 uv, float2 radius)
            {
                float coverage = Coverage(uv + float2(radius.x, 0)) + Coverage(uv - float2(radius.x, 0));
                coverage += Coverage(uv + float2(0, radius.y)) + Coverage(uv - float2(0, radius.y));
                float2 diagonal = radius * 0.707107;
                coverage += Coverage(uv + diagonal) + Coverage(uv - diagonal);
                coverage += Coverage(uv + float2(diagonal.x, -diagonal.y)) + Coverage(uv + float2(-diagonal.x, diagonal.y));
                return coverage * 0.125;
            }

            fixed4 OutlineSpriteFrag(v2f IN) : SV_Target
            {
                fixed4 textureColor = SampleSpriteTexture(IN.texcoord);
                fixed4 color = textureColor * IN.color;
                color.rgb *= color.a;

                // Build the outline mask from the sprite texture's coverage, not the renderer
                // tint alpha. Using color.a here makes a dimmed/unhovered opaque character look
                // transparent to the mask, causing _OutlineColor to bleed over the whole sprite.
                float centerAlpha = textureColor.a;
                float2 texel = _MainTex_TexelSize.xy * max(_OutlineSize, 0.0);

                float neighborAlpha = RingCoverage(IN.texcoord, texel);

                float outlineAlpha = saturate(neighborAlpha - centerAlpha) * _OutlineColor.a;
                fixed3 outlineRgb = _OutlineColor.rgb * outlineAlpha;

                fixed4 result;
                result.rgb = color.rgb + outlineRgb * (1.0 - centerAlpha);
                result.a = saturate(color.a + outlineAlpha);
                // Opt-in halo: shared banner materials and army duplicates stay outline-only.
                if (_AuraStrength > 0.001 && _OutlineSize > 0.0 && centerAlpha < 0.999)
                {
                    float2 haloTexel = _MainTex_TexelSize.xy * max(_AuraSize, _OutlineSize);
                    float halo = SoftCoverage(IN.texcoord, lerp(texel, haloTexel, 0.3)) * 0.5;
                    halo += SoftCoverage(IN.texcoord, lerp(texel, haloTexel, 0.65)) * 0.3;
                    halo += SoftCoverage(IN.texcoord, haloTexel) * 0.2;
                    float haloAlpha = saturate(halo - centerAlpha) * saturate(_AuraStrength) * _AuraColor.a;
                    // Premultiplied compositing keeps the glow outside the figure, even when dimmed.
                    float remaining = (1.0 - centerAlpha) * (1.0 - outlineAlpha);
                    result.rgb += _AuraColor.rgb * haloAlpha * remaining;
                    result.a = saturate(result.a + haloAlpha * remaining);
                }
                return result;
            }
            ENDCG
        }
    }
}
