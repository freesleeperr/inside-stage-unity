using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

	[Header("Distances")]
	[Tooltip("The maximum distance from the camera that the fog will be rendered up to.")]
	public ClampedFloatParameter distance = new ClampedFloatParameter(64.0f, 0.0f, 512.0f);
	[Tooltip("The distance from the camera at which the fog starts to appear. Everything closer than it stays completely clear, which keeps the foreground readable and separates it from the background.")]
	public MinFloatParameter startDistance = new MinFloatParameter(0.0f, 0.0f);
	[Tooltip("The distance from the camera at which the fog reaches the density specified in the volume. Between the start distance and this one, the fog builds up gradually. Values equal or lower than the start distance make the fog reach its full density right away.")]
	public MinFloatParameter fullDensityDistance = new MinFloatParameter(0.0f, 0.0f);
	[Tooltip("Shapes how the fog builds up between the start distance and the full density distance. Values lower than one make the fog build up quickly and then flatten out, while higher values keep the closest fog thinner and push most of the buildup towards the full density distance.")]
	public ClampedFloatParameter distanceFalloff = new ClampedFloatParameter(1.0f, 0.1f, 8.0f);
	[Tooltip("The world height at which the fog will have the density specified in the volume.")]
	public FloatParameter baseHeight = new FloatParameter(0.0f, true);
	[Tooltip("The world height at which the fog will have no density at all.")]
	public FloatParameter maximumHeight = new FloatParameter(50.0f, true);
	[Tooltip("Shapes how the fog thins out between the base height and the maximum height. Values lower than one keep the fog dense for most of the range and then fade it out quickly, while higher values concentrate the fog near the base height, which reads as a low lying layer of mist.")]
	public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(1.0f, 0.1f, 8.0f);

	[Header("Ground")]
	[Tooltip("When enabled, allows to define a world height. Below it, fog will have no density at all.")]
	public BoolParameter enableGround = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("Below this world height, fog will have no density at all.")]
	public FloatParameter groundHeight = new FloatParameter(0.0f);

	[Header("Lighting")]
	[Tooltip("How dense is the fog.")]
	public ClampedFloatParameter density = new ClampedFloatParameter(0.2f, 0.0f, 1.0f);
	[Tooltip("Value that defines how much the fog attenuates light as distance increases. Lesser values lead to a darker image.")]
	public MinFloatParameter attenuationDistance = new MinFloatParameter(128.0f, 0.05f);
#if UNITY_2023_1_OR_NEWER
	[Tooltip("When enabled, adaptive probe volumes (APV) will be sampled to contribute to fog.")]
	public BoolParameter enableAPVContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("A weight factor for the light coming from adaptive probe volumes (APV) when the probe volume contribution is enabled.")]
	public ClampedFloatParameter APVContributionWeight = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
#endif

	[Header("Noise (Clouds)")]
	[Tooltip("When enabled, the fog density is modulated with animated 3D noise, which gives it a cloudy, volumetric look.")]
	public BoolParameter enableNoise = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("How much the noise modulates the fog density. At zero the fog is completely uniform, while at one it is fully driven by the noise.")]
	public ClampedFloatParameter noiseIntensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
	[Tooltip("How much of the volume is covered by the clouds of fog. Lower values leave bigger gaps of clear air between them.")]
	public ClampedFloatParameter noiseCoverage = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
	[Tooltip("The world size, in meters, of one tile of the noise that defines the overall shape of the clouds of fog.")]
	public MinFloatParameter noiseTileSize = new MinFloatParameter(48.0f, 0.01f);
	[Tooltip("When enabled, a third and much lower frequency noise varies the coverage across the world, which gathers the fog into banks separated by clearings instead of spreading it evenly. It is what gives the fog its sense of depth when looking into the distance.")]
	public BoolParameter enableNoiseMacro = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("The world size, in meters, of one tile of the noise that gathers the fog into banks. It should be several times bigger than the shape tile size, otherwise the banks are too small to be noticed as such.")]
	public MinFloatParameter noiseMacroTileSize = new MinFloatParameter(240.0f, 0.01f);
	[Tooltip("How much the banks of fog vary the coverage. At zero the coverage is uniform, while at one it goes from completely clear in the middle of the clearings to twice the coverage in the middle of the banks.")]
	public ClampedFloatParameter noiseMacroIntensity = new ClampedFloatParameter(0.75f, 0.0f, 1.0f);
	[Tooltip("When enabled, a second and higher frequency noise erodes the edges of the clouds of fog, adding wispy details at the cost of some performance.")]
	public BoolParameter enableNoiseDetail = new BoolParameter(true, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("The world size, in meters, of one tile of the noise that erodes the edges of the clouds of fog.")]
	public MinFloatParameter noiseDetailTileSize = new MinFloatParameter(8.0f, 0.01f);
	[Tooltip("How much the detail noise erodes the edges of the clouds of fog.")]
	public ClampedFloatParameter noiseDetailIntensity = new ClampedFloatParameter(0.35f, 0.0f, 1.0f);
	[Tooltip("The direction towards which the clouds of fog are blown. It does not need to be normalized.")]
	public Vector3Parameter windDirection = new Vector3Parameter(new Vector3(1.0f, 0.0f, 0.25f));
	[Tooltip("How fast, in meters per second, the clouds of fog are blown by the wind.")]
	public MinFloatParameter windSpeed = new MinFloatParameter(1.5f, 0.0f);
	[Tooltip("The distance from the camera at which the noise starts fading back into uniform fog, which hides the aliasing caused by the raymarching steps becoming too long to resolve it. Zero disables the fade.")]
	public MinFloatParameter noiseFadeDistance = new MinFloatParameter(0.0f, 0.0f);
	[Tooltip("Darkens the light scattered by the thinnest parts of the fog, which makes the clouds of fog look fluffier and gives them more contrast.")]
	public ClampedFloatParameter powderIntensity = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

	[Header("Main Light")]
	[Tooltip("Disabling this will avoid computing the main light contribution to fog, which in most cases will lead to better performance.")]
	public BoolParameter enableMainLightContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("Higher positive values will make the fog affected by the main light to appear brighter when directly looking to it, while lower negative values will make the fog to appear brighter when looking away from it. The closer the value is closer to 1 or -1, the less the brightness will spread. Most times, positive values higher than 0 and lower than 1 should be used.")]
	public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.4f, -1.0f, 1.0f);
	[Tooltip("Higher values will make fog affected by the main light to appear brighter.")]
	public ClampedFloatParameter scattering = new ClampedFloatParameter(0.15f, 0.0f, 1.0f);
	[Tooltip("A multiplier color to tint the main light fog.")]
	public ColorParameter tint = new ColorParameter(Color.white, true, false, true);

	[Header("Additional Lights")]
	[Tooltip("Disabling this will avoid computing additional lights contribution to fog, which in most cases will lead to better performance.")]
	public BoolParameter enableAdditionalLightsContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);

	[Header("Performance & Quality")]
	[Tooltip("Raymarching steps. Greater values will increase the fog quality at the expense of performance.")]
	public ClampedIntParameter maxSteps = new ClampedIntParameter(128, 8, 256);
	[Tooltip("The number of times that the fog texture will be blurred. Higher values lead to softer volumetric god rays at the cost of some performance.")]
	public ClampedIntParameter blurIterations = new ClampedIntParameter(2, 1, 4);
	[Tooltip("Disabling this will completely remove any feature from the volumetric fog from being rendered at all.")]
	public BoolParameter enabled = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);

	[Header("Render Pass Event")]
	[Tooltip("The URP render pass event to render the volumetric fog.")]
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