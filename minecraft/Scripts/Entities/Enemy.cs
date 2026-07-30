using Godot;
using System.Collections.Generic;

// Base class for all enemies. Inherits Entity, adds combat stats, detection range,
// and a procedurally built world-space health bar.
// Creature (and future enemy types) extend this, not Entity directly.
public partial class Enemy : Entity
{
    // Cheap registry for the fire-spread scan (Enemy.All) — avoids a scene-tree search.
    private static readonly List<Enemy> _all = new();
    public static IReadOnlyList<Enemy> All => _all;
    [Export] public int   AttackDamage   { get; set; } = 10;
    [Export] public float DetectionRange { get; set; } = 15f;
    [Export] public bool  Flying                       = false;

    // LOD tier by distance to player. Subclasses must read DistSqToPlayer/Lod instead of
    // computing their own player-distance — see documents/performance/ENEMY_PERFORMANCE.md.
    public enum LodTier { Near, Mid, Far }

    public float   DistSqToPlayer { get; private set; } = 0f;
    public LodTier Lod            { get; private set; } = LodTier.Near;

    private const float MidLodDistance = 40f;
    private const float FarLodDistance = 80f;

    private Node3D             _healthBarRoot;
    private MeshInstance3D     _healthBarFg;
    private StandardMaterial3D _healthBarFgMat;
    private float              _flashTimer = 0f;
    private const float        FlashDuration = 0.12f;
    private const float        DespawnRadius = 160f;

    private const float BarWidth  = 1.2f;
    private const float BarHeight = 0.1f;

    private Godot.Collections.Array<Node> _particles;
    private Godot.Collections.Array<Node> _animPlayers;
    private bool _counted = false;
    private bool _lastAnimateState = true;
    private bool _lastEmitState    = true;

    // ── Burning (Flaming Grapple accessory) ──────────────────────────────────
    // Kept separate from the shared _particles LOD list above — that list force-emits
    // on every Near-tier enemy, which would show fire on ones that aren't burning.
    public bool IsBurning { get; private set; } = false;

    private GpuParticles3D _fireParticles;
    private float _burnTimer            = 0f;
    private float _burnDamageTickTimer  = 0f;
    private float _burnSpreadTimer      = 0f;

    private const float BurnDamagePerTick   = 5f;
    private const float BurnTickInterval    = 0.5f;
    private const float BurnSpreadRadius    = 4f;
    private const float BurnSpreadInterval  = 1f;
    private const float SpreadIgniteDuration = 3f;

    public override void ImHere()
    {
        base.ImHere();
        BuildHealthBar();
        // GpuParticles3D is the standard going forward (native, GPU-driven — see
        // documents/performance/ENEMY_PERFORMANCE.md). UniParticles3D (GDScript, CPU-bound
        // per-particle simulation) is kept here only in case an older enemy scene still uses it.
        _particles = FindChildren("*", "GPUParticles3D", true, false);
        foreach (var legacy in FindChildren("*", "UniParticles3D", true, false))
            _particles.Add(legacy);
        _animPlayers = FindChildren("*", "AnimationPlayer", true, false);
        if (Global.Instance != null) { Global.Instance.EnemyCount++; _counted = true; }
        _all.Add(this);
    }

    public override void Die()
    {
        Global.Instance?.IncrementKills();
        DecrementCount();
        base.Die();
    }

    public override void _ExitTree()
    {
        DecrementCount();
        base._ExitTree();
    }

    private void DecrementCount()
    {
        if (_counted && Global.Instance != null)
        {
            Global.Instance.EnemyCount--;
            _counted = false;
        }
        _all.Remove(this);
    }

    // Computed once per physics tick, before any subclass decision logic runs. Subclasses
    // must read DistSqToPlayer/Lod instead of computing their own player distance.
    public override void ApplyMovementFromInput(double delta)
    {
        UpdatePlayerDistance();
        UpdateBurning((float)delta);
        base.ApplyMovementFromInput(delta);
    }

