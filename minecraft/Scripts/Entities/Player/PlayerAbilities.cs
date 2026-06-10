using Godot;
using System.Collections.Generic;

// All player abilities consolidated here. Accessories hook into the public state
// properties rather than adding new input handling — check the relevant flags first.
public partial class Player : Entity
{
    // ── Jackhammer ───────────────────────────────────────────────────────────
    // Press attack1 to commit a charge — builds automatically to full.
    // Holding at full charge holds the pose; release fires.
    // Damage is determined by speed at the moment of fire (3 tiers).
    public bool  JackhammerCharging { get; private set; } = false;
    public float JackhammerCharge   { get; private set; } = 0f;
    public float JackhammerRadius   { get; private set; } = 3f;

    [Export] public float HitstopMed  { get; set; } = 0.25f;
    [Export] public float HitstopHard { get; set; } = 0.5f;

    private const float JackhammerMaxCharge  = .5f;
    private const float JackhammerImpulseWeak = 35f;
    private const float JackhammerImpulseMed  = 50f;
    private const float JackhammerImpulseHard = 70f;
    private const float JackhammerConeRange  = 6f;
    private const float JackhammerConeAngle  = 0.65f; // ~41° half-angle

    private float _pendingJackhammerImpulse = 0f;

    // Actions whose IsActionJustPressed are buffered during hitstop and consumed on the next frame.
    private readonly HashSet<string> _inputBuffer = new();
    private static readonly string[] _bufferableActions = { "attack2", "grapple_send", "dash", "jump" };

    private bool IsJustPressedOrBuffered(string action)
        => Input.IsActionJustPressed(action) || _inputBuffer.Remove(action);

    // Speed-based damage tiers (player speed sampled at fire time)
    private const float JackhammerMedThreshold  = 15f;
    private const float JackhammerFastThreshold = 30f;
    private const int   JackhammerDamageWeak    = 20;
    private const int   JackhammerDamageMed     = 50;
    private const int   JackhammerDamageHard    = 100;

    // ── Laser ────────────────────────────────────────────────────────────────
    // Press attack2 to fire a persistent 1s beam. 10s cooldown after use.
    public bool  LaserActive   { get; private set; } = false;
    public float LaserTimer    { get; private set; } = 0f;
    public float LaserCooldown { get; private set; } = 0f;

    private const float LaserDuration           = 1.5f;
    private const float LaserCooldownMax        = 7.0f;
    private const float LaserRange              = 100f;
    private const float LaserDamagePerSecond    = 200f;
    private const float LaserKnockbackPerSecond = 55f;
    private const float LaserTunnelRadius       = 4f;
    private const float LaserBeamRadius         = .35f;
    private const float LaserExplodeRate        = 0.05f; // seconds between explode calls (~20/s)

    private MeshInstance3D _laserBeam;
    private CapsuleShape3D _laserShape;
    private float          _laserExplodeCooldown = 0f;

    // LaserOutline animation state machine — driven by LaserOutlineMesh export
    // Extended = ready or firing (shapes out). Spinning = firing + rotating.
    // Retraction only happens during cooldown; extends back when full again.
    private enum LaserOutlineState { Extended, Spinning, FoldTriangle, FoldPoles, Retracted, UnfoldPoles, UnfoldTriangle }
    private LaserOutlineState _laserOutlineState = LaserOutlineState.Extended;
    private float _outlineTriangle        = 0f;    // blend shape index 0 — 0=extruded, 1=hidden
    private float _outlinePoles           = 0.65f; // blend shape index 1 — 0=fully extruded, 0.75=ready, 1=hidden
    private bool  _laserOutlineInitialized = false;
    private float _outlineYaw      = 0f; // accumulated Y degrees while spinning
    private const float OutlineFoldSpeed = 3.5f; // blend units per second
    private const float OutlineSpinSpeed     = 360f; // degrees per second while firing
    private const float OutlineIdleSpinSpeed =  25f; // degrees per second while charged/ready

