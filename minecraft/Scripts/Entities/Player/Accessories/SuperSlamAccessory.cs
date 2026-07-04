using Godot;

// Super Slam — jackhammer release always triggers an explosion at the impact point,
// even on entity-only hits where the base kit wouldn't otherwise touch terrain.
// (This was Explosive Bounce's effect before the design split — Explosive Bounce is
// now a fall-impact accessory instead.)
public class SuperSlamAccessory : Accessory
{
    public override string Name => "Super Slam";

    private const float ExplosionRadius   = 4f;
    private const float ExplosionStrength = 1f;

    public override void OnJackhammerImpact(Vector3I? blockPos, Vector3 impactPos)
    {
        if (blockPos != null) return; // base kit already exploded this block
        var bp = new Vector3I(Mathf.FloorToInt(impactPos.X), Mathf.FloorToInt(impactPos.Y), Mathf.FloorToInt(impactPos.Z));
        Player.Global.CubeManager.explode(bp, ExplosionRadius, ExplosionStrength);
    }
}
