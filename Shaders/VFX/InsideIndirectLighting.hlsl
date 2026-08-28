#ifndef INSIDE_INDIRECT_LIGHTING_INCLUDED
#define INSIDE_INDIRECT_LIGHTING_INCLUDED

// The two halves of the hand placed indirect lighting: the light the bounce shapes throw back onto the frame, and the
// shade the occluder shapes take out of it. They used to be a fullscreen pass each, and each of them reconstructed the
// world position and the normal of every pixel of the frame before it could do anything with them. They share the one
// pass now, so that work is paid for once.
//
// Nothing here is derived from the geometry of the scene. An artist puts a squashed box on the floor under a lamp and
// the floor throws light back up, and puts a capsule along a body and the body lays shade down around itself. Both
// keep working when the lamp moves, when the lighting preset changes and when the room is rebuilt, which is what a
// lightmap cannot do.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

// How many shapes of each kind can contribute at the same time. They have to be kept in sync with their counterparts
// from InsideIndirectLightingRendererFeature.cs.
#define MAX_ANALYTIC_OCCLUDERS 16
#define MAX_INSIDE_BOUNCE_LIGHTS 8

#define BOUNCE_SHAPE_BOX 0.0

// The center of every occluder in xyz and its radius in w.
float4 _AnalyticOccluderSpheres[MAX_ANALYTIC_OCCLUDERS];

// Half of the segment a capsule is built around in xyz, which is zero for spheres, and its intensity in w.
float4 _AnalyticOccluderAxes[MAX_ANALYTIC_OCCLUDERS];

// The falloff distance in x and how much the shade is limited to the surfaces facing the shape in y.
float4 _AnalyticOccluderParams[MAX_ANALYTIC_OCCLUDERS];

// The transform of every bounce shape. The shape itself is always a unit primitive centered on the origin, so the whole
// of its size, its stretch and its skew live in these matrices. That is the point: one light can be a pill drawn out
// along a corridor or a slab flattened onto a floor without needing a row of point lights to cover the same ground.
float4x4 _InsideBounceLocalToWorld[MAX_INSIDE_BOUNCE_LIGHTS];
float4x4 _InsideBounceWorldToLocal[MAX_INSIDE_BOUNCE_LIGHTS];

// The color of every bounce shape in rgb and its intensity in a.
float4 _InsideBounceColors[MAX_INSIDE_BOUNCE_LIGHTS];

// Which primitive the shape is in x, how far the lambert is wrapped past the terminator in y, and how far out of the
// shape the light reaches in world units in z.
float4 _InsideBounceParams[MAX_INSIDE_BOUNCE_LIGHTS];

// The world space sphere that encloses the shape and everything its light reaches, as its center in xyz and its radius
// in w. It is what lets a pixel drop a shape before it pays for the matrix multiplication that would place it.
float4 _InsideBounceBounds[MAX_INSIDE_BOUNCE_LIGHTS];

// The counts come across as floats. A material only carries an integer property when the shader declares one, and a
// hidden fullscreen shader has no Properties block to declare it in.
float _AnalyticOccluderCount;
float _AnalyticOcclusionIntensity;
float _InsideBounceCount;
float _InsideBounceIntensity;

// Gets the point on the segment between a and b that is closest to the given position.
float3 GetClosestPointOnSegment(float3 pos, float3 a, float3 b)
{
    float3 axis = b - a;

    // A sphere collapses the segment into a single point, and dividing by its zero length would be a NaN.
    float lengthSq = max(dot(axis, axis), 1e-8);
    float t = saturate(dot(pos - a, axis) / lengthSq);

    return a + axis * t;
}

// Gets the point of the unit box closest to the given local position.
float3 GetClosestPointOnUnitBox(float3 posLS)
{
    return clamp(posLS, -0.5, 0.5);
}

// Gets the point of the unit capsule closest to the given local position. The capsule stands one unit tall along the
// local Y axis and is half a unit across.
float3 GetClosestPointOnUnitCapsule(float3 posLS)
{
    float3 a = float3(0.0, -0.25, 0.0);
    float3 b = float3(0.0, 0.25, 0.0);
    float3 ba = b - a;
    float h = saturate(dot(posLS - a, ba) / dot(ba, ba));
    float3 axisPoint = a + ba * h;
    float3 toAxis = posLS - axisPoint;
    float distanceToAxis = length(toAxis);

    // Inside the shaft there is no surface to walk out to, so the axis itself is the closest point.
    UNITY_BRANCH
    if (distanceToAxis <= 0.25)
        return posLS;

    return axisPoint + toAxis * (0.25 / distanceToAxis);
}

