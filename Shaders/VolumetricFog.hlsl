#ifndef VOLUMETRIC_FOG_INCLUDED
#define VOLUMETRIC_FOG_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#if UNITY_VERSION >= 202310 && _APV_CONTRIBUTION_ENABLED
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
        #include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
    #endif
#endif
#include "./DeclareDownsampledDepthTexture.hlsl"
#include "./VolumetricShadows.hlsl"
#include "./ProjectionUtils.hlsl"
#include "./VolumetricFogNoise.hlsl"
#include "./VolumetricFogArea.hlsl"
#include "./VolumetricFogPlacedShadows.hlsl"

// How fast the powder effect darkens the fog as its density decreases.
#define VOLUMETRIC_FOG_POWDER_SHARPNESS 6.0

// How many steps are marched towards the main light when the fog shadows itself.
#define VOLUMETRIC_FOG_SELF_SHADOWING_STEPS 6

// How many steps are marched towards every additional light when the fog shadows itself. Every light that reaches the
// step being shaded pays for its own march, so they are fewer than the ones marched towards the main light.
#define VOLUMETRIC_FOG_ADDITIONAL_LIGHT_SELF_SHADOWING_STEPS 4

// How much every self shadowing step grows with respect to the previous one. Growing steps concentrate the samples
// near the position being shaded, which is where the fog that occludes it matters the most.
#define VOLUMETRIC_FOG_SELF_SHADOWING_STEP_GROWTH 2.0

// The transmittance below which the fog is considered to hide everything behind it and the ray stops marching.
#define VOLUMETRIC_FOG_MIN_TRANSMITTANCE 0.005

// The product of distance attenuation and scattering below which one additional light is considered not to reach the
// fog at all, so that everything it would cost is skipped.
#define VOLUMETRIC_FOG_MIN_ADDITIONAL_LIGHT_CONTRIBUTION 1e-4

int _FrameCount;
uint _CustomAdditionalLightsCount;
float _Distance;
float _DistanceFadeStart;
float _DistanceFadeRcpRange;
float _DistanceFalloff;
float _BaseHeight;
float _MaximumHeight;
float _HeightFalloff;
float _GroundHeight;
float _Density;
float _Absortion;
float _APVContributionWeight;
float3 _Tint;
float3 _AmbientColor;
float _PowderIntensity;
float _SelfShadowingStepLength;
float _SelfShadowingIntensity;
float _AdditionalLightsSelfShadowingDistance;
float _AdditionalLightsSelfShadowingStepFraction;
float _AdditionalLightsSelfShadowingIntensity;
int _MaxSteps;

float _Anisotropies[MAX_VISIBLE_LIGHTS + 1];
float _Scatterings[MAX_VISIBLE_LIGHTS + 1];
float _RadiiSq[MAX_VISIBLE_LIGHTS];

// Computes the ray origin, direction, and returns the reconstructed world position for orthographic projection.
float3 ComputeOrthographicParams(float2 uv, float depth, out float3 ro, out float3 rd)
{
    float4x4 viewMatrix = UNITY_MATRIX_V;
    float2 ndc = uv * 2.0 - 1.0;
    
    rd = normalize(-viewMatrix[2].xyz);
    float3 rightOffset = normalize(viewMatrix[0].xyz) * (ndc.x * unity_OrthoParams.x);
    float3 upOffset = normalize(viewMatrix[1].xyz) * (ndc.y * unity_OrthoParams.y);
    float3 fwdOffset = rd * depth;
    
    float3 posWs = GetCameraPositionWS() + fwdOffset + rightOffset + upOffset;
    ro = posWs - fwdOffset;

    return posWs;
}

