using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// Renders the scene mirrored across the plane defined by this transform into a texture, and hands that texture to
	/// the surfaces that reflect it, so that a flat surface can show a real reflection of what stands on it. It is what
	/// turns a floor into a wet one, which is the cheapest way of getting the foreground silhouettes to read twice in
	/// the frame.
	///
	/// The texture is written onto the surfaces themselves rather than into a global, so a scene can hold several
	/// reflective planes at once. It is also kept per camera, so that the scene view showing the reflection does not
	/// leave the game view with a reflection rendered for the wrong point of view.
	/// </summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[AddComponentMenu("Rendering/Planar Reflection")]
	public sealed class PlanarReflection : MonoBehaviour
	{
	#region Public Attributes

		[Tooltip("The surfaces the reflection is written onto. Leaving it empty uses every renderer on this object and its children, which is what a floor made of a single mesh wants.")]
		public List<Renderer> targetRenderers = new List<Renderer>();
		[Tooltip("The layers that are rendered into the reflection. Leaving the small props out keeps the reflection cheap without anybody noticing.")]
		public LayerMask reflectionLayers = -1;
		[Tooltip("The resolution of the reflection texture, as a fraction of the resolution of the camera. Reflections on a rough floor are blurry anyway, so this can go low.")]
		[Range(0.1f, 1.0f)]
		public float resolutionScale = 0.5f;
		[Tooltip("How far above the plane the mirrored camera starts clipping. Raising it hides the seam that shows up where geometry meets the floor, at the cost of cutting the reflection slightly short.")]
		public float clipPlaneOffset = 0.02f;
		[Tooltip("Whether the reflection renders shadows. Turning it off nearly halves its cost and is rarely noticed on a floor.")]
		public bool renderShadows = false;
		[Tooltip("Whether the reflection renders the volumetric fog and the rest of the post processing. It is expensive, and the fog is already visible through the reflected geometry.")]
		public bool renderPostProcessing = false;
		[Tooltip("Whether the reflection is also rendered for the scene view. It costs a second pass over the scene while the editor window is open, and it is the only way to see what the floor will actually look like without entering play mode.")]
		public bool renderInSceneView = true;

	#endregion

	#region Private Attributes

		private static readonly int PlanarReflectionTextureId = Shader.PropertyToID("_PlanarReflectionTexture");

		private static bool isRenderingReflection;

		// One texture per camera. A single one shared between the game view and the scene view would be resized back and
		// forth every frame, and whichever of them rendered last would leave its point of view on the floor of the other.
		private readonly Dictionary<Camera, RenderTexture> reflectionTextures = new Dictionary<Camera, RenderTexture>();
		private readonly List<Camera> staleCameras = new List<Camera>();

		// Reused between frames, and read back before it is written so that whatever else has been set on a renderer
		// survives having the reflection handed to it. It cannot be built in a field initializer, as the engine refuses
		// to create one while a MonoBehaviour is still being constructed.
		private MaterialPropertyBlock propertyBlock;

		// The render request is the path that replaces UniversalRenderPipeline.RenderSingleCamera, which is deprecated
		// and gone from Unity 6.
		private readonly UniversalRenderPipeline.SingleCameraRequest reflectionRenderRequest = new UniversalRenderPipeline.SingleCameraRequest();

		private Camera reflectionCamera;
		private Renderer[] resolvedRenderers;

	#endregion

	#region MonoBehaviour Methods

		private void OnEnable()
		{
			ResolveTargetRenderers();

			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
		}

		private void OnDisable()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

			SetOwnRenderersVisible(true);
			SetReflectionTexture(null);
			Cleanup();
		}

#if UNITY_EDITOR

		private void OnValidate()
		{
			// The list is authored in the inspector, so the renderers it resolves to have to follow it while the scene is
			// being put together rather than only when the component is enabled.
			if (isActiveAndEnabled)
				ResolveTargetRenderers();
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
			Gizmos.color = new Color(0.4f, 0.8f, 1.0f, 0.5f);
			Gizmos.DrawWireCube(Vector3.zero, new Vector3(20.0f, 0.0f, 20.0f));
		}

