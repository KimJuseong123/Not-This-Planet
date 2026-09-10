Shader "SpaceFlop/TractorFieldBeam"
{
    Properties
    {
        _Visibility ("Visibility", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Visibility;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float endFade = smoothstep(0, 0.04, input.uv.y) * (1 - smoothstep(0.72, 1, input.uv.y));
                float face = abs(dot(normalize(input.normalWS), normalize(GetWorldSpaceViewDir(input.positionWS))));
                float edgeFade = smoothstep(0.02, 0.45, face);
                float streak = pow(saturate(sin(input.uv.x * 150.796 + sin(input.uv.y * 7 + _Time.y))), 12);
                float flow = pow(saturate(sin(input.uv.y * 55 + _Time.y * 6 + input.uv.x * 25.133)), 14);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams);
                float beamDepth = -TransformWorldToView(input.positionWS).z;
                float softContact = saturate((sceneDepth - beamDepth) / 0.5);
                float alpha = input.color.a * _Visibility * endFade * edgeFade * softContact;
                return half4(input.color.rgb * (1.2 + streak * 1.8 + streak * flow * 4), alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
