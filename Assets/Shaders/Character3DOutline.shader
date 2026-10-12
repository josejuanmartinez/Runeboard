// Alignment outline for live 3D board characters (prototype, see Character3DModel): the 3D
// stand-in for the sprite outline. An inverted hull — back faces pushed out along their normals by
// a fixed number of screen pixels — in the rim colour the sprite controller computes (ally / neutral
// / enemy, focus and the enemy heartbeat). Unlike a fresnel rim it is one continuous line around the
// whole silhouette, whatever the folds of the cloth do.
//
// Rendered as an extra material slot on each model renderer (Unity draws the last submesh once more
// per surplus material), with the same per-renderer depth slab as Character3D, nudged a hair farther
// so the model always covers the hull's inside.
Shader "RetroLOTR/Character3DOutline"
{
    Properties
    {
        _MainTex ("Texture (alpha cutout)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _DepthBase;
            float _DepthSlabWidth;
            float _DepthSlabScale;
            float4 _C3D_RimColor;     // rgb, a = strength (0 hides the outline)
            float _C3D_OutlineWidth;  // screen pixels

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 clip = UnityObjectToClipPos(v.vertex);
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float2 screenNormal = mul((float3x3)UNITY_MATRIX_VP, worldNormal).xy;
                float len = length(screenNormal);
                if (len > 1e-5)
                    clip.xy += screenNormal / len * (_C3D_OutlineWidth * 2.0 / _ScreenParams.xy) * clip.w;

                float relative = UnityObjectToViewPos(v.vertex.xyz).z - UnityObjectToViewPos(float3(0, 0, 0)).z;
                float nearness = _DepthBase + saturate(0.5 + relative * _DepthSlabScale) * _DepthSlabWidth - _DepthSlabWidth * 0.02;
                #if UNITY_REVERSED_Z
                clip.z = nearness * clip.w;
                #else
                clip.z = (1.0 - 2.0 * nearness) * clip.w;
                #endif

                o.vertex = clip;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.screenPos = ComputeScreenPos(clip);
                return o;
            }

            static const float Bayer4x4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            fixed4 frag(v2f i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a - 0.5);
                // Fades with the rim strength and the sprite tint alpha, as a screen-door dither.
                float alpha = saturate(_C3D_RimColor.a) * _Color.a;
                float2 pixel = floor(i.screenPos.xy / i.screenPos.w * _ScreenParams.xy);
                int index = (int)fmod(pixel.x, 4.0) + (int)fmod(pixel.y, 4.0) * 4;
                clip(alpha - (Bayer4x4[index] + 0.5) / 16.0);
                return fixed4(_C3D_RimColor.rgb * _Color.rgb, 1.0);
            }
            ENDCG
        }
    }
}
