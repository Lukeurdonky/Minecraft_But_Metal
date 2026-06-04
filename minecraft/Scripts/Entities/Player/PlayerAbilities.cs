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
    public float JackhammerRadius   { get; private set; } = 3f;

    private const float JackhammerMaxCharge = .5f;
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
    // Optional: assign a Node3D child of the Camera in character.tscn for exact arm-tip origin.
    // If left unassigned, the rope starts from a computed left-side camera offset.
    [Export] public Node3D        GrappleArmTip { get; set; }
    [Export] public MeshInstance3D RightArmMesh  { get; set; }
    [Export] public MeshInstance3D LeftArmMesh   { get; set; }


    private GrappleHook _activeHook;
    private Entity      _grappledEntity = null;
    private Vector3     _reelVelocity   = Vector3.Zero;

    private const float GrappleSpeed          = 250f;
    private const float GrappleRange          = 180f;
    private const float GrapplePullAccel      = 60f;
    private const float GrappleMaxSpeed       = 40f;
    private const float GrappleLungeSpeed     = 60f;
    private const float GrappleDetachDist     = 1.5f;
    private const float LightEntityYBoost    = 8f;  // Y velocity added to player on light-entity attach
    private const float LightEntityReelSpeed = 20f; // speed entity is pulled toward player

    private MeshInstance3D _ropeNode;

    private float _leftArmBlend = 1f;

    private bool  _leftArmBaseSet     = false;
    private Basis _leftArmBaseBasis;
    private Basis _leftArmCurrentBasis;

    private const float GrappleArmResistance = 1f; // 0 = no tracking, 1 = full
    private const float GrappleArmTrackSpeed = 8f;

    private void ProcessSpeedThreshold(float delta)
    {
        float speed = Velocity.Length();
        if (speed <= SpeedDamageThreshold) return;

        float excessRatio = (speed - SpeedDamageThreshold) / SpeedDamageThreshold;
        float damage      = excessRatio * SpeedBlockDamageRate * delta;
        var   cur         = new Vector3I(
            (int)Mathf.Floor(GlobalPosition.X),
            (int)Mathf.Floor(GlobalPosition.Y),
            (int)Mathf.Floor(GlobalPosition.Z));

        bool brokeBlock = false;
        for (int x = -2; x <= 2; x++)
        for (int y = -2; y <= 2; y++)
        for (int z = -2; z <= 2; z++)
        {
            if (x*x + y*y + z*z > 6) continue; // spherical ~radius 2.5
            var pos = cur + new Vector3I(x, y, z);
            if (Global.CubeManager.get_block(pos) != 0)
                if (Global.CubeManager.damage_check(pos, damage))
                    brokeBlock = true;
        }

        if (brokeBlock)
            Velocity *= Mathf.Pow(SpeedPenaltyDecay, delta * 60f);
    }

    // ── Dash ─────────────────────────────────────────────────────────────────
    // Press dash: burst in current input direction, or camera forward if idle.
    public float DashCooldown { get; private set; } = 0f;

    private const float DashStrength    = 22f;
    private const float DashCooldownMax = 1.0f;

    // ── Speed threshold ───────────────────────────────────────────────────────
    // Above this speed: nearby blocks take damage and velocity is penalised.
    private const float SpeedDamageThreshold = 30f;
    private const float SpeedPenaltyDecay    = 0.8f; // per-frame multiplier (dt-corrected)
    private const float SpeedBlockDamageRate = 60f;  // base damage/s scaled by excess ratio

    // ─────────────────────────────────────────────────────────────────────────

    public void ProcessAbilities(float delta)
    {
        ProcessJackhammer(delta);
        ProcessLaser(delta);
        ProcessGrapple(delta);
        ProcessDash(delta);
        ProcessSpeedThreshold(delta);
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
        float impulse = JackhammerImpulse * t;
        Velocity      = new Vector3(bounceDir.X * impulse, bounceDir.Y * impulse, bounceDir.Z * impulse);

        if (SelectedCube != 0)
            Global.CubeManager.explode(SelectedCubePosition, JackhammerRadius * t, t);
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
                // Guard: entity may have died
                if (_grappledEntity != null && !GodotObject.IsInstanceValid(_grappledEntity))
                {
                    CancelGrapple();
                    break;
                }

                // Keep anchor tracking entity so rope and arm follow it
                if (_grappledEntity != null)
                    GrappleAnchor = _grappledEntity.GetCenter();

                if (Input.IsActionJustPressed("grapple_send"))
                {
                    CancelGrapple();
                }
                else if (Input.IsActionJustReleased("grapple_send"))
                {
                    if (_grappledEntity != null && !_grappledEntity.heavy)
                    {
                        // Throw: hand the entity the velocity it was being reeled with
                        _grappledEntity.Velocity = _reelVelocity;
                    }
                    else
                    {
                        // Lunge toward block or heavy entity
                        var raw = GrappleAnchor - GlobalPosition;
                        if (raw.LengthSquared() > 0.001f)
                            Velocity = raw.Normalized() * GrappleLungeSpeed;
                    }
                    _grappledEntity     = null;
                    _airJumps           = 1;
                    CurrentGrappleState = GrappleState.Idle;
                }
                else
                {
                    var toAnchor = GrappleAnchor - GlobalPosition;
                    if (toAnchor.Length() < GrappleDetachDist)
                    {
                        _grappledEntity     = null;
                        CurrentGrappleState = GrappleState.Idle;
                    }
                    else if (_grappledEntity != null && !_grappledEntity.heavy)
                    {
                        // Pull light entity toward player
                        var toPlayer   = GlobalPosition - _grappledEntity.GetCenter();
                        _reelVelocity  = toPlayer.Normalized() * LightEntityReelSpeed;
                        _grappledEntity.Velocity = _reelVelocity;
                    }
                    else
                    {
                        // Pull player toward block or heavy entity
                        var pullDir    = toAnchor.Normalized();
                        float inDir    = Velocity.Dot(pullDir);
                        float addSpeed = Mathf.Clamp(GrappleMaxSpeed - inDir, 0f, GrapplePullAccel * delta);
                        Velocity      += pullDir * addSpeed;
                    }
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
            _airJumps           = 1;
            _activeHook         = null;
        };

        hook.OnAttachEntity = (entity) =>
        {
            _grappledEntity     = entity;
            GrappleAnchor       = entity.GetCenter();
            CurrentGrappleState = GrappleState.Attached;
            _airJumps           = 1;
            _activeHook         = null;

            if (!entity.heavy)
            {
                var v = Velocity;
                v.Y      = Mathf.Max(v.Y, 0f) + LightEntityYBoost;
                Velocity = v;
            }
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
        _grappledEntity     = null;
        CurrentGrappleState = GrappleState.Idle;
    }

    public void UpdateGrappleRope()
    {
        if (Camera == null) return;
        var svCam = LeftArmMesh?.GetViewport()?.GetCamera3D();
        if (svCam == null) return;

        if (_ropeNode == null)
        {
            var cylinder          = new CylinderMesh();
            cylinder.TopRadius    = 0.005f;
            cylinder.BottomRadius = 0.025f;
            cylinder.Height       = 1f;

            var mat = LeftArmMesh?.GetActiveMaterial(0);
            if (mat != null)
                cylinder.SurfaceSetMaterial(0, mat);

            _ropeNode            = new MeshInstance3D { Mesh = cylinder };
            _ropeNode.Layers     = 32768;
            _ropeNode.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            LeftArmMesh.GetViewport().AddChild(_ropeNode);
        }

        Vector3 hookPosWorld;
        switch (CurrentGrappleState)
        {
            case GrappleState.Sent when _activeHook != null:
                hookPosWorld = _activeHook.GlobalPosition;
                break;
            case GrappleState.Attached:
                hookPosWorld = GrappleAnchor;
                break;
            default:
                _ropeNode.Visible = false;
                return;
        }

        // Arm tip is already in SubViewport space
        var armTip = GrappleArmTip != null
            ? GrappleArmTip.GlobalPosition
            : svCam.GlobalPosition - svCam.GlobalTransform.Basis.X * 0.3f - svCam.GlobalTransform.Basis.Y * 0.2f;

        // Convert world-space hook direction + distance into SubViewport space
        var toGrapple  = hookPosWorld - Camera.GlobalPosition;
        var distance   = toGrapple.Length();
        var localDir   = Camera.GlobalTransform.Basis.Inverse() * toGrapple.Normalized();
        var hookPosSv  = svCam.GlobalPosition + localDir * distance;

        var diff   = hookPosSv - armTip;
        var length = diff.Length();

        if (length < 0.001f) { _ropeNode.Visible = false; return; }

        _ropeNode.Visible = true;

        var dir   = diff / length;
        var xAxis = (Mathf.Abs(dir.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Forward).Cross(dir).Normalized();
        var zAxis = xAxis.Cross(dir).Normalized();
        _ropeNode.GlobalTransform = new Transform3D(new Basis(xAxis, dir * length, zAxis), armTip + diff * 0.5f);
    }

    // ── Arm blend shapes ─────────────────────────────────────────────────────

    public void UpdateArmBlendShapes(float delta)
    {
        // Right arm: jackhammer charge mapped directly to blend shape (0 = idle, 1 = full charge)
        if (RightArmMesh != null)
            RightArmMesh.SetBlendShapeValue(0, JackhammerCharge / JackhammerMaxCharge);

        // Left arm: 1 = ready, 0 = thrown. Fast retract on throw, slower return when grapple lands back.
        if (LeftArmMesh != null)
        {
            float target = CurrentGrappleState == GrappleState.Idle ? 1f : 0f;
            float speed  = target == 0f ? 10f : 10f;
            _leftArmBlend = Mathf.MoveToward(_leftArmBlend, target, speed * delta);
            LeftArmMesh.SetBlendShapeValue(0, _leftArmBlend);
        }
    }

    public void UpdateLeftArmTracking(float delta)
    {
        if (LeftArmMesh == null || Camera == null) return;
        var leftArm = LeftArmMesh.GetParentOrNull<Node3D>();
        if (leftArm == null) return;

        if (!_leftArmBaseSet)
        {
            _leftArmBaseBasis    = leftArm.GlobalTransform.Basis;
            _leftArmCurrentBasis = _leftArmBaseBasis;
            _leftArmBaseSet      = true;
        }

        Basis targetBasis = _leftArmBaseBasis;

        if (CurrentGrappleState != GrappleState.Idle)
        {
            var grapplePos = CurrentGrappleState == GrappleState.Attached
                ? GrappleAnchor
                : (_activeHook?.GlobalPosition ?? Camera.GlobalPosition - Camera.GlobalTransform.Basis.Z * 5f);

            var rawDir = grapplePos - Camera.GlobalPosition;
            if (rawDir.LengthSquared() > 1e-6f)
            {
                var svTarget = leftArm.GlobalPosition + Camera.GlobalTransform.Basis.Inverse() * rawDir.Normalized() * 10f;
                targetBasis  = leftArm.GlobalTransform.LookingAt(svTarget, Vector3.Up).Basis;
            }
        }

        _leftArmCurrentBasis = _leftArmCurrentBasis.Slerp(targetBasis, Mathf.Min(GrappleArmTrackSpeed * delta, 1f));

        var t = leftArm.GlobalTransform;
        t.Basis = _leftArmCurrentBasis;
        leftArm.GlobalTransform = t;
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
        var v = Velocity;
        v.X      = dashDir.X * DashStrength;
        v.Z      = dashDir.Z * DashStrength;
        Velocity = v;

        DashCooldown = DashCooldownMax;
    }
}
