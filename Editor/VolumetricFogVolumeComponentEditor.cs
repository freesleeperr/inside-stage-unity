using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace FreeSleeperr.InsideStage.Editor
{
	/// <summary>
	/// Custom editor for the volumetric fog volume component.
	/// </summary>
	[CustomEditor(typeof(VolumetricFogVolumeComponent))]
	public sealed class VolumetricFogVolumeComponentEditor : VolumeComponentEditor
	{
	#region Definitions

		/// <summary>
		/// Prefix of the editor prefs keys that keep each foldout group expanded or collapsed between sessions.
		/// </summary>
		private const string FoldoutKeyPrefix = "FreeSleeperr.InsideStage.VolumetricFog.Foldout.";

	#endregion

	#region Private Attributes

		private SerializedDataParameter enabled;

		private SerializedDataParameter density;
		private SerializedDataParameter areaGlobalDensity;
		private SerializedDataParameter distance;
		private SerializedDataParameter startDistance;
		private SerializedDataParameter fullDensityDistance;
		private SerializedDataParameter distanceFalloff;
		private SerializedDataParameter baseHeight;
		private SerializedDataParameter maximumHeight;
		private SerializedDataParameter heightFalloff;
		private SerializedDataParameter enableGround;
		private SerializedDataParameter groundHeight;

		private SerializedDataParameter enableNoise;
		private SerializedDataParameter noiseIntensity;
		private SerializedDataParameter noiseCoverage;
		private SerializedDataParameter noiseTileSize;
		private SerializedDataParameter windDirection;
		private SerializedDataParameter windSpeed;
		private SerializedDataParameter noiseFadeDistance;
		private SerializedDataParameter powderIntensity;
		private SerializedDataParameter enableNoiseMacro;
		private SerializedDataParameter noiseMacroTileSize;
		private SerializedDataParameter noiseMacroIntensity;
		private SerializedDataParameter enableNoiseDetail;
		private SerializedDataParameter noiseDetailTileSize;
		private SerializedDataParameter noiseDetailIntensity;

		private SerializedDataParameter attenuationDistance;
		private SerializedDataParameter enableAmbientContribution;
		private SerializedDataParameter ambientColor;
		private SerializedDataParameter ambientIntensity;
#if UNITY_2023_1_OR_NEWER
		private SerializedDataParameter enableAPVContribution;
		private SerializedDataParameter APVContributionWeight;
#endif

		private SerializedDataParameter enableMainLightContribution;
		private SerializedDataParameter anisotropy;
		private SerializedDataParameter scattering;
		private SerializedDataParameter tint;
		private SerializedDataParameter enableSelfShadowing;
		private SerializedDataParameter selfShadowingIntensity;
		private SerializedDataParameter selfShadowingDistance;

		private SerializedDataParameter enableAdditionalLightsContribution;
		private SerializedDataParameter enableAdditionalLightsSelfShadowing;
		private SerializedDataParameter additionalLightsSelfShadowingIntensity;
		private SerializedDataParameter additionalLightsSelfShadowingDistance;

		private SerializedDataParameter resolution;
		private SerializedDataParameter maxSteps;
		private SerializedDataParameter blurIterations;
		private SerializedDataParameter renderPassEvent;

	#endregion

	#region VolumeComponentEditor Methods

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		public override void OnEnable()
		{
			PropertyFetcher<VolumetricFogVolumeComponent> pf = new PropertyFetcher<VolumetricFogVolumeComponent>(serializedObject);

			enabled = Unpack(pf.Find(x => x.enabled));

			density = Unpack(pf.Find(x => x.density));
			areaGlobalDensity = Unpack(pf.Find(x => x.areaGlobalDensity));
			distance = Unpack(pf.Find(x => x.distance));
			startDistance = Unpack(pf.Find(x => x.startDistance));
			fullDensityDistance = Unpack(pf.Find(x => x.fullDensityDistance));
			distanceFalloff = Unpack(pf.Find(x => x.distanceFalloff));
			baseHeight = Unpack(pf.Find(x => x.baseHeight));
			maximumHeight = Unpack(pf.Find(x => x.maximumHeight));
			heightFalloff = Unpack(pf.Find(x => x.heightFalloff));
			enableGround = Unpack(pf.Find(x => x.enableGround));
			groundHeight = Unpack(pf.Find(x => x.groundHeight));

			enableNoise = Unpack(pf.Find(x => x.enableNoise));
			noiseIntensity = Unpack(pf.Find(x => x.noiseIntensity));
			noiseCoverage = Unpack(pf.Find(x => x.noiseCoverage));
			noiseTileSize = Unpack(pf.Find(x => x.noiseTileSize));
			windDirection = Unpack(pf.Find(x => x.windDirection));
			windSpeed = Unpack(pf.Find(x => x.windSpeed));
			noiseFadeDistance = Unpack(pf.Find(x => x.noiseFadeDistance));
			powderIntensity = Unpack(pf.Find(x => x.powderIntensity));
			enableNoiseMacro = Unpack(pf.Find(x => x.enableNoiseMacro));
			noiseMacroTileSize = Unpack(pf.Find(x => x.noiseMacroTileSize));
			noiseMacroIntensity = Unpack(pf.Find(x => x.noiseMacroIntensity));
			enableNoiseDetail = Unpack(pf.Find(x => x.enableNoiseDetail));
			noiseDetailTileSize = Unpack(pf.Find(x => x.noiseDetailTileSize));
			noiseDetailIntensity = Unpack(pf.Find(x => x.noiseDetailIntensity));

			attenuationDistance = Unpack(pf.Find(x => x.attenuationDistance));
			enableAmbientContribution = Unpack(pf.Find(x => x.enableAmbientContribution));
			ambientColor = Unpack(pf.Find(x => x.ambientColor));
			ambientIntensity = Unpack(pf.Find(x => x.ambientIntensity));
#if UNITY_2023_1_OR_NEWER
			enableAPVContribution = Unpack(pf.Find(x => x.enableAPVContribution));
			APVContributionWeight = Unpack(pf.Find(x => x.APVContributionWeight));
#endif

			enableMainLightContribution = Unpack(pf.Find(x => x.enableMainLightContribution));
			anisotropy = Unpack(pf.Find(x => x.anisotropy));
			scattering = Unpack(pf.Find(x => x.scattering));
			tint = Unpack(pf.Find(x => x.tint));
			enableSelfShadowing = Unpack(pf.Find(x => x.enableSelfShadowing));
			selfShadowingIntensity = Unpack(pf.Find(x => x.selfShadowingIntensity));
			selfShadowingDistance = Unpack(pf.Find(x => x.selfShadowingDistance));

			enableAdditionalLightsContribution = Unpack(pf.Find(x => x.enableAdditionalLightsContribution));
			enableAdditionalLightsSelfShadowing = Unpack(pf.Find(x => x.enableAdditionalLightsSelfShadowing));
			additionalLightsSelfShadowingIntensity = Unpack(pf.Find(x => x.additionalLightsSelfShadowingIntensity));
			additionalLightsSelfShadowingDistance = Unpack(pf.Find(x => x.additionalLightsSelfShadowingDistance));

			resolution = Unpack(pf.Find(x => x.resolution));
			maxSteps = Unpack(pf.Find(x => x.maxSteps));
			blurIterations = Unpack(pf.Find(x => x.blurIterations));
			renderPassEvent = Unpack(pf.Find(x => x.renderPassEvent));
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		public override void OnInspectorGUI()
		{
			PropertyField(enabled);

			if (!IsOn(enabled))
				return;

			DrawShapeGroup();
			DrawNoiseGroup();
			DrawLightingGroup();
			DrawMainLightGroup();
			DrawAdditionalLightsGroup();
			DrawPerformanceGroup();
		}

	#endregion

	#region Group Methods

		/// <summary>
		/// Draws the parameters that shape where the fog is and how thick it gets.
		/// </summary>
		private void DrawShapeGroup()
		{
			if (BeginGroup("Shape", "Shape"))
			{
				PropertyField(density);
				PropertyField(areaGlobalDensity);

				DrawSubHeader("Distance");
				PropertyField(distance);
				PropertyField(startDistance);
				PropertyField(fullDensityDistance);
				PropertyField(distanceFalloff);

				DrawSubHeader("Height");
				PropertyField(baseHeight);
				PropertyField(maximumHeight);
				PropertyField(heightFalloff);
				PropertyField(enableGround);
				if (IsOn(enableGround))
					PropertyField(groundHeight);
			}

			EndGroup();
		}

		/// <summary>
		/// Draws the parameters of the noise that turns the fog into clouds.
		/// </summary>
		private void DrawNoiseGroup()
		{
			if (BeginGroup("Noise", "Noise (Clouds)"))
			{
				PropertyField(enableNoise);

				if (IsOn(enableNoise))
				{
					PropertyField(noiseIntensity);
					PropertyField(noiseCoverage);
					PropertyField(noiseTileSize);
					PropertyField(windDirection);
					PropertyField(windSpeed);
					PropertyField(noiseFadeDistance);
					PropertyField(powderIntensity);

					DrawSubHeader("Macro");
					PropertyField(enableNoiseMacro);
					if (IsOn(enableNoiseMacro))
					{
						PropertyField(noiseMacroTileSize);
						PropertyField(noiseMacroIntensity);
					}

					DrawSubHeader("Detail");
					PropertyField(enableNoiseDetail);
					if (IsOn(enableNoiseDetail))
					{
						PropertyField(noiseDetailTileSize);
						PropertyField(noiseDetailIntensity);
					}
				}
			}

			EndGroup();
		}

		/// <summary>
		/// Draws the parameters of the lighting that does not come from any particular light.
		/// </summary>
		private void DrawLightingGroup()
		{
			if (BeginGroup("Lighting", "Lighting"))
			{
				PropertyField(attenuationDistance);

				PropertyField(enableAmbientContribution);
				if (IsOn(enableAmbientContribution))
				{
					PropertyField(ambientColor);
					PropertyField(ambientIntensity);
				}

#if UNITY_2023_1_OR_NEWER
				PropertyField(enableAPVContribution);
				if (IsOn(enableAPVContribution))
					PropertyField(APVContributionWeight);
#endif
			}

			EndGroup();
		}

		/// <summary>
		/// Draws the parameters of the main light contribution.
		/// </summary>
		private void DrawMainLightGroup()
		{
			if (BeginGroup("MainLight", "Main Light"))
			{
				PropertyField(enableMainLightContribution);

				if (IsOn(enableMainLightContribution))
				{
					PropertyField(anisotropy);
					PropertyField(scattering);
					PropertyField(tint);

					DrawSubHeader("Self Shadowing");
					PropertyField(enableSelfShadowing);
					if (IsOn(enableSelfShadowing))
					{
						PropertyField(selfShadowingIntensity);
						PropertyField(selfShadowingDistance);
					}
				}
			}

			EndGroup();
		}

		/// <summary>
		/// Draws the parameters of the additional lights contribution.
		/// </summary>
		private void DrawAdditionalLightsGroup()
		{
			if (BeginGroup("AdditionalLights", "Additional Lights"))
			{
				PropertyField(enableAdditionalLightsContribution);

				if (IsOn(enableAdditionalLightsContribution))
				{
					DrawSubHeader("Self Shadowing");
					PropertyField(enableAdditionalLightsSelfShadowing);
					if (IsOn(enableAdditionalLightsSelfShadowing))
					{
						PropertyField(additionalLightsSelfShadowingIntensity);
						PropertyField(additionalLightsSelfShadowingDistance);
					}
				}
			}

			EndGroup();
		}

		/// <summary>
		/// Draws the parameters that trade quality for performance.
		/// </summary>
		private void DrawPerformanceGroup()
		{
			if (BeginGroup("Performance", "Performance & Quality"))
			{
				PropertyField(resolution);
				PropertyField(maxSteps);
				PropertyField(blurIterations);
				PropertyField(renderPassEvent);
			}

			EndGroup();
		}

	#endregion

	#region Layout Methods

		/// <summary>
		/// Begins a foldout group whose expanded state is kept between sessions, and returns whether it is expanded.
		/// </summary>
		/// <param name="key"></param>
		/// <param name="label"></param>
		/// <returns></returns>
		private static bool BeginGroup(string key, string label)
		{
			string prefsKey = FoldoutKeyPrefix + key;
			bool wasExpanded = EditorPrefs.GetBool(prefsKey, true);

			EditorGUILayout.Space(2.0f);
			bool isExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(wasExpanded, label);

			if (isExpanded != wasExpanded)
				EditorPrefs.SetBool(prefsKey, isExpanded);

			return isExpanded;
		}

		/// <summary>
		/// Ends the foldout group that was begun last.
		/// </summary>
		private static void EndGroup()
		{
			EditorGUILayout.EndFoldoutHeaderGroup();
		}

		/// <summary>
		/// Draws the label that separates a block of related parameters inside a group.
		/// </summary>
		/// <param name="label"></param>
		private static void DrawSubHeader(string label)
		{
			EditorGUILayout.Space(2.0f);
			EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
		}

		/// <summary>
		/// Gets whether the given toggle parameter is both overriden and turned on.
		/// </summary>
		/// <param name="parameter"></param>
		/// <returns></returns>
		private static bool IsOn(SerializedDataParameter parameter)
		{
			return parameter.overrideState.boolValue && parameter.value.boolValue;
		}

	#endregion
	}
}
