using Godot;

public partial class Creature : Enemy
{
    [Export] public float ChaseAccel      { get; set; } = 15.0f;
    [Export] public float LungeSpeed      { get; set; } = 18.0f;
    [Export] public float IdleRotSpeed    { get; set; } = 1.5f;
    [Export] public float GrabDamageStart { get; set; } = 0.7f;  // seconds into Grab anim
    [Export] public float GrabDamageEnd   { get; set; } = 1.3f;
    [Export] public float GrabLungeAccel  { get; set; } = 30.0f;

    [Export] public float AttackRange        { get; set; } = 6.0f;
    [Export] public float KnockbackStrength  { get; set; } = 12f;
    [Export] public float KnockbackUpFactor  { get; set; } = 0.5f;

    private enum State { Idle, Chase, Grab }
    private State           _state        = State.Idle;
    private AnimationPlayer _anim;
    private float           _grabTimer    = 0f;
    private bool            _hitDealt     = false;
    private bool              _lungeApplied = false;
    private Vector3           _lungeDir     = Vector3.Zero;
    private CollisionShape3D  _hitboxShape;
    private Node3D            _mesh;

    public override void ImHere()
    {
        base.ImHere();
        Flying = true;
        _anim        = GetNode<AnimationPlayer>("TentacleCreature/AnimationPlayer");
        _hitboxShape = GetNode<CollisionShape3D>("GrabHitbox/HitboxShape");
        _mesh        = GetNode<Node3D>("TentacleCreature");
        _anim.AnimationFinished += OnAnimationFinished;
        _anim.Play("Idle");
    }

    private void OnAnimationFinished(StringName animName)
    {
        if (animName == "Idle")
            _anim.Play("Idle");          // manual loop — imported clips can't be marked looping
        else if (animName == "Grab")
        {
            var dist = (Global.GetPlayerPos() - GlobalPosition).Length();
            _state = dist <= DetectionRange ? State.Chase : State.Idle;
            _anim.Play("Idle");
        }
    }

    public override void ApplyMovementFromInput(double delta)
    {
        float   dt        = (float)delta;
        Vector3 playerPos = Global.GetPlayerPos();
        var     vel       = Velocity;

        float distToPlayer = (playerPos - GlobalPosition).Length();

        if (_state == State.Idle)
        {
            var toPlayer3D = playerPos - GlobalPosition;
            var flat       = toPlayer3D with { Y = 0f };
            if (flat.LengthSquared() > 0.01f)
            {
                float targetYaw   = Mathf.Atan2(flat.X, flat.Z);
                float targetPitch = -Mathf.Atan2(toPlayer3D.Y, flat.Length());
                Rotation = Rotation with { Y = Mathf.LerpAngle(Rotation.Y, targetYaw, IdleRotSpeed * dt) };
                if (_mesh != null)
                    _mesh.Rotation = _mesh.Rotation with { X = Mathf.LerpAngle(_mesh.Rotation.X, targetPitch, IdleRotSpeed * dt) };
            }

            // Hover in place
            vel.X = Mathf.MoveToward(vel.X, 0f, ChaseAccel * dt);
            vel.Y = Mathf.MoveToward(vel.Y, 0f, ChaseAccel * dt);
            vel.Z = Mathf.MoveToward(vel.Z, 0f, ChaseAccel * dt);

            if (distToPlayer <= DetectionRange)
                _state = State.Chase;
        }
        else if (_state == State.Chase)
        {
            // Face and move toward player — Idle animation keeps playing
            var toPlayer = playerPos - GlobalPosition;
            var flat     = toPlayer with { Y = 0f };
            if (flat.LengthSquared() > 0.01f)
            {
                float targetYaw   = Mathf.Atan2(flat.X, flat.Z);
                float targetPitch = -Mathf.Atan2(toPlayer.Y, flat.Length());
                Rotation = Rotation with { Y = Mathf.LerpAngle(Rotation.Y, targetYaw, IdleRotSpeed * dt) };
                if (_mesh != null)
                    _mesh.Rotation = _mesh.Rotation with { X = Mathf.LerpAngle(_mesh.Rotation.X, targetPitch, IdleRotSpeed * dt) };
            }

            var dir = toPlayer.Normalized();
            vel += dir * ChaseAccel * dt;
            if (vel.Length() > LungeSpeed * 0.5f)
                vel = vel.Normalized() * LungeSpeed * 0.5f;

            // In attack range → trigger grab
            if (distToPlayer <= AttackRange)
            {
                _state        = State.Grab;
                _grabTimer    = 0f;
                _hitDealt     = false;
                _lungeApplied = false;
                _lungeDir = (GlobalTransform.Basis * _mesh.Transform.Basis.Z).Normalized();
                _anim.Play("Grab");
            }
            else if (distToPlayer > DetectionRange)
            {
                _state = State.Idle;
            }
        }
        else // Grab
        {
            _grabTimer += dt;

            if (_grabTimer < GrabDamageStart)
            {
                // Charge: bleed off momentum, hold in place
                vel.X = Mathf.MoveToward(vel.X, 0f, GrabLungeAccel * dt);
                vel.Y = Mathf.MoveToward(vel.Y, 0f, GrabLungeAccel * dt);
                vel.Z = Mathf.MoveToward(vel.Z, 0f, GrabLungeAccel * dt);
            }
            else if (!_lungeApplied)
            {
                vel           = _lungeDir * LungeSpeed;
                _lungeApplied = true;
            }
            else if (_grabTimer > GrabDamageEnd)
            {
                // Recovery: decelerate back to rest
                vel.X = Mathf.MoveToward(vel.X, 0f, GrabLungeAccel * dt);
                vel.Y = Mathf.MoveToward(vel.Y, 0f, GrabLungeAccel * dt);
                vel.Z = Mathf.MoveToward(vel.Z, 0f, GrabLungeAccel * dt);
            }
            // During damage window: carry lunge velocity, no forces applied

            // Damage check — only once per grab
            if (!_hitDealt && _grabTimer >= GrabDamageStart && _grabTimer <= GrabDamageEnd)
            {
                var player = Global.Instance?.Player;
                if (player != null && _hitboxShape != null)
                {
                    var box      = (BoxShape3D)_hitboxShape.Shape;
                    var hitAabb  = new Aabb(_hitboxShape.GlobalPosition - box.Size / 2, box.Size);
                    if (hitAabb.Intersects(player.GetAABB()))
                    {
                        var knockback = (player.GlobalPosition - GlobalPosition).Normalized() * KnockbackStrength;
                        knockback.Y += KnockbackStrength * KnockbackUpFactor;
                        player.TakeDamage(AttackDamage, knockback);
                        _hitDealt = true;
                    }
                }
            }
        }

        Velocity = vel;
    }

}
