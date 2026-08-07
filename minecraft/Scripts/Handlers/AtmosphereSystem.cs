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
    [Export] public NodePath SunLightPath         { get; set; } = "../DirectionalLight3D";

    // Cave planets get no sun at all — a directional light underground reads as a hole in
    // the ceiling that isn't there. The world light has to carry the whole scene instead,
    // so ambient switches from the default BG source (which just re-uses the background
    // colour at energy 1) to an explicit colour it can actually be brightened past.
    // Both knobs are exported because "bright enough" is an eyeball call.
    [Export] public float CaveAmbientEnergy { get; set; } = 1.8f;
    [Export] public float CaveAmbientLift   { get; set; } = 0.45f; // fog colour → white

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

        env.BackgroundColor = fog;

        // Both branches are written explicitly rather than only touching the cave case:
        // planet-to-planet moves reload this scene, but the Environment is a sub-resource
        // and a cached one would otherwise carry a previous cave's lighting onto a field.
        bool underground = biome.Template == "Cave";
        var sun = GetNodeOrNull<DirectionalLight3D>(SunLightPath);
        if (sun != null) sun.Visible = !underground;

        if (underground)
        {
            env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            env.AmbientLightColor  = fog.Lerp(Colors.White, CaveAmbientLift);
            env.AmbientLightEnergy = CaveAmbientEnergy;
        }
        else
        {
            env.AmbientLightSource = Godot.Environment.AmbientSource.Bg;
            env.AmbientLightColor  = fog * 0.6f;
            env.AmbientLightEnergy = 1f;
        }

        // Switch to exponential fog — BiomeDescriptor.FogDensity values (0.01–0.07)
        // are calibrated for this mode. Depth fog (mode 1) ignores FogDensity entirely.
        env.FogEnabled  = true;
        env.FogMode     = Godot.Environment.FogModeEnum.Exponential;
        env.FogLightColor = fog;
        env.FogDensity  = biome.FogDensity;
    }
}
