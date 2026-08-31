using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// This is a component that can be added to additional lights to set the parameters that will
	/// affect how this light is considered for the volumetric fog effect.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(Light), typeof(UniversalAdditionalLightData))]
	public sealed class VolumetricAdditionalLight : MonoBehaviour
	{
	#region Private Attributes

		[Tooltip("控制这盏灯照亮的雾往哪个方向散射。正值越大，正对着灯看时雾越亮；负值越小，背对着灯看时雾越亮。越接近 1 或 -1，亮部越集中、扩散得越少。多数情况下使用 0 到 1 之间的正值。")]
		[Range(-1.0f, 1.0f)]
		[SerializeField] private float anisotropy = 0.25f;
		[Tooltip("这盏灯照亮的雾有多亮，数值越大越亮。")]
		[Range(0.0f, 16.0f)]
		[SerializeField] private float scattering = 1.0f;
		[Tooltip("这盏灯的衰减半径。数值越大，靠近灯源处的噪点越少。")]
		[Range(0.0f, 1.0f)]
		[SerializeField] private float radius = 0.2f;

	#endregion

	#region Properties

		public float Anisotropy
		{
			get { return anisotropy; }
			set { anisotropy = Mathf.Clamp(value, -1.0f, 1.0f); }
		}

		public float Scattering
		{
			get { return scattering; }
			set { scattering = Mathf.Clamp(value, 0.0f, 16.0f); }
		}

		public float Radius
		{
			get { return radius; }
			set { radius = Mathf.Clamp01(value); }
		}

	#endregion
	}
}
