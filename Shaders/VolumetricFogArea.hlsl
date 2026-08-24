#ifndef VOLUMETRIC_FOG_AREA_INCLUDED
#define VOLUMETRIC_FOG_AREA_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// Keeps the ray-box intersection from dividing by zero when the ray is parallel to any of the faces of the box.
#define VOLUMETRIC_FOG_AREA_MIN_RAY_DIRECTION 1e-6

// Guards the division done by the borders, so that borders of size zero lead to hard edges instead of NaNs.
#define VOLUMETRIC_FOG_AREA_MIN_BORDER 1e-4

float4x4 _FogAreaWorldToLocal;
float3 _FogAreaBorder;
float _FogAreaDensityMultiplier;

// Gets the density multiplier of the fog area, which is zero outside of its box and fades the fog in across its borders.
float GetFogAreaDensityMultiplier(float3 posWS)
{
#if !_FOG_AREA_ENABLED
    return 1.0;
#else
    // Inside of the box every axis goes from -1 to 1, so the distance to the closest face is one minus its absolute value.
    float3 posAS = mul(_FogAreaWorldToLocal, float4(posWS, 1.0)).xyz;
    float3 distanceToFaces = 1.0 - abs(posAS);

    UNITY_BRANCH
    if (any(distanceToFaces <= 0.0))
        return 0.0;

    float3 fade = saturate(distanceToFaces / max(_FogAreaBorder, VOLUMETRIC_FOG_AREA_MIN_BORDER));
    fade = fade * fade * (3.0 - 2.0 * fade);

    return fade.x * fade.y * fade.z * _FogAreaDensityMultiplier;
#endif
}

// Intersects the given ray with the box of the fog area. Returns whether the box is crossed at all and, when it is,
// the distances along the ray at which the ray enters and leaves it.
bool IntersectFogArea(float3 ro, float3 rd, out float enterDistance, out float exitDistance)
{
    float3 roAS = mul(_FogAreaWorldToLocal, float4(ro, 1.0)).xyz;
    float3 rdAS = mul((float3x3)_FogAreaWorldToLocal, rd);

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

#endif
