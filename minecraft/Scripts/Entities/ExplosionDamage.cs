using Godot;

// Pure gameplay backend for explosion damage — no visuals. Spawn() places it,
// it immediately damages every enemy within Radius, applies distance-falloff
// knockback out to a slightly larger band (knockback-only past Radius), then
// frees itself. Visuals are the caller's job — pair with a premade effect scene
// (e.g. Player.SpawnSmallExplosion) instantiated alongside.
public partial class ExplosionDamage : Node3D
{
    public int   Damage    = 0;
    public float Radius    = 3f;
    public float Knockback = 20f;

    // Enemies between Radius and Radius * this still get shoved, just not damaged.
    private const float KnockbackBandMult = 1.35f;
    // Knockback falloff floor — even at the outer edge the shove is this fraction.
    private const float MinFalloff = 0.25f;

    public static void Spawn(Node context, Vector3 pos, int damage, float radius, float knockback)
    {
        var e = new ExplosionDamage { Damage = damage, Radius = radius, Knockback = knockback };
        context.GetTree().CurrentScene.AddChild(e);
        e.GlobalPosition = pos; // after AddChild (GrappleHook rule)
        e.Detonate();
        e.QueueFree();
    }

    private void Detonate()
    {
        float outerRadius = Radius * KnockbackBandMult;

        var query = new PhysicsShapeQueryParameters3D
        {
            Shape         = new SphereShape3D { Radius = outerRadius },
            Transform     = new Transform3D(Basis.Identity, GlobalPosition),
            CollisionMask = 2
        };

        foreach (var hit in GetWorld3D().DirectSpaceState.IntersectShape(query))
        {
            if (hit["collider"].AsGodotObject() is not Entity entity || entity is Player) continue;

            var   toEntity = entity.GetCenter() - GlobalPosition;
            float dist     = toEntity.Length();
            var   dir      = dist < 0.01f ? Vector3.Up : toEntity / dist;

            float falloff = Mathf.Max(1f - dist / outerRadius, MinFalloff);
            var   kb      = dir * (Knockback * falloff);

            entity.TakeDamage(dist <= Radius ? Damage : 0, kb);
        }
    }
}
