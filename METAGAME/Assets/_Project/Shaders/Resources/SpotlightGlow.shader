// 스포트라이트의 **보이는 부분**을 그린다 (S15P21B202-631).
//
// Unity 의 Light 컴포넌트는 표면을 밝힐 뿐 공기 중의 빛줄기를 그리지 않는다.
// 무대 조명처럼 보이려면 빛기둥·바닥 웅덩이·광원 원반을 **직접 그려야** 한다.
// 셋 다 이 셰이더 하나로 그리고, 모양만 _RadialMix 로 가른다.
Shader "NotThisPlanet/SpotlightGlow"
{
    Properties
    {
        _Color ("색", Color) = (0.86, 0.92, 1, 1)
        _Strength ("세기", Range(0, 4)) = 1
        // 0 이면 기둥(세로 페이드 + 시선각 페이드), 1 이면 원반(중심에서 방사 페이드)
        _RadialMix ("원반 정도", Range(0, 1)) = 0
        _CoreBoost ("중심 밝기", Range(1, 6)) = 2.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            // 가산 합성. 빛은 더해지는 것이지 가리는 것이 아니다.
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Strength;
                float _RadialMix;
                float _CoreBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
            };

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
                // ── 기둥: 위가 밝고 아래로 옅어진다 ────────────────────
                float down = 1 - smoothstep(0.0, 1.0, input.uv.y);
                float cone = saturate(0.18 + down * 0.95);

                // 옆에서 보면 벽면을 스치므로 가장자리를 부드럽게 한다.
                // 이걸 안 하면 원뿔의 윤곽선이 종이처럼 딱 끊긴다.
                float face = abs(dot(normalize(input.normalWS),
                                     normalize(GetWorldSpaceViewDir(input.positionWS))));
                cone *= lerp(0.25, 1.0, smoothstep(0.0, 0.55, face));

                // ── 원반: 중심이 밝고 가장자리로 사라진다 ──────────────
                float2 d = input.uv - 0.5;
                float r = saturate(length(d) * 2);
                float radial = pow(saturate(1 - r), 2.2);

                float shape = lerp(cone, radial, _RadialMix);
                float alpha = saturate(input.color.a * shape * _Strength);
                clip(alpha - 0.002);

                // 중심일수록 하얗게 뜨도록 살짝 밀어준다. 무대 조명의 느낌이 여기서 난다.
                half3 rgb = _Color.rgb * input.color.rgb * (1 + shape * (_CoreBoost - 1));
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