// Gets how much of the light reaching the given surface the hand placed occluders take away from it, as one for a
// surface they leave alone and zero for one they take all the way to black.
float EvaluateAnalyticOcclusion(float3 posWS, float3 normalWS)
{
    float occlusion = 1.0;
    int occluderCount = (int)_AnalyticOccluderCount;

    UNITY_LOOP
    for (int i = 0; i < occluderCount; ++i)
    {
        float4 sphere = _AnalyticOccluderSpheres[i];
        float4 axis = _AnalyticOccluderAxes[i];
        float4 params = _AnalyticOccluderParams[i];

        // The box that encloses the shape and the whole of its falloff, tested before anything is projected onto the
        // segment. Most pixels of a frame are nowhere near most of the shapes in it, and this is what lets them say so
        // for the price of three subtractions.
        float3 extents = abs(axis.xyz) + (sphere.w + params.x);
        float3 delta = abs(posWS - sphere.xyz) - extents;

        UNITY_BRANCH
        if (max(delta.x, max(delta.y, delta.z)) > 0.0)
            continue;

        float3 closestPoint = GetClosestPointOnSegment(posWS, sphere.xyz - axis.xyz, sphere.xyz + axis.xyz);
        float3 toShape = closestPoint - posWS;
        float distanceToSurface = length(toShape) - sphere.w;

        // Past the falloff the shape has nothing left to contribute.
        UNITY_BRANCH
        if (distanceToSurface >= params.x)
            continue;

        float shade = saturate(1.0 - max(distanceToSurface, 0.0) / params.x);

        // Squaring keeps the shade tight around the shape instead of hazing out across its whole reach.
        shade *= shade;

        // A surface turned away from the shape is not occluded by it, which is what keeps this reading as shade rather
        // than as a dark blob painted over everything nearby.
        float facing = saturate(dot(normalWS, normalize(toShape + 1e-6)));
        shade *= lerp(1.0, facing, params.y);

        occlusion *= 1.0 - axis.w * shade;
    }

    return lerp(1.0, occlusion, _AnalyticOcclusionIntensity);
}

// Gets the light the hand placed bounce shapes throw onto the given surface, before the occlusion is applied to it.
//
// The lighting term is a wrapped lambert rather than the real one. Softening the falloff around the terminator is what
// makes the result read as a diffuse bounce instead of as another lamp pointed at the wall.
float3 EvaluateInsideBounceLight(float3 posWS, float3 normalWS)
{
    float3 bounce = float3(0.0, 0.0, 0.0);
    int bounceCount = (int)_InsideBounceCount;

    UNITY_LOOP
    for (int i = 0; i < bounceCount; ++i)
    {
        // The sphere that encloses the shape and the whole of its reach. Rejecting against it costs a subtraction and a
        // dot product, where placing the pixel in the space of the shape costs a matrix multiplication.
        float4 bounds = _InsideBounceBounds[i];
        float3 toBounds = posWS - bounds.xyz;

        UNITY_BRANCH
        if (dot(toBounds, toBounds) > bounds.w * bounds.w)
            continue;

        float4 color = _InsideBounceColors[i];
        float4 params = _InsideBounceParams[i];

        float3 posLS = mul(_InsideBounceWorldToLocal[i], float4(posWS, 1.0)).xyz;

        float3 closestLS = params.x == BOUNCE_SHAPE_BOX
            ? GetClosestPointOnUnitBox(posLS)
            : GetClosestPointOnUnitCapsule(posLS);

        // The reach is measured in world units rather than in the local space of the shape, so that stretching a shape
        // changes where the light comes from without also changing how far it carries.
        float3 closestWS = mul(_InsideBounceLocalToWorld[i], float4(closestLS, 1.0)).xyz;
        float3 toShape = closestWS - posWS;
        float distanceToShape = length(toShape);

        // Past the reach the shape has nothing left to contribute.
        UNITY_BRANCH
        if (distanceToShape >= params.z)
            continue;

        float attenuation = saturate(1.0 - distanceToShape / params.z);

        // Squaring pulls the light in tight around the shape, which is how a real bounce behaves and what keeps a large
        // shape from washing the whole room to a flat grey.
        attenuation *= attenuation;

        // A surface lying right on the shape has no direction to be lit from, so it is simply lit.
        float3 lightDirection = distanceToShape > 1e-4 ? toShape / distanceToShape : normalWS;

        // The wrap is the whole trick. At zero this is an ordinary lambert and the shape reads as a lamp, and as it
        // opens up the terminator softens and spreads around the surface until what is left has no obvious direction at
        // all, which is what indirect light looks like.
        float wrap = params.y;
        float diffuse = saturate((dot(normalWS, lightDirection) + wrap) / (1.0 + wrap));

        bounce += color.rgb * (color.a * attenuation * diffuse);
    }

    return bounce * _InsideBounceIntensity;
}

#endif
