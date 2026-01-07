using Godot;
using System;
using System.Collections.Generic;

public partial class Item : Entity
{
    public string name;
    // public Item_Definition Data { get; set; }
    public float itemFriction = 0.9f;
    public int maxStack;
    [Export]
    public MeshInstance3D meshInstance;
    [Export]
    public float Gravity { get; set; } = 9.8f;

    public override void ImHere()
    {
        base.ImHere();

        // Additional initialization for Item if needed

    }

    public override void ApplyMovementFromInput(double delta)
    {
        Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * (float)delta, Velocity.Z);
        GlobalRotate(new Vector3(0, 1, 0), (float)delta);
        // Additional per-frame logic for Item if needed

        float fricMult = itemFriction;
        if (!OnFloor())
        {
            fricMult = 1.0f;
        }
        
        Velocity = new Vector3(Velocity.X * fricMult, Velocity.Y, Velocity.Z * fricMult);
    }
}