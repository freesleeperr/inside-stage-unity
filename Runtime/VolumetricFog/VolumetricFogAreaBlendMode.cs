namespace FreeSleeperr.InsideStage
{
	/// <summary>
	/// How the fog that one area holds is combined with the fog of the areas it overlaps.
	/// </summary>
	public enum VolumetricFogAreaBlendMode
	{
		/// <summary>
		/// The densest of the overlapping areas wins the position, and gives it its tint and its noise as well.
		/// </summary>
		Max = 0,

		/// <summary>
		/// The fog of this area, carved by its own noise, is piled on top of whatever the other areas already left.
		/// </summary>
		Additive = 1
	}
}