// Calculates the initial raymarching parameters.
void CalculateRaymarchingParams(float2 uv, out float3 ro, out float3 rd, out float iniOffsetToNearPlane, out float offsetLength, out float3 rdPhase)
{
    float depth = SampleDownsampledSceneDepth(uv);
    float3 posWS;
    
    UNITY_BRANCH
    if (unity_OrthoParams.w <= 0)
    {
        ro = GetCameraPositionWS();
#if !UNITY_REVERSED_Z
        depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
#endif
        posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
        float3 offset = posWS - ro;
        offsetLength = length(offset);
        rd = offset / offsetLength;
        rdPhase = rd;
        
        // In perspective, ray direction should vary in length depending on which fragment we are at.
        float3 camFwd = normalize(-UNITY_MATRIX_V[2].xyz);
        float cos = dot(camFwd, rd);
        float fragElongation = 1.0 / cos;
        iniOffsetToNearPlane = fragElongation * _ProjectionParams.y;
    }
    else
    {
        depth = LinearEyeDepthOrthographic(depth);
        posWS = ComputeOrthographicParams(uv, depth, ro, rd);
        offsetLength = depth;
        
        // Fake the ray direction that will be used to calculate the phase, so we can still use anisotropy in orthographic mode.
        rdPhase = normalize(posWS - GetCameraPositionWS());
        iniOffsetToNearPlane = _ProjectionParams.y;
    }
}

// Gets the main light phase function.
float GetMainLightPhase(float3 rd)
{
#if _MAIN_LIGHT_CONTRIBUTION_DISABLED
    return 0.0;
#else
    return CornetteShanksPhaseFunction(_Anisotropies[_CustomAdditionalLightsCount], dot(rd, GetMainLight().direction));
#endif
}

// Gets the density multiplier, in the [0, 1] range, that builds the fog up as it gets further away from the camera.
// When the distance layering is disabled the fade start is negative and the range is nearly zero, so it returns one everywhere.
float GetFogDistanceMultiplier(float dist)
{
    float t = saturate((dist - _DistanceFadeStart) * _DistanceFadeRcpRange);

    // PositivePow never returns exactly zero, so the fog before the start distance has to be discarded apart.
    return t > 0.0 ? PositivePow(t, _DistanceFalloff) : 0.0;
}

// Gets the density multiplier, in the [0, 1] range, that fades the fog out as it gets closer to the maximum height.
float GetFogHeightMultiplier(float posWSY)
{
    float t = saturate((posWSY - _BaseHeight) / (_MaximumHeight - _BaseHeight));
    t = 1.0 - t;
    t = lerp(t, 0.0, posWSY < _GroundHeight);

    // PositivePow never returns exactly zero, so the fog above the maximum height has to be discarded apart.
    return t > 0.0 ? PositivePow(t, _HeightFalloff) : 0.0;
}

// Gets the fog density at the given world position, which is at the given distance from the camera, along with the
// tint of the fog area that holds it. The tint is white where no area does.
float GetFogDensityAndAreaTint(float3 posWS, float dist, bool includeDetail, out float3 areaTint)
{
    areaTint = float3(1.0, 1.0, 1.0);

    float t = GetFogHeightMultiplier(posWS.y);

    // Skip the rest of the work where the height gradient has already faded the fog out.
    UNITY_BRANCH
    if (t <= 0.0)
        return 0.0;

    t *= GetFogDistanceMultiplier(dist);

    // The same, once the distance gradient has had its say.
    UNITY_BRANCH
    if (t <= 0.0)
        return 0.0;

    // The areas that blend by keeping the densest of them, along with the global fog, resolve to a single multiplier
    // and to the noise of whichever of them won the position.
    int areaIndex = -1;
    float3 maxTint;
    float multiplier = GetFogAreaDensityMultiplierAndTint(posWS, maxTint, areaIndex);

    float density = 0.0;
    float3 tintedDensity = float3(0.0, 0.0, 0.0);

    // Skip sampling the noise where the area borders have already faded the fog out.
    UNITY_BRANCH
    if (multiplier > 0.0)
    {
        density = multiplier * GetFogNoiseDensityMultiplier(posWS, dist, includeDetail, areaIndex);
        tintedDensity = density * maxTint;
    }

#if _FOG_AREA_ENABLED
    // The areas that blend by adding then pile their own fog, carved by their own noise, on top of that. Each of them
    // pays for a noise evaluation of its own wherever it reaches, and that is what buys a puff of smoke that sits
    // inside a bank of haze and is lit along with it, instead of replacing the haze with itself.
    UNITY_LOOP
    for (int i = 0; i < _FogAreasCount; ++i)
    {
        UNITY_BRANCH
        if (!IsFogAreaAdditive(i))
            continue;

        float areaMultiplier = GetFogAreaMultiplier(i, posWS);

        UNITY_BRANCH
        if (areaMultiplier <= 0.0)
            continue;

        float areaDensity = areaMultiplier * GetFogNoiseDensityMultiplier(posWS, dist, includeDetail, i);

        density += areaDensity;
        tintedDensity += areaDensity * _FogAreasTint[i].rgb;
    }
#endif

    // The fog is tinted by every area that left something here, each of them weighted by how much of it that area put
    // there, so that the color of a puff bleeds into the haze around it instead of switching across its border.
    UNITY_BRANCH
    if (density > 0.0)
        areaTint = tintedDensity / density;

    return _Density * t * density;
}

