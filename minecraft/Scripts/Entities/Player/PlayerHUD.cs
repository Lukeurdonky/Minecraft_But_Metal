using Godot;
using System.Collections.Generic;
using System.Linq;

// Drives all player-state HUD indicators from one place.
// To add a new indicator: declare an [Export], auto-wire in _Ready, react to state changes in _Process.
public partial class PlayerHUD : Control
{
    [Export] public AnimationPlayer JumpAnimPlayer  { get; set; }
    [Export] public Node2D          EnemyIndicator  { get; set; }
    [Export] public CanvasItem      Crosshair       { get; set; }
    [Export] public HBoxContainer   AccessoryRow    { get; set; }

    private Player    _player;
    private bool      _hadJump           = false;
    private float     _prevJumpMeter     = Player.JumpMeterMax;
    private string    _lastEnemyAnim     = "";
    private bool      _lastGrappleTarget = false;

    // Health bar + laser bar — nodes live in CanvasLayer/BarContainer
    [Export] public ColorRect HpBarFg     { get; set; }
    [Export] public ColorRect LaserBarFg  { get; set; }
    [Export] public ColorRect SpeedSection1 { get; set; }
    [Export] public ColorRect SpeedSection2 { get; set; }
    [Export] public ColorRect SpeedSection3 { get; set; }
    [Export] public ColorRect JumpSection1  { get; set; }
    [Export] public ColorRect JumpSection2  { get; set; }
    [Export] public ColorRect JumpSection3  { get; set; }
    [Export] public ColorRect HitFlash    { get; set; }

    [Export] public Label KillLabel  { get; set; }
    [Export] public Label TimerLabel { get; set; }

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
        AccessoryRow   ??= GetNodeOrNull<HBoxContainer>("RunUI/AccessoryRow");
        HpBarFg        ??= GetNodeOrNull<ColorRect>("../BarContainer/HPBarFg");
        LaserBarFg     ??= GetNodeOrNull<ColorRect>("../BarContainer/LaserBarFg");
        SpeedSection1  ??= GetNodeOrNull<ColorRect>("../BarContainer/SpeedSection1");
        SpeedSection2  ??= GetNodeOrNull<ColorRect>("../BarContainer/SpeedSection2");
        SpeedSection3  ??= GetNodeOrNull<ColorRect>("../BarContainer/SpeedSection3");
        JumpSection1   ??= GetNodeOrNull<ColorRect>("../BarContainer/JumpSection1");
        JumpSection2   ??= GetNodeOrNull<ColorRect>("../BarContainer/JumpSection2");
        JumpSection3   ??= GetNodeOrNull<ColorRect>("../BarContainer/JumpSection3");
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
            _prevJumpMeter = _player.JumpMeter;
            _hadJump       = _player.JumpMeter >= 1f;
            JumpAnimPlayer?.Play(_hadJump ? "JumpUITrue" : "JumpUIFalse");
            return;
        }

        UpdateJumpIndicator();
        UpdateJumpMeter();
        UpdateEnemyIndicator();
        UpdateCrosshair();
        UpdateHealthBar();
        UpdateLaserBar();
        UpdateSpeedBar();
        UpdateHitFlash((float)delta);
        UpdateRunStats();
        UpdateAccessoryRow();
    }

    private void UpdateJumpIndicator()
    {
        float meter       = _player.JumpMeter;
        bool  hasJump     = meter >= 1f;
        bool  jumpSpent   = _prevJumpMeter - meter >= 0.9f;
        _prevJumpMeter    = meter;

        if (jumpSpent)
        {
            // Flash off on every jump, even when charges remain
            _hadJump = false;
            JumpAnimPlayer?.Play("JumpUIFalse");
        }
        else if (hasJump != _hadJump)
        {
            _hadJump = hasJump;
            JumpAnimPlayer?.Play(hasJump ? "JumpUITrue" : "JumpUIFalse");
        }
    }

    private static readonly Color JumpColor    = new Color(0.5f, 0.9f, 1.0f);
    private const float JumpSectionDim = 0.12f;

    private void UpdateJumpMeter()
    {
        if (JumpSection1 == null) return;
        float meter = _player.JumpMeter;
        SetJumpSection(JumpSection1, Mathf.Clamp(meter - 0f, 0f, 1f));
        SetJumpSection(JumpSection2, Mathf.Clamp(meter - 1f, 0f, 1f));
        SetJumpSection(JumpSection3, Mathf.Clamp(meter - 2f, 0f, 1f));
    }

    private static void SetJumpSection(ColorRect rect, float fill)
    {
        var c = JumpColor;
        c.A       = Mathf.Lerp(JumpSectionDim, 1f, fill);
        rect.Color = c;
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

    private void UpdateRunStats()
    {
        if (Global.Instance == null) return;
        if (KillLabel  != null) KillLabel.Text  = $"{Global.Instance.KillCount}";
        if (TimerLabel != null)
        {
            float t = Global.Instance.RunTimer;
            TimerLabel.Text = $"{(int)(t / 60f):D2}:{(int)(t % 60f):D2}";
        }
    }

    // Same 12-col x 8-row / 16px-cell grid Item_Registry.cs used for this atlas.
    private const int AccessoryAtlasCols  = 12;
    private const int AccessoryIconSize   = 16;
    private static Texture2D _accessoryAtlas;

    private readonly List<string> _lastAccessoryNames = new();

    private static AtlasTexture MakeAccessoryIcon(int iconIndex)
    {
        _accessoryAtlas ??= GD.Load<Texture2D>("res://Sprites/Textures/item_texture_atlas.png");
        int col = iconIndex % AccessoryAtlasCols;
        int row = iconIndex / AccessoryAtlasCols;
        return new AtlasTexture
        {
            Atlas  = _accessoryAtlas,
            Region = new Rect2(col * AccessoryIconSize, row * AccessoryIconSize, AccessoryIconSize, AccessoryIconSize),
        };
    }

    private void UpdateAccessoryRow()
    {
        if (AccessoryRow == null) return;

        var current = _player.Accessories.Select(a => a.Name).ToList();
        if (current.SequenceEqual(_lastAccessoryNames)) return;

        _lastAccessoryNames.Clear();
        _lastAccessoryNames.AddRange(current);

        foreach (var child in AccessoryRow.GetChildren())
            child.QueueFree();

        foreach (var name in current)
        {
            var descriptor = Accessory_Registry.Get(name);
            AccessoryRow.AddChild(new TextureRect
            {
                Texture           = MakeAccessoryIcon(descriptor?.IconIndex ?? 0),
                CustomMinimumSize = new Vector2(24, 24),
                StretchMode       = TextureRect.StretchModeEnum.Scale,
                TextureFilter     = CanvasItem.TextureFilterEnum.Nearest,
            });
        }
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
