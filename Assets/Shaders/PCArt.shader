// Settlement (PC) art on the board, drawn over its hex's terrain by HexSeamlessTerrain. The terrain
// tiles go through HexSeamlessBlend, which cross-fades every rim into its neighbours and leaves the
// map muted and continuous; the PC art used to be a plain sprite, so each town read as a brighter,
// crisper tile pasted on top. This keeps the art's silhouette (towers and trees still rise past
// the hex) but makes it sit in the same painting:
//
//  • Edge erode — the same silhouette clean-up as Sprites/EdgeErode, which this replaces: pixels
//    near the transparent outline fade, removing the baked dark halo.
//  • Grade toward the terrain (BoardLookSettings): less saturation and contrast, a little darker.
//  • Ground rim dissolve — along the hex's lower edges (SE, SW, and E/W below the centre) the art
//    fades out over its last stretch, so the blended terrain underneath shows through and the
//    town's ground melts into the map; anything below those edges (the tile's earthen slab and its
//    ink outline) is dropped. The upper edges are left alone: that is where buildings rise.
//
// Per renderer (MaterialPropertyBlock): _HexCenter = the cell centre in world space, _HexStep =
// Board.hexSize (column spacing, row spacing). Edge distance t is 1 on a shared hex edge — the same
// measure as HexSeamlessBlend's SeamT.
Shader "RetroLOTR/PCArt"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EdgeWidth ("Erode Width (texels)", Range(0.1, 20)) = 20
        _AlphaThreshold ("Transparent Threshold", Range(0, 1)) = 0.039
        _Strength ("Removal Strength", Range(0, 1)) = 1
        _Saturation ("Saturation", Range(0, 1.5)) = 0.8
        _Contrast ("Contrast", Range(0.5, 1.5)) = 0.85
        _Brightness ("Brightness", Range(0.5, 1.5)) = 0.9
        _RimStart ("Ground Rim Fade Start (edge distance)", Range(0.3, 1)) = 0.72
        [PerRendererData] _HexCenter ("Hex Centre (world)", Vector) = (0,0,0,0)
        [PerRendererData] _HexStep ("Hex Step (column, row spacing)", Vector) = (1,1,0,0)
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
            #pragma vertex PCVert
            #pragma fragment PCFrag
            #pragma target 3.0
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnitySprites.cginc"

            float _EdgeWidth;
            float _AlphaThreshold;
            float _Strength;
            float _Saturation;
            float _Contrast;
            float _Brightness;
            float _RimStart;
            float4 _HexCenter;
            float4 _HexStep;
            float4 _MainTex_TexelSize;

            struct v2fPC
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 world : TEXCOORD1;
            };

            v2fPC PCVert(appdata_t IN)
            {
                v2f s = SpriteVert(IN);
                v2fPC o;
                o.vertex = s.vertex;
                o.color = s.color;
                o.texcoord = s.texcoord;
                o.world = mul(unity_ObjectToWorld, IN.vertex).xy;
                return o;
            }

            // 1 on the edge shared with the neighbour at `offset`, 0 at the centre.
            float EdgeT(float2 d, float2 offset)
            {
                return 2.0 * dot(d, offset) / max(dot(offset, offset), 1e-6);
            }

            fixed4 PCFrag(v2fPC IN) : SV_Target
            {
                fixed4 center = SampleSpriteTexture(IN.texcoord);

                // Edge erode (see Sprites/EdgeErode).
                const int R = 3;
                float2 tap = _MainTex_TexelSize.xy * max(_EdgeWidth, 0.0001) / R;
                float minDist = R + 1;
                UNITY_UNROLL
                for (int x = -R; x <= R; x++)
                {
                    UNITY_UNROLL
                    for (int y = -R; y <= R; y++)
                    {
                        float dist = length(float2(x, y));
                        if (dist > R || dist < 0.5) continue;
                        fixed a = SampleSpriteTexture(IN.texcoord + tap * float2(x, y)).a;
                        if (a < _AlphaThreshold) minDist = min(minDist, dist);
                    }
                }
                float closeness = 1.0 - saturate((minDist - 1.0) / max((float)R - 1.0, 0.0001));
                float removal = saturate(closeness * _Strength);

                fixed4 result = center * IN.color;
                result.a *= 1.0 - removal;

                // Grade toward the muted, blended terrain (pivot ~ linear mid grey).
                float luma = dot(result.rgb, float3(0.2126, 0.7152, 0.0722));
                result.rgb = lerp(luma.xxx, result.rgb, _Saturation);
                result.rgb = max((result.rgb - 0.2) * _Contrast + 0.2, 0.0) * _Brightness;

                // Ground rim dissolve along the lower edges only.
                float2 d = IN.world - _HexCenter.xy;
                float col = _HexStep.x, row = _HexStep.y;
                float lower = max(EdgeT(d, float2(col * 0.5, -row)), EdgeT(d, float2(-col * 0.5, -row)));
                float sides = max(EdgeT(d, float2(col, 0)), EdgeT(d, float2(-col, 0)));
                // E/W edges count fully below the centre, fading out over the quarter row above it,
                // so there is no seam at mid-height.
                lower = max(lower, sides * saturate(1.0 - d.y / max(row * 0.25, 1e-4)));
                float rim = 1.0 - smoothstep(_RimStart, 1.0, lower);
                result.a *= rim;

                result.rgb *= result.a;
                return result;
            }
            ENDCG
        }
    }
}
