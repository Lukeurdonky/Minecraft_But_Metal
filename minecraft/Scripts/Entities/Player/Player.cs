using Godot;
using System;
using System.Collections.Generic;

public partial class Player : Entity
{
	[Export]
	public Camera3D Camera { get; set; }
	
	[Export]
	public CollisionShape3D CollisionShape { get; set; }
	
	[Export]
	public Node3D Inventory { get; set; }
	[Export]
	public MeshInstance3D HandMesh { get; set; }
	
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
	[Export]
	public float PickUpRange { get; set; } = 4.0f;

	// How fast the player glides up a step (units per second).
	// At 8.0 a 1-block step takes ~0.125 s — snappy but visible.
	// Raise for faster traversal, lower for a more deliberate climb.
	[Export]
	public float StepUpSpeed { get; set; } = 12.0f;
	

	private float pitch = 0.0f;
	private float yaw = 0.0f;

	public int SelectedCube { get; set; }
	public Vector3I SelectedCubePosition { get; set; }
	public bool IsSprinting { get; set; } = false;
	public bool MouseVisible { get; set; } = false;
	public bool SpectatorMode { get; set; } = false;

	private Vector3 forwardDirection = Vector3.Zero;
	private Vector3 rightDirection = Vector3.Zero;
	private Vector3 direction = Vector3.Zero;

	// ── Step-up traversal state ──────────────────────────────────────────────
	// When the player steps onto a block we record how much vertical distance
	// remains and smoothly close the gap every _PhysicsProcess tick.
	private float stepRemainder = 0f;   // remaining Y to travel upward
	private const float STEP_HEIGHT    = 1.0f;   // maximum climbable step (blocks)
	private const float STEP_THRESHOLD = 0.001f; // snap-to-zero below this

	private Area3D pickupArea;

	public override void ImHere()
	{
		base.ImHere();
		Input.MouseMode = Input.MouseModeEnum.Captured;
		Global = GetNode<Global>("/root/Global");
		Global.Player = this;
		if (Camera != null)
			Camera.Current = true;
		
		pickupArea = new Area3D();
		pickupArea.Name = "PickupArea";
		pickupArea.CollisionLayer = 0;
		pickupArea.CollisionMask = 4;
		pickupArea.Monitoring = true;
		
		var shape = new CollisionShape3D();
		shape.Shape = new SphereShape3D { Radius = PickUpRange };
		pickupArea.AddChild(shape);
		
		AddChild(pickupArea);
		
		pickupArea.BodyEntered += OnPickupAreaEntered;
		pickupArea.BodyExited += OnPickupAreaExited;
	}

	public override void _Process(double delta)
	{
		RotateCamera();
		// ApplyStepTraversal((float)delta);
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
		timeSinceLeftGround += (float)delta; // Increment timer while in grace period
		ApplyMovement(delta);
	}

	private void ApplyMovement(double delta)
	{
		bool IsOnFloor = OnFloor();
		if (Input.IsActionJustPressed("toggle_mouse"))
		{
			ToggleMouseVisibility();
		}
		
		direction = Vector3.Zero;
		float tempSpeed = Speed;
		float max = Speed;
		
		if (Input.IsActionPressed("sprint"))
		{
			TogglePlayerSprint(true);
		}
		
		if (Input.IsActionJustPressed("toggle_spectator"))
		{
			ToggleSpectator();
		}
		
		UpdateFacingDirections();
		
		if (Input.IsActionPressed("move_forward"))
		{
			direction += forwardDirection;
		}
		else
		{
			TogglePlayerSprint(false);
		}
		
		if (Input.IsActionPressed("move_back"))
		{
			direction -= forwardDirection;
			TogglePlayerSprint(false);
		}
		
		if (Input.IsActionPressed("move_left"))
		{
			direction -= rightDirection;
		}
		
		if (Input.IsActionPressed("move_right"))
		{
			direction += rightDirection;
		}
		
		if (IsSprinting)
		{
			tempSpeed *= SprintMult;
			max *= SprintMult;
		}
		
		float fricMult = Global.GroundFriction;
		if (!IsOnFloor)
		{
			tempSpeed /= 8;
			fricMult = Global.AirFriction;
		}
		
		direction = direction.Normalized() * tempSpeed;
		
		Velocity = new Vector3(Velocity.X * fricMult, Velocity.Y, Velocity.Z * fricMult);
		
		Velocity = new Vector3(
			Velocity.X + direction.X * (float)delta * Accel,
			Velocity.Y,
			Velocity.Z + direction.Z * (float)delta * Accel
		);
		
		Velocity = new Vector3(Velocity.X, 0, Velocity.Z).LimitLength(max) + new Vector3(0, Velocity.Y, 0);
		
		if (!SpectatorMode)
		{
			Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * (float)delta, Velocity.Z);
		}
		else
		{
			Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
		}
		
		if ((IsOnFloor || SpectatorMode) && Input.IsActionPressed("jump") && Velocity.Y <= .25f)
		{
			Velocity = new Vector3(Velocity.X, JumpStrength, Velocity.Z);
		}
		
		if (Input.IsActionPressed("crouch"))
		{
			if (SpectatorMode)
			{
				Velocity = new Vector3(Velocity.X, -JumpStrength, Velocity.Z);
			}
		}

		Velocity = new Vector3(Velocity.X, Mathf.Clamp(Velocity.Y, -MaxFallSpeed, Mathf.Inf), Velocity.Z);
	}

	public void RotateCamera()
	{
		if (Camera == null) return;
		Camera.RotationDegrees = new Vector3(pitch, yaw, 0);
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
		CurrentHealth -= amount;
		if (CurrentHealth <= 0)
			Die();
	}

	private void OnPickupAreaEntered(Node3D body)
	{
		// Item pickup detection disabled during testing
	}

	private void OnPickupAreaExited(Node3D body)
	{
		// Item pickup detection disabled during testing
	}

	public override void Die()
	{
		QueueFree();
	}
}
