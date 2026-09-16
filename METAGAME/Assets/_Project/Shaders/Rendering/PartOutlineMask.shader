Shader "Hidden/Metagame/Rendering/PartOutlineMask"
{
    // 핵심 파츠를 마스크 텍스처에 **덩어리 번호**로 그린다 (S15P21B202-562).
    //
    // 색이 아니라 번호다. 다음 패스가 이웃 픽셀의 번호를 견주어, 다른 자리에만 선을
    // 그린다. 용접으로 묶인 파츠가 같은 번호를 쓰면 그 사이에는 선이 생기지 않아
    // 저절로 하나의 윤곽이 된다.
    //
    // 번호는 0~1 로 정규화해 넣는다. R8 한 장이면 255 덩어리까지 구분되는데, 핵심
    // 파츠는 네 개라 남는다.
    Properties
    {
        _OutlineGroup ("Group (0~1)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "PartOutlineMask"
            Tags { "LightMode"="SRPDefaultUnlit" }

            // **깊이는 읽고 쓰지 않는다.** 읽어야 벽 뒤 파츠가 마스크에 안 찍힌다
            // (가려진 것까지 그리면 안내가 아니라 투시가 된다). 쓰지 않는 이유는
            // 이 패스가 카메라 깊이를 그대로 빌려 쓰기 때문이다 — 쓰면 원본이 상한다.
            ZTest LEqual
            ZWrite Off
            Cull Back
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // **CBUFFER 밖에 둔다.** 그려지는 파츠마다 값이 달라야 하는데, 재질 하나를
            // 공유하므로 남는 길은 둘뿐이다 — 렌더러마다 MaterialPropertyBlock 을
            // 붙이거나, 그리기 직전에 전역으로 넣거나.
            //
            // MPB 는 쓰지 않는다. 한 번 붙이면 그 렌더러가 SRP Batcher 대상에서 빠져
            // **파츠의 본 렌더링 배칭이 깨진다** — 테두리 하나 그리자고 파츠 전체를
            // 느리게 만드는 셈이다. 이 패스는 파츠를 순서대로 그리므로 그리기 직전에
            // SetGlobalFloat 로 넣으면 충분하다.
            half _OutlineGroup;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(_OutlineGroup, 0, 0, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
