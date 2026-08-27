using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Adds the light thrown by the hand placed bounce shapes onto the opaque surfaces of the frame. It is the counterpart
/// of the analytic occlusion pass: one adds the indirect light that a lightmap would have baked and the other takes
/// away the occlusion it would have baked with it, and neither of them is baked, so both survive a lighting preset
/// changing underneath them at runtime.
///
/// It has to run before the occlusion pass, so that the shade lands on the bounce as well as on everything else.
/// </summary>
[DisallowMultipleRendererFeature("Inside Bounce Light")]
public sealed class InsideBounceLightRendererFeature : ScriptableRendererFeature
{
	#region Definitions

	/// <summary>
	/// The settings of the bounce light.
	/// </summary>
	[System.Serializable]
	public sealed class Settings
	{
		[Tooltip("A master multiplier over every bounce light in the scene, so that the whole layer can be dialed back or turned off without touching what has been placed.")]
		[Min(0.0f)]
		public float intensity = 1.0f;
		[Tooltip("When the pass runs. After the opaque geometry is where the surfaces it lights have just been drawn, and it is early enough for the occlusion pass and then the volumetric fog to still sit on top of the result.")]
		public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
	}

	/// <summary>
	/// The render pass that adds the bounce.
	/// </summary>
	private sealed class BounceLightPass : ScriptableRenderPass
	{
		#region Private Attributes

		private static readonly int LocalToWorldId = Shader.PropertyToID("_InsideBounceLocalToWorld");
		private static readonly int WorldToLocalId = Shader.PropertyToID("_InsideBounceWorldToLocal");
		private static readonly int ColorsId = Shader.PropertyToID("_InsideBounceColors");
		private static readonly int ParamsId = Shader.PropertyToID("_InsideBounceParams");
		private static readonly int CountId = Shader.PropertyToID("_InsideBounceCount");
		private static readonly int IntensityId = Shader.PropertyToID("_InsideBounceIntensity");

		// The fullscreen triangle comes from Blit.hlsl, which scales its texture coordinates by this. Blitter is what
		// normally fills it in, and this pass draws the triangle itself, so it has to fill it in as well.
		private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

		private const string ProfilerTag = "Inside Bounce Light";

		// The material locks the length of these arrays on the first upload, so they are allocated once and only their
		// first entries, the ones holding the lights that are actually rendered, are filled every frame.
		private static readonly Matrix4x4[] LocalToWorld = new Matrix4x4[MaxBounceLights];
		private static readonly Matrix4x4[] WorldToLocal = new Matrix4x4[MaxBounceLights];
		private static readonly Vector4[] Colors = new Vector4[MaxBounceLights];
		private static readonly Vector4[] Params = new Vector4[MaxBounceLights];

		private readonly Material material;
		private readonly Settings settings;

		private RTHandle source;

		#endregion

		#region Initialization Methods

		public BounceLightPass(Material material, Settings settings)
		{
			this.material = material;
			this.settings = settings;

			profilingSampler = new ProfilingSampler(ProfilerTag);

			// The light is placed in world space, so the pass needs the depth to find the surfaces and the normals to
			// shade them with.
			ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
		}

		#endregion

		#region Methods

		/// <summary>
		/// Sets the color target that the bounce is added onto.
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
			List<InsideBounceLight> lights = InsideBounceLight.GetClosestActiveLights(cameraPositionWS, MaxBounceLights);
			int count = lights.Count;

			if (count == 0 || settings.intensity <= 0.0f)
				return;

			for (int i = 0; i < count; ++i)
			{
				InsideBounceLight light = lights[i];
				Color color = light.color;

				LocalToWorld[i] = light.LocalToWorld;
				WorldToLocal[i] = light.WorldToLocal;
				Colors[i] = new Vector4(color.r, color.g, color.b, light.intensity);
				Params[i] = new Vector4((float)(int)light.shape, light.wrap, light.reach, 0.0f);
			}

			CommandBuffer cmd = CommandBufferPool.Get();

			using (new ProfilingScope(cmd, profilingSampler))
			{
				material.SetMatrixArray(LocalToWorldId, LocalToWorld);
				material.SetMatrixArray(WorldToLocalId, WorldToLocal);
				material.SetVectorArray(ColorsId, Colors);
				material.SetVectorArray(ParamsId, Params);
				material.SetFloat(CountId, count);
				material.SetFloat(IntensityId, settings.intensity);
				material.SetVector(BlitScaleBiasId, new Vector4(1.0f, 1.0f, 0.0f, 0.0f));

				// The pass adds onto the target it is drawing into, so it never has to read the color back.
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

	private const string ShaderName = "Hidden/Inside/BounceLight";

	// It has to be kept in sync with its counterpart from InsideBounceLight.shader.
	private const int MaxBounceLights = 8;

	private Material material;
	private BounceLightPass bouncePass;

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

		bouncePass = new BounceLightPass(material, settings);
		bouncePass.renderPassEvent = settings.renderPassEvent;
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="renderer"></param>
	/// <param name="renderingData"></param>
	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		if (bouncePass == null || material == null || settings.intensity <= 0.0f)
			return;

		if (renderingData.cameraData.cameraType == CameraType.Preview)
			return;

		bouncePass.renderPassEvent = settings.renderPassEvent;
		renderer.EnqueuePass(bouncePass);
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="renderer"></param>
	/// <param name="renderingData"></param>
	public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
	{
		if (bouncePass == null)
			return;

		bouncePass.SetSource(renderer.cameraColorTargetHandle);
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="disposing"></param>
	protected override void Dispose(bool disposing)
	{
		CoreUtils.Destroy(material);

		material = null;
		bouncePass = null;
	}

	#endregion
}
