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

    public Vector2[][] faceUVs; // Specific for this block
    public Block_Model Model;
}