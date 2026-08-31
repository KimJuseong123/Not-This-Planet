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

            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
                if (_MetagameAtmosphereHazeEnabled < 0.5h) return source;
                float rawDepth = SampleSceneDepth(input.uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float depthRange = max(_MetagameAtmosphereHazeEnd - _MetagameAtmosphereHazeStart, 0.001);
                half depthFactor = saturate((eyeDepth - _MetagameAtmosphereHazeStart) / depthRange);
                half haze = saturate(depthFactor * _MetagameAtmosphereHazeDensity);
                source.rgb = lerp(source.rgb, _MetagameAtmosphereHazeColor.rgb, haze);
                return source;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
