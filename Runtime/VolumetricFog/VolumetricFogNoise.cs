using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Procedurally generates and caches the tileable 3D noise textures that give the volumetric fog its cloudy look.
/// The shape texture holds a Perlin-Worley mix plus three Worley octaves, while the detail texture holds three
/// higher frequency Worley octaves that are used to erode the edges of the fog.
/// </summary>
public static class VolumetricFogNoise
{
	#region Definitions

	/// <summary>
	/// Fills the pixels of one of the 3D noise textures. Each index maps to a single voxel.
	/// </summary>
	private struct NoiseGenerationJob : IJobParallelFor
	{
		public int resolution;
		public int baseCells;
		public bool isShapeTexture;
		public uint seed;

		[WriteOnly]
		public NativeArray<Color32> pixels;

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="index"></param>
		public void Execute(int index)
		{
			int x = index % resolution;
			int y = (index / resolution) % resolution;
			int z = index / (resolution * resolution);

			float inverseResolution = 1.0f / resolution;
			Vector3 p = new Vector3((x + 0.5f) * inverseResolution, (y + 0.5f) * inverseResolution, (z + 0.5f) * inverseResolution);

			float r;
			float g;
			float b;
			float a;

			float lowFrequencyWorley = WorleyFbm(p, baseCells, seed + 1u);
			float midFrequencyWorley = WorleyFbm(p, baseCells * 2, seed + 2u);
			float highFrequencyWorley = WorleyFbm(p, baseCells * 4, seed + 3u);

			if (isShapeTexture)
			{
				// Dilating the perlin noise with the lowest frequency worley noise is what gives the billowy, cloud alike look.
				float perlin = ValueNoiseFbm(p, baseCells * 2, 4, seed + 4u);
				r = Remap(perlin, 0.0f, 1.0f, lowFrequencyWorley, 1.0f);
				g = lowFrequencyWorley;
				b = midFrequencyWorley;
				a = highFrequencyWorley;
			}
			else
			{
				r = lowFrequencyWorley;
				g = midFrequencyWorley;
				b = highFrequencyWorley;
				a = 1.0f;
			}

			pixels[index] = new Color32(ToByte(r), ToByte(g), ToByte(b), ToByte(a));
		}
	}

	#endregion

	#region Private Attributes

	private const int ShapeResolution = 64;
	private const int DetailResolution = 32;

	private const int ShapeBaseCells = 2;
	private const int DetailBaseCells = 1;

	private const uint ShapeSeed = 3571u;
	private const uint DetailSeed = 9127u;

	private static Texture3D shapeNoiseTexture;
	private static Texture3D detailNoiseTexture;

	#endregion

	#region Properties

	/// <summary>
	/// The low frequency 3D noise texture that defines the overall shape of the fog. It is generated the first
	/// time it is requested.
	/// </summary>
	public static Texture3D ShapeNoiseTexture
	{
		get
		{
			if (shapeNoiseTexture == null)
				shapeNoiseTexture = GenerateNoiseTexture(ShapeResolution, ShapeBaseCells, true, ShapeSeed, "VolumetricFogShapeNoise");

			return shapeNoiseTexture;
		}
	}

	/// <summary>
	/// The high frequency 3D noise texture that is used to erode the edges of the fog. It is generated the first
	/// time it is requested.
	/// </summary>
	public static Texture3D DetailNoiseTexture
	{
		get
		{
			if (detailNoiseTexture == null)
				detailNoiseTexture = GenerateNoiseTexture(DetailResolution, DetailBaseCells, false, DetailSeed, "VolumetricFogDetailNoise");

			return detailNoiseTexture;
		}
	}

	#endregion

	#region Methods

	/// <summary>
	/// Generates the noise textures, if they have not been generated yet. Meant to be called from the main thread,
	/// before the render pass that samples them is recorded.
	/// </summary>
	/// <param name="isNoiseEnabled"></param>
	/// <param name="isDetailEnabled"></param>
	public static void Prewarm(bool isNoiseEnabled, bool isDetailEnabled)
	{
		if (!isNoiseEnabled)
			return;

		Texture3D unused = ShapeNoiseTexture;

		if (isDetailEnabled)
			unused = DetailNoiseTexture;
	}

	/// <summary>
	/// Destroys the cached noise textures. They will be regenerated the next time they are requested.
	/// </summary>
	public static void Dispose()
	{
		CoreUtils.Destroy(shapeNoiseTexture);
		CoreUtils.Destroy(detailNoiseTexture);

		shapeNoiseTexture = null;
		detailNoiseTexture = null;
	}

