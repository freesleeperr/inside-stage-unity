using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Relights whatever stage is already in the scene, without touching a single piece of its geometry. Every position
/// below is derived from the bounds of the deck rather than written down, so the same rig lands correctly on a stage
/// of any size, and the numbers that are absolute are the ones that are about the air rather than about the room.
///
/// The look this places is the one thing the mood scene and this share: a single practical throwing a pool and a long
/// shadow, a wall of lit haze behind the stage for those shadows and silhouettes to be read against, and an ambient
/// low enough that everything the practical does not reach falls to black.
/// </summary>
public static class InsideStageLighting
{
	#region Private Attributes

	private const string StageFloorName = "Floor";
	private const string LampBulbName = "Lamp Bulb";

	private const string LightingGroupName = "Lighting";
	private const string FogGroupName = "Fog Areas";
	private const string OccluderGroupName = "Occluders";

	// The deck, in world space. Everything the rig places is an offset from these.
	private static Vector3 stageCenter;
	private static float stageHalfWidth;
	private static float stageHalfDepth;
	private static float stageTop;

	private static Transform sceneRoot;

	#endregion

	#region Methods

	/// <summary>
	/// Places the whole rig onto the stage in the open scene, replacing whatever a previous run left behind.
	/// </summary>
	[MenuItem("Tools/Inside/Light Stage")]
	public static void LightStage()
	{
		if (!MeasureStage())
			return;

		BuildEnvironmentSettings();
		BuildLighting();
		BuildFogAreas();
		BuildOccluders();
		TuneVolumeProfile();

		EditorSceneManager.MarkSceneDirty(sceneRoot.gameObject.scene);
		EditorSceneManager.SaveScene(sceneRoot.gameObject.scene);

		AssetDatabase.SaveAssets();

		Debug.Log("The stage has been relit around a deck of " + (stageHalfWidth * 2.0f).ToString("0.##") + " by " + (stageHalfDepth * 2.0f).ToString("0.##") + " at " + stageCenter + ".");
	}

	/// <summary>
	/// Finds the deck and measures it. Nothing else in this file makes sense until this has run: the rig is placed in
	/// terms of where the stage is and how big it is, and neither of those is something to assume.
	/// </summary>
	/// <returns></returns>
	private static bool MeasureStage()
	{
		GameObject floor = GameObject.Find(StageFloorName);

		if (floor == null)
		{
			foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
			{
				Transform found = root.transform.Find("Environment/Room/" + StageFloorName);

				if (found == null)
					continue;

				floor = found.gameObject;

				break;
			}
		}

		MeshRenderer renderer = floor != null ? floor.GetComponent<MeshRenderer>() : null;

		if (renderer == null)
		{
			Debug.LogError("No object named '" + StageFloorName + "' with a renderer on it was found, so there is no stage to measure and nothing to light.");

			return false;
		}

		Bounds bounds = renderer.bounds;

		stageCenter = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
		stageHalfWidth = bounds.extents.x;
		stageHalfDepth = bounds.extents.z;
		stageTop = bounds.max.y;

		sceneRoot = floor.transform.root;

		return true;
	}

	#endregion

	#region Environment Methods

	/// <summary>
	/// Pulls the ambient down. This is the first half of the whole look and the easiest thing to get wrong: an ambient
	/// bright enough to be comfortable lights every face of every prop, and a prop with a lit front is a grey object
	/// on a dark ground rather than a silhouette. What is left here is barely enough to keep the shadows from being
	/// holes, and everything else the image needs comes from the haze behind it.
	/// </summary>
	private static void BuildEnvironmentSettings()
	{
		RenderSettings.skybox = null;

		RenderSettings.ambientMode = AmbientMode.Trilight;
		RenderSettings.ambientSkyColor = new Color(0.24f, 0.27f, 0.33f);
		RenderSettings.ambientEquatorColor = new Color(0.13f, 0.15f, 0.19f);
		RenderSettings.ambientGroundColor = new Color(0.045f, 0.05f, 0.062f);
		RenderSettings.ambientIntensity = 1.0f;

		RenderSettings.fog = false;
		RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
		RenderSettings.customReflection = null;
		RenderSettings.reflectionIntensity = 0.25f;
	}

	#endregion

	#region Lighting Methods

