# Antithesis Conquering Simulator — Project State

> You're in your spaceship. You go to randomly generated planets. You kill things.

A voxel-based action roguelike built in **Godot 4** (C# + GDScript). Each run: choose a planet → fight → collect upgrades → boss. All combat, no crafting, no inventory. See `NEW_VISION.md` for the full design doc.

---

## Design Pillars

- All combat, no filler
- Fun over narrative
- Still descending — the world goes deep
- Darker palette + bright electronic enemies
- Destructible world

## Game Loop

```
Choose 1 of 3 planets → Difficulty → Clear / Survive → Upgrades → Next planet → Boss
```

---

## Architecture Overview

C# for all performance-critical systems, GDScript for camera/HUD/scene scripting.

```
Scripts/
├── Handlers/         Global.cs (singleton), DebugMenu.gd (HUD)
├── The World/        Chunk_Manager.cs, Block_Registry.cs, Block_Model.cs
│   └── Generation/   World_Generator.cs (5-stage pipeline, all stages EMPTY — fill these)
├── Entities/         Entity.cs (base), GrappleHook.cs
│   └── Player/       Player.cs, PlayerAbilities.cs, interactions.gd
└── Datasets/         Block_Registry.cs, Item_Registry.cs (ARCHIVED)
```

### Key architectural decisions

**Dual velocity channels (Player.cs / PlayerAbilities.cs)**
- `_inputVel` — WASD + gravity + jump. Friction and speed cap applied here only.
- `_abilityVel` — dash, grapple, jackhammer impulse. Decays independently (0.97/tick air, 0.85/tick ground for XZ; always 0.97 for Y). No hard cap.
- `Velocity = _inputVel + _abilityVel` recomputed twice per tick: once in `ApplyMovement`, once after `ProcessAbilities` so ability impulses are visible to collision that same frame.
- On block collision any axis: both channel components zeroed for that axis.

**PhysicallyOnFloor() vs OnFloor()**
- `PhysicallyOnFloor()` — pure block check, no grace period. Used for friction, speed cap, ability XZ decay.
- `OnFloor()` — includes coyote time (0.2s grace). Used only for jump eligibility.
- Ground friction and ability decay never fire during coyote window.

**Abilities (PlayerAbilities.cs — partial class of Player)**
All four abilities consolidated in one file. Public state flags (JackhammerCharging, LaserActive, GrappleState, DashCooldown) are the accessory hook-in points — don't add new input systems, read these instead.

**GrappleHook.cs**
Standalone Node3D. Flies in `FireDirection` each `_Process` tick. Block detection via `get_block`. Entity detection via Area3D BodyEntered. State machine: Flying → Retracting → Done. Fires `OnAttach(Vector3)` or `OnRetracted()` callbacks. Has `StartRetract()` for early recall.

**Entity.cs base**
Manual AABB collision against voxel data. Do NOT switch to Godot physics for entity-world collision. All entities extend this.

---

## What's Implemented

### World & Rendering
- 16×16×16 chunk system, threaded generation + mesh building
- Greedy face culling, sphere/cylinder render distance, chunk eviction
- Block damage overlay (MultiMesh + shader), up to 1500 simultaneously damaged blocks
- **Explosion system** (`explode()` in Chunk_Manager) — directly used for combat

### Player Movement
- WASD, mouse-look FPS camera, sprint, spectator mode (V)
- Dual velocity channel physics (see above)
- `PhysicallyOnFloor()` / `OnFloor()` split for correct coyote behavior
- Air velocity carries properly — no mid-air hard cap

### Player Abilities
- **Jackhammer** (`attack1` hold/release) — charges over 1.5s, bounces player in opposite of camera look direction on release. Damages targeted block proportional to charge. Writes to `_abilityVel`.
- **Laser** (`attack2`) — 1s persistent beam, 10s cooldown. Raycast on entity layer. `LaserActive`, `LaserTimer`, `LaserCooldown` public for VFX.
- **Grapple** (`grapple_send`) — hook projectile travels at 150 u/s, max 120 units. Attaches to blocks or entities. While attached: acceleration-based pull (90 u/s²). Release: lunge at 60 u/s. Re-press while hook is out: despawn + refire. Release before attach: immediate retract. All writes to `_abilityVel`.
- **Dash** (`dash`) — horizontal burst (22 u/s) in direction of currently-held keys only. Falls back to camera forward if no key held. 1s cooldown.

### Air Jump System
- 1 air jump granted when leaving ground (`_wasPhysOnFloor && !isPhysOnFloor`)
- +1 on grapple hook attach
- +1 on grapple lunge release
- Reset to 0 on landing
- Air jump uses `IsActionJustPressed`, clears `_abilityVel.Y` so it isn't fought

### Blocks & Entities
- 3 block types: Grass, Dirt, Stone
- Entity base with health, AABB physics, landing/collision callbacks
- Creature.cs — placeholder chase AI
- Explosion system wired to E key in interactions.gd

### Archived (do not restore)
- Minecraft inventory (36-slot), item registry, item behaviors, placeable/consumable/tool system, world-dropped items. All wrapped in `/* */` or `#`. See CLAUDE.md for file list.

---

## What's Not Started Yet

| System | Notes |
|---|---|
| World generation | `World_Generator.cs` 5-stage pipeline is empty. Chunk_Manager uses raw FastNoise2D directly. |
| Enemy AI | `Creature.cs` only chases. No attack, no spawning system, no variety. |
| Combat | No damage between player and enemies. No knockback. No player health UI. |
| Run structure | No planet select, no upgrade screen, no boss trigger. |
| Accessories | All 10 defined in NEW_VISION.md. None implemented. |
| VFX | No laser beam, no grapple rope/line, no dash trail, no block break particles. |
| Sound | Nothing. |
| GrappleHook scene | User needs to create `Assets/GrappleHook.tscn` (see CLAUDE.md for structure). |
| World save/load | Explicitly removed. Roguelike — no persistence between runs. |

---

## Notable Code Locations

| Thing | File |
|---|---|
| Dual velocity channels, movement | `Scripts/Entities/Player/Player.cs` → `ApplyMovement` |
| All 4 player abilities | `Scripts/Entities/Player/PlayerAbilities.cs` |
| Grapple hook projectile | `Scripts/Entities/GrappleHook.cs` |
| Air jump state | `Player.cs` → `_airJumps`, `_wasPhysOnFloor` |
| Chunk generation & mesh | `Scripts/The World/Chunk_Manager.cs` |
| World generator pipeline | `Scripts/The World/Generation/World_Generator.cs` |
| Block registry | `Scripts/Datasets/Block_Registry.cs` |
| Explosion | `Chunk_Manager.cs` → `explode()` |
| Global constants + abyss layers | `Scripts/Handlers/Global.cs` |
| Entity base physics | `Scripts/Entities/Entity.cs` |
