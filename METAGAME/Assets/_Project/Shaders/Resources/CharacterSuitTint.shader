Shader "NotThisPlanet/CharacterSuitTint"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 1
        _WhiteTint ("White Area Tint", Color) = (1, 1, 1, 1)
        _PreviewBrightness ("Preview Brightness", Range(1, 2.5)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 250

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _WhiteTint;
                half _BumpScale;
                half _PreviewBrightness;
                half _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = TransformWorldToShadowCoord(position.positionWS);
                return output;
            }

            half3 Shade(half3 albedo, half3 normalWS, half3 viewDirection, Light light)
            {
                half attenuation = light.distanceAttenuation * light.shadowAttenuation;
                half diffuse = saturate(dot(normalWS, light.direction));
                half3 halfDirection = SafeNormalize(light.direction + viewDirection);
                half specular = pow(saturate(dot(normalWS, halfDirection)), lerp(18.0h, 96.0h, _Smoothness));
                return (albedo * diffuse + specular * _Smoothness) * light.color * attenuation;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half brightest = max(sampled.r, max(sampled.g, sampled.b));
                half darkest = min(sampled.r, min(sampled.g, sampled.b));
                half chroma = brightest - darkest;
                half brightness = dot(sampled.rgb, half3(0.299h, 0.587h, 0.114h));
                half neutralMask = 1.0h - smoothstep(0.10h, 0.24h, chroma);
                half whiteMask = neutralMask * smoothstep(0.52h, 0.78h, brightness);
                half3 albedo = sampled.rgb * lerp(half3(1, 1, 1), _WhiteTint.rgb, whiteMask);
                albedo *= _PreviewBrightness;

                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = normalize(cross(normalWS, tangentWS) * input.tangentWS.w);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));

                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light mainLight = GetMainLight(input.shadowCoord);
                half3 color = Shade(albedo, normalWS, viewDirection, mainLight);
                // 커스터마이저의 어두운 배경에서도 흰 슈트의 형태와 색이 묻히지 않게
                // 환경광에 부드러운 최소 밝기를 더한다.
                color += SampleSH(normalWS) * albedo * 1.15h;
                color += albedo * 0.30h;

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < lightCount; lightIndex++)
                    color += Shade(albedo, normalWS, viewDirection, GetAdditionalLight(lightIndex, input.positionWS));
                #endif

                color = MixFog(color, ComputeFogFactor(input.positionCS.z));
                return half4(color, sampled.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack Off
}
