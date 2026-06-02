# Antithesis Conquering Simulator — Claude Context

## What this project is

A voxel-based action roguelike built in **Godot 4** (C# + GDScript). NOT a Minecraft clone. Game loop: choose a planet → fight → collect upgrades → next planet → boss. All combat, no crafting, no inventory. See `NEW_VISION.md` for full design doc, `PROGRESS.md` for current state, `TODO.md` for next steps.

---

## Tech stack

- **Godot 4** (.NET C#)
- **C#** — chunk generation, mesh building, physics, entity logic, player abilities
- **GDScript** — camera (interactions.gd), debug HUD (DebugMenu.gd)
- `Minecraft.csproj` compiles all `.cs` files

---

## Project structure

```
Scripts/
├── Handlers/         Global.cs (autoload singleton), DebugMenu.gd
├── The World/        Chunk_Manager.cs, Chunk.cs, Block_Registry.cs, Block_Model.cs
│   └── Generation/   World_Generator.cs  ← 5-stage pipeline, ALL STAGES EMPTY
├── Entities/         Entity.cs, GrappleHook.cs, Creature.cs, Projectile.cs
│   └── Player/       Player.cs, PlayerAbilities.cs, interactions.gd, inventory.gd (ARCHIVED)
└── Datasets/         Block_Registry.cs, Item_Registry.cs (ARCHIVED stub)
Assets/               character.tscn, creature.tscn
                      GrappleHook.tscn  ← NEEDS TO BE CREATED BY USER
Scenes/               CubeLand.tscn (main scene)
Washed Code/          Old/abandoned code — read-only reference, do not add to it
```

---

## Archived systems — do not restore or build on

| File | What it was |
|---|---|
| `Scripts/Entities/Player/inventory.gd` | 36-slot Minecraft inventory (extends Node3D stub kept for scene compat) |
| `Scripts/Datasets/Item_Registry.cs` | Minecraft item registry (Node stub kept for autoload) |
| `Scripts/Entities/Item_Definition.cs` | Item data schema |
| `Scripts/Entities/Item.cs` | World-dropped item entity |
| `Scripts/Entities/Item Behaviors/IItemBehavior.cs` | Item behavior interface |
| `Scripts/Entities/Item Behaviors/ToolBehavior.cs` | Mining tool |
| `Scripts/Entities/Item Behaviors/PlaceableBehavior.cs` | Block placement |
| `Scripts/Entities/Item Behaviors/ConsumableBehavior.cs` | Food/consumable |

---

## Critical architecture — read before touching movement or abilities

### Dual velocity channels

`Player.cs` splits velocity into two independent vectors:

- **`_inputVel`** — WASD steering, gravity, jump. Has friction, speed cap (ground only), coyote jump.
- **`_abilityVel`** — dash, grapple pull/lunge, jackhammer bounce. Decays slowly (0.97/tick air; XZ only 0.85/tick on physical ground; Y always 0.97). No hard cap.
- `Velocity = _inputVel + _abilityVel` — recomputed in `ApplyMovement` AND again after `ProcessAbilities` so ability impulses hit collision the same frame.
- On block collision any axis: both channel components zeroed for that axis in `CheckWorldCollisionsWithStepUp`.
- Abilities write ONLY to `_abilityVel`. Never write directly to `Velocity` from an ability.

### PhysicallyOnFloor() vs OnFloor()

- `PhysicallyOnFloor()` — actual block contact, no grace period. Used for: friction multiplier, speed cap, ability XZ decay.
- `OnFloor()` — includes 0.2s coyote grace. Used ONLY for: jump eligibility check.
- Never use `OnFloor()` for friction — it causes ground friction to fire mid-air during coyote window.

### Air jump system

Declared in `Player.cs`: `_airJumps`, `_wasPhysOnFloor`.

- Leave ground → `_airJumps = max(existing, 1)`
- Grapple hook attaches (`OnAttach` callback) → `_airJumps += 1`
- Grapple lunge (release while Attached) → `_airJumps += 1`
- Land → `_airJumps = 0`
- Air jump input: `IsActionJustPressed("jump")` while `!isOnFloor` and `_airJumps > 0`. Clears `_abilityVel.Y`.

### PlayerAbilities.cs

Partial class of Player — shares all private fields. All four abilities here:

| Ability | Input action | Key state |
|---|---|---|
| Jackhammer | `attack1` hold/release | `JackhammerCharging`, `JackhammerCharge` |
| Laser | `attack2` press | `LaserActive`, `LaserTimer`, `LaserCooldown` |
| Grapple | `grapple_send` | `CurrentGrappleState` (Idle/Sent/Attached), `GrappleAnchor` |
| Dash | `dash` press | `DashCooldown` |

Public state properties are the **accessory hook-in points**. Accessories read these flags rather than adding their own input handling.

Grapple states:
- `Idle` → press fires hook
- `Sent` → hook in flight; release = immediate retract; re-press = despawn + refire
- `Attached` → acceleration pull; release = lunge + `_airJumps += 1`; re-press = cancel

### GrappleHook.cs

Standalone `Node3D`. Does NOT extend Entity. Properties set before `AddChild`, `GlobalPosition` set AFTER `AddChild` (Godot requirement). State machine: `Flying → Retracting → Done`. Block detection via `Global.CubeManager.get_block()`. Entity detection via `Area3D` `BodyEntered`. `StartRetract()` method for immediate recall.

**The scene `Assets/GrappleHook.tscn` still needs to be created:**
```
Node3D  (name: GrappleHook, script: GrappleHook.cs)
├── MeshInstance3D  (name: Mesh)
└── Area3D  (name: HitArea, Collision Layer: 0, Mask: Layer 2)
    └── CollisionShape3D  (SphereShape3D, radius 0.2)
```
After creating: assign to `GrappleHookScene` export on the Player node in Inspector.

### Entity.cs

Base class for all entities. Manual AABB collision against voxel blocks. Do NOT use Godot physics engine for entity-world collision. `TakeDamage(int amount, Vector3 knockback)` overload exists for combat. Override `OnLandedOnBlock` and `OnBlockCollision` for custom behavior.

---

## Chunk_Manager.cs — do not casually refactor

~1400 lines. Threaded chunk generation. The `explode()` method is the primary combat terrain-destruction tool — wire new weapon effects through it. The 5-stage world generator pipeline in `World_Generator.cs` is meant to replace the direct FastNoise2D call in Chunk_Manager — that's the next major world system task.

---

## Conventions

- New enemy types extend `Entity.cs`
- New abilities go in `PlayerAbilities.cs`, write to `_abilityVel`
- New blocks go in `Block_Registry.cs`
- No Minecraft systems (crafting, farming, hunger, sleep, building)
- `Washed Code/` is read-only reference — don't add to it
- `interactions.gd` handles camera, block targeting, explosion trigger — add new input wiring here if needed from GDScript side