    // ── Grapple ──────────────────────────────────────────────────────────────
    // Press grapple_send: instant raycast attach if something is in range.
    // Miss (nothing in range): physical hook fires out, snaps back on release.
    // While Attached: player accelerates toward anchor.
    // Release grapple_send while Attached: lunge at fixed speed toward anchor.
    // Press again while Sent/Attached: cancel.
    public enum GrappleState { Idle, Sent, Attached }
    public GrappleState CurrentGrappleState { get; private set; } = GrappleState.Idle;
    public Vector3      GrappleAnchor       { get; private set; } = Vector3.Zero;

    [Export] public PackedScene    GrappleHookScene { get; set; }
    [Export] public bool           CanGrappleLunge  { get; set; } = false;
    // Optional: assign a Node3D child of the Camera in character.tscn for exact arm-tip origin.
    // If left unassigned, the rope starts from a computed left-side camera offset.
    [Export] public Node3D         GrappleArmTip  { get; set; }
    [Export] public Node3D         LaserTip       { get; set; }
    [Export] public MeshInstance3D RightArmMesh   { get; set; }
    [Export] public MeshInstance3D LeftArmMesh    { get; set; }
    [Export] public MeshInstance3D LaserOutlineMesh { get; set; }

    private GrappleHook _activeHook;
    private Entity      _grappledEntity = null;
    public  Entity      GrappledEntity  => _grappledEntity;
    private Vector3     _reelVelocity   = Vector3.Zero;

    private const float GrappleSpeed          = 300f;
    private const float GrappleRange          = 220f;
    private const float GrapplePullAccel      = 72f;
    private const float GrappleMaxSpeed       = 50f;
    private const float GrappleLungeSpeed     = 50f;
    private const float GrappleDetachDist     = 1.5f;
    private const float LightEntityYBoost       = 8f;  // Y velocity added to player on light-entity attach
    private const float LightEntityReelSpeed    = 35f; // speed entity is pulled toward player
    private const float LightEntityReleaseBoost = 10f; // upward boost on release (zeroes Y first)
    private const float HeavyEntityReelSpeed    = 35f; // speed player is pulled toward heavy entity
    private const float HeavyEntityArrivalBoost = 8f;  // upward boost when player reaches heavy entity
    private const float GrappleCooldownMax       = 0.1f;

