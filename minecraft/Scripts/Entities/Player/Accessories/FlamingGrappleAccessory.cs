using Godot;

// Flaming Grapple — hitting an enemy with the grapple sets it on fire for a few seconds
// (damage over time + a chance to spread to nearby enemies — see Enemy.SetOnFire).
public class FlamingGrappleAccessory : Accessory
{
    public override string Name => "Flaming Grapple";

    private const float BurnDuration = 3f;

    public override void OnGrappleAttach(Entity entity, Vector3 anchor)
    {
        if (entity is Enemy enemy) enemy.SetOnFire(BurnDuration);
    }
}
