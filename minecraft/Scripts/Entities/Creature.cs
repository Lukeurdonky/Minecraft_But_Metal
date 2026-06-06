using Godot;

public partial class Creature : Enemy
{
    [Export] public float WalkSpeed  { get; set; } = 3.0f;
    [Export] public float ChaseSpeed { get; set; } = 6.0f;
    [Export] public float ChaseAccel { get; set; } = 15.0f;

    private Vector3 targetPosition  = Vector3.Zero;
    private bool    isChasing       = false;
    private float   _attackCooldown = 0f;

    public override void ImHere()
    {
        base.ImHere();
        Flying = true;
    }

    public override void ApplyMovementFromInput(double delta)
    {
        float   dt           = (float)delta;
        Vector3 playerPos    = Global.GetPlayerPos();
        float   distToPlayer = (playerPos - GlobalTransform.Origin).Length();

        isChasing = distToPlayer <= DetectionRange;
        if (isChasing) targetPosition = playerPos;

        var vel = Velocity;

        if (isChasing)
        {
            var dir = (targetPosition - GlobalTransform.Origin).Normalized();
            vel.X += dir.X * ChaseAccel * dt;
            vel.Z += dir.Z * ChaseAccel * dt;
            if (Flying) vel.Y += dir.Y * ChaseAccel * dt;

            if (Flying)
            {
                if (vel.Length() > ChaseSpeed)
                    vel = vel.Normalized() * ChaseSpeed;
            }
            else
            {
                var horiz = new Vector2(vel.X, vel.Z);
                if (horiz.Length() > ChaseSpeed)
                {
                    horiz = horiz.Normalized() * ChaseSpeed;
                    vel.X = horiz.X;
                    vel.Z = horiz.Y;
                }
            }
        }
        else
        {
            vel.X = Mathf.MoveToward(vel.X, 0f, ChaseAccel * dt);
            vel.Z = Mathf.MoveToward(vel.Z, 0f, ChaseAccel * dt);
            if (Flying) vel.Y = Mathf.MoveToward(vel.Y, 0f, ChaseAccel * dt);
        }

        if (!Flying)
            vel.Y = Mathf.Clamp(vel.Y, -MaxFallSpeed, Mathf.Inf);

        Velocity = vel;

        TryAttackPlayer(dt);
    }

    private void TryAttackPlayer(float dt)
    {
        _attackCooldown = Mathf.Max(_attackCooldown - dt, 0f);
        if (_attackCooldown > 0f) return;

        var player = Global.Instance?.Player;
        if (player == null) return;

        if (GetAABB().Intersects(player.GetAABB()))
        {
            var knockback = (player.GlobalPosition - GlobalPosition).Normalized() * 8f;
            player.TakeDamage(AttackDamage, knockback);
            _attackCooldown = 1.0f;
        }
    }
}
