// The wooden table under the title screen's board (TitleDiorama): dark oak planks drawn
// procedurally in world space (no texture) — per-plank tone and grain, ring figure, seams and butt
// joints — lit by a warm lantern pool and falling off into a dark vignette. Drawn first in the
// sprite order and never writes depth, so the map and the miniatures always stand on it.
Shader "Hidden/Runeboard/TitleTable"
{
    Properties
    {
        _WoodDark ("Wood Dark", Color) = (0.10, 0.060, 0.035, 1)
        _WoodLight ("Wood Light", Color) = (0.30, 0.185, 0.10, 1)
        _PlankWidth ("Plank Width (world)", Float) = 2.4
        _LampPos ("Lantern (world xy)", Vector) = (0, 0, 0, 0)
        _LampRadius ("Lantern Radius (world)", Float) = 18
        _LampColor ("Lantern Colour", Color) = (1.0, 0.72, 0.42, 1)
        _Ambient ("Ambient", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _WoodDark, _WoodLight, _LampColor, _LampPos;
            float _PlankWidth, _LampRadius, _Ambient;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 world : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0.0, amp = 0.5;
                for (int k = 0; k < 4; k++) { sum += amp * Noise(p); p *= 2.03; amp *= 0.5; }
                return sum;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 w = i.world;
                // Planks run along x; each has its own offset, tone and joint.
                float row = floor(w.y / _PlankWidth);
                float across = frac(w.y / _PlankWidth);
                float seed = Hash(float2(row, 7.1));
                float jointLength = _PlankWidth * (6.0 + 5.0 * seed);
                float along = w.x + seed * 37.0;
                float piece = floor(along / jointLength);
                float pieceSeed = Hash(float2(row, piece));

                // Grain: long streaks along the plank, warped so the ring figure meanders.
                float2 g = float2(along * 0.18, (across + row) * 5.5);
                float warp = Fbm(g * float2(0.6, 0.35) + pieceSeed * 13.0);
                float streaks = Fbm(float2(along * 0.9, (across * 14.0 + warp * 3.0) + pieceSeed * 5.0));
                float rings = 0.5 + 0.5 * sin((across * 9.0 + warp * 4.0 + pieceSeed * 6.0) * 3.14159);
                float tone = saturate(0.30 + 0.38 * streaks + 0.22 * rings * rings + (pieceSeed - 0.5) * 0.3);
                float3 wood = lerp(_WoodDark.rgb, _WoodLight.rgb, tone);

                // Seams between planks and butt joints between pieces.
                float seam = smoothstep(0.0, 0.025, across) * smoothstep(1.0, 0.975, across);
                float jointPos = frac(along / jointLength) * jointLength;
                float joint = smoothstep(0.0, 0.05, jointPos) * smoothstep(jointLength, jointLength - 0.05, jointPos);
                wood *= lerp(0.25, 1.0, seam * joint);
                // Worn, slightly polished middle of each plank.
                wood *= 0.88 + 0.12 * sin(across * 3.14159);

                // Warm lantern pool, then the room falls away into the dark.
                float d = distance(w, _LampPos.xy) / max(_LampRadius, 1e-3);
                float lamp = exp(-d * d * 1.6);
                float3 lit = wood * (_Ambient + lamp * 1.4 * _LampColor.rgb);
                return fixed4(lit, 1.0);
            }
            ENDCG
        }
    }
}
