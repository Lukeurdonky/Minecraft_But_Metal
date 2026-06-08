using Godot;

// Slow, tanky, ground-based. Heavy entity — player gets pulled toward it when grappled.
// Charge attack at range. Auto-jumps over 1-block walls when chasing.
// Assign model via HeavyEnemy.tscn when art is ready.
public partial class HeavyEnemy : Enemy
{
    [Export] public float WalkSpeed     { get; set; } = 3.5f;
    [Export] public float ChaseAccel    { get; set; } = 8f;
    [Export] public float JumpImpulse   { get; set; } = 10f;
    [Export] public float ChargeRange   { get; set; } = 12f;
    [Export] public float ChargeSpeed   { get; set; } = 18f;
    [Export] public float ChargeDuration{ get; set; } = 0.4f;
    [Export] public float ChargeCooldown{ get; set; } = 4f;
    private const float Gravity = 20f;

    private bool    _isChasing     = false;
    private float   _attackCooldown= 0f;
    private float   _chargeCooldown= 0f;
    private float   _chargeTimer   = 0f;
    private Vector3 _chargeDir     = Vector3.Zero;

    public override void ImHere()
    {
        base.ImHere();
        MaxHealth      = 300;
        CurrentHealth  = 300;
        AttackDamage   = 25;
        DetectionRange = 18f;
        heavy          = true;
        Flying         = false;
        width          = 1.4f;
        height         = 2.2f;
    }

    public override void ApplyMovementFromInput(double delta)
    {
        float   dt        = (float)delta;
        Vector3 playerPos = Global.GetPlayerPos();
        float   dist      = (playerPos - GlobalPosition).Length();
        _isChasing = dist <= DetectionRange;

        var vel = Velocity;

        if (!PhysicallyOnFloor()) vel.Y -= Gravity * dt;
        vel.Y = Mathf.Max(vel.Y, -MaxFallSpeed);

        // Continue charge until timer expires
        if (_chargeTimer > 0f)
        {
            _chargeTimer -= dt;
            vel.X = _chargeDir.X * ChargeSpeed;
            vel.Z = _chargeDir.Z * ChargeSpeed;
            Velocity = vel;
            TryAttackPlayer(dt);
            return;
        }

        _chargeCooldown = Mathf.Max(_chargeCooldown - dt, 0f);

        if (_isChasing)
        {
            var toPlayer = playerPos - GlobalPosition;
            var dir      = toPlayer.Normalized();

            if (dist > ChargeRange && _chargeCooldown <= 0f && PhysicallyOnFloor())
            {
                _chargeDir   = new Vector3(dir.X, 0f, dir.Z).Normalized();
                _chargeTimer = ChargeDuration;
                _chargeCooldown = ChargeCooldown;
            }
            else
            {
                vel.X = Mathf.MoveToward(vel.X, dir.X * WalkSpeed, ChaseAccel * dt);
                vel.Z = Mathf.MoveToward(vel.Z, dir.Z * WalkSpeed, ChaseAccel * dt);
            }
        }
        else
        {
            vel.X = Mathf.MoveToward(vel.X, 0f, ChaseAccel * dt);
            vel.Z = Mathf.MoveToward(vel.Z, 0f, ChaseAccel * dt);
        }

        Velocity = vel;
        TryAttackPlayer(dt);
    }

    // Auto-jump over 1-block walls when chasing and grounded
    protected override void OnBlockCollision(Vector3 faceNormal, Vector3I blockPos)
    {
        if (!_isChasing || !PhysicallyOnFloor()) return;
        int footY = (int)Mathf.Floor(GlobalPosition.Y - height / 2f);
        if (blockPos.Y > footY)
            Velocity = new Vector3(Velocity.X, JumpImpulse, Velocity.Z);
    }

    private void TryAttackPlayer(float dt)
    {
        _attackCooldown = Mathf.Max(_attackCooldown - dt, 0f);
        if (_attackCooldown > 0f) return;
        var player = Global.Instance?.Player;
        if (player == null) return;
        if (!GetAABB().Intersects(player.GetAABB())) return;
        var kb = (player.GlobalPosition - GlobalPosition).Normalized() * 15f;
        player.TakeDamage(AttackDamage, kb);
        _attackCooldown = 1.5f;
    }
}
