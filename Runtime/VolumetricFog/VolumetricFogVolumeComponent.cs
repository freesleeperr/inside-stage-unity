using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// Volume component for the volumetric fog.
	/// </summary>
#if UNITY_2023_1_OR_NEWER
	[VolumeComponentMenu("Custom/Volumetric Fog")]
#if UNITY_6000_0_OR_NEWER
	[VolumeRequiresRendererFeatures(typeof(VolumetricFogRendererFeature))]
#endif
	[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
	[VolumeComponentMenuForRenderPipeline("Custom/Volumetric Fog", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_6000_3_OR_NEWER
	[DisplayInfo(name = "Volumetric Fog")]
#endif
	public sealed class VolumetricFogVolumeComponent : VolumeComponent, IPostProcessComponent
	{
	#region Public Attributes

		[Header("General")]
		[Tooltip("关闭后体积雾的所有功能都不会被渲染。")]
		public BoolParameter enabled = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);

		[Header("Shape - Density")]
		[Tooltip("雾有多浓。")]
		public ClampedFloatParameter density = new ClampedFloatParameter(0.2f, 0.0f, 1.0f);
		[Tooltip("场景中体积雾区域之外还有多少雾，取值为下方浓度的比例。为 0 时雾完全被限制在各区域内部，光线步进的采样点也因此能集中到区域周围。数值更高时到处都会留下一层稀薄的雾，各区域则在这层雾之上成为更浓的团块。场景中没有任何区域时本项无效。")]
		public ClampedFloatParameter areaGlobalDensity = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

		[Header("Shape - Distance")]
		[Tooltip("雾最远渲染到距相机多远的位置。")]
		public ClampedFloatParameter distance = new ClampedFloatParameter(64.0f, 0.0f, 512.0f);
		[Tooltip("距相机多远处雾才开始出现。比这更近的地方完全清澈，从而让前景保持清晰，并与背景拉开层次。")]
		public MinFloatParameter startDistance = new MinFloatParameter(0.0f, 0.0f);
		[Tooltip("距相机多远处雾达到 Volume 中设定的浓度。在起始距离与这个距离之间，雾会逐渐堆积。数值等于或小于起始距离时，雾立刻达到完整浓度。")]
		public MinFloatParameter fullDensityDistance = new MinFloatParameter(0.0f, 0.0f);
		[Tooltip("控制雾在起始距离到满浓度距离之间如何堆积。等于 1 时线性堆积，小于 1 时雾很快变浓随后趋于平缓，大于 1 时近处的雾更稀薄，把大部分堆积推向满浓度距离处。")]
		public ClampedFloatParameter distanceFalloff = new ClampedFloatParameter(1.0f, 0.1f, 8.0f);

		[Header("Shape - Height")]
		[Tooltip("在这个世界高度上，雾具有 Volume 中设定的浓度。")]
		public FloatParameter baseHeight = new FloatParameter(0.0f, true);
		[Tooltip("在这个世界高度上，雾的浓度降为零。")]
		public FloatParameter maximumHeight = new FloatParameter(50.0f, true);
		[Tooltip("控制雾在基准高度到最大高度之间如何变稀。等于 1 时线性变稀，小于 1 时雾在大部分高度上都很浓，然后迅速消散；大于 1 时雾集中在基准高度附近，看起来像贴地的一层薄雾。")]
		public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(1.0f, 0.1f, 8.0f);
		[Tooltip("启用后可以指定一个世界高度，低于它的地方雾的浓度为零。")]
		public BoolParameter enableGround = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("低于这个世界高度的地方，雾的浓度为零。")]
		public FloatParameter groundHeight = new FloatParameter(0.0f);

		[Header("Noise (Clouds)")]
		[Tooltip("启用后，雾的浓度会被动态的三维噪声调制，呈现云团般的体积感。")]
		public BoolParameter enableNoise = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("噪声对雾浓度的调制程度。为 0 时雾完全均匀，为 1 时完全由噪声决定。")]
		public ClampedFloatParameter noiseIntensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
		[Tooltip("云状雾覆盖了多大范围。数值越低，云团之间留出的清澈空隙越大。")]
		public ClampedFloatParameter noiseCoverage = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
		[Tooltip("决定云状雾整体形状的那层噪声，其一个平铺单元的世界尺寸，单位为米。")]
		public MinFloatParameter noiseTileSize = new MinFloatParameter(48.0f, 0.01f);
		[Tooltip("云状雾被吹向的方向，不需要归一化。")]
		public Vector3Parameter windDirection = new Vector3Parameter(new Vector3(1.0f, 0.0f, 0.25f));
		[Tooltip("云状雾被风吹动的速度，单位为米每秒。")]
		public MinFloatParameter windSpeed = new MinFloatParameter(1.5f, 0.0f);
		[Tooltip("距相机多远处噪声开始淡回均匀的雾，用来掩盖光线步进步长过长、无法解析噪声而产生的锯齿。为 0 时不做这种淡出。")]
		public MinFloatParameter noiseFadeDistance = new MinFloatParameter(0.0f, 0.0f);
		[Tooltip("压暗雾最稀薄处所散射的光，让云状雾看起来更蓬松、对比更强。")]
		public ClampedFloatParameter powderIntensity = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
		[Tooltip("启用后，第三层频率低得多的噪声会在世界范围内改变覆盖度，把雾聚成一片片雾墙、中间留出空隙，而不是均匀铺开。正是它让远望时的雾有了纵深感。")]
		public BoolParameter enableNoiseMacro = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("把雾聚成雾墙的那层噪声，其一个平铺单元的世界尺寸，单位为米。它应当比形状平铺尺寸大上几倍，否则雾墙太小、看不出成片的效果。")]
		public MinFloatParameter noiseMacroTileSize = new MinFloatParameter(240.0f, 0.01f);
		[Tooltip("雾墙对覆盖度的改变幅度。为 0 时覆盖度均匀，为 1 时从空隙中央的完全清澈变化到雾墙中央的两倍覆盖度。")]
		public ClampedFloatParameter noiseMacroIntensity = new ClampedFloatParameter(0.75f, 0.0f, 1.0f);
		[Tooltip("启用后，第二层更高频的噪声会侵蚀云状雾的边缘，以一定性能为代价增添缕状细节。")]
		public BoolParameter enableNoiseDetail = new BoolParameter(true, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("侵蚀云状雾边缘的那层噪声，其一个平铺单元的世界尺寸，单位为米。")]
		public MinFloatParameter noiseDetailTileSize = new MinFloatParameter(8.0f, 0.01f);
		[Tooltip("细节噪声对云状雾边缘的侵蚀程度。")]
		public ClampedFloatParameter noiseDetailIntensity = new ClampedFloatParameter(0.35f, 0.0f, 1.0f);

		[Header("Lighting - Ambient")]
		[Tooltip("随着距离增加，雾对光线的衰减程度。数值越小画面越暗。")]
		public MinFloatParameter attenuationDistance = new MinFloatParameter(128.0f, 0.05f);
		[Tooltip("启用后，雾会散射一份不来自任何具体灯光的恒定光照，让没有光直接照到的地方也依然可辨。不启用时，暗处的雾会变成纯黑。")]
		public BoolParameter enableAmbientContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("从四面八方同时照到雾上的光的颜色。让它与场景的环境光或天空一致，雾才会融进画面，而不是浮在画面之上。")]
		public ColorParameter ambientColor = new ColorParameter(Color.white, true, false, true);
		[Tooltip("雾散射多少环境光。正是这个参数把雾从纯黑里提起来，所以很小的数值就已经很明显。")]
		public MinFloatParameter ambientIntensity = new MinFloatParameter(0.1f, 0.0f);
