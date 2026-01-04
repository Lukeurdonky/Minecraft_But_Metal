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
    public float Speed { get; set; } = 5.0f;  // Movement speed
    
    [Export]
    public float SprintMult { get; set; } = 2.0f;  // Sprint Multiplier
    
    [Export]
    public float Accel { get; set; } = 5.0f;
    
    [Export]
    public float JumpStrength { get; set; } = 10.0f;  // Jump force
    
    [Export]
    public float Gravity { get; set; } = 9.8f;

    private float pitch = 0.0f;
    private float yaw = 0.0f;

    public int SelectedCube { get; set; }
    public Vector3I SelectedCubePosition { get; set; }
    public bool IsSprinting { get; set; } = false;
    public bool MouseVisible { get; set; } = false;
    public bool SpectatorMode { get; set; } = false;

    private Vector3 forwardDirection = Vector3.Zero;
    private Vector3 rightDirection = Vector3.Zero;
    private Vector3 direction = Vector3.Zero;  // Movement direction

    private Global Global;

    public override void ImHere()
    {
        base.ImHere();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Global = GetNode<Global>("/root/Global");
        Global.Player = this;
        if (Camera != null)
            Camera.Current = true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            if (!MouseVisible)
            {
                // Adjust yaw (horizontal rotation) and pitch (vertical rotation)
                yaw -= mouseMotion.Relative.X * Global.SensitivityX;
                pitch -= mouseMotion.Relative.Y * Global.SensitivityY;
                
                // Clamp pitch to avoid flipping the camera
                pitch = Mathf.Clamp(pitch, Global.MinPitch, Global.MaxPitch);
            }
        }
    }

    public override void HandleWorldCollisions(Vector3 moveBy)
    {
        // Placeholder for world collision handling
        if(!SpectatorMode) CheckWorldCollisions(moveBy);
    }

    public override void ApplyMovementFromInput(double delta)
    {
        RotateCamera();
        ApplyMovement(delta);
    }

    private void ApplyMovement(double delta)
    {
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
        if (!IsOnFloor())
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
        
        if ((IsOnFloor() || SpectatorMode) && Input.IsActionPressed("jump"))
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
        
        MoveAndSlide();
    }

    public void RotateCamera()
    {
        if (Camera == null) return;
        
        // Rotate the camera based on pitch and yaw
        Camera.RotationDegrees = new Vector3(pitch, yaw, 0);
    }

    private void UpdateFacingDirections()
    {
        if (Camera == null) return;
        
        // Update the forward and right directions based on the camera's rotation
        float rot = Camera.RotationDegrees.Y;
        forwardDirection = Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(rot));
        rightDirection = Vector3.Right.Rotated(Vector3.Up, Mathf.DegToRad(rot));
    }

    private void ToggleSpectator()
    {
        SpectatorMode = !SpectatorMode;
        if (CollisionShape != null)
        {
            CollisionShape.Disabled = SpectatorMode;
        }
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
        //do a thing
        CurrentHealth -= amount;

        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    public override void Die()
    {
        QueueFree();
    }
}
