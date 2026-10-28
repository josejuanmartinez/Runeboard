// Board-wide fog of war drawn as one soft, drifting cloud layer over the map.
// _Mask is a low-resolution, blurred visibility map built by FogOfWarOverlay (0 = in sight,
// ~0.5 = known but unseen, 1 = unexplored); because it is blurred and bilinearly sampled the fog
// edge feathers across hexes instead of snapping to hex outlines. Where the fog is clear it still
// lays soft, drifting cloud shadows over the land (_CloudShadow, from BoardLookSettings) and
// small sun glints on water (_Mask.g marks water hexes, painted by FogOfWarOverlay).
Shader "Runeboard/FogOfWar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Mask ("Visibility Mask", 2D) = "white" {}
        _MaskRect ("Mask world rect (x, y, width, height)", Vector) = (0,0,1,1)
        _ShadowColor ("Shadow", Color) = (0.035,0.040,0.050,1)
        _FogColor ("Fog", Color) = (0.150,0.165,0.185,1)
        _HighlightColor ("Mist Highlight", Color) = (0.330,0.345,0.360,1)
        _Scale ("Cloud Scale (per world unit)", Range(0.05,2)) = 0.32
        _Speed ("Drift Speed", Vector) = (0.030, 0.010, 0, 0)
        _Opacity ("Opacity", Range(0,1)) = 0.92
        _UnexploredOpacity ("Never-explored Opacity", Range(0,1)) = 1
        _EdgeGlow ("Edge Glow", Range(0,1)) = 0.35
        _EdgeColor ("Edge Glow Colour", Color) = (0.62,0.52,0.34,1)
        _CloudShadow ("Cloud Shadow Strength", Range(0,1)) = 0
        _CloudShadowColor ("Cloud Shadow Colour", Color) = (0.05,0.07,0.12,1)
        _CloudShadowScale ("Cloud Shadow Scale (per world unit)", Range(0.01,0.5)) = 0.07
        _CloudShadowDrift ("Cloud Shadow Drift (world units/s)", Vector) = (0.12, 0.04, 0, 0)
        _WaterShimmer ("Water Glints", Range(0,1)) = 0
        _GlintColor ("Water Glint Colour", Color) = (0.92,0.95,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _Mask;
            float4 _MaskRect;
            fixed4 _ShadowColor, _FogColor, _HighlightColor, _EdgeColor;
            float _Scale, _Opacity, _UnexploredOpacity, _EdgeGlow;
            float4 _Speed;
            fixed4 _CloudShadowColor;
            float _CloudShadow, _CloudShadowScale;
            float4 _CloudShadowDrift;
            float _WaterShimmer;
            fixed4 _GlintColor;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 world : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                o.color = v.color;
                return o;
            }

            float hash (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1,0)), u.x), lerp(hash(i + float2(0,1)), hash(i + float2(1,1)), u.x), u.y);
            }
            float fbm (float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * vnoise(p); p = p * 2.03 + float2(17.1, 9.2); a *= 0.5; }
                return v;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.world - _MaskRect.xy) / _MaskRect.zw;
                float2 maskSample = tex2D(_Mask, uv).rg; // r = visibility, g = water
                float mask = maskSample.r;

                float2 p = i.world * _Scale;
                float t = _Time.y;
                float n = fbm(p + _Speed.xy * t * 10.0);
                float n2 = fbm(p * 1.9 - _Speed.yx * t * 14.0 + n);
                float cloud = saturate(0.55 * n + 0.45 * n2);

                // Coverage: solid where unexplored, thinner veils over known-but-unseen land, and
                // a cloud-shaped (not hex-shaped) boundary where the two meet.
                float coverage = smoothstep(0.05, 0.85, mask + (cloud - 0.5) * 0.35);
                float alpha = coverage * lerp(0.82, 1.0, cloud) * _Opacity;
                // Land never explored is hidden outright (no terrain showing through); the veil
                // over known-but-unseen land stays thin. The boundary keeps the cloud-shaped edge.
                float unexplored = smoothstep(0.72, 0.95, mask + (cloud - 0.5) * 0.12);
                alpha = lerp(alpha, _UnexploredOpacity, unexplored);

                fixed3 col = lerp(_ShadowColor.rgb, _FogColor.rgb, cloud);
                col += _HighlightColor.rgb * pow(cloud, 3.0) * 0.55;
                // Faint warm rim where the fog thins at the edge of sight.
                float rim = saturate(1.0 - abs(coverage - 0.35) / 0.25) * _EdgeGlow;
                col = lerp(col, _EdgeColor.rgb, rim * 0.35);

                // Cloud shadows: large, slow, soft-edged shade drifting over land in sight, as if
                // clouds passed between the lamp and the table. Fog covers them where it thickens.
                float2 sp = (i.world - _CloudShadowDrift.xy * t) * _CloudShadowScale;
                float shade = smoothstep(0.48, 0.7, fbm(sp)) * _CloudShadow;
                // Water glints: two slow noise fields drifting against each other; only where both
                // peak does a small bright fleck of sun show on the water. Dimmed under cloud shade.
                float2 wp = i.world * 3.0;
                float glintField = vnoise(wp + float2(t * 0.22, t * 0.09)) + vnoise(wp * 1.37 - float2(t * 0.17, -t * 0.13));
                float glint = smoothstep(1.32, 1.62, glintField) * maskSample.g * _WaterShimmer * (1.0 - shade * 2.0);
                glint = saturate(glint);

                // The clear-land layer: cloud shade (dark) and glints (light), as one colour + alpha.
                // Blended premultiplied, so an empty clear layer (no shade, no glint) adds nothing:
                // blending its colour straight in dragged the veil over known land toward black.
                float clearAlpha = saturate(shade + glint);
                float3 clearPremul = _CloudShadowColor.rgb * shade + _GlintColor.rgb * glint;
                float3 premul = lerp(clearPremul, col * alpha, coverage);
                alpha = lerp(clearAlpha, alpha, coverage);
                col = premul / max(alpha, 1e-4);
                return fixed4(col, alpha * i.color.a);
            }
            ENDCG
        }
    }
}