// Gets the fog density at the given world position, which is at the given distance from the camera. It is the one used
// wherever only the amount of fog matters and the color it scatters does not, as the light march does.
float GetFogDensity(float3 posWS, float dist, bool includeDetail)
{
    float3 unusedAreaTint;

    return GetFogDensityAndAreaTint(posWS, dist, includeDetail, unusedAreaTint);
}

// Marches a short ray towards a light, accumulating the optical depth of all the fog that it finds on its way, which
// is what makes the fog shadow itself. Every step grows with respect to the previous one, so the samples gather near
// the position being shaded, which is where the fog that occludes it matters the most.
// The distance to the camera is the one of the view ray step being shaded, not the one of every sample. It keeps
// the whole light march inside the same distance band, so that the layering does not fade the shadows in and out.
float GetFogOpticalDepthTowardsLight(float3 posWS, float3 lightDirection, float dist, float jitter, float stepLength, int steps)
{
    float travelled = stepLength * jitter;
    float opticalDepth = 0.0;

    for (int i = 0; i < steps; ++i)
    {
        travelled += stepLength;

        // The detail noise is skipped here. It only erodes the edges of the fog, which these steps are far too long
        // to resolve anyway, so paying for it would just double the 3D texture fetches that the light march costs.
        opticalDepth += GetFogDensity(posWS + lightDirection * travelled, dist, false) * stepLength;

        stepLength *= VOLUMETRIC_FOG_SELF_SHADOWING_STEP_GROWTH;
    }

    return opticalDepth;
}

// Gets how much of the main light energy reaches the given world position after crossing the fog that occludes it.
// It is what gives the clouds of fog bright edges and dark cores, instead of lighting them up uniformly.
float GetFogSelfShadowing(float3 posWS, float3 lightDirection, float dist, float jitter)
{
#if !_FOG_SELF_SHADOWING_ENABLED
    return 1.0;
#else
    float opticalDepth = GetFogOpticalDepthTowardsLight(posWS, lightDirection, dist, jitter, _SelfShadowingStepLength,
                                                        VOLUMETRIC_FOG_SELF_SHADOWING_STEPS);

    // The extinction here is kept apart from the one used along the view ray, so that the shadowing can be dialed in
    // without having to retweak the attenuation distance, which drives how dark the whole image gets.
    return exp(-opticalDepth * _SelfShadowingIntensity);
#endif
}

