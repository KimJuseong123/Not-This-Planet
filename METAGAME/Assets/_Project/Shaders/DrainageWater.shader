Shader "Metagame/Drainage Water"
{
    Properties
    {
        _Shallow("Shallow water", Color) = (0.12,0.32,0.26,1)
        _Deep("Deep water", Color) = (0.018,0.075,0.067,1)
        _Flow("Flow XZ", Vector) = (0,-0.3,0,0)
        _Environment("Captured drainage environment", Cube) = "grey" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Shallow, _Deep;
            float4 _Flow;
            CBUFFER_END
            float4 _WaterRipples[4];
            TEXTURECUBE(_Environment); SAMPLER(sampler_Environment);
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float fog:TEXCOORD1; };
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float Height(float2 p)
            {
                float2 q=p-_Flow.xy*_Time.y;
                float h=sin(q.x*2.7+q.y*1.4+sin(q.y*.5))*.016
                    +sin(q.y*4.3-q.x*1.2+_Time.y*.6)*.008;
                h+=(Noise(q*5+Noise(q*.7)*2)-.5)*.025;
                for(int i=0;i<4;i++)
                {
                    float d=distance(p,_WaterRipples[i].xy);
                    h+=sin(d*18-_Time.y*11)*exp(-d*2)*_WaterRipples[i].z*.022;
                }
                return h;
            }
            Varyings Vert(Attributes a)
            {
                Varyings o;
                o.world=TransformObjectToWorld(a.positionOS.xyz);
                o.world.y+=sin(o.world.x*2.7+o.world.z*1.4+_Time.y)*.012;
                o.positionCS=TransformWorldToHClip(o.world);o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i, FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target
            {
                float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
                float h=Height(i.world.xz);
                float3 n=normalize(float3((h-Height(i.world.xz+float2(.035,0)))/.035,1,(h-Height(i.world.xz+float2(0,.035)))/.035));
                float3 v=GetWorldSpaceNormalizeViewDir(i.world);
                n=IS_FRONT_VFACE(facing,n,-n);
                float eye=-TransformWorldToView(i.world).z;
                float depth=max(0,LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams)-eye);
                float2 refracted=uv+n.xz*.018*saturate(depth);
                // Do not pull foreground equipment into the refraction.
                float test=LinearEyeDepth(SampleSceneDepth(refracted),_ZBufferParams);
                refracted=test>eye?refracted:uv;
                float3 transmission=SampleSceneColor(refracted);
                float3 absorb=exp(-float3(.7,.29,.23)*depth);
                float3 tint=lerp(_Shallow.rgb,_Deep.rgb,1-exp(-depth*.5));
                float3 color=transmission*absorb+tint*(1-absorb);
                float fresnel=.025+.975*pow(1-saturate(dot(n,v)),5);
                float3 reflection=SAMPLE_TEXTURECUBE_LOD(_Environment,sampler_Environment,reflect(-v,n),1).rgb;
                color=lerp(color,reflection,fresnel*.85);
                Light sun=GetMainLight();
                float3 halfDir=normalize(v+sun.direction);
                color+=sun.color*pow(saturate(dot(n,halfDir)),240)*1.8;
                float flowNoise=Noise((i.world.xz-_Flow.xy*_Time.y)*7);
                float foam=(1-smoothstep(.02,.25,depth))*smoothstep(.25,.68,flowNoise);
                // Thin fragmented foam streaks, stretched in the direction of drainage.
                foam+=pow(saturate(Noise(float2(i.world.x*9,i.world.z*.5+_Time.y*.24))-.58)*2.38,4)*.2;
                color=lerp(color,float3(.71,.79,.69),saturate(foam)*.65);
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
