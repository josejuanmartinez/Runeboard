// Selection sigil under the focused unit, lying flat on the isometric board (ellipse via
// _IsoSquash). Procedural, single material, drawn on a flat quad:
//   • a warm pool of light on the ground under the unit
//   • a bright outer ring with a soft halo (HDR, so it catches the board bloom)
//   • an inner ring of rune dashes slowly turning the other way, with three gem pips
//   • a ripple that leaves the ring every couple of seconds and fades outward
// The front half of the sigil (toward the camera) is brighter than the back, so it reads as
// lying on the ground around the feet. Vertex colour alpha is the "appear" amount driven by
// CharacterAnimationController (0 hidden .. 1 selected): the sigil grows in from a smaller
// radius with a slight overshoot as it fades in.
Shader "RetroLOTR/GoldRing"
{
    Properties
    {
        _ColorEdge ("Ring Edge Color (deep gold)", Color) = (0.55, 0.32, 0.05, 1)
        _ColorCenter ("Ring Core Color (bright gold)", Color) = (1, 0.88, 0.45, 1)
        _GlowColor ("Ground Light Color", Color) = (1, 0.72, 0.32, 1)
        _RingRadius ("Ring Radius (UV, 0-0.5)", Range(0.1, 0.5)) = 0.42
        _RingWidth ("Ring Core Width", Range(0.005, 0.1)) = 0.03
        _IsoSquash ("Isometric Squash (Y, 1=circle)", Range(0.3, 1)) = 0.55
        _Intensity ("Ring Brightness (HDR)", Range(0.5, 4)) = 1.8
        _GlowStrength ("Ground Light Strength", Range(0, 1)) = 0.38
        _RuneCount ("Rune Dashes", Range(6, 36)) = 18
        _RuneSpeed ("Rune Turn Speed (rev/sec)", Float) = -0.04
        _RippleInterval ("Ripple Interval (sec)", Range(0.8, 6)) = 2.4
        _Alpha ("Overall Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha // premultiplied: glows add light, the pool tints the ground

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define TAU 6.28318530718

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            fixed4 _ColorEdge;
            fixed4 _ColorCenter;
            fixed4 _GlowColor;
            float _RingRadius;
            float _RingWidth;
            float _IsoSquash;
            float _Intensity;
            float _GlowStrength;
            float _RuneCount;
            float _RuneSpeed;
            float _RippleInterval;
            float _Alpha;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            // Thin anti-aliased line at radius `at`, plus an exponential halo around it.
            float Line(float r, float at, float width, float aa)
            {
                return saturate(1.0 - abs(r - at) / max(width + aa, 1e-4));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float appear = saturate(i.color.a * _Alpha);
                if (appear <= 0.001) return 0;

                float2 p = (i.uv - 0.5) * 2.0;
                float2 pc = float2(p.x, p.y / max(_IsoSquash, 1e-4));
                float r = length(pc);
                float ang = atan2(pc.y, pc.x) / TAU; // -0.5..0.5
                float aa = fwidth(r) * 1.2;
                float t = _Time.y;

                // Grow in with a small overshoot (easeOutBack) as the unit gains focus.
                float e = appear - 1.0;
                float grow = 1.0 + 2.2 * e * e * e + 1.2 * e * e;
                float R = _RingRadius * lerp(0.72, 1.0, grow);
                float breathe = 1.0 + 0.012 * sin(t * 2.2);
                R *= breathe;

                // Front (toward camera, -y) brighter than the back half.
                float front = lerp(0.55, 1.0, saturate(0.5 - pc.y / max(R, 1e-4) * 0.5));

                // Ground pool of light.
                float pool = exp(-pow(r / (R * 0.95), 2.0) * 2.4) * _GlowStrength;

                // Outer ring: core line + halo.
                float core = Line(r, R, _RingWidth * 0.5, aa);
                float halo = exp(-abs(r - R) / (_RingWidth * 1.6)) * 0.55;

                // Inner rune ring: dashes turning the other way, three gem pips riding on it.
                float rr = R * 0.8;
                float band = Line(r, rr, _RingWidth * 0.32, aa);
                float dashPhase = frac((ang + t * _RuneSpeed) * _RuneCount);
                float dash = smoothstep(0.0, 0.08, dashPhase) * (1.0 - smoothstep(0.5, 0.58, dashPhase));
                float runes = band * dash * 0.85;
                float gems = 0.0;
                [unroll(3)]
                for (int k = 0; k < 3; k++)
                {
                    float ga = frac(ang - t * _RuneSpeed * 1.5 + k / 3.0 + 0.5) - 0.5;
                    float2 d = float2(ga * TAU * rr, r - rr);
                    gems += exp(-dot(d, d) / (_RingWidth * _RingWidth * 0.9));
                }

                // Ripple: leaves the ring and fades outward.
                float phase = frac(t / _RippleInterval);
                float rippleR = R * lerp(1.0, 1.6, phase);
                float ripple = Line(r, rippleR, _RingWidth * 0.6, aa) * pow(1.0 - phase, 2.0) * 0.7;

                float3 ringCol = lerp(_ColorEdge.rgb, _ColorCenter.rgb, 0.7);
                float3 rgb = _GlowColor.rgb * pool
                           + _ColorCenter.rgb * core * _Intensity
                           + ringCol * halo * _Intensity * 0.45
                           + ringCol * runes * _Intensity * 0.8
                           + _ColorCenter.rgb * gems * _Intensity * 1.2
                           + ringCol * ripple * _Intensity * 0.7;
                float a = saturate(pool * 0.75 + core + halo * 0.35 + runes * 0.8 + gems + ripple * 0.6);

                return fixed4(rgb * front * appear * i.color.rgb, a * front * appear);
            }
            ENDCG
        }
    }
}
