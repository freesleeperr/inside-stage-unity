using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// Holds volumetric fog inside the box defined by this transform, so that the clouds of fog exist inside a volume
	/// of the world instead of filling it entirely. The box is placed, rotated and scaled with the transform, and its
	/// borders fade the fog in smoothly. Several areas can hold fog at the same time, and when the volume leaves no fog
	/// outside of them the raymarching is restricted to the part of the ray that crosses them, which gathers the
	/// raymarching steps around the areas and gives the fog they hold a lot more detail.
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
		[Tooltip("A multiplier color for the light that the fog scatters inside of this area. It allows one pocket of fog to be colder or warmer than the next one without needing a volume of its own.")]
		[ColorUsage(false, true)]
		public Color tint = Color.white;

		[Header("Noise Override")]
		[Tooltip("Gives this area its own noise instead of the one authored in the volume, so that a small pocket of fog can churn at its own scale and drift on its own wind. The noise has to be enabled in the volume for this to have any effect.")]
		public bool overrideNoise = false;
		[Tooltip("The size, in world units, of the shape noise that carves this area. Small sizes give the tight curl of smoke, while big ones give the slow banks of a haze.")]
		[Min(0.01f)]
		public float noiseTileSize = 3.0f;
		[Tooltip("The size, in world units, of the detail noise that erodes the edges of this area into wisps.")]
		[Min(0.01f)]
		public float noiseDetailTileSize = 0.8f;
		[Tooltip("How much of the density of this area the noise is allowed to carve away. Zero leaves the fog uniform.")]
		[Range(0.0f, 1.0f)]
		public float noiseIntensity = 0.9f;
		[Tooltip("How much of this area the fog covers. Low values open up the gaps that separate the fog into tendrils.")]
		[Range(0.0f, 1.0f)]
		public float noiseCoverage = 0.55f;
		[Tooltip("How much the detail noise erodes this area.")]
		[Range(0.0f, 1.0f)]
		public float noiseDetailIntensity = 0.45f;
		[Tooltip("The direction that the fog of this area is blown towards. Pointing it up is what makes smoke rise.")]
		public Vector3 noiseWindDirection = new Vector3(0.15f, 1.0f, 0.0f);
		[Tooltip("How fast the fog of this area is blown along its wind direction.")]
		[Min(0.0f)]
		public float noiseWindSpeed = 0.35f;

	#endregion

	#region Private Attributes

		// The smallest half extent that keeps the box matrix invertible.
		private const float MinimumHalfExtent = 1e-4f;

		private static readonly List<VolumetricFogArea> ActiveAreas = new List<VolumetricFogArea>();
		private static readonly List<VolumetricFogArea> ClosestAreas = new List<VolumetricFogArea>();
		private static readonly Comparison<VolumetricFogArea> ClosestAreasComparison = CompareDistanceToSortPosition;

		private static Vector3 sortPositionWS;

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

		/// <summary>
		/// The borders of the box packed in the xyz channels, with its density multiplier in the w one, which is how
		/// the shader reads them.
		/// </summary>
		public Vector4 BorderAndDensity
		{
			get
			{
				Vector3 border = Border;

				return new Vector4(border.x, border.y, border.z, densityMultiplier);
			}
		}

		/// <summary>
		/// The noise override of this area packed the way the shader reads it: the tiling of the shape noise in x, the
		/// tiling of the detail noise in y, its intensity in z and its coverage in w. The x channel is zero when this
		/// area does not override the noise, which is what the shader branches on.
		/// </summary>
		public Vector4 NoiseParameters
		{
			get
			{
				if (!overrideNoise)
					return Vector4.zero;

				return new Vector4(1.0f / noiseTileSize, 1.0f / noiseDetailTileSize, noiseIntensity, noiseCoverage);
			}
		}

		/// <summary>
		/// Gets the offset, in world units, that the wind of this area has drifted its noise by at the given time. The
		/// fog is blown towards the wind direction, so the noise is scrolled towards the opposite one.
		/// </summary>
		/// <param name="time"></param>
		/// <returns></returns>
		public Vector3 GetNoiseWindOffset(float time)
		{
			Vector3 direction = noiseWindDirection.sqrMagnitude > 0.0f ? noiseWindDirection.normalized : Vector3.zero;

			return -direction * (noiseWindSpeed * time);
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
		/// Gets the squared distance from the given position to the box of this area, which is zero for positions that
		/// are inside of it. Measuring against the box instead of against its center keeps a big area that surrounds the
		/// camera from being sorted behind the small ones that are gathered further away.
		/// </summary>
		/// <param name="positionWS"></param>
		/// <returns></returns>
		public float GetSquaredDistanceToBox(Vector3 positionWS)
		{
			Vector3 posLS = Quaternion.Inverse(transform.rotation) * (positionWS - transform.position);
			Vector3 halfExtents = HalfExtents;

			Vector3 distanceToFaces = new Vector3(Mathf.Max(Mathf.Abs(posLS.x) - halfExtents.x, 0.0f),
												  Mathf.Max(Mathf.Abs(posLS.y) - halfExtents.y, 0.0f),
												  Mathf.Max(Mathf.Abs(posLS.z) - halfExtents.z, 0.0f));

			return distanceToFaces.sqrMagnitude;
		}

		/// <summary>
		/// Gets the enabled areas that are closest to the given position, up to the given maximum amount. The returned
		/// list is reused between calls, so it has to be consumed before calling this again.
		/// </summary>
		/// <param name="positionWS"></param>
		/// <param name="maximumCount"></param>
		/// <returns></returns>
		public static List<VolumetricFogArea> GetClosestActiveAreas(Vector3 positionWS, int maximumCount)
		{
			ClosestAreas.Clear();

			for (int i = 0; i < ActiveAreas.Count; ++i)
			{
				VolumetricFogArea area = ActiveAreas[i];

				if (area == null || !area.isActiveAndEnabled || area.densityMultiplier <= 0.0f)
					continue;

				ClosestAreas.Add(area);
			}

			// Sorting is only worth it when there are more areas in the scene than the ones that can be rendered.
			if (ClosestAreas.Count > maximumCount)
			{
				sortPositionWS = positionWS;
				ClosestAreas.Sort(ClosestAreasComparison);
				ClosestAreas.RemoveRange(maximumCount, ClosestAreas.Count - maximumCount);
			}

			return ClosestAreas;
		}

		/// <summary>
		/// Compares two areas by how far away they are from the position that the areas are being sorted around.
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		private static int CompareDistanceToSortPosition(VolumetricFogArea a, VolumetricFogArea b)
		{
			float distanceSqA = a.GetSquaredDistanceToBox(sortPositionWS);
			float distanceSqB = b.GetSquaredDistanceToBox(sortPositionWS);

			return distanceSqA.CompareTo(distanceSqB);
		}

	#endregion
	}
}
