using UnityEditor;
using UnityEditor.Rendering;

/// <summary>
/// Custom editor for the volumetric fog volume component.
/// </summary>
[CustomEditor(typeof(VolumetricFogVolumeComponent))]
public sealed class VolumetricFogVolumeComponentEditor : VolumeComponentEditor
{
	#region Private Attributes

	private SerializedDataParameter distance;
	private SerializedDataParameter startDistance;
	private SerializedDataParameter fullDensityDistance;
	private SerializedDataParameter distanceFalloff;
	private SerializedDataParameter baseHeight;
	private SerializedDataParameter maximumHeight;
	private SerializedDataParameter heightFalloff;

	private SerializedDataParameter enableGround;
	private SerializedDataParameter groundHeight;

	private SerializedDataParameter areaGlobalDensity;

	private SerializedDataParameter density;
	private SerializedDataParameter attenuationDistance;
	private SerializedDataParameter enableAmbientContribution;
	private SerializedDataParameter ambientColor;
	private SerializedDataParameter ambientIntensity;
#if UNITY_2023_1_OR_NEWER
	private SerializedDataParameter enableAPVContribution;
	private SerializedDataParameter APVContributionWeight;
#endif

	private SerializedDataParameter enableNoise;
	private SerializedDataParameter noiseIntensity;
	private SerializedDataParameter noiseCoverage;
	private SerializedDataParameter noiseTileSize;
	private SerializedDataParameter enableNoiseMacro;
	private SerializedDataParameter noiseMacroTileSize;
	private SerializedDataParameter noiseMacroIntensity;
	private SerializedDataParameter enableNoiseDetail;
	private SerializedDataParameter noiseDetailTileSize;
	private SerializedDataParameter noiseDetailIntensity;
	private SerializedDataParameter windDirection;
	private SerializedDataParameter windSpeed;
	private SerializedDataParameter noiseFadeDistance;
	private SerializedDataParameter powderIntensity;

	private SerializedDataParameter enableMainLightContribution;
	private SerializedDataParameter anisotropy;
	private SerializedDataParameter scattering;
	private SerializedDataParameter tint;
	private SerializedDataParameter enableSelfShadowing;
	private SerializedDataParameter selfShadowingIntensity;
	private SerializedDataParameter selfShadowingDistance;

	private SerializedDataParameter enableAdditionalLightsContribution;

	private SerializedDataParameter maxSteps;
	private SerializedDataParameter blurIterations;
	private SerializedDataParameter enabled;
	
	private SerializedDataParameter renderPassEvent;

	#endregion

