using Godot;

// Grounded gunner. Rotates to face the player, walks slowly toward them, and
// auto-jumps over 1-block walls like the other ground enemies. Has a single
// "Aim" animation clip (GroundShooterReal/AnimationPlayer) whose 2s length
// spans the gun arm's full -90..+90 degree pitch sweep — instead of playing
// it, we scrub (Seek) to the position matching the player's vertical angle.
public partial class GroundRobotShooter : Enemy
{
	[Export] public float MoveSpeed   { get; set; } = 2.5f;
	[Export] public float MoveAccel   { get; set; } = 8f;
	[Export] public float TurnSpeed   { get; set; } = 3f;
	[Export] public float JumpImpulse { get; set; } = 10f;
	[Export] public float AimLerpSpeed { get; set; } = 12f;
	[Export] public float FireRate     { get; set; } = 2f;
	[Export] public float BulletSpeed  { get; set; } = 35f;
	[Export] public int   BulletDamage { get; set; } = 10;
	[Export] public Node3D FirePoint;
	private const float Gravity = 20f;
	// CacheMode.Replace: a static field initializer only runs once per assembly load, and
	// GD.Load's default cache mode happily hands back a PackedScene snapshotted from an
	// earlier edit of enemy_bullet.tscn within the same long-running editor session —
	// every .tscn-side tweak (width/height, shapes, mesh) was silently ignored at runtime
	// even though the file on disk was correct. Force a fresh read every time instead.
	private static readonly PackedScene BulletScene =
		(PackedScene)ResourceLoader.Load("res://Assets/enemy_bullet.tscn", "PackedScene", ResourceLoader.CacheMode.Replace);

	private AnimationPlayer _anim;
	private Node3D _gunArm;
	private float _aimAnimLength = 2f;
	private float _currentPitch  = 0f;
	private bool  _isChasing = false;
	private float _fireCooldown = 0f;

	public override void ImHere()
	{
		base.ImHere();
		// MaxHealth      = 100;
		// CurrentHealth  = 100;
		// AttackDamage   = 15;
		// DetectionRange = 30f;
		// heavy          = false;
		// Flying         = false;
		// width          = 1.0f;
		// height         = 2.0f;

		_anim   = GetNode<AnimationPlayer>("GroundShooterReal/AnimationPlayer");
		_gunArm = GetNode<Node3D>("GroundShooterReal/GunArm");
		if(FirePoint == null) FirePoint = _gunArm;
		var aimAnim = _anim.GetAnimation("Aim");
		if (aimAnim != null)
		{
			_aimAnimLength = (float)aimAnim.Length;
			// Seek() only moves the player's *current* animation — Play() once to assign
			// it, then drive position purely via Seek below (SpeedScale=0 so nothing
			// auto-advances; Enemy._Process's LOD gating can flip SpeedScale back to 1,
			// so it's re-zeroed every tick right before seeking, not just here).
			_anim.Play("Aim");
			_anim.SpeedScale = 0f;
		}
	}

	public override void ApplyMovementFromInput(double delta)
	{
		base.ApplyMovementFromInput(delta); // updates DistSqToPlayer / Lod

		float   dt        = (float)delta;
		Vector3 playerPos = Global.GetPlayerPos();
		Vector3 toPlayer  = playerPos - GlobalPosition;
		Vector3 flat      = toPlayer with { Y = 0f };
		_isChasing = DistSqToPlayer <= DetectionRange * DetectionRange;

		var vel = Velocity;
		if (!PhysicallyOnFloor()) vel.Y -= Gravity * dt;
		vel.Y = Mathf.Max(vel.Y, -MaxFallSpeed);

		if (_isChasing && flat.LengthSquared() > 0.01f)
		{
			float targetYaw = Mathf.Atan2(flat.X, flat.Z);
			Rotation = Rotation with { Y = Mathf.LerpAngle(Rotation.Y, targetYaw, TurnSpeed * dt) };

			var dir = flat.Normalized();
			vel.X = Mathf.MoveToward(vel.X, dir.X * MoveSpeed, MoveAccel * dt);
			vel.Z = Mathf.MoveToward(vel.Z, dir.Z * MoveSpeed, MoveAccel * dt);

			// Skip the (relatively costly) skeleton pose update at Far tier — see
			// documents/performance/ENEMY_PERFORMANCE.md, gate per-frame ops by Lod.
			if (Lod != LodTier.Far)
			{
				// Vertical angle to player: -90deg (straight down) .. +90deg (straight up)
				// maps linearly onto the clip's 0..length scrub range. Lerped so the arm
				// swings into place instead of snapping every tick.
				float targetPitch = Mathf.Atan2(toPlayer.Y, flat.Length());
				_currentPitch = Mathf.LerpAngle(_currentPitch, targetPitch, Mathf.Min(AimLerpSpeed * dt, 1f));
				float t = Mathf.Clamp(_currentPitch / Mathf.Pi + 0.5f, 0f, 1f);
				_anim.SpeedScale = 0f;
				_anim.Seek(t * _aimAnimLength, true);
			}

			_fireCooldown = Mathf.Max(_fireCooldown - dt, 0f);
			if (_fireCooldown <= 0f)
			{
				FireBullet(toPlayer.Normalized());
				_fireCooldown = FireRate;
			}
		}
		else
		{
			vel.X = Mathf.MoveToward(vel.X, 0f, MoveAccel * dt);
			vel.Z = Mathf.MoveToward(vel.Z, 0f, MoveAccel * dt);
		}

		Velocity = vel;
	}

	private void FireBullet(Vector3 direction)
	{
		//GD.Print($"[GroundRobotShooter] root={GlobalPosition}, muzzle={_gunArm.GlobalPosition}, dir={direction}");
		var bullet = BulletScene.Instantiate<EnemyBullet>();
		bullet.Damage = BulletDamage;
		
		GetTree().CurrentScene.AddChild(bullet);
		bullet.GlobalPosition = FirePoint.GlobalPosition;
		// LookAt errors if direction is parallel to the up vector (firing straight up/down)
		if (Mathf.Abs(direction.Dot(Vector3.Up)) < 0.999f)
			bullet.LookAt(bullet.GlobalPosition + direction, Vector3.Up);
		bullet.Velocity = direction * BulletSpeed;
	}

	// Auto-jump over 1-block walls when chasing and grounded
	protected override void OnBlockCollision(Vector3 faceNormal, Vector3I blockPos)
	{
		if (!_isChasing || !PhysicallyOnFloor()) return;
		int footY = (int)Mathf.Floor(GlobalPosition.Y - height / 2f);
		if (blockPos.Y > footY)
			Velocity = new Vector3(Velocity.X, JumpImpulse, Velocity.Z);
	}
}
