#ifndef VOLUMETRIC_FOG_NOISE_INCLUDED
#define VOLUMETRIC_FOG_NOISE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.freesleeperr.insidestage/Shaders/VolumetricFogArea.hlsl"

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

// Evaluates the noise that carves the fog at the given position with the given parameters, in the [0, 1] range. It is
// kept apart from the parameters themselves so that both the noise authored in the volume and the one that a fog area
// authors for itself can be resolved by the very same code.
// The macro octave is only worth sampling for the fog of the volume, which spans the whole world. The pockets of fog
// that an area holds are far smaller than the macro tile, so it would be a constant across all of them.
float EvaluateFogNoise(float3 posWS, float shapeTiling, float3 shapeOffset, float coverage, bool useMacro,
                       bool includeDetail, float detailTiling, float3 detailOffset, float detailIntensity)
{
    float3 shapeUvw = posWS * shapeTiling + shapeOffset;
    float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(_FogShapeNoiseTexture, sampler_FogShapeNoiseTexture, shapeUvw, 0.0);

    // The perlin-worley channel gives the billowy shape, while the worley octaves round it up into cloud alike blobs.
    float lowFrequencyFbm = shapeNoise.g * 0.625 + shapeNoise.b * 0.25 + shapeNoise.a * 0.125;
    float noise = saturate(RemapNoise(shapeNoise.r, lowFrequencyFbm - 1.0, 1.0, 0.0, 1.0));
    noise = saturate(RemapNoise(noise, VOLUMETRIC_FOG_SHAPE_NOISE_FLOOR, 1.0, 0.0, 1.0));

#if _FOG_NOISE_MACRO_ENABLED
    UNITY_BRANCH
    if (useMacro)
    {
        // A very low frequency worley octave, which is smooth and roughly centered around one half, varies the coverage
        // across the world. It gathers the fog into banks separated by clearings, which is what gives it depth.
        float3 macroUvw = posWS * _NoiseMacroTiling + _NoiseMacroOffset;
        float macroNoise = SAMPLE_TEXTURE3D_LOD(_FogShapeNoiseTexture, sampler_FogShapeNoiseTexture, macroUvw, 0.0).g;
        coverage = saturate(coverage * lerp(1.0, macroNoise * 2.0, _NoiseMacroIntensity));
    }
#endif

    // Coverage carves away the lowest densities, which is what opens up the gaps between the clouds of fog.
    noise = saturate((noise - (1.0 - coverage)) / max(coverage, VOLUMETRIC_FOG_MIN_REMAP_RANGE));

#if _FOG_NOISE_DETAIL_ENABLED
    UNITY_BRANCH
    if (includeDetail && noise > 0.0 && detailIntensity > 0.0)
    {
        float3 detailUvw = posWS * detailTiling + detailOffset;
        float3 detailNoise = SAMPLE_TEXTURE3D_LOD(_FogDetailNoiseTexture, sampler_FogDetailNoiseTexture, detailUvw, 0.0).rgb;
        float detailFbm = detailNoise.r * 0.625 + detailNoise.g * 0.25 + detailNoise.b * 0.125;

        // Wispy erosion where the fog is thin and billowy erosion where it is dense, as it is usually done for clouds.
        float detail = lerp(1.0 - detailFbm, detailFbm, saturate(noise * 2.0)) * detailIntensity;
        noise = saturate((noise - detail) / max(1.0 - detail, VOLUMETRIC_FOG_MIN_REMAP_RANGE));
    }
#endif

    return noise;
}

// Gets the density multiplier, in the [0, 1] range, that turns the otherwise uniform fog into cloud alike fog. The
// fog area that holds the position, when any does, is allowed to author a noise of its own, so that a small pocket of
// fog can churn at its own scale and drift on its own wind without the fog of the whole world following it.
// The detail noise can be skipped by the callers that are too coarse to resolve it, such as the light march that
// makes the fog shadow itself, so that they do not pay for the extra 3D texture fetch that it costs.
float GetFogNoiseDensityMultiplier(float3 posWS, float dist, bool includeDetail, int areaIndex)
{
#if !_FOG_NOISE_ENABLED
    return 1.0;
#else
    float noise;
    float intensity;

    // The index has to be clamped apart from the tests that guard it. With no fog area in the scene the areas are
    // compiled out and the index that reaches here is a compile time constant of minus one, and the shader compiler
    // resolves the indexing before it resolves the branch that would have kept it from ever happening, so it rejects
    // the whole program over an access that the running shader never makes.
    int safeAreaIndex = max(areaIndex, 0);

    float4 areaNoiseParams = areaIndex >= 0 ? _FogAreasNoiseParams[safeAreaIndex] : float4(0.0, 0.0, 0.0, 0.0);

    UNITY_BRANCH
    if (areaNoiseParams.x > 0.0)
    {
        float4 areaDetailOffset = _FogAreasNoiseDetailOffset[safeAreaIndex];

        noise = EvaluateFogNoise(posWS, areaNoiseParams.x, _FogAreasNoiseShapeOffset[safeAreaIndex].xyz, areaNoiseParams.w,
                                 false, includeDetail, areaNoiseParams.y, areaDetailOffset.xyz, areaDetailOffset.w);
        intensity = areaNoiseParams.z;
    }
    else
    {
        noise = EvaluateFogNoise(posWS, _NoiseShapeTiling, _NoiseShapeOffset, _NoiseCoverage,
                                 true, includeDetail, _NoiseDetailTiling, _NoiseDetailOffset, _NoiseDetailIntensity);
        intensity = _NoiseIntensity;
    }

    // Far away from the camera the raymarching steps are too long to resolve the noise, so fade it back to uniform fog.
    float distanceFade = saturate((dist - _NoiseFadeStart) * _NoiseFadeRcpRange);

    return lerp(lerp(1.0, noise, intensity), 1.0, distanceFade);
#endif
}

#endif
