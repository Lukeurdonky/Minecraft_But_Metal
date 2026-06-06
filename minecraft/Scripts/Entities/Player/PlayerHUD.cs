using Godot;

// Drives all player-state HUD indicators from one place.
// To add a new indicator: declare an [Export], auto-wire in _Ready, react to state changes in _Process.
public partial class PlayerHUD : Control
{
    [Export] public AnimationPlayer JumpAnimPlayer  { get; set; }
    [Export] public Node2D          EnemyIndicator  { get; set; }
    [Export] public CanvasItem      Crosshair       { get; set; }

    private Player _player;
    private bool   _hadJump           = false;
    private string _lastEnemyAnim     = "";
    private bool   _lastGrappleTarget = false;

    public override void _Ready()
    {
        JumpAnimPlayer ??= GetNodeOrNull<AnimationPlayer>("Panel/Jump/AnimationPlayer");
        EnemyIndicator ??= GetNodeOrNull<Node2D>("Panel/Enemy");
        Crosshair      ??= GetNodeOrNull<CanvasItem>("Panel/Crosshair");
    }

    public override void _Process(double delta)
    {
        if (_player == null)
        {
            _player = Global.Instance?.Player;
            if (_player == null) return;
            // Sync initial jump state without waiting for a transition
            _hadJump = _player.AirJumpsAvailable > 0;
            JumpAnimPlayer?.Play(_hadJump ? "JumpUITrue" : "JumpUIFalse");
            return;
        }

        UpdateJumpIndicator();
        UpdateEnemyIndicator();
        UpdateCrosshair();
    }

    private void UpdateJumpIndicator()
    {
        bool hasJump = _player.AirJumpsAvailable > 0;
        if (hasJump == _hadJump) return;
        _hadJump = hasJump;
        JumpAnimPlayer?.Play(hasJump ? "JumpUITrue" : "JumpUIFalse");
    }

    private void UpdateCrosshair()
    {
        if (Crosshair == null) return;
        bool hasTarget = _player.HasGrappleTarget;
        if (hasTarget == _lastGrappleTarget) return;
        _lastGrappleTarget = hasTarget;

        var m = Crosshair.Modulate;
        m.R = hasTarget ? .5f : 1f;
        m.G = hasTarget ? 1f : 1f;
        m.B = hasTarget ? .5f : 1f;
        Crosshair.Modulate = m;
    }

    private void UpdateEnemyIndicator()
    {
        if (EnemyIndicator == null) return;

        var enemy         = _player.SelectedEnemy;
        var grappledEnemy = _player.GrappledEntity;

        string targetAnim;
        if (grappledEnemy != null && GodotObject.IsInstanceValid(grappledEnemy))
            targetAnim = "EnemyUIGrappling";
        else if (enemy != null && GodotObject.IsInstanceValid(enemy))
            targetAnim = "EnemyUISpin";
        else
            targetAnim = "";

        if (targetAnim == "")
        {
            EnemyIndicator.Visible = false;
            _lastEnemyAnim         = "";
            return;
        }

        EnemyIndicator.Visible = true;

        if (targetAnim != _lastEnemyAnim)
        {
            GetNodeOrNull<AnimationPlayer>("Panel/Enemy/AnimationPlayer")?.Play(targetAnim);
            _lastEnemyAnim = targetAnim;
        }

        var trackTarget = grappledEnemy ?? enemy;
        EnemyIndicator.GlobalPosition = _player.Camera.UnprojectPosition(trackTarget.GetCenter());
    }
}
