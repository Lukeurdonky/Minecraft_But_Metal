using Godot;
using System;


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

	public Chunk(Vector3I position)
	{
		Position = position;
	}

	public bool IsFullySolid { get; set; } = false;
}
