using Godot;
using System;
using System.Collections.Generic;

public partial class Player : Entity
{
	[Export]
	public Camera3D Camera { get; set; }
	
	[Export]
	public CollisionShape3D CollisionShape { get; set; }
	
	// ARCHIVED: Inventory and HandMesh removed — Minecraft item system archived.
	// public Node3D Inventory { get; set; }
	// public MeshInstance3D HandMesh { get; set; }

	[Export]
	public float Speed { get; set; } = 5.0f;
	
	[Export]
	public float SprintMult { get; set; } = 2.0f;
	
	[Export]
	public float Accel { get; set; } = 5.0f;
	
	[Export]
	public float JumpStrength { get; set; } = 10.0f;
	
	[Export]
	public float Gravity { get; set; } = 9.8f;

	// ARCHIVED: PickUpRange removed — item pickup system archived.
	// public float PickUpRange { get; set; } = 4.0f;

	// How fast the player glides up a step (units per second).
	// At 8.0 a 1-block step takes ~0.125 s — snappy but visible.
	// Raise for faster traversal, lower for a more deliberate climb.
	[Export]
	public float StepUpSpeed { get; set; } = 12.0f;
	

	private float pitch = 0.0f;
	private float yaw = 0.0f;

	public int SelectedCube { get; set; }
	public Vector3I SelectedCubePosition { get; set; }
	public bool IsSprinting  { get; set; } = false;
	public bool MouseVisible  { get; set; } = false;
	public bool SpectatorMode { get; set; } = false;
	public bool IsDead        { get; private set; } = false;

	[Export] public Control DeathScreen { get; set; }

	private Vector3 forwardDirection = Vector3.Zero;
	private Vector3 rightDirection   = Vector3.Zero;
	private Vector3 direction        = Vector3.Zero;

	// All movement operates on Velocity directly. Friction only applied when steering.

	// Air jump state — shared with PlayerAbilities via partial class.
	// _airJumps is set to 1 on leaving ground and incremented by grapple attach.
	private int  _airJumps       = 0;
	private bool _wasPhysOnFloor = true;

	public int    AirJumpsAvailable => _airJumps;
	public Entity SelectedEnemy     { get; private set; }
	public bool   HasGrappleTarget  { get; private set; }

	private SphereShape3D _enemySelectShape;
	private const float EnemySelectCone = 0.98f;

	// ── Step-up traversal state ──────────────────────────────────────────────
	// When the player steps onto a block we record how much vertical distance
	// remains and smoothly close the gap every _PhysicsProcess tick.
	private float stepRemainder = 0f;
	private const float STEP_HEIGHT    = 1.0f;
	private const float STEP_THRESHOLD = 0.001f;

	public override void ImHere()
	{
		base.ImHere();
		Input.MouseMode = Input.MouseModeEnum.Captured;
		Global = GetNode<Global>("/root/Global");
		Global.Player = this;
		if (Camera != null)
			Camera.Current = true;

		// ARCHIVED: Pickup area removed — item pickup system archived.
	}

	public override void _Process(double delta)
	{
		if (IsDead)
		{
			if (Input.IsActionJustPressed("jump"))
				GetTree().ReloadCurrentScene();
			return;
		}
		RotateCamera();
		UpdateEnemySelection();
		UpdateGrappleTargetCheck();
		UpdateGrappleRope();
		UpdateArmBlendShapes((float)delta);
		UpdateLeftArmTracking((float)delta);
		// ApplyStepTraversal((float)delta);
	}

	private void UpdateGrappleTargetCheck()
	{
		if (SelectedEnemy != null) { HasGrappleTarget = true; return; }
		if (Camera == null || Global?.CubeManager == null) { HasGrappleTarget = false; return; }

		var origin  = Camera.GlobalPosition;
		var lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();

		const float step = 1.0f;
		for (float t = step; t <= GrappleRange; t += step)
		{
			var p  = origin + lookDir * t;
			var bp = new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
			if (Global.CubeManager.get_block(bp) != 0) { HasGrappleTarget = true; return; }
		}

		HasGrappleTarget = false;
	}

