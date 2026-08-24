using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Collections;

#if UNITY_6000_0_OR_NEWER
using System;
using UnityEngine.Rendering.RenderGraphModule;
#endif

/// <summary>
/// The volumetric fog render pass.
/// </summary>
public sealed class VolumetricFogRenderPass : ScriptableRenderPass
{
	#region Definitions

	/// <summary>
	/// Downsampling factor for the camera depth texture that the volumetric fog will use to render
	/// the fog.
	/// </summary>
	private enum DownsampleFactor : byte
	{
		// TODO: Add quarter downsample factor.
		Half = 2,
	}

#if UNITY_6000_0_OR_NEWER

	/// <summary>
	/// The subpasses the volumetric fog render pass is made of.
	/// </summary>
	private enum PassStage : byte
	{
		DownsampleDepth,
		VolumetricFogRender,
		VolumetricFogBlur,
		VolumetricFogUpsampleComposition
	}

	/// <summary>
	/// Holds the data needed by the execution of the volumetric fog render pass subpasses.
	/// </summary>
	private class PassData
	{
		public PassStage stage;

		public TextureHandle source;
		public TextureHandle target;

		public Material material;
		public int materialPassIndex;
		public int materialAdditionalPassIndex;

		public TextureHandle downsampledCameraDepthTarget;
		public TextureHandle volumetricFogRenderTarget;
		public UniversalLightData lightData;
		public Vector3 cameraPositionWS;
	}

#endif

	#endregion

	#region Public Attributes

	public const RenderPassEvent DefaultRenderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
	public const VolumetricFogRenderPassEvent DefaultVolumetricFogRenderPassEvent = (VolumetricFogRenderPassEvent)DefaultRenderPassEvent;

	#endregion

	#region Private Attributes

	private const string DownsampledCameraDepthRTName = "_DownsampledCameraDepth";
	private const string VolumetricFogRenderRTName = "_VolumetricFog";
	private const string VolumetricFogBlurRTName = "_VolumetricFogBlur";
	private const string VolumetricFogUpsampleCompositionRTName = "_VolumetricFogUpsampleComposition";

	private static readonly int DownsampledCameraDepthTextureId = Shader.PropertyToID("_DownsampledCameraDepthTexture");
	private static readonly int VolumetricFogTextureId = Shader.PropertyToID("_VolumetricFogTexture");

	private static readonly int FrameCountId = Shader.PropertyToID("_FrameCount");
	private static readonly int CustomAdditionalLightsCountId = Shader.PropertyToID("_CustomAdditionalLightsCount");
	private static readonly int DistanceId = Shader.PropertyToID("_Distance");
	private static readonly int DistanceFadeStartId = Shader.PropertyToID("_DistanceFadeStart");
	private static readonly int DistanceFadeRcpRangeId = Shader.PropertyToID("_DistanceFadeRcpRange");
	private static readonly int DistanceFalloffId = Shader.PropertyToID("_DistanceFalloff");
	private static readonly int BaseHeightId = Shader.PropertyToID("_BaseHeight");
	private static readonly int MaximumHeightId = Shader.PropertyToID("_MaximumHeight");
	private static readonly int HeightFalloffId = Shader.PropertyToID("_HeightFalloff");
	private static readonly int GroundHeightId = Shader.PropertyToID("_GroundHeight");
	private static readonly int DensityId = Shader.PropertyToID("_Density");
	private static readonly int AbsortionId = Shader.PropertyToID("_Absortion");
#if UNITY_2023_1_OR_NEWER
	private static readonly int APVContributionWeigthId = Shader.PropertyToID("_APVContributionWeight");
#endif
	private static readonly int TintId = Shader.PropertyToID("_Tint");
	private static readonly int MaxStepsId = Shader.PropertyToID("_MaxSteps");
	private static readonly int PowderIntensityId = Shader.PropertyToID("_PowderIntensity");

