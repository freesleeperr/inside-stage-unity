using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders the scene mirrored across the plane defined by this transform into a texture, so that a flat surface can
/// show a real reflection of what stands on it. It is what turns a floor into a wet one, which is the cheapest way of
/// getting the foreground silhouettes to read twice in the frame.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Planar Reflection")]
public sealed class PlanarReflection : MonoBehaviour
{
	#region Public Attributes

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

	#endregion

	#region Private Attributes

	private static readonly int PlanarReflectionTextureId = Shader.PropertyToID("_PlanarReflectionTexture");

	private Camera reflectionCamera;
	private RenderTexture reflectionTexture;
	private Vector2Int reflectionTextureSize;

	#endregion

	#region MonoBehaviour Methods

	private void OnEnable()
	{
		RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
	}

	private void OnDisable()
	{
		RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
		Cleanup();
	}

#if UNITY_EDITOR

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
	/// Renders the reflection right before the given camera renders, so that it always matches the point of view that
	/// is about to be drawn.
	/// </summary>
	/// <param name="context"></param>
	/// <param name="camera"></param>
	private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
	{
		// Reflecting a reflection would recurse forever, and the small cameras that draw asset previews have nothing
		// worth reflecting.
		if (camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
			return;

		EnsureReflectionCamera();
		EnsureReflectionTexture(camera);
		UpdateReflectionCamera(camera);

		bool invertCulling = GL.invertCulling;

		// Mirroring the view matrix flips the winding of every triangle, so the culling has to be flipped back.
		GL.invertCulling = !invertCulling;
#pragma warning disable CS0618
		UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
#pragma warning restore CS0618
		GL.invertCulling = invertCulling;

		Shader.SetGlobalTexture(PlanarReflectionTextureId, reflectionTexture);
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
	/// Creates the texture that the reflection is rendered into, and recreates it whenever the resolution of the
	/// camera changes.
	/// </summary>
	/// <param name="camera"></param>
	private void EnsureReflectionTexture(Camera camera)
	{
		Vector2Int size = new Vector2Int(Mathf.Max(Mathf.RoundToInt(camera.pixelWidth * resolutionScale), 4),
										 Mathf.Max(Mathf.RoundToInt(camera.pixelHeight * resolutionScale), 4));

		if (reflectionTexture != null && reflectionTextureSize == size)
			return;

		DestroyRenderTexture();

		reflectionTextureSize = size;
		reflectionTexture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.DefaultHDR);
		reflectionTexture.name = "Planar Reflection";
		reflectionTexture.hideFlags = HideFlags.HideAndDontSave;
		reflectionTexture.useMipMap = false;
		reflectionTexture.antiAliasing = 1;
		reflectionTexture.Create();
	}

	/// <summary>
	/// Mirrors the given camera across the plane of this transform and points the reflection camera at the result.
	/// </summary>
	/// <param name="camera"></param>
	private void UpdateReflectionCamera(Camera camera)
	{
		reflectionCamera.CopyFrom(camera);
		reflectionCamera.enabled = false;
		reflectionCamera.targetTexture = reflectionTexture;
		reflectionCamera.cullingMask = reflectionLayers;
		reflectionCamera.useOcclusionCulling = false;

		UniversalAdditionalCameraData cameraData = reflectionCamera.GetComponent<UniversalAdditionalCameraData>();
		cameraData.renderShadows = renderShadows;
		cameraData.renderPostProcessing = renderPostProcessing;
		cameraData.requiresColorOption = CameraOverrideOption.Off;
		cameraData.requiresDepthOption = CameraOverrideOption.Off;

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

		DestroyRenderTexture();
	}

	/// <summary>
	/// Releases the texture that the reflection is rendered into.
	/// </summary>
	private void DestroyRenderTexture()
	{
		if (reflectionTexture == null)
			return;

		reflectionTexture.Release();
		DestroyObject(reflectionTexture);

		reflectionTexture = null;
		reflectionTextureSize = Vector2Int.zero;
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
