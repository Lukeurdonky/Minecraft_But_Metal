using Godot;
using System;
using System.Collections.Generic;

// Per-block damage state for the block-cracking overlay system. Lives on the owning
// Chunk (see Chunk.DamageData) instead of a global dictionary, so memory cost scales
// with currently-damaged chunks rather than every loaded chunk in the world.
public sealed class BlockHealth
{
	public float health = 1.0f;
	public int blockType = 0;
	public LinkedListNode<Vector3I> insertionNode;
	// Stable index into this block's type's overlay MultiMesh instance array, or -1 if
	// this block currently has no rendered crack overlay.
	public int overlaySlot = -1;
	// World position, duplicated from the owning Chunk.DamageData key so a MultiMesh
	// capacity-growth resize can redraw this block's slot without needing to reverse-map
	// from chunk + local position.
	public Vector3I worldPos;
}

public sealed class Chunk
{
	public Vector3I Position;
	public byte[] Voxels;
	public bool Dirty = false;
	public bool Loaded = false;
	public bool Generated = false;
	public MeshInstance3D MeshInstance;
	// public StaticBody3D CollisionShape;
	// Whether the player (or game) has modified this chunk's voxels.
	// If false, the chunk can be safely evicted and regenerated deterministically.
	public bool WasEdited = false;

	// Lazy/sparse per-chunk damage tracking, keyed by LOCAL voxel position. Null until this
	// chunk's first damaged block, set back to null once its last damaged block clears —
	// a chunk with no damage costs nothing beyond the field itself, unlike a dense
	// per-chunk array (which at CHUNK_SIZE 48 would be 48^3 bytes per loaded chunk).
	public Dictionary<Vector3I, BlockHealth> DamageData;

	public Chunk(Vector3I position)
	{
		Position = position;
	}

	public bool IsFullySolid { get; set; } = false;
	public bool IsAllAir { get; set; } = false;
}
