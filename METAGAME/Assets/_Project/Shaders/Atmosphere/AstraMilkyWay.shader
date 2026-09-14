Shader "Metagame/Atmosphere/AstraMilkyWay"
{
    Properties
    {
        _Exposure ("Starlight", Range(.2, 3)) = 1.2
        _NebulaColor ("Nebula", Color) = (.32, .17, .55, 1)
        _CoreColor ("Galactic core", Color) = (.58, .70, .84, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Exposure;
                half4 _NebulaColor, _CoreColor;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.direction = i.positionOS;
                return o;
            }
            float Hash(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),
                                 lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                            lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),
                                 lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);
            }
            float Cloud(float3 p)
            {
                float n = Noise(p)*.53; p = p*2.13+17;
                n += Noise(p)*.27; p = p*2.07-11;
                n += Noise(p)*.13; return n+Noise(p*2.1)*.07;
            }
            float Stars(float2 uv, float scale, float cutoff)
            {
                float2 grid = uv*float2(scale,scale*.5), cell=floor(grid);
                float seed=Hash(float3(cell,scale));
                float2 center=float2(Hash(float3(cell,13)),Hash(float3(cell,27)))*.7+.15;
                float2 delta=frac(grid)-center;
                float radius=lerp(.018,.095,pow(seed,14));
                float aa=max(length(fwidth(grid))*.25,.015);
                float star=1-smoothstep(radius,radius+aa,length(delta));
                return star*step(cutoff,seed)*lerp(.45,1.7,pow(seed,9));
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 d=normalize(i.direction);
                float band=dot(d,normalize(float3(.35,.60,-.62)));
                float swirl=Cloud(d*7.5);
                float veil=exp(-pow((band+(swirl-.5)*.075)/.19,2));
                float spine=exp(-pow((band+(Cloud(d*24)-.5)*.07)/.052,2));
                float dust=pow(saturate(Cloud(d*31)-.32)*1.8,2);
                float bulge=pow(saturate(dot(d,normalize(float3(.1,.7,.65)))),6);
                float2 uv=float2(atan2(d.z,d.x)/6.2831853+.5,asin(clamp(d.y,-1,1))/3.1415927+.5);
                half3 sky=lerp(half3(.008,.012,.031),half3(.035,.025,.075),pow(saturate(1-abs(d.y)),4));
                sky+=_NebulaColor.rgb*veil*(.12+swirl*.5);
                sky+=_CoreColor.rgb*spine*(.12+bulge*.48)*(1-saturate(dust));
                sky*=1-saturate(spine*dust*.65);
                float stars=Stars(uv,1100,.958)+Stars(uv+float2(.231,.137),2400,.987)*(.3+veil*.6);
                half3 tint=lerp(half3(.7,.83,1),half3(1,.88,.68),Noise(d*130));
                sky+=stars*tint;
                return half4(sky*_Exposure,1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
