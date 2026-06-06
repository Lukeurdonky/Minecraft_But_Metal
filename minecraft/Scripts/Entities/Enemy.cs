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

    private const float BarWidth  = 1.2f;
    private const float BarHeight = 0.1f;

    public override void ImHere()
    {
        base.ImHere();
        BuildHealthBar();
    }

    public override void _Process(double delta)
    {
        if (_healthBarRoot == null) return;
        var cam = Global.Instance?.Player?.Camera;
        if (cam == null) return;
        // Look away from camera so the root's +Z (QuadMesh face direction) points at the camera
        var toCamera = cam.GlobalPosition - _healthBarRoot.GlobalPosition;
        if (toCamera.LengthSquared() > 0.001f)
            _healthBarRoot.LookAt(_healthBarRoot.GlobalPosition - toCamera, Vector3.Up);
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

        RefreshHealthBar();
    }

    private void RefreshHealthBar()
    {
        if (_healthBarFg == null) return;
        float ratio = MaxHealth > 0 ? Mathf.Clamp((float)CurrentHealth / MaxHealth, 0f, 1f) : 0f;

        ((QuadMesh)_healthBarFg.Mesh).Size = new Vector2(BarWidth * ratio, BarHeight * 0.7f);
        // Shift left so the bar shrinks from the right
        _healthBarFg.Position = new Vector3((ratio - 1f) * BarWidth * 0.5f, 0f, 0.001f);
        // Green → red as health drops
        _healthBarFgMat.AlbedoColor = new Color(1f - ratio, 0.8f * ratio, 0.1f);
    }

    public override void TakeDamage(int amount)
    {
        base.TakeDamage(amount);
        RefreshHealthBar();
    }

    public override void TakeDamage(int amount, Vector3 knockback)
    {
        base.TakeDamage(amount, knockback);
        RefreshHealthBar();
    }
}
