using UnityEngine.Rendering.Universal;

namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// Render pass events for the volumetric fog. Matches the ones in the Universal Render Pipeline.
	/// </summary>
	public enum VolumetricFogRenderPassEvent
	{
		BeforeRenderingTransparents = RenderPassEvent.BeforeRenderingTransparents,
		AfterRenderingTransparents = RenderPassEvent.AfterRenderingTransparents,
		BeforeRenderingPostProcessing = RenderPassEvent.BeforeRenderingPostProcessing
	}
}
