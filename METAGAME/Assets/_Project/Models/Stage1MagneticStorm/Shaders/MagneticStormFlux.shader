Shader "Metagame/Atmosphere/MagneticStormFlux"
{
    Properties
    {
        [HDR] _Color ("Cyan emission", Color) = (0.035,2.3,3.8,1)
        [NoScaleOffset] _EmissionMap ("Emission tint", 2D) = "white" {}
        _StormPhase ("Loop phase", Float) = 0
        _StormOpacity ("Visibility", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off Blend SrcAlpha One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _StormPhase, _StormOpacity;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p=v.positionOS.xyz;
                float a=6.2831853*(_StormPhase+v.uv.x);
                p.y+=.20*sin(a*2)+.07*sin(a*3);
                p.z+=.24*cos(a)+.08*sin(a*2);
                o.positionCS=TransformObjectToHClip(p);o.uv=v.uv;return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float phase=6.2831853*(i.uv.x*3-_StormPhase*2);
                float pulse=pow(.5+.5*sin(phase),5);
                float arc=pow(saturate(.5+.5*sin(6.2831853*(i.uv.x-_StormPhase))),2);
                half3 tint=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,float2(.5,.5)).rgb;
                return half4(_Color.rgb*lerp(half3(1,1,1),tint,.15)*(.18+pulse*.85),_StormOpacity*arc*.75);
            }
            ENDHLSL
        }
    }
}