	private static readonly int FogShapeNoiseTextureId = Shader.PropertyToID("_FogShapeNoiseTexture");
	private static readonly int FogDetailNoiseTextureId = Shader.PropertyToID("_FogDetailNoiseTexture");
	private static readonly int NoiseShapeOffsetId = Shader.PropertyToID("_NoiseShapeOffset");
	private static readonly int NoiseDetailOffsetId = Shader.PropertyToID("_NoiseDetailOffset");
	private static readonly int NoiseMacroOffsetId = Shader.PropertyToID("_NoiseMacroOffset");
	private static readonly int NoiseShapeTilingId = Shader.PropertyToID("_NoiseShapeTiling");
	private static readonly int NoiseDetailTilingId = Shader.PropertyToID("_NoiseDetailTiling");
	private static readonly int NoiseMacroTilingId = Shader.PropertyToID("_NoiseMacroTiling");
	private static readonly int NoiseIntensityId = Shader.PropertyToID("_NoiseIntensity");
	private static readonly int NoiseCoverageId = Shader.PropertyToID("_NoiseCoverage");
	private static readonly int NoiseDetailIntensityId = Shader.PropertyToID("_NoiseDetailIntensity");
	private static readonly int NoiseMacroIntensityId = Shader.PropertyToID("_NoiseMacroIntensity");
	private static readonly int NoiseFadeStartId = Shader.PropertyToID("_NoiseFadeStart");
	private static readonly int NoiseFadeRcpRangeId = Shader.PropertyToID("_NoiseFadeRcpRange");

	private static readonly int FogAreaWorldToLocalId = Shader.PropertyToID("_FogAreaWorldToLocal");
	private static readonly int FogAreaBorderId = Shader.PropertyToID("_FogAreaBorder");
	private static readonly int FogAreaDensityMultiplierId = Shader.PropertyToID("_FogAreaDensityMultiplier");

	private const string FogAreaKeyword = "_FOG_AREA_ENABLED";
	private const string FogNoiseKeyword = "_FOG_NOISE_ENABLED";
	private const string FogNoiseDetailKeyword = "_FOG_NOISE_DETAIL_ENABLED";
	private const string FogNoiseMacroKeyword = "_FOG_NOISE_MACRO_ENABLED";

	private const float DetailWindSpeedMultiplier = 2.0f;
	private const float MacroWindSpeedMultiplier = 0.5f;
	private const float NoiseFadeDisabledStart = 1e9f;

	// Distance layering is disabled by starting the buildup before the camera and completing it right away.
	private const float DistanceFadeDisabledStart = -1.0f;
	private const float DistanceFadeInstantRcpRange = 1e6f;

	private static readonly int AnisotropiesArrayId = Shader.PropertyToID("_Anisotropies");
	private static readonly int ScatteringsArrayId = Shader.PropertyToID("_Scatterings");
	private static readonly int RadiiSqArrayId = Shader.PropertyToID("_RadiiSq");

	private static int LightsParametersLength = UniversalRenderPipeline.maxVisibleAdditionalLights + 1;

	private static readonly float[] Anisotropies = new float[LightsParametersLength];
	private static readonly float[] Scatterings = new float[LightsParametersLength];
	private static readonly float[] RadiiSq = new float[LightsParametersLength];

	private int downsampleDepthPassIndex;
	private int volumetricFogRenderPassIndex;
	private int volumetricFogHorizontalBlurPassIndex;
	private int volumetricFogVerticalBlurPassIndex;
	private int volumetricFogUpsampleCompositionPassIndex;

	private Material downsampleDepthMaterial;
	private Material volumetricFogMaterial;

#if !UNITY_6000_4_OR_NEWER

	private RTHandle downsampledCameraDepthRTHandle;
	private RTHandle volumetricFogRenderRTHandle;
	private RTHandle volumetricFogBlurRTHandle;
	private RTHandle volumetricFogUpsampleCompositionRTHandle;

#endif

	private ProfilingSampler downsampleDepthProfilingSampler;

	#endregion

	#region Initialization Methods

	/// <summary>
	/// Constructor.
	/// </summary>
	/// <param name="downsampleDepthMaterial"></param>
	/// <param name="volumetricFogMaterial"></param>
	/// <param name="passEvent"></param>
	public VolumetricFogRenderPass(Material downsampleDepthMaterial, Material volumetricFogMaterial, RenderPassEvent passEvent) : base()
	{
		profilingSampler = new ProfilingSampler("Volumetric Fog");
		downsampleDepthProfilingSampler = new ProfilingSampler("Downsample Depth");
		renderPassEvent = passEvent;
#if UNITY_6000_0_OR_NEWER
		requiresIntermediateTexture = false;
#endif

		this.downsampleDepthMaterial = downsampleDepthMaterial;
		this.volumetricFogMaterial = volumetricFogMaterial;

		InitializePassesIndices();
	}

