using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// Evaluates the hand placed indirect lighting of the scene: the light the <see cref="InsideBounceLight"/> shapes
	/// throw back onto the frame and the shade the <see cref="AnalyticOccluder"/> shapes take out of it. Neither is
	/// derived from the geometry and neither is baked, which is the whole point of them: they are shapes an artist
	/// composes with, and they keep working when the lamp moves, when the lighting preset changes and when the room is
	/// rebuilt.
	///
	/// It replaces the two separate fullscreen passes those two used to be. Each of them reconstructed the world
	/// position and the normal of every pixel of the frame before it could do anything with them, and the frame paid for
	/// that twice.
	///
	/// The shade is handed to the pipeline as <c>_ScreenSpaceOcclusionTexture</c> so that it lands on the indirect term
	/// of every surface as it is shaded, rather than being multiplied over the finished image where it darkened the
	/// emissive surfaces, the sky and the already computed direct light along with everything else. The bounce has no
	/// equivalent hook, so it is still composited, but it is only ever added where a surface was actually found.
	/// </summary>
	[DisallowMultipleRendererFeature("Inside Indirect Lighting")]
	public sealed class InsideIndirectLightingRendererFeature : ScriptableRendererFeature
	{
	#region Definitions

		/// <summary>
		/// The resolution the indirect lighting is evaluated at, as a divisor of the resolution of the camera.
		/// </summary>
		public enum IndirectLightingResolution : byte
		{
			Full = 1,
			Half = 2
		}

		/// <summary>
		/// The settings of the hand placed indirect lighting.
		/// </summary>
		[Serializable]
		public sealed class Settings
		{
			[Tooltip("A master multiplier over every occluder in the scene, so that the whole layer of shade can be dialed back or turned off without touching what has been placed.")]
			[Range(0.0f, 1.0f)]
			public float occlusionIntensity = 1.0f;
			[Tooltip("How much of the shade also falls on the light that reaches a surface directly. At zero the occluders only take away the ambient and the bounce, which is where shade physically belongs, and at one they darken the lit side of an object as well, which is blunter but reads more strongly in a dark room.")]
			[Range(0.0f, 1.0f)]
			public float occlusionDirectLightingStrength = 0.25f;
			[Tooltip("A master multiplier over every bounce light in the scene, so that the whole layer of light can be dialed back or turned off without touching what has been placed.")]
			[Min(0.0f)]
			public float bounceIntensity = 1.0f;
			[Tooltip("The resolution the shade and the bounce are evaluated at. Both are broad and soft, so halving the resolution is rarely visible and takes three quarters of the pixels out of the pass.")]
			public IndirectLightingResolution resolution = IndirectLightingResolution.Full;
			[Tooltip("When the bounce is added onto the frame. After the opaque geometry is where the surfaces it lights have just been drawn, and it is early enough for the volumetric fog to still sit on top of the result. The shade is not affected by this, as it has to be ready before anything is shaded at all.")]
			public RenderPassEvent bounceRenderPassEvent = RenderPassEvent.AfterRenderingOpaques;
		}

		/// <summary>
		/// What the evaluation pass leaves behind for the composition pass to pick up, as the two of them run at
		/// different points of the frame and only the first of them knows whether there is anything to compose.
		/// </summary>
		private sealed class PassState
		{
			public RTHandle target;
			public bool hasBounce;
		}

		/// <summary>
		/// The render pass that evaluates both halves of the indirect lighting into a single target, before anything
		/// opaque has been shaded.
		/// </summary>
		private sealed class IndirectLightingEvaluatePass : ScriptableRenderPass
		{
		#region Private Attributes

			private static readonly int OccluderSpheresId = Shader.PropertyToID("_AnalyticOccluderSpheres");
			private static readonly int OccluderAxesId = Shader.PropertyToID("_AnalyticOccluderAxes");
			private static readonly int OccluderParamsId = Shader.PropertyToID("_AnalyticOccluderParams");
			private static readonly int OccluderCountId = Shader.PropertyToID("_AnalyticOccluderCount");
			private static readonly int OcclusionIntensityId = Shader.PropertyToID("_AnalyticOcclusionIntensity");

			private static readonly int BounceLocalToWorldId = Shader.PropertyToID("_InsideBounceLocalToWorld");
			private static readonly int BounceWorldToLocalId = Shader.PropertyToID("_InsideBounceWorldToLocal");
			private static readonly int BounceColorsId = Shader.PropertyToID("_InsideBounceColors");
			private static readonly int BounceParamsId = Shader.PropertyToID("_InsideBounceParams");
			private static readonly int BounceBoundsId = Shader.PropertyToID("_InsideBounceBounds");
			private static readonly int BounceCountId = Shader.PropertyToID("_InsideBounceCount");
			private static readonly int BounceIntensityId = Shader.PropertyToID("_InsideBounceIntensity");

			// The two properties the pipeline itself reads. Filling them in is what gets the shade applied to the
			// indirect term of a surface instead of to the image the surface ended up in.
			private static readonly int ScreenSpaceOcclusionTextureId = Shader.PropertyToID("_ScreenSpaceOcclusionTexture");
			private static readonly int AmbientOcclusionParamId = Shader.PropertyToID("_AmbientOcclusionParam");

			// The fullscreen triangle comes from Blit.hlsl, which scales its texture coordinates by this. Blitter is
			// what normally fills it in, and this pass draws the triangle itself, so it has to fill it in as well.
			private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

			private const string ProfilerTag = "Inside Indirect Lighting";
			private const string TargetName = "_InsideIndirectLightingTexture";

			// The material locks the length of these arrays on the first upload, so they are allocated once and only
			// their first entries, the ones holding the shapes that are actually rendered, are filled every frame.
			private static readonly Vector4[] OccluderSpheres = new Vector4[MaxOccluders];
			private static readonly Vector4[] OccluderAxes = new Vector4[MaxOccluders];
			private static readonly Vector4[] OccluderParams = new Vector4[MaxOccluders];

			private static readonly Matrix4x4[] BounceLocalToWorld = new Matrix4x4[MaxBounceLights];
			private static readonly Matrix4x4[] BounceWorldToLocal = new Matrix4x4[MaxBounceLights];
			private static readonly Vector4[] BounceColors = new Vector4[MaxBounceLights];
			private static readonly Vector4[] BounceParams = new Vector4[MaxBounceLights];
			private static readonly Vector4[] BounceBounds = new Vector4[MaxBounceLights];

			// Reused every frame, so that culling the shapes against the frame costs nothing in garbage.
			private static readonly Plane[] FrustumPlanes = new Plane[6];

			private readonly Material material;
			private readonly Settings settings;
			private readonly PassState state;

		#endregion

		#region Initialization Methods

			public IndirectLightingEvaluatePass(Material material, Settings settings, PassState state)
			{
				this.material = material;
				this.settings = settings;
				this.state = state;

				profilingSampler = new ProfilingSampler(ProfilerTag);

				// The shade has to exist before the first opaque surface is shaded, as the whole point of handing it to
				// the pipeline is that the pipeline applies it while it shades.
				renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;

				// Both halves are placed in world space, so the pass needs the depth to find the surfaces and the
				// normals to tell which way they face.
				ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
			}

		#endregion

		#region Methods

			/// <summary>
			/// Releases the target both halves are evaluated into.
			/// </summary>
			public void Dispose()
			{
				state.target?.Release();
				state.target = null;
			}

			/// <summary>
			/// <inheritdoc/>
			/// </summary>
			/// <param name="cmd"></param>
			/// <param name="renderingData"></param>
			public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
			{
				RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
				int divisor = (int)settings.resolution;

				descriptor.width = Mathf.Max(descriptor.width / divisor, 1);
				descriptor.height = Mathf.Max(descriptor.height / divisor, 1);
				descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
				descriptor.depthBufferBits = 0;
				descriptor.msaaSamples = 1;

				// Bilinear, as the pipeline samples this with the screen space coordinates of whatever surface it is
				// shading and a half resolution target has to be filtered back up on the way.
				RenderingUtils.ReAllocateIfNeeded(ref state.target, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: TargetName);
			}

			/// <summary>
			/// <inheritdoc/>
			/// </summary>
			/// <param name="context"></param>
			/// <param name="renderingData"></param>
			public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
			{
				state.hasBounce = false;

				if (material == null || state.target == null)
					return;

				Camera camera = renderingData.cameraData.camera;
				Vector3 cameraPositionWS = camera.transform.position;

				// Culling against the frame first is what keeps a shape that is behind the camera or off to the side of
				// it from taking one of the slots, and from being looped over by every pixel that is drawn.
				GeometryUtility.CalculateFrustumPlanes(camera.projectionMatrix * camera.worldToCameraMatrix, FrustumPlanes);

				int occluderCount = settings.occlusionIntensity > 0.0f ? FillOccluders(cameraPositionWS) : 0;
				int bounceCount = settings.bounceIntensity > 0.0f ? FillBounceLights(cameraPositionWS) : 0;

				state.hasBounce = bounceCount > 0;

				CommandBuffer cmd = CommandBufferPool.Get();

				using (new ProfilingScope(cmd, profilingSampler))
				{
					// Set either way, as another camera rendered earlier in the frame may have left it on.
					CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, occluderCount > 0);

					if (occluderCount > 0 || bounceCount > 0)
					{
						material.SetVectorArray(OccluderSpheresId, OccluderSpheres);
						material.SetVectorArray(OccluderAxesId, OccluderAxes);
						material.SetVectorArray(OccluderParamsId, OccluderParams);
						material.SetFloat(OccluderCountId, occluderCount);
						material.SetFloat(OcclusionIntensityId, settings.occlusionIntensity);

						material.SetMatrixArray(BounceLocalToWorldId, BounceLocalToWorld);
						material.SetMatrixArray(BounceWorldToLocalId, BounceWorldToLocal);
						material.SetVectorArray(BounceColorsId, BounceColors);
						material.SetVectorArray(BounceParamsId, BounceParams);
						material.SetVectorArray(BounceBoundsId, BounceBounds);
						material.SetFloat(BounceCountId, bounceCount);
						material.SetFloat(BounceIntensityId, settings.bounceIntensity);

						material.SetVector(BlitScaleBiasId, new Vector4(1.0f, 1.0f, 0.0f, 0.0f));

						CoreUtils.SetRenderTarget(cmd, state.target);
						CoreUtils.DrawFullScreen(cmd, material, null, EvaluatePassIndex);
					}

					if (occluderCount > 0)
					{
						cmd.SetGlobalTexture(ScreenSpaceOcclusionTextureId, state.target);

						// x scales the shade the pipeline reads back, and one leaves what was written alone. w is how
						// much of it also falls on the direct light.
						cmd.SetGlobalVector(AmbientOcclusionParamId, new Vector4(1.0f, 0.0f, 0.0f, settings.occlusionDirectLightingStrength));
					}
				}

				context.ExecuteCommandBuffer(cmd);
				cmd.Clear();
				CommandBufferPool.Release(cmd);
			}

			/// <summary>
			/// <inheritdoc/>
			/// </summary>
			/// <param name="cmd"></param>
			public override void OnCameraCleanup(CommandBuffer cmd)
			{
				// The keyword is global, so leaving it on would have the next camera shade against whatever this one
				// happened to leave in the target.
				CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, false);
			}

			/// <summary>
			/// Fills the occluder arrays with the shapes that are visible from the given position, and returns how many
			/// of them there were.
			/// </summary>
			/// <param name="cameraPositionWS"></param>
			/// <returns></returns>
			private int FillOccluders(Vector3 cameraPositionWS)
			{
				List<AnalyticOccluder> occluders = AnalyticOccluder.GetClosestActiveOccluders(cameraPositionWS, FrustumPlanes, MaxOccluders);
				int count = occluders.Count;

				for (int i = 0; i < count; ++i)
				{
					AnalyticOccluder occluder = occluders[i];

					Vector3 center = occluder.transform.position;
					Vector3 halfAxis = occluder.WorldHalfAxis;

					OccluderSpheres[i] = new Vector4(center.x, center.y, center.z, occluder.WorldRadius);
					OccluderAxes[i] = new Vector4(halfAxis.x, halfAxis.y, halfAxis.z, occluder.intensity);
					OccluderParams[i] = new Vector4(occluder.falloff, occluder.directionality, 0.0f, 0.0f);
				}

				return count;
			}

			/// <summary>
			/// Fills the bounce light arrays with the shapes that are visible from the given position, and returns how
			/// many of them there were.
			/// </summary>
			/// <param name="cameraPositionWS"></param>
			/// <returns></returns>
			private int FillBounceLights(Vector3 cameraPositionWS)
			{
				List<InsideBounceLight> lights = InsideBounceLight.GetClosestActiveLights(cameraPositionWS, FrustumPlanes, MaxBounceLights);
				int count = lights.Count;

				for (int i = 0; i < count; ++i)
				{
					InsideBounceLight light = lights[i];

					Color color = light.color;
					Vector3 center = light.BoundingCenter;

					BounceLocalToWorld[i] = light.LocalToWorld;
					BounceWorldToLocal[i] = light.WorldToLocal;
					BounceColors[i] = new Vector4(color.r, color.g, color.b, light.intensity);
					BounceParams[i] = new Vector4((float)(int)light.shape, light.wrap, light.reach, 0.0f);
					BounceBounds[i] = new Vector4(center.x, center.y, center.z, light.BoundingRadius);
				}

				return count;
			}

		#endregion
		}

		/// <summary>
		/// The render pass that adds the bounce onto the frame, once the surfaces it lights have been drawn.
		/// </summary>
		private sealed class BounceCompositePass : ScriptableRenderPass
		{
		#region Private Attributes

			private static readonly int IndirectLightingTextureId = Shader.PropertyToID("_InsideIndirectLightingTexture");
			private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

			private const string ProfilerTag = "Inside Bounce Light Composition";

			private readonly Material material;
			private readonly PassState state;

			private RTHandle source;

		#endregion

		#region Initialization Methods

			public BounceCompositePass(Material material, PassState state)
			{
				this.material = material;
				this.state = state;

				profilingSampler = new ProfilingSampler(ProfilerTag);
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
				if (material == null || source == null || state.target == null || !state.hasBounce)
					return;

				CommandBuffer cmd = CommandBufferPool.Get();

				using (new ProfilingScope(cmd, profilingSampler))
				{
					material.SetVector(BlitScaleBiasId, new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
					cmd.SetGlobalTexture(IndirectLightingTextureId, state.target);

					// The pass adds onto the target it is drawing into, so it never has to read the color back.
					CoreUtils.SetRenderTarget(cmd, source);
					CoreUtils.DrawFullScreen(cmd, material, null, CompositePassIndex);
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

		private const string ShaderName = "Hidden/Inside/IndirectLighting";

		private const int EvaluatePassIndex = 0;
		private const int CompositePassIndex = 1;

		// They have to be kept in sync with their counterparts from InsideIndirectLighting.hlsl.
		private const int MaxOccluders = 16;
		private const int MaxBounceLights = 8;

		// Serialized rather than looked up at runtime. Shader.Find only sees the shaders that made it into the build,
		// and nothing references a hidden shader, so it is found in the editor and missing in a player. Holding the
		// reference on the feature is what pulls the shader into the build in the first place.
		[HideInInspector]
		[SerializeField] private Shader indirectLightingShader;

		// One material per pass. They are recorded into the same command buffer and a material only carries the values
		// it holds when the buffer is submitted, so sharing one between the two would have each of them execute with
		// whatever the other set last.
		private Material evaluateMaterial;
		private Material compositeMaterial;

		private PassState state;
		private IndirectLightingEvaluatePass evaluatePass;
		private BounceCompositePass compositePass;

	#endregion

	#region ScriptableRendererFeature Methods

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		public override void Create()
		{
			if (!ValidateResources(true))
				return;

			state = new PassState();
			evaluatePass = new IndirectLightingEvaluatePass(evaluateMaterial, settings, state);
			compositePass = new BounceCompositePass(compositeMaterial, state);
			compositePass.renderPassEvent = settings.bounceRenderPassEvent;
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="renderer"></param>
		/// <param name="renderingData"></param>
		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (evaluatePass == null || compositePass == null)
				return;

			if (!ShouldRenderForCamera(in renderingData.cameraData))
				return;

			if (settings.occlusionIntensity <= 0.0f && settings.bounceIntensity <= 0.0f)
				return;

			renderer.EnqueuePass(evaluatePass);

			compositePass.renderPassEvent = settings.bounceRenderPassEvent;
			renderer.EnqueuePass(compositePass);
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="renderer"></param>
		/// <param name="renderingData"></param>
		public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
		{
			if (compositePass == null)
				return;

			if (!ShouldRenderForCamera(in renderingData.cameraData))
				return;

			compositePass.SetSource(renderer.cameraColorTargetHandle);
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="disposing"></param>
		protected override void Dispose(bool disposing)
		{
			evaluatePass?.Dispose();

			CoreUtils.Destroy(evaluateMaterial);
			CoreUtils.Destroy(compositeMaterial);

			evaluateMaterial = null;
			compositeMaterial = null;
			evaluatePass = null;
			compositePass = null;
			state = null;
		}

	#endregion

	#region Methods

		/// <summary>
		/// Whether the frame being set up is one the hand placed indirect lighting belongs in. It belongs in the game
		/// view and, so that the artist composing the shapes can see what they are composing, in the scene view. It does
		/// not belong in a preview, in a reflection probe bake, or in one of the depth only offscreen cameras other
		/// effects render for themselves - the volumetric beams render one of those per beam to gather their occlusion,
		/// and such a camera has no color target for the composite to read in the first place. URP sets those up through
		/// a shortcut path in <c>UniversalRenderer.Setup</c> that calls <see cref="SetupRenderPasses"/> before the
		/// pipeline is marked as executing, so asking the renderer for its color target there logs an error every time
		/// one of them renders.
		/// </summary>
		/// <param name="cameraData"></param>
		/// <returns></returns>
		static bool ShouldRenderForCamera(in CameraData cameraData)
		{
			if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView)
				return false;

			var target = cameraData.targetTexture;

			return target == null || target.format != RenderTextureFormat.Depth;
		}

		/// <summary>
		/// Validates the resources used by the indirect lighting passes, and resolves the shader while in the editor so
		/// that the reference is already serialized by the time the feature is built into a player.
		/// </summary>
		/// <param name="forceRefresh"></param>
		/// <returns></returns>
		private bool ValidateResources(bool forceRefresh)
		{
			if (forceRefresh)
			{
#if UNITY_EDITOR
				Shader found = Shader.Find(ShaderName);

				// Only written when it actually changes, so that the asset is not dirtied on every domain reload.
				if (found != null && found != indirectLightingShader)
				{
					indirectLightingShader = found;
					UnityEditor.EditorUtility.SetDirty(this);
				}
#endif
				if (indirectLightingShader == null)
				{
					Debug.LogWarning($"[Inside Stage] The shader \"{ShaderName}\" could not be resolved, so the hand placed indirect lighting will not render. Reimport the Inside Stage package.", this);

					return false;
				}

				CoreUtils.Destroy(evaluateMaterial);
				CoreUtils.Destroy(compositeMaterial);

				evaluateMaterial = CoreUtils.CreateEngineMaterial(indirectLightingShader);
				compositeMaterial = CoreUtils.CreateEngineMaterial(indirectLightingShader);
			}

			return indirectLightingShader != null && evaluateMaterial != null && compositeMaterial != null;
		}

	#endregion
	}
}
