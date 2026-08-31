#ifndef VOLUMETRIC_FOG_AREA_INCLUDED
#define VOLUMETRIC_FOG_AREA_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// How many areas can hold fog at the same time. It has to be kept in sync with its counterpart from
// VolumetricFogRenderPass.cs.
#define MAX_VOLUMETRIC_FOG_AREAS 8

// Keeps the ray-box intersection from dividing by zero when the ray is parallel to any of the faces of the box.
#define VOLUMETRIC_FOG_AREA_MIN_RAY_DIRECTION 1e-6

// Guards the division done by the borders, so that borders of size zero lead to hard edges instead of NaNs.
#define VOLUMETRIC_FOG_AREA_MIN_BORDER 1e-4

// The boxes go from -1 to 1 in every axis in their own space, so the transform holds their placement.
float4x4 _FogAreasWorldToLocal[MAX_VOLUMETRIC_FOG_AREAS];

// The borders of every box are stored in the xyz channels and its density multiplier in the w one.
float4 _FogAreasBorderAndDensity[MAX_VOLUMETRIC_FOG_AREAS];

// A multiplier color for the light scattered by the fog that every box holds, so that one area can be colder or
// warmer than the next one without needing a volume of its own.
float4 _FogAreasTint[MAX_VOLUMETRIC_FOG_AREAS];

// The noise that every box authors for itself, when it does: the tiling of the shape noise in x, the tiling of the
// detail noise in y, its intensity in z and its coverage in w. An x of zero means that the box keeps the noise of
// the volume, which is what the noise code branches on.
float4 _FogAreasNoiseParams[MAX_VOLUMETRIC_FOG_AREAS];

// The offsets that the wind of every box has drifted its noise by, already scaled by the tiling and wrapped on the
// CPU so that they keep their precision as time goes by.
float4 _FogAreasNoiseShapeOffset[MAX_VOLUMETRIC_FOG_AREAS];

// The offset of the detail noise in xyz and the intensity that erodes the box with it in w.
float4 _FogAreasNoiseDetailOffset[MAX_VOLUMETRIC_FOG_AREAS];

// Whether every box piles its fog on top of the ones it overlaps instead of competing with them for the densest,
// as the VolumetricFogAreaBlendMode of the area. Zero is max, one is additive.
float _FogAreasBlendMode[MAX_VOLUMETRIC_FOG_AREAS];

float _FogAreasGlobalDensityMultiplier;
int _FogAreasCount;

// Whether the fog only exists inside of the areas, which is what allows the raymarching to be restricted to them.
bool IsFogConfinedToAreas()
{
    return _FogAreasGlobalDensityMultiplier <= 0.0;
}

// Whether the given area piles its fog on top of the ones it overlaps instead of competing with them for the densest.
bool IsFogAreaAdditive(int index)
{
    return _FogAreasBlendMode[index] > 0.5;
}

// Gets the density multiplier of one fog area at the given world position, faded in across its borders. It is zero
// outside of the box. The noise is not part of it, so that every caller can carve it with the one it needs.
float GetFogAreaMultiplier(int index, float3 posWS)
{
    // Inside of the box every axis goes from -1 to 1, so the distance to the closest face is one minus its absolute value.
    float3 posAS = mul(_FogAreasWorldToLocal[index], float4(posWS, 1.0)).xyz;
    float3 distanceToFaces = 1.0 - abs(posAS);

    UNITY_BRANCH
    if (any(distanceToFaces <= 0.0))
        return 0.0;

    float4 borderAndDensity = _FogAreasBorderAndDensity[index];
    float3 fade = saturate(distanceToFaces / max(borderAndDensity.xyz, VOLUMETRIC_FOG_AREA_MIN_BORDER));
    fade = fade * fade * (3.0 - 2.0 * fade);

    return fade.x * fade.y * fade.z * borderAndDensity.w;
}

