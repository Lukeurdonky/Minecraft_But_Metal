using Godot;
using System;

public partial class GrappleHook : Node3D
{
    public Vector3 FireDirection  { get; set; }
    public Node3D  PlayerRef     { get; set; }
    public float   Speed         { get; set; } = 50f;
    public float   RetractSpeed  { get; set; } = 600f;
    public float   MaxDistance   { get; set; } = 120f;

    public Action<Vector3> OnAttach;       // block attach — world-space position
    public Action<Entity>  OnAttachEntity; // entity attach — the entity ref
    public Action          OnRetracted;

    private enum State { Flying, Retracting, Done }
    private State  _state             = State.Flying;
    private float  _distanceTravelled = 0f;
    private Global _global;

    public override void _Ready()
    {
        _global = GetNode<Global>("/root/Global");

        var hitArea = GetNodeOrNull<Area3D>("HitArea");
        if (hitArea != null)
            hitArea.BodyEntered += OnBodyEntered;

        var mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (mesh != null)
        {
            var mat = GD.Load<StandardMaterial3D>("res://Materials/GrappleMaterial.tres");
            mesh.SetSurfaceOverrideMaterial(0, mat);
        }
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_state != State.Flying || body == PlayerRef) return;

        if (body is Entity entity)
        {
            _state = State.Done;
            OnAttachEntity?.Invoke(entity);
            QueueFree();
        }
        else
        {
            Attach(GlobalPosition);
        }
    }

    public override void _Process(double delta)
    {
        switch (_state)
        {
            case State.Flying:   Fly((float)delta);     break;
            case State.Retracting: Retract((float)delta); break;
        }
    }

    public void StartRetract()
    {
        if (_state == State.Flying)
            _state = State.Retracting;
    }

    private void Fly(float dt)
    {
        var step = FireDirection * Speed * dt;
        GlobalPosition      += step;
        _distanceTravelled  += step.Length();

        var blockPos = new Vector3I(
            Mathf.FloorToInt(GlobalPosition.X),
            Mathf.FloorToInt(GlobalPosition.Y),
            Mathf.FloorToInt(GlobalPosition.Z)
        );

        if (_global.CubeManager.get_block(blockPos) != 0)
        {
            Attach(GlobalPosition);
            return;
        }

        if (_distanceTravelled >= MaxDistance)
            _state = State.Retracting;
    }

    private void Retract(float dt)
    {
        if (PlayerRef == null) { Finish(); return; }

        GlobalPosition = GlobalPosition.MoveToward(PlayerRef.GlobalPosition, RetractSpeed * dt);

        if (GlobalPosition.DistanceTo(PlayerRef.GlobalPosition) < 0.5f)
        {
            OnRetracted?.Invoke();
            Finish();
        }
    }

    private void Attach(Vector3 worldPos)
    {
        _state = State.Done;
        OnAttach?.Invoke(worldPos);
        QueueFree();
    }

    private void Finish()
    {
        _state = State.Done;
        QueueFree();
    }
}