#endif

	#endregion

	#region Methods

		/// <summary>
		/// Rebuilds the list of surfaces the reflection is written onto, from what has been authored or, failing that,
		/// from the renderers this component lives on.
		/// </summary>
		public void ResolveTargetRenderers()
		{
			if (targetRenderers != null && targetRenderers.Count > 0)
			{
				resolvedRenderers = targetRenderers.ToArray();

				return;
			}

			resolvedRenderers = GetComponentsInChildren<Renderer>(true);
		}

		/// <summary>
		/// Renders the reflection right before the given camera renders, so that it always matches the point of view that
		/// is about to be drawn.
		/// </summary>
		/// <param name="context"></param>
		/// <param name="camera"></param>
		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			// Reflecting a reflection would recurse forever, and the small cameras that draw asset previews have nothing
			// worth reflecting.
			if (isRenderingReflection || !IsReflectedCamera(camera))
				return;

			EnsureReflectionCamera();

			RenderTexture reflectionTexture = GetReflectionTexture(camera);

			UpdateReflectionCamera(camera, reflectionTexture);

			bool invertCulling = GL.invertCulling;

			// Mirroring the view matrix flips the winding of every triangle, so the culling has to be flipped back.
			GL.invertCulling = !invertCulling;

			isRenderingReflection = true;
			SetOwnRenderersVisible(false);

			try
			{
				RenderReflection(context, reflectionTexture);
			}
			finally
			{
				SetOwnRenderersVisible(true);
				isRenderingReflection = false;
				GL.invertCulling = invertCulling;
			}

			SetReflectionTexture(reflectionTexture);
		}

		/// <summary>
		/// Gets whether the given camera is one this component renders a reflection for.
		/// </summary>
		/// <param name="camera"></param>
		/// <returns></returns>
		private bool IsReflectedCamera(Camera camera)
		{
			if (camera.cameraType == CameraType.Game)
				return true;

			return renderInSceneView && camera.cameraType == CameraType.SceneView;
		}

		/// <summary>
		/// Renders the mirrored camera into the given texture.
		/// </summary>
		/// <param name="context"></param>
		/// <param name="reflectionTexture"></param>
		private void RenderReflection(ScriptableRenderContext context, RenderTexture reflectionTexture)
		{
#if UNITY_6000_0_OR_NEWER
			SubmitReflectionRenderRequest(reflectionTexture);
#else
#pragma warning disable CS0618
			UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
#pragma warning restore CS0618
#endif
		}

		/// <summary>
		/// Renders the mirrored camera into the given texture through a render request, which is what replaced
		/// RenderSingleCamera. It is compiled on every supported version so that it is checked along with the rest of the
		/// component, and only called on the versions where the call it replaces no longer exists.
		/// </summary>
		/// <param name="reflectionTexture"></param>
		private void SubmitReflectionRenderRequest(RenderTexture reflectionTexture)
		{
			reflectionRenderRequest.destination = reflectionTexture;

			reflectionCamera.SubmitRenderRequest(reflectionRenderRequest);
		}

		/// <summary>
		/// Hands the reflection to the surfaces that show it. It goes onto the renderers themselves rather than into a
		/// global, so that a second reflective plane in the same scene does not overwrite the first one.
		/// </summary>
		/// <param name="reflectionTexture"></param>
		private void SetReflectionTexture(Texture reflectionTexture)
		{
			if (resolvedRenderers == null)
				return;

			propertyBlock ??= new MaterialPropertyBlock();

			Texture texture = reflectionTexture != null ? reflectionTexture : Texture2D.blackTexture;

			for (int i = 0; i < resolvedRenderers.Length; ++i)
			{
				Renderer renderer = resolvedRenderers[i];

				if (renderer == null)
					continue;

				// Read back first, as a renderer may well carry overrides that have nothing to do with the reflection.
				renderer.GetPropertyBlock(propertyBlock);
				propertyBlock.SetTexture(PlanarReflectionTextureId, texture);
				renderer.SetPropertyBlock(propertyBlock);
			}
		}

		/// <summary>
		/// Shows or hides the surfaces this component lives on, so that the reflective surface itself stays out of its own
		/// reflection. Leaving it in makes it sample the texture it is about to write, which feeds the reflection back into
		/// itself frame after frame until the surface is a flat block of colour with nothing of the scene left in it.
		/// </summary>
		/// <param name="visible"></param>
		private void SetOwnRenderersVisible(bool visible)
		{
			if (resolvedRenderers == null)
				return;

			for (int i = 0; i < resolvedRenderers.Length; ++i)
			{
				if (resolvedRenderers[i] != null)
					resolvedRenderers[i].forceRenderingOff = !visible;
			}
		}

		/// <summary>
		/// Creates the camera that renders the reflection, which is kept hidden as it is not something to be authored.
		/// </summary>
		private void EnsureReflectionCamera()
		{
			if (reflectionCamera != null)
				return;

			GameObject go = new GameObject("Planar Reflection Camera");
			go.hideFlags = HideFlags.HideAndDontSave;

			reflectionCamera = go.AddComponent<Camera>();
			reflectionCamera.enabled = false;

			go.AddComponent<UniversalAdditionalCameraData>();
		}

		/// <summary>
		/// Gets the texture the reflection for the given camera is rendered into, creating it and recreating it whenever
		/// the resolution of that camera changes.
		/// </summary>
		/// <param name="camera"></param>
		/// <returns></returns>
		private RenderTexture GetReflectionTexture(Camera camera)
		{
			Vector2Int size = new Vector2Int(Mathf.Max(Mathf.RoundToInt(camera.pixelWidth * resolutionScale), 4),
											 Mathf.Max(Mathf.RoundToInt(camera.pixelHeight * resolutionScale), 4));

			if (reflectionTextures.TryGetValue(camera, out RenderTexture existing) && existing != null)
			{
				if (existing.width == size.x && existing.height == size.y)
					return existing;

				DestroyRenderTexture(existing);
			}

			PurgeStaleReflectionTextures();

			RenderTexture reflectionTexture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.DefaultHDR);
			reflectionTexture.name = $"Planar Reflection ({camera.name})";
			reflectionTexture.hideFlags = HideFlags.HideAndDontSave;
			reflectionTexture.useMipMap = false;
			reflectionTexture.antiAliasing = 1;
			reflectionTexture.filterMode = FilterMode.Bilinear;
			// The surfaces reading this texture offset their sample to bend the reflection with the ripples, so a sample
			// taken at the edge of the screen lands outside of it. Repeating would bring the opposite side of the frame
			// back in along the seam.
			reflectionTexture.wrapMode = TextureWrapMode.Clamp;
			reflectionTexture.Create();

			reflectionTextures[camera] = reflectionTexture;

			return reflectionTexture;
		}

		/// <summary>
		/// Releases the textures kept for cameras that no longer exist, which is what a scene view window being closed
		/// leaves behind.
		/// </summary>
		private void PurgeStaleReflectionTextures()
		{
			staleCameras.Clear();

			foreach (KeyValuePair<Camera, RenderTexture> entry in reflectionTextures)
			{
				if (entry.Key == null)
					staleCameras.Add(entry.Key);
			}

			for (int i = 0; i < staleCameras.Count; ++i)
			{
				DestroyRenderTexture(reflectionTextures[staleCameras[i]]);
				reflectionTextures.Remove(staleCameras[i]);
			}

			staleCameras.Clear();
		}

		/// <summary>
		/// Mirrors the given camera across the plane of this transform and points the reflection camera at the result.
		/// </summary>
		/// <param name="camera"></param>
		/// <param name="reflectionTexture"></param>
		private void UpdateReflectionCamera(Camera camera, RenderTexture reflectionTexture)
		{
			reflectionCamera.CopyFrom(camera);
			reflectionCamera.enabled = false;
			reflectionCamera.targetTexture = reflectionTexture;
			reflectionCamera.cullingMask = reflectionLayers;
			reflectionCamera.useOcclusionCulling = false;

			UniversalAdditionalCameraData cameraData = reflectionCamera.GetComponent<UniversalAdditionalCameraData>();
			cameraData.renderShadows = renderShadows;
			cameraData.renderPostProcessing = renderPostProcessing;
			UniversalAdditionalCameraData sourceCameraData = camera.GetComponent<UniversalAdditionalCameraData>();
			if (sourceCameraData != null)
			{
				cameraData.volumeLayerMask = sourceCameraData.volumeLayerMask;
				cameraData.volumeTrigger = sourceCameraData.volumeTrigger;
			}
			cameraData.requiresColorOption = CameraOverrideOption.On;
			cameraData.requiresDepthOption = CameraOverrideOption.On;

			Vector3 planePosition = transform.position;
			Vector3 planeNormal = transform.up;
			float planeDistance = -Vector3.Dot(planeNormal, planePosition) - clipPlaneOffset;

			Matrix4x4 reflectionMatrix = GetReflectionMatrix(new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, planeDistance));
			reflectionCamera.worldToCameraMatrix = camera.worldToCameraMatrix * reflectionMatrix;

			// Clipping against the plane itself keeps whatever is under the floor from showing up in its reflection.
			Vector4 clipPlane = GetCameraSpacePlane(reflectionCamera, planePosition, planeNormal);
			reflectionCamera.projectionMatrix = camera.CalculateObliqueMatrix(clipPlane);
			reflectionCamera.cullingMatrix = reflectionCamera.projectionMatrix * reflectionCamera.worldToCameraMatrix;

			// The mirrored camera sits below the floor, and its transform is only kept in sync for whatever reads it.
			reflectionCamera.transform.position = reflectionMatrix.MultiplyPoint(camera.transform.position);

			Transform volumeTrigger = cameraData.volumeTrigger != null ? cameraData.volumeTrigger : reflectionCamera.transform;
			VolumeManager.instance.Update(volumeTrigger, cameraData.volumeLayerMask);
		}

		/// <summary>
		/// Gets the matrix that mirrors the world across the given plane, written as its normal in xyz and its distance to
		/// the origin in w.
		/// </summary>
		/// <param name="plane"></param>
		/// <returns></returns>
		private static Matrix4x4 GetReflectionMatrix(Vector4 plane)
		{
			Matrix4x4 matrix = Matrix4x4.identity;

			matrix.m00 = 1.0f - 2.0f * plane.x * plane.x;
			matrix.m01 = -2.0f * plane.x * plane.y;
			matrix.m02 = -2.0f * plane.x * plane.z;
			matrix.m03 = -2.0f * plane.w * plane.x;

			matrix.m10 = -2.0f * plane.y * plane.x;
			matrix.m11 = 1.0f - 2.0f * plane.y * plane.y;
			matrix.m12 = -2.0f * plane.y * plane.z;
			matrix.m13 = -2.0f * plane.w * plane.y;

			matrix.m20 = -2.0f * plane.z * plane.x;
			matrix.m21 = -2.0f * plane.z * plane.y;
			matrix.m22 = 1.0f - 2.0f * plane.z * plane.z;
			matrix.m23 = -2.0f * plane.w * plane.z;

			return matrix;
		}

		/// <summary>
		/// Gets the reflection plane expressed in the space of the given camera, which is what the oblique projection needs
		/// in order to clip against it.
		/// </summary>
		/// <param name="camera"></param>
		/// <param name="planePosition"></param>
		/// <param name="planeNormal"></param>
		/// <returns></returns>
		private Vector4 GetCameraSpacePlane(Camera camera, Vector3 planePosition, Vector3 planeNormal)
		{
			Vector3 offsetPosition = planePosition + planeNormal * clipPlaneOffset;
			Matrix4x4 worldToCamera = camera.worldToCameraMatrix;

			Vector3 positionCS = worldToCamera.MultiplyPoint(offsetPosition);
			Vector3 normalCS = worldToCamera.MultiplyVector(planeNormal).normalized;

			return new Vector4(normalCS.x, normalCS.y, normalCS.z, -Vector3.Dot(positionCS, normalCS));
		}

		/// <summary>
		/// Destroys everything that was created in order to render the reflection.
		/// </summary>
		private void Cleanup()
		{
			if (reflectionCamera != null)
			{
				DestroyObject(reflectionCamera.gameObject);
				reflectionCamera = null;
			}

			foreach (KeyValuePair<Camera, RenderTexture> entry in reflectionTextures)
				DestroyRenderTexture(entry.Value);

			reflectionTextures.Clear();
		}

		/// <summary>
		/// Releases one of the textures the reflection is rendered into.
		/// </summary>
		/// <param name="reflectionTexture"></param>
		private static void DestroyRenderTexture(RenderTexture reflectionTexture)
		{
			if (reflectionTexture == null)
				return;

			reflectionTexture.Release();
			DestroyObject(reflectionTexture);
		}

		/// <summary>
		/// Destroys the given object with whichever call is the valid one outside and inside of play mode.
		/// </summary>
		/// <param name="obj"></param>
		private static void DestroyObject(Object obj)
		{
			if (Application.isPlaying)
				Destroy(obj);
			else
				DestroyImmediate(obj);
		}

	#endregion
	}
}
