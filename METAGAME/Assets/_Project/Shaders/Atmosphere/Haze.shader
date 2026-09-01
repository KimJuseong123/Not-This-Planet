Shader "Hidden/Metagame/Atmosphere/Haze"
{
    Properties { _BlitTexture ("Source", 2D) = "white" {} }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "AtmosphereHaze"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_BlitTexture);

            // These values are intentionally global rather than material properties.
            // AtmosphereController updates them per active stage/profile with
            // Shader.SetGlobal*, while the pass material itself stays reusable.
            half4 _MetagameAtmosphereHazeColor;
            half _MetagameAtmosphereHazeEnabled;
            half _MetagameAtmosphereHazeDensity;
            half _MetagameAtmosphereHazeStart;
            half _MetagameAtmosphereHazeEnd;
            half4 _MetagameAtmosphereGroundFogColor;
            half _MetagameAtmosphereGroundFogEnabled;
            half _MetagameAtmosphereGroundFogDensity;
            half _MetagameAtmosphereGroundFogBaseHeight;
            half _MetagameAtmosphereGroundFogHeightFalloff;
            half _MetagameAtmosphereGroundFogStart;
            half _MetagameAtmosphereGroundFogEnd;
            half _MetagameAtmosphereGroundFogNoiseScale;
            half2 _MetagameAtmosphereGroundFogNoiseSpeed;
            half _MetagameAtmosphereGroundFogNoiseStrength;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);
                float lower = lerp(Hash21(cell), Hash21(cell + float2(1.0, 0.0)), local.x);
                float upper = lerp(Hash21(cell + float2(0.0, 1.0)), Hash21(cell + 1.0), local.x);
                return lerp(lower, upper, local.y);
            }

            half GroundFogNoise(float2 positionWS)
            {
                float2 offset = _Time.y * _MetagameAtmosphereGroundFogNoiseSpeed;
                float broad = ValueNoise(positionWS * _MetagameAtmosphereGroundFogNoiseScale + offset);
                float detail = ValueNoise(positionWS * (_MetagameAtmosphereGroundFogNoiseScale * 2.1h) + offset * 1.7h);
                return saturate(broad * 0.72h + detail * 0.28h);
            }

            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.00001h;
                #else
                    return rawDepth >= 0.99999h;
                #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
                bool hazeEnabled = _MetagameAtmosphereHazeEnabled >= 0.5h;
                bool groundFogEnabled = _MetagameAtmosphereGroundFogEnabled >= 0.5h;
                if (!hazeEnabled && !groundFogEnabled) return source;

                float rawDepth = SampleSceneDepth(input.uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                if (hazeEnabled)
                {
                    float depthRange = max(_MetagameAtmosphereHazeEnd - _MetagameAtmosphereHazeStart, 0.001);
                    half depthFactor = saturate((eyeDepth - _MetagameAtmosphereHazeStart) / depthRange);
                    half haze = saturate(depthFactor * _MetagameAtmosphereHazeDensity);
                    source.rgb = lerp(source.rgb, _MetagameAtmosphereHazeColor.rgb, haze);
                }

                if (groundFogEnabled && !IsSkyDepth(rawDepth))
                {
                    float3 positionWS = ComputeWorldSpacePosition(input.uv, rawDepth, UNITY_MATRIX_I_VP);
                    float heightAboveGround = max(positionWS.y - _MetagameAtmosphereGroundFogBaseHeight, 0.0);
                    half heightFactor = exp(-heightAboveGround * _MetagameAtmosphereGroundFogHeightFalloff);
                    half distanceFactor = smoothstep(
                        _MetagameAtmosphereGroundFogStart,
                        max(_MetagameAtmosphereGroundFogEnd, _MetagameAtmosphereGroundFogStart + 0.001h),
                        eyeDepth);
                    half noise = GroundFogNoise(positionWS.xz);
                    half noiseFactor = lerp(
                        1.0h - _MetagameAtmosphereGroundFogNoiseStrength,
                        1.0h + _MetagameAtmosphereGroundFogNoiseStrength,
                        noise);
                    half groundFog = saturate(
                        _MetagameAtmosphereGroundFogDensity * heightFactor * distanceFactor * noiseFactor);
                    source.rgb = lerp(source.rgb, _MetagameAtmosphereGroundFogColor.rgb, groundFog);
                }

                return source;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
