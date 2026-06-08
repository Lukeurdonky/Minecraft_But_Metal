using Godot;

// Drives all player-state HUD indicators from one place.
// To add a new indicator: declare an [Export], auto-wire in _Ready, react to state changes in _Process.
public partial class PlayerHUD : Control
{
    [Export] public AnimationPlayer JumpAnimPlayer  { get; set; }
    [Export] public Node2D          EnemyIndicator  { get; set; }
    [Export] public CanvasItem      Crosshair       { get; set; }

    private Player    _player;
    private bool      _hadJump           = false;
    private string    _lastEnemyAnim     = "";
    private bool      _lastGrappleTarget = false;

    // Health bar + laser bar — nodes live in CanvasLayer/BarContainer
    [Export] public ColorRect HpBarFg     { get; set; }
    [Export] public ColorRect LaserBarFg  { get; set; }
    [Export] public ColorRect SpeedSection1 { get; set; }
    [Export] public ColorRect SpeedSection2 { get; set; }
    [Export] public ColorRect SpeedSection3 { get; set; }
    [Export] public ColorRect HitFlash    { get; set; }

    private float _hpBarLeft;
    private float _hpBarFullRight;
    private float _laserBarLeft;
    private float _laserBarFullRight;

    private int   _lastHealth    = -1;
    private float _hitFlashTimer = 0f;
    private const float HitFlashDuration = 0.4f;

    public override void _Ready()
    {
        JumpAnimPlayer ??= GetNodeOrNull<AnimationPlayer>("Panel/Jump/AnimationPlayer");
        EnemyIndicator ??= GetNodeOrNull<Node2D>("Panel/Enemy");
        Crosshair      ??= GetNodeOrNull<CanvasItem>("Panel/Crosshair");
        HpBarFg        ??= GetNodeOrNull<ColorRect>("../BarContainer/HPBarFg");
        LaserBarFg     ??= GetNodeOrNull<ColorRect>("../BarContainer/LaserBarFg");
        SpeedSection1  ??= GetNodeOrNull<ColorRect>("../BarContainer/SpeedSection1");
        SpeedSection2  ??= GetNodeOrNull<ColorRect>("../BarContainer/SpeedSection2");
        SpeedSection3  ??= GetNodeOrNull<ColorRect>("../BarContainer/SpeedSection3");
        HitFlash       ??= GetNodeOrNull<ColorRect>("../HitFlash");

        if (HpBarFg != null)
        {
            _hpBarLeft      = HpBarFg.OffsetLeft;
            _hpBarFullRight = HpBarFg.OffsetRight;
        }
        if (LaserBarFg != null)
        {
            _laserBarLeft      = LaserBarFg.OffsetLeft;
            _laserBarFullRight = LaserBarFg.OffsetRight;
        }
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
        UpdateHealthBar();
        UpdateLaserBar();
        UpdateSpeedBar();
        UpdateHitFlash((float)delta);
    }

    private void UpdateJumpIndicator()
    {
        bool hasJump = _player.AirJumpsAvailable > 0;
        if (hasJump == _hadJump) return;
        _hadJump = hasJump;
        JumpAnimPlayer?.Play(hasJump ? "JumpUITrue" : "JumpUIFalse");
    }

    private static readonly Color LaserColorReady    = new Color(0.2f, 0.5f, 1.0f);
    private static readonly Color LaserColorRecharge = new Color(0.35f, 0.35f, 0.35f);

    private void UpdateLaserBar()
    {
        if (LaserBarFg == null) return;
        bool recharging = _player.LaserCooldown > 0f && !_player.LaserActive;
        float ratio;
        if (_player.LaserActive)
            ratio = _player.LaserTimer / 1.5f;
        else if (recharging)
            ratio = 1f - (_player.LaserCooldown / 7.0f);
        else
            ratio = 1f;

        LaserBarFg.Color       = recharging ? LaserColorRecharge : LaserColorReady;
        LaserBarFg.OffsetRight = _laserBarLeft + (_laserBarFullRight - _laserBarLeft) * Mathf.Clamp(ratio, 0f, 1f);
    }

    private void UpdateHealthBar()
    {
        if (HpBarFg == null) return;
        float ratio = _player.MaxHealth > 0
            ? Mathf.Clamp((float)_player.CurrentHealth / _player.MaxHealth, 0f, 1f)
            : 0f;
        HpBarFg.OffsetRight = _hpBarLeft + (_hpBarFullRight - _hpBarLeft) * ratio;
    }

    private static readonly Color SpeedColorWeak = new Color(0.2f,  0.85f, 0.2f);
    private static readonly Color SpeedColorMed  = new Color(1.0f,  0.85f, 0.0f);
    private static readonly Color SpeedColorHard = new Color(1.0f,  0.2f,  0.2f);
    private const float SpeedSectionDim = 0.12f;

    private void UpdateSpeedBar()
    {
        if (SpeedSection1 == null) return;

        int   raw  = _player.RawSpeedTier;
        float hard = _player.HardCoyoteTimer;
        float med  = _player.MedCoyoteTimer;

        // Coyote flash: oscillate when coyote is active for a tier we've left.
        float t = (float)Time.GetTicksMsec() / 1000f;
        float flash = (Mathf.Sin(t * Mathf.Pi * 14f) + 1f) * 0.5f; // 3 Hz, 0..1

        bool hardFlashing = hard > 0f && raw < 2;
        bool medFlashing  = med  > 0f && raw < 1;

        float hardFlashA, medFlashA, weakA;
        if (hardFlashing)
        {
            hardFlashA = Mathf.Lerp(SpeedSectionDim, 1f, flash) * (hard / 0.5f);
            medFlashA  = SpeedSectionDim;
            weakA      = SpeedSectionDim;
        }
        else if (medFlashing)
        {
            hardFlashA = SpeedSectionDim;
            medFlashA  = Mathf.Lerp(SpeedSectionDim, 1f, flash) * (med / 0.5f);
            weakA      = SpeedSectionDim;
        }
        else
        {
            hardFlashA = raw == 2 ? 1f : SpeedSectionDim;
            medFlashA  = raw == 1 ? 1f : SpeedSectionDim;
            weakA      = raw == 0 ? 1f : SpeedSectionDim;
        }

        SetSectionAlpha(SpeedSection1, SpeedColorWeak, weakA);
        SetSectionAlpha(SpeedSection2, SpeedColorMed,  medFlashA);
        SetSectionAlpha(SpeedSection3, SpeedColorHard, hardFlashA);
    }

    private static void SetSectionAlpha(ColorRect rect, Color col, float alpha)
    {
        col.A        = alpha;
        rect.Color   = col;
    }

    private void UpdateHitFlash(float dt)
    {
        if (HitFlash == null) return;

        int hp = _player.CurrentHealth;
        if (_lastHealth < 0) { _lastHealth = hp; }
        else if (hp < _lastHealth) { _hitFlashTimer = HitFlashDuration; }
        _lastHealth = hp;

        if (_hitFlashTimer > 0f)
        {
            _hitFlashTimer = Mathf.Max(_hitFlashTimer - dt, 0f);
            float alpha = _hitFlashTimer / HitFlashDuration * 0.35f;
            HitFlash.Modulate = new Color(1f, 1f, 1f, alpha);
        }
        else
        {
            HitFlash.Modulate = new Color(1f, 1f, 1f, 0f);
        }
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
