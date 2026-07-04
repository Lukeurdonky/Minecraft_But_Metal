using System;

// Data entry for one accessory. Mirrors the BiomeDescriptor/Biome_Registry convention —
// Name is the stable lookup key, CreateInstance is a factory for the runtime Accessory object.
public class AccessoryDescriptor
{
    public string Name;
    public string Description;
    public Func<Accessory> CreateInstance;

    // Position in an icon texture atlas, same convention as Block_Registry's blockIndex
    // (row = IconIndex / cols, col = IconIndex % cols) — set once the atlas exists.
    public int IconIndex;
}
