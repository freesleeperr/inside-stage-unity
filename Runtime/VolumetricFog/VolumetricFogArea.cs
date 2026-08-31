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

		[Tooltip("雾所填充的盒体尺寸，单位为局部单位，会随 Transform 的缩放一起缩放。")]
		public Vector3 size = new Vector3(50.0f, 20.0f, 50.0f);
		[Tooltip("从盒体侧面向中心算起，雾淡入所占的比例。为 0 时边缘生硬，为 1 时雾从侧面一路淡入到盒体中心。")]
		[Range(0.0f, 1.0f)]
		public float horizontalBorder = 0.25f;
		[Tooltip("从盒体顶部和底部向中心算起，雾淡入所占的比例。")]
		[Range(0.0f, 1.0f)]
		public float verticalBorder = 0.25f;
		[Tooltip("这个区域内雾浓度的倍率。不必改动 Volume 本身，就能让某一块区域比 Volume 里设定的浓度更浓。")]
		[Min(0.0f)]
		public float densityMultiplier = 1.0f;
		[Tooltip("这个区域内雾所散射光线的颜色倍率。不必为它单独建一个 Volume，就能让这团雾比旁边的更冷或更暖。")]
		[ColorUsage(false, true)]
		public Color tint = Color.white;
		[Tooltip("这个区域的雾与它所重叠的其他区域如何混合。Max 取其中最浓的一个，因此把两个区域叠在一起也不会让交界处变厚，胜出的区域同时决定该处的染色和噪声。Additive 则把这个区域的雾（由它自己的噪声雕刻）叠加到其他区域已经留下的雾之上，这样一团烟就能待在一片薄雾里，而不是把薄雾替换掉。Additive 会在这个区域覆盖的范围内多算一次噪声。")]
		public VolumetricFogAreaBlendMode blendMode = VolumetricFogAreaBlendMode.Max;

		[Header("Noise Override")]
		[Tooltip("让这个区域使用自己的噪声，而不是 Volume 里设定的那一套，这样一小团雾就能按自己的尺度翻涌、随自己的风飘动。需要在 Volume 中启用噪声，本项才会生效。")]
		public bool overrideNoise = false;
		[Tooltip("雕刻这个区域的形状噪声尺寸，单位为世界单位。尺寸小时呈现烟雾般紧凑的翻卷，尺寸大时呈现缓慢流动的雾墙。")]
		[Min(0.01f)]
		public float noiseTileSize = 3.0f;
		[Tooltip("把这个区域边缘侵蚀成缕状的细节噪声尺寸，单位为世界单位。")]
		[Min(0.01f)]
		public float noiseDetailTileSize = 0.8f;
		[Tooltip("噪声最多能削掉这个区域多少浓度。为 0 时雾是均匀的。")]
		[Range(0.0f, 1.0f)]
		public float noiseIntensity = 0.9f;
		[Tooltip("雾覆盖这个区域的比例。数值越低，缝隙越多，雾会被分成一缕一缕的。")]
		[Range(0.0f, 1.0f)]
		public float noiseCoverage = 0.55f;
		[Tooltip("细节噪声对这个区域的侵蚀程度。")]
		[Range(0.0f, 1.0f)]
		public float noiseDetailIntensity = 0.45f;
		[Tooltip("这个区域的雾被吹向的方向。朝上时烟就会往上升。")]
		public Vector3 noiseWindDirection = new Vector3(0.15f, 1.0f, 0.0f);
		[Tooltip("这个区域的雾沿风向被吹动的速度。")]
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
		/// Whether this area piles its fog on top of the areas it overlaps instead of competing with them for the
		/// densest one, as the shader reads it out of a float array.
		/// </summary>
		public float BlendModeAsFloat
		{
			get { return (float)(int)blendMode; }
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
