// Course surface detail (issue #161).
//
// Replaces Sprites/Default on the race surface mesh. The mesh is still one
// paint-ordered, vertex-colored triangle list, so this shader keeps the
// transparent-queue, no-cull, no-depth-write setup that made append order the
// layering rule.
//
// Mapping is world-space, not per-vertex: the surface camera pins world XY to
// RaceLayout's 1920x1080 reference pixels, so a fragment's own world position
// divided by a tile size in those same pixels IS a seam-free tiling UV. That
// costs no vertex UVs and has nothing to resolve at closed-loop joins, pit
// junctions, or the Hourglass/Infinity self-crossings. Mip selection therefore
// comes from the screen-space derivatives of that computed UV, which tex2D
// takes automatically -- there is no vertex UV distribution for Unity to key
// mip streaming off, which is why the textures ship with mips built in.
//
// UV0 is repurposed as the detail channel: (roadWeight, shoulderWeight,
// detailStrength, unused). Ground is the complement of road and shoulder, so
// three samplers cover four surfaces. detailStrength 0 falls back to flat
// vertex color, which is how markings -- stripes, start line, pit boxes,
// crossing shadow and parapets -- stay crisp over a textured road.
//
// The tiles carry their own color. An earlier revision kept them neutral and
// modulated the vertex color by detail * 2, which coupled absolute grain to
// base brightness: the road at 0.29 luminance received a tenth of the ground's
// variation from a tile authored with more amplitude, and read as flat. Color
// therefore lives in the art, where a dark asphalt can be authored with the
// contrast it actually needs, and where hue can vary at all -- greyscale times
// a color can only ever vary value.
//
// The per-surface tint is a grade on top, not the color source, and defaults to
// white. Pit lane and corners currently share the road tile ungraded, so every
// road-family surface renders identically: a flat baseline for judging the raw
// assets before any grading is layered on.
Shader "BoardRacing/CourseSurface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GroundTex ("Ground detail", 2D) = "white" {}
        _RoadTex ("Road detail", 2D) = "white" {}
        _ShoulderTex ("Shoulder detail", 2D) = "white" {}
        _GroundTile ("Ground tile size (reference px)", Float) = 128
        _RoadTile ("Road tile size (reference px)", Float) = 128
        _ShoulderTile ("Shoulder tile size (reference px)", Float) = 128
        // Grades on top of the authored tile color. White is the baseline.
        _GroundTint ("Ground tint", Color) = (1, 1, 1, 1)
        _RoadTint ("Road tint", Color) = (1, 1, 1, 1)
        _ShoulderTint ("Shoulder tint", Color) = (1, 1, 1, 1)
        // Per-surface enables. An unbound sampler reads white, so a surface
        // without a tile has to fall back to flat vertex color rather than
        // blowing out -- that is what lets a partial theme (say ground and road
        // but no shoulder tile) render correctly instead of catastrophically.
        _GroundOn ("Ground detail enabled", Range(0, 1)) = 0
        _RoadOn ("Road detail enabled", Range(0, 1)) = 0
        _ShoulderOn ("Shoulder detail enabled", Range(0, 1)) = 0
        // 0 falls back to flat vertex color, which is both how markings stay
        // crisp and how the committed pre-texture treatment stays reachable.
        _DetailStrength ("Detail strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "CourseSurfaceUnlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float4 detail     : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float4 detail     : TEXCOORD0;
                float2 worldXY    : TEXCOORD1;
            };

            TEXTURE2D(_GroundTex);   SAMPLER(sampler_GroundTex);
            TEXTURE2D(_RoadTex);     SAMPLER(sampler_RoadTex);
            TEXTURE2D(_ShoulderTex); SAMPLER(sampler_ShoulderTex);

            CBUFFER_START(UnityPerMaterial)
                float _GroundTile;
                float _RoadTile;
                float _ShoulderTile;
                half4 _GroundTint;
                half4 _RoadTint;
                half4 _ShoulderTint;
                float _GroundOn;
                float _RoadOn;
                float _ShoulderOn;
                float _DetailStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.worldXY = positions.positionWS.xy;
                output.color = input.color;
                output.detail = input.detail;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float roadWeight = saturate(input.detail.x);
                float shoulderWeight = saturate(input.detail.y);
                float strength = saturate(input.detail.z) * _DetailStrength;

                half3 flat3 = input.color.rgb;
                half3 ground = lerp(flat3, SAMPLE_TEXTURE2D(_GroundTex, sampler_GroundTex,
                    input.worldXY / max(_GroundTile, 1e-3)).rgb * _GroundTint.rgb, _GroundOn);
                half3 road = lerp(flat3, SAMPLE_TEXTURE2D(_RoadTex, sampler_RoadTex,
                    input.worldXY / max(_RoadTile, 1e-3)).rgb * _RoadTint.rgb, _RoadOn);
                half3 shoulder = lerp(flat3, SAMPLE_TEXTURE2D(_ShoulderTex, sampler_ShoulderTex,
                    input.worldXY / max(_ShoulderTile, 1e-3)).rgb * _ShoulderTint.rgb, _ShoulderOn);

                // Ground is what is left after road and shoulder claim their
                // share, so a vertex with neither weight samples ground.
                float groundWeight = saturate(1.0 - roadWeight - shoulderWeight);
                half3 detail = ground * groundWeight + road * roadWeight
                    + shoulder * shoulderWeight;

                // The tile supplies the color outright; vertex color is the
                // flat fallback the surface returns to as strength drops, and
                // is what markings (strength 0) keep.
                half3 rgb = lerp(flat3, detail, strength);
                return half4(rgb, input.color.a);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
