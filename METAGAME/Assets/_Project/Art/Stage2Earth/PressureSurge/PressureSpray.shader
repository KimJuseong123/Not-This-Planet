Shader "Metagame/Pressure Spray"
{
    Properties { _BaseColor("Water tint",Color)=(0.65,0.86,0.95,0.5) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.color=i.color;o.uv=i.uv;return o;}
            half4 frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;float r=dot(p,p);
                half alpha=(1-smoothstep(.12,1,r))*i.color.a*_BaseColor.a;
                half highlight=pow(saturate(1-length(p-float2(-.22,.2))),12);
                return half4(lerp(_BaseColor.rgb*i.color.rgb,half3(1,1,1),highlight*.8),alpha);
            }
            ENDHLSL
        }
    }
}
