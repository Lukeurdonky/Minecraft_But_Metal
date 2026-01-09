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
    public float pickupDelay = 1f;  // Can't pickup immediately after spawn
    public float pursueSpeed = 16f;
    public float pickupDistance = 0.5f;
    private float age = 0f;

    public bool Detected = false;

    public override void ImHere()
    {
        base.ImHere();

        // Layer 3 (bitmask 4) = items - so Player's Area3D can detect us
        // Mask 0 = don't physically collide with anything (world collision is manual via CheckWorldCollisions)
        CollisionLayer = 4;  
        CollisionMask = 0;   
        
        // Add collision shape if not already in scene (required for CharacterBody3D)
        if (GetNodeOrNull<CollisionShape3D>("CollisionShape3D") == null)
        {
            CollisionShape3D shape = new CollisionShape3D();
            shape.Shape = new SphereShape3D() { Radius = 0.3f };
            AddChild(shape);
        }
    }

    public override void ApplyMovementFromInput(double delta)
    {
        GlobalRotate(new Vector3(0, 1, 0), (float)delta);
        age += (float)delta;


        if(Detected && age > pickupDelay && Global.Player != null)
        {
            float dist = GlobalPosition.DistanceTo(Global.Player.GlobalPosition);
        
            if (dist < Global.Player.PickUpRange)
            {
                // Pursue player
                Vector3 direction = (Global.Player.GlobalPosition - GlobalPosition).Normalized();
                Velocity = direction * pursueSpeed;
                
                if (dist < pickupDistance)
                {
                    PickUp();
                    return;
                }
            }
        }
        else
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * (float)delta, Velocity.Z);
        
            // Additional per-frame logic for Item if needed

            float fricMult = itemFriction;
            if (!OnFloor())
            {
                fricMult = 1.0f;
            }
            
            Velocity = new Vector3(Velocity.X * fricMult, Velocity.Y, Velocity.Z * fricMult);
        }
        
    }

    public void PickUp()
    {
        // Logic for picking up the item
        Global.Player.Inventory.Call("add_item", name);
        QueueFree(); // Remove the item from the world
    }
}