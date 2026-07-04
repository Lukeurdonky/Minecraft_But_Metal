// All 10 accessories from documents/design/NEW_VISION.md. None have real gameplay
// effects yet — CreateInstance returns a stub Accessory subclass for each.
public static class Accessory_Registry
{
    public static readonly AccessoryDescriptor[] All = new AccessoryDescriptor[]
    {
        new AccessoryDescriptor {
            Name = "Super Jump", Description = "Increases jump height.", IconIndex = 36,
            CreateInstance = () => new SuperJumpAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Super Slam", Description = "Jackhammer release always triggers an explosion at the impact point.", IconIndex = 37,
            CreateInstance = () => new SuperSlamAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Explosive Bounce", Description = "Ramming into a block fast enough to break it explodes it and bounces you back.", IconIndex = 38,
            CreateInstance = () => new ExplosiveBounceAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Destructive Laser", Description = "Laser tunnels a much wider hole through blocks.", IconIndex = 39,
            CreateInstance = () => new DestructiveLaserAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Little Friend", Description = "A companion that fights alongside you.", IconIndex = 40,
            CreateInstance = () => new LittleFriendAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Glide", Description = "Slow fall while holding jump in the air.", IconIndex = 41,
            CreateInstance = () => new GlideAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Dig Dig Dig!", Description = "Jackhammer mines blocks faster; vertical escape tool.", IconIndex = 42,
            CreateInstance = () => new DigDigDigAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Flaming Grapple", Description = "Applies fire on grapple pull/lunge.", IconIndex = 43,
            CreateInstance = () => new FlamingGrappleAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Tech Vision", Description = "Reveals additional information about the world and enemies.", IconIndex = 44,
            CreateInstance = () => new TechVisionAccessory(),
        },
        new AccessoryDescriptor {
            Name = "Exo Suit", Description = "General mobility upgrade.", IconIndex = 45,
            CreateInstance = () => new ExoSuitAccessory(),
        },
    };

public static AccessoryDescriptor Get(string name) =>
        System.Array.Find(All, a => a.Name == name);
}