	private void UpdateEnemySelection()
	{
		if (Camera == null) { SelectedEnemy = null; return; }

		var lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();

		_enemySelectShape ??= new SphereShape3D { Radius = GrappleRange };

		var query = new PhysicsShapeQueryParameters3D
		{
			Shape         = _enemySelectShape,
			Transform     = new Transform3D(Basis.Identity, GlobalPosition),
			CollisionMask = 2
		};

		var results = GetWorld3D().DirectSpaceState.IntersectShape(query);

		Entity best    = null;
		float  bestDot = EnemySelectCone;

		foreach (var result in results)
		{
			if (result["collider"].AsGodotObject() is not Entity entity) continue;
			float dot = lookDir.Dot((entity.GetCenter() - Camera.GlobalPosition).Normalized());
			if (dot <= bestDot) continue;
			if (!HasBlockLOS(Camera.GlobalPosition, entity.GetCenter())) continue;
			bestDot = dot;
			best    = entity;
		}

		SelectedEnemy = best;
	}

	private bool HasBlockLOS(Vector3 from, Vector3 to)
	{
		var   toTarget = to - from;
		float dist     = toTarget.Length();
		if (dist < 0.01f) return true;
		var         dir  = toTarget / dist;
		const float step = 0.5f;
		for (float t = step; t < dist - 0.5f; t += step)
		{
			var p  = from + dir * t;
			var bp = new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
			if (Global.CubeManager.get_block(bp) != 0) return false;
		}
		return true;
	}

