Shader "Hidden/Metagame/OccludedInteractionOutline"
{
    Properties
    {
        _OutlineColor ("Color", Color) = (1, 0.78, 0.2, 0.95)
        _OutlineThickness ("Width in pixels", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_X(_InteractionMask);
            TEXTURE2D_X_FLOAT(_InteractionSceneDepth);
            float4 _OutlineColor;
            float _OutlineThickness;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }
            float2 Mask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_InteractionMask, sampler_PointClamp, saturate(uv)).rg;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 here = Mask(uv);
                // 실루엣 내부에만 선을 그린다. 가림 경계 자체에는 새 선을 만들지 않는다.
                if (here.y == 0) return 0;
                float rawDepth = SAMPLE_TEXTURE2D_X(_InteractionSceneDepth, sampler_PointClamp, uv).r;
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    rawDepth = 1 - rawDepth;
                    #endif
                    sceneDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                }
                // 깊이 정밀도와 자체 표면 차이로 생기는 깜빡임 방지.
                if (here.x <= sceneDepth + max(0.025, sceneDepth * 0.0001)) return 0;
                float2 stepUV = _OutlineThickness / _ScaledScreenParams.xy;
                bool edge = Mask(uv + float2(stepUV.x, 0)).y != here.y ||
                            Mask(uv - float2(stepUV.x, 0)).y != here.y ||
                            Mask(uv + float2(0, stepUV.y)).y != here.y ||
                            Mask(uv - float2(0, stepUV.y)).y != here.y;
                return edge ? _OutlineColor : 0;
            }
            ENDHLSL
        }
    }
}