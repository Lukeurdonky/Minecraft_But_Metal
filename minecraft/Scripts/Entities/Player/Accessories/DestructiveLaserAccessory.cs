// Destructive Laser — tunnels a much wider hole through blocks, with a thicker
// beam to visually match the bigger tunnel.
public class DestructiveLaserAccessory : Accessory
{
    public override string Name => "Destructive Laser";

    private const float TunnelRadiusMultiplier = 2.5f;
    private const float BeamRadiusMultiplier   = 1.6f;

    public override float ModifyLaserTunnelRadius(float radius) => radius * TunnelRadiusMultiplier;
    public override float ModifyLaserBeamRadius(float radius) => radius * BeamRadiusMultiplier;
}
