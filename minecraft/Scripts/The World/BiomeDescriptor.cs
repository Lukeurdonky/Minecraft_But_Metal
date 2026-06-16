using Godot;

// Defines one biome: block palette, param ranges, and atmosphere.
// Each biome belongs to exactly one template. MakePlanetParams randomises
// within the biome's ranges so every generated planet feels unique.
public class BiomeDescriptor
{
    public string Name;
    public string Template;   // "Field", "Cave", "Abyss"

    public byte SurfaceBlock;

    // Terrain param ranges (Field / Abyss templates)
    public float NoiseScaleMin  = 1.5f, NoiseScaleMax  = 1.5f;
    public float HeightAmpMin   = 10f,  HeightAmpMax   = 10f;

    // Cave param ranges (Cave template)
    public float CaveScaleMin       = 2.0f, CaveScaleMax       = 2.0f;
    public float CaveThresholdMin   = 0.25f,CaveThresholdMax   = 0.35f;

    // Chasm param ranges (Abyss template)
    public float ChasmRadiusMin = 14f, ChasmRadiusMax = 22f;

    // Atmosphere — fog that masks chunk load boundary.
    // Actual WorldEnvironment wiring is a separate task.
    public Color FogColor   = new Color(0.5f, 0.5f, 0.5f);
    public float FogDensity = 0.02f;

    // Future: enemy type tags, structure descriptors

    public PlanetParams MakePlanetParams(int seed = 0)
    {
        var rng = new System.Random(seed);
        var p = Template switch {
            "Cave"  => PlanetParams.MakeCave(),
            "Abyss" => PlanetParams.MakeAbyss(),
            _       => PlanetParams.MakeField(),
        };
        p.Biome        = Name;
        p.SurfaceBlock = SurfaceBlock;
        p.NoiseScale      = Rng(rng, NoiseScaleMin,  NoiseScaleMax);
        p.HeightAmplitude = Rng(rng, HeightAmpMin,   HeightAmpMax);
        if (Template == "Cave") {
            p.CaveScale     = Rng(rng, CaveScaleMin,     CaveScaleMax);
            p.CaveThreshold = Rng(rng, CaveThresholdMin, CaveThresholdMax);
        }
        if (Template == "Abyss") {
            p.ChasmRadius = Rng(rng, ChasmRadiusMin, ChasmRadiusMax);
        }
        return p;
    }

    private static float Rng(System.Random r, float min, float max) =>
        min + (float)r.NextDouble() * (max - min);
}
