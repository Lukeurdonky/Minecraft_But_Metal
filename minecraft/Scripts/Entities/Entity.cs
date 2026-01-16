using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class Entity : CharacterBody3D
{
	[Export]
	public int MaxHealth = 100;
	[Export]
	public float MaxFallSpeed = 50f;

	[Export]
	public Vector2 offset = Vector2.Zero;
	[Export]
	public float width = 1f;
	[Export]
	public float height = 1.8f;
	public int CurrentHealth { get; set; }
	public Global Global;

	// Frame spike detection
	private static readonly Stopwatch _frameTimer = new Stopwatch();
	private const double SPIKE_THRESHOLD_MS = 16.67; // Anything over 60fps frame time
	private static double _lastDelta = 0;

	public override void _Ready()
	{
		Global = GetNode<Global>("/root/Global");
		ImHere();
	}

	public override void _PhysicsProcess(double delta)
	{
		// _frameTimer.Restart();
		
		// // Detect frame time spikes (jitter in delta)
		// if (_lastDelta > 0)
		// {
		// 	double deltaMs = delta * 1000.0;
		// 	double lastDeltaMs = _lastDelta * 1000.0;
		// 	double jitter = Math.Abs(deltaMs - lastDeltaMs);
			
		// 	// Log if frame time varies significantly (>2ms jitter)
		// 	if (jitter > 2.0)
		// 	{
		// 		GD.Print($"[JITTER] Delta: {deltaMs:F2}ms, LastDelta: {lastDeltaMs:F2}ms, Jitter: {jitter:F2}ms");
		// 	}
		// }
		// _lastDelta = delta;
		
		// Entity logic here
		ApplyMovementFromInput(delta);
		
		HandleWorldCollisions(Velocity * (float)delta);
		MoveAndSlide();
		
		// _frameTimer.Stop();
		// double elapsedMs = _frameTimer.Elapsed.TotalMilliseconds;
		// if (elapsedMs > SPIKE_THRESHOLD_MS)
		// {
		// 	GD.Print($"[SLOW FRAME] {GetType().Name} physics took {elapsedMs:F2}ms (>{SPIKE_THRESHOLD_MS}ms)");
		// }
	}

	public virtual void TakeDamage(int amount)
	{
		CurrentHealth -= amount;

		if (CurrentHealth <= 0)
		{
			Die();
		}
	}

	public virtual void TakeDamage(int amount, Vector3 knockbackVector)
	{
		CurrentHealth -= amount;

		if (CurrentHealth <= 0)
		{
			Die();
		}

		// Apply knockback
		Velocity += knockbackVector;
	}

	public virtual void Die()
	{
		QueueFree();
	}

	public virtual void ApplyMovementFromInput(double delta)
	{
		// Placeholder for movement logic
		Velocity = new Vector3(Velocity.X, Mathf.Clamp(Velocity.Y, -MaxFallSpeed, Mathf.Inf), Velocity.Z);
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

	public virtual bool CheckWorldCollisions(Vector3 moveBy)
	{
		Aabb entityBox = GetAABB();
		bool didCollide = false;
		// Check X axis separately
		if (moveBy.X != 0)
		{
			Aabb futureBox = entityBox;
			futureBox.Position += new Vector3(moveBy.X, 0, 0);
			if (CheckAxisCollision(futureBox))
			{
				didCollide = true;
				Velocity = new Vector3(0, Velocity.Y, Velocity.Z);
				moveBy.X = 0;
			}
			else
			{
				entityBox.Position += new Vector3(moveBy.X, 0, 0);  // Apply successful X movement
			}
		}
		
		// Check Y axis separately
		if (moveBy.Y != 0)
		{
			Aabb futureBox = entityBox;
			futureBox.Position += new Vector3(0, moveBy.Y, 0);
			if (CheckAxisCollision(futureBox, true))  // Pass true for Y to detect landing
			{
				Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
				moveBy.Y = 0;
				didCollide = true;
			}
			else
			{
				entityBox.Position += new Vector3(0, moveBy.Y, 0);  // Apply successful Y movement
			}
		}
		
		// Check Z axis separately
		if (moveBy.Z != 0)
		{
			Aabb futureBox = entityBox;
			futureBox.Position += new Vector3(0, 0, moveBy.Z);
			if (CheckAxisCollision(futureBox))
			{
				Velocity = new Vector3(Velocity.X, Velocity.Y, 0);
				moveBy.Z = 0;
				didCollide = true;
			}
			else
			{
				entityBox.Position += new Vector3(0, 0, moveBy.Z);  // Apply successful Z movement
			}
		}
		return didCollide;
	}

	private bool CheckAxisCollision(Aabb futureBox, bool isYAxis = false)
	{
		int minX = (int)Mathf.Floor(futureBox.Position.X);
		int maxX = (int)Mathf.Ceil(futureBox.End.X);
		int minY = (int)Mathf.Floor(futureBox.Position.Y);
		int maxY = (int)Mathf.Ceil(futureBox.End.Y);
		int minZ = (int)Mathf.Floor(futureBox.Position.Z);
		int maxZ = (int)Mathf.Ceil(futureBox.End.Z);
		
		for(int x = minX; x <= maxX; x++)
		{
			for(int y = minY; y <= maxY; y++)
			{
				for(int z = minZ; z <= maxZ; z++)
				{
					Vector3I blockPos = new Vector3I(x, y, z);
					int blockID = Global.CubeManager.get_block(blockPos);
					if(blockID == 0) continue;
					
					Aabb blockBox = new Aabb(
						new Vector3(blockPos.X, blockPos.Y, blockPos.Z),
						new Vector3(1, 1, 1)
					);
					
					if(futureBox.Intersects(blockBox))
					{
						if(isYAxis && Velocity.Y < 0)
						{
							OnLandedOnBlock(blockPos);
						}
						OnBlockCollision(Vector3.Zero, blockPos);
						return true;
					}
				}
			}
		}
		return false;
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
			new Vector3(pos.X, pos.Y, pos.Z),
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



	public virtual bool OnFloor()
	{
		// Get entity's foot-level AABB (slightly below actual position to check for ground)
		Aabb entityBox = GetAABB();
		
		// Create a thin slice just below the entity's feet
		Aabb footCheckBox = new Aabb(
			new Vector3(entityBox.Position.X, entityBox.Position.Y - 0.1f, entityBox.Position.Z),
			new Vector3(entityBox.Size.X, 0.1f, entityBox.Size.Z)
		);
		
		// Calculate the min and max block positions the entity's feet could be over
		int minX = (int)Mathf.Floor(footCheckBox.Position.X);
		int maxX = (int)Mathf.Floor(footCheckBox.End.X);
		int minZ = (int)Mathf.Floor(footCheckBox.Position.Z);
		int maxZ = (int)Mathf.Floor(footCheckBox.End.Z);
		int checkY = (int)Mathf.Floor(footCheckBox.Position.Y);
		
		// Check all blocks the entity could be standing on
		for(int x = minX; x <= maxX; x++)
		{
			for(int z = minZ; z <= maxZ; z++)
			{
				Vector3I blockPos = new Vector3I(x, checkY, z);
				
				int blockID = Global.CubeManager.get_block(blockPos);
				if(blockID == 0) continue; // Air, skip
				
				// Create AABB for this block
				Aabb blockBox = new Aabb(
					new Vector3(blockPos.X, blockPos.Y, blockPos.Z),
					new Vector3(1, 1, 1)
				);
				
				// Check if foot box intersects with this block
				if(footCheckBox.Intersects(blockBox))
				{
					return true;
				}
			}
		}
		
		return false;
	}
}