	/// <summary>
	/// Places the lights. There are five, and only two of them are ever meant to be noticed: the practical hanging
	/// over the deck, and the haze standing behind it. The other three exist to keep what those two leave dark from
	/// going to nothing at all.
	/// </summary>
	private static void BuildLighting()
	{
		Transform lights = ClearGroup(LightingGroupName);

		// The practical, anchored to the bulb that is already hanging there so that the cone in the haze and the shade
		// it comes out of cannot drift apart. It is aimed a little in front of the middle of the deck rather than
		// straight down: a vertical cone puts every shadow directly under the thing casting it, and this look lives on
		// the shadows being long and pointed at the camera.
		Vector3 keyPosition = FindLampAnchor();
		Vector3 keyTarget = stageCenter + new Vector3(-0.6f, 0.0f, -1.0f);

		CreateSpotLight("Lamp Key", keyPosition, Quaternion.LookRotation(keyTarget - keyPosition),
						new Color(1.0f, 0.96f, 0.88f), 230.0f, 34.0f, 66.0f, true, 1.0f, 2.6f, 0.4f, lights);

		// The spill the shade lets past its rim, thrown back up. It is what keeps the fixture from sitting in a black
		// hole of its own making, and it is the only thing in the frame lighting the air above the lamp.
		CreateSpotLight("Lamp Spill", keyPosition + new Vector3(0.0f, 0.55f, 0.0f), Quaternion.Euler(-90.0f, 0.0f, 0.0f),
						new Color(1.0f, 0.97f, 0.92f), 14.0f, 8.0f, 150.0f, false, 0.0f, 0.25f, 0.0f, lights);

		// The second half of the look. There is no backdrop wall any more, so the wall of grey the silhouettes are
		// read against has to be made out of the air itself: a wide, dim source standing behind the stage and pointed
		// back at the camera, lighting nothing but the haze it shines through. Everything on the deck goes dark
		// against it, which is the entire reason the deck can be lit by one lamp and still read.
		Vector3 hazePosition = stageCenter + new Vector3(0.0f, 4.6f, stageHalfDepth + 10.0f);

		CreateSpotLight("Haze Wall", hazePosition, Quaternion.LookRotation(stageCenter + new Vector3(0.0f, 2.2f, 0.0f) - hazePosition),
						new Color(0.90f, 0.94f, 1.0f), 22.0f, 26.0f, 84.0f, false, 0.0f, 0.35f, 0.15f, lights);

		// The rake. It comes from behind the stage and off to the side opposite the lamp, and it shadows: that is what
		// it is for. It pulls a thin edge of light down the far side of everything standing on the deck and throws a
		// second, longer shadow towards the camera, which is what stops the deck from reading as a flat lit rectangle.
		Vector3 rimPosition = stageCenter + new Vector3(-stageHalfWidth - 1.5f, 3.6f, stageHalfDepth + 3.0f);

		CreateSpotLight("Back Rim", rimPosition, Quaternion.LookRotation(stageCenter + new Vector3(0.8f, 0.7f, -1.0f) - rimPosition),
						new Color(0.88f, 0.93f, 1.0f), 42.0f, 24.0f, 54.0f, true, 0.9f, 0.5f, 0.2f, lights);

		// The bounce off the boards, aimed back up. Without it the undersides of everything on the deck go flat and
		// dead, which is the one thing that gives away that there is no global illumination at play.
		CreateSpotLight("Deck Bounce", stageCenter + new Vector3(0.5f, 0.35f, 0.5f), Quaternion.Euler(-90.0f, 0.0f, 0.0f),
						new Color(0.86f, 0.89f, 0.94f), 16.0f, 9.0f, 150.0f, false, 0.0f, 0.0f, 0.0f, lights);

		// A very low frontal fill, from where the camera is. It is not there to light anything: it is there so that
		// the near faces sit a hair above pure black, which is the difference between a silhouette and a hole.
		Vector3 fillPosition = stageCenter + new Vector3(-1.0f, 5.0f, -stageHalfDepth - 8.0f);

		CreateSpotLight("Front Fill", fillPosition, Quaternion.LookRotation(stageCenter + new Vector3(0.0f, 0.6f, 0.0f) - fillPosition),
						new Color(0.86f, 0.90f, 0.98f), 10.0f, 26.0f, 60.0f, false, 0.0f, 0.0f, 0.0f, lights);
	}

	/// <summary>
	/// Finds the bulb the practical hangs off, so that the light is placed where the fixture actually is. A stage with
	/// no fixture over it gets one hung above the middle of it instead.
	/// </summary>
	/// <returns></returns>
	private static Vector3 FindLampAnchor()
	{
		GameObject bulb = GameObject.Find(LampBulbName);

		return bulb != null ? bulb.transform.position : stageCenter + new Vector3(0.0f, 5.3f, stageHalfDepth * 0.5f);
	}

