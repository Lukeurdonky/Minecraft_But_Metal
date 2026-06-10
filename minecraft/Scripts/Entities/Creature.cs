using Godot;

public partial class Creature : Enemy
{
    [Export] public float ChaseAccel      { get; set; } = 15.0f;
    [Export] public float LungeSpeed      { get; set; } = 18.0f;
    [Export] public float IdleRotSpeed    { get; set; } = 1.5f;
    [Export] public float GrabDamageStart { get; set; } = 0.5f;  // seconds into Grab anim
    [Export] public float GrabDamageEnd   { get; set; } = 1.3f;
    [Export] public float GrabLungeAccel  { get; set; } = 30.0f;

    private enum State { Idle, Grab }
    private State           _state     = State.Idle;
    private AnimationPlayer _anim;
    private float           _grabTimer = 0f;
    private bool            _hitDealt  = false;

    public override void ImHere()
    {
        base.ImHere();
        Flying = true;
        _anim = GetNode<AnimationPlayer>("TentacleCreature/AnimationPlayer");
        _anim.AnimationFinished += OnAnimationFinished;
        _anim.Play("Idle");
    }

    private void OnAnimationFinished(StringName animName)
    {
        if (animName == "Idle")
            _anim.Play("Idle");          // manual loop — imported clips can't be marked looping
        else if (animName == "Grab")
        {
            _state = State.Idle;
            _anim.Play("Idle");
        }
    }

    public override void ApplyMovementFromInput(double delta)
    {
        float   dt        = (float)delta;
        Vector3 playerPos = Global.GetPlayerPos();
        var     vel       = Velocity;

        if (_state == State.Idle)
        {
            // Slowly Y-rotate toward player
            var flat = playerPos - GlobalPosition;
            flat.Y = 0f;
            if (flat.LengthSquared() > 0.01f)
            {
                float targetAngle = Mathf.Atan2(flat.X, flat.Z);
                Rotation = Rotation with { Y = Mathf.LerpAngle(Rotation.Y, targetAngle, IdleRotSpeed * dt) };
            }

            // Hover in place
            vel.X = Mathf.MoveToward(vel.X, 0f, ChaseAccel * dt);
            vel.Y = Mathf.MoveToward(vel.Y, 0f, ChaseAccel * dt);
            vel.Z = Mathf.MoveToward(vel.Z, 0f, ChaseAccel * dt);

            // LOS trigger
            if (HasLineOfSight(playerPos))
            {
                _state     = State.Grab;
                _grabTimer = 0f;
                _hitDealt  = false;
                _anim.Play("Grab");
            }
        }
        else // Grab
        {
            _grabTimer += dt;

            // Lunge toward player
            var dir = (playerPos - GlobalPosition).Normalized();
            vel += dir * GrabLungeAccel * dt;
            if (vel.Length() > LungeSpeed)
                vel = vel.Normalized() * LungeSpeed;

            // Damage window — only once per grab
            if (!_hitDealt && _grabTimer >= GrabDamageStart && _grabTimer <= GrabDamageEnd)
            {
                var player = Global.Instance?.Player;
                if (player != null && GetAABB().Intersects(player.GetAABB()))
                {
                    var knockback = (player.GlobalPosition - GlobalPosition).Normalized() * 12f;
                    player.TakeDamage(AttackDamage, knockback);
                    _hitDealt = true;
                }
            }
        }

        Velocity = vel;
    }

    // Casts against terrain only (layer 1) so the player body doesn't block LOS.
    private bool HasLineOfSight(Vector3 playerPos)
    {
        var space  = GetWorld3D().DirectSpaceState;
        var query  = PhysicsRayQueryParameters3D.Create(GlobalPosition, playerPos, collisionMask: 1);
        var result = space.IntersectRay(query);
        return result.Count == 0;
    }
}
