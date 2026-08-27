using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Throws light back onto whatever surface is near this shape. It stands in for the indirect bounce that a lightmap
/// would have baked, with the difference that it is placed by hand and stays alive at runtime: it can be moved, it can
/// be turned off with a lighting preset, and it can be parented to a lamp or a torch and carried around.
///
/// The shape is a unit primitive that the transform stretches into place, so a single one of these covers a corridor
/// as a drawn out pill or a floor as a flattened slab, rather than needing a row of point lights to do the same job.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Inside Bounce Light")]
public sealed class InsideBounceLight : MonoBehaviour
{
	#region Public Types

	/// <summary>
	/// The primitive the light is thrown from.
	/// </summary>
	public enum BounceShape
	{
		Box,
		Capsule
	}

	#endregion

	#region Public Attributes

	[Tooltip("The primitive the light is thrown from. A box flattened onto a floor is the light a floor throws back up, while a capsule drawn out along an axis is what a corridor, a strip light or a window slot wants.")]
	public BounceShape shape = BounceShape.Box;
	[Tooltip("The color of the light. A real bounce carries the color of whatever it came off, so a warm floor should throw warm light even when the lamp above it is white.")]
	[ColorUsage(false, true)]
	public Color color = Color.white;
	[Tooltip("How bright the light is right at the surface of the shape.")]
	[Min(0.0f)]
	public float intensity = 0.6f;
	[Tooltip("How far past the surface of the shape, in world units, the light carries before it fades out completely. It is measured in world space, so stretching the shape changes where the light comes from without changing how far it reaches.")]
	[Min(0.001f)]
	public float reach = 3.0f;
	[Tooltip("How far the lambert term is wrapped past the terminator. At zero this behaves like an ordinary lamp, and as it opens up the falloff softens and spreads until what is left has almost no direction to it, which is what makes it read as indirect light rather than as another light source.")]
	[Range(0.0f, 1.0f)]
	public float wrap = 0.7f;

	#endregion

	#region Private Attributes

	private static readonly List<InsideBounceLight> ActiveLights = new List<InsideBounceLight>();
	private static readonly List<InsideBounceLight> ClosestLights = new List<InsideBounceLight>();
	private static readonly Comparison<InsideBounceLight> ClosestLightsComparison = CompareDistanceToSortPosition;

	private static Vector3 sortPositionWS;

	#endregion

	#region Properties

	/// <summary>
	/// The transform that stretches the unit primitive into the shape that is actually placed in the scene.
	/// </summary>
	public Matrix4x4 LocalToWorld
	{
		get { return transform.localToWorldMatrix; }
	}

	/// <summary>
	/// The transform that takes a world position back into the space the unit primitive is defined in.
	/// </summary>
	public Matrix4x4 WorldToLocal
	{
		get { return transform.worldToLocalMatrix; }
	}

	#endregion

	#region MonoBehaviour Methods

	private void OnEnable()
	{
		if (!ActiveLights.Contains(this))
			ActiveLights.Add(this);
	}

	private void OnDisable()
	{
		ActiveLights.Remove(this);
	}

#if UNITY_EDITOR

	private void OnDrawGizmosSelected()
	{
		Gizmos.matrix = transform.localToWorldMatrix;
		Gizmos.color = new Color(color.r, color.g, color.b, 1.0f);

		if (shape == BounceShape.Box)
			Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
		else
			DrawCapsuleGizmo();

		Gizmos.matrix = Matrix4x4.identity;
	}

	/// <summary>
	/// Draws the unit capsule as the two spheres it is built from and the shaft between them.
	/// </summary>
	private void DrawCapsuleGizmo()
	{
		Vector3 top = new Vector3(0.0f, 0.25f, 0.0f);
		Vector3 bottom = new Vector3(0.0f, -0.25f, 0.0f);

		Gizmos.DrawWireSphere(top, 0.25f);
		Gizmos.DrawWireSphere(bottom, 0.25f);

		Gizmos.DrawLine(top + Vector3.right * 0.25f, bottom + Vector3.right * 0.25f);
		Gizmos.DrawLine(top - Vector3.right * 0.25f, bottom - Vector3.right * 0.25f);
		Gizmos.DrawLine(top + Vector3.forward * 0.25f, bottom + Vector3.forward * 0.25f);
		Gizmos.DrawLine(top - Vector3.forward * 0.25f, bottom - Vector3.forward * 0.25f);
	}

#endif

	#endregion

	#region Methods

	/// <summary>
	/// Gets the enabled bounce lights closest to the given position, up to the given maximum amount. The returned list
	/// is reused between calls, so it has to be consumed before calling this again.
	/// </summary>
	/// <param name="positionWS"></param>
	/// <param name="maximumCount"></param>
	/// <returns></returns>
	public static List<InsideBounceLight> GetClosestActiveLights(Vector3 positionWS, int maximumCount)
	{
		ClosestLights.Clear();

		for (int i = 0; i < ActiveLights.Count; ++i)
		{
			InsideBounceLight light = ActiveLights[i];

			if (light == null || !light.isActiveAndEnabled || light.intensity <= 0.0f)
				continue;

			ClosestLights.Add(light);
		}

		// Sorting is only worth it when there are more lights in the scene than the ones that can be rendered.
		if (ClosestLights.Count > maximumCount)
		{
			sortPositionWS = positionWS;
			ClosestLights.Sort(ClosestLightsComparison);
			ClosestLights.RemoveRange(maximumCount, ClosestLights.Count - maximumCount);
		}

		return ClosestLights;
	}

	/// <summary>
	/// Compares two lights by how far away they are from the position they are being sorted around.
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	private static int CompareDistanceToSortPosition(InsideBounceLight a, InsideBounceLight b)
	{
		float distanceSqA = (a.transform.position - sortPositionWS).sqrMagnitude;
		float distanceSqB = (b.transform.position - sortPositionWS).sqrMagnitude;

		return distanceSqA.CompareTo(distanceSqB);
	}

	#endregion
}