	/// <summary>
	/// Initializes the passes indices.
	/// </summary>
	private void InitializePassesIndices()
	{
		downsampleDepthPassIndex = downsampleDepthMaterial.FindPass("DownsampleDepth");
		volumetricFogRenderPassIndex = volumetricFogMaterial.FindPass("VolumetricFogRender");
		volumetricFogHorizontalBlurPassIndex = volumetricFogMaterial.FindPass("VolumetricFogHorizontalBlur");
		volumetricFogVerticalBlurPassIndex = volumetricFogMaterial.FindPass("VolumetricFogVerticalBlur");
		volumetricFogUpsampleCompositionPassIndex = volumetricFogMaterial.FindPass("VolumetricFogUpsampleComposition");
	}

	#endregion

	#region Scriptable Render Pass Methods

#if !UNITY_6000_4_OR_NEWER

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="cmd"></param>
	/// <param name="renderingData"></param>
#if UNITY_6000_0_OR_NEWER
	[Obsolete]
#endif
	public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
	{
		base.OnCameraSetup(cmd, ref renderingData);

		RenderTextureDescriptor cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
		cameraTargetDescriptor.depthBufferBits = (int)DepthBits.None;

		RenderTextureFormat originalColorFormat = cameraTargetDescriptor.colorFormat;
		Vector2Int originalResolution = new Vector2Int(cameraTargetDescriptor.width, cameraTargetDescriptor.height);

		cameraTargetDescriptor.width /= (int)DownsampleFactor.Half;
		cameraTargetDescriptor.height /= (int)DownsampleFactor.Half;
		cameraTargetDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
		ReAllocateIfNeeded(ref downsampledCameraDepthRTHandle, cameraTargetDescriptor, wrapMode: TextureWrapMode.Clamp, name: DownsampledCameraDepthRTName);

		cameraTargetDescriptor.colorFormat = RenderTextureFormat.ARGBHalf;
		ReAllocateIfNeeded(ref volumetricFogRenderRTHandle, cameraTargetDescriptor, wrapMode: TextureWrapMode.Clamp, name: VolumetricFogRenderRTName);
		ReAllocateIfNeeded(ref volumetricFogBlurRTHandle, cameraTargetDescriptor, wrapMode: TextureWrapMode.Clamp, name: VolumetricFogBlurRTName);

		cameraTargetDescriptor.width = originalResolution.x;
		cameraTargetDescriptor.height = originalResolution.y;
		cameraTargetDescriptor.colorFormat = originalColorFormat;
		ReAllocateIfNeeded(ref volumetricFogUpsampleCompositionRTHandle, cameraTargetDescriptor, wrapMode: TextureWrapMode.Clamp, name: VolumetricFogUpsampleCompositionRTName);
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="context"></param>
	/// <param name="renderingData"></param>
#if UNITY_6000_0_OR_NEWER
	[Obsolete]
#endif
	public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
	{
		CommandBuffer cmd = CommandBufferPool.Get();

		using (new ProfilingScope(cmd, downsampleDepthProfilingSampler))
		{
			Blitter.BlitCameraTexture(cmd, downsampledCameraDepthRTHandle, downsampledCameraDepthRTHandle, downsampleDepthMaterial, downsampleDepthPassIndex);
			volumetricFogMaterial.SetTexture(DownsampledCameraDepthTextureId, downsampledCameraDepthRTHandle);
		}

		using (new ProfilingScope(cmd, profilingSampler))
		{
			UpdateVolumetricFogMaterialParameters(volumetricFogMaterial, renderingData.lightData.mainLightIndex, renderingData.lightData.additionalLightsCount, renderingData.lightData.visibleLights, renderingData.cameraData.worldSpaceCameraPos);
			Blitter.BlitCameraTexture(cmd, volumetricFogRenderRTHandle, volumetricFogRenderRTHandle, volumetricFogMaterial, volumetricFogRenderPassIndex);

			int blurIterations = VolumeManager.instance.stack.GetComponent<VolumetricFogVolumeComponent>().blurIterations.value;

			for (int i = 0; i < blurIterations; ++i)
			{
				Blitter.BlitCameraTexture(cmd, volumetricFogRenderRTHandle, volumetricFogBlurRTHandle, volumetricFogMaterial, volumetricFogHorizontalBlurPassIndex);
				Blitter.BlitCameraTexture(cmd, volumetricFogBlurRTHandle, volumetricFogRenderRTHandle, volumetricFogMaterial, volumetricFogVerticalBlurPassIndex);
			}

			volumetricFogMaterial.SetTexture(VolumetricFogTextureId, volumetricFogRenderRTHandle);

			RTHandle cameraColorRt = renderingData.cameraData.renderer.cameraColorTargetHandle;
			Blitter.BlitCameraTexture(cmd, cameraColorRt, volumetricFogUpsampleCompositionRTHandle, volumetricFogMaterial, volumetricFogUpsampleCompositionPassIndex);
			Blitter.BlitCameraTexture(cmd, volumetricFogUpsampleCompositionRTHandle, cameraColorRt);
		}

		context.ExecuteCommandBuffer(cmd);

		cmd.Clear();

		CommandBufferPool.Release(cmd);
	}

#endif

#if UNITY_6000_0_OR_NEWER

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="renderGraph"></param>
	/// <param name="frameData"></param>
	public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
	{
		UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
		UniversalLightData lightData = frameData.Get<UniversalLightData>();
		UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

		CreateRenderGraphTextures(renderGraph, cameraData, out TextureHandle downsampledCameraDepthTarget, out TextureHandle volumetricFogRenderTarget, out TextureHandle volumetricFogBlurRenderTarget, out TextureHandle volumetricFogUpsampleCompositionTarget);

		using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Downsample Depth Pass", out PassData passData, downsampleDepthProfilingSampler))
		{
			passData.stage = PassStage.DownsampleDepth;
			passData.source = resourceData.cameraDepthTexture;
			passData.target = downsampledCameraDepthTarget;
			passData.material = downsampleDepthMaterial;
			passData.materialPassIndex = downsampleDepthPassIndex;

			builder.SetRenderAttachment(downsampledCameraDepthTarget, 0, AccessFlags.WriteAll);
			builder.UseTexture(resourceData.cameraDepthTexture);
			builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
		}

		using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Volumetric Fog Render Pass", out PassData passData, profilingSampler))
		{
			passData.stage = PassStage.VolumetricFogRender;
			passData.source = downsampledCameraDepthTarget;
			passData.target = volumetricFogRenderTarget;
			passData.material = volumetricFogMaterial;
			passData.materialPassIndex = volumetricFogRenderPassIndex;
			passData.downsampledCameraDepthTarget = downsampledCameraDepthTarget;
			passData.lightData = lightData;
			passData.cameraPositionWS = cameraData.worldSpaceCameraPos;

			builder.SetRenderAttachment(volumetricFogRenderTarget, 0, AccessFlags.WriteAll);
			builder.UseTexture(downsampledCameraDepthTarget);
			if (resourceData.mainShadowsTexture.IsValid())
				builder.UseTexture(resourceData.mainShadowsTexture);
			if (resourceData.additionalShadowsTexture.IsValid())
				builder.UseTexture(resourceData.additionalShadowsTexture);
			builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
		}

		using (IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass("Volumetric Fog Blur Pass", out PassData passData, profilingSampler))
		{
			passData.stage = PassStage.VolumetricFogBlur;
			passData.source = volumetricFogRenderTarget;
			passData.target = volumetricFogBlurRenderTarget;
			passData.material = volumetricFogMaterial;
			passData.materialPassIndex = volumetricFogHorizontalBlurPassIndex;
			passData.materialAdditionalPassIndex = volumetricFogVerticalBlurPassIndex;

			builder.UseTexture(volumetricFogRenderTarget, AccessFlags.ReadWrite);
			builder.UseTexture(volumetricFogBlurRenderTarget, AccessFlags.ReadWrite);
			builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecuteUnsafeBlurPass(data, context));
		}

		using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Volumetric Fog Upsample Composition Pass", out PassData passData, profilingSampler))
		{
			passData.stage = PassStage.VolumetricFogUpsampleComposition;
			passData.source = resourceData.cameraColor;
			passData.target = volumetricFogUpsampleCompositionTarget;
			passData.material = volumetricFogMaterial;
			passData.materialPassIndex = volumetricFogUpsampleCompositionPassIndex;
			passData.volumetricFogRenderTarget = volumetricFogRenderTarget;

			builder.SetRenderAttachment(volumetricFogUpsampleCompositionTarget, 0, AccessFlags.WriteAll);
			builder.UseTexture(resourceData.cameraDepthTexture);
			builder.UseTexture(downsampledCameraDepthTarget);
			builder.UseTexture(volumetricFogRenderTarget);
			builder.UseTexture(resourceData.cameraColor);
			builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
		}

		resourceData.cameraColor = volumetricFogUpsampleCompositionTarget;
	}

#endif

