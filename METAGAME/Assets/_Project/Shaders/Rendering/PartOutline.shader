Shader "Hidden/Metagame/Rendering/PartOutline"
{
    // 마스크의 **번호가 바뀌는 자리**에 테두리를 올린다 (S15P21B202-562).
    //
    // 이것이 "용접하면 테두리가 하나로 합쳐진다" 의 전부다. 이웃 픽셀과 번호를 견주어
    // 다를 때만 선을 그리므로, 용접으로 같은 번호를 받은 두 파츠 사이에는 선이 생기지
    // 않는다. 파츠마다 따로 외곽선을 그리는 방식(부풀린 메시 뒤집기)으로는 이것이
    // 되지 않는다 — 각자 자기 윤곽을 그려, 결합해도 둘이 겹칠 뿐이다.
    //
    // 전체화면 패스의 뼈대는 Haze.shader 와 같다 — 같은 저장소에서 두 가지 방법을
    // 쓰지 않는다.
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.42, 0.92, 1, 1)
        _OutlineThickness ("Thickness (px)", Range(1, 10)) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "PartOutline"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_MetagamePartOutlineMask);
            SAMPLER(sampler_MetagamePartOutlineMask);

            half4 _OutlineColor;
            half _OutlineThickness;

            // 번호를 R8 에서 되읽는다. 정확히 같은지 견주면 양자화 오차에 걸리므로
            // 반 칸을 허용치로 둔다 — 이웃한 두 번호의 간격이 1/255 다.
            #define GROUP_EPSILON (0.5 / 255.0)

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half Group(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_MetagamePartOutlineMask,
                                          sampler_MetagamePartOutlineMask, uv).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half here = Group(uv);

                // **화면 픽셀로 잰다** (S15P21B202-562).
                //
                // 예전에는 마스크 텍스처의 _TexelSize 를 썼다. 그런데 그 값은 렌더그래프가
                // 잡아 준 텍스처에는 제대로 채워지지 않아서, 거리에 따라 두께가 들쭉날쭉해
                // 보였다 — 가까이 갈수록 얇아졌다. _ScreenParams 는 언제나 카메라의 실제
                // 해상도라 흔들리지 않는다(zw 가 1 + 1/너비, 1 + 1/높이 다).
                float2 step = (_ScreenParams.zw - 1.0) * _OutlineThickness;

                // 네 방향만 본다. 대각선까지 보면 모서리가 두꺼워져 파츠가 부어 보인다.
                half up    = Group(uv + float2(0.0,     step.y));
                half down  = Group(uv + float2(0.0,    -step.y));
                half left  = Group(uv + float2(-step.x, 0.0));
                half right = Group(uv + float2( step.x, 0.0));

                half diff = max(max(abs(here - up), abs(here - down)),
                                max(abs(here - left), abs(here - right)));
                if (diff <= GROUP_EPSILON) return half4(0, 0, 0, 0);

                // 빈 곳끼리는 경계가 아니다. 한쪽이라도 파츠여야 선을 긋는다.
                half occupied = max(here, max(max(up, down), max(left, right)));
                if (occupied <= GROUP_EPSILON) return half4(0, 0, 0, 0);

                return half4(_OutlineColor.rgb, _OutlineColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
