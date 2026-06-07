using Godot;

// Medium-health ground enemy. Maintains ideal distance from player, strafes, fires EnemyBolt.
// Light entity — grapple throws it. Auto-jumps over 1-block walls.
// Assign model via RangedEnemy.tscn when art is ready.
public partial class RangedEnemy : Enemy
{
    [Export] public float MoveSpeed   { get; set; } = 4.5f;
    [Export] public float MoveAccel   { get; set; } = 12f;
    [Export] public float IdealRange  { get; set; } = 20f;
    [Export] public float FireRate    { get; set; } = 2.5f;
    [Export] public float BoltSpeed   { get; set; } = 22f;
    [Export] public float JumpImpulse { get; set; } = 10f;
    private const float Gravity = 20f;

    private float _fireCooldown = 0f;
    private float _strafeSign   = 1f;
    private float _strafeTimer  = 0f;
    private bool  _isChasing    = false;

    public override void ImHere()
    {
        base.ImHere();
        MaxHealth      = 80;
        CurrentHealth  = 80;
        AttackDamage   = 15;
        DetectionRange = 30f;
        heavy          = false;
        Flying         = false;
        width          = 0.9f;
        height         = 1.8f;
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

        if (_isChasing)
        {
            var toPlayer = playerPos - GlobalPosition;
            var flatDir  = new Vector3(toPlayer.X, 0f, toPlayer.Z).Normalized();

            // Approach if too far, retreat if too close
            var moveDir = flatDir * Mathf.Sign(dist - IdealRange);

            // Strafe direction flips every 1.5s
            _strafeTimer -= dt;
            if (_strafeTimer <= 0f) { _strafeSign = GD.Randf() > 0.5f ? 1f : -1f; _strafeTimer = 1.5f; }
            var strafe  = new Vector3(-flatDir.Z, 0f, flatDir.X) * _strafeSign;

            var desired = (moveDir + strafe * 0.5f).Normalized() * MoveSpeed;
            vel.X = Mathf.MoveToward(vel.X, desired.X, MoveAccel * dt);
            vel.Z = Mathf.MoveToward(vel.Z, desired.Z, MoveAccel * dt);

            _fireCooldown = Mathf.Max(_fireCooldown - dt, 0f);
            if (_fireCooldown <= 0f && dist <= IdealRange * 1.5f && HasLOS(playerPos))
            {
                FireBolt(toPlayer.Normalized());
                _fireCooldown = FireRate;
            }
        }
        else
        {
            vel.X = Mathf.MoveToward(vel.X, 0f, MoveAccel * dt);
            vel.Z = Mathf.MoveToward(vel.Z, 0f, MoveAccel * dt);
        }

        Velocity = vel;
    }

    // Auto-jump over 1-block walls when chasing and grounded
    protected override void OnBlockCollision(Vector3 faceNormal, Vector3I blockPos)
    {
        if (!_isChasing || !PhysicallyOnFloor()) return;
        int footY = (int)Mathf.Floor(GlobalPosition.Y - height / 2f);
        if (blockPos.Y > footY)
            Velocity = new Vector3(Velocity.X, JumpImpulse, Velocity.Z);
    }

    private void FireBolt(Vector3 direction)
    {
        var bolt = new EnemyBolt { Damage = AttackDamage };
        GetTree().CurrentScene.AddChild(bolt);
        bolt.GlobalPosition = GlobalPosition + Vector3.Up * (height * 0.4f);
        bolt.Velocity       = direction * BoltSpeed;
    }

    private bool HasLOS(Vector3 target)
    {
        var cm = Global?.CubeManager;
        if (cm == null) return false;
        var  origin = GlobalPosition + Vector3.Up * (height * 0.4f);
        var  delta  = target - origin;
        int  steps  = (int)(delta.Length() / 0.5f);
        var  step   = delta.Normalized() * 0.5f;
        var  pos    = origin;
        for (int i = 0; i < steps; i++)
        {
            pos += step;
            if (cm.get_block(new Vector3I((int)pos.X, (int)pos.Y, (int)pos.Z)) != 0)
                return false;
        }
        return true;
    }
}
