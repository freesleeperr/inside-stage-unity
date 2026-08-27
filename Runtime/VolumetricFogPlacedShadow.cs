using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Darkens the volumetric fog with a shadow that is placed by hand, instead of being cast by the geometry of the
/// scene. The shape is placed, rotated and scaled with the transform, and it can either be projected along the
/// direction of the lights, which makes it look like the shadow of an occluder that is not there, or stay where it
/// is, which darkens the fog around it like an ambient occlusion does.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Volumetric Fog Placed Shadow")]
public sealed class VolumetricFogPlacedShadow : MonoBehaviour
{
	#region Public Types

	/// <summary>
	/// The shape that occludes the light.
	/// </summary>
	public enum ShadowShape
	{
		Sphere,
		Box
	}

	#endregion

	#region Public Attributes

	[Tooltip("The shape that occludes the light. Spheres cast round shadows, while boxes cast shadows with straight edges.")]
	public ShadowShape shape = ShadowShape.Sphere;
	[Tooltip("The size, in local units, of the shape. It is scaled by the transform scale, so shapes can be stretched into ellipsoids and slabs.")]
	public Vector3 size = new Vector3(5.0f, 5.0f, 5.0f);
	[Tooltip("How much light this shadow blocks. One blocks it completely, while lower values only tint the fog darker.")]
	[Range(0.0f, 1.0f)]
	public float intensity = 1.0f;
	[Tooltip("The fraction of the shape, measured from its surface outwards, across which the shadow fades out. Zero leads to hard edges.")]
	[Range(0.0f, 1.0f)]
	public float softness = 0.35f;
	[Tooltip("How much the shape is projected along the direction of the lights. One casts a shadow behind the shape, like a real occluder would, while zero just darkens the fog inside of the shape, like an ambient occlusion.")]
	[Range(0.0f, 1.0f)]
	public float projection = 1.0f;
	[Tooltip("How far, in world units, the projected shadow reaches before it fades out completely. Its penumbra also widens across that distance. Zero makes the shadow reach infinitely far.")]
	[Min(0.0f)]
	public float length = 25.0f;

	#endregion

	#region Private Attributes

	// The smallest half extent that keeps the shape matrix invertible.
	private const float MinimumHalfExtent = 1e-4f;

	private static readonly List<VolumetricFogPlacedShadow> ActiveShadows = new List<VolumetricFogPlacedShadow>();
	private static readonly List<VolumetricFogPlacedShadow> ClosestShadows = new List<VolumetricFogPlacedShadow>();
	private static readonly Comparison<VolumetricFogPlacedShadow> ClosestShadowsComparison = CompareDistanceToSortPosition;

	private static Vector3 sortPositionWS;

	#endregion

	#region Properties

	/// <summary>
	/// The half extents of the shape, in world units, with the transform scale already applied.
	/// </summary>
	public Vector3 HalfExtents
	{
		get
		{
			Vector3 scale = transform.lossyScale;

			return new Vector3(Mathf.Max(Mathf.Abs(size.x * scale.x) * 0.5f, MinimumHalfExtent),
							   Mathf.Max(Mathf.Abs(size.y * scale.y) * 0.5f, MinimumHalfExtent),
							   Mathf.Max(Mathf.Abs(size.z * scale.z) * 0.5f, MinimumHalfExtent));
		}
	}

	/// <summary>
	/// The matrix that transforms a world position into the space of the shape, where it is a unit sphere or a unit
	/// box centered at the origin.
	/// </summary>
	public Matrix4x4 WorldToShadowMatrix
	{
		get { return Matrix4x4.TRS(transform.position, transform.rotation, HalfExtents).inverse; }
	}

	/// <summary>
	/// How much the shape is blended from a sphere towards a box.
	/// </summary>
	public float Boxness
	{
		get { return shape == ShadowShape.Box ? 1.0f : 0.0f; }
	}

