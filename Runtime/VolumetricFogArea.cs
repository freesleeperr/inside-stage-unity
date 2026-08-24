using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Confines the volumetric fog to the box defined by this transform, so that the clouds of fog exist inside a
/// volume of the world instead of filling it entirely. The box is placed, rotated and scaled with the transform,
/// its borders fade the fog in smoothly, and the raymarching is restricted to the part of the ray that crosses
/// it, which concentrates all of the raymarching steps inside of the box and gives the fog a lot more detail.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Volumetric Fog Area")]
public sealed class VolumetricFogArea : MonoBehaviour
{
	#region Public Attributes

	[Tooltip("The size, in local units, of the box that the fog fills. It is scaled by the transform scale.")]
	public Vector3 size = new Vector3(50.0f, 20.0f, 50.0f);
	[Tooltip("The fraction of the box, measured from its sides towards its center, across which the fog fades in. Zero leads to hard edges, while one makes the fog fade in from the sides all the way to the center of the box.")]
	[Range(0.0f, 1.0f)]
	public float horizontalBorder = 0.25f;
	[Tooltip("The fraction of the box, measured from its top and bottom towards its center, across which the fog fades in.")]
	[Range(0.0f, 1.0f)]
	public float verticalBorder = 0.25f;
	[Tooltip("A multiplier for the density that the fog has inside of this area. It allows to make one area denser than the density set in the volume without touching the volume itself.")]
	[Min(0.0f)]
	public float densityMultiplier = 1.0f;

	#endregion

	#region Private Attributes

	// The smallest half extent that keeps the box matrix invertible.
	private const float MinimumHalfExtent = 1e-4f;

	private static readonly List<VolumetricFogArea> ActiveAreas = new List<VolumetricFogArea>();

	#endregion

	#region Properties

	/// <summary>
	/// The half extents of the box, in world units, with the transform scale already applied.
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
	/// The matrix that transforms a world position into the space of the box, where the box goes from -1 to 1
	/// in every axis.
	/// </summary>
	public Matrix4x4 WorldToAreaMatrix
	{
		get { return Matrix4x4.TRS(transform.position, transform.rotation, HalfExtents).inverse; }
	}

	/// <summary>
	/// The borders of the box, as the fraction of each of its half extents across which the fog fades in.
	/// </summary>
	public Vector3 Border
	{
		get { return new Vector3(horizontalBorder, verticalBorder, horizontalBorder); }
	}

	#endregion

	#region MonoBehaviour Methods

	private void OnEnable()
	{
		if (!ActiveAreas.Contains(this))
			ActiveAreas.Add(this);
	}

	private void OnDisable()
	{
		ActiveAreas.Remove(this);
	}

#if UNITY_EDITOR

	private void OnDrawGizmosSelected()
	{
		Vector3 halfExtents = HalfExtents;

		Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
		Gizmos.color = new Color(0.5f, 0.75f, 1.0f, 1.0f);
		Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2.0f);

		// The inner box shows where the borders have already faded the fog completely in.
		Vector3 border = Border;
		Vector3 innerHalfExtents = new Vector3(halfExtents.x * (1.0f - border.x), halfExtents.y * (1.0f - border.y), halfExtents.z * (1.0f - border.z));

		Gizmos.color = new Color(0.5f, 0.75f, 1.0f, 0.35f);
		Gizmos.DrawWireCube(Vector3.zero, innerHalfExtents * 2.0f);
	}

#endif

	#endregion

	#region Methods

	/// <summary>
	/// Gets the enabled area that is closest to the given position, or null when there is none. Only one area is
	/// rendered at a time, so the closest one to the camera is the one that is picked.
	/// </summary>
	/// <param name="positionWS"></param>
	/// <returns></returns>
	public static VolumetricFogArea GetClosestActiveArea(Vector3 positionWS)
	{
		VolumetricFogArea closestArea = null;
		float closestDistanceSq = float.MaxValue;

		for (int i = 0; i < ActiveAreas.Count; ++i)
		{
			VolumetricFogArea area = ActiveAreas[i];

			if (area == null || !area.isActiveAndEnabled || area.densityMultiplier <= 0.0f)
				continue;

			float distanceSq = (area.transform.position - positionWS).sqrMagnitude;

			if (distanceSq < closestDistanceSq)
			{
				closestDistanceSq = distanceSq;
				closestArea = area;
			}
		}

		return closestArea;
	}

	#endregion
}
