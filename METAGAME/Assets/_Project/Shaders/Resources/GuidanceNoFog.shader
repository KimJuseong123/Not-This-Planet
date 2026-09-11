Shader "Metagame/Guidance/NoFog"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (0.12,0.16,0.2,1)
        [Toggle] _EmissionEnabled ("Emission", Float) = 1
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionIntensity ("Emission Intensity", Range(0, 5)) = 1.25
        _MaxBrightness ("Color Preserving Brightness Limit", Range(0.5, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }
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
                half4 _EmissionColor;
                float _EmissionIntensity;
                float _EmissionEnabled;
                float _MaxBrightness;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 radiance = max(0, _BaseColor.rgb + _EmissionColor.rgb * _EmissionIntensity * step(0.5, _EmissionEnabled));
                // Scale every channel together: per-channel clipping turns amber/green
                // yellow and washes out the core. Bloom uses a lower threshold instead.
                half peak = max(radiance.r, max(radiance.g, radiance.b));
                radiance *= min(1.0h, _MaxBrightness / max(peak, 0.0001h));
                return half4(radiance, 1);
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
                half4 _EmissionColor;
                float _EmissionIntensity;
                float _EmissionEnabled;
                float _MaxBrightness;
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
