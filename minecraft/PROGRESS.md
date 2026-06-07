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
- **Jackhammer** (`attack1` press-to-commit) — press once to commit a 0.5s charge; charge runs automatically to full. Holding the button at full charge holds the pose; release fires. Explosion at targeted block (full radius). Damage determined by speed at fire time — 3 tiers: weak (<15 u/s, 20 dmg), medium (15–30 u/s, 50 dmg), hard (>30 u/s, 100 dmg). Player bounced opposite camera look at full impulse. A 0.5s coyote window keeps the effective tier active after speed drops, so grapple/laser momentum can be cashed in even as you decelerate.
- **Laser** (`attack2`) — 1.5s persistent beam of mass destruction, 7s cooldown. Obliterates terrain via rate-limited `explode()` calls, shreds entities with high DPS, and blasts the player backward with continuous knockback — designed to be a chaotic momentum tool as much as a weapon. Red emissive beam VFX in SubViewport space. LaserOutline arm animation: state machine with Extended (poles at 0.65, triangle at 0, slow idle spin) → Spinning (both fully extruded, fast spin) → FoldPoles → FoldTriangle → Retracted → UnfoldPoles → UnfoldTriangle → back to Extended.
- **Grapple** (`grapple_send`) — hook at 300 u/s, max 220 units. Attaches to blocks OR entities. 0.1s cooldown between fires.
  - *Block*: Quake-style pull (72 u/s accel, 50 u/s cap). Release = lunge at 50 u/s (Quake-capped, won't slow you if already faster).
  - *Heavy entity*: toggle-latch — stays attached until re-press or block crosses the line. Player pulled at 35 u/s. Arrival boosts player up. Line-of-sight blocked = auto-cancel.
  - *Light entity*: player gets Y boost on attach; entity reeled toward player at 35 u/s. Release = thrown at reel velocity + upward boost.
  - Jump while attached to entity = breaks grapple and uses air jump to launch away.
  - Jackhammer hit on the grappled entity = ungrapple (knockback not overridden).
  - Enemy soft-aim: cone dot > 0.96, LOS ray march, blocks selection through walls.
  - Rope: dark green emissive cylinder in SubViewport, layer 32768.
  - Hook projectile: dark green emissive box mesh (material set at runtime in GrappleHook._Ready()).
  - Arm tracks grapple target in 3D (LookAt in SubViewport space).
- **Dash** (`dash`) — horizontal burst in held-key direction, fallback to camera forward. 1s cooldown.

### Speed Tier System
Three tiers based on player speed, tracked every frame with a 0.5s descending-only coyote window:
- **Weak** (<15 u/s): jackhammer deals 20 dmg
- **Medium** (15–30 u/s): jackhammer deals 50 dmg
- **Hard** (>30 u/s): jackhammer deals 100 dmg
- `RawSpeedTier` (0/1/2) = actual current tier. `EffectiveSpeedTier` = coyote-aware tier used for damage. Coyote only fires when descending — ascending grants the higher tier instantly.
- Temporary HUD: 3 colored segments (green/yellow/red) below the laser bar. Active tier is bright; others dim. On tier drop, the vacated segment flashes for 0.5s and all others stay dark.

### Speed Threshold Terrain Destruction
Above 30 u/s, spherical radius-2.5 check around the player each tick:
- Any block in radius → `damage_check(pos, excessRatio * rate * delta)` — breaks it immediately if lethal
- Drag (`SpeedPenaltyDecay = 0.8`) applied only if a block was actually broken (not just chipped)
- Outer ring blocks chip but don't trigger drag, allowing terrain to crumble at range

### Camera Shake System
`Global.ShakeCamera(intensity, duration)` — callable from any script. Shake decays linearly over the duration. A louder hit overrides a weaker ongoing shake. Applied per-frame in `Player.RotateCamera()` as random pitch/yaw offset scaled by current intensity.

### Air Jump System
- Max 1 air jump at all times
- Granted when leaving ground, on grapple attach, on grapple lunge release
- Reset to 0 on landing

### Blocks & Entities
- Stone-only generation (temp — full palette wired once World_Generator pipeline is built)
- `Entity.cs` base: health, AABB physics, `heavy` bool, `Grappled` bool (suppresses movement during reel)
- `Enemy.cs` (extends Entity): `AttackDamage`, `DetectionRange`, `Flying`, procedural world-space health bar (green→red, camera-facing billboard, damage flash, hidden at full health). Tracks `EnemyCount` in Global on spawn/death.
- `Creature.cs` (extends Enemy): 3D flying chase AI, accelerates toward player up to `ChaseSpeed`, respects `MaxFallSpeed` when not flying. Deals `AttackDamage` on AABB contact (1s cooldown) with directional knockback.
- `SwarmEnemy.cs` (extends Enemy): fast (12 u/s), small (0.6×0.7), flying, `heavy=false`. Random jitter each 0.4s prevents all swarm members taking identical paths. Short attack cooldown (0.6s). Needs model + scene.
- `HeavyEnemy.cs` (extends Enemy): slow (3.5 u/s), large (1.4×2.2), ground, `heavy=true`. Charge attack (18 u/s burst, 0.4s, 4s cooldown) at range > 12. Auto-jumps 1-block walls via `OnBlockCollision`. Needs model + scene.
- `RangedEnemy.cs` (extends Enemy): medium (4.5 u/s), ground, `heavy=false`. Maintains 20-unit ideal range, strafes perpendicular to player. Fires `EnemyBolt` every 2.5s when in LOS. LOS via block ray march. Auto-jumps walls. Needs model + scene.
- `EnemyBolt.cs` (extends Projectile): orange emissive box, slight arc (gravity 4), 4s lifetime. Damages player on contact with directional knockback.
- Explosion system wired to E key in interactions.gd
- `PlayerHUD.cs`: jump indicator, enemy soft-aim indicator, crosshair color, player health bar (red, bottom-left), laser charge bar (blue when ready/firing, gray while recharging), speed tier indicator (3 segments, temp), red full-screen flash on player hit (fades over 0.4s)

### Archived (do not restore)
- Minecraft inventory (36-slot), item registry, item behaviors, placeable/consumable/tool system, world-dropped items. See CLAUDE.md for file list.

---

## What's Not Started Yet

| System | Notes |
|---|---|
| World generation | `World_Generator.cs` 5-stage pipeline is empty. Chunk_Manager uses raw FastNoise2D directly. |
| Enemy AI | 3 enemy type skeletons (Swarm/Heavy/Ranged) coded, waiting on models. EnemySpawner active. A* pathfinding not yet implemented — ground enemies auto-jump 1-block walls for now. |
| Combat | Enemies take damage and die. Player deals damage via jackhammer/laser/grapple. No player health UI yet. |
| Run structure | No planet select, no upgrade screen, no boss trigger. |
| Accessories | All 10 defined in NEW_VISION.md. None implemented. |
| VFX | Laser beam ✅ done. No dash trail, no block break particles. Grapple rope ✅ done. |
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
