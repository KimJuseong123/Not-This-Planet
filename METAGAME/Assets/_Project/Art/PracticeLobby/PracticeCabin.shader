Shader "NotThisPlanet/Practice Cabin"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _Glow ("Soft glow", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "CabinForward"
            // Forward-only also works in the project's deferred renderer.
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Glow;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; half3 normalWS:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                // Art-directed broad ceiling light: stable soft toy shading, no noisy texture.
                half3 normal=normalize(input.normalWS);
                half key=saturate(dot(normal,normalize(half3(-0.35,0.85,-0.4)))*0.5h+0.5h);
                half shade=lerp(0.55h,1.0h,key);
                half3 tint=lerp(half3(0.87,0.9,1.0),half3(1,0.98,0.94),key);
                half3 color=_BaseColor.rgb*shade*tint;
                return half4(lerp(color,_BaseColor.rgb,_Glow),1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
