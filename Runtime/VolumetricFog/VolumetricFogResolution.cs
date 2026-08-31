namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// The resolution the volumetric fog is rendered at, as the factor the camera resolution is divided by. Every
	/// value has to stay a divisor of the camera resolution that the depth downsample can gather a whole block for.
	/// </summary>
	public enum VolumetricFogResolution
	{
		Half = 2,
		Quarter = 4
	}
}
