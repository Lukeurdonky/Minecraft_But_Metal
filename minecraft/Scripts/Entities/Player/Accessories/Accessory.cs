using Godot;

// Base class for all accessories. Subclasses override only the hooks they need —
// continuous effects live in Process/PhysicsProcess (read Player's public state
// directly, e.g. Velocity, OnFloor(), CurrentGrappleState); one-shot effects tied
// to a specific ability moment use the discrete Modify*/On* hooks below, called
// from PlayerAbilities.cs at the point the context (radius/damage/target) exists.
public abstract class Accessory
{
    public abstract string Name { get; }
    protected Player Player { get; private set; }

    public void Attach(Player player)
    {
        Player = player;
        OnEquip();
    }

    public void Detach()
    {
        OnUnequip();
        Player = null;
    }

    public virtual void OnEquip() { }
    public virtual void OnUnequip() { }
    public virtual void Process(float delta) { }
    public virtual void PhysicsProcess(float delta) { }

    public virtual float ModifyJackhammerRadius(float radius) => radius;
    public virtual int   ModifyJackhammerDamage(int damage) => damage;
    public virtual float ModifyJackhammerImpulse(float impulse) => impulse;
    public virtual void  OnJackhammerImpact(Vector3I? blockPos, Vector3 impactPos) { }

    public virtual float ModifyJumpStrength(float strength) => strength;

    public virtual void OnGrappleAttach(Entity entity, Vector3 anchor) { }

    // Fired from ProcessSpeedThreshold when ramming into a block at high speed actually
    // breaks it (the existing "ram fast enough and blocks break" mechanic). position is
    // the player's position at the moment of impact, speed is the player's current speed.
    public virtual void OnSpeedImpact(Vector3 position, float speed) { }

    public virtual float ModifyLaserTunnelRadius(float radius) => radius;
    public virtual float ModifyLaserBeamRadius(float radius) => radius;
}
