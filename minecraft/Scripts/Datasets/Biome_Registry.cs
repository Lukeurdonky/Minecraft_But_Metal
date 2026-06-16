using Godot;

public static class Biome_Registry
{
    public static readonly BiomeDescriptor[] All = new BiomeDescriptor[]
    {
        // ── Field ──────────────────────────────────────────────────────────
        new BiomeDescriptor {
            Name = "Bouncy Cloud Plains", Template = "Field",
            SurfaceBlock    = 8,   // Cloud
            NoiseScaleMin   = 0.8f,  NoiseScaleMax  = 1.2f,
            HeightAmpMin    = 6f,    HeightAmpMax   = 12f,
            FogColor        = new Color(0.85f, 0.9f, 1.0f), FogDensity = 0.015f,
        },
        new BiomeDescriptor {
            Name = "Grassy Plains", Template = "Field",
            SurfaceBlock    = 1,   // Grass
            NoiseScaleMin   = 1.0f,  NoiseScaleMax  = 1.5f,
            HeightAmpMin    = 5f,    HeightAmpMax   = 15f,
            FogColor        = new Color(0.55f, 0.75f, 0.45f), FogDensity = 0.01f,
        },
        new BiomeDescriptor {
            Name = "Metallic Mountains", Template = "Field",
            SurfaceBlock    = 6,   // Steel
            NoiseScaleMin   = 2.0f,  NoiseScaleMax  = 3.5f,
            HeightAmpMin    = 20f,   HeightAmpMax   = 40f,
            FogColor        = new Color(0.4f, 0.45f, 0.5f), FogDensity = 0.02f,
        },

        // ── Cave ───────────────────────────────────────────────────────────
        new BiomeDescriptor {
            Name = "Tight Stone Tunnels", Template = "Cave",
            SurfaceBlock      = 3,   // Stone
            CaveScaleMin      = 3.0f,  CaveScaleMax      = 4.5f,
            CaveThresholdMin  = 0.35f, CaveThresholdMax  = 0.45f,
            FogColor          = new Color(0.12f, 0.12f, 0.12f), FogDensity = 0.05f,
        },
        new BiomeDescriptor {
            Name = "Crystal Caverns", Template = "Cave",
            SurfaceBlock      = 11,  // LightCrystal
            CaveScaleMin      = 1.5f,  CaveScaleMax      = 2.5f,
            CaveThresholdMin  = 0.25f, CaveThresholdMax  = 0.32f,
            FogColor          = new Color(0.25f, 0.45f, 0.75f), FogDensity = 0.03f,
        },
        new BiomeDescriptor {
            Name = "The Moss Grotto", Template = "Cave",
            SurfaceBlock      = 14,  // Moss
            CaveScaleMin      = 2.0f,  CaveScaleMax      = 3.0f,
            CaveThresholdMin  = 0.28f, CaveThresholdMax  = 0.38f,
            FogColor          = new Color(0.18f, 0.35f, 0.18f), FogDensity = 0.04f,
        },

        // ── Abyss ──────────────────────────────────────────────────────────
        new BiomeDescriptor {
            Name = "Dark Descent", Template = "Abyss",
            SurfaceBlock    = 3,   // Stone
            NoiseScaleMin   = 1.2f,  NoiseScaleMax  = 1.8f,
            HeightAmpMin    = 6f,    HeightAmpMax   = 12f,
            ChasmRadiusMin  = 12f,   ChasmRadiusMax = 20f,
            FogColor        = new Color(0.05f, 0.05f, 0.1f), FogDensity = 0.06f,
        },
        new BiomeDescriptor {
            Name = "The Virus", Template = "Abyss",
            SurfaceBlock    = 16,  // Virus
            NoiseScaleMin   = 1.0f,  NoiseScaleMax  = 2.0f,
            HeightAmpMin    = 4f,    HeightAmpMax   = 10f,
            ChasmRadiusMin  = 8f,    ChasmRadiusMax = 16f,
            FogColor        = new Color(0.1f, 0.25f, 0.05f), FogDensity = 0.05f,
        },
        new BiomeDescriptor {
            Name = "Lava Walls", Template = "Abyss",
            SurfaceBlock    = 15,  // Lava
            NoiseScaleMin   = 1.2f,  NoiseScaleMax  = 2.0f,
            HeightAmpMin    = 8f,    HeightAmpMax   = 18f,
            ChasmRadiusMin  = 16f,   ChasmRadiusMax = 28f,
            FogColor        = new Color(0.6f, 0.15f, 0.0f), FogDensity = 0.07f,
        },
    };

    public static BiomeDescriptor Get(string name) =>
        System.Array.Find(All, b => b.Name == name);

    public static BiomeDescriptor[] ForTemplate(string template) =>
        System.Array.FindAll(All, b => b.Template == template);
}
