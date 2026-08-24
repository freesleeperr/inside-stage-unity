#ifndef VOLUMETRIC_FOG_NOISE_INCLUDED
#define VOLUMETRIC_FOG_NOISE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// The lowest value that the shape noise reaches. Remapping it back to zero spreads the noise along the whole
// [0, 1] range, which is what makes the coverage parameter useful along its whole range too.
#define VOLUMETRIC_FOG_SHAPE_NOISE_FLOOR 0.5

// Guards the divisions done when remapping, so that the extremes of the parameters can not produce NaNs.
#define VOLUMETRIC_FOG_MIN_REMAP_RANGE 1e-4

TEXTURE3D(_FogShapeNoiseTexture);
SAMPLER(sampler_FogShapeNoiseTexture);
TEXTURE3D(_FogDetailNoiseTexture);
SAMPLER(sampler_FogDetailNoiseTexture);

float3 _NoiseShapeOffset;
float3 _NoiseDetailOffset;
float3 _NoiseMacroOffset;
float _NoiseShapeTiling;
float _NoiseDetailTiling;
float _NoiseMacroTiling;
float _NoiseIntensity;
float _NoiseCoverage;
float _NoiseDetailIntensity;
float _NoiseMacroIntensity;
float _NoiseFadeStart;
float _NoiseFadeRcpRange;

// Remaps the given value from the original range to the new range.
float RemapNoise(float value, float originalMin, float originalMax, float newMin, float newMax)
{
    return newMin + (value - originalMin) / (originalMax - originalMin) * (newMax - newMin);
}

// Gets the density multiplier, in the [0, 1] range, that turns the otherwise uniform fog into cloud alike fog.
float GetFogNoiseDensityMultiplier(float3 posWS, float dist)
{
#if !_FOG_NOISE_ENABLED
    return 1.0;
#else
    float3 shapeUvw = posWS * _NoiseShapeTiling + _NoiseShapeOffset;
    float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(_FogShapeNoiseTexture, sampler_FogShapeNoiseTexture, shapeUvw, 0.0);

    // The perlin-worley channel gives the billowy shape, while the worley octaves round it up into cloud alike blobs.
    float lowFrequencyFbm = shapeNoise.g * 0.625 + shapeNoise.b * 0.25 + shapeNoise.a * 0.125;
    float noise = saturate(RemapNoise(shapeNoise.r, lowFrequencyFbm - 1.0, 1.0, 0.0, 1.0));
    noise = saturate(RemapNoise(noise, VOLUMETRIC_FOG_SHAPE_NOISE_FLOOR, 1.0, 0.0, 1.0));

    float coverage = _NoiseCoverage;

#if _FOG_NOISE_MACRO_ENABLED
    // A very low frequency worley octave, which is smooth and roughly centered around one half, varies the coverage
    // across the world. It gathers the fog into banks separated by clearings, which is what gives it depth.
    float3 macroUvw = posWS * _NoiseMacroTiling + _NoiseMacroOffset;
    float macroNoise = SAMPLE_TEXTURE3D_LOD(_FogShapeNoiseTexture, sampler_FogShapeNoiseTexture, macroUvw, 0.0).g;
    coverage = saturate(coverage * lerp(1.0, macroNoise * 2.0, _NoiseMacroIntensity));
#endif

    // Coverage carves away the lowest densities, which is what opens up the gaps between the clouds of fog.
    noise = saturate((noise - (1.0 - coverage)) / max(coverage, VOLUMETRIC_FOG_MIN_REMAP_RANGE));

#if _FOG_NOISE_DETAIL_ENABLED
    UNITY_BRANCH
    if (noise > 0.0)
    {
        float3 detailUvw = posWS * _NoiseDetailTiling + _NoiseDetailOffset;
        float3 detailNoise = SAMPLE_TEXTURE3D_LOD(_FogDetailNoiseTexture, sampler_FogDetailNoiseTexture, detailUvw, 0.0).rgb;
        float detailFbm = detailNoise.r * 0.625 + detailNoise.g * 0.25 + detailNoise.b * 0.125;

        // Wispy erosion where the fog is thin and billowy erosion where it is dense, as it is usually done for clouds.
        float detail = lerp(1.0 - detailFbm, detailFbm, saturate(noise * 2.0)) * _NoiseDetailIntensity;
        noise = saturate((noise - detail) / max(1.0 - detail, VOLUMETRIC_FOG_MIN_REMAP_RANGE));
    }
#endif

    // Far away from the camera the raymarching steps are too long to resolve the noise, so fade it back to uniform fog.
    float distanceFade = saturate((dist - _NoiseFadeStart) * _NoiseFadeRcpRange);

    return lerp(lerp(1.0, noise, _NoiseIntensity), 1.0, distanceFade);
#endif
}

#endif
