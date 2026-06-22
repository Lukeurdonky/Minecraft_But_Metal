using Godot;
using System;
using System.Collections.Generic;

public partial class Projectile : Entity
{
	public float Gravity = 9.8f;
	public float LifeTime = 5.0f; // seconds
	protected float age = 0.0f;
	private Area3D hitArea;

	public override void ImHere()
	{
		base.ImHere();

		// World collision is manual (HandleWorldCollisions/GetAABB), not physics-layer
		// based, and entity hits go through the dedicated hitArea below — so this body
		// doesn't need to participate in Godot's physics collision response at all.
		// Layer/mask = 0 was previously layer=3/mask=1, which made every enemy-fired
		// projectile physically collide with its own shooter (and the player) via
		// MoveAndSlide the instant it spawned — visible as the shooter jittering sideways
		// on every shot.
		CollisionLayer = 0;
		CollisionMask = 0;

		// Add collision shape if not already in scene (code-only subclasses like
		// EnemyBolt have no .tscn; scene-based bullets like enemy_bullet.tscn define
		// their own sized shape and visuals declaratively)
		if (GetNodeOrNull<CollisionShape3D>("CollisionShape3D") == null)
		{
			CollisionShape3D shape = new CollisionShape3D();
			shape.Shape = new SphereShape3D() { Radius = 0.1f };
			AddChild(shape);
		}

		// Area3D for entity-hit detection. Reuse the scene's "HitArea" if present,
		// otherwise build one (code-only subclasses).
		hitArea = GetNodeOrNull<Area3D>("HitArea");
		if (hitArea == null)
		{
			hitArea = new Area3D();
			var areaShape = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.15f } };
			hitArea.AddChild(areaShape);
			AddChild(hitArea);
		}
		hitArea.CollisionLayer = 0;  // Don't need to be detected
		// Bit 8 (layer 4) is Player-exclusive (set in Player.ImHere()). Every enemy also
		// sits on layer 1 (collision_layer = 3, bits 1+2), so masking bit 1 to find the
		// player would also match the projectile's own shooter — bit 8 is the only bit
		// that uniquely identifies the player.
		hitArea.CollisionMask = 8;
		hitArea.BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		// Check if we hit an entity (but not ourselves)
		if (body is Entity entity && entity != this)
		{
			OnHitEntity(entity);
		}
	}

	public virtual void OnHitEntity(Entity entity)
	{
		// Logic for when projectile hits an entity
		QueueFree(); // Destroy projectile on hit
	}

	public virtual void OnHitWorld()
	{
		// Logic for when projectile hits an entity
		QueueFree(); // Destroy projectile on hit
	}

	public override void HandleWorldCollisions(Vector3 moveBy)
	{
		// Placeholder for world collision handling
		if(CheckWorldCollisions(moveBy))
		{
			OnHitWorld();
		}
	}

	public override void ApplyMovementFromInput(double delta)
	{
		age += (float)delta;
		if (age > LifeTime)
		{
			QueueFree();
			return;
		}

		// Apply gravity
		Velocity += Vector3.Down * Gravity * (float)delta;

		base.ApplyMovementFromInput(delta);
	}
}