	/// <summary>
	/// The reciprocal of the length of the shadow, which is zero when the shadow reaches infinitely far.
	/// </summary>
	public float ReciprocalLength
	{
		get { return length > 0.0f ? 1.0f / length : 0.0f; }
	}

	#endregion

	#region MonoBehaviour Methods

	private void OnEnable()
	{
		if (!ActiveShadows.Contains(this))
			ActiveShadows.Add(this);
	}

	private void OnDisable()
	{
		ActiveShadows.Remove(this);
	}

#if UNITY_EDITOR

	private void OnDrawGizmosSelected()
	{
		Vector3 halfExtents = HalfExtents;

		Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, halfExtents);
		Gizmos.color = new Color(0.15f, 0.15f, 0.2f, 1.0f);
		DrawShapeGizmo();

		// The outer shape shows where the softness has already faded the shadow out completely.
		Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, halfExtents * (1.0f + softness));
		Gizmos.color = new Color(0.15f, 0.15f, 0.2f, 0.35f);
		DrawShapeGizmo();

		DrawProjectionGizmo(halfExtents);

		Gizmos.matrix = Matrix4x4.identity;
	}

	/// <summary>
	/// Draws the wire shape that occludes the light, with the gizmo matrix already scaled to its half extents.
	/// </summary>
	private void DrawShapeGizmo()
	{
		if (shape == ShadowShape.Box)
			Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 2.0f);
		else
			Gizmos.DrawWireSphere(Vector3.zero, 1.0f);
	}

	/// <summary>
	/// Draws where the shadow lands, by repeating the shape at the end of the projection along the light of the scene.
	/// </summary>
	/// <param name="halfExtents"></param>
	private void DrawProjectionGizmo(Vector3 halfExtents)
	{
		Light sun = RenderSettings.sun;

		if (sun == null || projection <= 0.0f || length <= 0.0f)
			return;

		Vector3 shadowEnd = transform.position + sun.transform.forward * (length * projection);

		Gizmos.matrix = Matrix4x4.TRS(shadowEnd, transform.rotation, halfExtents * (1.0f + softness * 2.0f));
		Gizmos.color = new Color(0.15f, 0.15f, 0.2f, 0.2f);
		DrawShapeGizmo();

		Gizmos.matrix = Matrix4x4.identity;
		Gizmos.DrawLine(transform.position, shadowEnd);
	}

#endif

	#endregion

	#region Methods

	/// <summary>
	/// Gets the enabled shadows that are closest to the given position, up to the given maximum amount. The returned
	/// list is reused between calls, so it has to be consumed before calling this again.
	/// </summary>
	/// <param name="positionWS"></param>
	/// <param name="maximumCount"></param>
	/// <returns></returns>
	public static List<VolumetricFogPlacedShadow> GetClosestActiveShadows(Vector3 positionWS, int maximumCount)
	{
		ClosestShadows.Clear();

		for (int i = 0; i < ActiveShadows.Count; ++i)
		{
			VolumetricFogPlacedShadow placedShadow = ActiveShadows[i];

			if (placedShadow == null || !placedShadow.isActiveAndEnabled || placedShadow.intensity <= 0.0f)
				continue;

			ClosestShadows.Add(placedShadow);
		}

		// Sorting is only worth it when there are more shadows in the scene than the ones that can be rendered.
		if (ClosestShadows.Count > maximumCount)
		{
			sortPositionWS = positionWS;
			ClosestShadows.Sort(ClosestShadowsComparison);
			ClosestShadows.RemoveRange(maximumCount, ClosestShadows.Count - maximumCount);
		}

		return ClosestShadows;
	}

	/// <summary>
	/// Compares two shadows by how far away they are from the position that the shadows are being sorted around.
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	private static int CompareDistanceToSortPosition(VolumetricFogPlacedShadow a, VolumetricFogPlacedShadow b)
	{
		float distanceSqA = (a.transform.position - sortPositionWS).sqrMagnitude;
		float distanceSqB = (b.transform.position - sortPositionWS).sqrMagnitude;

		return distanceSqA.CompareTo(distanceSqB);
	}

	#endregion
}
