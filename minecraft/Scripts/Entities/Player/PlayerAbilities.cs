using Godot;

// All player abilities consolidated here. Accessories hook into the public state
// properties rather than adding new input handling — check the relevant flags first.
public partial class Player : Entity
{
    // ── Jackhammer ───────────────────────────────────────────────────────────
    // Hold attack1 to charge. Release to bounce: velocity fires in the opposite
    // direction of the camera's look vector, scaled by charge.
    public bool  JackhammerCharging { get; private set; } = false;
    public float JackhammerCharge   { get; private set; } = 0f;

    private const float JackhammerMaxCharge = 1.5f;
    private const float JackhammerImpulse   = 35f;

    // ── Laser ────────────────────────────────────────────────────────────────
    // Press attack2 to fire a persistent 1s beam. 10s cooldown after use.
    public bool  LaserActive   { get; private set; } = false;
    public float LaserTimer    { get; private set; } = 0f;
    public float LaserCooldown { get; private set; } = 0f;

    private const float LaserDuration        = 1.0f;
    private const float LaserCooldownMax     = 10.0f;
    private const float LaserRange           = 60f;
    private const float LaserDamagePerSecond = 20f;

    // ── Grapple ──────────────────────────────────────────────────────────────
    // Press grapple_send: hook travels out, attaches to block or entity.
    // While Attached: player accelerates toward anchor.
    // Release grapple_send while Attached: lunge at fixed speed toward anchor.
    // Press again while Sent/Attached: cancel.
    public enum GrappleState { Idle, Sent, Attached }
    public GrappleState CurrentGrappleState { get; private set; } = GrappleState.Idle;
    public Vector3      GrappleAnchor       { get; private set; } = Vector3.Zero;

    [Export] public PackedScene GrappleHookScene { get; set; }

    // Ability momentum channel — declared here, shared with Player.cs via partial class.
    // ApplyMovement() decays this each tick and adds it to _inputVel for the final Velocity.
    private Vector3 _abilityVel = Vector3.Zero;

    private GrappleHook _activeHook;
    private const float GrappleSpeed       = 150f;
    private const float GrappleRange       = 120f;
    private const float GrapplePullAccel   = 90f;  // acceleration added per second toward anchor
    private const float GrappleLungeSpeed  = 60f;  // set velocity on release
    private const float GrappleDetachDist  = 1.5f;

    // ── Dash ─────────────────────────────────────────────────────────────────
    // Press dash: burst in current input direction, or camera forward if idle.
    public float DashCooldown { get; private set; } = 0f;

    private const float DashStrength    = 22f;
    private const float DashCooldownMax = 1.0f;

    // ─────────────────────────────────────────────────────────────────────────

    public void ProcessAbilities(float delta)
    {
        ProcessJackhammer(delta);
        ProcessLaser(delta);
        ProcessGrapple(delta);
        ProcessDash(delta);
    }

    // ── Jackhammer ───────────────────────────────────────────────────────────

    private void ProcessJackhammer(float delta)
    {
        if (Input.IsActionPressed("attack1"))
        {
            JackhammerCharging = true;
            JackhammerCharge   = Mathf.Min(JackhammerCharge + delta, JackhammerMaxCharge);
        }
        else if (JackhammerCharging)
        {
            FireJackhammer();
            JackhammerCharging = false;
            JackhammerCharge   = 0f;
        }
    }

    private void FireJackhammer()
    {
        if (Camera == null) return;
        float t = JackhammerCharge / JackhammerMaxCharge;

        // Bounce opposite to where the camera is pointing — full charge = full impulse
        var lookDir   = -Camera.GlobalTransform.Basis.Z.Normalized();
        var bounceDir = -lookDir;
        _abilityVel = bounceDir * (JackhammerImpulse * t);

        if (SelectedCube != 0)
            Global.CubeManager.damage_block(SelectedCubePosition, t);
    }

    // ── Laser ────────────────────────────────────────────────────────────────

    private void ProcessLaser(float delta)
    {
        LaserCooldown = Mathf.Max(LaserCooldown - delta, 0f);

        if (LaserActive)
        {
            LaserTimer -= delta;
            TickLaser(delta);
            if (LaserTimer <= 0f)
            {
                LaserActive   = false;
                LaserCooldown = LaserCooldownMax;
            }
        }
        else if (Input.IsActionJustPressed("attack2") && LaserCooldown <= 0f)
        {
            LaserActive = true;
            LaserTimer  = LaserDuration;
        }
    }