// Gets the density multiplier of the fog areas that blend by keeping the densest of them, along with the tint of the
// area that ends up holding the fog. Overlapping them does not thicken the fog where they cross, so that sliding one
// area over another leaves the fog alone, and the tint follows that very same area so that the color never blends into
// something that was not authored. The areas that blend by adding are resolved apart, as each of them carries its own
// noise and so cannot be reduced to one multiplier here. Outside of all of them the fog keeps the global multiplier,
// which is zero when the fog is confined to the areas, and is left untinted.
float GetFogAreaDensityMultiplierAndTint(float3 posWS, out float3 tint, out int areaIndex)
{
    tint = float3(1.0, 1.0, 1.0);
    areaIndex = -1;

#if !_FOG_AREA_ENABLED
    return 1.0;
#else
    float multiplier = 0.0;

    UNITY_LOOP
    for (int i = 0; i < _FogAreasCount; ++i)
    {
        UNITY_BRANCH
        if (IsFogAreaAdditive(i))
            continue;

        float areaMultiplier = GetFogAreaMultiplier(i, posWS);

        UNITY_BRANCH
        if (areaMultiplier > multiplier)
        {
            multiplier = areaMultiplier;
            tint = _FogAreasTint[i].rgb;
            areaIndex = i;
        }
    }

    // The fog that the areas do not reach is the global one, which no area has authored a tint for.
    UNITY_BRANCH
    if (_FogAreasGlobalDensityMultiplier > multiplier)
    {
        multiplier = _FogAreasGlobalDensityMultiplier;
        tint = float3(1.0, 1.0, 1.0);
        areaIndex = -1;
    }

    return multiplier;
#endif
}

// Intersects the given ray with the box of one of the fog areas. Returns whether the box is crossed at all and, when
// it is, the distances along the ray at which the ray enters and leaves it.
bool IntersectFogArea(int index, float3 ro, float3 rd, out float enterDistance, out float exitDistance)
{
    float3 roAS = mul(_FogAreasWorldToLocal[index], float4(ro, 1.0)).xyz;
    float3 rdAS = mul((float3x3)_FogAreasWorldToLocal[index], rd);

    // The sign is taken apart so that rays parallel to a face are pushed away from the face instead of towards it.
    float3 rdSign = rdAS >= 0.0 ? float3(1.0, 1.0, 1.0) : float3(-1.0, -1.0, -1.0);
    float3 rcpRdAS = rdSign / max(abs(rdAS), VOLUMETRIC_FOG_AREA_MIN_RAY_DIRECTION);

    float3 t0 = (-1.0 - roAS) * rcpRdAS;
    float3 t1 = (1.0 - roAS) * rcpRdAS;
    float3 tMin = min(t0, t1);
    float3 tMax = max(t0, t1);

    // The transform is affine, so the ray keeps its parametrization and these distances are the world space ones.
    enterDistance = max(max(tMin.x, tMin.y), tMin.z);
    exitDistance = min(min(tMax.x, tMax.y), tMax.z);

    return exitDistance > max(enterDistance, 0.0);
}

// Intersects the given ray with every fog area and returns the range of the ray that has to be marched to cross all of
// them. The gaps left between areas that do not touch each other are marched as well, since skipping them would need
// one loop per area, but gathering the steps around the areas still resolves them with a lot more detail than marching
// the whole fog distance would.
bool IntersectFogAreas(float3 ro, float3 rd, out float enterDistance, out float exitDistance)
{
    enterDistance = FLT_MAX;
    exitDistance = -FLT_MAX;

    bool anyCrossed = false;

    UNITY_LOOP
    for (int i = 0; i < _FogAreasCount; ++i)
    {
        float areaEnterDistance;
        float areaExitDistance;

        UNITY_BRANCH
        if (!IntersectFogArea(i, ro, rd, areaEnterDistance, areaExitDistance))
            continue;

        enterDistance = min(enterDistance, areaEnterDistance);
        exitDistance = max(exitDistance, areaExitDistance);
        anyCrossed = true;
    }

    return anyCrossed;
}

#endif
