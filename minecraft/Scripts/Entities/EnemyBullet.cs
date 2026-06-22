using Godot;

// Generic straight-line projectile shared by multiple enemy types (e.g. GroundRobotShooter).
// No gravity/arc — travels in a fixed direction for LifeTime seconds then disappears.
// Damages the player on contact. Parrying (jackhammer) is a future addition, not handled yet.
// Visuals/collision shapes live in enemy_bullet.tscn (mesh, CollisionShape3D, HitArea) —
// new bullet variants can be built as their own scene with this same script.
public partial class EnemyBullet : Projectile
{
	public int Damage { get; set; } = 10;

	public override void ImHere()
	{
		base.ImHere();
		Gravity  = 0f;
		LifeTime = 10f;
	}

	public override void OnHitEntity(Entity entity)
	{
		GD.Print($"[EnemyBullet] OnHitEntity: {entity.Name} ({entity.GetType().Name})");
		if (entity is not Player player) return;
		var kb = Velocity.Normalized() * 5f;
		player.TakeDamage(Damage, kb);
		QueueFree();
	}

	public override void OnHitWorld()
	{
		var box = GetAABB();
		GD.Print($"[EnemyBullet] OnHitWorld at {GlobalPosition}, size={width} - {height}, age={age:F3}s, box=[{box.Position} .. {box.End}]");

		int minX = (int)Mathf.Floor(box.Position.X), maxX = (int)Mathf.Ceil(box.End.X);
		int minY = (int)Mathf.Floor(box.Position.Y), maxY = (int)Mathf.Ceil(box.End.Y);
		int minZ = (int)Mathf.Floor(box.Position.Z), maxZ = (int)Mathf.Ceil(box.End.Z);
		for (int x = minX; x <= maxX; x++)
		for (int y = minY; y <= maxY; y++)
		for (int z = minZ; z <= maxZ; z++)
		{
			var bp = new Vector3I(x, y, z);
			int id = Global.CubeManager.get_block(bp);
			if (id != 0)
				GD.Print($"[EnemyBullet]   solid block at {bp} id={id}");
		}

		base.OnHitWorld();
	}
}
