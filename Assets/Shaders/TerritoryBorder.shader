// The territory line around settlements (TerritoryBorders): vertex colour with soft edges across
// the line's width, and a depth pinned just in front of the far plane — behind the depth slabs of
// every 3D unit, building and cast shadow (Character3D.shader, DepthSlabbed) — so whatever order
// it is drawn in, the miniatures and towns always stand over it. It never writes depth.
Shader "Hidden/Runeboard/TerritoryBorder"
{
    Properties
    {
        _Softness ("Edge Softness", Range(0.01, 0.5)) = 0.35
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

            float _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            // Same 0 (far) .. 1 (near) convention as Character3D.shader's WithNearness; slabs
            // start at 0.05 and shadows at half a slab base, so 0.005 is behind all of them.
            static const float Nearness = 0.005;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                #if UNITY_REVERSED_Z
                o.vertex.z = Nearness * o.vertex.w;
                #else
                o.vertex.z = (1.0 - 2.0 * Nearness) * o.vertex.w;
                #endif
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // uv.y runs across the line: fade both edges.
                float across = abs(i.uv.y * 2.0 - 1.0);
                float alpha = 1.0 - smoothstep(1.0 - _Softness * 2.0, 1.0, across);
                return fixed4(i.color.rgb, i.color.a * alpha);
            }
            ENDCG
        }
    }
}
