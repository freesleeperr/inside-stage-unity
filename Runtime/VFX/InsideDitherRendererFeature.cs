using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Dithers the image and lays film grain on top of it, once everything else has already been rendered. It is what
/// keeps the long and dark gradients of a moody image from showing the steps of an 8 bit target, and what stops the
/// shadows from reading as flat, empty black.
/// </summary>
[DisallowMultipleRendererFeature("Inside Dither")]
public sealed class InsideDitherRendererFeature : ScriptableRendererFeature
{
	#region Definitions

	/// <summary>
	/// The settings of the dithering and the grain.
	/// </summary>
	[System.Serializable]
	public sealed class Settings
	{
		[Tooltip("How much the ordered dithering spreads the banding out. One covers a bit more than a single step of an 8 bit target, which is what a gradient needs in order to stop showing its steps.")]
		[Range(0.0f, 4.0f)]
		public float ditherIntensity = 1.0f;
		[Tooltip("How much grain is laid on top of the image. Small values already read clearly once the image is in motion.")]
		[Range(0.0f, 0.2f)]
		public float grainIntensity = 0.025f;
		[Tooltip("How much the grain is pushed towards the shadows. At zero it is spread evenly across the image, while at one it nearly disappears from the brightest parts.")]
		[Range(0.0f, 1.0f)]
		public float grainShadowBias = 0.85f;
		[Tooltip("How sharply the grain falls off as the image gets brighter. Higher values confine it to the deepest shadows.")]
		[Range(0.5f, 6.0f)]
		public float grainResponse = 2.0f;
		[Tooltip("Whether the grain moves from frame to frame. A still grain reads as texture on the screen instead of as grain in the image.")]
		public bool animateGrain = true;
		[Tooltip("When the pass runs. After post processing is where the banding that has to be broken up actually appears.")]
		public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
	}

	/// <summary>
	/// The render pass that dithers the image.
	/// </summary>
	private sealed class DitherPass : ScriptableRenderPass
	{
		#region Private Attributes

		private static readonly int DitherIntensityId = Shader.PropertyToID("_DitherIntensity");
		private static readonly int GrainIntensityId = Shader.PropertyToID("_GrainIntensity");
		private static readonly int GrainShadowBiasId = Shader.PropertyToID("_GrainShadowBias");
		private static readonly int GrainResponseId = Shader.PropertyToID("_GrainResponse");
		private static readonly int FrameIndexId = Shader.PropertyToID("_FrameIndex");

		private const string ProfilerTag = "Inside Dither";

		private readonly Material material;
		private readonly Settings settings;

		private RTHandle source;
		private RTHandle temporaryTarget;

		#endregion

		#region Initialization Methods

		public DitherPass(Material material, Settings settings)
		{
			this.material = material;
			this.settings = settings;

			profilingSampler = new ProfilingSampler(ProfilerTag);
		}

		#endregion

		#region Methods

		/// <summary>
		/// Sets the color target that is dithered, which is only known once the renderer has been set up.
		/// </summary>
		/// <param name="source"></param>
		public void SetSource(RTHandle source)
		{
			this.source = source;
		}

		/// <summary>
		/// Releases the temporary target that the pass blits through.
		/// </summary>
		public void Dispose()
		{
			temporaryTarget?.Release();
			temporaryTarget = null;
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="cmd"></param>
		/// <param name="renderingData"></param>
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
			descriptor.depthBufferBits = 0;
			descriptor.msaaSamples = 1;

			RenderingUtils.ReAllocateIfNeeded(ref temporaryTarget, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_InsideDitherTarget");
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="context"></param>
		/// <param name="renderingData"></param>
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			if (material == null || source == null || temporaryTarget == null)
				return;

			CommandBuffer cmd = CommandBufferPool.Get();

			using (new ProfilingScope(cmd, profilingSampler))
			{
				material.SetFloat(DitherIntensityId, settings.ditherIntensity);
				material.SetFloat(GrainIntensityId, settings.grainIntensity);
				material.SetFloat(GrainShadowBiasId, settings.grainShadowBias);
				material.SetFloat(GrainResponseId, settings.grainResponse);
				material.SetFloat(FrameIndexId, settings.animateGrain ? Time.renderedFrameCount % 64 : 0.0f);

				// The image cannot be read and written at the same time, so it takes a round trip through a target of
				// its own to get back where it came from.
				Blitter.BlitCameraTexture(cmd, source, temporaryTarget, material, 0);
				Blitter.BlitCameraTexture(cmd, temporaryTarget, source);
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

	private const string ShaderName = "Hidden/Inside/Dither";

	private Material material;
	private DitherPass ditherPass;

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

		ditherPass = new DitherPass(material, settings);
		ditherPass.renderPassEvent = settings.renderPassEvent;
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="renderer"></param>
	/// <param name="renderingData"></param>
	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		if (ditherPass == null || material == null)
			return;

		CameraType cameraType = renderingData.cameraData.cameraType;

		// The reflection and the preview cameras render tiny images that nobody looks at closely enough for the
		// banding to matter, and graining them would only add noise to what the main camera reflects.
		if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
			return;

		if (settings.ditherIntensity <= 0.0f && settings.grainIntensity <= 0.0f)
			return;

		ditherPass.renderPassEvent = settings.renderPassEvent;
		renderer.EnqueuePass(ditherPass);
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="renderer"></param>
	/// <param name="renderingData"></param>
	public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
	{
		if (ditherPass == null)
			return;

		ditherPass.SetSource(renderer.cameraColorTargetHandle);
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="disposing"></param>
	protected override void Dispose(bool disposing)
	{
		ditherPass?.Dispose();
		CoreUtils.Destroy(material);

		material = null;
		ditherPass = null;
	}

	#endregion
}
