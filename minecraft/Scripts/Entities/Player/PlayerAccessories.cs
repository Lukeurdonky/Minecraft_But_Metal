using Godot;
using System.Collections.Generic;
using System.Linq;

// Accessory equip/unequip and the aggregation helpers PlayerAbilities.cs calls into.
// Adding a new accessory never requires touching this file — only Accessory_Registry
// and a new Accessory subclass. This file only grows when a genuinely new hook point
// (not covered by Process/PhysicsProcess or the existing Modify*/On* hooks) is needed.
public partial class Player : Entity
{
    private readonly List<Accessory> _accessories = new();
    public IReadOnlyList<Accessory> Accessories => _accessories;

    public bool HasAccessory(string name) => _accessories.Any(a => a.Name == name);

    public void EquipAccessory(string name)
    {
        if (HasAccessory(name)) return;
        var descriptor = Accessory_Registry.Get(name);
        if (descriptor == null) return;

        var instance = descriptor.CreateInstance();
        _accessories.Add(instance);
        instance.Attach(this);
    }

    public void UnequipAccessory(string name)
    {
        var instance = _accessories.FirstOrDefault(a => a.Name == name);
        if (instance == null) return;

        instance.Detach();
        _accessories.Remove(instance);
    }

    // Called from ImHere() — re-equips whatever RunManager/Global persisted across
    // the scene reload. Population of Global.EquippedAccessoryIds is not wired up yet.
    public void EquipStartingAccessories()
    {
        foreach (var name in Global.EquippedAccessoryIds)
            EquipAccessory(name);
    }

    private void ProcessAccessories(float delta)
    {
        foreach (var a in _accessories) a.Process(delta);
    }

    private void PhysicsProcessAccessories(float delta)
    {
        foreach (var a in _accessories) a.PhysicsProcess(delta);
    }

    private float ApplyJumpStrengthMods(float baseStrength)
    {
        float result = baseStrength;
        foreach (var a in _accessories) result = a.ModifyJumpStrength(result);
        return result;
    }

    private void NotifyGrappleAttach(Entity entity, Vector3 anchor)
    {
        foreach (var a in _accessories) a.OnGrappleAttach(entity, anchor);
    }
}
