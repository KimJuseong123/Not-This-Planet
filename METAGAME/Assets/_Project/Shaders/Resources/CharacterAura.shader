Shader "NotThisPlanet/CharacterAura"
{
    Properties
    {
        _Intensity ("HDR Glow", Range(0, 8)) = 2.5
        _Star ("Star Shape", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
                half _Star;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
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
                half2 p = abs(input.uv * 2 - 1);
                half radius = length(p);
                half halo = pow(saturate(1 - radius), 3);
                half core = 1 - smoothstep(0.04h, 0.24h, radius);
                half rays = pow(saturate(1 - min(p.x, p.y) * 13), 2) * pow(saturate(1 - max(p.x, p.y)), 1.5h);
                half mask = saturate(core + halo * .45h + rays * _Star);
                // Procedural halo remains readable without post-processing; HDR feeds existing bloom.
                return half4(input.color.rgb * _Intensity, mask * input.color.a);
            }
            ENDHLSL
        }
    }
}