// Gets how much of the energy of one additional light reaches the given world position after crossing the fog that
// occludes it. It is the same light march the main light does, and it is what shapes the smoke of a scene lit by
// lamps rather than by a sun: without it every pocket of fog is lit right through and reads as a flat blob.
// The march stops at the light itself, since the fog behind a lamp does not shade what is in front of it, and that
// also keeps the march short for the lamps that sit inside of the very fog they light.
float GetFogAdditionalLightSelfShadowing(float3 posWS, float3 lightDirection, float distanceToLight, float dist, float jitter)
{
#if !_FOG_ADDITIONAL_LIGHTS_SELF_SHADOWING_ENABLED
    return 1.0;
#else
    // The first step is the fraction of the marched distance that makes all of the growing steps add up to exactly it.
    float marchDistance = min(_AdditionalLightsSelfShadowingDistance, distanceToLight);
    float stepLength = marchDistance * _AdditionalLightsSelfShadowingStepFraction;

    float opticalDepth = GetFogOpticalDepthTowardsLight(posWS, lightDirection, dist, jitter, stepLength,
                                                        VOLUMETRIC_FOG_ADDITIONAL_LIGHT_SELF_SHADOWING_STEPS);

    return exp(-opticalDepth * _AdditionalLightsSelfShadowingIntensity);
#endif
}

// Gets the powder factor, which darkens the light scattered by the thinnest parts of the fog, making it look fluffier.
float GetPowderFactor(float density)
{
    return lerp(1.0, 1.0 - exp(-density * VOLUMETRIC_FOG_POWDER_SHARPNESS), _PowderIntensity);
}

// Gets the ambient light scattered by the fog at one raymarch step. It is the light that reaches the fog from
// everywhere at once, which is what keeps the fog readable where no light directly hits it. Without it the fog goes
// pure black in the shade, since the only other source of light that does not depend on a light being in range is the
// adaptive probe volume, which is not available in every Unity version.
float3 GetStepAmbientColor(float density)
{
#if !_FOG_AMBIENT_ENABLED
    return float3(0.0, 0.0, 0.0);
#else
    return _AmbientColor * density;
#endif
}

// Gets the GI evaluation from the adaptive probe volume at one raymarch step.
float3 GetStepAdaptiveProbeVolumeEvaluation(float2 uv, float3 posWS, float density)
{
    float3 apvDiffuseGI = float3(0.0, 0.0, 0.0);
    
#if UNITY_VERSION >= 202310 && _APV_CONTRIBUTION_ENABLED
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
        EvaluateAdaptiveProbeVolume(posWS, uv * _ScreenSize.xy, apvDiffuseGI);
        apvDiffuseGI = apvDiffuseGI * _APVContributionWeight * density;
    #endif
#endif
 
    return apvDiffuseGI;
}

// Gets the main light color at one raymarch step.
float3 GetStepMainLightColor(float3 currPosWS, float phaseMainLight, float density, float dist, float jitter)
{
#if _MAIN_LIGHT_CONTRIBUTION_DISABLED
    return float3(0.0, 0.0, 0.0);
#endif
    Light mainLight = GetMainLight();
    float4 shadowCoord = TransformWorldToShadowCoord(currPosWS);
    mainLight.shadowAttenuation = VolumetricMainLightRealtimeShadow(shadowCoord);
#if _LIGHT_COOKIES
    mainLight.color *= SampleMainLightCookie(currPosWS);
#endif
    float selfShadowing = GetFogSelfShadowing(currPosWS, mainLight.direction, dist, jitter);
    float placedShadows = GetPlacedShadowsAttenuation(currPosWS, mainLight.direction);

    return (mainLight.color * _Tint) * (mainLight.shadowAttenuation * selfShadowing * placedShadows * phaseMainLight * density * _Scatterings[_CustomAdditionalLightsCount]);
}

