Shader "Hidden/Metagame/Stage1DistanceFog"
{
    Properties { _BlitTexture("Source", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            // Existing planet/marker shaders reserve this bit for atmosphere exclusion.
            Stencil { Ref 64 ReadMask 64 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 _FogRange; // start, end, maximum opacity, exponential curve density
            float4 _FogTint;
            float _DistanceDesaturation;
            float4 _GroundMist; // maximum opacity, world-space base, thickness, drift speed
            float4 _MistTint;
            float MistHash(float3 p)
            {
                p = frac(p * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float MistNoise(float3 p)
            {
                float3 a = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(
                    lerp(lerp(MistHash(a), MistHash(a+float3(1,0,0)), f.x),
                         lerp(MistHash(a+float3(0,1,0)), MistHash(a+float3(1,1,0)), f.x), f.y),
                    lerp(lerp(MistHash(a+float3(0,0,1)), MistHash(a+float3(1,0,1)), f.x),
                         lerp(MistHash(a+float3(0,1,1)), MistHash(a+1), f.x), f.y), f.z);
            }
            float GroundMistAmount(float2 uv, float rawDepth)
            {
                if (_GroundMist.x <= 0) return 0;
                #if !UNITY_REVERSED_Z
                rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 surface = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 ray = surface - _WorldSpaceCameraPos;
                float lengthToSurface = length(ray);
                float3 direction = ray / max(lengthToSurface, .001);
                float thickness = max(_GroundMist.z, .1);
                float bottom = _GroundMist.y - thickness * .5;
                float top = _GroundMist.y + thickness;
                float entry = 0, exitDistance = min(lengthToSurface, 100);
                // Integrate only the ray segment INSIDE the fog layer, even when viewed from above.
                if (abs(direction.y) < .0001)
                {
                    if (_WorldSpaceCameraPos.y < bottom || _WorldSpaceCameraPos.y > top) return 0;
                }
                else
                {
                    float a = (bottom - _WorldSpaceCameraPos.y) / direction.y;
                    float b = (top - _WorldSpaceCameraPos.y) / direction.y;
                    entry = max(0, min(a,b));
                    exitDistance = min(exitDistance, max(a,b));
                }
                if (exitDistance <= entry) return 0;
                float stepLength = (exitDistance-entry)/32.0;
                float opticalDepth = 0;
                float3 drift = _Time.y * _GroundMist.w * float3(.35, .03, .16);
                [loop] for (int i=0; i<32; i++)
                {
                    float3 p = _WorldSpaceCameraPos + direction*(entry+(i+.5)*stepLength);
                    float h=(p.y-_GroundMist.y)/thickness;
                    float layer=smoothstep(-.5,0,h)*(1-smoothstep(0,1,h));
                    // Broad, continuous density variations, not distinct clouds or camera-facing cards.
                    float variation=lerp(.55,1.15,MistNoise(p*float3(.055,.13,.055)+drift));
                    float boundary=1-smoothstep(55,80,length(p.xz));
                    opticalDepth += layer*variation*boundary*stepLength*_GroundMist.x*.12;
                }
                return min(1-exp(-opticalDepth), .78);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float depth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                if (depth <= 0.00001) return source;
                #else
                if (depth >= 0.99999) return source;
                #endif
                float distance = LinearEyeDepth(depth, _ZBufferParams);
                float t = saturate((distance - _FogRange.x) / max(_FogRange.y - _FogRange.x, .01));
                // Smooth onset and bounded transmittance: never erase all surface contrast.
                t = t * t * (3.0 - 2.0 * t);
                float density = max(_FogRange.w, .01);
                float amount = (1.0 - exp(-density * t)) / (1.0 - exp(-density));
                // Distance grading only: nearby gameplay colors remain untouched.
                // Violet/magenta highlights receive extra attenuation, not a global material edit.
                float luminance = dot(source.rgb, float3(.2126, .7152, .0722));
                float peak = max(max(source.r, source.g), source.b);
                float magenta = saturate((min(source.r, source.b) - source.g) / max(peak, .001) * 3.0);
                float desaturation = saturate(t * _DistanceDesaturation * (1.0 + .35 * magenta));
                source.rgb = lerp(source.rgb, luminance.xxx, desaturation);
                amount *= saturate(_FogRange.z);
                source.rgb = lerp(source.rgb, _FogTint.rgb, amount);
                // A separate, gently moving low layer; guide beams are drawn afterwards.
                float mist = GroundMistAmount(uv, depth);
                source.rgb = lerp(source.rgb, _MistTint.rgb, mist);
                return source;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
