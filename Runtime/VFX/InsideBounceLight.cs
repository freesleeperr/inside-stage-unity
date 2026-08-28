using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// 将光线反射到形状附近的表面上。它用于代替光照贴图烘焙出的间接反弹光，区别在于它由手动放置并且在运行时保持有效：
	/// 可以移动，可以通过灯光预设关闭，也可以设为灯具或手电筒的子物体随其移动。
	///
	/// 该形状是一个由变换组件拉伸到指定位置的单位图元，因此单个组件就能覆盖拉长胶囊形的走廊或扁平板状的地面，
	/// 不需要使用一排点光源来完成同样的效果。
	/// </summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[AddComponentMenu("Rendering/Inside Bounce Light")]
	public sealed class InsideBounceLight : MonoBehaviour
	{
		#region Public Types

		/// <summary>
		/// 发出反弹光的图元形状。
		/// </summary>
		public enum BounceShape
		{
			Box,
			Capsule
		}

		#endregion

		#region Public Attributes

		[Tooltip("光线发出的图元形状。压扁后贴在地面上的方盒体可模拟地面向上反射的光线，沿某个轴拉长的胶囊体则适合走廊、灯带或窗缝。")]
		public BounceShape shape = BounceShape.Box;
		[Tooltip("光线的颜色。真实的反弹光会带有反射表面的颜色，因此即使上方灯具是白色的，暖色地面也应反射出暖色光。")]
		[ColorUsage(false, true)]
		public Color color = Color.white;
		[Tooltip("光线在形状表面处的亮度。")]
		[Min(0.0f)]
		public float intensity = 0.6f;
		[Tooltip("光线从形状表面向外传播的世界空间距离，超过该距离后会完全淡出。它以世界空间测量，因此拉伸形状只会改变光线的来源位置，不会改变光线的传播距离。")]
		[Min(0.001f)]
		public float reach = 3.0f;
		[Tooltip("兰伯特项越过明暗交界线的包裹程度。为零时表现得像普通灯光；数值增大后，衰减会变得更柔和、更分散，直到剩余光线几乎没有方向性，从而呈现为间接光而不是另一个光源。")]
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
		/// 将单位图元拉伸为场景中实际放置形状的变换矩阵。
		/// </summary>
		public Matrix4x4 LocalToWorld
		{
			get { return transform.localToWorldMatrix; }
		}

		/// <summary>
		/// 将世界坐标位置转换回单位图元定义空间的变换矩阵。
		/// </summary>
		public Matrix4x4 WorldToLocal
		{
			get { return transform.worldToLocalMatrix; }
		}

		/// <summary>
		/// 包围该形状及其光照范围的球体中心。单位图元以自身空间原点为中心，因此这里就是变换组件所在的位置。
		/// </summary>
		public Vector3 BoundingCenter
		{
			get { return transform.position; }
		}

		/// <summary>
		/// 包围该形状及其光照范围的球体半径。渲染通道使用它与画面进行检测，使摄像机后方的形状不会抢占实际位于画面中的
		/// 形状的槽位；像素也会先使用它进行检测，再决定是否付出代价转换到该形状的空间中。
		/// </summary>
		public float BoundingRadius
		{
			get
			{
				Vector3 scale = transform.lossyScale;
				Vector3 absoluteScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

				// 拉伸后单位方盒体对角线的一半，它同样可以包围拉伸后的单位胶囊体。
				return 0.5f * absoluteScale.magnitude + reach;
			}
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
		/// 将单位胶囊体绘制为组成它的两个球体及其之间的连接杆。
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
		/// 获取能够到达给定平面所描述画面的已启用反弹光，按距离给定位置的远近排序，最多返回指定数量。
		/// 返回的列表会在调用之间复用，因此必须在再次调用此方法前完成使用。
		///
		/// 不传入平面时会保留所有已启用的灯光，适用于调用方没有画面可供剔除的情况。
		/// </summary>
		/// <param name="positionWS"></param>
		/// <param name="frustumPlanes"></param>
		/// <param name="maximumCount"></param>
		/// <returns></returns>
		public static List<InsideBounceLight> GetClosestActiveLights(Vector3 positionWS, Plane[] frustumPlanes, int maximumCount)
		{
			ClosestLights.Clear();

			for (int i = 0; i < ActiveLights.Count; ++i)
			{
				InsideBounceLight light = ActiveLights[i];

				if (light == null || !light.isActiveAndEnabled || light.intensity <= 0.0f)
					continue;

				// 在分配槽位前进行剔除，而不是分配后再剔除，这样远离画面的形状就不会挤掉画面中的形状。
				if (!IsInsideFrustum(frustumPlanes, light.BoundingCenter, light.BoundingRadius))
					continue;

				ClosestLights.Add(light);
			}

			// 只有场景中的灯光数量超过可渲染数量时，排序才有意义。
			if (ClosestLights.Count > maximumCount)
			{
				sortPositionWS = positionWS;
				ClosestLights.Sort(ClosestLightsComparison);
				ClosestLights.RemoveRange(maximumCount, ClosestLights.Count - maximumCount);
			}

			return ClosestLights;
		}

		/// <summary>
		/// 判断给定中心和半径描述的球体是否进入给定平面所描述的画面范围。
		/// 只要没有平面将形状完全留在后方，就保留该形状；因此对于刚好离开画面的形状，检测会倾向于保留而不是将其弹出画面。
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
		/// 根据两个灯光到排序参考位置的距离进行比较。
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
}