// Gets the accumulated color from additional lights at one raymarch step.
float3 GetStepAdditionalLightsColor(float2 uv, float3 currPosWS, float3 rd, float density, float dist, float jitter)
{
#if _ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED
    return float3(0.0, 0.0, 0.0);
#endif
#if _FORWARD_PLUS
    // Forward+ rendering path needs this data before the light loop.
    InputData inputData = (InputData)0;
    inputData.normalizedScreenSpaceUV = uv;
    inputData.positionWS = currPosWS;
#endif
    float3 additionalLightsColor = float3(0.0, 0.0, 0.0);
                
    // Loop differently through lights in Forward+ while considering Forward and Deferred too.
    LIGHT_LOOP_BEGIN(_CustomAdditionalLightsCount)
        UNITY_BRANCH
        if (_Scatterings[lightIndex] > 0.0)
        {
            Light additionalLight = GetAdditionalPerObjectLight(lightIndex, currPosWS);

            // See universal\ShaderLibrary\RealtimeLights.hlsl - GetAdditionalPerObjectLight.
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
            float4 additionalLightPos = _AdditionalLightsBuffer[lightIndex].position;
#else
            float4 additionalLightPos = _AdditionalLightsPosition[lightIndex];
#endif
            // This is useful for both spotlights and pointlights. For the latter it is specially true when the point light is inside some geometry and casts shadows.
            // Gradually reduce additional lights scattering to zero at their origin to try to avoid flicker-aliasing.
            float3 distToPos = additionalLightPos.xyz - currPosWS;
            float distToPosMagnitudeSq = dot(distToPos, distToPos);

            float newScattering = smoothstep(0.0, _RadiiSq[lightIndex], distToPosMagnitudeSq) ;
            newScattering *= newScattering;
            newScattering *= _Scatterings[lightIndex];

            // If directional lights are also considered as additional lights when more than 1 is used, ignore the previous code when it is a directional light.
            // They store direction in additionalLightPos.xyz and have .w set to 0, while point and spotlights have it set to 1.
            // newScattering = lerp(1.0, newScattering, additionalLightPos.w);

            // Everything this light scatters is scaled by its distance attenuation and by its scattering, and both are
            // cheap: the attenuation comes with the light itself and the scattering is a couple of multiplies. Once
            // their product is negligible the light can no longer brighten this step in any noticeable way, so the
            // shadow map fetch, the placed shadows, the cookie and the whole light march that it would pay for are
            // skipped. A lamp lights the fog around it rather than the whole world, so most of the steps of a scene
            // lit by lamps end up out of reach of most of them.
            UNITY_BRANCH
            if (additionalLight.distanceAttenuation * newScattering > VOLUMETRIC_FOG_MIN_ADDITIONAL_LIGHT_CONTRIBUTION)
            {
                additionalLight.shadowAttenuation = VolumetricAdditionalLightRealtimeShadow(lightIndex, currPosWS, additionalLight.direction);
                additionalLight.shadowAttenuation *= GetPlacedShadowsAttenuation(currPosWS, additionalLight.direction);
#if _LIGHT_COOKIES
                additionalLight.color *= SampleAdditionalLightCookie(lightIndex, currPosWS);
#endif
                // Point and spot lights store their position and have their w set to one, while directional lights store
                // their direction there and have it set to zero, so those are marched the whole distance instead.
                float distanceToLight = additionalLightPos.w > 0.0 ? sqrt(distToPosMagnitudeSq) : FLT_MAX;
                additionalLight.shadowAttenuation *= GetFogAdditionalLightSelfShadowing(currPosWS, additionalLight.direction, distanceToLight, dist, jitter);

                float phase = CornetteShanksPhaseFunction(_Anisotropies[lightIndex], dot(rd, additionalLight.direction));
                additionalLightsColor += (additionalLight.color * (additionalLight.shadowAttenuation * additionalLight.distanceAttenuation * phase * density * newScattering));
            }
        }
    LIGHT_LOOP_END

    return additionalLightsColor;
}