    private void TickLaser(float delta)
    {
        if (Camera == null) return;
        var origin  = Camera.GlobalPosition;
        var lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();

        var spaceState = GetWorld3D().DirectSpaceState;
        var query      = PhysicsRayQueryParameters3D.Create(origin, origin + lookDir * LaserRange);
        query.CollisionMask = 2;

        var hit = spaceState.IntersectRay(query);
        if (hit.Count > 0 && hit["collider"].AsGodotObject() is Entity entity)
            entity.TakeDamage((int)(LaserDamagePerSecond * delta));
    }

    // ── Grapple ──────────────────────────────────────────────────────────────

    private void ProcessGrapple(float delta)
    {
        switch (CurrentGrappleState)
        {
            case GrappleState.Idle:
                if (Input.IsActionJustPressed("grapple_send"))
                    FireGrapple();
                break;

            case GrappleState.Sent:
                if (Input.IsActionJustPressed("grapple_send"))
                {
                    // Re-press while hook is out (flying or retracting) — despawn and fire a new one
                    CancelGrapple();
                    FireGrapple();
                }
                else if (Input.IsActionJustReleased("grapple_send"))
                {
                    // Released before attaching — reel back immediately
                    _activeHook?.StartRetract();
                }
                break;

            case GrappleState.Attached:
                if (Input.IsActionJustPressed("grapple_send"))
                {
                    CancelGrapple();
                }
                else if (Input.IsActionJustReleased("grapple_send"))
                {
                    // Lunge: blast ability channel toward anchor, input channel cleared so gravity doesn't immediately fight it
                    var lungeDir = (GrappleAnchor - GlobalPosition).Normalized();
                    _abilityVel = lungeDir * GrappleLungeSpeed;
                    _inputVel.Y = 0f;
                    _airJumps  += 1; // releasing the grapple lunge grants one more air jump
                    CurrentGrappleState = GrappleState.Idle;
                }
                else
                {
                    // Accelerate toward anchor each tick — friction and gravity act naturally
                    var toAnchor = GrappleAnchor - GlobalPosition;
                    if (toAnchor.Length() < GrappleDetachDist)
                        CurrentGrappleState = GrappleState.Idle;
                    else
                        _abilityVel += toAnchor.Normalized() * GrapplePullAccel * delta;
                }
                break;
        }
    }

    private void FireGrapple()
    {
        if (GrappleHookScene == null || Camera == null) return;

        var lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();
        var hook    = GrappleHookScene.Instantiate<GrappleHook>();

        hook.FireDirection = lookDir;
        hook.PlayerRef     = this;
        hook.Speed         = GrappleSpeed;
        hook.MaxDistance   = GrappleRange;

        hook.OnAttach = (worldPos) =>
        {
            GrappleAnchor       = worldPos;
            CurrentGrappleState = GrappleState.Attached;
            _airJumps          += 1; // successful grapple grants one additional air jump
            _activeHook         = null;
        };

        hook.OnRetracted = () =>
        {
            CurrentGrappleState = GrappleState.Idle;
            _activeHook         = null;
        };

        GetTree().CurrentScene.AddChild(hook);
        hook.GlobalPosition = Camera.GlobalPosition; // must be set after AddChild
        _activeHook         = hook;
        CurrentGrappleState = GrappleState.Sent;
    }

    private void CancelGrapple()
    {
        _activeHook?.QueueFree();
        _activeHook         = null;
        CurrentGrappleState = GrappleState.Idle;
    }

    // ── Dash ─────────────────────────────────────────────────────────────────

    private void ProcessDash(float delta)
    {
        DashCooldown = Mathf.Max(DashCooldown - delta, 0f);
        if (!Input.IsActionJustPressed("dash") || DashCooldown > 0f) return;

        // Direction built only from keys held at the moment of the dash press
        var dashDir = Vector3.Zero;
        if (Input.IsActionPressed("move_forward")) dashDir += forwardDirection;
        if (Input.IsActionPressed("move_back"))    dashDir -= forwardDirection;
        if (Input.IsActionPressed("move_left"))    dashDir -= rightDirection;
        if (Input.IsActionPressed("move_right"))   dashDir += rightDirection;

        if (dashDir.LengthSquared() < 0.01f)
        {
            // No key held — use camera look direction flattened to horizontal plane
            var look = -Camera.GlobalTransform.Basis.Z;
            dashDir = new Vector3(look.X, 0, look.Z).Normalized();
        }

        dashDir = dashDir.Normalized();
        _abilityVel.X = dashDir.X * DashStrength;
        _abilityVel.Z = dashDir.Z * DashStrength;

        DashCooldown = DashCooldownMax;
    }
}
