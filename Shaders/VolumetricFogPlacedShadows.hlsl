#ifndef VOLUMETRIC_FOG_PLACED_SHADOWS_INCLUDED
#define VOLUMETRIC_FOG_PLACED_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// How many placed shadows can darken the fog at the same time. It has to be kept in sync with its counterpart
// from VolumetricFogRenderPass.cs.
#define MAX_VOLUMETRIC_FOG_PLACED_SHADOWS 8

// Guards the divisions done with the softness and the light direction, so that a softness of zero leads to hard
// edges instead of NaNs and shapes are never divided by a degenerate direction.
#define VOLUMETRIC_FOG_PLACED_SHADOW_EPSILON 1e-4

// The shapes are unit spheres and unit boxes in their own space, so the transform holds their placement.
float4x4 _PlacedShadowsWorldToLocal[MAX_VOLUMETRIC_FOG_PLACED_SHADOWS];
float _PlacedShadowsIntensity[MAX_VOLUMETRIC_FOG_PLACED_SHADOWS];
float _PlacedShadowsSoftness[MAX_VOLUMETRIC_FOG_PLACED_SHADOWS];
float _PlacedShadowsProjection[MAX_VOLUMETRIC_FOG_PLACED_SHADOWS];
float _PlacedShadowsRcpLength[MAX_VOLUMETRIC_FOG_PLACED_SHADOWS];
float _PlacedShadowsBoxness[MAX_VOLUMETRIC_FOG_PLACED_SHADOWS];
int _PlacedShadowsCount;

// Gets the signed distance from the given position, in the space of the shape, to the surface of the shape. It is
// negative inside of it. The boxness blends between the unit sphere and the unit box, so that shapes can be rounded.
float GetPlacedShadowShapeDistance(float3 posLS, float boxness)
{
    float sphereDistance = length(posLS) - 1.0;

    float3 q = abs(posLS) - 1.0;
    float boxDistance = length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);

    return lerp(sphereDistance, boxDistance, boxness);
}

// Gets how much of the light energy reaches the given world position after crossing the shadows placed by hand.
// Every shape is slid along the direction towards the light until it is as close as it can get to the position, which
// projects its shadow the same way a real occluder would, and then the position is shaded by how deep it ends up
// inside of the shape. Shapes that are not projected at all just darken the fog around them, like an ambient occlusion.
float GetPlacedShadowsAttenuation(float3 posWS, float3 lightDirection)
{
#if !_FOG_PLACED_SHADOWS_ENABLED
    return 1.0;
#else
    float attenuation = 1.0;

    UNITY_LOOP
    for (int i = 0; i < _PlacedShadowsCount; ++i)
    {
        float3 posLS = mul(_PlacedShadowsWorldToLocal[i], float4(posWS, 1.0)).xyz;
        float3 lightDirectionLS = mul((float3x3)_PlacedShadowsWorldToLocal[i], lightDirection);

        // The transform is affine, so the ray keeps its parametrization and this distance is the world space one,
        // even though the closest approach itself is measured in the space of the shape.
        float travelled = -dot(posLS, lightDirectionLS) / max(dot(lightDirectionLS, lightDirectionLS), VOLUMETRIC_FOG_PLACED_SHADOW_EPSILON);

        // Shapes behind the position, with respect to the light, never occlude it.
        travelled = max(travelled, 0.0) * _PlacedShadowsProjection[i];
        posLS += lightDirectionLS * travelled;

        // The penumbra widens and the shadow fades out as it gets further away from the shape that casts it.
        float fade = saturate(1.0 - travelled * _PlacedShadowsRcpLength[i]);
        float softness = _PlacedShadowsSoftness[i] * (1.0 + travelled * _PlacedShadowsRcpLength[i]);

        float distanceToShape = GetPlacedShadowShapeDistance(posLS, _PlacedShadowsBoxness[i]);
        float shade = 1.0 - smoothstep(0.0, max(softness, VOLUMETRIC_FOG_PLACED_SHADOW_EPSILON), distanceToShape);

        attenuation *= 1.0 - _PlacedShadowsIntensity[i] * shade * fade;
    }

    return attenuation;
#endif
}

#endif