    private float _grappleCooldown     = 0f;
    private float _grappleJumpCooldown = 0f;

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
        {
            Velocity *= Mathf.Pow(SpeedPenaltyDecay, delta * 60f);
            Global.Instance.ShakeCamera(Mathf.Clamp(excessRatio * 0.45f, 0.1f, 0.5f), 0.08f);
        }
    }

    // ── Dash ─────────────────────────────────────────────────────────────────
    // Press dash: burst in current input direction, or camera forward if idle.
    public float DashCooldown { get; private set; } = 0f;

    private const float DashStrength    = 22f;
    private const float DashCooldownMax = 1.0f;

    // ── Speed threshold ───────────────────────────────────────────────────────
    // Above this speed: nearby blocks take damage and velocity is penalised.
    private const float SpeedDamageThreshold = 30f;
    private const float SpeedPenaltyDecay    = 0.8f;
    private const float SpeedBlockDamageRate = 60f;

    // Speed tier coyote — once speed drops below a threshold the tier stays
    // active for SpeedCoyoteDuration seconds so the player can still benefit.
    private const float SpeedCoyoteDuration = 0.5f;
    private float _hardCoyoteTimer = 0f;
    private float _medCoyoteTimer  = 0f;

    // 0=weak, 1=medium, 2=hard — updated every frame
    public int   RawSpeedTier      { get; private set; } = 0;
    public int   EffectiveSpeedTier { get; private set; } = 0;
    public float HardCoyoteTimer   => _hardCoyoteTimer;
    public float MedCoyoteTimer    => _medCoyoteTimer;

    // ─────────────────────────────────────────────────────────────────────────

    public void ProcessAbilities(float delta)
    {
        ProcessSpeedTier(delta);
        ProcessJackhammer(delta);
        ProcessLaser(delta);
        ProcessGrapple(delta);
        ProcessDash(delta);
        ProcessSpeedThreshold(delta);
    }

    private void ProcessSpeedTier(float delta)
    {
        float speed = Velocity.Length();

        RawSpeedTier = speed >= JackhammerFastThreshold ? 2
                     : speed >= JackhammerMedThreshold  ? 1
                     : 0;

        // Coyote only fires when descending: timer resets while above threshold,
        // counts down only after dropping below it.
        if (speed >= JackhammerFastThreshold)
            _hardCoyoteTimer = SpeedCoyoteDuration;
        else
            _hardCoyoteTimer = Mathf.Max(_hardCoyoteTimer - delta, 0f);

        if (speed >= JackhammerMedThreshold)
            _medCoyoteTimer = SpeedCoyoteDuration;
        else
            _medCoyoteTimer = Mathf.Max(_medCoyoteTimer - delta, 0f);

        EffectiveSpeedTier = _hardCoyoteTimer > 0f ? 2 : (_medCoyoteTimer > 0f ? 1 : 0);
    }

    // ── Jackhammer ───────────────────────────────────────────────────────────

    private bool _jackhammerHoldQueued = false;

    private void ProcessJackhammer(float delta)
    {
        if (!JackhammerCharging)
        {
            if (Input.IsActionJustPressed("attack1") || _jackhammerHoldQueued)
            {
                JackhammerCharging    = true;
                _jackhammerHoldQueued = false;
            }
            return;
        }

        JackhammerCharge = Mathf.Min(JackhammerCharge + delta, JackhammerMaxCharge);

        // Fire when fully charged and button is no longer held.
        // Still holding = hold the charge pose; fires the moment you release.
        if (JackhammerCharge >= JackhammerMaxCharge && !Input.IsActionPressed("attack1"))
        {
            FireJackhammer();
            JackhammerCharging = false;
            JackhammerCharge   = 0f;
        }
    }

    private void FireJackhammer()
    {
        if (Camera == null) return;

        bool         hitBlock = FindJackhammerBlock(out var blockPos);
        List<Entity> targets  = FindJackhammerEntities();
        if (!hitBlock && targets.Count == 0) return;

        float scaledImpulse = targets.Count > 0
            ? EffectiveSpeedTier switch { 2 => JackhammerImpulseHard, 1 => JackhammerImpulseMed, _ => JackhammerImpulseWeak }
            : JackhammerImpulseWeak;

        float hitstop = targets.Count > 0
            ? EffectiveSpeedTier switch { 2 => HitstopHard, 1 => HitstopMed, _ => 0f }
            : 0f;
        if (hitstop > 0f)
        {
            Global.Instance.TriggerHitstop(hitstop);
            _pendingJackhammerImpulse = scaledImpulse;
            Velocity = Vector3.Zero; // freeze; impulse fires opposite look dir when hitstop ends
        }
        else
        {
            Velocity = Camera.GlobalTransform.Basis.Z.Normalized() * scaledImpulse;
        }
        _airJumps = 1;

        // Use coyote-aware effective tier — ProcessSpeedTier already ran this frame.
        int damage = EffectiveSpeedTier switch
        {
            2 => JackhammerDamageHard,
            1 => JackhammerDamageMed,
            _ => JackhammerDamageWeak,
        };

        var lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();

        if (hitBlock)
            Global.CubeManager.explode(blockPos, JackhammerRadius, 1f);

        var knockback = -lookDir * scaledImpulse * 0.5f;
        foreach (var entity in targets)
            entity.TakeDamage(damage, knockback);

        if (_grappledEntity != null && targets.Contains(_grappledEntity))
            CancelGrapple();
    }

    private bool FindJackhammerBlock(out Vector3I hitBlock)
    {
        hitBlock = default;
        if (Camera == null) return false;

        var   lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();
        var   origin  = Camera.GlobalPosition;
        float closest = float.MaxValue;
        bool  found   = false;

        int r      = (int)Mathf.Ceil(JackhammerConeRange);
        var center = new Vector3I(Mathf.FloorToInt(origin.X), Mathf.FloorToInt(origin.Y), Mathf.FloorToInt(origin.Z));

        for (int x = -r; x <= r; x++)
        for (int y = -r; y <= r; y++)
        for (int z = -r; z <= r; z++)
        {
            var   bp          = center + new Vector3I(x, y, z);
            var   blockCenter = (Vector3)bp + Vector3.One * 0.5f;
            var   toBlock     = blockCenter - origin;
            float dist        = toBlock.Length();

            if (dist > JackhammerConeRange || dist < 0.01f) continue;
            if (lookDir.Dot(toBlock / dist) < JackhammerConeAngle) continue;
            if (Global.CubeManager.get_block(bp) == 0) continue;

            if (dist < closest) { closest = dist; hitBlock = bp; found = true; }
        }

        return found;
    }

    private List<Entity> FindJackhammerEntities()
    {
        var results = new List<Entity>();
        if (Camera == null) return results;

        var lookDir     = -Camera.GlobalTransform.Basis.Z.Normalized();
        var sphereCenter = GlobalPosition + lookDir * (JackhammerConeRange * 0.5f);
        float radius    = JackhammerConeRange * 0.6f;

        var query = new PhysicsShapeQueryParameters3D
        {
            Shape         = new SphereShape3D { Radius = radius },
            Transform     = new Transform3D(Basis.Identity, sphereCenter),
            CollisionMask = 2
        };

        foreach (var hit in GetWorld3D().DirectSpaceState.IntersectShape(query))
            if (hit["collider"].AsGodotObject() is Entity entity)
                results.Add(entity);

        return results;
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
                LaserActive              = false;
                LaserCooldown            = LaserCooldownMax;
                if (_laserBeam != null) _laserBeam.Visible = false;
            }
        }
        else if (IsJustPressedOrBuffered("attack2") && LaserCooldown <= 0f)
        {
            LaserActive = true;
            LaserTimer  = LaserDuration;
        }

        UpdateLaserOutline(delta);
    }

    private void TickLaser(float delta)
    {
        if (Camera == null) return;
        var origin  = Camera.GlobalPosition;
        var lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();

        // Block tunneling — ray march to first block, explode on cooldown
        Vector3     beamEnd    = origin + lookDir * LaserRange;
        float       beamLength = LaserRange;
        const float step       = 0.5f;
        _laserExplodeCooldown  = Mathf.Max(_laserExplodeCooldown - delta, 0f);
        for (float t = 1.0f; t <= LaserRange; t += step)
        {
            var p  = origin + lookDir * t;
            var bp = new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
            if (Global.CubeManager.get_block(bp) != 0)
            {
                beamEnd    = p;
                beamLength = t;
                if (_laserExplodeCooldown <= 0f)
                {
                    Global.CubeManager.explode(bp, LaserTunnelRadius, 1.0f);
                    _laserExplodeCooldown = LaserExplodeRate;
                }
                break;
            }
        }

        // Entity damage — capsule cast along beam (hits any entity intersecting the laser tube)
        _laserShape        ??= new CapsuleShape3D { Radius = LaserBeamRadius };
        _laserShape.Height   = beamLength;

        var right    = (Mathf.Abs(lookDir.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Forward).Cross(lookDir).Normalized();
        var fwd      = right.Cross(lookDir).Normalized();
        var capBasis = new Basis(right, lookDir, -fwd);

        var shapeQuery = new PhysicsShapeQueryParameters3D
        {
            Shape         = _laserShape,
            Transform     = new Transform3D(capBasis, origin + lookDir * (beamLength * 0.5f)),
            CollisionMask = 2,
        };
        foreach (var result in GetWorld3D().DirectSpaceState.IntersectShape(shapeQuery))
            if (result["collider"].AsGodotObject() is Entity ent)
                ent.TakeDamage((int)(LaserDamagePerSecond * delta));

        // Player knockback opposite to firing direction
        Velocity -= lookDir * LaserKnockbackPerSecond * delta;

        // VFX beam
        UpdateLaserBeam(beamEnd);
    }

    private void UpdateLaserBeam(Vector3 worldEnd)
    {
        var svCam = RightArmMesh?.GetViewport()?.GetCamera3D();
        if (svCam == null || Camera == null) return;

        if (_laserBeam == null)
        {
            var cyl          = new CylinderMesh();
            cyl.TopRadius    = LaserBeamRadius;
            cyl.BottomRadius = LaserBeamRadius;
            cyl.Height       = 1f;

            cyl.SurfaceSetMaterial(0, GD.Load<StandardMaterial3D>("res://Materials/LaserMaterial.tres"));

            _laserBeam            = new MeshInstance3D { Mesh = cyl };
            _laserBeam.Layers     = 32768;
            _laserBeam.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            RightArmMesh.GetViewport().AddChild(_laserBeam);
        }

        // Start: laser tip already in SubViewport space
        var tipPos = LaserTip != null
            ? LaserTip.GlobalPosition
            : svCam.GlobalPosition + svCam.GlobalTransform.Basis.X * 0.2f - svCam.GlobalTransform.Basis.Y * 0.2f;

        // End: convert world-space hit point into SubViewport space
        var toHit    = worldEnd - Camera.GlobalPosition;
        var dist     = toHit.Length();
        if (dist < 0.001f) { _laserBeam.Visible = false; return; }
        var localDir = Camera.GlobalTransform.Basis.Inverse() * (toHit / dist);
        var endSv    = svCam.GlobalPosition + localDir * dist;

        var diff   = endSv - tipPos;
        var length = diff.Length();
        if (length < 0.001f) { _laserBeam.Visible = false; return; }

        _laserBeam.Visible = true;
        var dir   = diff / length;
        var xAxis = (Mathf.Abs(dir.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Forward).Cross(dir).Normalized();
        var zAxis = xAxis.Cross(dir).Normalized();
        _laserBeam.GlobalTransform = new Transform3D(new Basis(xAxis, dir * length, zAxis), tipPos + diff * 0.5f);
    }

    private void UpdateLaserOutline(float delta)
    {
        var outline = LaserOutlineMesh;
        if (outline == null) return;

        if (!_laserOutlineInitialized)
        {
            outline.SetBlendShapeValue(0, 0f);    // triangle: fully extruded
            outline.SetBlendShapeValue(1, 0.65f); // poles: ready state
            _laserOutlineInitialized = true;
        }

        switch (_laserOutlineState)
        {
            case LaserOutlineState.Extended:
                // Triangle at 0 (fully extruded, index 0), poles at 0.75 (partial, index 1), slow idle spin
                _outlineTriangle = 0f;
                _outlinePoles    = 0.65f;
                outline.SetBlendShapeValue(0, 0f);    // triangle
                outline.SetBlendShapeValue(1, 0.65f); // poles
                _outlineYaw = (_outlineYaw + OutlineIdleSpinSpeed * delta) % 360f;
                outline.Rotation = new Vector3(outline.Rotation.X, Mathf.DegToRad(_outlineYaw), outline.Rotation.Z);
                if (LaserActive)
                {
                    _outlinePoles = 0f;
                    outline.SetBlendShapeValue(1, 0f); // poles fully extruded while firing
                    _laserOutlineState = LaserOutlineState.Spinning;
                }
                break;

            case LaserOutlineState.Spinning:
                // Both shapes fully extruded, rotate while laser fires
                _outlineYaw = (_outlineYaw + OutlineSpinSpeed * delta) % 360f;
                var spinRot = outline.Rotation;
                spinRot.Y   = Mathf.DegToRad(_outlineYaw);
                outline.Rotation = spinRot;
                if (!LaserActive)
                {
                    var r = outline.Rotation;
                    r.Y              = 0f;
                    outline.Rotation = r;
                    _outlineYaw        = 0f;
                    _laserOutlineState = LaserOutlineState.FoldPoles;
                }
                break;

            case LaserOutlineState.FoldPoles:
                _outlinePoles = Mathf.MoveToward(_outlinePoles, 1f, OutlineFoldSpeed * delta);
                outline.SetBlendShapeValue(1, _outlinePoles); // index 1 = poles
                if (_outlinePoles >= 1f)
                    _laserOutlineState = LaserOutlineState.FoldTriangle;
                break;

            case LaserOutlineState.FoldTriangle:
                _outlineTriangle = Mathf.MoveToward(_outlineTriangle, 1f, OutlineFoldSpeed * delta);
                outline.SetBlendShapeValue(0, _outlineTriangle); // index 0 = triangle
                if (_outlineTriangle >= 1f)
                    _laserOutlineState = LaserOutlineState.Retracted;
                break;

            case LaserOutlineState.Retracted:
                // Hold hidden for the full cooldown, then extend when ready
                if (LaserCooldown <= 0f)
                    _laserOutlineState = LaserOutlineState.UnfoldPoles;
                break;

            case LaserOutlineState.UnfoldPoles:
                _outlinePoles = Mathf.MoveToward(_outlinePoles, 0.65f, OutlineFoldSpeed * delta);
                outline.SetBlendShapeValue(1, _outlinePoles); // index 1 = poles
                if (_outlinePoles <= 0.65f)
                    _laserOutlineState = LaserOutlineState.UnfoldTriangle;
                break;

            case LaserOutlineState.UnfoldTriangle:
                _outlineTriangle = Mathf.MoveToward(_outlineTriangle, 0f, OutlineFoldSpeed * delta);
                outline.SetBlendShapeValue(0, _outlineTriangle); // index 0 = triangle
                if (_outlineTriangle <= 0f)
                    _laserOutlineState = LaserOutlineState.Extended;
                break;
        }
    }

    // ── Grapple ──────────────────────────────────────────────────────────────

    private void ProcessGrapple(float delta)
    {
        _grappleCooldown     = Mathf.Max(_grappleCooldown - delta, 0f);
        _grappleJumpCooldown = Mathf.Max(_grappleJumpCooldown - delta, 0f);
        if (CurrentGrappleState == GrappleState.Attached && _grappledEntity != null && _grappleJumpCooldown <= 0f)
            _airJumps = Mathf.Max(_airJumps, 1);

        switch (CurrentGrappleState)
        {
            case GrappleState.Idle:
                if (_grappleCooldown <= 0f && IsJustPressedOrBuffered("grapple_send"))
                    FireGrapple();
                break;

            case GrappleState.Sent:
                if (IsJustPressedOrBuffered("grapple_send"))
                {
                    CancelGrapple();
                    FireGrapple();
                }
                else if (Input.IsActionJustReleased("grapple_send"))
                {
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

                if (IsJustPressedOrBuffered("grapple_send"))
                {
                    CancelGrapple();
                }
                else if (Input.IsActionJustReleased("grapple_send"))
                {
                    if (_grappledEntity != null && _grappledEntity.heavy)
                    {
                        // Lunge player toward heavy entity on release
                        var toEntity = GrappleAnchor - GlobalPosition;
                        if (toEntity.LengthSquared() > 0.001f)
                            Velocity = toEntity.Normalized() * HeavyEntityReelSpeed;
                        ReleaseGrappledEntity();
                        _airJumps           = 1;
                        CurrentGrappleState = GrappleState.Idle;
                        _grappleCooldown    = GrappleCooldownMax;
                    }
                    else if (_grappledEntity != null && !_grappledEntity.heavy)
                    {
                        // Launch light entity toward player on release
                        var toPlayer = GlobalPosition - _grappledEntity.GetCenter();
                        if (toPlayer.LengthSquared() > 0.001f)
                            _grappledEntity.Velocity = toPlayer.Normalized() * LightEntityReelSpeed;
                        ReleaseGrappledEntity();
                        _airJumps           = 1;
                        CurrentGrappleState = GrappleState.Idle;
                        _grappleCooldown    = GrappleCooldownMax;
                    }
                    else
                    {
                        if (CanGrappleLunge)
                        {
                            // Lunge toward block — only if not already faster in that direction
                            var raw = GrappleAnchor - GlobalPosition;
                            if (raw.LengthSquared() > 0.001f)
                            {
                                var lungeDir = raw.Normalized();
                                if (Velocity.Dot(lungeDir) < GrappleLungeSpeed)
                                    Velocity = lungeDir * GrappleLungeSpeed;
                            }
                        }
                        ReleaseGrappledEntity();
                        _airJumps            = 1;
                        CurrentGrappleState  = GrappleState.Idle;
                        _grappleCooldown     = GrappleCooldownMax;
                    }
                }
                else
                {
                    // Jump escape — directional lunge if movement keys held, straight up if not.
                    // ApplyMovement already applied the Y jump this frame, so we only override XZ.
                    if (_grappledEntity == null && IsJustPressedOrBuffered("jump"))
                    {
                        CancelGrapple();
                        var lungeDir = Vector3.Zero;
                        if (Input.IsActionPressed("move_back"))  lungeDir -= forwardDirection;
                        if (Input.IsActionPressed("move_left"))  lungeDir -= rightDirection;
                        if (Input.IsActionPressed("move_right")) lungeDir += rightDirection;
                        if (lungeDir.LengthSquared() > 0.01f)
                        {
                            var v = Velocity;
                            v.X      = lungeDir.Normalized().X * DashStrength;
                            v.Z      = lungeDir.Normalized().Z * DashStrength;
                            Velocity = v;
                        }
                        break;
                    }

                    // Heavy entity: cancel if a block crosses the grapple line
                    if (_grappledEntity != null && _grappledEntity.heavy && IsGrappleLineBlocked())
                    {
                        CancelGrapple();
                        break;
                    }

                    var toAnchor = GrappleAnchor - GlobalPosition;

                    // Player arrived at heavy enemy — boost up and detach
                    if (_grappledEntity != null && _grappledEntity.heavy && toAnchor.Length() < GrappleDetachDist)
                    {
                        var v = Velocity;
                        v.X      = 0f;
                        v.Z      = 0f;
                        v.Y      = HeavyEntityArrivalBoost;
                        Velocity = v;
                        CancelGrapple();
                        break;
                    }

                    else
                    {
                        // Pull player toward block (Quake-style acceleration)
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
        if (Camera == null) return;

        // Auto-aim: if a selected enemy is in range, snap straight to it
        if (SelectedEnemy != null && GodotObject.IsInstanceValid(SelectedEnemy))
        {
            _grappledEntity     = SelectedEnemy;
            GrappleAnchor       = SelectedEnemy.GetCenter();
            CurrentGrappleState = GrappleState.Attached;
            _airJumps           = 1;
            if (!SelectedEnemy.heavy)
            {
                SelectedEnemy.Grappled = true;
                var v = Velocity;
                v.Y      = Mathf.Max(v.Y, 0f) + LightEntityYBoost;
                Velocity = v;
            }
            return;
        }

        var origin  = Camera.GlobalPosition;
        var lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();

        // Physics raycast for entities (layer 2)
        Entity hitEntity  = null;
        float  entityDist = float.MaxValue;
        var spaceState = GetWorld3D().DirectSpaceState;
        var query      = PhysicsRayQueryParameters3D.Create(origin, origin + lookDir * GrappleRange);
        query.CollisionMask = 2;
        var physHit = spaceState.IntersectRay(query);
        if (physHit.Count > 0 && physHit["collider"].AsGodotObject() is Entity ent)
        {
            hitEntity  = ent;
            entityDist = ((Vector3)physHit["position"] - origin).Length();
        }

        // Voxel block ray march
        Vector3 blockHitPos = Vector3.Zero;
        float   blockDist   = float.MaxValue;
        const float step    = 0.25f;
        for (float t = step; t <= GrappleRange; t += step)
        {
            var p  = origin + lookDir * t;
            var bp = new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
            if (Global.CubeManager.get_block(bp) != 0)
            {
                blockHitPos = p;
                blockDist   = t;
                break;
            }
        }

        if (hitEntity != null && entityDist <= blockDist)
        {
            // Instant attach — entity in range
            _grappledEntity     = hitEntity;
            GrappleAnchor       = hitEntity.GetCenter();
            CurrentGrappleState = GrappleState.Attached;
            _airJumps           = 1;

            if (!hitEntity.heavy)
            {
                hitEntity.Grappled = true;
                var v = Velocity;
                v.Y      = Mathf.Max(v.Y, 0f) + LightEntityYBoost;
                Velocity = v;
            }
        }
        else if (blockDist < float.MaxValue)
        {
            // Instant attach — block in range
            GrappleAnchor       = blockHitPos;
            CurrentGrappleState = GrappleState.Attached;
            _airJumps           = 1;
        }
        else if (GrappleHookScene != null)
        {
            // Nothing in range — fire physical hook (snaps back fast on release/max distance)
            var hook           = GrappleHookScene.Instantiate<GrappleHook>();
            hook.FireDirection = lookDir;
            hook.PlayerRef     = this;
            hook.Speed         = GrappleSpeed;
            hook.RetractSpeed  = 600f;
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
                if (!entity.heavy)
                {
                    entity.Grappled = true;
                    var v = Velocity;
                    v.Y      = Mathf.Max(v.Y, 0f) + LightEntityYBoost;
                    Velocity = v;
                }
                _activeHook = null;
            };
            hook.OnRetracted = () =>
            {
                CurrentGrappleState = GrappleState.Idle;
                _activeHook         = null;
            };

            GetTree().CurrentScene.AddChild(hook);
            hook.GlobalPosition = Camera.GlobalPosition;
            _activeHook         = hook;
            CurrentGrappleState = GrappleState.Sent;
        }
    }

    private void ReleaseGrappledEntity()
    {
        if (_grappledEntity != null)
            _grappledEntity.Grappled = false;
        _grappledEntity = null;
    }

    private void CancelGrapple()
    {
        _activeHook?.QueueFree();
        _activeHook          = null;
        ReleaseGrappledEntity();
        CurrentGrappleState  = GrappleState.Idle;
        _grappleCooldown     = GrappleCooldownMax;
    }

    private bool IsGrappleLineBlocked()
    {
        var   origin   = GlobalPosition;
        var   toTarget = GrappleAnchor - origin;
        float dist     = toTarget.Length();
        if (dist < 0.01f) return false;

        var         dir  = toTarget / dist;
        const float step = 0.5f;

        // Start past the player's own position, stop before the anchor (entity center)
        for (float t = 1.0f; t < dist - 0.5f; t += step)
        {
            var p  = origin + dir * t;
            var bp = new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
            if (Global.CubeManager.get_block(bp) != 0) return true;
        }
        return false;
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

            cylinder.SurfaceSetMaterial(0, GD.Load<StandardMaterial3D>("res://Materials/GrappleMaterial.tres"));

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

    private MeshInstance3D _jackhammerHardMesh;
    private float          _jackhammerHardBlend = 1f;

    public void UpdateArmBlendShapes(float delta)
    {
        // Right arm: jackhammer charge mapped directly to blend shape (0 = idle, 1 = full charge)
        if (RightArmMesh != null)
            RightArmMesh.SetBlendShapeValue(0, JackhammerCharge / JackhammerMaxCharge);

        // JackhammerHard sibling: blend shape 0 = visible (hard tier), 1 = hidden
        if (_jackhammerHardMesh == null && RightArmMesh != null)
            _jackhammerHardMesh = RightArmMesh.GetParent()?.GetNodeOrNull<MeshInstance3D>("JackhammerHard");
        if (_jackhammerHardMesh != null)
        {
            float target = EffectiveSpeedTier == 2 ? 0f : 1f;
            _jackhammerHardBlend = Mathf.MoveToward(_jackhammerHardBlend, target, 6f * delta);
            _jackhammerHardMesh.SetBlendShapeValue(0, _jackhammerHardBlend);
        }

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
        if (!IsJustPressedOrBuffered("dash") || DashCooldown > 0f) return;

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