    private void UpdatePlayerDistance()
    {
        var player = Global.Instance?.Player;
        DistSqToPlayer = player != null
            ? GlobalPosition.DistanceSquaredTo(player.GlobalPosition)
            : float.MaxValue;
        Lod = DistSqToPlayer > FarLodDistance * FarLodDistance ? LodTier.Far
            : DistSqToPlayer > MidLodDistance * MidLodDistance ? LodTier.Mid
            : LodTier.Near;
    }

    public override void _Process(double delta)
    {
        if (DistSqToPlayer > DespawnRadius * DespawnRadius)
        {
            QueueFree();
            return;
        }

        bool hitstop = Global?.HitstopActive == true;

        // Animation + particle LOD — the dominant per-enemy cost (skeleton eval + skinning).
        // Gated on Lod, and only written when the value actually changes (Godot property
        // writes aren't free at 50 entities). See documents/performance/ENEMY_PERFORMANCE.md.
        bool shouldAnimate = Lod != LodTier.Far && !hitstop;
        if (shouldAnimate != _lastAnimateState)
        {
            foreach (var node in _animPlayers)
                if (node is AnimationPlayer ap) ap.SpeedScale = shouldAnimate ? 1f : 0f;
            _lastAnimateState = shouldAnimate;
        }

        // Pure LOD gating — hitstop is NOT handled here. Freezing particles during hitstop is
        // global (Global's hitstop_particles group sweep, SpeedScale=0). Emitting=false only
        // gates new spawns and would leave in-flight particles moving through the freeze.
        bool shouldEmit = Lod == LodTier.Near;
        if (shouldEmit != _lastEmitState)
        {
            foreach (var node in _particles)
                if (node is GpuParticles3D p) p.Emitting = shouldEmit;
                else node.Set("paused", !shouldEmit);
            _lastEmitState = shouldEmit;
        }

        // Health bar billboard tracking is unreadable at Far tier — skip it entirely.
        if (_healthBarRoot == null || Lod == LodTier.Far) return;
        var cam = Global.Instance?.Player?.Camera;
        if (cam == null) return;
        var toCamera = cam.GlobalPosition - _healthBarRoot.GlobalPosition;
        if (toCamera.LengthSquared() > 0.001f)
            _healthBarRoot.LookAt(_healthBarRoot.GlobalPosition - toCamera, Vector3.Up);

        if (_flashTimer > 0f)
        {
            _flashTimer -= (float)delta;
            float t = Mathf.Clamp(_flashTimer / FlashDuration, 0f, 1f);
            float ratio = MaxHealth > 0 ? Mathf.Clamp((float)CurrentHealth / MaxHealth, 0f, 1f) : 0f;
            var baseColor = new Color(1f - ratio, 0.8f * ratio, 0.1f);
            _healthBarFgMat.AlbedoColor = baseColor.Lerp(Colors.White, t);
        }
    }

    private void BuildHealthBar()
    {
        _healthBarRoot = new Node3D { Position = new Vector3(0f, height + 0.5f, 0f) };
        AddChild(_healthBarRoot);

        var bgMesh = new QuadMesh { Size = new Vector2(BarWidth, BarHeight) };
        var bgMat  = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.1f, 0.1f, 0.1f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        bgMesh.SurfaceSetMaterial(0, bgMat);
        _healthBarRoot.AddChild(new MeshInstance3D { Mesh = bgMesh });

        var fgMesh = new QuadMesh { Size = new Vector2(BarWidth, BarHeight * 0.7f) };
        _healthBarFgMat = new StandardMaterial3D
        {
            AlbedoColor    = new Color(0.2f, 0.9f, 0.2f),
            ShadingMode    = BaseMaterial3D.ShadingModeEnum.Unshaded,
            RenderPriority = 1,
        };
        fgMesh.SurfaceSetMaterial(0, _healthBarFgMat);
        _healthBarFg = new MeshInstance3D { Mesh = fgMesh };
        _healthBarRoot.AddChild(_healthBarFg);

        _healthBarRoot.Visible = false;
    }

