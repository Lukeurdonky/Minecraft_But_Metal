
using Godot;
using System;
using System.Diagnostics;
//using System.Numerics;
public sealed class Block_Model
{
	public string name;
	public enum Type { Cube, Stair, Slab, Custom };
	public Type type;
	public string datapath;
	
	public Vector3 heldScale;
	public Vector3 heldRotation;
	public Vector3 heldOffset;
	// public Vector2[] UVs;      // Optional to define but here for entity models
	public Vector3[] Vertices;  // Pre-built
	public Vector3[] Normals;   // Pre-built
	public int[] Indices;      // Pre-built
}
