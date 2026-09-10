Shader "SpaceFlop/CockpitNoFog"
{
    Properties { _BaseColor ("Color", Color) = (0.12,0.16,0.2,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        // Shared exclusion bit with PlanetNoFog and the fullscreen atmosphere pass.
        Stencil { Ref 64 Comp Always Pass Replace ReadMask 64 WriteMask 64 }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float shading = 0.7 + 0.3 * saturate(dot(normalize(input.normalWS), normalize(float3(0.3,1,0.5))));
                return half4(_BaseColor.rgb * shading, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex VertDepth
            #pragma fragment FragDepth
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            float4 VertDepth(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }
            half4 FragDepth() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
