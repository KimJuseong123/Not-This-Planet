Shader "Metagame/Atmosphere/SkyGradient"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.015, 0.017, 0.032, 1)
        _HorizonColor ("Horizon", Color) = (0.12, 0.08, 0.18, 1)
        _NadirColor ("Nadir", Color) = (0.005, 0.006, 0.012, 1)
        _HorizonHeight ("Horizon Height", Range(-1, 1)) = 0
        _HorizonExponent ("Horizon Softness", Range(0.1, 8)) = 1.5
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _NadirColor;
                half _HorizonHeight;
                half _HorizonExponent;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.directionWS = TransformObjectToWorld(input.positionOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half height = normalize(input.directionWS).y;
                half horizon = saturate((height - _HorizonHeight) * 0.5h + 0.5h);
                horizon = pow(horizon, _HorizonExponent);
                half3 upper = lerp(_HorizonColor.rgb, _ZenithColor.rgb, horizon);
                half lower = saturate((-height + _HorizonHeight) * 0.5h + 0.5h);
                half3 color = lerp(upper, _NadirColor.rgb, lower * lower);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
