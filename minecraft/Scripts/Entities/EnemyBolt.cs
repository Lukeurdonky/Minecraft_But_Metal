using Godot;

// Projectile fired by RangedEnemy. Orange emissive box, slight arc, damages player on contact.
public partial class EnemyBolt : Projectile
{
    public int Damage { get; set; } = 15;

    public override void ImHere()
    {
        base.ImHere();
        Gravity  = 4f;
        LifeTime = 4f;

        var mat = new StandardMaterial3D
        {
            ShadingMode      = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor      = new Color(1f, 0.5f, 0f),
            EmissionEnabled  = true,
            Emission         = new Color(1f, 0.3f, 0f),
            EmissionEnergyMultiplier = 2f,
        };
        var box = new BoxMesh { Size = new Vector3(0.25f, 0.25f, 0.25f) };
        box.SurfaceSetMaterial(0, mat);
        AddChild(new MeshInstance3D { Mesh = box });
    }

    public override void OnHitEntity(Entity entity)
    {
        if (entity is Player player)
        {
            var kb = Velocity.Normalized() * 5f;
            player.TakeDamage(Damage, kb);
        }
        QueueFree();
    }
}