	/// <summary>
	/// Generates one of the tileable 3D noise textures.
	/// </summary>
	/// <param name="resolution"></param>
	/// <param name="baseCells"></param>
	/// <param name="isShapeTexture"></param>
	/// <param name="seed"></param>
	/// <param name="name"></param>
	/// <returns></returns>
	private static Texture3D GenerateNoiseTexture(int resolution, int baseCells, bool isShapeTexture, uint seed, string name)
	{
		int voxelCount = resolution * resolution * resolution;
		NativeArray<Color32> pixels = new NativeArray<Color32>(voxelCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

		NoiseGenerationJob job = new NoiseGenerationJob
		{
			resolution = resolution,
			baseCells = baseCells,
			isShapeTexture = isShapeTexture,
			seed = seed,
			pixels = pixels
		};

		job.Schedule(voxelCount, resolution * resolution).Complete();

		Texture3D texture = new Texture3D(resolution, resolution, resolution, TextureFormat.RGBA32, false)
		{
			name = name,
			wrapMode = TextureWrapMode.Repeat,
			filterMode = FilterMode.Bilinear,
			anisoLevel = 0,
			hideFlags = HideFlags.HideAndDontSave
		};

		texture.SetPixelData(pixels, 0);
		texture.Apply(false, true);

		pixels.Dispose();

		return texture;
	}

	#endregion

	#region Noise Methods

	/// <summary>
	/// Gets a tileable fractal brownian motion made of three worley noise octaves.
	/// </summary>
	/// <param name="p"></param>
	/// <param name="cells"></param>
	/// <param name="seed"></param>
	/// <returns></returns>
	private static float WorleyFbm(Vector3 p, int cells, uint seed)
	{
		return WorleyNoise(p, cells, seed) * 0.625f + WorleyNoise(p, cells * 2, seed + 31u) * 0.25f + WorleyNoise(p, cells * 4, seed + 71u) * 0.125f;
	}

	/// <summary>
	/// Gets tileable worley noise, already inverted so that the value is close to one at the feature points.
	/// </summary>
	/// <param name="p"></param>
	/// <param name="cells"></param>
	/// <param name="seed"></param>
	/// <returns></returns>
	private static float WorleyNoise(Vector3 p, int cells, uint seed)
	{
		Vector3 scaled = p * cells;

		int xi = Mathf.FloorToInt(scaled.x);
		int yi = Mathf.FloorToInt(scaled.y);
		int zi = Mathf.FloorToInt(scaled.z);

		float minDistanceSq = float.MaxValue;

		for (int dz = -1; dz <= 1; ++dz)
		{
			for (int dy = -1; dy <= 1; ++dy)
			{
				for (int dx = -1; dx <= 1; ++dx)
				{
					int cx = xi + dx;
					int cy = yi + dy;
					int cz = zi + dz;

					// The hash is done with the wrapped cell so that the noise tiles seamlessly, while the feature
					// point is placed on the unwrapped cell so that distances remain continuous across the border.
					int wx = Wrap(cx, cells);
					int wy = Wrap(cy, cells);
					int wz = Wrap(cz, cells);

					float px = cx + Hash01(wx, wy, wz, seed);
					float py = cy + Hash01(wx, wy, wz, seed + 101u);
					float pz = cz + Hash01(wx, wy, wz, seed + 211u);

					float ox = px - scaled.x;
					float oy = py - scaled.y;
					float oz = pz - scaled.z;

					float distanceSq = ox * ox + oy * oy + oz * oz;
					minDistanceSq = Mathf.Min(minDistanceSq, distanceSq);
				}
			}
		}

		return 1.0f - Mathf.Clamp01(Mathf.Sqrt(minDistanceSq));
	}

	/// <summary>
	/// Gets a tileable fractal brownian motion made of value noise octaves.
	/// </summary>
	/// <param name="p"></param>
	/// <param name="period"></param>
	/// <param name="octaves"></param>
	/// <param name="seed"></param>
	/// <returns></returns>
	private static float ValueNoiseFbm(Vector3 p, int period, int octaves, uint seed)
	{
		float sum = 0.0f;
		float totalAmplitude = 0.0f;
		float amplitude = 0.5f;
		int currPeriod = period;

		for (int i = 0; i < octaves; ++i)
		{
			sum += amplitude * ValueNoise(p, currPeriod, seed + (uint)i * 17u);
			totalAmplitude += amplitude;
			amplitude *= 0.5f;
			currPeriod *= 2;
		}

		return sum / totalAmplitude;
	}

	/// <summary>
	/// Gets tileable value noise.
	/// </summary>
	/// <param name="p"></param>
	/// <param name="period"></param>
	/// <param name="seed"></param>
	/// <returns></returns>
	private static float ValueNoise(Vector3 p, int period, uint seed)
	{
		Vector3 scaled = p * period;

		int xi = Mathf.FloorToInt(scaled.x);
		int yi = Mathf.FloorToInt(scaled.y);
		int zi = Mathf.FloorToInt(scaled.z);

		float fx = scaled.x - xi;
		float fy = scaled.y - yi;
		float fz = scaled.z - zi;

		float ux = fx * fx * (3.0f - 2.0f * fx);
		float uy = fy * fy * (3.0f - 2.0f * fy);
		float uz = fz * fz * (3.0f - 2.0f * fz);

		float c000 = Hash01(Wrap(xi, period), Wrap(yi, period), Wrap(zi, period), seed);
		float c100 = Hash01(Wrap(xi + 1, period), Wrap(yi, period), Wrap(zi, period), seed);
		float c010 = Hash01(Wrap(xi, period), Wrap(yi + 1, period), Wrap(zi, period), seed);
		float c110 = Hash01(Wrap(xi + 1, period), Wrap(yi + 1, period), Wrap(zi, period), seed);
		float c001 = Hash01(Wrap(xi, period), Wrap(yi, period), Wrap(zi + 1, period), seed);
		float c101 = Hash01(Wrap(xi + 1, period), Wrap(yi, period), Wrap(zi + 1, period), seed);
		float c011 = Hash01(Wrap(xi, period), Wrap(yi + 1, period), Wrap(zi + 1, period), seed);
		float c111 = Hash01(Wrap(xi + 1, period), Wrap(yi + 1, period), Wrap(zi + 1, period), seed);

		float x00 = Mathf.Lerp(c000, c100, ux);
		float x10 = Mathf.Lerp(c010, c110, ux);
		float x01 = Mathf.Lerp(c001, c101, ux);
		float x11 = Mathf.Lerp(c011, c111, ux);

		float y0 = Mathf.Lerp(x00, x10, uy);
		float y1 = Mathf.Lerp(x01, x11, uy);

		return Mathf.Lerp(y0, y1, uz);
	}

	/// <summary>
	/// Wraps the given lattice coordinate into the [0, period) range.
	/// </summary>
	/// <param name="value"></param>
	/// <param name="period"></param>
	/// <returns></returns>
	private static int Wrap(int value, int period)
	{
		return ((value % period) + period) % period;
	}

	/// <summary>
	/// Hashes the given lattice coordinate into the [0, 1) range.
	/// </summary>
	/// <param name="x"></param>
	/// <param name="y"></param>
	/// <param name="z"></param>
	/// <param name="seed"></param>
	/// <returns></returns>
	private static float Hash01(int x, int y, int z, uint seed)
	{
		unchecked
		{
			uint h = seed + 374761393u;
			h += (uint)x * 3266489917u;
			h += (uint)y * 668265263u;
			h += (uint)z * 2246822519u;
			h ^= h >> 15;
			h *= 2654435761u;
			h ^= h >> 13;
			h *= 3266489917u;
			h ^= h >> 16;

			return (h & 0xFFFFFFu) * (1.0f / 16777216.0f);
		}
	}

	/// <summary>
	/// Remaps the given value from the original range to the new range.
	/// </summary>
	/// <param name="value"></param>
	/// <param name="originalMin"></param>
	/// <param name="originalMax"></param>
	/// <param name="newMin"></param>
	/// <param name="newMax"></param>
	/// <returns></returns>
	private static float Remap(float value, float originalMin, float originalMax, float newMin, float newMax)
	{
		return newMin + (value - originalMin) / (originalMax - originalMin) * (newMax - newMin);
	}

	/// <summary>
	/// Converts the given normalized value to a byte.
	/// </summary>
	/// <param name="value"></param>
	/// <returns></returns>
	private static byte ToByte(float value)
	{
		return (byte)(Mathf.Clamp01(value) * 255.0f + 0.5f);
	}

	#endregion
}
