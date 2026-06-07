using Godot;

// Fast, fragile, flying mob. Attacks in groups. Light entity — grapple throws it.
// Assign model via SwarmEnemy.tscn when art is ready.
public partial class SwarmEnemy : Enemy
{
    [Export] public float ChaseSpeed { get; set; } = 12f;
    [Export] public float ChaseAccel { get; set; } = 20f;

    private float   _attackCooldown = 0f;
    private Vector3 _jitter         = Vector3.Zero;
    private float   _jitterTimer    = 0f;

    public override void ImHere()
    {
        base.ImHere();
        MaxHealth      = 30;
        CurrentHealth  = 30;
        AttackDamage   = 8;
        DetectionRange = 22f;
        heavy          = false;
        Flying         = true;
        width          = 0.6f;
        height         = 0.7f;
    }

    public override void ApplyMovementFromInput(double delta)
    {
        float   dt        = (float)delta;
        Vector3 playerPos = Global.GetPlayerPos();
        float   dist      = (playerPos - GlobalPosition).Length();
        bool    chasing   = dist <= DetectionRange;

        // Slight random drift so swarms don't all overlap
        _jitterTimer -= dt;
        if (_jitterTimer <= 0f)
        {
            _jitter = new Vector3(
                GD.Randf() * 2f - 1f,
                GD.Randf() * 2f - 1f,
                GD.Randf() * 2f - 1f) * 0.3f;
            _jitterTimer = 0.4f;
        }

        var vel = Velocity;
        if (chasing)
        {
            var dir = ((playerPos - GlobalPosition).Normalized() + _jitter).Normalized();
            vel += dir * ChaseAccel * dt;
            if (vel.Length() > ChaseSpeed) vel = vel.Normalized() * ChaseSpeed;
        }
        else
        {
            vel = vel.MoveToward(Vector3.Zero, ChaseAccel * dt);
        }

        Velocity = vel;
        TryAttackPlayer(dt);
    }

    private void TryAttackPlayer(float dt)
    {
        _attackCooldown = Mathf.Max(_attackCooldown - dt, 0f);
        if (_attackCooldown > 0f) return;
        var player = Global.Instance?.Player;
        if (player == null) return;
        if (!GetAABB().Intersects(player.GetAABB())) return;
        var kb = (player.GlobalPosition - GlobalPosition).Normalized() * 6f;
        player.TakeDamage(AttackDamage, kb);
        _attackCooldown = 0.6f;
    }
}
