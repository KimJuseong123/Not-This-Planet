// URP compatible transparent rim material. The shader name is kept stable for Prim.
Shader "NotThisPlanet/Ghost"
{
    Properties
    {
        _Color ("색", Color) = (0.55, 0.95, 0.70, 0.35)
        _RimPower ("Rim Power", Range(0.5, 6)) = 2.2
        _RimBoost ("Rim Boost", Range(0, 4)) = 1.6
        _Pulse ("Pulse", Range(0, 2)) = 0.0
        _FogStrength ("Fog Strength", Range(0, 1)) = 1.0
        _AtmosphereFogExemptMask ("Atmosphere Fog Exempt Mask", Float) = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        // Bit 6 is shared with the fullscreen atmosphere pass. Only materials
        // that opt in through _AtmosphereFogExemptMask write it, so existing
        // Ghost effects keep their normal atmosphere behavior.
        Stencil
        {
            Ref 64
            Comp Always
            Pass Replace
            ReadMask 64
            WriteMask [_AtmosphereFogExemptMask]
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _RimPower;
                half _RimBoost;
                half _Pulse;
                half _FogStrength;
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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = 1.0h - saturate(dot(normalWS, viewDir));
                rim = pow(rim, _RimPower) * _RimBoost;

                half pulse = 1.0h + _Pulse * 0.35h * sin(_Time.y * 3.4h);
                half4 color = _Color;
                color.rgb *= (0.75h + rim) * pulse;
                color.a = saturate(color.a * pulse + rim * 0.55h);
                half3 fogged = MixFog(color.rgb, ComputeFogFactor(input.positionCS.z));
                color.rgb = lerp(color.rgb, fogged, _FogStrength);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
