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
├── Entities/         Entity.cs (base), GrappleHook.cs, Creature.cs
│   └── Player/       Player.cs, PlayerAbilities.cs, interactions.gd
└── Datasets/         Block_Registry.cs, Item_Registry.cs (ARCHIVED)
```

### Key architectural decisions

**Single velocity system (Player.cs)**
All movement and ability impulses write directly to `Velocity`. No split channels.
- Ground: `GroundFriction` (currently 0 = instant stop) applied every tick
- Air + keys held: `AirFriction` (0.91) applied — delta-time corrected via `Mathf.Pow(friction, dt * 60f)`
- Air + no keys: no friction — ability momentum (grapple, dash, jackhammer) carries freely
- Quake-style steering: friction only skipped when already exceeding `inputSpeed` in the input direction; acceleration capped so WASD alone never exceeds speed limit
- All vertical (gravity, jump, ability Y) lives in `Velocity.Y` — no separate decay channel

**PhysicallyOnFloor() vs OnFloor()**
- `PhysicallyOnFloor()` — pure block check, no grace period. Used for friction.
- `OnFloor()` — includes coyote time (0.2s grace). Used only for jump eligibility.

**Abilities (PlayerAbilities.cs — partial class of Player)**
All four abilities consolidated in one file. Public state flags (`JackhammerCharging`, `LaserActive`, `CurrentGrappleState`, `DashCooldown`) are accessory hook-in points. Abilities write directly to `Velocity`.

**GrappleHook.cs**
Standalone Node3D. Fires `OnAttach(Vector3)` for block hits, `OnAttachEntity(Entity)` for entity hits, `OnRetracted()` callbacks. State: Flying → Retracting → Done. `StartRetract()` for early recall. GlobalPosition must be set AFTER AddChild.

**Entity.cs base**
Manual AABB collision against voxel data. `heavy` bool on every entity — used by grapple to decide pull direction. All entities extend this.

**Chunk_Manager damage system**
`damage_block(pos, 0–1)` accumulates damage, `break_block(pos)` instant removal, `damage_check(pos, damage)` — checks remaining health and breaks immediately if the hit would be lethal (bypasses multi-frame accumulation).

---

## What's Implemented

### World & Rendering
- 16×16×16 chunk system, threaded generation + mesh building
- Greedy face culling, sphere/cylinder render distance, chunk eviction
- Block damage overlay (MultiMesh + shader), up to 1500 simultaneously damaged blocks
- Explosion system (`explode()` in Chunk_Manager) — damage = 1 required to instant-kill center block
- `damage_check()` — instant break when accumulated damage would be lethal

### Player Movement
- WASD, mouse-look FPS camera, sprint, spectator mode (V)
- Single velocity system with Quake-style directional air movement
- Delta-time correct friction via `Mathf.Pow`
- Air friction only applies when pressing movement keys AND slower than input speed in that direction
- Ability momentum (grapple/dash) carries freely through open air, no friction unless steering
- `PhysicallyOnFloor()` / `OnFloor()` split for correct coyote behavior

### Player Abilities
- **Jackhammer** (`attack1` hold/release) — charges over 0.5s, bounces player opposite camera look. Explosion at targeted block scaled by charge (`damage_block` + `explode`). Damage >= 0.2 charge required to trigger. Writes XZ to `Velocity`, Y to `Velocity.Y`.
- **Laser** (`attack2`) — 1s persistent beam, 10s cooldown. Raycast on entity layer.
- **Grapple** (`grapple_send`) — hook at 300 u/s, max 220 units. Attaches to blocks OR entities. 0.1s cooldown between fires.
  - *Block*: Quake-style pull (72 u/s accel, 50 u/s cap). Release = lunge at 50 u/s (Quake-capped, won't slow you if already faster).
  - *Heavy entity*: toggle-latch — stays attached until re-press or block crosses the line. Player pulled at 35 u/s. Arrival boosts player up. Line-of-sight blocked = auto-cancel.
  - *Light entity*: player gets Y boost on attach; entity reeled toward player at 35 u/s. Release = thrown at reel velocity + upward boost.
  - Jump while attached to entity = breaks grapple and uses air jump to launch away.
  - Jackhammer hit on the grappled entity = ungrapple (knockback not overridden).
  - Enemy soft-aim: cone dot > 0.96, LOS ray march, blocks selection through walls.
  - Rope: cylinder mesh in SubViewport using tentacle material + layer 32768.
  - Arm tracks grapple target in 3D (LookAt in SubViewport space).
- **Dash** (`dash`) — horizontal burst in held-key direction, fallback to camera forward. 1s cooldown.

### Speed Threshold System
Above 30 u/s, spherical radius-2.5 check around the player each tick:
- Any block in radius → `damage_check(pos, excessRatio * rate * delta)` — breaks it immediately if lethal
- Drag (`SpeedPenaltyDecay = 0.8`) applied only if a block was actually broken (not just chipped)
- Outer ring blocks chip but don't trigger drag, allowing terrain to crumble at range

### Air Jump System
- Max 1 air jump at all times
- Granted when leaving ground, on grapple attach, on grapple lunge release
- Reset to 0 on landing

### Blocks & Entities
- Stone-only generation (temp — full palette wired once World_Generator pipeline is built)
- `Entity.cs` base: health, AABB physics, `heavy` bool, `Grappled` bool (suppresses movement during reel)
- `Enemy.cs` (extends Entity): `AttackDamage`, `DetectionRange`, `Flying`, procedural world-space health bar (green→red, camera-facing billboard)
- `Creature.cs` (extends Enemy): 3D flying chase AI, accelerates toward player up to `ChaseSpeed`, respects `MaxFallSpeed` when not flying
- Explosion system wired to E key in interactions.gd

### Archived (do not restore)
- Minecraft inventory (36-slot), item registry, item behaviors, placeable/consumable/tool system, world-dropped items. See CLAUDE.md for file list.

---

## What's Not Started Yet

| System | Notes |
|---|---|
| World generation | `World_Generator.cs` 5-stage pipeline is empty. Chunk_Manager uses raw FastNoise2D directly. |
| Enemy AI | `Creature.cs` chases + can be grappled/killed. No attack on contact, no spawning system, no variety. |
| Combat | Enemies take damage and die. Player deals damage via jackhammer/laser/grapple. No player health UI yet. |
| Run structure | No planet select, no upgrade screen, no boss trigger. |
| Accessories | All 10 defined in NEW_VISION.md. None implemented. |
| VFX | No laser beam, no dash trail, no block break particles. Grapple rope ✅ done. |
| Sound | Nothing. |
| World save/load | Explicitly removed. Roguelike — no persistence between runs. |

---

## Notable Code Locations

| Thing | File |
|---|---|
| Movement (single velocity, Quake air) | `Scripts/Entities/Player/Player.cs` → `ApplyMovement` |
| All 4 player abilities + speed threshold | `Scripts/Entities/Player/PlayerAbilities.cs` |
| Grapple hook projectile + entity detection | `Scripts/Entities/GrappleHook.cs` |
| Air jump state | `Player.cs` → `_airJumps`, `_wasPhysOnFloor` |
| Chunk generation & mesh | `Scripts/The World/Chunk_Manager.cs` |
| damage_check (instant-break if lethal) | `Chunk_Manager.cs` → `damage_check()` |
| World generator pipeline | `Scripts/The World/Generation/World_Generator.cs` |
| Block registry | `Scripts/Datasets/Block_Registry.cs` |
| Explosion | `Chunk_Manager.cs` → `explode()` |
| Global constants + friction values | `Scripts/Handlers/Global.cs` |
| Entity base physics | `Scripts/Entities/Entity.cs` |
| Enemy base class (health bar, stats) | `Scripts/Entities/Enemy.cs` |
| Creature AI + `heavy` flag | `Scripts/Entities/Creature.cs` |
| Enemy soft-aim + LOS check | `Player.cs` → `UpdateEnemySelection`, `HasBlockLOS` |
