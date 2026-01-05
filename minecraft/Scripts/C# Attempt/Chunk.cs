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

	public Chunk(Vector3I position)
	{
		Position = position;
	}
}
