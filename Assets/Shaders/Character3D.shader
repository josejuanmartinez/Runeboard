// Live 3D board character (prototype, see Character3DView). Two passes, both drawn by the URP 2D
// Renderer (it renders the untagged "SRPDefaultUnlit" pass and the "Universal2D" pass):
//
//  1. Cast shadow — the mesh itself flattened onto the ground with the same shear the sprite
//     cast shadows use, so it walks and swings with the animation and matches 2D units. Darkest at the
//     feet, fading with height; a per-model depth keeps overlapping limbs from double-darkening.
//     Depth sits just behind every model's slab, so it lies under all units but over the board.
//
//  2. The model — the albedo with the scene lighting Character3DLighting publishes: a soft two-tone
//     key-light band (painterly rather than smooth Lambert) over a sky/ground hemisphere ambient,
//     never brighter than the texture itself. _Lit = 0 gives back the flat look the spritesheets
//     were baked with. The alignment outline is a separate material (Character3DOutline).
//
// Shared per-renderer values (Character3DModel, MaterialPropertyBlock):
//   • _Color: the sprite tint Hex applies (hover dimming); alpha < 1 fades with a screen-door
//     dither, so the mesh can stay opaque and keep writing depth.
//   • Depth slab: the board is a flat 2D plane, so real view depth is meaningless between units and
//     against sprites. Each model's depth is squeezed into its own thin slab near the far plane —
//     slabs ordered by board Y (lower on screen = nearer) — so a model self-occludes correctly,
//     overlaps its neighbours in the right order, and never hides a sprite (banners, icons, fog)
//     drawn after it, since sprites test at their real, much nearer depth.
Shader "RetroLOTR/Character3D"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ShadowZTest ("Shadow Depth Test", Float) = 2
        _ShadowDebug ("Shadow Debug (solid magenta)", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    float4 _Color;
    float _ShadowDebug;
    float _DepthBase;      // slab start, 0 = far plane .. 1 = near plane
    float _DepthSlabWidth; // slab thickness in the same 0..1 units
    float _DepthSlabScale; // 1 / (model depth extent in view units)
    float3 _C3D_Origin;    // feet, world space
    float3 _C3D_Up;        // the unit's up axis (board frame, pitched toward the camera)
    float _C3D_Height;     // model height, world units

    // Published once per frame by Character3DLighting.
    float3 _C3D_LightDir;     // toward the key light, world space
    float4 _C3D_KeyColor;
    float4 _C3D_SkyColor;
    float4 _C3D_GroundColor;
    float4 _C3D_ShadowColor;  // rgb, a = opacity at the feet
    float4 _C3D_Params;       // x = lit amount, y = band softness, z = shadow far opacity, w = band offset
    float4 _C3D_ShadowShape;  // x = shear, y = squash (CharacterCastShadow's ground projection)

    // Writes a depth in the 0 (far) .. 1 (near) convention into a clip position, for either Z
    // direction.
    float4 WithNearness(float4 clipPos, float nearness)
    {
        #if UNITY_REVERSED_Z
        clipPos.z = nearness * clipPos.w;
        #else
        clipPos.z = (1.0 - 2.0 * nearness) * clipPos.w;
        #endif
        return clipPos;
    }

    static const float Bayer4x4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    void DitherFade(float4 screenPos)
    {
        if (_Color.a < 0.999)
        {
            float2 pixel = floor(screenPos.xy / screenPos.w * _ScreenParams.xy);
            int index = (int)fmod(pixel.x, 4.0) + (int)fmod(pixel.y, 4.0) * 4;
            clip(_Color.a - (Bayer4x4[index] + 0.5) / 16.0);
        }
    }
    ENDCG

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        // ── 1. Planar cast shadow ────────────────────────────────────────────────────────────
        Pass
        {
            Name "CastShadow"
            Cull Off
            // Each model's shadow is flat at one depth of its own (half its slab base): writing it
            // and testing strictly Less lets overlapping limbs darken once, models occlude it, and
            // the board (no depth) never does. (A stencil bit was tried first, but the 2D Renderer
            // doesn't leave the stencil clear, and every shadow pixel was rejected.)
            ZWrite On
            ZTest [_ShadowZTest]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float height : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                // The same ground projection as the baked sprites' CharacterCastShadow — a shear
                // about the feet in board (screen) space — so 2D and 3D units on the board cast
                // identical shadows: x += shear·h, y = squash·h, h = height above the feet.
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 rel = world.xy - _C3D_Origin.xy;
                float h = max(rel.y, 0.0);
                float2 sheared = float2(rel.x + _C3D_ShadowShape.x * h, min(rel.y, 0.0) + _C3D_ShadowShape.y * h);
                float3 ground = float3(_C3D_Origin.xy + sheared, world.z);
                o.vertex = WithNearness(mul(UNITY_MATRIX_VP, float4(ground, 1.0)), _DepthBase * 0.5);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.height = h / max(_C3D_Height, 1e-4);
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a - 0.5);
                DitherFade(i.screenPos);
                float opacity = _C3D_ShadowColor.a * lerp(1.0, _C3D_Params.z, saturate(i.height));
                if (_ShadowDebug > 0.5) return fixed4(saturate(i.height), 0, 1, 1); // blue at the feet .. magenta at the crown
                return fixed4(_C3D_ShadowColor.rgb, opacity * _C3D_Params.x);
            }
            ENDCG
        }

        // ── 2. The lit model ─────────────────────────────────────────────────────────────────
        Pass
        {
            Name "Model"
            Tags { "LightMode" = "Universal2D" }
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            // The miniature's base (Character3DBase) — adds the selection glow on its top face.
            #pragma multi_compile_local _ _MINIATURE_BASE

            float _C3D_Focus; // base only: 0 = idle, 0.65 = hovered, 1 = selected (smoothed)

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
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 objectPos : TEXCOORD4;
            };

            #if _MINIATURE_BASE
            // The selected miniature's base lights up: a soft, slowly breathing warm light over the
            // whole grey top (object space: unit radius, top at y = 0), brightest under the figure
            // and fading toward the rim — no ring. Gold motes rise from it (AmbientLife.Motes).
            float3 BaseGlow(float3 p)
            {
                if (_C3D_Focus <= 0.001 || p.y < -0.002) return 0;
                float r = length(p.xz);
                float fill = 1.0 - smoothstep(0.1, 0.95, r) * 0.6;
                float pulse = 0.8 + 0.2 * sin(_Time.y * 2.4);
                return float3(1.0, 0.78, 0.42) * fill * pulse * _C3D_Focus * 0.55;
            }
            #endif

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                // View-space z grows toward the camera; measured from the object's own origin so
                // the slab only holds this model's relative depth.
                float relative = UnityObjectToViewPos(v.vertex.xyz).z - UnityObjectToViewPos(float3(0, 0, 0)).z;
                o.vertex = WithNearness(o.vertex, _DepthBase + saturate(0.5 + relative * _DepthSlabScale) * _DepthSlabWidth);

                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.vertex);
                o.objectPos = v.vertex.xyz;
                return o;
            }

            float4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv);
                if (_ShadowDebug > 0.5) discard; // debug: show the whole shadow, unoccluded
                clip(albedo.a - 0.5);
                DitherFade(i.screenPos);

                // Two-sided: back faces (capes, sleeves) light from their visible side.
                float3 n = normalize(i.worldNormal) * (facing > 0 ? 1.0 : -1.0);

                float softness = max(_C3D_Params.y, 1e-3);
                float band = smoothstep(-softness, softness, dot(n, _C3D_LightDir) - _C3D_Params.w);
                float skyward = dot(n, normalize(_C3D_Up)) * 0.5 + 0.5;
                float3 ambient = lerp(_C3D_GroundColor.rgb, _C3D_SkyColor.rgb, skyward);
                // Capped at 1: the lit side shows the texture exactly as baked, only shade darkens it.
                float3 lit = albedo.rgb * min(ambient + _C3D_KeyColor.rgb * band, 1.0);
                float3 color = lerp(albedo.rgb, lit, _C3D_Params.x) * _Color.rgb;
                #if _MINIATURE_BASE
                color += BaseGlow(i.objectPos);
                #endif

                // The alignment outline is its own material (Character3DOutline): a fresnel rim here
                // broke up into patches along every cloth fold.
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
}
