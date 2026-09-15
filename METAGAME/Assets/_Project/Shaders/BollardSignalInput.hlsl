#ifndef METAGAME_BOLLARD_SIGNAL_INPUT_INCLUDED
#define METAGAME_BOLLARD_SIGNAL_INPUT_INCLUDED

// Retain URP's material layout and PBR sampling in every pass. Only the neutral
// signal islands use _BaseColor; painted ivory/lavender must never be tinted.
#define InitializeStandardLitSurfaceData InitializeBollardOriginalSurfaceData
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
#undef InitializeStandardLitSurfaceData

void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
{
    InitializeBollardOriginalSurfaceData(uv, surface);
    half3 paint = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).rgb;
    half signal = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv).r;
    surface.albedo = lerp(paint, _BaseColor.rgb, signal);
    surface.emission = signal * _EmissionColor.rgb;
    // Signal windows are diffusing plastic, even if the one-piece PBR source
    // classified their neutral gray as metal.
    surface.metallic = lerp(surface.metallic, 0.0h, signal);
    surface.smoothness = lerp(surface.smoothness, 0.3h, signal);
}
#endif
