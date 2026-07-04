using Godot;

// Explosive Bounce — hooks the existing "ram into a block fast enough and it breaks"
// mechanic (ProcessSpeedThreshold). On top of the base poke-damage, this triggers a much
// bigger explosion at the impact point and reflects the player's velocity (bounce off
// whatever was hit — wall, ceiling, or ground). Cooldown-gated so tunneling through a
// thick wall at speed doesn't fire an explosion+bounce every physics tick.
public class ExplosiveBounceAccessory : Accessory
{
    public override string Name => "Explosive Bounce";

    private const float ExplosionRadius   = 6f; // bigger than Super Slam's jackhammer-impact explosion
    private const float ExplosionStrength = 1.5f;
    private const float BounceMultiplier  = -0.8f; // reflect + dampen incoming velocity
    private const float Cooldown          = 0.5f;

    private float _cooldownTimer = 0f;

    public override void Process(float delta)
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= delta;
    }

    public override void OnSpeedImpact(Vector3 position, float speed)
    {
        if (_cooldownTimer > 0f) return;
        _cooldownTimer = Cooldown;

        var bp = new Vector3I(Mathf.FloorToInt(position.X), Mathf.FloorToInt(position.Y), Mathf.FloorToInt(position.Z));
        Player.Global.CubeManager.explode(bp, ExplosionRadius, ExplosionStrength);
        Player.Velocity *= BounceMultiplier;
    }
}
