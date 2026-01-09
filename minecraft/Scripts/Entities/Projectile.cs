using Godot;
using System;
using System.Collections.Generic;

public partial class Projectile : Entity
{
    public float Gravity = 9.8f;
    public float LifeTime = 5.0f; // seconds
    private float age = 0.0f;
    private Area3D hitArea;

    public override void ImHere()
    {
        base.ImHere();

        // Layer 5 (bitmask 32) = projectiles - so other entities can detect us
        // Mask 1 (bitmask 1) = world - so we collide with the world
        CollisionLayer = 32;  
        CollisionMask = 1;   
        
        // Add collision shape if not already in scene (required for CharacterBody3D)
        if (GetNodeOrNull<CollisionShape3D>("CollisionShape3D") == null)
        {
            CollisionShape3D shape = new CollisionShape3D();
            shape.Shape = new SphereShape3D() { Radius = 0.1f };
            AddChild(shape);
        }

        // Add Area3D for entity detection
        hitArea = new Area3D();
        hitArea.CollisionLayer = 0;  // Don't need to be detected
        hitArea.CollisionMask = 2;   // Detect layer 2 (entities) - adjust as needed
        
        CollisionShape3D areaShape = new CollisionShape3D();
        areaShape.Shape = new SphereShape3D() { Radius = 0.15f };
        hitArea.AddChild(areaShape);
        
        hitArea.BodyEntered += OnBodyEntered;
        AddChild(hitArea);
    }

    private void OnBodyEntered(Node3D body)
    {
        // Check if we hit an entity (but not ourselves)
        if (body is Entity entity && entity != this)
        {
            OnHitEntity(entity);
        }
    }

    public virtual void OnHitEntity(Entity entity)
    {
        // Logic for when projectile hits an entity
        QueueFree(); // Destroy projectile on hit
    }

    public virtual void OnHitWorld()
    {
        // Logic for when projectile hits an entity
        QueueFree(); // Destroy projectile on hit
    }

    public override void HandleWorldCollisions(Vector3 moveBy)
	{
		// Placeholder for world collision handling
		if(CheckWorldCollisions(moveBy));
        {
            OnHitWorld();
        }
	}

    public override void ApplyMovementFromInput(double delta)
    {
        age += (float)delta;
        if (age > LifeTime)
        {
            QueueFree();
            return;
        }

        // Apply gravity
        Velocity += Vector3.Down * Gravity * (float)delta;

        base.ApplyMovementFromInput(delta);
    }
}