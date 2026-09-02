using UnityEngine.Rendering.Universal;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// Render pass events for the volumetric fog. Matches the ones in the Universal Render Pipeline.
	/// </summary>
	public enum VolumetricFogRenderPassEvent
	{
		/// <summary>
		/// Renders the fog right after the skybox, before the pass that copies the camera color into
		/// _CameraOpaqueTexture. The renderer sorts passes stably and adds the ones the features enqueue before its
		/// own, so at this event the fog lands in the camera color first and the opaque texture the transparent
		/// materials read is already fogged. Transparent materials that rebuild their background out of that texture,
		/// such as water or glass, stay continuous with the fog around them, at the cost of the fog no longer being
		/// laid over the transparents themselves.
		/// </summary>
		AfterRenderingSkybox = RenderPassEvent.AfterRenderingSkybox,

		BeforeRenderingTransparents = RenderPassEvent.BeforeRenderingTransparents,
		AfterRenderingTransparents = RenderPassEvent.AfterRenderingTransparents,
		BeforeRenderingPostProcessing = RenderPassEvent.BeforeRenderingPostProcessing
	}
}