	#endregion

	#region Methods

	/// <summary>
	/// Updates the volumetric fog material parameters.
	/// </summary>
	/// <param name="volumetricFogMaterial"></param>
	/// <param name="mainLightIndex"></param>
	/// <param name="additionalLightsCount"></param>
	/// <param name="visibleLights"></param>
	/// <param name="cameraPositionWS"></param>
	private static void UpdateVolumetricFogMaterialParameters(Material volumetricFogMaterial, int mainLightIndex, int additionalLightsCount, NativeArray<VisibleLight> visibleLights, Vector3 cameraPositionWS)
	{
		VolumetricFogVolumeComponent fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFogVolumeComponent>();

		bool enableMainLightContribution = fogVolume.enableMainLightContribution.value && fogVolume.scattering.value > 0.0f && mainLightIndex > -1;
		bool enableAdditionalLightsContribution = fogVolume.enableAdditionalLightsContribution.value && additionalLightsCount > 0;

#if UNITY_2023_1_OR_NEWER
		bool enableAPVContribution = fogVolume.enableAPVContribution.value && fogVolume.APVContributionWeight.value > 0.0f;
		if (enableAPVContribution)
			volumetricFogMaterial.EnableKeyword("_APV_CONTRIBUTION_ENABLED");
		else
			volumetricFogMaterial.DisableKeyword("_APV_CONTRIBUTION_ENABLED");
#endif

		if (enableMainLightContribution)
			volumetricFogMaterial.DisableKeyword("_MAIN_LIGHT_CONTRIBUTION_DISABLED");
		else
			volumetricFogMaterial.EnableKeyword("_MAIN_LIGHT_CONTRIBUTION_DISABLED");

		if (enableAdditionalLightsContribution)
			volumetricFogMaterial.DisableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");
		else
			volumetricFogMaterial.EnableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");
		
		UpdateLightsParameters(volumetricFogMaterial, fogVolume, enableMainLightContribution, enableAdditionalLightsContribution, mainLightIndex, visibleLights);
		UpdateNoiseParameters(volumetricFogMaterial, fogVolume);
		UpdateAreaParameters(volumetricFogMaterial, cameraPositionWS);

		volumetricFogMaterial.SetInteger(FrameCountId, Time.renderedFrameCount % 64);
		volumetricFogMaterial.SetInteger(CustomAdditionalLightsCountId, additionalLightsCount);
		volumetricFogMaterial.SetFloat(DistanceId, fogVolume.distance.value);
		UpdateDistanceLayeringParameters(volumetricFogMaterial, fogVolume);
		volumetricFogMaterial.SetFloat(BaseHeightId, fogVolume.baseHeight.value);
		volumetricFogMaterial.SetFloat(MaximumHeightId, fogVolume.maximumHeight.value);
		volumetricFogMaterial.SetFloat(HeightFalloffId, fogVolume.heightFalloff.value);
		volumetricFogMaterial.SetFloat(GroundHeightId, (fogVolume.enableGround.overrideState && fogVolume.enableGround.value) ? fogVolume.groundHeight.value : float.MinValue);
		volumetricFogMaterial.SetFloat(DensityId, fogVolume.density.value);
		volumetricFogMaterial.SetFloat(AbsortionId, 1.0f / fogVolume.attenuationDistance.value);
#if UNITY_2023_1_OR_NEWER
		volumetricFogMaterial.SetFloat(APVContributionWeigthId, fogVolume.enableAPVContribution.value ? fogVolume.APVContributionWeight.value : 0.0f);
#endif
		volumetricFogMaterial.SetColor(TintId, fogVolume.tint.value);
		volumetricFogMaterial.SetInteger(MaxStepsId, fogVolume.maxSteps.value);
	}

