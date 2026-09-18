Shader "Metagame/Atmosphere/Galaxy"
{
    Properties
    {
        _MainTex ("Milky Way Texture", 2D) = "white" {}
        _CoreColor ("Core Color", Color) = (0.72, 0.66, 1, 0.38)
        _EdgeColor ("Edge Color", Color) = (0.2, 0.36, 0.76, 0)
        _Intensity ("Intensity", Range(0, 2)) = 0.62
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
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
                half4 _CoreColor;
                half4 _EdgeColor;
                half _Intensity;
            CBUFFER_END
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float SmoothNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);
                float bottom = lerp(Hash(cell), Hash(cell + float2(1.0, 0.0)), local.x);
                float top = lerp(Hash(cell + float2(0.0, 1.0)), Hash(cell + 1.0), local.x);
                return lerp(bottom, top, local.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float centre = 1.0 - abs(input.uv.y * 2.0 - 1.0);
                // 넓은 구간에 걸쳐 가장자리를 감쇠해 리본처럼 잘린 경계가 보이지 않게 한다.
                float softBand = smoothstep(0.0, 0.86, centre);
                softBand = pow(saturate(softBand), 0.85);
                float2 cloudUv = float2(input.uv.x * 24.0, input.uv.y * 4.0);
                float cloud = lerp(0.72, 1.0, SmoothNoise(cloudUv));
                half4 color = lerp(_EdgeColor, _CoreColor, softBand);
                half3 milkyWay = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                float luminance = dot(milkyWay, half3(0.2126, 0.7152, 0.0722));
                color.rgb *= milkyWay * (1.25 + luminance) * _Intensity * cloud;
                color.a *= softBand * saturate(luminance * 1.8) * cloud * _Intensity;
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
