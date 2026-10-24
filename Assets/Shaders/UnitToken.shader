// A unit's strategic-zoom token (see UnitToken): a round medallion of its card illustration in a
// nation-coloured ring with a thin gold edge, standing on the miniature's base. Drawn on a unit
// quad; _UVRect picks the square of the illustration to show (around the face).
Shader "RetroLOTR/UnitToken"
{
    Properties
    {
        _MainTex ("Illustration", 2D) = "white" {}
        _UVRect ("Portrait UV rect (x, y, w, h)", Vector) = (0,0,1,1)
        _RingColor ("Ring (nation colour)", Color) = (0.4,0.4,0.4,1)
        _Alpha ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _UVRect;
            fixed4 _RingColor;
            float _Alpha;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(p);
                float aa = fwidth(d) * 1.5;
                const float portrait = 0.8, ring = 0.95;

                fixed3 art = tex2D(_MainTex, _UVRect.xy + i.uv * _UVRect.zw).rgb;
                // Inner shade toward the ring, so the portrait sits inside it.
                art *= lerp(1.0, 0.7, smoothstep(portrait - 0.25, portrait, d));

                // Ring in the nation colour, lit from the top-left, with gold inner and outer edges.
                float light = saturate(0.75 + 0.35 * dot(normalize(p + 1e-5), float2(-0.6, 0.8)));
                fixed3 ringCol = _RingColor.rgb * light;
                fixed3 gold = fixed3(0.85, 0.68, 0.36);
                float edge = max(1.0 - smoothstep(0.0, 0.03, abs(d - portrait)), 1.0 - smoothstep(0.0, 0.03, abs(d - ring + 0.015)));
                ringCol = lerp(ringCol, gold, edge * 0.8);

                fixed3 col = lerp(art, ringCol, smoothstep(portrait - aa, portrait + aa, d));
                float alpha = 1.0 - smoothstep(ring - aa, ring + aa, d);
                return fixed4(col, alpha * _Alpha);
            }
            ENDCG
        }
    }
}
