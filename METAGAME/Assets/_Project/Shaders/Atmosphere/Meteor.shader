Shader "Metagame/Atmosphere/Meteor"
{
    Properties { _Color ("Color", Color) = (0.65, 0.82, 1, 0.75) }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 처음 보이는 파티클은 둥근 유성의 머리다. 이동하며 Particle Trail이
                // 같은 마스크를 길게 늘려 쓰므로 자연스럽게 캡슐형 직선 꼬리로 바뀐다.
                float2 centred = input.uv * 2.0 - 1.0;
                float radial = length(centred);
                half mask = 1.0h - smoothstep(0.68h, 1.0h, radial);
                half core = 1.0h - smoothstep(0.0h, 0.48h, radial);
                half4 color = _Color * input.color;
                color.rgb *= lerp(1.0h, 1.8h, core);
                color.a *= mask;
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
