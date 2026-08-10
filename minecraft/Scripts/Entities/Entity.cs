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
	[Export]
	public float onGroundGracePeriod = 0.2f; // Time after leaving ground where jump is still allowed
	public float timeSinceLeftGround = 0f;
	[Export]
	public float onGroundDetectionLength = 0.2f;
	[Export]
	public bool heavy   = true;
	public bool Grappled = false;
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
		CacheCurveVisuals();
	}

	// --------------------- planet curvature (visual only) ---------------------------
	//
	// The terrain bends in a vertex shader; an entity's imported .glb materials know
	// nothing about it, so without this every enemy hovers above — or sinks into — ground
	// that has curved away beneath it.
	//
	// Handled on the CPU by dropping the entity's VISUAL children rather than by giving
	// every model a curved shader, for three reasons: it works with any material including
	// the ones the .glb importer generates, an entity is small enough relative to the curve
	// that bending it internally would be invisible anyway, and — most importantly — the
	// entity's own transform stays flat, so collision, AI and every get_block consumer are
	// untouched. Same render-only guarantee the shader makes.
	//
	// Deliberately NOT done by reparenting the visuals under an offset pivot: AnimationPlayer
	// tracks address their targets by NodePath, and inserting a node would break every
	// animation on every imported enemy.
	private Node3D[] _curveVisuals;
	private float[]  _curveVisualBaseY;

	private void CacheCurveVisuals()
	{
		var visuals = new List<Node3D>();
		foreach (var child in GetChildren())
		{
			// Only things that draw. CollisionShape3D/Area3D must stay put — moving them
			// would turn this into a physics change, which is exactly what it must not be.
			if (child is Node3D n3d && !(child is CollisionShape3D) && !(child is Area3D) && HasVisuals(n3d))
				visuals.Add(n3d);
		}

		_curveVisuals     = visuals.ToArray();
		_curveVisualBaseY = new float[_curveVisuals.Length];
		for (int i = 0; i < _curveVisuals.Length; i++)
			_curveVisualBaseY[i] = _curveVisuals[i].Position.Y;
	}

	private static bool HasVisuals(Node node)
	{
		if (node is VisualInstance3D) return true;
		foreach (var child in node.GetChildren())
			if (HasVisuals(child)) return true;
		return false;
	}

	// Called from _PhysicsProcess so it stays in step with the movement that produced the
	// position — running it in _Process would read a position one tick stale and make fast
	// entities visibly swim against the ground.
	protected void ApplyCurveToVisuals()
	{
		if (_curveVisuals == null || _curveVisuals.Length == 0 || Global == null) return;

		float drop = Global.CurveDropAt(GlobalPosition);
		for (int i = 0; i < _curveVisuals.Length; i++)
		{
			var v = _curveVisuals[i];
			if (v == null || !IsInstanceValid(v)) continue;
			var p = v.Position;
			v.Position = new Vector3(p.X, _curveVisualBaseY[i] - drop, p.Z);
		}
	}

	// Override to false on entities that should keep processing inputs during hitstop (i.e. Player).
	protected virtual bool FreezeDuringHitstop => true;

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

		// Enemies freeze completely. Player still processes inputs (abilities, jump) but
		// physics simulation (gravity, friction, MoveAndSlide) is skipped — see ApplyMovement.
		if (!Grappled && (Global?.HitstopActive != true || !FreezeDuringHitstop))
			ApplyMovementFromInput(delta);

		if (Global?.HitstopActive == true) return;

		HandleWorldCollisions(Velocity * (float)delta);
		MoveAndSlide();

		// After MoveAndSlide, so the visual drop matches the position actually arrived at.
		ApplyCurveToVisuals();
		
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

	protected bool CheckAxisCollision(Aabb futureBox, bool isYAxis = false)
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



	// True only when physically touching a block below — no grace period.
	// Use this for friction and movement caps. Use OnFloor() for jump eligibility.
	public virtual bool PhysicallyOnFloor()
	{
		Aabb entityBox   = GetAABB();
		Aabb footCheckBox = new Aabb(
			new Vector3(entityBox.Position.X, entityBox.Position.Y - onGroundDetectionLength, entityBox.Position.Z),
			new Vector3(entityBox.Size.X, 0.1f, entityBox.Size.Z)
		);

		int minX   = (int)Mathf.Floor(footCheckBox.Position.X);
		int maxX   = (int)Mathf.Floor(footCheckBox.End.X);
		int minZ   = (int)Mathf.Floor(footCheckBox.Position.Z);
		int maxZ   = (int)Mathf.Floor(footCheckBox.End.Z);
		int checkY = (int)Mathf.Floor(footCheckBox.Position.Y);

		for (int x = minX; x <= maxX; x++)
		{
			for (int z = minZ; z <= maxZ; z++)
			{
				int blockID = Global.CubeManager.get_block(new Vector3I(x, checkY, z));
				if (blockID == 0) continue;
				Aabb blockBox = new Aabb(new Vector3(x, checkY, z), new Vector3(1, 1, 1));
				if (footCheckBox.Intersects(blockBox)) return true;
			}
		}
		return false;
	}

	public virtual bool OnFloor()
	{
		Aabb entityBox   = GetAABB();
		Aabb footCheckBox = new Aabb(
			new Vector3(entityBox.Position.X, entityBox.Position.Y - onGroundDetectionLength, entityBox.Position.Z),
			new Vector3(entityBox.Size.X, 0.1f, entityBox.Size.Z)
		);

		int minX   = (int)Mathf.Floor(footCheckBox.Position.X);
		int maxX   = (int)Mathf.Floor(footCheckBox.End.X);
		int minZ   = (int)Mathf.Floor(footCheckBox.Position.Z);
		int maxZ   = (int)Mathf.Floor(footCheckBox.End.Z);
		int checkY = (int)Mathf.Floor(footCheckBox.Position.Y);

		for (int x = minX; x <= maxX; x++)
		{
			for (int z = minZ; z <= maxZ; z++)
			{
				int blockID = Global.CubeManager.get_block(new Vector3I(x, checkY, z));
				if (blockID == 0) continue;
				Aabb blockBox = new Aabb(new Vector3(x, checkY, z), new Vector3(1, 1, 1));
				if (footCheckBox.Intersects(blockBox))
				{
					timeSinceLeftGround = 0f;
					return true;
				}
			}
		}

		// Grace period — still jumpable, but not physically touching
		if (timeSinceLeftGround < onGroundGracePeriod)
			return true;

		return false;
	}
}
