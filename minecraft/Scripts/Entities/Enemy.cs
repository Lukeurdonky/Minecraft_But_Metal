using Godot;

// Base class for all enemies. Inherits Entity, adds combat stats, detection range,
// and a procedurally built world-space health bar.
// Creature (and future enemy types) extend this, not Entity directly.
public partial class Enemy : Entity
{
    [Export] public int   AttackDamage   { get; set; } = 10;
    [Export] public float DetectionRange { get; set; } = 15f;
    [Export] public bool  Flying                       = false;

    private Node3D             _healthBarRoot;
    private MeshInstance3D     _healthBarFg;
    private StandardMaterial3D _healthBarFgMat;
    private float              _flashTimer = 0f;
    private const float        FlashDuration = 0.12f;

    private const float BarWidth  = 1.2f;
    private const float BarHeight = 0.1f;

    private Godot.Collections.Array<Node> _particles;
    private Godot.Collections.Array<Node> _animPlayers;

    public override void ImHere()
    {
        base.ImHere();
        BuildHealthBar();
        _particles   = FindChildren("*", "UniParticles3D",  true, false);
        _animPlayers = FindChildren("*", "AnimationPlayer", true, false);
        if (Global.Instance != null) Global.Instance.EnemyCount++;
    }

    public override void Die()
    {
        if (Global.Instance != null) Global.Instance.EnemyCount--;
        base.Die();
    }

    public override void _Process(double delta)
    {
        bool hitstop = Global?.HitstopActive == true;
        foreach (var node in _particles)
            node.Set("paused", hitstop);
        foreach (var node in _animPlayers)
            if (node is AnimationPlayer ap)
                ap.SpeedScale = hitstop ? 0f : 1f;

        if (_healthBarRoot == null) return;
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
