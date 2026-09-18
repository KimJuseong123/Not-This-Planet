// URP compatible vertex-color unlit material used by stars and distant markers.
Shader "NotThisPlanet/Flat"
{
    Properties
    {
        _Color ("색", Color) = (1, 1, 1, 1)

        // 반짝임 (S15P21B202-546). **기본값 0 이어야 한다** — 이 셰이더는 별뿐 아니라
        // 먼 표시등에도 쓰인다. 0 이면 곱하는 값이 정확히 1 이라 그쪽은 한 픽셀도
        // 바뀌지 않는다. 별 머티리얼만 값을 올린다.
        _TwinkleAmount ("반짝임 깊이", Range(0, 1)) = 0
        _TwinkleSpeed ("반짝임 빠르기", Float) = 2.4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-100" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _TwinkleAmount;
                half _TwinkleSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 정점 색의 알파는 이 별의 위상(0~1)이다. 불투명 패스라 알파는 그리는 데
                // 쓰이지 않으므로, 별마다 다른 수를 나르는 자리로 쓴다.
                half phase = input.color.a * 6.2831853;
                half wave = 0.5 + 0.5 * sin(_Time.y * _TwinkleSpeed + phase);
                half twinkle = lerp(1 - _TwinkleAmount, 1, wave);

                // 알파는 1 로 고정한다. 위상을 그대로 내보내면 별마다 투명도가 달라진다.
                return half4(_Color.rgb * input.color.rgb * twinkle, 1);
            }
            ENDHLSL
        }

        // SSAO requests a depth-normal prepass. Opaque surfaces must participate
        // so depth-sampled volumes (including the magnetic storm) stop here.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _TwinkleAmount;
                half _TwinkleSpeed;
            CBUFFER_END
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitDepthNormalsPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            Cull Off
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex VertDepth
            #pragma fragment FragDepth
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _TwinkleAmount;
                half _TwinkleSpeed;
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
