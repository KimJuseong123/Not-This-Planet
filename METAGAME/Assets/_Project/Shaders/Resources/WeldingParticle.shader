// Soft additive particle used by the plasma welder. The radial alpha removes
// the rectangular silhouette of Unity's billboard particle mesh.
Shader "NotThisPlanet/WeldingParticle"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 0.72, 0.42, 0.72)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "WeldingParticle"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

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
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half2 centered = input.uv * 2.0h - 1.0h;
                half radial = saturate(1.0h - dot(centered, centered));
                radial *= radial;
                half alpha = input.color.a * radial;
                return half4(input.color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