	/// <summary>
	/// Updates the parameters of the fog area that confines the fog to a box of the world. When there is no area
	/// enabled in the scene, the fog fills the world as usual.
	/// </summary>
	/// <param name="volumetricFogMaterial"></param>
	/// <param name="cameraPositionWS"></param>
	private static void UpdateAreaParameters(Material volumetricFogMaterial, Vector3 cameraPositionWS)
	{
		VolumetricFogArea fogArea = VolumetricFogArea.GetClosestActiveArea(cameraPositionWS);

		CoreUtils.SetKeyword(volumetricFogMaterial, FogAreaKeyword, fogArea != null);

		if (fogArea == null)
			return;

		volumetricFogMaterial.SetMatrix(FogAreaWorldToLocalId, fogArea.WorldToAreaMatrix);
		volumetricFogMaterial.SetVector(FogAreaBorderId, fogArea.Border);
		volumetricFogMaterial.SetFloat(FogAreaDensityMultiplierId, fogArea.densityMultiplier);
	}

	/// <summary>
	/// Updates the parameters that build the fog density up with the distance to the camera.
	/// </summary>
	/// <param name="volumetricFogMaterial"></param>
	/// <param name="fogVolume"></param>
	private static void UpdateDistanceLayeringParameters(Material volumetricFogMaterial, VolumetricFogVolumeComponent fogVolume)
	{
		float startDistance = fogVolume.startDistance.value;
		float fullDensityDistance = Mathf.Max(fogVolume.fullDensityDistance.value, startDistance);
		bool enableDistanceLayering = startDistance > 0.0f || fullDensityDistance > startDistance;

		volumetricFogMaterial.SetFloat(DistanceFadeStartId, enableDistanceLayering ? startDistance : DistanceFadeDisabledStart);
		volumetricFogMaterial.SetFloat(DistanceFadeRcpRangeId, (enableDistanceLayering && fullDensityDistance > startDistance) ? 1.0f / (fullDensityDistance - startDistance) : DistanceFadeInstantRcpRange);
		volumetricFogMaterial.SetFloat(DistanceFalloffId, enableDistanceLayering ? fogVolume.distanceFalloff.value : 1.0f);
	}

