// Soft round puffs for the board's ambient particles (AmbientLife: chimney smoke, dust): a
// procedural soft disc — no texture — in the particle's vertex colour, with a little noise so a
// puff reads as smoke rather than a perfect circle.
Shader "RetroLOTR/AmbientParticle"
{
    Properties
    {
        _Softness ("Edge Softness", Range(0.05, 1)) = 0.7
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
            #include "UnityCG.cginc"

            float _Softness;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float seed : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv.xy;
                o.seed = frac(dot(v.vertex.xy, float2(12.9898, 78.233)) * 0.1);
                return o;
            }

            float hash (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1,0)), u.x), lerp(hash(i + float2(0,1)), hash(i + float2(1,1)), u.x), u.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(p) + (vnoise(p * 2.5 + i.seed * 17.0) - 0.5) * 0.35;
                float a = 1.0 - smoothstep(1.0 - _Softness, 1.0, d);
                return fixed4(i.color.rgb, i.color.a * a);
            }
            ENDCG
        }
    }
}