	/// <summary>
	/// Creates one spot light, optionally shadowing and optionally contributing to the volumetric fog.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="position"></param>
	/// <param name="rotation"></param>
	/// <param name="color"></param>
	/// <param name="intensity"></param>
	/// <param name="range"></param>
	/// <param name="spotAngle"></param>
	/// <param name="castShadows"></param>
	/// <param name="shadowStrength"></param>
	/// <param name="scattering"></param>
	/// <param name="anisotropy"></param>
	/// <param name="parent"></param>
	private static void CreateSpotLight(string name, Vector3 position, Quaternion rotation, Color color, float intensity, float range, float spotAngle, bool castShadows, float shadowStrength, float scattering, float anisotropy, Transform parent)
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

		// A wide inner angle gives the pool a hard rim, which reads as a spotlight on a stage. This look wants the
		// opposite: the edge of the pool has to fall off over most of the cone so that it dies into the haze rather
		// than ending on a line drawn across the boards.
		light.innerSpotAngle = spotAngle * 0.18f;

		light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
		light.shadowStrength = shadowStrength;
		light.shadowBias = 0.035f;
		light.shadowNormalBias = 0.3f;
		light.shadowNearPlane = 0.15f;

		if (scattering <= 0.0f)
			return;

		VolumetricAdditionalLight volumetricLight = go.AddComponent<VolumetricAdditionalLight>();
		volumetricLight.Scattering = scattering;
		volumetricLight.Anisotropy = anisotropy;
		volumetricLight.Radius = 0.4f;
	}

	#endregion

	#region Fog Methods

	/// <summary>
	/// Places the boxes of haze. Three of them do three different jobs: one fills the air the stage stands in, one
	/// stands behind it and is the grey wall everything is read against, and one thickens the air under the lamp so
	/// that its cone has something to be drawn in.
	/// </summary>
	private static void BuildFogAreas()
	{
		Transform areas = ClearGroup(FogGroupName);

		// The air around the deck. It is wider than the stage on every side, because the edge of a fog box that ends
		// inside the frame is visible as a seam no matter how soft its border is.
		CreateFogArea("Stage Haze", stageCenter + new Vector3(0.0f, 2.0f, 0.0f), new Vector3(stageHalfWidth * 2.0f + 10.0f, 6.0f, stageHalfDepth * 2.0f + 12.0f),
					  0.25f, 0.35f, 0.45f, new Color(0.92f, 0.96f, 1.0f), areas);

		// The wall. It is thick, it is tall, and it sits far enough behind the deck that the shadows thrown at the
		// camera have somewhere to come from. This is the backdrop of the shot, and it is made of nothing.
		CreateFogArea("Back Haze", stageCenter + new Vector3(0.0f, 3.8f, stageHalfDepth + 7.0f), new Vector3(stageHalfWidth * 2.0f + 24.0f, 11.0f, 14.0f),
					  0.3f, 0.4f, 1.2f, new Color(0.93f, 0.96f, 1.0f), areas);

		// A shallow layer over the boards, which lifts the base of everything standing on them and keeps the deck from
		// ending in mid air along its edges. Kept thin: any denser and the lit half of the deck washes out to a flat
		// white, which costs the shadows the practical is there to draw.
		CreateFogArea("Deck Haze", stageCenter + new Vector3(0.0f, 0.55f, 0.0f), new Vector3(stageHalfWidth * 2.0f + 6.0f, 1.8f, stageHalfDepth * 2.0f + 6.0f),
					  0.3f, 0.6f, 0.35f, Color.white, areas);

		// A pocket under the fixture, and nowhere else. The cone reads because the air it crosses is thicker than the
		// air around it, which is far easier to control than raising the density of everything until the cone shows up
		// and then finding the haze behind the stage has gone white.
		Vector3 lamp = FindLampAnchor();

		CreateFogArea("Lamp Pocket", new Vector3(lamp.x - 0.4f, (lamp.y + stageTop) * 0.5f, lamp.z - 1.0f), new Vector3(9.0f, lamp.y - stageTop + 1.5f, 9.0f),
					  0.55f, 0.45f, 1.4f, new Color(1.0f, 0.99f, 0.96f), areas);
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

	#endregion

	#region Occluder Methods

	/// <summary>
	/// Places the shade by hand. A shadow map draws where the light is blocked, and nothing else: it cannot darken the
	/// millimetre of floor a box is standing on, and that contact is what tells the eye the box is on the deck rather
	/// than floating a hand above it. Every shape below is one of those contacts, or a corner of the frame that wanted
	/// to be darker so the eye stays on the middle.
	/// </summary>
	private static void BuildOccluders()
	{
		Transform occluders = ClearGroup(OccluderGroupName);

		// The contacts below are parented to the props they belong to rather than to the group, so emptying the group
		// is not enough to clear the ones a previous run left behind.
		foreach (AnalyticOccluder existing in Object.FindObjectsOfType<AnalyticOccluder>())
			Object.DestroyImmediate(existing.gameObject);

		// Everything standing on the deck gets sat down onto it, whether it was put there by a builder or by hand.
		// Parented to what it belongs to, so that the shade travels with it: this is the one thing screen space
		// occlusion cannot be asked to do.
		foreach (Transform prop in FindProps())
		{
			Bounds bounds = prop.GetComponent<MeshRenderer>().bounds;
			float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.35f;

			CreateOccluder(prop.name + " Contact", AnalyticOccluder.OccluderShape.Sphere,
						   new Vector3(bounds.center.x, stageTop + 0.05f, bounds.center.z), Quaternion.identity,
						   radius, 0.0f, 1.0f, radius * 1.6f, 0.5f, prop);
		}

		// The near edge of the boards, which is otherwise a hard, graphic line straight across the frame.
		CreateOccluder("Deck Edge", AnalyticOccluder.OccluderShape.Capsule, stageCenter + new Vector3(0.0f, 0.05f, -stageHalfDepth + 0.6f), Quaternion.Euler(0.0f, 0.0f, 90.0f),
					   1.3f, stageHalfWidth * 2.0f, 0.4f, 2.2f, 0.3f, occluders);

		// The underside of the deck. The stage stands in a room with no floor under it, and without this the slab
		// reads as a lit rectangle hanging in the air rather than as something with weight.
		CreateOccluder("Deck Base", AnalyticOccluder.OccluderShape.Capsule, stageCenter + new Vector3(0.0f, -1.0f, -stageHalfDepth + 0.2f), Quaternion.Euler(0.0f, 0.0f, 90.0f),
					   1.6f, stageHalfWidth * 2.0f, 0.9f, 2.4f, 0.55f, occluders);

		// Composition rather than contact: these two close the back corners so the eye stays on the lit middle.
		CreateOccluder("Corner Left", AnalyticOccluder.OccluderShape.Sphere, stageCenter + new Vector3(-stageHalfWidth - 1.5f, 2.0f, stageHalfDepth), Quaternion.identity,
					   4.5f, 0.0f, 0.45f, 6.0f, 0.15f, occluders);
		CreateOccluder("Corner Right", AnalyticOccluder.OccluderShape.Sphere, stageCenter + new Vector3(stageHalfWidth + 1.5f, 2.0f, stageHalfDepth), Quaternion.identity,
					   4.5f, 0.0f, 0.45f, 6.0f, 0.15f, occluders);
	}

	/// <summary>
	/// Finds everything standing on the deck: whatever has a renderer, sits inside the footprint of the stage, and is
	/// resting on top of it rather than being the stage itself or hanging over it.
	/// </summary>
	/// <returns></returns>
	private static List<Transform> FindProps()
	{
		List<Transform> props = new List<Transform>();

		foreach (MeshRenderer renderer in Object.FindObjectsOfType<MeshRenderer>())
		{
			Bounds bounds = renderer.bounds;

			if (Mathf.Abs(bounds.center.x - stageCenter.x) > stageHalfWidth || Mathf.Abs(bounds.center.z - stageCenter.z) > stageHalfDepth)
				continue;

			// Anything whose base is a good way off the boards is hanging rather than standing, and the fixture
			// overhead is the whole reason that test is here.
			if (bounds.min.y < stageTop - 0.1f || bounds.min.y > stageTop + 0.35f)
				continue;

			props.Add(renderer.transform);
		}

		return props;
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
	private static void CreateOccluder(string name, AnalyticOccluder.OccluderShape shape, Vector3 position, Quaternion rotation, float radius, float height, float intensity, float falloff, float directionality, Transform parent)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(parent, true);
		go.transform.SetPositionAndRotation(position, rotation);

		AnalyticOccluder occluder = go.AddComponent<AnalyticOccluder>();
		occluder.shape = shape;
		occluder.radius = radius;
		occluder.height = height;
		occluder.intensity = intensity;
		occluder.falloff = falloff;
		occluder.directionality = directionality;
	}

	#endregion

	#region Volume Methods

	/// <summary>
	/// Retunes the haze around the size of the shot. Every parameter touched here is measured in meters, and a set of
	/// them tuned for a corridor fifty meters deep does nothing at all across twenty: the fog is still there, it just
	/// never gets thick enough to be seen before the frame runs out of distance to build it up over.
	/// </summary>
	private static void TuneVolumeProfile()
	{
		Volume volume = Object.FindObjectOfType<Volume>();
		VolumeProfile profile = volume != null ? volume.sharedProfile : null;

		if (profile == null || !profile.TryGet(out VolumetricFogVolumeComponent fog))
		{
			Debug.LogWarning("No volume with a volumetric fog override was found, so the haze has been left at whatever it was tuned to.");

			return;
		}

		float depth = Vector3.Distance(Camera.main != null ? Camera.main.transform.position : stageCenter + new Vector3(0.0f, 4.0f, -21.0f), stageCenter);

		Override(fog.distance, depth * 3.0f);

		// The haze sits from just under the boards to well over the fixture, and no higher: it is a room with a lid,
		// not weather.
		Override(fog.baseHeight, stageTop - 1.2f);
		Override(fog.maximumHeight, stageTop + 8.0f);
		Override(fog.heightFalloff, 0.8f);
		Override(fog.enableGround, false);

		Override(fog.density, 0.13f);
		Override(fog.areaGlobalDensity, 0.15f);

		// How dark the haze gets as it piles up, and how far it takes to get there. Both are set off the distance
		// between the camera and the stage, which is the only distance in the frame that matters.
		Override(fog.attenuationDistance, depth * 0.9f);

		// The haze holds off until well past the near field. Starting it at the lens lays it flat over the whole frame
		// as a veil, and the black the stage is framed by is the first thing that veil costs.
		Override(fog.startDistance, depth * 0.45f);
		Override(fog.fullDensityDistance, depth * 1.5f);
		Override(fog.distanceFalloff, 1.0f);

		Override(fog.enableAmbientContribution, true);
		Override(fog.ambientColor, new Color(0.60f, 0.66f, 0.78f));
		Override(fog.ambientIntensity, 0.22f);

		// The tiles of the noise are scaled with the shot for the same reason the distances are: banks the size of the
		// old room are a single flat value across a stage this size.
		Override(fog.enableNoise, true);
		Override(fog.noiseIntensity, 0.26f);
		Override(fog.noiseCoverage, 0.72f);
		Override(fog.noiseTileSize, 10.0f);

		Override(fog.enableNoiseMacro, true);
		Override(fog.noiseMacroTileSize, 18.0f);
		Override(fog.noiseMacroIntensity, 0.38f);

		Override(fog.enableNoiseDetail, true);
		Override(fog.noiseDetailTileSize, 2.4f);
		Override(fog.noiseDetailIntensity, 0.15f);

		Override(fog.noiseFadeDistance, depth * 1.2f);
		Override(fog.selfShadowingDistance, 8.0f);

		// The practicals are what make the cone and the wall, so they are let into the haze in full. The directional
		// fill is not: a beam coming in from off screen would give the stage an outside, and it does not have one.
		Override(fog.enableAdditionalLightsContribution, true);
		Override(fog.enableMainLightContribution, false);

		EditorUtility.SetDirty(profile);

		if (!profile.TryGet(out DepthOfField depthOfField))
			return;

		// Focused on the stage, with the haze behind it falling off.
		Override(depthOfField.focusDistance, depth);
	}

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

	#endregion

	#region Helper Methods

	/// <summary>
	/// Empties the group of the given name, creating it under the root of the scene when it is not there yet. The
	/// groups are emptied rather than replaced so that anything pointing at them keeps working, and so that a rerun
	/// after a tweak leaves the scene with one rig in it rather than two.
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	private static Transform ClearGroup(string name)
	{
		Transform group = sceneRoot.Find(name);

		if (group == null)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(sceneRoot, false);

			return go.transform;
		}

		for (int i = group.childCount - 1; i >= 0; --i)
			Object.DestroyImmediate(group.GetChild(i).gameObject);

		return group;
	}

	#endregion
}