	/// <summary>
	/// Updates the noise parameters that turn the fog into cloud alike fog.
	/// </summary>
	/// <param name="volumetricFogMaterial"></param>
	/// <param name="fogVolume"></param>
	private static void UpdateNoiseParameters(Material volumetricFogMaterial, VolumetricFogVolumeComponent fogVolume)
	{
		bool enableNoise = fogVolume.enableNoise.value && fogVolume.noiseIntensity.value > 0.0f;
		bool enableNoiseDetail = enableNoise && fogVolume.enableNoiseDetail.value && fogVolume.noiseDetailIntensity.value > 0.0f;
		bool enableNoiseMacro = enableNoise && fogVolume.enableNoiseMacro.value && fogVolume.noiseMacroIntensity.value > 0.0f;

		CoreUtils.SetKeyword(volumetricFogMaterial, FogNoiseKeyword, enableNoise);
		CoreUtils.SetKeyword(volumetricFogMaterial, FogNoiseDetailKeyword, enableNoiseDetail);
		CoreUtils.SetKeyword(volumetricFogMaterial, FogNoiseMacroKeyword, enableNoiseMacro);

		volumetricFogMaterial.SetFloat(PowderIntensityId, fogVolume.powderIntensity.value);

		if (!enableNoise)
			return;

		float shapeTiling = 1.0f / fogVolume.noiseTileSize.value;
		float detailTiling = 1.0f / fogVolume.noiseDetailTileSize.value;
		float macroTiling = 1.0f / fogVolume.noiseMacroTileSize.value;

		Vector3 windDirection = fogVolume.windDirection.value;
		windDirection = windDirection.sqrMagnitude > 0.0f ? windDirection.normalized : Vector3.zero;

		// The fog is blown towards the wind direction, so the noise has to be scrolled towards the opposite one.
		float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
		Vector3 windOffset = -windDirection * (fogVolume.windSpeed.value * time);

		volumetricFogMaterial.SetTexture(FogShapeNoiseTextureId, VolumetricFogNoise.ShapeNoiseTexture);
		volumetricFogMaterial.SetVector(NoiseShapeOffsetId, WrapNoiseOffset(windOffset * shapeTiling));
		volumetricFogMaterial.SetFloat(NoiseShapeTilingId, shapeTiling);
		volumetricFogMaterial.SetFloat(NoiseIntensityId, fogVolume.noiseIntensity.value);
		volumetricFogMaterial.SetFloat(NoiseCoverageId, fogVolume.noiseCoverage.value);

		if (enableNoiseDetail)
		{
			volumetricFogMaterial.SetTexture(FogDetailNoiseTextureId, VolumetricFogNoise.DetailNoiseTexture);
			volumetricFogMaterial.SetVector(NoiseDetailOffsetId, WrapNoiseOffset(windOffset * (detailTiling * DetailWindSpeedMultiplier)));
			volumetricFogMaterial.SetFloat(NoiseDetailTilingId, detailTiling);
			volumetricFogMaterial.SetFloat(NoiseDetailIntensityId, fogVolume.noiseDetailIntensity.value);
		}

		if (enableNoiseMacro)
		{
			// The banks of fog are much bigger than the clouds inside them, so they are drifted slower to sell their scale.
			volumetricFogMaterial.SetVector(NoiseMacroOffsetId, WrapNoiseOffset(windOffset * (macroTiling * MacroWindSpeedMultiplier)));
			volumetricFogMaterial.SetFloat(NoiseMacroTilingId, macroTiling);
			volumetricFogMaterial.SetFloat(NoiseMacroIntensityId, fogVolume.noiseMacroIntensity.value);
		}

		float fadeStart = fogVolume.noiseFadeDistance.value;
		bool enableFade = fadeStart > 0.0f && fadeStart < fogVolume.distance.value;

		volumetricFogMaterial.SetFloat(NoiseFadeStartId, enableFade ? fadeStart : NoiseFadeDisabledStart);
		volumetricFogMaterial.SetFloat(NoiseFadeRcpRangeId, enableFade ? 1.0f / (fogVolume.distance.value - fadeStart) : 0.0f);
	}