	#region VolumeComponentEditor Methods

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	public override void OnEnable()
	{
		PropertyFetcher<VolumetricFogVolumeComponent> pf = new PropertyFetcher<VolumetricFogVolumeComponent>(serializedObject);

		distance = Unpack(pf.Find(x => x.distance));
		startDistance = Unpack(pf.Find(x => x.startDistance));
		fullDensityDistance = Unpack(pf.Find(x => x.fullDensityDistance));
		distanceFalloff = Unpack(pf.Find(x => x.distanceFalloff));
		baseHeight = Unpack(pf.Find(x => x.baseHeight));
		maximumHeight = Unpack(pf.Find(x => x.maximumHeight));
		heightFalloff = Unpack(pf.Find(x => x.heightFalloff));

		enableGround = Unpack(pf.Find(x => x.enableGround));
		groundHeight = Unpack(pf.Find(x => x.groundHeight));

		areaGlobalDensity = Unpack(pf.Find(x => x.areaGlobalDensity));

		density = Unpack(pf.Find(x => x.density));
		attenuationDistance = Unpack(pf.Find(x => x.attenuationDistance));
		enableAmbientContribution = Unpack(pf.Find(x => x.enableAmbientContribution));
		ambientColor = Unpack(pf.Find(x => x.ambientColor));
		ambientIntensity = Unpack(pf.Find(x => x.ambientIntensity));
#if UNITY_2023_1_OR_NEWER
		enableAPVContribution = Unpack(pf.Find(x => x.enableAPVContribution));
		APVContributionWeight = Unpack(pf.Find(x => x.APVContributionWeight));
#endif

		enableNoise = Unpack(pf.Find(x => x.enableNoise));
		noiseIntensity = Unpack(pf.Find(x => x.noiseIntensity));
		noiseCoverage = Unpack(pf.Find(x => x.noiseCoverage));
		noiseTileSize = Unpack(pf.Find(x => x.noiseTileSize));
		enableNoiseMacro = Unpack(pf.Find(x => x.enableNoiseMacro));
		noiseMacroTileSize = Unpack(pf.Find(x => x.noiseMacroTileSize));
		noiseMacroIntensity = Unpack(pf.Find(x => x.noiseMacroIntensity));
		enableNoiseDetail = Unpack(pf.Find(x => x.enableNoiseDetail));
		noiseDetailTileSize = Unpack(pf.Find(x => x.noiseDetailTileSize));
		noiseDetailIntensity = Unpack(pf.Find(x => x.noiseDetailIntensity));
		windDirection = Unpack(pf.Find(x => x.windDirection));
		windSpeed = Unpack(pf.Find(x => x.windSpeed));
		noiseFadeDistance = Unpack(pf.Find(x => x.noiseFadeDistance));
		powderIntensity = Unpack(pf.Find(x => x.powderIntensity));

		enableMainLightContribution = Unpack(pf.Find(x => x.enableMainLightContribution));
		anisotropy = Unpack(pf.Find(x => x.anisotropy));
		scattering = Unpack(pf.Find(x => x.scattering));
		tint = Unpack(pf.Find(x => x.tint));
		enableSelfShadowing = Unpack(pf.Find(x => x.enableSelfShadowing));
		selfShadowingIntensity = Unpack(pf.Find(x => x.selfShadowingIntensity));
		selfShadowingDistance = Unpack(pf.Find(x => x.selfShadowingDistance));

		enableAdditionalLightsContribution = Unpack(pf.Find(x => x.enableAdditionalLightsContribution));

		maxSteps = Unpack(pf.Find(x => x.maxSteps));
		blurIterations = Unpack(pf.Find(x => x.blurIterations));
		enabled = Unpack(pf.Find(x => x.enabled));
		
		renderPassEvent = Unpack(pf.Find(x => x.renderPassEvent));
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	public override void OnInspectorGUI()
	{
		bool isEnabled = enabled.overrideState.boolValue && enabled.value.boolValue;

		if (!isEnabled)
		{
			PropertyField(enabled);
			return;
		}

		bool enabledGround = enableGround.overrideState.boolValue && enableGround.value.boolValue;
		bool enabledNoise = enableNoise.overrideState.boolValue && enableNoise.value.boolValue;
		bool enabledNoiseMacro = enableNoiseMacro.overrideState.boolValue && enableNoiseMacro.value.boolValue;
		bool enabledNoiseDetail = enableNoiseDetail.overrideState.boolValue && enableNoiseDetail.value.boolValue;
		bool enabledMainLightContribution = enableMainLightContribution.overrideState.boolValue && enableMainLightContribution.value.boolValue;
		bool enabledAdditionalLightsContribution = enableAdditionalLightsContribution.overrideState.boolValue && enableAdditionalLightsContribution.value.boolValue;

		PropertyField(distance);
		PropertyField(startDistance);
		PropertyField(fullDensityDistance);
		PropertyField(distanceFalloff);
		PropertyField(baseHeight);
		PropertyField(maximumHeight);
		PropertyField(heightFalloff);

		PropertyField(enableGround);
		if (enabledGround)
			PropertyField(groundHeight);

		PropertyField(areaGlobalDensity);

		PropertyField(density);
		PropertyField(attenuationDistance);

		PropertyField(enableAmbientContribution);
		if (enableAmbientContribution.overrideState.boolValue && enableAmbientContribution.value.boolValue)
		{
			PropertyField(ambientColor);
			PropertyField(ambientIntensity);
		}
#if UNITY_2023_1_OR_NEWER
		bool enabledAPVContribution = enableAPVContribution.overrideState.boolValue && enableAPVContribution.value.boolValue;
		PropertyField(enableAPVContribution);
		if (enabledAPVContribution)
			PropertyField(APVContributionWeight);
#endif

		PropertyField(enableNoise);
		if (enabledNoise)
		{
			PropertyField(noiseIntensity);
			PropertyField(noiseCoverage);
			PropertyField(noiseTileSize);

			PropertyField(enableNoiseMacro);
			if (enabledNoiseMacro)
			{
				PropertyField(noiseMacroTileSize);
				PropertyField(noiseMacroIntensity);
			}

			PropertyField(enableNoiseDetail);
			if (enabledNoiseDetail)
			{
				PropertyField(noiseDetailTileSize);
				PropertyField(noiseDetailIntensity);
			}

			PropertyField(windDirection);
			PropertyField(windSpeed);
			PropertyField(noiseFadeDistance);
			PropertyField(powderIntensity);
		}

		PropertyField(enableMainLightContribution);
		if (enabledMainLightContribution)
		{
			PropertyField(anisotropy);
			PropertyField(scattering);
			PropertyField(tint);

			PropertyField(enableSelfShadowing);
			if (enableSelfShadowing.overrideState.boolValue && enableSelfShadowing.value.boolValue)
			{
				PropertyField(selfShadowingIntensity);
				PropertyField(selfShadowingDistance);
			}
		}

		PropertyField(enableAdditionalLightsContribution);

		PropertyField(maxSteps);
		PropertyField(blurIterations);
		PropertyField(enabled);
		
		PropertyField(renderPassEvent);
	}

	#endregion
}