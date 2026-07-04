using Godot;

// Super Jump — press "super_jump" (C) to launch straight up, on a cooldown.
// Independent of the normal jump meter; usable in the air too.
public class SuperJumpAccessory : Accessory
{
    public override string Name => "Super Jump";

    private const float LaunchSpeed  = 34f;
    private const float CooldownMax  = 5f;

    private float _cooldown = 0f;

    public override void PhysicsProcess(float delta)
    {
        _cooldown = Mathf.Max(_cooldown - delta, 0f);

        if (_cooldown > 0f || !Input.IsActionJustPressed("super_jump")) return;

        var v = Player.Velocity;
        v.Y = LaunchSpeed;
        Player.Velocity = v;
        _cooldown = CooldownMax;

        Global.Instance?.ShakeCamera(0.3f, 0.15f);
    }
}
