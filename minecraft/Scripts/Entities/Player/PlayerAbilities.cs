using Godot;
using System.Collections.Generic;

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

    private const float JackhammerMaxCharge  = .5f;
    private const float JackhammerImpulse   = 35f;
    private const float JackhammerBaseDamage = 30f;
    private const float JackhammerConeRange = 6f;
    private const float JackhammerConeAngle = 0.75f; // ~41° half-angle

    // ── Laser ────────────────────────────────────────────────────────────────
    // Press attack2 to fire a persistent 1s beam. 10s cooldown after use.
    public bool  LaserActive   { get; private set; } = false;
    public float LaserTimer    { get; private set; } = 0f;
    public float LaserCooldown { get; private set; } = 0f;

    private const float LaserDuration           = 1.5f;
    private const float LaserCooldownMax        = 7.0f;
    private const float LaserRange              = 100f;
    private const float LaserDamagePerSecond    = 20f;
    private const float LaserKnockbackPerSecond = 45f;
    private const float LaserTunnelRadius       = 3f;
    private const float LaserBeamRadius         = .25f;
    private const float LaserExplodeRate        = 0.05f; // seconds between explode calls (~20/s)

    private MeshInstance3D _laserBeam;
    private CapsuleShape3D _laserShape;
    private float          _laserExplodeCooldown = 0f;

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
    // Optional: assign a Node3D child of the Camera in character.tscn for exact arm-tip origin.
    // If left unassigned, the rope starts from a computed left-side camera offset.
    [Export] public Node3D         GrappleArmTip { get; set; }
    [Export] public Node3D         LaserTip      { get; set; }
    [Export] public MeshInstance3D RightArmMesh  { get; set; }
    [Export] public MeshInstance3D LeftArmMesh   { get; set; }

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

    private float _grappleCooldown = 0f;

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

        bool         hitBlock = FindJackhammerBlock(out var blockPos);
        List<Entity> targets  = FindJackhammerEntities();
        if (!hitBlock && targets.Count == 0) return;

        float t     = JackhammerCharge / JackhammerMaxCharge;
        var lookDir = -Camera.GlobalTransform.Basis.Z.Normalized();
        Velocity    = -lookDir * JackhammerImpulse * t;
        _airJumps   = 1;

        if (hitBlock)
            Global.CubeManager.explode(blockPos, JackhammerRadius * t, t);

        var knockback = -lookDir * JackhammerImpulse * t * 0.5f;
        foreach (var entity in targets)
            entity.TakeDamage((int)(JackhammerBaseDamage * t), knockback);

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

            var mat = RightArmMesh?.GetActiveMaterial(0);
            if (mat != null)
                cyl.SurfaceSetMaterial(0, mat);

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

    // ── Grapple ──────────────────────────────────────────────────────────────

    private void ProcessGrapple(float delta)
    {
        _grappleCooldown = Mathf.Max(_grappleCooldown - delta, 0f);

        switch (CurrentGrappleState)
        {
            case GrappleState.Idle:
                if (Input.IsActionJustPressed("grapple_send") && _grappleCooldown <= 0f)
                    FireGrapple();
                break;

            case GrappleState.Sent:
                if (Input.IsActionJustPressed("grapple_send"))
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

                if (Input.IsActionJustPressed("grapple_send"))
                {
                    CancelGrapple();
                }
                else if (Input.IsActionJustReleased("grapple_send"))
                {
                    if (_grappledEntity != null && _grappledEntity.heavy)
                    {
                        // Toggle mode: release does nothing — stays latched until re-press or line blocked
                    }
                    else if (_grappledEntity != null && !_grappledEntity.heavy)
                    {
                        // Throw light entity + upward boost
                        _grappledEntity.Velocity = _reelVelocity;
                        var v = Velocity;
                        v.Y      = LightEntityReleaseBoost;
                        Velocity = v;
                        ReleaseGrappledEntity();
                        _airJumps            = 1;
                        CurrentGrappleState  = GrappleState.Idle;
                        _grappleCooldown     = GrappleCooldownMax;
                    }
                    else
                    {
                        // Lunge toward block — only if not already faster in that direction
                        var raw = GrappleAnchor - GlobalPosition;
                        if (raw.LengthSquared() > 0.001f)
                        {
                            var lungeDir = raw.Normalized();
                            if (Velocity.Dot(lungeDir) < GrappleLungeSpeed)
                                Velocity = lungeDir * GrappleLungeSpeed;
                        }
                        ReleaseGrappledEntity();
                        _airJumps            = 1;
                        CurrentGrappleState  = GrappleState.Idle;
                        _grappleCooldown     = GrappleCooldownMax;
                    }
                }
                else
                {
                    // Jump breaks out of any entity grapple
                    if (_grappledEntity != null && Input.IsActionJustPressed("jump"))
                    {
                        CancelGrapple();
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

                    // Light enemy arrived at player — zero their horizontal velocity and detach
                    if (toAnchor.Length() < GrappleDetachDist && (_grappledEntity == null || !_grappledEntity.heavy))
                    {
                        if (_grappledEntity != null)
                        {
                            var ev = _grappledEntity.Velocity;
                            ev.X = 0f;
                            ev.Z = 0f;
                            _grappledEntity.Velocity = ev;
                        }
                        ReleaseGrappledEntity();
                        CurrentGrappleState = GrappleState.Idle;
                        _grappleCooldown    = GrappleCooldownMax;
                    }
                    else if (_grappledEntity != null && !_grappledEntity.heavy)
                    {
                        // Pull light entity toward player
                        var toPlayer  = GlobalPosition - _grappledEntity.GetCenter();
                        _reelVelocity = toPlayer.Normalized() * LightEntityReelSpeed;
                        _grappledEntity.Velocity = _reelVelocity;
                    }
                    else if (_grappledEntity != null && _grappledEntity.heavy)
                    {
                        // Pull player toward heavy entity — direct velocity, mirrors light entity reel
                        Velocity = toAnchor.Normalized() * HeavyEntityReelSpeed;
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