	// ── Smooth step traversal ────────────────────────────────────────────────
	// Called every _Process frame so the visual rise is independent of the
	// physics tick rate. We move Position.Y directly here; the collision
	// system already approved the destination in AttemptStepUp.
	private void ApplyStepTraversal(float delta)
	{
		if (stepRemainder <= STEP_THRESHOLD)
		{
			stepRemainder = 0f;
			return;
		}

		float move = Mathf.Min(StepUpSpeed * delta, stepRemainder);
		Position += new Vector3(0f, move, 0f);
		stepRemainder -= move;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseMotion)
		{
			if (!MouseVisible)
			{
				yaw   -= mouseMotion.Relative.X * Global.SensitivityX;
				pitch -= mouseMotion.Relative.Y * Global.SensitivityY;
				pitch  = Mathf.Clamp(pitch, Global.MinPitch, Global.MaxPitch);
			}
		}
	}

	public override void HandleWorldCollisions(Vector3 moveBy)
	{
		if (!SpectatorMode) CheckWorldCollisionsWithStepUp(moveBy);
	}

	private void CheckWorldCollisionsWithStepUp(Vector3 moveBy)
	{
		Aabb entityBox = GetAABB();
		
		// ── X axis ──────────────────────────────────────────────────────────
		if (moveBy.X != 0)
		{
			Aabb futureBox = entityBox;
			futureBox.Position += new Vector3(moveBy.X, 0, 0);
			if (CheckAxisCollision(futureBox))
			{
				if (AttemptStepUp(moveBy.X, 0, entityBox))
				{
					entityBox = GetAABB();
				}
				else
				{
					Velocity = new Vector3(0, Velocity.Y, Velocity.Z);
					moveBy.X = 0;
				}
			}
			else
			{
				entityBox.Position += new Vector3(moveBy.X, 0, 0);
			}
		}
		
		// ── Y axis ──────────────────────────────────────────────────────────
		if (moveBy.Y != 0)
		{
			Aabb futureBox = entityBox;
			futureBox.Position += new Vector3(0, moveBy.Y, 0);
			if (CheckAxisCollision(futureBox, true))
			{
				Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
				moveBy.Y = 0;
			}
			else
			{
				entityBox.Position += new Vector3(0, moveBy.Y, 0);
			}
		}
		
		// ── Z axis ──────────────────────────────────────────────────────────
		if (moveBy.Z != 0)
		{
			Aabb futureBox = entityBox;
			futureBox.Position += new Vector3(0, 0, moveBy.Z);
			if (CheckAxisCollision(futureBox))
			{
				if (AttemptStepUp(0, moveBy.Z, entityBox))
				{
					entityBox = GetAABB();
				}
				else
				{
					Velocity = new Vector3(Velocity.X, Velocity.Y, 0);
					moveBy.Z = 0;
				}
			}
			else
			{
				entityBox.Position += new Vector3(0, 0, moveBy.Z);
			}
		}
	}

	// ── AttemptStepUp ────────────────────────────────────────────────────────
	// Checks whether the destination at +1 block height is clear.
	// On success it records the remaining climb into stepRemainder so
	// ApplyStepTraversal can glide the player up smoothly. The physics
	// position is NOT changed here — only the visual traversal is queued.
	private bool AttemptStepUp(float moveX, float moveZ, Aabb currentBox)
	{
		// Aabb testBox = currentBox;
		// testBox.Position += new Vector3(0f, STEP_HEIGHT, 0f);
		// testBox.Position += new Vector3(moveX, 0f, moveZ);

		// if (!CheckAxisCollision(testBox))
		// {
		// 	// Accumulate rather than replace so rapid step sequences don't
		// 	// reset mid-climb. Cap at STEP_HEIGHT to avoid compounding.
		// 	stepRemainder = Mathf.Min(stepRemainder + STEP_HEIGHT, STEP_HEIGHT);
		// 	return true;
		// }

		return false;
	}

	public override void ApplyMovementFromInput(double delta)
	{
		if (IsDead) return;
		timeSinceLeftGround += (float)delta;
		ApplyMovement(delta);
		ProcessAbilities((float)delta);
	}

	private void ApplyMovement(double delta)
	{
		bool isOnFloor     = OnFloor();
		bool isPhysOnFloor = PhysicallyOnFloor();

		if (!_wasPhysOnFloor && isPhysOnFloor) _airJumps = 0;
		if (_wasPhysOnFloor && !isPhysOnFloor) _airJumps = Mathf.Max(_airJumps, 1);
		_wasPhysOnFloor = isPhysOnFloor;
		float dt = (float)delta;

		if (Input.IsActionJustPressed("toggle_mouse"))     ToggleMouseVisibility();
		if (Input.IsActionJustPressed("toggle_spectator")) ToggleSpectator();

		direction = Vector3.Zero;
		UpdateFacingDirections();

		if (Input.IsActionPressed("move_forward")) direction += forwardDirection;
		else TogglePlayerSprint(false);
		if (Input.IsActionPressed("move_back"))  { direction -= forwardDirection; TogglePlayerSprint(false); }
		if (Input.IsActionPressed("move_left"))  direction -= rightDirection;
		if (Input.IsActionPressed("move_right")) direction += rightDirection;
		if (Input.IsActionPressed("sprint"))     TogglePlayerSprint(true);

		bool  hasInput   = direction.LengthSquared() > 0f;
		float inputSpeed = Speed * (IsSprinting ? SprintMult : 1f);

		var vel      = Velocity;
		var inputDir = hasInput ? direction.Normalized() : Vector3.Zero;
		var inputDir2D = new Vector2(inputDir.X, inputDir.Z);

		if (isPhysOnFloor)
		{
			float fric = Mathf.Pow(Global.GroundFriction, dt * 60f);
			vel.X *= fric;
			vel.Z *= fric;
		}
		else if (hasInput)
		{
			// Skip friction if we're already exceeding inputSpeed in the desired direction —
			// preserves grapple/dash momentum when steering into it (Ultrakill-style).
			float speedInDir = new Vector2(vel.X, vel.Z).Dot(inputDir2D);
			if (speedInDir < inputSpeed)
			{
				float fric = Mathf.Pow(Global.AirFriction, dt * 60f);
				vel.X *= fric;
				vel.Z *= fric;
			}
		}

		if (hasInput)
		{
			float accel = isPhysOnFloor ? Accel : Accel / 8f;
			// Only add up to inputSpeed in the input direction so WASD can't exceed the speed
			// cap, but existing ability momentum above that cap is never removed.
			float currentInDir = new Vector2(vel.X, vel.Z).Dot(inputDir2D);
			float addSpeed     = Mathf.Clamp(inputSpeed - currentInDir, 0f, accel * dt);
			vel.X += inputDir.X * addSpeed;
			vel.Z += inputDir.Z * addSpeed;
		}

		if (!SpectatorMode)
			vel.Y -= Gravity * dt;
		else
			vel.Y = 0f;

		if ((isOnFloor || SpectatorMode) && Input.IsActionPressed("jump") && vel.Y <= 0.25f)
			vel.Y = JumpStrength;
		else if (!isOnFloor && !SpectatorMode && Input.IsActionJustPressed("jump")
			&& CurrentGrappleState == GrappleState.Attached && _grappledEntity != null
			&& _grappleJumpCooldown <= 0f)
		{
			vel.Y                = JumpStrength;
			_grappleJumpCooldown = 0.1f;
			_airJumps            = 0;
			var lungeDir = Vector3.Zero;
			if (Input.IsActionPressed("move_back"))  lungeDir -= forwardDirection;
			if (Input.IsActionPressed("move_left"))  lungeDir -= rightDirection;
			if (Input.IsActionPressed("move_right")) lungeDir += rightDirection;
			if (lungeDir.LengthSquared() > 0.01f)
			{
				lungeDir = lungeDir.Normalized();
				vel.X    = lungeDir.X * DashStrength;
				vel.Z    = lungeDir.Z * DashStrength;
			}
		}
		else if (!isOnFloor && !SpectatorMode && Input.IsActionJustPressed("jump") && _airJumps > 0)
		{
			_airJumps--;
			vel.Y = JumpStrength;
		}

		if (SpectatorMode && Input.IsActionPressed("crouch"))
			vel.Y = -JumpStrength;

		vel.Y    = Mathf.Clamp(vel.Y, -MaxFallSpeed, Mathf.Inf);
		Velocity = vel;
	}

	public void RotateCamera()
	{
		if (Camera == null) return;
		float shake = Global.Instance?.CurrentShake ?? 0f;
		float sp    = shake > 0f ? (GD.Randf() - 0.5f) * 2f * shake : 0f;
		float sy    = shake > 0f ? (GD.Randf() - 0.5f) * 2f * shake : 0f;
		Camera.RotationDegrees = new Vector3(pitch + sp, yaw + sy, 0);
	}

	private void UpdateFacingDirections()
	{
		if (Camera == null) return;
		float rot = Camera.RotationDegrees.Y;
		forwardDirection = Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(rot));
		rightDirection   = Vector3.Right.Rotated(Vector3.Up, Mathf.DegToRad(rot));
	}

	private void ToggleSpectator()
	{
		SpectatorMode = !SpectatorMode;
		if (CollisionShape != null)
			CollisionShape.Disabled = SpectatorMode;
	}

	private void TogglePlayerSprint(bool flag)
	{
		IsSprinting = flag;
	}

	private void ToggleMouseVisibility()
	{
		if (Input.MouseMode != Input.MouseModeEnum.Visible)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			MouseVisible = true;
		}
		else
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			MouseVisible = false;
		}
	}

	public override void TakeDamage(int amount)
	{
		if (IsDead) return;
		CurrentHealth -= amount;
		if (CurrentHealth <= 0)
			Die();
	}

	public override void TakeDamage(int amount, Vector3 knockback)
	{
		if (IsDead) return;
		Velocity += knockback;
		Global.Instance?.ShakeCamera(0.8f, 0.3f);
		TakeDamage(amount);
	}

	// ARCHIVED: OnPickupAreaEntered / OnPickupAreaExited removed — item pickup system archived.

	public override void Die()
	{
		IsDead = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		CancelGrapple();
		if (DeathScreen != null)
			DeathScreen.Visible = true;
		else
			GetNodeOrNull<Control>("CanvasLayer/DeathScreen")?.Show();
	}
}
