using Godot;

// Glide — hold jump while airborne to cap your fall speed. Vertical only —
// no horizontal push (The Messenger's cape glide, not Minecraft's elytra).
public class GlideAccessory : Accessory
{
    public override string Name => "Glide";

    private const float GlideFallSpeed = -4f;

    public override void PhysicsProcess(float delta)
    {
        if (Player.SpectatorMode || Player.PhysicallyOnFloor()) return;
        if (!Input.IsActionPressed("jump")) return;
        if (Player.Velocity.Y >= GlideFallSpeed) return; // not falling faster than the glide cap

        var v = Player.Velocity;
        v.Y = GlideFallSpeed;
        Player.Velocity = v;
    }
}
