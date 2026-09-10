// URP compatible toy/clay surface. The shader name and material properties are
// intentionally kept stable so runtime-created Prim materials keep working.
Shader "NotThisPlanet/Dough"
{
    Properties
    {
        _Color        ("색",         Color) = (0.90, 0.62, 0.50, 1)
        _Softness     ("Softness",   Range(0,1)) = 0.85
        _Wrap         ("Light Wrap", Range(0,1)) = 0.55
        _SSS          ("Subsurface", Range(0,2)) = 0.6
        _Gloss        ("Gloss",      Range(0,1)) = 0.45
        _Mottle       ("얼룩",       Range(0,1)) = 0.22
        _MottleScale  ("얼룩 크기",  Range(0.1,24)) = 1.4
        _Ambient      ("Ambient",    Range(0,2)) = 0.45
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Softness;
                half _Wrap;
                half _SSS;
                half _Gloss;
                half _Mottle;
                half _MottleScale;
                half _Ambient;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            float Hash31(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.shadowCoord = TransformWorldToShadowCoord(position.positionWS);
                return output;
            }

            half3 ShadeLight(half3 albedo, half3 normalWS, half3 viewDir, Light light)
            {
                half3 lightDir = normalize(light.direction);
                half wrap = lerp(0.18h, _Wrap, _Softness);
                half ndl = dot(normalWS, lightDir);
                half diffuse = saturate((ndl + wrap) / (1.0h + wrap));

                half3 halfway = normalize(lightDir + viewDir);
                half specularPower = lerp(46.0h, 22.0h, _Softness);
                half specular = pow(saturate(dot(normalWS, halfway)), specularPower) * _Gloss;
                half back = pow(saturate(dot(viewDir, -lightDir)), 2.0h) * _SSS * _Softness * 0.45h;

                half attenuation = light.distanceAttenuation * light.shadowAttenuation;
                half3 result = albedo * light.color * diffuse * attenuation;
                result += light.color * specular * attenuation;
                result += albedo * light.color * back * attenuation;
                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                float noiseA = ValueNoise(input.positionWS * _MottleScale);
                float noiseB = ValueNoise(input.positionWS * _MottleScale * 2.3);
                half mottle = lerp(1.0h - _Mottle * 0.30h, 1.0h + _Mottle * 0.16h,
                                   noiseA * 0.82h + noiseB * 0.18h);
                half3 albedo = _Color.rgb * mottle;

                Light mainLight = GetMainLight(input.shadowCoord);
                half3 color = ShadeLight(albedo, normalWS, viewDir, mainLight);
                color += SampleSH(normalWS) * albedo * _Ambient * lerp(0.75h, 1.0h, _Softness);

                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < additionalLightCount; lightIndex++)
                {
                    Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS);
                    color += ShadeLight(albedo, normalWS, viewDir, additionalLight);
                }
                #endif

                color = MixFog(color, ComputeFogFactor(input.positionCS.z));
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack Off
}
