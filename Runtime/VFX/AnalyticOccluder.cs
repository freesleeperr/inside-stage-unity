using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Darkens whatever surface is near this shape. It is placed by hand rather than derived from the geometry, which is
/// the whole point: it is not a simulation of where light fails to reach, it is a brush for painting shade into the
/// scene in three dimensions. Parent one to a character and the shade it lays down follows the character around.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Analytic Occluder")]
public sealed class AnalyticOccluder : MonoBehaviour
{
	#region Public Types

	/// <summary>
	/// The shape that lays the shade down.
	/// </summary>
	public enum OccluderShape
	{
		Sphere,
		Capsule
	}

	#endregion

	#region Public Attributes

	[Tooltip("The shape that lays the shade down. A sphere pools it under a single object, while a capsule spreads it along a length, which is what a body, a pipe or a beam wants.")]
	public OccluderShape shape = OccluderShape.Sphere;
	[Tooltip("The radius of the shape, in world units, before the transform scale is applied.")]
	[Min(0.001f)]
	public float radius = 0.5f;
	[Tooltip("The length of the capsule, measured between the centers of its two caps, along the local Y axis. It is ignored by spheres.")]
	[Min(0.0f)]
	public float height = 1.0f;
	[Tooltip("How dark the shade gets right at the surface of the shape. One takes the surface all the way to black.")]
	[Range(0.0f, 1.0f)]
	public float intensity = 0.7f;
	[Tooltip("How far past the surface of the shape, in world units, the shade reaches before it fades out completely.")]
	[Min(0.001f)]
	public float falloff = 1.2f;
	[Tooltip("How much the shade is limited to the surfaces that actually face the shape. At one it behaves like occlusion and leaves the surfaces turned away from it alone, while at zero it darkens everything nearby regardless of which way it faces, which is blunter but sometimes exactly what a composition needs.")]
	[Range(0.0f, 1.0f)]
	public float directionality = 0.75f;

	#endregion

	#region Private Attributes

	// The smallest radius that keeps the shape from collapsing into something with no surface at all.
	private const float MinimumRadius = 1e-3f;

	private static readonly List<AnalyticOccluder> ActiveOccluders = new List<AnalyticOccluder>();
	private static readonly List<AnalyticOccluder> ClosestOccluders = new List<AnalyticOccluder>();
	private static readonly Comparison<AnalyticOccluder> ClosestOccludersComparison = CompareDistanceToSortPosition;

	private static Vector3 sortPositionWS;

	#endregion

	#region Properties

	/// <summary>
	/// The radius of the shape in world units, with the transform scale already applied. The largest axis wins, so
	/// that scaling a shape up never leaves part of it thinner than it looks in the scene view.
	/// </summary>
	public float WorldRadius
	{
		get
		{
			Vector3 scale = transform.lossyScale;
			float maximumScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

			return Mathf.Max(radius * maximumScale, MinimumRadius);
		}
	}

	/// <summary>
	/// Half of the segment that the capsule is built around, in world units. It is zero for spheres, which turns the
	/// segment into a single point and the capsule into a sphere.
	/// </summary>
	public Vector3 WorldHalfAxis
	{
		get
		{
			if (shape == OccluderShape.Sphere)
				return Vector3.zero;

			return transform.up * (height * 0.5f * Mathf.Abs(transform.lossyScale.y));
		}
	}

	#endregion

	#region MonoBehaviour Methods

	private void OnEnable()
	{
		if (!ActiveOccluders.Contains(this))
			ActiveOccluders.Add(this);
	}

	private void OnDisable()
	{
		ActiveOccluders.Remove(this);
	}

#if UNITY_EDITOR

	private void OnDrawGizmosSelected()
	{
		Vector3 halfAxis = WorldHalfAxis;
		float worldRadius = WorldRadius;

		Gizmos.color = new Color(0.1f, 0.1f, 0.12f, 1.0f);
		DrawShapeGizmo(halfAxis, worldRadius);

		// The outer shape shows where the shade has already faded out completely.
		Gizmos.color = new Color(0.1f, 0.1f, 0.12f, 0.3f);
		DrawShapeGizmo(halfAxis, worldRadius + falloff);
	}

	/// <summary>
	/// Draws the shape at the given radius, as the two caps of its segment.
	/// </summary>
	/// <param name="halfAxis"></param>
	/// <param name="drawRadius"></param>
	private void DrawShapeGizmo(Vector3 halfAxis, float drawRadius)
	{
		Vector3 center = transform.position;

		Gizmos.DrawWireSphere(center + halfAxis, drawRadius);

		if (halfAxis == Vector3.zero)
			return;

		Gizmos.DrawWireSphere(center - halfAxis, drawRadius);
		Gizmos.DrawLine(center + halfAxis, center - halfAxis);
	}

#endif

	#endregion

	#region Methods

	/// <summary>
	/// Gets the squared distance from the given position to the segment this shape is built around, which is what
	/// decides which shapes are kept when there are more of them than can be rendered.
	/// </summary>
	/// <param name="positionWS"></param>
	/// <returns></returns>
	public float GetSquaredDistanceToSegment(Vector3 positionWS)
	{
		Vector3 center = transform.position;
		Vector3 halfAxis = WorldHalfAxis;

		if (halfAxis == Vector3.zero)
			return (center - positionWS).sqrMagnitude;

		Vector3 start = center - halfAxis;
		Vector3 axis = halfAxis * 2.0f;
		float t = Mathf.Clamp01(Vector3.Dot(positionWS - start, axis) / Vector3.Dot(axis, axis));

		return (start + axis * t - positionWS).sqrMagnitude;
	}

	/// <summary>
	/// Gets the enabled occluders closest to the given position, up to the given maximum amount. The returned list is
	/// reused between calls, so it has to be consumed before calling this again.
	/// </summary>
	/// <param name="positionWS"></param>
	/// <param name="maximumCount"></param>
	/// <returns></returns>
	public static List<AnalyticOccluder> GetClosestActiveOccluders(Vector3 positionWS, int maximumCount)
	{
		ClosestOccluders.Clear();

		for (int i = 0; i < ActiveOccluders.Count; ++i)
		{
			AnalyticOccluder occluder = ActiveOccluders[i];

			if (occluder == null || !occluder.isActiveAndEnabled || occluder.intensity <= 0.0f)
				continue;

			ClosestOccluders.Add(occluder);
		}

		// Sorting is only worth it when there are more shapes in the scene than the ones that can be rendered.
		if (ClosestOccluders.Count > maximumCount)
		{
			sortPositionWS = positionWS;
			ClosestOccluders.Sort(ClosestOccludersComparison);
			ClosestOccluders.RemoveRange(maximumCount, ClosestOccluders.Count - maximumCount);
		}

		return ClosestOccluders;
	}

	/// <summary>
	/// Compares two occluders by how far away they are from the position they are being sorted around.
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	private static int CompareDistanceToSortPosition(AnalyticOccluder a, AnalyticOccluder b)
	{
		float distanceSqA = a.GetSquaredDistanceToSegment(sortPositionWS);
		float distanceSqB = b.GetSquaredDistanceToSegment(sortPositionWS);

		return distanceSqA.CompareTo(distanceSqB);
	}

	#endregion
}
