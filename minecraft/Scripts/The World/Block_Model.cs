
using Godot;
using System;
using System.Diagnostics;
public sealed class Block_Model
{
    public string name;
    public enum Type { Cube, Stair, Slab, Custom };
    public Type type;
    public string datapath;
    
    public Quaternion rotation;
    public Vector3 offset;
    // public Vector2[] UVs;      // Optional to define but here for entity models
    public Vector3[] Vertices;  // Pre-built
    public Vector3[] Normals;   // Pre-built
    public int[] Indices;      // Pre-built
}