	/// <summary>
	/// Wraps the noise offset into the [0, 1) range. The noise textures tile seamlessly, so the result is identical
	/// to the unwrapped offset, but the floating point precision is kept as time goes by.
	/// </summary>
	/// <param name="offset"></param>
	/// <returns></returns>
	private static Vector3 WrapNoiseOffset(Vector3 offset)
	{
		return new Vector3(Mathf.Repeat(offset.x, 1.0f), Mathf.Repeat(offset.y, 1.0f), Mathf.Repeat(offset.z, 1.0f));
	}

	/// <summary>
	/// Updates the lights parameters from the material.
	/// </summary>
	/// <param name="volumetricFogMaterial"></param>
	/// <param name="fogVolume"></param>
	/// <param name="enableMainLightContribution"></param>
	/// <param name="enableAdditionalLightsContribution"></param>
	/// <param name="mainLightIndex"></param>
	/// <param name="visibleLights"></param>
	private static void UpdateLightsParameters(Material volumetricFogMaterial, VolumetricFogVolumeComponent fogVolume, bool enableMainLightContribution, bool enableAdditionalLightsContribution, int mainLightIndex, NativeArray<VisibleLight> visibleLights)
	{
		// TODO: Forward+ and deferred+ visibleLights.Length is 256. In forward, it is 257 so the main light is considered apart. In deferred it seems to not have any limit (seen 1.6k and beyond).
		// All rendering paths have maxVisibleAdditionalLights at 256.
		// For the time being, just assume that if additional lights contribution to fog is enabled, we are either using clustered rendering, or using forward or deferred with, at most, 257 visible lights.
		int lastIndex = Mathf.Clamp(visibleLights.Length, 0, LightsParametersLength);
		lastIndex = lastIndex - 1;

		if (enableMainLightContribution && lastIndex >= 0)
		{
			Anisotropies[lastIndex] = fogVolume.anisotropy.value;
			Scatterings[lastIndex] = fogVolume.scattering.value;
		}

		if (enableAdditionalLightsContribution)
		{
			int additionalLightIndex = 0;

			for (int i = 0; i <= lastIndex; ++i)
			{
				if (i == mainLightIndex)
					continue;

				float anisotropy = 0.0f;
				float scattering = 0.0f;
				float radius = 0.0f;

				if (visibleLights[i].light.TryGetComponent(out VolumetricAdditionalLight volumetricLight))
				{
					if (volumetricLight.gameObject.activeInHierarchy && volumetricLight.enabled)
					{
						anisotropy = volumetricLight.Anisotropy;
						scattering = volumetricLight.Scattering;
						radius = volumetricLight.Radius;
					}
				}

				Anisotropies[additionalLightIndex] = anisotropy;
				Scatterings[additionalLightIndex] = scattering;
				RadiiSq[additionalLightIndex++] = radius * radius;
			}
		}

		if (enableMainLightContribution || enableAdditionalLightsContribution)
		{
			volumetricFogMaterial.SetFloatArray(AnisotropiesArrayId, Anisotropies);
			volumetricFogMaterial.SetFloatArray(ScatteringsArrayId, Scatterings);
			volumetricFogMaterial.SetFloatArray(RadiiSqArrayId, RadiiSq);
		}
	}

#if UNITY_6000_0_OR_NEWER

