using Godot;

// Applies atmosphere (fog colour, sky colour, ambient light) from the active
// biome to the WorldEnvironment on scene load. Reads Global.ActivePlanet.Biome
// → Biome_Registry → BiomeDescriptor. Structural fog settings (depth curve,
// end distance, energy) stay as authored in the .tscn; this only overrides
// colours and density so every biome feels visually distinct.
// Wire to PlanetDescriptor when RunManager is built — swap the biome lookup
// for Global.ActiveDescriptor.Atmosphere at that point.
public partial class AtmosphereSystem : Node
{
    [Export] public NodePath WorldEnvironmentPath { get; set; } = "../WorldEnvironment";

    public override void _Ready()
    {
        Apply();
    }

    private void Apply()
    {
        var planet = Global.Instance?.ActivePlanet;
        if (planet == null) return;

        var biome = Biome_Registry.Get(planet.Biome);
        if (biome == null) return;

        var envNode = GetNodeOrNull<WorldEnvironment>(WorldEnvironmentPath);
        if (envNode?.Environment == null) return;

        var env = envNode.Environment;
        var fog = biome.FogColor;

        env.BackgroundColor   = fog;
        env.AmbientLightColor = fog * 0.6f;

        // Switch to exponential fog — BiomeDescriptor.FogDensity values (0.01–0.07)
        // are calibrated for this mode. Depth fog (mode 1) ignores FogDensity entirely.
        env.FogEnabled  = true;
        env.FogMode     = Godot.Environment.FogModeEnum.Exponential;
        env.FogLightColor = fog;
        env.FogDensity  = biome.FogDensity;
    }
}
