using Godot;
using System;
using System.Diagnostics;
public sealed class Block_Definition
{
    public ushort Id;
    public string Name;
    public float Hardness;
    public string DropId;
    public byte DropCount;

    // --- Transparency (render-only; a transparent block is still fully solid to
    // collision, grapple, explosions and every other get_block consumer) ---

    // Two consequences in Chunk_Manager: the block is meshed into a separate
    // alpha-blended surface, and it stops hiding the face of whatever is behind it.
    // Set this directly for art that is transparent per-pixel at full strength — the
    // Frame's cut-out holes live in the atlas, not in Alpha.
    private bool _transparent;
    public bool Transparent
    {
        get => _transparent || Alpha < 1f;
        set => _transparent = value;
    }

    // Uniform tint alpha, multiplied onto the atlas texture's own per-pixel alpha via
    // vertex colour. Lets a block be faded without repainting its art. Anything below 1
    // implies Transparent, so setting this alone is enough.
    public float Alpha = 1f;

    // --- Markers (authoring-only) ---

    // A marker is a real, placeable, visible block *in the builder* and nowhere else:
    // Structure.Stamp/StampIntoChunk skip it, so it never reaches a live world. It exists so
    // positional facts about a structure ("the console is here") are authored in the same
    // place the structure is, instead of being restated as an offset in whatever script
    // stamps it — two sources of truth that drift silently.
    //
    // Markers stay in the saved Voxels rather than being stripped at capture time, so a
    // builder round-trip (Load -> edit -> Save) preserves them.
    public bool IsMarker;

    public Vector2[][] faceUVs; // Specific for this block
    public Block_Model Model;
}
