Shader "Metagame/Atmosphere/MagneticStormVolume"
{
    Properties
    {
        [NoScaleOffset] _DensityTex ("Cloud density (3D)", 3D) = "" {}
        [NoScaleOffset] _NoiseTex ("Turbulence (3D)", 3D) = "" {}
        _ShadowColor ("Violet shadow", Color) = (0.075,0.035,0.18,1)
        _LightColor ("Lavender scattering", Color) = (0.58,0.38,0.84,1)
        [HDR] _IonColor ("Ion scattering", Color) = (0.02,0.4,0.65,1)
        _Density ("Extinction", Range(0.1,4)) = 2.4
        _Steps ("Ray steps", Range(24,96)) = 64
        _BillowStrength ("Rolling cloud strength", Range(0,1.5)) = 1
        _FrontBillowStrength ("Travel direction billow", Range(0,3)) = 1.4
        _RearBillowStrength ("Opposite direction billow", Range(0,3)) = 1.8
        [HideInInspector] _TravelAxisUV ("Travel projection", Vector) = (0,0,1,0)
        [HideInInspector] _TravelOffsetUV ("Travel UV per metre", Vector) = (0,0,0.1,0)
        [HideInInspector] _UpOffsetUV ("Up UV per metre", Vector) = (0,0.2,0,0)
        [HideInInspector] _WakeAges ("Head age, tail age, lifetime, enabled", Vector) = (0,0,1,0)
        _Erosion ("Breaking wisps", Range(0,0.4)) = 0.22
        _StormPhase ("Loop phase", Float) = 0
        _StormOpacity ("Visibility", Range(0,1)) = 1
        _SmokeOpacity ("Smoke opacity", Range(0,1)) = 0.38
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Name "VolumetricSmoke"
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE3D(_DensityTex); SAMPLER(sampler_DensityTex);
            TEXTURE3D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowColor, _LightColor, _IonColor;
                float _Density, _Steps, _StormPhase, _StormOpacity;
                float _BillowStrength, _Erosion;
                float _SmokeOpacity;
                float _FrontBillowStrength, _RearBillowStrength;
                float4 _WakeAges;
                float4 _TravelAxisUV, _TravelOffsetUV, _UpOffsetUV;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                return o;
            }
            float DensityAt(float3 uv, float phase)
            {
                float a=phase*6.2831853;
                // Travelling rollers deform the density itself, not just its surface tint.
                // Integer temporal harmonics and repeat-noise offsets close the six-second loop.
                float along=dot((uv-.5)*2,_TravelAxisUV.xyz);
                float front=saturate(along*.5+.5);
                float3 travel=normalize(_TravelOffsetUV.xyz);
                float3 up=normalize(_UpOffsetUV.xyz);
                float3 across=normalize(cross(up,travel));
                float lateral=dot(uv-.5,across);
                float vertical=dot(uv-.5,up);
                // Static lateral phase variation keeps separate lobes instead of flat
                // wave sheets, without transporting the cloud sideways.
                float roller=along*6+sin(lateral*17)*2+vertical*4-a;
                float crossRoll=along*8+cos(lateral*23)*1.5+a*2;
                float lift=sin(roller);
                // Roll in the travel/up plane, not across the broad sweep width.
                float3 warp=_TravelOffsetUV.xyz*(.65*cos(roller)+.20*sin(crossRoll+vertical*6))
                    +_UpOffsetUV.xyz*(.35*lift+.10*sin(crossRoll));
                float billow=_BillowStrength*lerp(_RearBillowStrength,_FrontBillowStrength,smoothstep(.25,.75,front));
                float3 q=uv-warp*billow;
                // Taper the original box boundary so animated lobes cannot expose a cut plane.
                float3 border=min(uv,1-uv);
                float edge=smoothstep(0,.065,min(min(border.x,border.y),border.z));
                float3 warpedBorder=min(q,1-q);
                edge*=saturate(min(min(warpedBorder.x,warpedBorder.y),warpedBorder.z)*24);
                float shape=SAMPLE_TEXTURE3D_LOD(_DensityTex,sampler_DensityTex,saturate(q),0).r;
                // Advect the noise along travel as well; lateral phase stays fixed.
                float3 flow=float3(dot(q,across)*5,dot(q,up)*2.6,dot(q,travel)*3.5)
                    +float3(0,-phase,-phase*2);
                float noise=SAMPLE_TEXTURE3D_LOD(_NoiseTex,sampler_NoiseTex,flow,0).r;
                float remaining=saturate(1-lerp(_WakeAges.y,_WakeAges.x,front)/max(.001,_WakeAges.z));
                float wakeFade=lerp(1,remaining*remaining,_WakeAges.w);
                return saturate(shape*(.52+noise*.96)-(1-noise)*_Erosion)*edge*wakeFade;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 screenUV=GetNormalizedScreenSpaceUV(i.positionCS);
                float3 originWS=GetCameraPositionWS();
                float3 directionWS=normalize(i.positionWS-originWS);
                #if UNITY_REVERSED_Z
                    float nearDepth=1;
                #else
                    float nearDepth=UNITY_NEAR_CLIP_VALUE;
                #endif
                if(unity_OrthoParams.w>.5)
                {
                    originWS=ComputeWorldSpacePosition(screenUV,nearDepth,UNITY_MATRIX_I_VP);
                    directionWS=normalize(i.positionWS-originWS);
                }
                float3 originOS=TransformWorldToObject(originWS);
                // Keep this direction unnormalised: ray distance remains in world metres.
                float3 directionOS=mul((float3x3)unity_WorldToObject,directionWS);
                float3 safeDirection=lerp(-1,1,step(0,directionOS))*max(abs(directionOS),.00001);
                float3 extent=float3(12,2.5,5);
                float3 ta=(-extent-originOS)/safeDirection;
                float3 tb=( extent-originOS)/safeDirection;
                float3 nearT=min(ta,tb),farT=max(ta,tb);
                float start=max(max(nearT.x,nearT.y),nearT.z);
                float end=min(min(farT.x,farT.y),farT.z);
                start=max(start,0);
                float depth=SampleSceneDepth(screenUV);
                #if !UNITY_REVERSED_Z
                    depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
                #endif
                float3 sceneWS=ComputeWorldSpacePosition(screenUV,depth,UNITY_MATRIX_I_VP);
                end=min(end,dot(sceneWS-originWS,directionWS));
                if(end<=start || _StormOpacity<=.001) return 0;
                int steps=(int)clamp(_Steps,24,96);
                float stepLength=(end-start)/steps;
                // Stable subpixel dithering avoids temporal shimmer while hiding ray bands.
                float jitter=frac(52.9829189*frac(dot(floor(i.positionCS.xy),float2(.06711056,.00583715))));
                float distanceAlong=start+stepLength*(.25+.5*jitter);
                float transmittance=1;
                float3 radiance=0;
                [loop] for(int s=0;s<96;s++)
                {
                    if(s>=steps || transmittance<.015) break;
                    float3 uv=(originOS+directionOS*distanceAlong)/(extent*2)+.5;
                    float density=DensityAt(uv,_StormPhase)*_Density*_StormOpacity;
                    if(density>.004)
                    {
                        float upper=DensityAt(uv+float3(-.012,.055,.025),_StormPhase);
                        float farUpper=DensityAt(uv+float3(-.03,.14,.06),_StormPhase);
                        float light=exp(-upper*1.8-farUpper*1.6);
                        light=saturate(light*.88+uv.y*.12);
                        float3 color=lerp(_ShadowColor.rgb,_LightColor.rgb,light);
                        float ion=SAMPLE_TEXTURE3D_LOD(_NoiseTex,sampler_NoiseTex,uv*2+float3(_StormPhase,0,.2),0).r;
                        color+=_IonColor.rgb*smoothstep(.62,.85,ion)*.22;
                        float alpha=1-exp(-density*stepLength);
                        radiance+=transmittance*alpha*color;
                        transmittance*=1-alpha;
                    }
                    distanceAlong+=stepLength;
                }
                // Scale premultiplied color and alpha together; thick smoke still stays see-through.
                return half4(radiance,1-transmittance)*saturate(_SmokeOpacity);
            }
            ENDHLSL
        }
    }
}
