using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the whole mood scene from scratch: the pipeline settings it needs, its materials, its geometry, its
/// lighting, the volumetric fog that fills it and the post processing that grades it. Everything is authored here
/// rather than by hand so that the look can be reasoned about, diffed and rebuilt after a change.
/// </summary>
public static class InsideSceneBuilder
{
	#region Private Attributes

	private const string RootFolder = "Assets/Inside";
	private const string SettingsFolder = RootFolder + "/Settings";
	private const string MaterialsFolder = RootFolder + "/Materials";
	private const string MeshesFolder = RootFolder + "/Meshes";
	private const string ScenePath = RootFolder + "/InsideMood.unity";

	private const string PipelineAssetPath = SettingsFolder + "/URP-Inside.asset";
	private const string RendererAssetPath = SettingsFolder + "/URP-Inside-Renderer.asset";
	private const string VolumeProfilePath = SettingsFolder + "/InsideVolumeProfile.asset";

	// The room runs along X and recedes along +Z, with the camera looking into it from -Z. Everything below is
	// placed in those terms.

	private static Material backdropMaterial;
	private static Material concreteMaterial;
	private static Material floorMaterial;
	private static Material darkMetalMaterial;
	private static Material silhouetteMaterial;
	private static Material bulbMaterial;

	private static Transform sceneRoot;
	private static Transform figure;

	#endregion

	#region Methods

	/// <summary>
	/// Builds the mood scene, replacing whatever was built by a previous run.
	/// </summary>
	[MenuItem("Tools/Inside/Build Mood Scene")]
	public static void BuildMoodScene()
	{
		EnsureFolders();
		CreateMaterials();

		CreatePipelineAssets();

		Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

		sceneRoot = new GameObject("Inside Mood").transform;

		BuildEnvironmentSettings();
		BuildGeometry();
		BuildLighting();
		TuneShadowCasters();
		BuildFogAreas();
		BuildOccluders();
		BuildVolume();
		BuildCamera();

		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene, ScenePath);

