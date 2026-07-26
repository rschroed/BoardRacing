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
Shader "BoardRacing/CourseSurface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GroundTex ("Ground detail", 2D) = "white" {}
        _RoadTex ("Road detail", 2D) = "white" {}
        _ShoulderTex ("Shoulder detail", 2D) = "white" {}
        _GroundTile ("Ground tile size (reference px)", Float) = 130
        _RoadTile ("Road tile size (reference px)", Float) = 88
        _ShoulderTile ("Shoulder tile size (reference px)", Float) = 110
        // Detail modulates the vertex color around mid grey, so a flat 0.5
        // texture is a no-op and the committed flat treatment is recoverable
        // by setting this to 0.
        _DetailStrength ("Detail strength", Range(0, 2)) = 1
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

                half3 ground = SAMPLE_TEXTURE2D(_GroundTex, sampler_GroundTex,
                    input.worldXY / max(_GroundTile, 1e-3)).rgb;
                half3 road = SAMPLE_TEXTURE2D(_RoadTex, sampler_RoadTex,
                    input.worldXY / max(_RoadTile, 1e-3)).rgb;
                half3 shoulder = SAMPLE_TEXTURE2D(_ShoulderTex, sampler_ShoulderTex,
                    input.worldXY / max(_ShoulderTile, 1e-3)).rgb;

                // Ground is what is left after road and shoulder claim their
                // share, so a vertex with neither weight samples ground.
                float groundWeight = saturate(1.0 - roadWeight - shoulderWeight);
                half3 detail = ground * groundWeight + road * roadWeight
                    + shoulder * shoulderWeight;

                // Modulate around mid grey: detail lightens and darkens the
                // authored color rather than replacing it, so the style value
                // stays the thing that decides what a surface looks like.
                half3 modulated = input.color.rgb * (detail * 2.0h);
                half3 rgb = lerp(input.color.rgb, modulated, strength);
                return half4(rgb, input.color.a);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
