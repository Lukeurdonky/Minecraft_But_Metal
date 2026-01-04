using Godot;
using System;
using System.Collections.Generic;

public partial class Entity : CharacterBody3D
{
    [Export]
    public int MaxHealth = 100;

    [Export]
    public Vector2 offset = Vector2.Zero;
    [Export]
    public float width = 1f;
    [Export]
    public float height = 1.8f;
    public int CurrentHealth { get; set; }
    public Global Global;

    public override void _Ready()
    {
        Global = GetNode<Global>("/root/Global");
        ImHere();
    }

    public override void _PhysicsProcess(double delta)
    {
        // Entity logic here
        ApplyMovementFromInput(delta);
        HandleWorldCollisions(Velocity * (float)delta);
    }

    public virtual void TakeDamage(int amount)
    {
        CurrentHealth -= amount;

        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    public virtual void Die()
    {
        QueueFree();
    }

    public virtual void ApplyMovementFromInput(double delta)
    {
        // Placeholder for movement logic

    }

    public virtual void ImHere()
    {
        CurrentHealth = MaxHealth;
    }

    public virtual void HandleWorldCollisions(Vector3 moveBy)
    {
        // Placeholder for world collision handling
        CheckWorldCollisions(moveBy);
    }

    public virtual void CheckWorldCollisions(Vector3 moveBy)
    {
        int xSize = (int)Mathf.Ceil(width);
        int ySize = (int)Mathf.Ceil(height);
        Aabb entityBox = GetAABB();
        
        // Apply the intended movement
        // Vector3 newPosition = GlobalPosition + moveBy;
        Aabb futureBox = entityBox;
        futureBox.Position += moveBy;
        
        // Check all blocks in the movement path
        for(int x = -1; x <= xSize; x++)
        {
            for(int y = -1; y <= ySize; y++)
            {
                for(int z = -1; z <= xSize; z++)
                {
                    Vector3 checkPos = new Vector3(futureBox.Position.X + x, futureBox.Position.Y + y, futureBox.Position.Z + z);
                    Vector3I blockPos = new Vector3I(
                        (int)Mathf.Floor(checkPos.X),
                        (int)Mathf.Floor(checkPos.Y),
                        (int)Mathf.Floor(checkPos.Z)
                    );
                    
                    // Check if there's a solid block at this position
                    int blockID = Global.CubeManager.get_block(blockPos);
                    if(blockID == 0) continue; // Air, skip
                    
                    // Create AABB for the block (1x1x1 cube)
                    Aabb blockBox = new Aabb(
                        new Vector3(blockPos.X, blockPos.Y, blockPos.Z),
                        new Vector3(1, 1, 1)
                    );
                    
                    // Check if the future entity box intersects with this block
                    if(futureBox.Intersects(blockBox))
                    {
                        // Calculate penetration depth on each axis
                        Vector3 penetration = CalculatePenetration(futureBox, blockBox);
                        
                        // Find the axis with the smallest penetration (collision normal)
                        float minPenetration = Mathf.Min(Mathf.Abs(penetration.X), Mathf.Min(Mathf.Abs(penetration.Y), Mathf.Abs(penetration.Z)));
                        
                        // Determine the collision face normal and resolve collision
                        if(Mathf.Abs(penetration.X) == minPenetration)
                        {
                            // Hit left or right face
                            Vector3 normal = new Vector3(Mathf.Sign(penetration.X), 0, 0);
                            ResolveCollision(normal, Mathf.Abs(penetration.X), blockPos);
                            futureBox = GetAABB(); // Update box after resolution
                        }
                        else if(Mathf.Abs(penetration.Y) == minPenetration)
                        {
                            // Hit top or bottom face
                            Vector3 normal = new Vector3(0, Mathf.Sign(penetration.Y), 0);
                            ResolveCollision(normal, Mathf.Abs(penetration.Y), blockPos);
                            futureBox = GetAABB(); // Update box after resolution
                        }
                        else if(Mathf.Abs(penetration.Z) == minPenetration)
                        {
                            // Hit front or back face
                            Vector3 normal = new Vector3(0, 0, Mathf.Sign(penetration.Z));
                            ResolveCollision(normal, Mathf.Abs(penetration.Z), blockPos);
                            futureBox = GetAABB(); // Update box after resolution
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Calculates the penetration depth between two AABBs on each axis.
    /// Positive values mean entity is penetrating from the positive side, negative from negative side.
    /// </summary>
    private Vector3 CalculatePenetration(Aabb entityBox, Aabb blockBox)
    {
        // Calculate the overlap on each axis
        float overlapX = Mathf.Min(entityBox.End.X, blockBox.End.X) - Mathf.Max(entityBox.Position.X, blockBox.Position.X);
        float overlapY = Mathf.Min(entityBox.End.Y, blockBox.End.Y) - Mathf.Max(entityBox.Position.Y, blockBox.Position.Y);
        float overlapZ = Mathf.Min(entityBox.End.Z, blockBox.End.Z) - Mathf.Max(entityBox.Position.Z, blockBox.Position.Z);
        
        // Determine direction of penetration (which side the entity is coming from)
        float signX = entityBox.GetCenter().X < blockBox.GetCenter().X ? -1 : 1;
        float signY = entityBox.GetCenter().Y < blockBox.GetCenter().Y ? -1 : 1;
        float signZ = entityBox.GetCenter().Z < blockBox.GetCenter().Z ? -1 : 1;
        
        return new Vector3(overlapX * signX, overlapY * signY, overlapZ * signZ);
    }
    
    /// <summary>
    /// Resolves collision by pushing the entity out and applying physics response.
    /// Normal indicates which face was hit, penetration is how far to push out (absolute value).
    /// </summary>
    private void ResolveCollision(Vector3 normal, float penetration, Vector3I blockPos)
    {
        // Push the entity out of the block (negative because we're pushing away from penetration)
        GlobalPosition += normal * penetration;
        
        // Stop velocity in the collision direction
        if(normal.X != 0)
        {
            Velocity = new Vector3(0, Velocity.Y, Velocity.Z);
        }
        else if(normal.Y != 0)
        {
            Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
            
            // Special handling for landing on top (normal.Y = 1 means hitting from below, -1 means landing on top)
            if(normal.Y < 0)
            {
                OnLandedOnBlock(blockPos);
            }
        }
        else if(normal.Z != 0)
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y, 0);
        }
        
        // Optional: Call custom behavior based on face
        OnBlockCollision(normal, blockPos);
    }
    
    /// <summary>
    /// Called when entity lands on top of a block (can be overridden for custom behavior)
    /// </summary>
    protected virtual void OnLandedOnBlock(Vector3I blockPos)
    {
        // Override in derived classes for custom landing behavior (e.g., jump reset, landing sound)
    }
    
    /// <summary>
    /// Called when entity collides with any block face (can be overridden for custom behavior)
    /// </summary>
    protected virtual void OnBlockCollision(Vector3 faceNormal, Vector3I blockPos)
    {
        // Override in derived classes for custom collision behavior per face
        // faceNormal indicates which face was hit: (1,0,0) = right, (-1,0,0) = left, etc.
    }

    public virtual Aabb GetAABB()
    {
        return new Aabb(
            new Vector3(GlobalPosition.X - width / 2 + offset.X, GlobalPosition.Y - height / 2 + offset.Y, GlobalPosition.Z - width / 2 + offset.X),
            new Vector3(width, height, width)
        );
    }

    public virtual bool Intersects(Vector3 pos)
    {
        Aabb box = new Aabb(
            new Vector3(pos.X - .5f, pos.Y - .5f, pos.Z - .5f),
            new Vector3(1, 1, 1)
        );

        if (GetAABB().Intersects(box))
            return true;
        return false;
    }

    public Vector3 GetCenter()
    {
        return GetAABB().GetCenter();
    }
}