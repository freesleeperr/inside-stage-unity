using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// 使靠近该形状的表面变暗。它由手动放置，而不是根据几何体推导，这正是它的意义所在：它不是对光线无法到达位置的
	/// 模拟，而是一把在三维场景中绘制阴影的画笔。将它设为角色的子物体后，它绘制的阴影会跟随角色移动。
	/// </summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[AddComponentMenu("Rendering/Analytic Occluder")]
	public sealed class AnalyticOccluder : MonoBehaviour
	{
		#region Public Types

		/// <summary>
		/// 用于绘制阴影的形状。
		/// </summary>
		public enum OccluderShape
		{
			Sphere,
			Capsule
		}

		#endregion

		#region Public Attributes

		[Tooltip("用于绘制阴影的形状。球体会将阴影集中在单个物体下方，胶囊体则会沿长度方向延展阴影，适合身体、管道或光束。")]
		public OccluderShape shape = OccluderShape.Sphere;
		[Tooltip("形状的半径，以世界单位表示，未计入变换组件的缩放。")]
		[Min(0.001f)]
		public float radius = 0.5f;
		[Tooltip("胶囊体的长度，即沿本地 Y 轴测量的两个端部半球中心之间的距离。球体会忽略此参数。")]
		[Min(0.0f)]
		public float height = 1.0f;
		[Tooltip("阴影在形状表面处的暗度。设为 1 时会将表面完全压暗至黑色。")]
		[Range(0.0f, 1.0f)]
		public float intensity = 0.7f;
		[Tooltip("阴影从形状表面向外延伸的世界空间距离，超过该距离后会完全淡出。")]
		[Min(0.001f)]
		public float falloff = 1.2f;
		[Tooltip("阴影限制在朝向该形状的表面上的程度。设为 1 时表现为遮挡，只影响朝向形状的表面；设为 0 时则不考虑表面朝向，将附近所有表面变暗，效果更直接，但有时正适合构图需要。")]
		[Range(0.0f, 1.0f)]
		public float directionality = 0.75f;

		#endregion

		#region Private Attributes

		// 保持形状具有实际表面的最小半径。
		private const float MinimumRadius = 1e-3f;

		private static readonly List<AnalyticOccluder> ActiveOccluders = new List<AnalyticOccluder>();
		private static readonly List<AnalyticOccluder> ClosestOccluders = new List<AnalyticOccluder>();
		private static readonly Comparison<AnalyticOccluder> ClosestOccludersComparison = CompareDistanceToSortPosition;

		private static Vector3 sortPositionWS;

		#endregion

		#region Properties

		/// <summary>
		/// 形状的世界空间半径，已应用变换组件的缩放。取缩放最大的轴，确保放大形状时不会出现看起来比场景视图中更薄的部分。
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
		/// 胶囊体所围绕线段的一半长度，以世界单位表示。球体的该值为零，此时线段会变为单个点，胶囊体也就变成球体。
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

		/// <summary>
		/// 包围该形状及其绘制的全部阴影范围的球体半径。渲染通道会用它与画面进行检测，确保位于摄像机后方的形状不会从实际处于画面中的形状那里抢占槽位。
		/// </summary>
		public float BoundingRadius
		{
			get { return WorldRadius + falloff + WorldHalfAxis.magnitude; }
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

			// 外层形状显示阴影已经完全淡出的位置。
			Gizmos.color = new Color(0.1f, 0.1f, 0.12f, 0.3f);
			DrawShapeGizmo(halfAxis, worldRadius + falloff);
		}

		/// <summary>
		/// 以给定半径绘制形状，将其显示为线段两端的两个端部半球。
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
		/// 获取给定位置到形状所围绕线段的距离平方。当形状数量超过可渲染数量时，该距离用于决定保留哪些形状。
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
		/// 获取能够到达给定平面所描述画面的已启用遮挡体，按距离给定位置的远近排序，最多返回指定数量。返回的列表会在
		/// 调用之间复用，因此必须在再次调用此方法前完成使用。
		///
		/// 不传入平面时会保留所有已启用的遮挡体，适用于调用方没有画面可供剔除的情况。
		/// </summary>
		/// <param name="positionWS"></param>
		/// <param name="frustumPlanes"></param>
		/// <param name="maximumCount"></param>
		/// <returns></returns>
		public static List<AnalyticOccluder> GetClosestActiveOccluders(Vector3 positionWS, Plane[] frustumPlanes, int maximumCount)
		{
			ClosestOccluders.Clear();

			for (int i = 0; i < ActiveOccluders.Count; ++i)
			{
				AnalyticOccluder occluder = ActiveOccluders[i];

				if (occluder == null || !occluder.isActiveAndEnabled || occluder.intensity <= 0.0f)
					continue;

				// 在分配槽位前进行剔除，而不是分配后再剔除，这样远离画面的形状就不会挤掉画面中的形状。
				if (!IsInsideFrustum(frustumPlanes, occluder.transform.position, occluder.BoundingRadius))
					continue;

				ClosestOccluders.Add(occluder);
			}

			// 只有场景中的形状数量超过可渲染数量时，排序才有意义。
			if (ClosestOccluders.Count > maximumCount)
			{
				sortPositionWS = positionWS;
				ClosestOccluders.Sort(ClosestOccludersComparison);
				ClosestOccluders.RemoveRange(maximumCount, ClosestOccluders.Count - maximumCount);
			}

			return ClosestOccluders;
		}

		/// <summary>
		/// 判断给定中心和半径描述的球体是否进入给定平面所描述的画面范围。只要没有平面将形状完全留在后方，就保留该形状；
		/// 因此对于刚好离开画面的形状，检测会倾向于保留而不是将其弹出画面。
		/// </summary>
		/// <param name="frustumPlanes"></param>
		/// <param name="centerWS"></param>
		/// <param name="radius"></param>
		/// <returns></returns>
		private static bool IsInsideFrustum(Plane[] frustumPlanes, Vector3 centerWS, float radius)
		{
			if (frustumPlanes == null)
				return true;

			for (int i = 0; i < frustumPlanes.Length; ++i)
			{
				if (frustumPlanes[i].GetDistanceToPoint(centerWS) < -radius)
					return false;
			}

			return true;
		}

		/// <summary>
		/// 根据两个遮挡体到排序参考位置的距离进行比较。
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
}
