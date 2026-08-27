using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Multiplies the shade laid down by the hand placed occluders onto the opaque surfaces of the frame. It is not an
/// approximation of anything: where the shapes are is where the image gets darker, which turns occlusion from
/// something a programmer configures once into something an artist composes with, shot by shot.
/// </summary>
[DisallowMultipleRendererFeature("Analytic Ambient Occlusion")]
public sealed class AnalyticAmbientOcclusionRendererFeature : ScriptableRendererFeature
{
	#region Definitions

	/// <summary>
	/// The settings of the analytic occlusion.
	/// </summary>
	[System.Serializable]
	public sealed class Settings
	{
		[Tooltip("A master multiplier over every occluder in the scene, so that the whole layer can be dialed back or turned off without touching what has been placed.")]
		[Range(0.0f, 1.0f)]
		public float intensity = 1.0f;
		[Tooltip("When the pass runs. After the opaque geometry is where the surfaces it darkens have just been drawn, and it is early enough for the volumetric fog to still sit on top of the result.")]
		public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
	}

	/// <summary>
	/// The render pass that lays the shade down.
	/// </summary>
	private sealed class AnalyticOcclusionPass : ScriptableRenderPass
	{
		#region Private Attributes

		private static readonly int OccluderSpheresId = Shader.PropertyToID("_AnalyticOccluderSpheres");
		private static readonly int OccluderAxesId = Shader.PropertyToID("_AnalyticOccluderAxes");
		private static readonly int OccluderParamsId = Shader.PropertyToID("_AnalyticOccluderParams");
		private static readonly int OccluderCountId = Shader.PropertyToID("_AnalyticOccluderCount");
		private static readonly int OcclusionIntensityId = Shader.PropertyToID("_AnalyticOcclusionIntensity");

		// The fullscreen triangle comes from Blit.hlsl, which scales its texture coordinates by this. Blitter is
		// what normally fills it in, and this pass draws the triangle itself, so it has to fill it in as well.
		private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

		private const string ProfilerTag = "Analytic Ambient Occlusion";

		// The material locks the length of these arrays on the first upload, so they are allocated once and only their
		// first entries, the ones holding the occluders that are actually rendered, are filled every frame.
		private static readonly Vector4[] OccluderSpheres = new Vector4[MaxOccluders];
		private static readonly Vector4[] OccluderAxes = new Vector4[MaxOccluders];
		private static readonly Vector4[] OccluderParams = new Vector4[MaxOccluders];

		private readonly Material material;
		private readonly Settings settings;

		private RTHandle source;

		#endregion

		#region Initialization Methods

		public AnalyticOcclusionPass(Material material, Settings settings)
		{
			this.material = material;
			this.settings = settings;

			profilingSampler = new ProfilingSampler(ProfilerTag);

			// The shade is placed in world space, so the pass needs the depth to find the surfaces and the normals to
			// tell which of them are actually facing a shape.
			ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
		}

		#endregion

		#region Methods

		/// <summary>
		/// Sets the color target that the shade is multiplied onto.
		/// </summary>
		/// <param name="source"></param>
		public void SetSource(RTHandle source)
		{
			this.source = source;
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="context"></param>
		/// <param name="renderingData"></param>
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			if (material == null || source == null)
				return;

			Vector3 cameraPositionWS = renderingData.cameraData.camera.transform.position;
			List<AnalyticOccluder> occluders = AnalyticOccluder.GetClosestActiveOccluders(cameraPositionWS, MaxOccluders);
			int count = occluders.Count;

			if (count == 0 || settings.intensity <= 0.0f)
				return;

			for (int i = 0; i < count; ++i)
			{
				AnalyticOccluder occluder = occluders[i];

				Vector3 center = occluder.transform.position;
				Vector3 halfAxis = occluder.WorldHalfAxis;

				OccluderSpheres[i] = new Vector4(center.x, center.y, center.z, occluder.WorldRadius);
				OccluderAxes[i] = new Vector4(halfAxis.x, halfAxis.y, halfAxis.z, occluder.intensity);
				OccluderParams[i] = new Vector4(occluder.falloff, occluder.directionality, 0.0f, 0.0f);
			}

			CommandBuffer cmd = CommandBufferPool.Get();

			using (new ProfilingScope(cmd, profilingSampler))
			{
				material.SetVectorArray(OccluderSpheresId, OccluderSpheres);
				material.SetVectorArray(OccluderAxesId, OccluderAxes);
				material.SetVectorArray(OccluderParamsId, OccluderParams);
				material.SetFloat(OccluderCountId, count);
				material.SetFloat(OcclusionIntensityId, settings.intensity);
				material.SetVector(BlitScaleBiasId, new Vector4(1.0f, 1.0f, 0.0f, 0.0f));

				// The pass multiplies onto the target it is drawing into, so it never has to read the color back.
				CoreUtils.SetRenderTarget(cmd, source);
				CoreUtils.DrawFullScreen(cmd, material, null, 0);
			}

			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			CommandBufferPool.Release(cmd);
		}

		#endregion
	}

	#endregion

	#region Public Attributes

	public Settings settings = new Settings();

	#endregion

	#region Private Attributes

	private const string ShaderName = "Hidden/Inside/AnalyticAmbientOcclusion";

	// It has to be kept in sync with its counterpart from AnalyticAmbientOcclusion.shader.
	private const int MaxOccluders = 16;

	private Material material;
	private AnalyticOcclusionPass occlusionPass;

	#endregion

	#region ScriptableRendererFeature Methods

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	public override void Create()
	{
		Shader shader = Shader.Find(ShaderName);

		if (shader == null)
			return;

		if (material == null)
			material = CoreUtils.CreateEngineMaterial(shader);

		occlusionPass = new AnalyticOcclusionPass(material, settings);
		occlusionPass.renderPassEvent = settings.renderPassEvent;
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="renderer"></param>
	/// <param name="renderingData"></param>
	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		if (occlusionPass == null || material == null || settings.intensity <= 0.0f)
			return;

		if (renderingData.cameraData.cameraType == CameraType.Preview)
			return;

		occlusionPass.renderPassEvent = settings.renderPassEvent;
		renderer.EnqueuePass(occlusionPass);
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="renderer"></param>
	/// <param name="renderingData"></param>
	public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
	{
		if (occlusionPass == null)
			return;

		occlusionPass.SetSource(renderer.cameraColorTargetHandle);
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="disposing"></param>
	protected override void Dispose(bool disposing)
	{
		CoreUtils.Destroy(material);

		material = null;
		occlusionPass = null;
	}

	#endregion
}