    private void RefreshHealthBar()
    {
        if (_healthBarFg == null) return;
        float ratio = MaxHealth > 0 ? Mathf.Clamp((float)CurrentHealth / MaxHealth, 0f, 1f) : 0f;

        _healthBarRoot.Visible = ratio < 1f;
        ((QuadMesh)_healthBarFg.Mesh).Size = new Vector2(BarWidth * ratio, BarHeight * 0.7f);
        _healthBarFg.Position = new Vector3((ratio - 1f) * BarWidth * 0.5f, 0f, 0.001f);
        _healthBarFgMat.AlbedoColor = new Color(1f - ratio, 0.8f * ratio, 0.1f);
    }

    // ── Burning ───────────────────────────────────────────────────────────────

    public void SetOnFire(float duration)
    {
        _burnTimer = Mathf.Max(_burnTimer, duration);
        if (IsBurning) return;

        IsBurning = true;
        if (_fireParticles == null)
        {
            _fireParticles = BuildFireParticles();
            AddChild(_fireParticles);
        }
        _fireParticles.Emitting = Lod != LodTier.Far;
    }

    private void UpdateBurning(float delta)
    {
        if (!IsBurning) return;

        _burnTimer -= delta;
        if (_burnTimer <= 0f)
        {
            IsBurning = false;
            if (_fireParticles != null) _fireParticles.Emitting = false;
            return;
        }

        _burnDamageTickTimer -= delta;
        if (_burnDamageTickTimer <= 0f)
        {
            _burnDamageTickTimer = BurnTickInterval;
            TakeDamage((int)BurnDamagePerTick);
        }

        if (_fireParticles != null)
            _fireParticles.Emitting = Lod != LodTier.Far;

        if (Lod == LodTier.Far) return; // spread scan is O(n) over Enemy.All — skip far from the player

        _burnSpreadTimer -= delta;
        if (_burnSpreadTimer <= 0f)
        {
            _burnSpreadTimer = BurnSpreadInterval;
            SpreadFireToNearby();
        }
    }

    private void SpreadFireToNearby()
    {
        float radiusSq = BurnSpreadRadius * BurnSpreadRadius;
        foreach (var other in _all)
        {
            if (other == this || !GodotObject.IsInstanceValid(other) || other.IsBurning) continue;
            if (GlobalPosition.DistanceSquaredTo(other.GlobalPosition) <= radiusSq)
                other.SetOnFire(SpreadIgniteDuration);
        }
    }

    private GpuParticles3D BuildFireParticles()
    {
        var processMat = new ParticleProcessMaterial
        {
            Direction             = new Vector3(0, 1, 0),
            Spread                = 20f,
            InitialVelocityMin    = 1.0f,
            InitialVelocityMax    = 2.0f,
            Gravity               = new Vector3(0, 1.5f, 0), // embers drift upward
            ScaleMin              = 0.3f,
            ScaleMax              = 0.6f,
            EmissionShape         = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius  = Mathf.Max(width * 0.4f, 0.2f),
        };

        var quad = new QuadMesh { Size = new Vector2(0.6f, 0.6f) };
        quad.SurfaceSetMaterial(0, new ShaderMaterial { Shader = GD.Load<Shader>("res://Materials/Fire.gdshader") });

        return new GpuParticles3D
        {
            Amount          = 12,
            Lifetime        = 0.6,
            ProcessMaterial = processMat,
            DrawPass1       = quad,
            Position        = new Vector3(0, height * 0.5f, 0),
            Emitting        = false,
        };
    }

    public override void TakeDamage(int amount)
    {
        base.TakeDamage(amount);
        RefreshHealthBar();
        _flashTimer = FlashDuration;
    }

    public override void TakeDamage(int amount, Vector3 knockback)
    {
        base.TakeDamage(amount, knockback);
        RefreshHealthBar();
        _flashTimer = FlashDuration;
    }
}
