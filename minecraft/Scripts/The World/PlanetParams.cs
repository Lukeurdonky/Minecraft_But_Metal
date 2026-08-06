public class PlanetParams
{
	public string Template        = "Field";
	public string Biome           = "";

	// Terrain
	// Skips terrain entirely — every chunk generates as air. This is what a hub scene
	// stands on: the only solid blocks in the world are the ones a Structure stamps in.
	// Cheaper than any terrain, since an all-air chunk builds no mesh at all.
	public bool   VoidWorld       = false;
	public bool   FillSolid       = false;
	public byte   SurfaceBlock    = 10;     // Crystal default
	public float  NoiseScale      = 1.5f;
	public float  HeightAmplitude = 10f;
	public int    SpawnY          = 20;

	// Caves
	public bool   CavesEnabled    = false;
	public bool   CaveFullRange   = false;  // carve at all Y levels (Cave template)
	public float  CaveScale       = 3.0f;
	public float  CaveYFrequency  = 0.05f;
	public float  CaveThreshold   = 0.25f;

	// Chasm — shaft centered at (PlanetWidth/2, PlanetDepth/2)
	public bool   ChasmEnabled    = false;
	public float  ChasmRadius     = 18f;
	public float  ChasmDriftScale = 0.006f;

	// Spawn clear — carve open ellipsoid around WorldSpawn (required for Cave template)
	public bool  SpawnClearEnabled  = false;
	public float SpawnClearRadiusXZ = 10f;
	public float SpawnClearRadiusY  = 6f;

	public static PlanetParams MakeField() => new PlanetParams
	{
		Template       = "Field",
		FillSolid      = false,
		SurfaceBlock   = 8,    // Cloud
		NoiseScale     = 1.5f,
		HeightAmplitude = 10f,
		SpawnY         = 20,
		CavesEnabled   = false,
		ChasmEnabled   = false,
	};

	public static PlanetParams MakeCave() => new PlanetParams
	{
		Template        = "Cave",
		FillSolid       = true,
		SurfaceBlock    = 10,   // Crystal
		NoiseScale      = 0f,
		HeightAmplitude = 0f,
		SpawnY          = 0,
		CavesEnabled    = true,
		CaveFullRange   = true,
		CaveScale       = 2.0f,   // feature size ~80 blocks — large chambers
		CaveYFrequency  = 1.0f,   // rate at which caves shift with depth
		CaveThreshold   = 0.3f,   // d1+d2 > 0.3 → cave; ~35% open space
		ChasmEnabled    = false,
		SpawnClearEnabled  = true,
		SpawnClearRadiusXZ = 10f,
		SpawnClearRadiusY  = 6f,
	};

	public static PlanetParams MakeAbyss() => new PlanetParams
	{
		Template       = "Abyss",
		FillSolid      = false,
		SurfaceBlock   = 6,    // Steel
		NoiseScale     = 1.5f,
		HeightAmplitude = 8f,
		SpawnY         = 20,
		CavesEnabled   = false,
		ChasmEnabled   = true,
		ChasmRadius    = 18f,
		ChasmDriftScale = 0.006f,
	};
}
