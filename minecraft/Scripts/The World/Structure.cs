using Godot;

// A hand-built chunk of voxels, authored in Scenes/StructureBuilder.tscn and saved as a
// .tres under res://Structures/. This is the format FeatureStage will eventually read to
// scatter buildings/landmarks during generation (see documents/engineering/generation_plan.md),
// so it deliberately knows how to write itself into BOTH a live world (Stamp) and a raw
// chunk byte[] mid-generation (StampIntoChunk) — the generator has no Chunk_Manager to talk to.
//
// Resource rather than JSON so it exports with the game automatically, shows up in the
// Inspector, and stores Voxels as a compact base64 PackedByteArray.
[GlobalClass]
public partial class Structure : Resource
{
	[Export] public string  Name   = "";

	// Tight bounding box of the authored blocks — SaveStructure trims empty margins away,
	// so Size is never padded out to the builder's working volume.
	[Export] public Vector3I Size  = Vector3I.Zero;

	// Which cell inside the box lands on the position passed to Stamp(). Defaults to
	// bottom-centre (see Structure_Registry.CaptureAndSave) because the common case is
	// "drop this building on that ground block".
	[Export] public Vector3I Anchor = Vector3I.Zero;

	// Block ids, indexed the same way Chunk_Manager indexes a chunk: x + z*SX + y*SX*SZ.
	// Keeping the two conventions identical means StampIntoChunk is a straight copy loop.
	[Export] public byte[]  Voxels = System.Array.Empty<byte>();

	public int BlockCount
	{
		get
		{
			int n = 0;
			for (int i = 0; i < Voxels.Length; i++) if (Voxels[i] != 0) n++;
			return n;
		}
	}

	public int Index(int x, int y, int z) => x + z * Size.X + y * Size.X * Size.Z;

	public byte Get(int x, int y, int z)
	{
		if (x < 0 || y < 0 || z < 0 || x >= Size.X || y >= Size.Y || z >= Size.Z) return 0;
		return Voxels[Index(x, y, z)];
	}

	public void Set(int x, int y, int z, byte id)
	{
		if (x < 0 || y < 0 || z < 0 || x >= Size.X || y >= Size.Y || z >= Size.Z) return;
		Voxels[Index(x, y, z)] = id;
	}

	public void Allocate(Vector3I size)
	{
		Size   = size;
		Voxels = new byte[size.X * size.Y * size.Z];
	}

	// Write into a live world. worldPos is where Anchor lands.
	// clearAir=false is additive (terrain shows through the structure's empty cells);
	// clearAir=true carves the full bounding box out first, which is what you want for
	// anything with an interior that must not be filled with the hillside it sits in.
	public void Stamp(Chunk_Manager cm, Vector3I worldPos, bool clearAir = false)
	{
		if (cm == null || Voxels.Length == 0) return;
		Vector3I origin = worldPos - Anchor;

		for (int y = 0; y < Size.Y; y++)
		for (int z = 0; z < Size.Z; z++)
		for (int x = 0; x < Size.X; x++)
		{
			byte id = Voxels[Index(x, y, z)];
			// A marker is authoring data, not geometry. Skipped rather than written as air so
			// clearAir=true still carves its cell, and so it never lands in a live world.
			if (Block_Registry.MarkerById[id]) continue;
			if (id == 0 && !clearAir) continue;
			cm.place_block(origin + new Vector3I(x, y, z), id);
		}
	}

	// Cells holding the given marker (1..Block_Registry.MarkerCount), in world space for a
	// structure stamped so Anchor lands on worldPos. Empty when the structure has no such
	// marker — callers should treat that as an authoring error and say so, not fall back to a
	// guessed position, which is the silent drift markers exist to prevent.
	//
	// Scanned on demand rather than cached: this runs once when a hub scene loads, and a cache
	// would have to be invalidated every time the builder re-saves the structure.
	public Godot.Collections.Array<Vector3I> FindMarkers(int markerNumber, Vector3I worldPos)
	{
		var found = new Godot.Collections.Array<Vector3I>();
		byte want = Block_Registry.MarkerBlockId(markerNumber);
		if (want == 0 || Voxels.Length == 0) return found;

		Vector3I origin = worldPos - Anchor;
		for (int y = 0; y < Size.Y; y++)
		for (int z = 0; z < Size.Z; z++)
		for (int x = 0; x < Size.X; x++)
			if (Voxels[Index(x, y, z)] == want)
				found.Add(origin + new Vector3I(x, y, z));

		return found;
	}

	// Generation-time variant: writes straight into one chunk's voxel array, clipping to
	// that chunk. Call it for every chunk the structure's box overlaps. No Chunk_Manager,
	// no meshing, no main thread — safe from a generation worker.
	public void StampIntoChunk(byte[] chunkVoxels, Vector3I chunkPos, Vector3I worldPos, bool clearAir = false)
	{
		if (chunkVoxels == null || Voxels.Length == 0) return;

		int cs = Global.CHUNK_SIZE;
		Vector3I origin     = worldPos - Anchor;
		Vector3I chunkWorld = chunkPos * cs;

		for (int y = 0; y < Size.Y; y++)
		{
			int ly = origin.Y + y - chunkWorld.Y;
			if (ly < 0 || ly >= cs) continue;

			for (int z = 0; z < Size.Z; z++)
			{
				int lz = origin.Z + z - chunkWorld.Z;
				if (lz < 0 || lz >= cs) continue;

				for (int x = 0; x < Size.X; x++)
				{
					int lx = origin.X + x - chunkWorld.X;
					if (lx < 0 || lx >= cs) continue;

					byte id = Voxels[Index(x, y, z)];
					if (Block_Registry.MarkerById[id]) continue; // authoring data — see Stamp
					if (id == 0 && !clearAir) continue;
					chunkVoxels[lx + lz * cs + ly * cs * cs] = id;
				}
			}
		}
	}
}