		// Opening a scene can unload whatever no longer has a reference holding it alive, so the pipeline asset is
		// picked up again from disk rather than kept across the switch.
		UniversalRenderPipelineAsset pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
		QualitySettings.renderPipeline = pipelineAsset;

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Debug.Log("Inside mood scene built at " + ScenePath + ". The '" + pipelineAsset.name + "' pipeline asset has been assigned to the active quality level.");
	}

	/// <summary>
	/// Creates the folders that the scene and its assets live in.
	/// </summary>
	private static void EnsureFolders()
	{
		if (!AssetDatabase.IsValidFolder(SettingsFolder))
			AssetDatabase.CreateFolder(RootFolder, "Settings");

		if (!AssetDatabase.IsValidFolder(MaterialsFolder))
			AssetDatabase.CreateFolder(RootFolder, "Materials");

		if (!AssetDatabase.IsValidFolder(MeshesFolder))
			AssetDatabase.CreateFolder(RootFolder, "Meshes");
	}

	#endregion

	#region Pipeline Methods

	/// <summary>
	/// Copies the pipeline currently in use and retunes the copy for the scene, so that its shadows are long and soft
	/// enough for the fog to carve shafts out of them without touching whatever the rest of the project renders with.
	/// </summary>
	private static void CreatePipelineAssets()
	{
		UniversalRenderPipelineAsset sourcePipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

		// A rerun would otherwise derive the settings from the ones it wrote the last time around, which compounds
		// every tweak instead of applying it once.
		if (sourcePipeline != null && AssetDatabase.GetAssetPath(sourcePipeline) == PipelineAssetPath)
			sourcePipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

		if (sourcePipeline == null)
		{
			Debug.LogError("The project is not rendering with URP, so the Inside pipeline settings cannot be derived from it.");

			return;
		}

		ScriptableRendererData sourceRenderer = GetRendererData(sourcePipeline, 0);

		// Copying the assets rather than creating them keeps every shader and texture reference that the pipeline
		// needs, which a freshly created instance does not have.
		CopyAssetIfMissing(AssetDatabase.GetAssetPath(sourceRenderer), RendererAssetPath);
		CopyAssetIfMissing(AssetDatabase.GetAssetPath(sourcePipeline), PipelineAssetPath);

		UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
		UniversalRenderPipelineAsset pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);

		TuneRenderer(rendererData);
		TunePipeline(pipelineAsset, rendererData);

		EditorUtility.SetDirty(rendererData);
		EditorUtility.SetDirty(pipelineAsset);
		AssetDatabase.SaveAssets();
	}

	/// <summary>
	/// Copies the asset at the given path to the given destination, unless something is already there. Deleting and
	/// copying over an asset that the pipeline is currently rendering with wedges the asset database, and there is
	/// nothing to gain from it either: everything this builder cares about is retuned in place right afterwards.
	/// </summary>
	/// <param name="sourcePath"></param>
	/// <param name="destinationPath"></param>
	private static void CopyAssetIfMissing(string sourcePath, string destinationPath)
	{
		if (AssetDatabase.LoadAssetAtPath<Object>(destinationPath) != null)
			return;

		AssetDatabase.CopyAsset(sourcePath, destinationPath);
		AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceUpdate);
	}

	/// <summary>
	/// Gets the renderer data at the given index of the given pipeline asset.
	/// </summary>
	/// <param name="pipelineAsset"></param>
	/// <param name="index"></param>
	/// <returns></returns>
	private static ScriptableRendererData GetRendererData(UniversalRenderPipelineAsset pipelineAsset, int index)
	{
		SerializedObject serializedPipeline = new SerializedObject(pipelineAsset);
		SerializedProperty rendererDataList = serializedPipeline.FindProperty("m_RendererDataList");

		return rendererDataList.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
	}

	/// <summary>
	/// Points the pipeline asset at the renderer built for the scene and retunes its shadows and its color pipeline.
	/// </summary>
	/// <param name="pipelineAsset"></param>
	/// <param name="rendererData"></param>
	private static void TunePipeline(UniversalRenderPipelineAsset pipelineAsset, ScriptableRendererData rendererData)
	{
		SerializedObject serialized = new SerializedObject(pipelineAsset);

		SerializedProperty rendererDataList = serialized.FindProperty("m_RendererDataList");
		rendererDataList.arraySize = 1;
		rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;

		// The fog reads the depth texture, the wet floor is graded in HDR, and MSAA is pointless once everything ends
		// up going through a fullscreen pass anyway.
		SetProperty(serialized, "m_RequireDepthTexture", true);
		SetProperty(serialized, "m_RequireOpaqueTexture", true);
		SetProperty(serialized, "m_SupportsHDR", true);
		SetProperty(serialized, "m_MSAA", 1);

		// Long, soft, high resolution shadows are what let a single directional light carve readable shafts through
		// the whole corridor instead of losing them a few meters in.
		SetProperty(serialized, "m_MainLightShadowsSupported", true);
		SetProperty(serialized, "m_MainLightShadowmapResolution", 4096);
		SetProperty(serialized, "m_ShadowDistance", 90.0f);
		SetProperty(serialized, "m_ShadowCascadeCount", 4);
		SetProperty(serialized, "m_SoftShadowsSupported", true);
		SetProperty(serialized, "m_ShadowDepthBias", 0.6f);
		SetProperty(serialized, "m_ShadowNormalBias", 0.6f);

		// The practicals have to light and shadow per pixel, as they are the ones making the shafts in the fog.
		SetProperty(serialized, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.PerPixel);
		SetProperty(serialized, "m_AdditionalLightsPerObjectLimit", 8);
		SetProperty(serialized, "m_AdditionalLightShadowsSupported", true);
		SetProperty(serialized, "m_AdditionalLightsShadowmapResolution", 2048);

		// A high precision grading LUT is what keeps a heavily graded, nearly monochrome image from banding before the
		// dithering even gets a chance to break it up.
		SetProperty(serialized, "m_ColorGradingMode", (int)ColorGradingMode.HighDynamicRange);
		SetProperty(serialized, "m_ColorGradingLutSize", 64);

		serialized.ApplyModifiedPropertiesWithoutUndo();
	}

	/// <summary>
	/// Retunes the ambient occlusion of the renderer and adds the dithering pass to it.
	/// </summary>
	/// <param name="rendererData"></param>
	private static void TuneRenderer(ScriptableRendererData rendererData)
	{
		// Screen space occlusion is turned off rather than tuned. It is derived from the depth buffer, which means it
		// lands wherever the geometry happens to make a crease and nowhere an artist asks for, and it cannot be moved.
		// The analytic occluders below replace it with shapes that are placed by hand and that follow whatever they
		// are parented to.
		foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
		{
			if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion")
				continue;

			feature.SetActive(false);
			EditorUtility.SetDirty(feature);
		}

		// The package's readme flags this one: with transparent receive shadows off, the volumetric shadows of the
		// additional lights are skipped, and every spot lights the fog as a ball instead of as a shaft.
		SerializedObject serializedRenderer = new SerializedObject(rendererData);
		SetProperty(serializedRenderer, "m_ShadowTransparentReceive", true);
		serializedRenderer.ApplyModifiedPropertiesWithoutUndo();

		AddFeature<AnalyticAmbientOcclusionRendererFeature>(rendererData, "AnalyticAmbientOcclusion");
		AddFeature<InsideDitherRendererFeature>(rendererData, "InsideDither");
	}

	/// <summary>
	/// Adds a render feature of the given type to the given renderer, unless it already carries one.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="rendererData"></param>
	/// <param name="name"></param>
	private static void AddFeature<T>(ScriptableRendererData rendererData, string name) where T : ScriptableRendererFeature
	{
		foreach (ScriptableRendererFeature existingFeature in rendererData.rendererFeatures)
		{
			if (existingFeature is T)
				return;
		}

		T feature = ScriptableObject.CreateInstance<T>();
		feature.name = name;

		AssetDatabase.AddObjectToAsset(feature, rendererData);
		AssetDatabase.SaveAssets();

		// The renderer keeps the local file id of every feature alongside it, and dropping one out of sync makes the
		// feature silently stop being executed.
		AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

		SerializedObject serialized = new SerializedObject(rendererData);
		SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
		SerializedProperty featureMap = serialized.FindProperty("m_RendererFeatureMap");

		features.arraySize++;
		features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

		featureMap.arraySize = features.arraySize;
		featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;

		serialized.ApplyModifiedPropertiesWithoutUndo();
	}

	/// <summary>
	/// Sets the property at the given path to the given value, doing nothing when the property is not there. Pipeline
	/// assets keep most of their settings private, and their names have moved around between Unity versions.
	/// </summary>
	/// <param name="serialized"></param>
	/// <param name="path"></param>
	/// <param name="value"></param>
	private static void SetProperty(SerializedObject serialized, string path, object value)
	{
		SerializedProperty property = serialized.FindProperty(path);

		if (property == null)
		{
			Debug.LogWarning("The property '" + path + "' was not found in '" + serialized.targetObject.name + "', so it has been left at whatever it was.");

			return;
		}

		if (value is bool boolValue)
			property.boolValue = boolValue;
		else if (value is int intValue)
			property.intValue = intValue;
		else if (value is float floatValue)
			property.floatValue = floatValue;
	}

	#endregion

	#region Material Methods

	/// <summary>
	/// Creates the handful of materials that the whole scene is built out of. The palette is deliberately tiny and
	/// almost colourless: depth in this look comes from how much haze sits between the camera and a surface, not from
	/// what the surface is made of, so all that really matters is how light each one is.
	/// </summary>
	private static void CreateMaterials()
	{
		// The far surfaces are the lightest, since the haze is meant to lift them towards the value of the light
		// itself. The near ones are darker so that they fall away into the black framing the shot.
		backdropMaterial = CreateLitMaterial("InsideBackdrop", new Color(0.46f, 0.465f, 0.46f), 0.0f, 0.05f);
		concreteMaterial = CreateLitMaterial("InsideConcrete", new Color(0.38f, 0.385f, 0.385f), 0.0f, 0.08f);
		floorMaterial = CreateLitMaterial("InsideFloor", new Color(0.42f, 0.425f, 0.425f), 0.0f, 0.10f);
		darkMetalMaterial = CreateLitMaterial("InsideDarkMetal", new Color(0.09f, 0.093f, 0.098f), 0.5f, 0.25f);
		silhouetteMaterial = CreateLitMaterial("InsideSilhouette", new Color(0.02f, 0.021f, 0.023f), 0.0f, 0.04f);
		bulbMaterial = CreateEmissiveMaterial("InsideBulb", new Color(1.0f, 0.97f, 0.90f), 14.0f);
	}

	/// <summary>
	/// Creates, or updates, a lit material with the given surface parameters.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="color"></param>
	/// <param name="metallic"></param>
	/// <param name="smoothness"></param>
	/// <returns></returns>
	private static Material CreateLitMaterial(string name, Color color, float metallic, float smoothness)
	{
		Material material = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");

		material.SetColor("_BaseColor", color);
		material.SetFloat("_Metallic", metallic);
		material.SetFloat("_Smoothness", smoothness);
		EditorUtility.SetDirty(material);

		return material;
	}

	/// <summary>
	/// Creates, or updates, the material of the light fixture itself, which is the one thing in frame that is its own
	/// source of light.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="color"></param>
	/// <param name="intensity"></param>
	/// <returns></returns>
	private static Material CreateEmissiveMaterial(string name, Color color, float intensity)
	{
		Material material = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");

		material.SetColor("_BaseColor", color);
		material.SetFloat("_Metallic", 0.0f);
		material.SetFloat("_Smoothness", 0.0f);
		material.SetColor("_EmissionColor", color * intensity);
		material.EnableKeyword("_EMISSION");
		material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
		EditorUtility.SetDirty(material);

		return material;
	}

	/// <summary>
	/// Loads the material with the given name, creating it with the given shader when it is not there yet.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="shaderName"></param>
	/// <returns></returns>
	private static Material LoadOrCreateMaterial(string name, string shaderName)
	{
		string path = MaterialsFolder + "/" + name + ".mat";
		Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

		if (material == null)
		{
			material = new Material(Shader.Find(shaderName));
			AssetDatabase.CreateAsset(material, path);
		}

		return material;
	}

	#endregion

	#region Scene Building Methods

	/// <summary>
	/// Sets the ambient and the background of the scene. There is no sky and no sun: the room is lit by one fixture on
	/// the ceiling and by a broad, flat ambient, which is what keeps the shadows open and free of any hard edge.
	/// </summary>
	private static void BuildEnvironmentSettings()
	{
		RenderSettings.skybox = null;

		// Three bands rather than one flat value. The haze overhead is the brightest thing in the room, so surfaces
		// facing up are lifted towards it while the ones facing down fall away, which is the whole reason a room with
		// no sky still reads as being lit from above.
		RenderSettings.ambientMode = AmbientMode.Trilight;
		RenderSettings.ambientSkyColor = new Color(0.54f, 0.59f, 0.67f);
		RenderSettings.ambientEquatorColor = new Color(0.31f, 0.35f, 0.42f);
		RenderSettings.ambientGroundColor = new Color(0.14f, 0.155f, 0.185f);
		RenderSettings.ambientIntensity = 1.0f;

		RenderSettings.fog = false;
		RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
		RenderSettings.customReflection = null;
		RenderSettings.reflectionIntensity = 0.35f;

		Lightmapping.realtimeGI = false;
		Lightmapping.bakedGI = false;
	}

	/// <summary>
	/// Builds everything solid in the scene. It is laid out in three bands: a light backdrop far away, a mid ground
	/// holding the fixture and the figure, and a foreground of shapes that never catch any light at all.
	/// </summary>
	private static void BuildGeometry()
	{
		Transform environment = CreateGroup("Environment");

		BuildRoom(environment);
		BuildMidGround(environment);
		BuildForeground(environment);
	}

	/// <summary>
	/// Builds the box that the whole shot happens inside of.
	/// </summary>
	/// <param name="parent"></param>
	private static void BuildRoom(Transform parent)
	{
		Transform room = CreateGroup("Room", parent);

		// Matte, and nothing else. A floor that reflects turns every silhouette into a second, upside down copy of
		// itself, which is exactly the sort of detail this look does not want.
		CreateBox("Floor", new Vector3(0.0f, -0.5f, 8.0f), new Vector3(48.0f, 1.0f, 52.0f), floorMaterial, room);

		// The backdrop is the lightest thing in frame. Everything else is read against it.
		CreateBox("Backdrop", new Vector3(0.0f, 5.0f, 25.0f), new Vector3(48.0f, 12.0f, 1.0f), backdropMaterial, room);

		CreateBox("Wall Left", new Vector3(-19.0f, 5.0f, 8.0f), new Vector3(1.0f, 12.0f, 52.0f), concreteMaterial, room);
		CreateBox("Wall Right", new Vector3(19.0f, 5.0f, 8.0f), new Vector3(1.0f, 12.0f, 52.0f), concreteMaterial, room);

		// The ceiling is solid. There are no gaps for light to come through, because there is no shaft to be made.
		CreateBox("Ceiling", new Vector3(0.0f, 7.0f, 8.0f), new Vector3(48.0f, 1.0f, 52.0f), concreteMaterial, room);
	}

	/// <summary>
	/// Builds what stands inside the lit part of the room, including the fixture that lights it and the figure the
	/// frame is composed around.
	/// </summary>
	/// <param name="parent"></param>
	private static void BuildMidGround(Transform parent)
	{
		Transform midGround = CreateGroup("Mid Ground", parent);

		BuildLampFixture(midGround);

		// A duct under the ceiling, which gives the top of the frame something to read as structure once the haze has
		// taken the contrast out of it.
		CreatePipe("Duct", new Vector3(0.0f, 6.05f, 20.0f), new Vector3(0.0f, 0.0f, 90.0f), 0.42f, 36.0f, concreteMaterial, midGround);
		CreatePipe("Pipe Wall", new Vector3(-12.5f, 3.6f, 24.0f), new Vector3(0.0f, 0.0f, 90.0f), 0.16f, 11.0f, concreteMaterial, midGround);

		BuildLadder(midGround);

		// The figure the whole frame is composed around, standing just inside the pool of light. It is the darkest
		// thing in the room by a long way: everything this look does rests on it staying a flat cutout.
		GameObject figureGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
		figureGo.name = "Figure";
		figureGo.transform.SetParent(midGround, false);
		figureGo.transform.position = new Vector3(1.3f, 0.80f, 6.5f);
		figureGo.transform.localScale = new Vector3(0.56f, 0.86f, 0.56f);
		figureGo.GetComponent<MeshRenderer>().sharedMaterial = silhouetteMaterial;
		Object.DestroyImmediate(figureGo.GetComponent<Collider>());

		figure = figureGo.transform;
	}

	/// <summary>
	/// Builds the shapes that sit between the camera and the room. They are far enough from the fixture that nothing
	/// reaches them, so they read as flat black cutouts, and they are what frames the shot and gives it its depth.
	/// </summary>
	/// <param name="parent"></param>
	private static void BuildForeground(Transform parent)
	{
		Transform foreground = CreateGroup("Foreground", parent);

		// The ceiling drops in the near field and closes the top of the shot in. It is kept to a band rather than a
		// mass: the room has to stay open, because the haze filling it is the subject.
		GameObject slab = CreateBox("Ceiling Slab", new Vector3(0.0f, 6.7f, -4.0f), new Vector3(52.0f, 1.2f, 14.0f), silhouetteMaterial, foreground);
		slab.transform.rotation = Quaternion.Euler(-4.0f, 0.0f, 0.0f);

		CreatePipe("Pipe Near", new Vector3(0.0f, 5.55f, -6.0f), new Vector3(0.0f, 0.0f, 90.0f), 0.22f, 52.0f, silhouetteMaterial, foreground);

		// The lip of floor the camera looks over, which closes the very bottom of the frame.
		// The two edges. They are all that is left of the black framing: enough to hold the eye in the middle of the
		// frame, not so much that the room stops reading as a room.
		CreateBox("Edge Left", new Vector3(-13.5f, 2.0f, 0.0f), new Vector3(5.0f, 8.0f, 5.0f), silhouetteMaterial, foreground);
		CreateBox("Edge Right", new Vector3(12.5f, 2.5f, -1.0f), new Vector3(5.0f, 9.0f, 4.0f), silhouetteMaterial, foreground);
	}

	/// <summary>
	/// Builds the fixture the room is lit by. It is the one thing in frame that is its own source of light, and the
	/// only object here whose exact silhouette matters, so it gets a shade of its own rather than a box standing in
	/// for one: the cone it hangs is read directly off the cone of the shade.
	/// </summary>
	/// <param name="parent"></param>
	private static void BuildLampFixture(Transform parent)
	{
		Transform lamp = CreateGroup("Lamp", parent);

		// The stub on the ceiling, the arm reaching out of it and the stem the shade hangs off.
		CreateBox("Lamp Mount", new Vector3(3.75f, 6.32f, 13.0f), new Vector3(1.0f, 0.26f, 0.32f), darkMetalMaterial, lamp);
		CreatePipe("Lamp Arm", new Vector3(3.05f, 6.26f, 13.0f), new Vector3(0.0f, 0.0f, 90.0f), 0.06f, 1.5f, darkMetalMaterial, lamp);
		CreatePipe("Lamp Stem", new Vector3(2.35f, 6.02f, 13.0f), Vector3.zero, 0.045f, 0.6f, darkMetalMaterial, lamp);

		CreateCone("Lamp Shade", new Vector3(2.35f, 5.58f, 13.0f), 0.15f, 0.70f, 0.52f, darkMetalMaterial, lamp);

		// The bulb, sitting just inside the rim so that it reads as a hot point rather than as a lit surface.
		GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		bulb.name = "Lamp Bulb";
		bulb.transform.SetParent(lamp, false);
		bulb.transform.position = new Vector3(2.35f, 5.33f, 13.0f);
		bulb.transform.localScale = new Vector3(0.30f, 0.30f, 0.30f);
		bulb.GetComponent<MeshRenderer>().sharedMaterial = bulbMaterial;
		bulb.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
		Object.DestroyImmediate(bulb.GetComponent<Collider>());
	}

	/// <summary>
	/// Builds the ladder leaning against the far wall. It is put together upright around the origin and then tilted
	/// as a whole, because everything below is placed in world space and would not follow a parent that was already
	/// rotated when they were created.
	/// </summary>
	/// <param name="parent"></param>
	private static void BuildLadder(Transform parent)
	{
		Transform ladder = CreateGroup("Ladder", parent);

		CreateBox("Rail Left", new Vector3(-0.55f, 3.1f, 0.0f), new Vector3(0.10f, 6.2f, 0.10f), darkMetalMaterial, ladder);
		CreateBox("Rail Right", new Vector3(0.55f, 3.1f, 0.0f), new Vector3(0.10f, 6.2f, 0.10f), darkMetalMaterial, ladder);

		for (int i = 0; i < 10; ++i)
			CreateBox("Rung " + i, new Vector3(0.0f, 0.55f + i * 0.6f, 0.0f), new Vector3(1.2f, 0.07f, 0.07f), darkMetalMaterial, ladder);

		ladder.SetPositionAndRotation(new Vector3(7.6f, 0.0f, 22.4f), Quaternion.Euler(-7.0f, 0.0f, 5.0f));
	}

	/// <summary>
	/// Builds the lighting of the scene. There is no sun and no shaft: one fixture on the ceiling, a broad bounce that
	/// stands in for the light it throws back off the floor, and a soft wash on the backdrop.
	/// </summary>
	private static void BuildLighting()
	{
		Transform lights = CreateGroup("Lighting");

		// The hero, and the only real source in the room: the bulb under the shade, throwing a cone straight down. It
		// is the one light here allowed to shadow, and the one carrying enough scattering to draw its own beam through
		// the haze. Everything else below only fills in what it leaves dark.
		CreateSpotLight("Lamp Key", new Vector3(2.35f, 5.30f, 13.0f), Quaternion.Euler(90.0f, 0.0f, 0.0f),
						new Color(1.0f, 0.96f, 0.88f), 210.0f, 34.0f, 78.0f, 2.4f, 0.35f, true, lights);

		// The spill the shade lets past its rim, thrown back up at the ceiling. It is what keeps the fixture from
		// sitting in a black hole of its own making.
		CreateSpotLight("Lamp Spill", new Vector3(2.35f, 5.85f, 13.0f), Quaternion.Euler(-90.0f, 0.0f, 0.0f),
						new Color(1.0f, 0.97f, 0.92f), 12.0f, 7.0f, 150.0f, 0.2f, 0.0f, false, lights);

		// A wide, dim wash pointed at the backdrop, which is what gives it its even, almost flat grey.
		CreateSpotLight("Backdrop Wash", new Vector3(0.0f, 5.0f, 15.0f), Quaternion.Euler(20.0f, 0.0f, 0.0f),
						new Color(0.94f, 0.96f, 1.0f), 95.0f, 40.0f, 150.0f, 0.25f, 0.0f, false, lights);

		// The left half of the room has nothing in it and no source of its own, and left alone it falls away into the
		// same black as the foreground, which costs the frame the one thing it is built on: a wall of even grey for
		// the silhouettes to be read against.
		CreateSpotLight("Room Fill", new Vector3(-8.0f, 4.0f, 4.0f), Quaternion.Euler(8.0f, 0.0f, 0.0f),
						new Color(0.90f, 0.93f, 1.0f), 90.0f, 42.0f, 150.0f, 0.0f, 0.0f, false, lights);

		// A broad wash straight down over the middle of the room. It is aimed to stop short of the near floor, which
		// is what leaves the bottom of the frame dark without a single piece of geometry being put there to block it:
		// the foreground of this look is an absence of light, not an object.
		CreateSpotLight("Floor Wash", new Vector3(1.0f, 6.5f, 13.0f), Quaternion.Euler(90.0f, 0.0f, 0.0f),
						new Color(0.93f, 0.95f, 1.0f), 125.0f, 34.0f, 128.0f, 0.0f, 0.0f, false, lights);

		CreateSpotLight("Floor Wash Near", new Vector3(0.0f, 6.5f, 4.0f), Quaternion.Euler(90.0f, 0.0f, 0.0f),
						new Color(0.93f, 0.95f, 1.0f), 100.0f, 30.0f, 130.0f, 0.0f, 0.0f, false, lights);

		// The bounce off the floor, aimed back up. Without it the undersides of everything go flat and dead, which is
		// the one thing that gives away that there is no global illumination at play.
		CreateSpotLight("Floor Bounce", new Vector3(1.5f, 0.4f, 12.0f), Quaternion.Euler(-90.0f, 0.0f, 0.0f),
						new Color(0.88f, 0.90f, 0.94f), 75.0f, 32.0f, 150.0f, 0.0f, 0.0f, false, lights);

		CreateKeyLight(lights);
	}

	/// <summary>
	/// Creates the directional fill. It is not the key any more and it is not meant to be seen as a source: it rakes
	/// across the room at low strength purely so that the flat faces of the props separate from one another. Turned
	/// up far enough to read as a sun it would give the room an outside, and this room does not have one.
	/// </summary>
	/// <param name="parent"></param>
	private static void CreateKeyLight(Transform parent)
	{
		GameObject go = new GameObject("Key Light");
		go.transform.SetParent(parent, false);
		go.transform.rotation = Quaternion.Euler(30.0f, -75.0f, 0.0f);

		Light light = go.AddComponent<Light>();
		light.type = LightType.Directional;
		light.color = new Color(0.86f, 0.91f, 0.98f);
		light.intensity = 0.9f;

		light.shadows = LightShadows.Soft;
		light.shadowStrength = 0.35f;
		light.shadowBias = 0.06f;
		light.shadowNormalBias = 0.5f;
		light.shadowNearPlane = 0.2f;
	}

	/// <summary>
	/// Decides what is allowed to cast a shadow. A directional light in a closed box would be stopped by the ceiling
	/// and by the slabs hanging over the camera before it ever reached the floor, so the shell of the room and the
	/// foreground are taken out of the shadow pass entirely. The foreground loses nothing by it: it is black in every
	/// frame regardless, and it is there to frame the shot rather than to shade it.
	/// </summary>
	private static void TuneShadowCasters()
	{
		string[] paths =
		{
			"Environment/Room/Backdrop",
			"Environment/Room/Wall Left",
			"Environment/Room/Wall Right",
			"Environment/Room/Ceiling",
			"Environment/Foreground/Ceiling Slab",
			"Environment/Foreground/Pipe Near",
			"Environment/Foreground/Edge Left",
			"Environment/Foreground/Edge Right",
			"Environment/Mid Ground/Lamp/Lamp Shade",
			"Environment/Mid Ground/Lamp/Lamp Mount",
			"Environment/Mid Ground/Lamp/Lamp Arm",
			"Environment/Mid Ground/Lamp/Lamp Stem"
		};

		foreach (string path in paths)
		{
			Transform transform = sceneRoot.Find(path);

			if (transform == null)
				continue;

			MeshRenderer renderer = transform.GetComponent<MeshRenderer>();

			if (renderer != null)
				renderer.shadowCastingMode = ShadowCastingMode.Off;
		}
	}

	/// <summary>
	/// Creates a spot light, optionally contributing to the volumetric fog when its scattering is above zero.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="position"></param>
	/// <param name="rotation"></param>
	/// <param name="color"></param>
	/// <param name="intensity"></param>
	/// <param name="range"></param>
	/// <param name="spotAngle"></param>
	/// <param name="scattering"></param>
	/// <param name="anisotropy"></param>
	/// <param name="castShadows"></param>
	/// <param name="parent"></param>
	private static void CreateSpotLight(string name, Vector3 position, Quaternion rotation, Color color, float intensity, float range, float spotAngle, float scattering, float anisotropy, bool castShadows, Transform parent)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(parent, false);
		go.transform.SetPositionAndRotation(position, rotation);

		Light light = go.AddComponent<Light>();
		light.type = LightType.Spot;
		light.color = color;
		light.intensity = intensity;
		light.range = range;
		light.spotAngle = spotAngle;
		light.innerSpotAngle = spotAngle * 0.2f;

		// Only the fixture shadows. The fills exist to lift what it leaves dark, and a fill that shadows stops being
		// a fill and starts being a second source, which is exactly what a room lit by one lamp cannot have.
		light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
		light.shadowStrength = 0.85f;
		light.shadowBias = 0.04f;
		light.shadowNormalBias = 0.35f;
		light.shadowNearPlane = 0.15f;

		if (scattering <= 0.0f)
			return;

		VolumetricAdditionalLight volumetricLight = go.AddComponent<VolumetricAdditionalLight>();
		volumetricLight.Scattering = scattering;
		volumetricLight.Anisotropy = anisotropy;
		volumetricLight.Radius = 0.4f;
	}

	/// <summary>
	/// Places the boxes of fog. Rather than gathering the fog into shafts, this look wants one even body of haze
	/// filling the room, so that the further away a surface is the more washed out it gets. That gradient is the
	/// entire depth cue of the shot.
	/// </summary>
	private static void BuildFogAreas()
	{
		Transform areas = CreateGroup("Fog Areas");

		// Tinted slightly towards blue, which is where the cold of the image comes from. Grading it in afterwards
		// would tint the black foreground along with it, and the foreground has to stay neutral.
		CreateFogArea("Room Haze", new Vector3(0.0f, 3.5f, 10.0f), new Vector3(44.0f, 9.0f, 48.0f),
					  0.15f, 0.25f, 1.0f, new Color(0.92f, 0.96f, 1.0f), areas);

		// A shallow layer over the floor, which lifts the base of everything standing on it and keeps the floor from
		// meeting the walls in a hard line. It is kept thin: any denser and it washes the lit floor out to a flat
		// white, which costs the shadows the key light is there to draw.
		CreateFogArea("Floor Haze", new Vector3(0.0f, 0.9f, 12.0f), new Vector3(44.0f, 2.8f, 48.0f),
					  0.15f, 0.65f, 0.45f, new Color(1.0f, 1.0f, 1.0f), areas);

		// A pocket of it under the lamp, and nowhere else. The cone reads because the air it crosses is thicker than
		// the air around it, which is a great deal easier to control than raising the density of the whole room until
		// the cone shows up and then finding the backdrop has gone white.
		CreateFogArea("Lamp Pocket", new Vector3(2.35f, 3.0f, 13.0f), new Vector3(10.0f, 6.0f, 10.0f),
					  0.60f, 0.50f, 1.7f, new Color(1.0f, 0.99f, 0.96f), areas);
	}

	/// <summary>
	/// Places the shade by hand. None of this is derived from the geometry: every shape is here because the image
	/// wanted to be darker at that spot, either to sit an object down onto the floor or to close a corner of the frame
	/// so that the eye stays where it is meant to.
	/// </summary>
	private static void BuildOccluders()
	{
		Transform occluders = CreateGroup("Occluders");

		// Parented to the figure rather than placed in the world, so the shade travels with it. This is the whole
		// reason for doing occlusion this way: it is the one thing screen space occlusion cannot be asked to do.
		CreateOccluder("Figure Contact", AnalyticOccluder.OccluderShape.Sphere, Vector3.zero, Quaternion.identity,
					   0.80f, 0.0f, 1.0f, 1.0f, 0.45f, figure).transform.position = new Vector3(1.3f, 0.06f, 6.5f);

		// Sitting the ladder down against the wall it leans on.
		CreateOccluder("Ladder Contact", AnalyticOccluder.OccluderShape.Sphere, new Vector3(7.9f, 0.15f, 22.6f), Quaternion.identity,
					   1.5f, 0.0f, 0.85f, 1.6f, 0.6f, occluders);

		// The edge of the slab, which is otherwise a hard, graphic line across the lower half of the frame.
		// The floor between the slab and the camera. Nothing stands there and nothing lights it, and the point of
		// shading it by hand is that the bottom of the frame closes on a fall of light rather than on an edge.
		CreateOccluder("Near Floor", AnalyticOccluder.OccluderShape.Capsule, new Vector3(0.0f, -0.4f, -3.5f), Quaternion.Euler(0.0f, 0.0f, 90.0f),
					   1.4f, 44.0f, 0.38f, 2.4f, 0.25f, occluders);

		// The seam where the floor runs into the backdrop.
		CreateOccluder("Backdrop Seam", AnalyticOccluder.OccluderShape.Capsule, new Vector3(0.0f, 0.2f, 24.3f), Quaternion.Euler(0.0f, 0.0f, 90.0f),
					   1.4f, 34.0f, 0.40f, 2.4f, 0.7f, occluders);

		// The seam along the ceiling, which keeps the top of the room from reading as a flat plate.
		CreateOccluder("Ceiling Seam", AnalyticOccluder.OccluderShape.Capsule, new Vector3(0.0f, 6.3f, 22.0f), Quaternion.Euler(0.0f, 0.0f, 90.0f),
					   1.6f, 34.0f, 0.45f, 3.0f, 0.6f, occluders);

		// Composition rather than contact: these two close the far corners so the eye stays on the lit middle.
		CreateOccluder("Corner Left", AnalyticOccluder.OccluderShape.Sphere, new Vector3(-16.0f, 3.0f, 21.0f), Quaternion.identity,
					   5.0f, 0.0f, 0.40f, 6.5f, 0.15f, occluders);
		CreateOccluder("Corner Right", AnalyticOccluder.OccluderShape.Sphere, new Vector3(16.0f, 3.0f, 21.0f), Quaternion.identity,
					   5.0f, 0.0f, 0.40f, 6.5f, 0.15f, occluders);
		CreateOccluder("Corner Right", AnalyticOccluder.OccluderShape.Sphere, new Vector3(16.0f, 3.0f, 21.0f), Quaternion.identity,
					   4.5f, 0.0f, 0.5f, 6.0f, 0.15f, occluders);
	}

	/// <summary>
	/// Creates one hand placed occluder.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="shape"></param>
	/// <param name="position"></param>
	/// <param name="rotation"></param>
	/// <param name="radius"></param>
	/// <param name="height"></param>
	/// <param name="intensity"></param>
	/// <param name="falloff"></param>
	/// <param name="directionality"></param>
	/// <param name="parent"></param>
	/// <returns></returns>
	private static AnalyticOccluder CreateOccluder(string name, AnalyticOccluder.OccluderShape shape, Vector3 position, Quaternion rotation, float radius, float height, float intensity, float falloff, float directionality, Transform parent)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(parent, false);
		go.transform.SetPositionAndRotation(position, rotation);

		AnalyticOccluder occluder = go.AddComponent<AnalyticOccluder>();
		occluder.shape = shape;
		occluder.radius = radius;
		occluder.height = height;
		occluder.intensity = intensity;
		occluder.falloff = falloff;
		occluder.directionality = directionality;

		return occluder;
	}

	/// <summary>
	/// Creates one box of fog.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="position"></param>
	/// <param name="size"></param>
	/// <param name="horizontalBorder"></param>
	/// <param name="verticalBorder"></param>
	/// <param name="densityMultiplier"></param>
	/// <param name="tint"></param>
	/// <param name="parent"></param>
	private static void CreateFogArea(string name, Vector3 position, Vector3 size, float horizontalBorder, float verticalBorder, float densityMultiplier, Color tint, Transform parent)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(parent, false);
		go.transform.position = position;

		VolumetricFogArea area = go.AddComponent<VolumetricFogArea>();
		area.size = size;
		area.horizontalBorder = horizontalBorder;
		area.verticalBorder = verticalBorder;
		area.densityMultiplier = densityMultiplier;
		area.tint = tint;
	}

	/// <summary>
	/// Creates the global volume of the scene along with the profile that grades it.
	/// </summary>
	private static void BuildVolume()
	{
		VolumeProfile profile = CreateVolumeProfile();

		GameObject go = new GameObject("Global Volume");
		go.transform.SetParent(sceneRoot, false);

		Volume volume = go.AddComponent<Volume>();
		volume.isGlobal = true;
		volume.priority = 0.0f;
		volume.sharedProfile = profile;
	}

	/// <summary>
	/// Creates, or rebuilds, the volume profile that holds both the volumetric fog and the grading of the scene.
	/// </summary>
	/// <returns></returns>
	private static VolumeProfile CreateVolumeProfile()
	{
		VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

		if (profile == null)
		{
			profile = ScriptableObject.CreateInstance<VolumeProfile>();
			AssetDatabase.CreateAsset(profile, VolumeProfilePath);
		}
		else
		{
			// The profile is emptied rather than deleted, so that whatever already points at it keeps working.
			foreach (VolumeComponent component in new List<VolumeComponent>(profile.components))
			{
				profile.Remove(component.GetType());
				Object.DestroyImmediate(component, true);
			}
		}

		AddFogOverride(profile);
		AddPostProcessingOverrides(profile);

		// Adding an override to a profile only puts it in the list held in memory. Unless every one of them is also
		// written into the profile asset as a sub asset, the whole grade is gone at the next domain reload and the
		// profile comes back with nothing but null references in it.
		foreach (VolumeComponent component in profile.components)
		{
			if (component == null || AssetDatabase.Contains(component))
				continue;

			component.hideFlags = HideFlags.HideInHierarchy;
			AssetDatabase.AddObjectToAsset(component, profile);
		}

		EditorUtility.SetDirty(profile);

		return profile;
	}

	/// <summary>
	/// Adds and tunes the volumetric fog override. Almost all of the light the fog scatters here is ambient rather
	/// than direct, which is what makes it read as an even, sourceless haze instead of as a beam.
	/// </summary>
	/// <param name="profile"></param>
	private static void AddFogOverride(VolumeProfile profile)
	{
		VolumetricFogVolumeComponent fog = profile.Add<VolumetricFogVolumeComponent>(true);

		Override(fog.enabled, true);
		Override(fog.distance, 90.0f);
		Override(fog.baseHeight, 0.2f);
		Override(fog.maximumHeight, 8.5f);
		Override(fog.heightFalloff, 0.75f);
		Override(fog.enableGround, true);
		Override(fog.groundHeight, -0.2f);

		// Thin, and deliberately so. This is not smoke: it is the air of a room that has been shut for a long time,
		// and it has to sit far enough below the point where it starts having shapes of its own that the only thing
		// it does is put more of itself between the camera and whatever is further away.
		Override(fog.density, 0.17f);
		Override(fog.areaGlobalDensity, 0.20f);

		// How dark the haze gets as it piles up. Shortening it is what closes the far end of the room into a single
		// wall of grey, which is the surface every silhouette in the frame is read against.
		Override(fog.attenuationDistance, 42.0f);

		// The haze builds up with distance rather than starting at the lens, which is what turns it into a depth cue
		// instead of a veil laid flat over the whole frame. The buildup is pushed back far enough that the mid ground
		// keeps its edges: everything nearer than the props stays readable, everything past them dissolves.
		Override(fog.startDistance, 3.0f);
		Override(fog.fullDensityDistance, 46.0f);
		Override(fog.distanceFalloff, 1.0f);

		// This is the parameter the entire look rests on. The haze is lit by nothing in particular, which is what
		// lets the far end of the room sit at an even, flat grey with no source anywhere in it. The colour is pulled
		// towards blue so that the depth of the shot and its coldness are the same gradient.
		Override(fog.enableAmbientContribution, true);
		Override(fog.ambientColor, new Color(0.62f, 0.68f, 0.78f));
		Override(fog.ambientIntensity, 0.36f);

		// The noise is turned nearly all the way down. Carved hard it makes clumps of smoke, and clumps of smoke have
		// a shape, a source and a direction, all of which pull the eye and none of which this room has. What is left
		// is just enough unevenness that the haze does not read as a flat sheet of grey laid over the lens.
		Override(fog.enableNoise, true);
		Override(fog.noiseIntensity, 0.28f);
		Override(fog.noiseCoverage, 0.72f);
		Override(fog.noiseTileSize, 20.0f);

		// Banks the thin and the thick of it into broad, slow banks across the room rather than an even scatter.
		Override(fog.enableNoiseMacro, true);
		Override(fog.noiseMacroTileSize, 34.0f);
		Override(fog.noiseMacroIntensity, 0.40f);

		// Frays what edges are left, so that the banks fade into one another instead of meeting.
		Override(fog.enableNoiseDetail, true);
		Override(fog.noiseDetailTileSize, 4.5f);
		Override(fog.noiseDetailIntensity, 0.15f);

		// Barely moving. Smoke this thick drifting at any speed reads as wind, and there is no wind in a basement.
		Override(fog.windDirection, new Vector3(1.0f, 0.08f, 0.15f));
		Override(fog.windSpeed, 0.22f);

		// Past this the clumps melt back into uniform fog, which keeps the far end of the room a single flat wall of
		// grey and hides the raymarching steps growing too long to resolve the noise.
		Override(fog.noiseFadeDistance, 38.0f);

		// Darkens the thin edges of the clumps, which is what gives them a fluffy border instead of a soft fade.
		Override(fog.powderIntensity, 0.3f);

		// The directional fill is barely allowed into the haze at all. A beam coming in from off screen would give the
		// room a window, and the one shaft this image is allowed to have is the one hanging off the lamp.
		Override(fog.enableMainLightContribution, true);
		Override(fog.anisotropy, 0.45f);
		Override(fog.scattering, 0.18f);
		Override(fog.tint, new Color(0.95f, 0.97f, 1.0f));
		Override(fog.enableAdditionalLightsContribution, true);

		// The clumps shade each other, which is the difference between smoke that has a near side and a far side and
		// smoke that glows evenly from the inside.
		Override(fog.enableSelfShadowing, true);
		Override(fog.selfShadowingIntensity, 0.18f);
		Override(fog.selfShadowingDistance, 12.0f);

		// The cone off the lamp is the one piece of structure in the haze, and it is what the step count is for: too
		// few and it bands across its own falloff. Two blur passes, because with the noise this far down there is no
		// detail left worth protecting and the raymarching grain is the only thing they take off.
		Override(fog.maxSteps, 176);
		Override(fog.blurIterations, 2);
	}

	/// <summary>
	/// Adds and tunes everything that grades the image. The palette is pulled nearly all the way to monochrome and the
	/// contrast is kept low in the midtones, so that the only real steps in the image are between the black foreground
	/// and everything behind it.
	/// </summary>
	/// <param name="profile"></param>
	private static void AddPostProcessingOverrides(VolumeProfile profile)
	{
		Tonemapping tonemapping = profile.Add<Tonemapping>(true);
		Override(tonemapping.mode, TonemappingMode.ACES);

		// Lifted, and flattened. The frame is meant to read as two values, the haze and the black cut out of it, and
		// the way to get there is to put the haze up near the top of the range rather than to pull the two apart with
		// contrast, which only ever costs the mid greys the far end of the room is made of.
		ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>(true);
		Override(colorAdjustments.postExposure, 0.20f);
		Override(colorAdjustments.contrast, 10.0f);
		Override(colorAdjustments.saturation, -85.0f);
		Override(colorAdjustments.colorFilter, new Color(1.0f, 1.0f, 0.99f));

		WhiteBalance whiteBalance = profile.Add<WhiteBalance>(true);
		Override(whiteBalance.temperature, -20.0f);
		Override(whiteBalance.tint, -4.0f);

		// A touch of cold in the shadows and of warmth in the light is all the colour this image gets.
		ShadowsMidtonesHighlights smh = profile.Add<ShadowsMidtonesHighlights>(true);
		Override(smh.shadows, new Vector4(0.90f, 0.98f, 1.14f, 0.0f));
		Override(smh.midtones, new Vector4(1.0f, 1.0f, 1.0f, 0.0f));
		Override(smh.highlights, new Vector4(1.02f, 1.01f, 1.0f, 0.0f));

		// Low threshold and wide scatter, so that the bloom is picked up by the haze itself rather than only by the
		// fixture. It is what puts the soft halo around everything bright enough to be seen through the air.
		Bloom bloom = profile.Add<Bloom>(true);
		Override(bloom.threshold, 0.60f);
		Override(bloom.intensity, 0.55f);
		Override(bloom.scatter, 0.85f);
		Override(bloom.tint, new Color(1.0f, 0.99f, 0.95f));

		Vignette vignette = profile.Add<Vignette>(true);
		Override(vignette.intensity, 0.22f);
		Override(vignette.smoothness, 0.65f);
		Override(vignette.color, Color.black);

		DepthOfField depthOfField = profile.Add<DepthOfField>(true);
		Override(depthOfField.mode, DepthOfFieldMode.Bokeh);
		Override(depthOfField.focusDistance, 22.0f);
		Override(depthOfField.aperture, 12.0f);
		Override(depthOfField.focalLength, 55.0f);

		ChromaticAberration chromaticAberration = profile.Add<ChromaticAberration>(true);
		Override(chromaticAberration.intensity, 0.06f);

		// Most of the grain comes from the dithering render feature, which pushes it into the shadows where it
		// belongs. This is the little that is left over the flat haze, where dithering has no gradient to work on.
		FilmGrain filmGrain = profile.Add<FilmGrain>(true);
		Override(filmGrain.intensity, 0.13f);
		Override(filmGrain.response, 0.85f);
	}

	/// <summary>
	/// Places the camera. A long lens flattens the room into layers, which is what a side on shot needs in order to
	/// read as a stack of silhouettes rather than as a space.
	/// </summary>
	private static void BuildCamera()
	{
		GameObject go = new GameObject("Main Camera");
		go.tag = "MainCamera";
		go.transform.SetParent(sceneRoot, false);
		go.transform.SetPositionAndRotation(new Vector3(-0.6f, 3.20f, -13.0f), Quaternion.Euler(4.5f, 0.0f, 0.0f));

		Camera camera = go.AddComponent<Camera>();
		camera.fieldOfView = 30.0f;
		camera.nearClipPlane = 0.3f;
		camera.farClipPlane = 220.0f;
		camera.clearFlags = CameraClearFlags.SolidColor;

		// Whatever is beyond the room is the same value as the haze that hides it, so the two never meet in a seam.
		camera.backgroundColor = new Color(0.075f, 0.080f, 0.085f);
		camera.allowMSAA = false;

		UniversalAdditionalCameraData cameraData = go.AddComponent<UniversalAdditionalCameraData>();
		cameraData.renderPostProcessing = true;
		cameraData.renderShadows = true;
		cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
		cameraData.antialiasingQuality = AntialiasingQuality.High;
	}

	#endregion

	#region Helper Methods

	/// <summary>
	/// Overrides the given volume parameter with the given value.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="parameter"></param>
	/// <param name="value"></param>
	private static void Override<T>(VolumeParameter<T> parameter, T value)
	{
		parameter.overrideState = true;
		parameter.value = value;
	}

	/// <summary>
	/// Creates an empty game object meant to group others under it.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="parent"></param>
	/// <returns></returns>
	private static Transform CreateGroup(string name, Transform parent = null)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(parent != null ? parent : sceneRoot, false);

		return go.transform;
	}

	/// <summary>
	/// Creates a box of the given size, in world units, with the given material.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="position"></param>
	/// <param name="size"></param>
	/// <param name="material"></param>
	/// <param name="parent"></param>
	/// <returns></returns>
	private static GameObject CreateBox(string name, Vector3 position, Vector3 size, Material material, Transform parent)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = name;
		go.transform.SetParent(parent, false);
		go.transform.position = position;
		go.transform.localScale = size;
		go.GetComponent<MeshRenderer>().sharedMaterial = material;

		Object.DestroyImmediate(go.GetComponent<Collider>());

		return go;
	}

	/// <summary>
	/// Creates the shade of the lamp: a frustum, widening downwards, built around its own centre. It is the one shape
	/// in the scene that is neither a box nor a cylinder, and it earns that because the cone of light hanging under it
	/// is read straight off its silhouette. A cylinder in its place reads as a can, and the light under a can does not
	/// look like the light under a lamp.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="position"></param>
	/// <param name="topRadius"></param>
	/// <param name="bottomRadius"></param>
	/// <param name="height"></param>
	/// <param name="material"></param>
	/// <param name="parent"></param>
	/// <returns></returns>
	private static GameObject CreateCone(string name, Vector3 position, float topRadius, float bottomRadius, float height, Material material, Transform parent)
	{
		GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
		go.transform.SetParent(parent, false);
		go.transform.position = position;

		go.GetComponent<MeshFilter>().sharedMesh = CreateConeMesh(name, topRadius, bottomRadius, height);
		go.GetComponent<MeshRenderer>().sharedMaterial = material;

		return go;
	}

	/// <summary>
	/// Builds, and saves, the mesh of a shade. It is written to disk because a mesh built in memory is gone by the
	/// time the scene is opened again, and it is two sided because the camera sits below the rim: a single sided
	/// shade would simply stop existing from the one angle the shot actually looks at it from.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="topRadius"></param>
	/// <param name="bottomRadius"></param>
	/// <param name="height"></param>
	/// <returns></returns>
	private static Mesh CreateConeMesh(string name, float topRadius, float bottomRadius, float height)
	{
		const int segments = 32;

		List<Vector3> vertices = new List<Vector3>();
		List<Vector3> normals = new List<Vector3>();
		List<int> triangles = new List<int>();

		float halfHeight = height * 0.5f;

		for (int i = 0; i < segments; ++i)
		{
			float angleA = i / (float)segments * Mathf.PI * 2.0f;
			float angleB = (i + 1) / (float)segments * Mathf.PI * 2.0f;

			Vector3 directionA = new Vector3(Mathf.Cos(angleA), 0.0f, Mathf.Sin(angleA));
			Vector3 directionB = new Vector3(Mathf.Cos(angleB), 0.0f, Mathf.Sin(angleB));

			Vector3 topA = directionA * topRadius + Vector3.up * halfHeight;
			Vector3 topB = directionB * topRadius + Vector3.up * halfHeight;
			Vector3 bottomA = directionA * bottomRadius - Vector3.up * halfHeight;
			Vector3 bottomB = directionB * bottomRadius - Vector3.up * halfHeight;

			// The side of a shade that widens downwards leans outwards and upwards, by however much wider its bottom
			// is than its top over however tall it is.
			Vector3 direction = (directionA + directionB).normalized;
			Vector3 outward = new Vector3(direction.x * height, bottomRadius - topRadius, direction.z * height).normalized;

			AddQuad(vertices, normals, triangles, topA, topB, bottomB, bottomA, outward);
			AddQuad(vertices, normals, triangles, bottomA, bottomB, topB, topA, -outward);
		}

		string path = MeshesFolder + "/" + name + ".asset";

		Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
		bool isNew = mesh == null;

		if (isNew)
			mesh = new Mesh();

		mesh.Clear();
		mesh.name = name;
		mesh.SetVertices(vertices);
		mesh.SetNormals(normals);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateBounds();

		if (isNew)
			AssetDatabase.CreateAsset(mesh, path);
		else
			EditorUtility.SetDirty(mesh);

		return mesh;
	}

	/// <summary>
	/// Adds one flat shaded quad, wound in the order the corners are given in, to the lists a mesh is being built up
	/// out of.
	/// </summary>
	/// <param name="vertices"></param>
	/// <param name="normals"></param>
	/// <param name="triangles"></param>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <param name="c"></param>
	/// <param name="d"></param>
	/// <param name="normal"></param>
	private static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
	{
		int baseIndex = vertices.Count;

		vertices.Add(a);
		vertices.Add(b);
		vertices.Add(c);
		vertices.Add(d);

		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);

		triangles.Add(baseIndex);
		triangles.Add(baseIndex + 1);
		triangles.Add(baseIndex + 2);

		triangles.Add(baseIndex);
		triangles.Add(baseIndex + 2);
		triangles.Add(baseIndex + 3);
	}

	/// <summary>
	/// Creates a cylinder of the given radius and length, laid along its own up axis before the given rotation.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="position"></param>
	/// <param name="eulerAngles"></param>
	/// <param name="radius"></param>
	/// <param name="length"></param>
	/// <param name="material"></param>
	/// <param name="parent"></param>
	/// <returns></returns>
	private static GameObject CreatePipe(string name, Vector3 position, Vector3 eulerAngles, float radius, float length, Material material, Transform parent)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		go.name = name;
		go.transform.SetParent(parent, false);
		go.transform.SetPositionAndRotation(position, Quaternion.Euler(eulerAngles));

		// A default cylinder is two units tall, so half the length is what its scale has to be.
		go.transform.localScale = new Vector3(radius * 2.0f, length * 0.5f, radius * 2.0f);
		go.GetComponent<MeshRenderer>().sharedMaterial = material;

		Object.DestroyImmediate(go.GetComponent<Collider>());

		return go;
	}

	#endregion
}