	/// <summary>
	/// Creates and returns all the necessary render graph textures.
	/// </summary>
	/// <param name="renderGraph"></param>
	/// <param name="cameraData"></param>
	/// <param name="downsampledCameraDepthTarget"></param>
	/// <param name="volumetricFogRenderTarget"></param>
	/// <param name="volumetricFogBlurRenderTarget"></param>
	/// <param name="volumetricFogUpsampleCompositionTarget"></param>
	private void CreateRenderGraphTextures(RenderGraph renderGraph, UniversalCameraData cameraData, out TextureHandle downsampledCameraDepthTarget, out TextureHandle volumetricFogRenderTarget, out TextureHandle volumetricFogBlurRenderTarget, out TextureHandle volumetricFogUpsampleCompositionTarget)
	{
		RenderTextureDescriptor cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
		cameraTargetDescriptor.depthBufferBits = (int)DepthBits.None;

		RenderTextureFormat originalColorFormat = cameraTargetDescriptor.colorFormat;
		Vector2Int originalResolution = new Vector2Int(cameraTargetDescriptor.width, cameraTargetDescriptor.height);

		cameraTargetDescriptor.width /= (int)DownsampleFactor.Half;
		cameraTargetDescriptor.height /= (int)DownsampleFactor.Half;
		cameraTargetDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
		downsampledCameraDepthTarget = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraTargetDescriptor, DownsampledCameraDepthRTName, false);

		cameraTargetDescriptor.colorFormat = RenderTextureFormat.ARGBHalf;
		volumetricFogRenderTarget = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraTargetDescriptor, VolumetricFogRenderRTName, false);
		volumetricFogBlurRenderTarget = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraTargetDescriptor, VolumetricFogBlurRTName, false);

		cameraTargetDescriptor.width = originalResolution.x;
		cameraTargetDescriptor.height = originalResolution.y;
		cameraTargetDescriptor.colorFormat = originalColorFormat;
		volumetricFogUpsampleCompositionTarget = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraTargetDescriptor, VolumetricFogUpsampleCompositionRTName, false);
	}

	/// <summary>
	/// Executes the pass with the information from the pass data.
	/// </summary>
	/// <param name="passData"></param>
	/// <param name="context"></param>
	private static void ExecutePass(PassData passData, RasterGraphContext context)
	{
		PassStage stage = passData.stage;

		if (stage == PassStage.VolumetricFogRender)
		{
			passData.material.SetTexture(DownsampledCameraDepthTextureId, passData.downsampledCameraDepthTarget);
			UpdateVolumetricFogMaterialParameters(passData.material, passData.lightData.mainLightIndex, passData.lightData.additionalLightsCount, passData.lightData.visibleLights, passData.cameraPositionWS);
		}
		else if (stage == PassStage.VolumetricFogUpsampleComposition)
		{
			passData.material.SetTexture(VolumetricFogTextureId, passData.volumetricFogRenderTarget);
		}

		Blitter.BlitTexture(context.cmd, passData.source, Vector2.one, passData.material, passData.materialPassIndex);
	}

	/// <summary>
	/// Executes the unsafe pass that does up to multiple separable blurs to the volumetric fog.
	/// </summary>
	/// <param name="passData"></param>
	/// <param name="context"></param>
	private static void ExecuteUnsafeBlurPass(PassData passData, UnsafeGraphContext context)
	{
		CommandBuffer unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

		int blurIterations = VolumeManager.instance.stack.GetComponent<VolumetricFogVolumeComponent>().blurIterations.value;

		for (int i = 0; i < blurIterations; ++i)
		{
			Blitter.BlitCameraTexture(unsafeCmd, passData.source, passData.target, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, passData.material, passData.materialPassIndex);
			Blitter.BlitCameraTexture(unsafeCmd, passData.target, passData.source, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, passData.material, passData.materialAdditionalPassIndex);
		}
	}

#endif

#if !UNITY_6000_4_OR_NEWER

	/// <summary>
	/// Re-allocate fixed-size RTHandle if it is not allocated or doesn't match the descriptor.
	/// </summary>
	/// <param name="handle"></param>
	/// <param name="descriptor"></param>
	/// <param name="wrapMode"></param>
	/// <param name="name"></param>
	private void ReAllocateIfNeeded(ref RTHandle handle, in RenderTextureDescriptor descriptor, TextureWrapMode wrapMode, string name)
	{
#if UNITY_6000_0_OR_NEWER
		RenderingUtils.ReAllocateHandleIfNeeded(ref handle, descriptor, wrapMode: wrapMode, name: name);
#else
		RenderingUtils.ReAllocateIfNeeded(ref handle, descriptor, wrapMode: wrapMode, name: name);
#endif
	}

	/// <summary>
	/// Disposes the resources used by this pass.
	/// </summary>
	public void Dispose()
	{
		downsampledCameraDepthRTHandle?.Release();
		volumetricFogRenderRTHandle?.Release();
		volumetricFogBlurRTHandle?.Release();
		volumetricFogUpsampleCompositionRTHandle?.Release();
	}

#endif

	#endregion
}