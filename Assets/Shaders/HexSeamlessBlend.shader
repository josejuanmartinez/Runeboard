// Seamless hex terrain shader, shared by the in-game board (fed per hex through
// MaterialPropertyBlocks by HexSeamlessTerrain) and the Scenario Creator preview
// (fed through material properties by ScenarioCreatorWindow). Cross-fades each hex's rim with
// its up-to-6 neighbors so painted terrain reads as one continuous surface instead of a grid of
// independent tiles.
//
// How it works: for the neighbor in direction d, the fragment position is reflected across the
// shared hex edge (for regular hexes this maps our hex exactly onto the neighbor's hex), and the
// neighbor's art is sampled at that reflected point. Both tiles therefore sample the same pair of
// colors at the seam, so at 0.5 strength each side resolves to the same average and the seam
// disappears. All geometry is supplied by ScenarioCreatorWindow in a tile-local frame: origin at
// the hex center, +y up on screen, 1 unit = drawn tile width (_NeighborOffsetN = neighbor center
// offsets in that frame, _AspectY = drawn height / drawn width). Direction order must match
// ScenarioCreatorWindow.TryGetNeighborIndex: 0=E, 1=NE, 2=NW, 3=W, 4=SW, 5=SE.
Shader "RetroLOTR/HexSeamlessBlend"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _SpriteUV ("Sprite UV (own tile atlas rect)", Vector) = (0,0,1,1)

        _NeighborTex0 ("Neighbor E", 2D) = "black" {}
        _NeighborTex1 ("Neighbor NE", 2D) = "black" {}
        _NeighborTex2 ("Neighbor NW", 2D) = "black" {}
        _NeighborTex3 ("Neighbor W", 2D) = "black" {}
        _NeighborTex4 ("Neighbor SW", 2D) = "black" {}
        _NeighborTex5 ("Neighbor SE", 2D) = "black" {}

        _NeighborUV0 ("Neighbor UV E", Vector) = (0,0,1,1)
        _NeighborUV1 ("Neighbor UV NE", Vector) = (0,0,1,1)
        _NeighborUV2 ("Neighbor UV NW", Vector) = (0,0,1,1)
        _NeighborUV3 ("Neighbor UV W", Vector) = (0,0,1,1)
        _NeighborUV4 ("Neighbor UV SW", Vector) = (0,0,1,1)
        _NeighborUV5 ("Neighbor UV SE", Vector) = (0,0,1,1)

        _NeighborOffset0 ("Neighbor Offset E", Vector) = (0,0,0,0)
        _NeighborOffset1 ("Neighbor Offset NE", Vector) = (0,0,0,0)
        _NeighborOffset2 ("Neighbor Offset NW", Vector) = (0,0,0,0)
        _NeighborOffset3 ("Neighbor Offset W", Vector) = (0,0,0,0)
        _NeighborOffset4 ("Neighbor Offset SW", Vector) = (0,0,0,0)
        _NeighborOffset5 ("Neighbor Offset SE", Vector) = (0,0,0,0)

        _NeighborValid0 ("Neighbor Valid E", Float) = 0
        _NeighborValid1 ("Neighbor Valid NE", Float) = 0
        _NeighborValid2 ("Neighbor Valid NW", Float) = 0
        _NeighborValid3 ("Neighbor Valid W", Float) = 0
        _NeighborValid4 ("Neighbor Valid SW", Float) = 0
        _NeighborValid5 ("Neighbor Valid SE", Float) = 0

        _AspectY ("Cell Aspect (drawn H / drawn W)", Float) = 1.3
        _BlendStrength ("Blend Strength (0.5 = seamless)", Range(0,0.5)) = 0.5
        _BlendBand ("Blend Band (fraction of center-to-edge)", Range(0.05,1)) = 0.4
        // Must complete inside the overdraw overhang (art edge ~1.10 with TileOverdraw 1.10).
        _EdgeTrim ("Edge Trim (feather width past the seam)", Range(0.01,0.1)) = 0.08
        // Zero preserves the sprite's own outline beside fog; positive values fade at the seam.
        _FogFade ("Fog Fade (0 = preserve sprite edges)", Range(0,0.5)) = 0.25
        // 1 = editor GUI target (drawn with GL.sRGBWrite off, needs manual linear->gamma);
        // 0 = in-game camera (pipeline does the conversion itself).
        _GammaOut ("Editor gamma output", Float) = 0
        // 1 (default) = crop each tile's TileOverdraw margin down to its hex, so neighboring tiles
        // interlock without overlapping — independent of _BlendStrength (color cross-fade) and of
        // the grid overlay below, which always traces the true hex edge regardless of this value.
        // 0 = leave the overdrawn art uncropped (bleeds past the hex into neighbors) for a
        // deliberately unconstrained look, e.g. HexNoSeamlessBlendGame.
        _EdgeCrop ("Crop Overdraw To Hex", Float) = 1

        _GridOn ("Neon Grid Enabled", Float) = 1
        _GridColor ("Neon Grid Tint", Color) = (1, 1, 1, 1)
        _GridIntensity ("Neon Grid Intensity", Range(0,2)) = 0.2
        _GridWidth ("Neon Grid Core Width (px)", Range(0.5,4)) = 1.2
        _GridGlowWidth ("Neon Grid Glow Width (px)", Range(0.5,12)) = 1
        _GridHueScale ("Neon Hue Cycle (per tile)", Range(0.02,0.5)) = 0.12
        _CellCenter ("Cell Center (map units, set per cell)", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0 // fwidth, for zoom-independent grid line width
            #include "UnityCG.cginc"

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

            sampler2D _MainTex;
            float4 _SpriteUV;

            sampler2D _NeighborTex0; sampler2D _NeighborTex1; sampler2D _NeighborTex2;
            sampler2D _NeighborTex3; sampler2D _NeighborTex4; sampler2D _NeighborTex5;
            float4 _NeighborUV0; float4 _NeighborUV1; float4 _NeighborUV2;
            float4 _NeighborUV3; float4 _NeighborUV4; float4 _NeighborUV5;
            float4 _NeighborOffset0; float4 _NeighborOffset1; float4 _NeighborOffset2;
            float4 _NeighborOffset3; float4 _NeighborOffset4; float4 _NeighborOffset5;
            float _NeighborValid0; float _NeighborValid1; float _NeighborValid2;
            float _NeighborValid3; float _NeighborValid4; float _NeighborValid5;

            float _AspectY;
            float _BlendStrength;
            float _BlendBand;
            float _EdgeTrim;
            float _FogFade;
            float _GammaOut;
            float _EdgeCrop;

            float _GridOn;
            fixed4 _GridColor;
            float _GridIntensity;
            float _GridWidth;
            float _GridGlowWidth;
            float _GridHueScale;
            float4 _CellCenter;

            float3 HueToRgb(float h)
            {
                h = frac(h);
                float r = abs(h * 6.0 - 3.0) - 1.0;
                float g = 2.0 - abs(h * 6.0 - 2.0);
                float b = 2.0 - abs(h * 6.0 - 4.0);
                return saturate(float3(r, g, b));
            }

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            // t (seam coordinate, 1 == the shared edge) for one direction, with no neighbor/glow
            // logic attached — used to find which of the 6 edges is nearest at a given pixel before
            // any of them draw their line (see tMax in frag()).
            float SeamT(float2 offset, float2 s)
            {
                float dist = length(offset);
                if (dist < 1e-4) return -1e6; // no geometry in this slot; never the nearest edge
                float halfD = 0.5 * dist;
                return dot(s, offset / dist) / halfD;
            }

            // Accumulates one neighbor's contribution for fragment position s (tile-local frame).
            // tMax is the largest of all 6 directions' t at this pixel (see frag()) — a direction's
            // grid line only draws where it IS that nearest edge.
            void AccumulateNeighbor(float valid, sampler2D tex, float4 rect, float2 offset,
                                    float2 s, float tMax, inout float3 rgbSum, inout float weightSum,
                                    inout float alphaMask, inout float glow)
            {
                float dist = length(offset);
                if (dist < 1e-4) return;

                float2 u = offset / dist;
                float dAlong = dot(s, u);
                float halfD = 0.5 * dist; // the shared edge lies halfway to the neighbor's center
                float t = dAlong / halfD; // 1 at the seam

                // Neon grid: a crisp core line plus a soft exponential halo along the seam, in
                // screen pixels via fwidth so the width is zoom-independent. Computed for all 6
                // directions (not just valid neighbors) so map-border hexes are outlined too.
                // Both overlapping tiles emit identical glow at the seam, so premultiplied
                // compositing keeps the line continuous regardless of draw order.
                //
                // t alone only constrains distance PERPENDICULAR to this edge (_GridWidth/
                // _GridGlowWidth) — along the edge, t stays exactly 1 forever, so without a second
                // bound this "line" is geometrically infinite, previously only ever cut off by
                // alpha (the overdraw crop, or the sprite's own art) at the true hex vertices. With
                // _EdgeCrop disabled that no longer happens, so nearestEdgeMask below is the actual
                // length bound: this direction only draws where it is the closest of the 6 edges,
                // matching the hexagon's true Voronoi boundary independent of alpha/_EdgeCrop.
                float seamPx = abs(t - 1.0) / max(fwidth(t), 1e-5);
                float core = saturate(1.0 - seamPx / _GridWidth);
                float halo = exp2(-seamPx / _GridGlowWidth);
                float nearestEdgeMask = smoothstep(-fwidth(t) * 2.0, 0.0, t - tMax);
                glow = max(glow, (core + halo * 0.2) * nearestEdgeMask);

                // Neighbor state: 1 = rendered neighbor (blend + feather), 0 = no neighbor at all
                // (map border: crisp edge), -1 = fog-of-war neighbor (exists but is not rendered).
                // Toward fog we fade our OWN alpha to zero at the seam — the rim dissolves softly
                // into the fog background. No sampling of the hidden art (no info leak), and the
                // usual double-translucency worry doesn't apply since only this side renders.
                if (valid < -0.5)
                {
                    // Do not use smoothstep(1, 1, t) for a disabled fade: that would
                    // still hard-clip the sprite, exposing its region underlay.
                    if (_FogFade > 0.0)
                        alphaMask = min(alphaMask, 1.0 - smoothstep(1.0 - _FogFade, 1.0, t));
                    return;
                }
                if (valid < 0.5) return;

                // Feather our own alpha ONLY beyond the seam (t > 1), i.e. in the overdraw
                // overhang that lies over the neighbor's home cell. There the neighbor is fully
                // opaque underneath, so total coverage stays 100% no matter which tile is drawn on
                // top — while the tile art's baked border ring (which sits entirely in the
                // overhang) fades to nothing. Starting the fade before the seam is wrong: both
                // overlapping tiles end up translucent at the seam and the window background
                // bleeds through as a visible grid.
                // Only edges that actually have a neighbor are feathered (map borders stay crisp).
                // Gated on _EdgeCrop, NOT _BlendStrength: whether to crop the overdraw is a
                // separate concern from whether to color-blend across the seam. Cropping is what
                // keeps adjacent tiles from overlapping (default on, for tiling correctness);
                // a material can disable it for a deliberately unconstrained look
                // (HexNoSeamlessBlendGame) without that decision touching the grid overlay at all
                // — the grid's glow/core/halo below is computed independently of alphaMask/_EdgeCrop
                // and always traces the true hex edge (t == 1) regardless of this setting.
                if (_EdgeCrop > 0.5)
                    alphaMask = min(alphaMask, 1.0 - smoothstep(1.0, 1.0 + _EdgeTrim, t));

                // Weight ramps 0 -> 1 over the outer _BlendBand fraction of the way from the hex
                // center to the shared edge.
                float band = _BlendBand * halfD;
                float w = saturate((dAlong - (halfD - band)) / band);
                if (w <= 0.0) return;

                // Reflect s across the shared edge; expressed in the neighbor's local frame this
                // collapses to s - 2*dot(s,u)*u. At the seam the reflected point IS the seam
                // point, so both tiles sample identical colors there (C0 continuity).
                float2 sn = s - 2.0 * dAlong * u;
                float2 nLocal = saturate(float2(sn.x + 0.5, sn.y / _AspectY + 0.5));
                float2 uv = rect.xy + nLocal * rect.zw;

                // Small cross average softens pixel-level mismatch between the two tiles' art.
                // Dividing the summed RGB by the summed alpha un-premultiplies the bilinear/mip
                // fringe at the art's edge, so the transparent canvas margin contributes no color
                // — naively averaging its raw (black) RGB is what used to darken every rim.
                float2 texel = rect.zw * 0.02;
                fixed4 c = tex2D(tex, uv);
                c += tex2D(tex, uv + float2(texel.x, 0));
                c += tex2D(tex, uv - float2(texel.x, 0));
                c += tex2D(tex, uv + float2(0, texel.y));
                c += tex2D(tex, uv - float2(0, texel.y));
                float3 rgb = saturate(c.rgb / max(c.a, 1e-3));
                float alpha = c.a / 5.0;

                // Fade out samples that are mostly transparent margin (nothing real to blend to).
                w *= smoothstep(0.0, 0.35, alpha);

                rgbSum += w * rgb;
                weightSum += w;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 own = tex2D(_MainTex, i.uv);

                // Tile-local frame: origin at hex center, +y up on screen, 1 unit = tile width.
                float2 localUV = (i.uv - _SpriteUV.xy) / _SpriteUV.zw;
                float2 s = float2(localUV.x - 0.5, (localUV.y - 0.5) * _AspectY);

                // Which of the 6 edges is nearest at this pixel — see AccumulateNeighbor/SeamT.
                float tMax = max(
                    max(max(SeamT(_NeighborOffset0.xy, s), SeamT(_NeighborOffset1.xy, s)),
                        max(SeamT(_NeighborOffset2.xy, s), SeamT(_NeighborOffset3.xy, s))),
                    max(SeamT(_NeighborOffset4.xy, s), SeamT(_NeighborOffset5.xy, s)));

                float3 rgbSum = float3(0, 0, 0);
                float weightSum = 0.0;
                float alphaMask = 1.0;
                float glow = 0.0;
                AccumulateNeighbor(_NeighborValid0, _NeighborTex0, _NeighborUV0, _NeighborOffset0.xy, s, tMax, rgbSum, weightSum, alphaMask, glow);
                AccumulateNeighbor(_NeighborValid1, _NeighborTex1, _NeighborUV1, _NeighborOffset1.xy, s, tMax, rgbSum, weightSum, alphaMask, glow);
                AccumulateNeighbor(_NeighborValid2, _NeighborTex2, _NeighborUV2, _NeighborOffset2.xy, s, tMax, rgbSum, weightSum, alphaMask, glow);
                AccumulateNeighbor(_NeighborValid3, _NeighborTex3, _NeighborUV3, _NeighborOffset3.xy, s, tMax, rgbSum, weightSum, alphaMask, glow);
                AccumulateNeighbor(_NeighborValid4, _NeighborTex4, _NeighborUV4, _NeighborOffset4.xy, s, tMax, rgbSum, weightSum, alphaMask, glow);
                AccumulateNeighbor(_NeighborValid5, _NeighborTex5, _NeighborUV5, _NeighborOffset5.xy, s, tMax, rgbSum, weightSum, alphaMask, glow);

                fixed4 result = own;
                if (weightSum > 1e-4)
                    result.rgb = lerp(own.rgb, rgbSum / weightSum, _BlendStrength * saturate(weightSum));

                result.a *= alphaMask;
                result *= i.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                // ScenarioCreatorWindow draws with GL.sRGBWrite off, so the GUI target stores our
                // output raw. In a Linear project the sampled values are linear — convert back to
                // gamma there (_GammaOut 1, editor .mat) or the map preview renders too dark. The
                // game camera pipeline does its own linear->sRGB conversion, so in-game the
                // runtime material keeps _GammaOut 0 or everything washes out.
                if (_GammaOut > 0.5) result.rgb = LinearToGammaSpace(result.rgb);
                #endif
                // Neon grid overlay, blended post-gamma so bright tints still pop like emissive
                // light. The hue is derived from the MAP-space position (_CellCenter + s), so both
                // tiles at a seam compute the identical color and the rainbow flows continuously
                // across the grid. Blending (not adding) toward _GridColor — rather than only ever
                // brightening — is what lets a dark/black _GridColor actually darken the seam into
                // a solid line instead of contributing nothing (black added to anything is a no-op).
                float hue = dot(_CellCenter.xy + s, float2(1.0, 0.8)) * _GridHueScale;
                float3 neon = HueToRgb(hue) * _GridColor.rgb;
                float gridMask = saturate(glow * _GridIntensity * _GridOn);
                result.rgb = lerp(result.rgb, neon, gridMask);
                result.rgb *= result.a;
                return result;
            }
            ENDCG
        }
    }
}