// Calculates the volumetric fog. Returns the color in the RGB channels and transmittance in alpha.
float4 VolumetricFog(float2 uv, float2 positionCS)
{
    float3 ro;
    float3 rd;
    float iniOffsetToNearPlane;
    float offsetLength;
    float3 rdPhase;

    CalculateRaymarchingParams(uv, ro, rd, iniOffsetToNearPlane, offsetLength, rdPhase);

    offsetLength -= iniOffsetToNearPlane;
    float3 roNearPlane = ro + rd * iniOffsetToNearPlane;

    float startDistance = 0.0;
    float endDistance = _Distance - iniOffsetToNearPlane;

#if _FOG_AREA_ENABLED
    // Raymarch only the part of the ray that crosses the areas. Nearly all of the steps end up inside of the boxes,
    // so the fog they hold is resolved with a lot more detail than marching the whole distance would. It can only be
    // done when the areas are the one and only place where the fog exists, as otherwise the global fog that fills the
    // rest of the world would be skipped along with the gaps.
    UNITY_BRANCH
    if (IsFogConfinedToAreas())
    {
        float areasEnterDistance;
        float areasExitDistance;

        UNITY_BRANCH
        if (!IntersectFogAreas(roNearPlane, rd, areasEnterDistance, areasExitDistance))
            return float4(0.0, 0.0, 0.0, 1.0);

        startDistance = max(startDistance, areasEnterDistance);
        endDistance = min(endDistance, areasExitDistance);

        UNITY_BRANCH
        if (startDistance >= endDistance || startDistance >= offsetLength)
            return float4(0.0, 0.0, 0.0, 1.0);
    }
#endif

    float stepLength = (endDistance - startDistance) / (float)_MaxSteps;
    float jitter01 = InterleavedGradientNoise(positionCS, _FrameCount);
    float jitter = stepLength * jitter01;

    float phaseMainLight = GetMainLightPhase(rdPhase);
    float minusStepLengthTimesAbsortion = -stepLength * _Absortion;
                
    float3 volumetricFogColor = float3(0.0, 0.0, 0.0);
    float transmittance = 1.0;

    UNITY_LOOP
    for (int i = 0; i < _MaxSteps; ++i)
    {
        float dist = startDistance + jitter + i * stepLength;
        
        UNITY_BRANCH
        if (dist >= offsetLength)
            break;

        // We are making the space between the camera position and the near plane "non existant", as if fog did not exist there.
        // However, it removes a lot of noise when in closed environments with an attenuation that makes the scene darker
        // and certain combinations of field of view, raymarching resolution and camera near plane.
        // In those edge cases, it looks so much better, specially when near plane is higher than the minimum (0.01) allowed.
        float3 currPosWS = roNearPlane + rd * dist;
        float3 areaTint;
        float density = GetFogDensityAndAreaTint(currPosWS, dist, true, areaTint);
                    
        UNITY_BRANCH
        if (density <= 0.0)
            continue;

        float stepAttenuation = exp(minusStepLengthTimesAbsortion * density);
        transmittance *= stepAttenuation;

        float3 apvColor = GetStepAdaptiveProbeVolumeEvaluation(uv, currPosWS, density);
        float3 ambientColor = GetStepAmbientColor(density);
        float3 mainLightColor = GetStepMainLightColor(currPosWS, phaseMainLight, density, dist, jitter01);
        float3 additionalLightsColor = GetStepAdditionalLightsColor(uv, currPosWS, rd, density, dist, jitter01);
        
        // TODO: Additional contributions? Reflection probes, etc...
        float3 stepColor = apvColor + ambientColor + (mainLightColor + additionalLightsColor) * GetPowderFactor(density);

        // The area that holds this bit of fog tints everything that it scatters, so that a pocket of fog can be graded
        // apart from the rest without needing a volume of its own.
        stepColor *= areaTint;
        volumetricFogColor += (stepColor * (transmittance * stepLength));
        
        // Once the fog is thick enough to hide whatever is behind it, the remaining steps can no longer add anything
        // noticeable, so the ray stops there and the leftover transmittance is snapped to fully opaque.
        UNITY_BRANCH
        if (transmittance < VOLUMETRIC_FOG_MIN_TRANSMITTANCE)
        {
            transmittance = 0.0;
            break;
        }
    }

    return float4(volumetricFogColor, transmittance);
}

#endif