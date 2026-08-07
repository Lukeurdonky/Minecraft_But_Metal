using Godot;

// Low, four-legged, spike-covered grazer of the grassy biomes. The easy enemy: it is
// fast on the ground and dies to a single weak jackhammer blow (20 damage — see
// PlayerAbilities.JackhammerDamageWeak; MaxHealth is set to match in the scene).
//
// It has no attack of its own. Damage happens purely by contact — the spikes do the
// work, so there is no wind-up, no telegraph and nothing to dodge except the creature
// itself. That is deliberately different from Creature's lunge-and-grab: this one is a
// hazard that walks.
//
// Movement is the same grounded pattern GroundRobotShooter uses (gravity, turn-toward,
// auto-jump 1-block walls) at 40% of its speed again — 2.5 -> 3.5 u/s.
public partial class MossCreature : Enemy
{
	[Export] public float MoveSpeed   { get; set; } = 3.5f;
	[Export] public float MoveAccel   { get; set; } = 14f;
	[Export] public float TurnSpeed   { get; set; } = 6f;
	[Export] public float JumpImpulse { get; set; } = 9f;

	// Contact damage. ContactRange is only a cheap pre-filter for the hitbox test below —
	// the actual hit needs real overlap, so widening it does not widen the hitbox. The
	// hitbox itself is ContactHitbox/HitboxShape in the scene, sized to the *visible* body:
	// the entity's own movement AABB is a 0.8 square while the model is 1.37 long, so
	// testing against that instead meant the spikes visibly overlapped the player with no
	// hit registering. Kept square in XZ so a rotating creature has even reach — the test
	// is axis-aligned (same approximation Creature.cs makes) and would otherwise breathe
	// as it turned.
	[Export] public float ContactRange          { get; set; } = 3f;
	[Export] public float ContactDamageInterval { get; set; } = 0.75f;
	[Export] public float KnockbackStrength     { get; set; } = 14f;
	[Export] public float KnockbackUpFactor     { get; set; } = 0.45f;

	// The exported clip runs slightly long and visibly hitches before it repeats, so the
	// loop point is pulled in to 1.94s. Kept here rather than in the .glb import because
	// Godot's scene importer can set an animation's loop mode but not its length, and a
	// re-export from Blender would silently restore the late loop.
	[Export] public float  WalkLoopSeconds { get; set; } = 1.94f;
	[Export] public string WalkAnimation   { get; set; } = "Walk";

	private const float Gravity = 20f;
	// A block only counts as a step up if its top surface clears the feet by this much —
	// without it, floating-point noise on the block it is already standing on reads as a wall.
	private const float StepUpEpsilon = 0.05f;

	private AnimationPlayer  _anim;
	private CollisionShape3D _hitboxShape;
	private bool  _isChasing      = false;
	private float _contactCooldown = 0f;

	public override void ImHere()
	{
		base.ImHere();
		// Stats live on the scene's exports (MaxHealth 20, width/height ~1 block) so they
		// stay inspector-tunable, same as the other enemies.
		Flying = false;

		_hitboxShape = GetNodeOrNull<CollisionShape3D>("ContactHitbox/HitboxShape");

		_anim = GetNodeOrNull<AnimationPlayer>("MossCreature/AnimationPlayer");
		if (_anim == null) return;

		var walk = _anim.GetAnimation(WalkAnimation);
		if (walk != null)
		{
			// Setting LoopMode here is what lets the clip repeat on its own — Creature.cs
			// has to re-Play() on AnimationFinished precisely because it never does this.
			walk.LoopMode = Animation.LoopModeEnum.Linear;
			if (!Mathf.IsEqualApprox((float)walk.Length, WalkLoopSeconds))
				walk.Length = WalkLoopSeconds;
		}
		// Walk is the only clip and it always plays. Enemy._Process still owns whether it
		// advances (SpeedScale 0 at Far tier and during hitstop) — don't gate it here too.
		_anim.Play(WalkAnimation);
	}

	public override void ApplyMovementFromInput(double delta)
	{
		base.ApplyMovementFromInput(delta); // updates DistSqToPlayer / Lod

		float   dt   = (float)delta;
		Vector3 flat = (Global.GetPlayerPos() - GlobalPosition) with { Y = 0f };
		_isChasing   = DistSqToPlayer <= DetectionRange * DetectionRange;

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
		}
		else
		{
			vel.X = Mathf.MoveToward(vel.X, 0f, MoveAccel * dt);
			vel.Z = Mathf.MoveToward(vel.Z, 0f, MoveAccel * dt);
		}

		Velocity = vel;
		UpdateContactDamage(dt);
	}

	// Touching it hurts. Squared-distance pre-filter first so the box build only happens
	// when the player is actually near — per the enemy LOD rules, nothing expensive runs
	// unconditionally per frame.
	private void UpdateContactDamage(float dt)
	{
		_contactCooldown = Mathf.Max(_contactCooldown - dt, 0f);
		if (_contactCooldown > 0f || DistSqToPlayer > ContactRange * ContactRange) return;

		var player = Global.Instance?.Player;
		if (player == null) return;

		// Falls back to the movement AABB only if the scene is missing the hitbox — that box
		// is smaller than the model, so a fallback hit is a hint the node got deleted.
		var box = _hitboxShape?.Shape as BoxShape3D;
		Aabb hitAabb = box != null
			? new Aabb(_hitboxShape.GlobalPosition - box.Size / 2f, box.Size)
			: GetAABB();
		if (!hitAabb.Intersects(player.GetAABB())) return;

		var knockback = (player.GlobalPosition - GlobalPosition).Normalized() * KnockbackStrength;
		knockback.Y += KnockbackStrength * KnockbackUpFactor;
		player.TakeDamage(AttackDamage, knockback);
		_contactCooldown = ContactDamageInterval;
	}

	// Auto-jump 1-block walls while chasing — same traversal the other ground enemies use,
	// otherwise it snags on every terrain lip and never reaches the player.
	//
	// Deliberately NOT GroundRobotShooter's `blockPos.Y > floor(GlobalPosition.Y - height/2)`.
	// That shorthand ignores `offset`, and it only works there because that enemy's offset is
	// positive, which happens to floor one cell lower. This creature's offset is negative, so
	// the same expression landed on the wall block's own cell, `>` was never true, and it
	// never stepped up at all. Compare the block's top surface against the real box bottom
	// (GetAABB() includes offset) instead — correct for any offset.
	protected override void OnBlockCollision(Vector3 faceNormal, Vector3I blockPos)
	{
		if (!_isChasing || !PhysicallyOnFloor()) return;
		float feetY = GetAABB().Position.Y;
		if (blockPos.Y + 1f > feetY + StepUpEpsilon)
			Velocity = new Vector3(Velocity.X, JumpImpulse, Velocity.Z);
	}
}
