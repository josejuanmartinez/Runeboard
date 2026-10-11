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
        _KeyLight ("Top Key Light", Range(0,0.5)) = 0.14
        _FootShade ("Foot Shade", Range(0,0.6)) = 0.22
        _RimColor ("Rim Light Color", Color) = (1, 0.82, 0.55, 1)
        _RimStrength ("Rim Light Strength", Range(0,2)) = 0.55
        _RimWidth ("Rim Light Width (texels)", Range(0,24)) = 6
        _Contrast ("Contrast", Range(0.8,1.4)) = 1.1
        _Saturation ("Saturation", Range(0.5,1.6)) = 1.12
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
            float _KeyLight;
            float _FootShade;
            fixed4 _RimColor;
            float _RimStrength;
            float _RimWidth;
            float _Contrast;
            float _Saturation;

            // One tap, biased toward the sharper mip. Baked spritesheets are mipmapped (with alpha
            // coverage preserved) and drawn ~6-11x smaller than authored: the mip chain does the
            // heavy prefiltering, the bias plus the rotated-grid taps below keep the figure crisp.
            // On a texture without mips the bias is a no-op and the taps alone anti-alias.
            fixed4 SharpTap(float2 uv)
            {
                fixed4 c = tex2Dbias(_MainTex, float4(uv, 0, -0.7));
            #if ETC1_EXTERNAL_ALPHA
                c.a = lerp(c.a, tex2Dbias(_AlphaTex, float4(uv, 0, -0.7)).r, _EnableExternalAlpha);
            #endif
                return c;
            }

            fixed4 SampleFiltered(float2 uv)
            {
                float2 dx = ddx(uv), dy = ddy(uv);
                float footprint = max(length(dx), length(dy)) / max(_MainTex_TexelSize.x, 1e-6);
                if (footprint < 1.5) return SampleSpriteTexture(uv);
                fixed4 c = SharpTap(uv + dx * 0.125 + dy * 0.375);
                c += SharpTap(uv - dx * 0.375 + dy * 0.125);
                c += SharpTap(uv + dx * 0.375 - dy * 0.125);
                c += SharpTap(uv - dx * 0.125 - dy * 0.375);
                return c * 0.25;
            }

            // One texel of the ORIGINAL baked frame (cells are baked at 512 px), independent of the
            // size the sheet is imported at — so outline/aura/rim widths don't change if the sheets
            // are imported smaller. Renderers without a frame rect (banners) use the real texel.
            float2 SourceTexel()
            {
                float2 cell = _OutlineUVRect.zw - _OutlineUVRect.xy;
                return cell.x < 0.99 ? cell / 512.0 : _MainTex_TexelSize.xy;
            }

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
                fixed4 textureColor = SampleFiltered(IN.texcoord);
                fixed4 color = textureColor * IN.color;
                // Simple volume: warm key light from above, soft shade toward the feet.
                float h = saturate((IN.texcoord.y - _OutlineUVRect.y) / max(_OutlineUVRect.w - _OutlineUVRect.y, 1e-5));
                color.rgb *= (1.0 + _KeyLight * smoothstep(0.45, 1.0, h)) * (1.0 - _FootShade * (1.0 - smoothstep(0.0, 0.35, h)));

                // A touch more contrast and colour so the figure separates from the painted terrain.
                float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
                color.rgb = lerp(luma.xxx, color.rgb, _Saturation);
                color.rgb = saturate((color.rgb - 0.5) * _Contrast + 0.5);

                // Rim light: edges facing the light (front-left, matching CharacterCastShadow) catch
                // a warm highlight — where the figure is opaque but the texel toward the light is
                // empty. Width kept >= ~2 screen px so it survives zooming out.
                float2 pixelUvRim = max(abs(ddx(IN.texcoord)), abs(ddy(IN.texcoord)));
                float2 rimStep = max(SourceTexel() * _RimWidth, pixelUvRim * 2.0);
                float2 lightDir = float2(-0.8, 0.6);
                float towardLight = (Coverage(IN.texcoord + lightDir * rimStep * 0.5) + Coverage(IN.texcoord + lightDir * rimStep)) * 0.5;
                float rim = saturate(textureColor.a - towardLight) * smoothstep(0.1, 0.5, h);
                color.rgb += _RimColor.rgb * rim * _RimStrength;
                color.rgb *= color.a;

                // Build the outline mask from the sprite texture's coverage, not the renderer
                // tint alpha. Using color.a here makes a dimmed/unhovered opaque character look
                // transparent to the mask, causing _OutlineColor to bleed over the whole sprite.
                float centerAlpha = textureColor.a;
                // Outline at least ~1.1 screen pixels wide, so it stays continuous when zoomed out.
                float2 pixelUv = max(abs(ddx(IN.texcoord)), abs(ddy(IN.texcoord)));
                float2 texel = _OutlineSize > 0.0 ? max(SourceTexel() * _OutlineSize, pixelUv * 1.1) : 0;

                float neighborAlpha = RingCoverage(IN.texcoord, texel);

                float outlineAlpha = saturate(neighborAlpha - centerAlpha) * _OutlineColor.a;
                fixed3 outlineRgb = _OutlineColor.rgb * outlineAlpha;

                fixed4 result;
                result.rgb = color.rgb + outlineRgb * (1.0 - centerAlpha);
                result.a = saturate(color.a + outlineAlpha);
                // Opt-in halo: shared banner materials and army duplicates stay outline-only.
                if (_AuraStrength > 0.001 && _OutlineSize > 0.0 && centerAlpha < 0.999)
                {
                    float2 haloTexel = SourceTexel() * max(_AuraSize, _OutlineSize);
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