#if UNITY_2023_1_OR_NEWER
		[Tooltip("启用后会采样自适应探针体积（APV），让它参与雾的照明。")]
		public BoolParameter enableAPVContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("启用探针体积贡献后，来自自适应探针体积（APV）的光照所占的权重。")]
		public ClampedFloatParameter APVContributionWeight = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
#endif

		[Header("Lighting - Main Light")]
		[Tooltip("关闭后将不再计算主光源对雾的贡献，多数情况下能提升性能。")]
		public BoolParameter enableMainLightContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("控制主光源照亮的雾往哪个方向散射。正值越大，正对着光看时雾越亮；负值越小，背对着光看时雾越亮。越接近 1 或 -1，亮部越集中、扩散得越少。多数情况下使用 0 到 1 之间的正值。")]
		public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.4f, -1.0f, 1.0f);
		[Tooltip("主光源照亮的雾有多亮，数值越大越亮。")]
		public ClampedFloatParameter scattering = new ClampedFloatParameter(0.15f, 0.0f, 1.0f);
		[Tooltip("为主光源照亮的雾染色的颜色倍率。")]
		public ColorParameter tint = new ColorParameter(Color.white, true, false, true);
		[Tooltip("启用后，雾会在每个光线步进点朝主光源再走一小段，从而对自身投影。正是它让云状雾有了明亮的边缘和暗沉的内核，而不是被均匀照亮，代价是明显的性能开销。")]
		public BoolParameter enableSelfShadowing = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("雾对自身的压暗程度。数值越大，被其他雾挡住的部分越暗。它与衰减距离分开设置，这样调整自阴影时不会改变整幅画面的明暗。")]
		public ClampedFloatParameter selfShadowingIntensity = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
		[Tooltip("为每个光线步进点朝主光源走多远来收集遮挡它的雾，单位为米。数值越大越能囊括整团雾、投出大片阴影；数值越小，自阴影越集中在附近的细节上。")]
		public MinFloatParameter selfShadowingDistance = new MinFloatParameter(16.0f, 0.01f);

		[Header("Lighting - Additional Lights")]
		[Tooltip("关闭后将不再计算额外光源对雾的贡献，多数情况下能提升性能。")]
		public BoolParameter enableAdditionalLightsContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("启用后，点光源和聚光灯也会让雾对自身投影，而不只是主光源。对于靠灯具而非太阳照明的场景，正是它塑造出烟的形体：不启用时每盏灯都会把它那团雾照透，烟看起来就是一块扁平的糊斑。每一盏照到雾的灯都会各自走一小段射线，因此灯光重叠得越多开销越大。")]
		public BoolParameter enableAdditionalLightsSelfShadowing = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
		[Tooltip("针对额外光源，雾对自身的压暗程度。数值越大，从灯的方向看被其他雾挡住的雾就越暗。")]
		public ClampedFloatParameter additionalLightsSelfShadowingIntensity = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
		[Tooltip("为每个光线步进点朝各个额外光源走多远来收集遮挡它的雾，单位为米。灯具只照亮周围的雾而非整个世界，因此这个距离通常比主光源的短得多。步进不会越过灯本身。")]
		public MinFloatParameter additionalLightsSelfShadowingDistance = new MinFloatParameter(6.0f, 0.01f);

		[Header("Performance & Quality")]
		[Tooltip("雾在被模糊并合成回画面之前所使用的渲染分辨率。四分之一分辨率的开销是二分之一的四分之一，是最省的一档，代价是雾与前方几何体交界处的边缘更软。以薄雾为主的效果在四分之一下依然成立；由细密噪声或狭窄光束雕刻出的雾则会看出损失。")]
		public VolumetricFogResolutionParameter resolution = new VolumetricFogResolutionParameter(VolumetricFogResolution.Half);
		[Tooltip("光线步进的步数。数值越大雾的质量越高，代价是性能。")]
		public ClampedIntParameter maxSteps = new ClampedIntParameter(128, 8, 256);
		[Tooltip("雾纹理被模糊的次数。数值越大，体积光（丁达尔光束）越柔和，代价是一定的性能。")]
		public ClampedIntParameter blurIterations = new ClampedIntParameter(2, 1, 4);
		[Tooltip("渲染体积雾所使用的 URP 渲染通道事件。")]
		public VolumetricFogRenderPassEventParameter renderPassEvent = new VolumetricFogRenderPassEventParameter(VolumetricFogRenderPass.DefaultVolumetricFogRenderPassEvent);

	#endregion

	#region Initialization Methods

#if !UNITY_6000_3_OR_NEWER

		public VolumetricFogVolumeComponent() : base()
		{
			displayName = "Volumetric Fog";
		}

#endif

	#endregion

	#region Volume Component Methods

		private void OnValidate()
		{
			maximumHeight.overrideState = baseHeight.overrideState;
			maximumHeight.value = Mathf.Max(baseHeight.value, maximumHeight.value);
			baseHeight.value = Mathf.Min(baseHeight.value, maximumHeight.value);
		}

	#endregion

	#region IPostProcessComponent Methods

#if !UNITY_2023_1_OR_NEWER

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <returns></returns>
		public bool IsTileCompatible()
		{
			return true;
		}

#endif

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <returns></returns>
		public bool IsActive()
		{
			return enabled.value && distance.value > 0.0f && groundHeight.value < maximumHeight.value && density.value > 0.0f;
		}

	#endregion
	}
}
