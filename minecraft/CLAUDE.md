# Antithesis Conquering Simulator — Claude Context

> **Starting a new session?** See `documents/project/STARTUP.md`.
> **Ending the session / handing off the project?** See `documents/project/HANDOFF.md` and follow its instructions.
> All other project documentation lives under `documents/` — see `documents/README.md` for the index.

## MCP server — use it proactively

The `godot-ai` MCP server is always available when Godot is open. Use it without being asked whenever it would give better results than guessing:

- **Before touching a scene file** — call `scene_get_hierarchy` / `node_get_properties` to read live state instead of assuming from the `.tscn` text.
- **After any visual change** — call `editor_screenshot` to confirm it looks right.
- **When iterating on transforms, materials, or export values** — use `node_set_property` directly instead of editing the `.tscn` file by hand.
- **When the user reports a visual bug** — screenshot first, then diagnose.
- **When building new scene structure** — use `node_create` + `script_attach` instead of writing raw `.tscn` text.
- **When checking logs after a crash or error** — call `logs_read` instead of asking the user to paste output.

---

## What this project is

A voxel-based action roguelike built in **Godot 4** (C# + GDScript). NOT a Minecraft clone. Game loop: choose a planet → fight → collect upgrades → next planet → boss. All combat, no crafting, no inventory. See `documents/design/NEW_VISION.md` for full design doc, `documents/project/PROGRESS.md` for current state, `documents/project/TODO.md` for next steps.

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
Assets/               character.tscn, creature.tscn, GrappleHook.tscn
                      left_arm.tscn, right_arm.tscn
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

### Single velocity system

`Player.cs` operates on a single `Velocity` vector. There are no separate `_inputVel` / `_abilityVel` channels.

- **Ground**: `Global.GroundFriction` applied every tick (currently 0 = instant stop, rebuilt from input)
- **Air + keys held**: `Global.AirFriction` applied, but **only** if current speed in the input direction is below `inputSpeed` — ability momentum is preserved when steering into it
- **Air + no keys**: no friction at all — grapple/dash/jackhammer momentum carries freely
- All friction is delta-time correct: `Mathf.Pow(friction, delta * 60f)`
- **Quake-style acceleration**: only adds velocity up to `inputSpeed` in the input direction; WASD can never push past the speed cap, but existing momentum above it is never removed
- All vertical (gravity, jump, ability Y impulses) lives directly in `Velocity.Y` — no separate decay channel

On block collision any axis: `Velocity` component zeroed for that axis in `CheckWorldCollisionsWithStepUp`.

Abilities write directly to `Velocity`. Never introduce a separate accumulation channel.

### PhysicallyOnFloor() vs OnFloor()

- `PhysicallyOnFloor()` — actual block contact, no grace period. Used for: friction.
- `OnFloor()` — includes 0.2s coyote grace. Used ONLY for: jump eligibility check.
- Never use `OnFloor()` for friction — it causes ground friction to fire mid-air during coyote window.

### Air jump system

Declared in `Player.cs`: `_airJumps`, `_wasPhysOnFloor`. **Max 1 at all times.**

- Leave ground → `_airJumps = max(existing, 1)`
- Grapple hook attaches or lunge releases → `_airJumps = 1`
- Land → `_airJumps = 0`
- Air jump input: `IsActionJustPressed("jump")` while `!isOnFloor` and `_airJumps > 0`

### PlayerAbilities.cs

Partial class of Player — shares all private fields. All abilities here:

| Ability | Input action | Key state |
|---|---|---|
| Jackhammer | `attack1` hold/release | `JackhammerCharging`, `JackhammerCharge` |
| Laser | `attack2` press — mass destruction beam: obliterates terrain, high entity DPS, blasts player backward for momentum | `LaserActive`, `LaserTimer`, `LaserCooldown` |
| Grapple | `grapple_send` | `CurrentGrappleState` (Idle/Sent/Attached), `GrappleAnchor` |
| Dash | `dash` press | `DashCooldown` |

Public state properties are the **accessory hook-in points**. Accessories read these flags rather than adding their own input handling.

Grapple states:
- `Idle` → press fires hook
- `Sent` → hook in flight; release = immediate retract; re-press = despawn + refire
- `Attached` (block or heavy entity) → Quake-style pull toward anchor (40 u/s cap); release = lunge at 60 u/s
- `Attached` (light entity) → entity pulled toward player at 20 u/s; release = throw entity at reel velocity

Also in PlayerAbilities: **speed threshold** — above 30 u/s, spherical radius-2.5 scan around player breaks blocks via `damage_check`. Drag (`SpeedPenaltyDecay`) only fires when a block actually broke.

### GrappleHook.cs

Standalone `Node3D`. Does NOT extend Entity. `GlobalPosition` set AFTER `AddChild` (Godot requirement). State machine: `Flying → Retracting → Done`. Block detection via `get_block()`. Entity detection via `Area3D` `BodyEntered`.

Two attach callbacks:
- `OnAttach(Vector3 worldPos)` — fired for block hits
- `OnAttachEntity(Entity entity)` — fired for entity hits (non-player bodies)

`StartRetract()` for immediate recall.

GrappleHook.tscn structure (already created — assign `GrappleHookScene` export on Player in Inspector if missing):
```
Node3D  (name: GrappleHook, script: GrappleHook.cs)
├── MeshInstance3D  (name: Mesh)
└── Area3D  (name: HitArea, Collision Layer: 0, Mask: Layer 2)
    └── CollisionShape3D  (SphereShape3D, radius 0.2)
```

### Entity.cs

Base class for all entities. Manual AABB collision against voxel blocks. Do NOT use Godot physics engine for entity-world collision. `TakeDamage(int amount, Vector3 knockback)` overload exists for combat. Override `OnLandedOnBlock` and `OnBlockCollision` for custom behavior.

`heavy` (bool, default false) — controls grapple behaviour. Heavy = player pulled toward entity. Light = entity pulled toward player and thrown on release. Set on creature prefabs/exports.

### Enemy.cs — LOD standard (required for every AI-driven enemy)

Root cause history: raising the spawn cap to 50 enemies dropped fps to 25 identically whether enemies were on/off-screen — confirmed CPU-bound cost from animation/particle/AI work that ran unconditionally per enemy per frame, with no LOD and no pooling. Full writeup, fixes, and the standard below: `documents/performance/ENEMY_PERFORMANCE.md`.

`Enemy.cs` now caches a per-frame player-distance and LOD tier (`Near` / `Mid` / `Far`, thresholds 40u / 80u), computed once in `Enemy.ApplyMovementFromInput` before any subclass logic runs:

- **`DistSqToPlayer`** / **`Lod`** — public, read-only from subclasses. **Never call `(playerPos - GlobalPosition).Length()` / `.DistanceTo()` in an enemy subclass** — use these instead. Squared-distance comparisons (`DistSqToPlayer <= Range * Range`) replace linear-distance checks; only call `Normalized()` when you actually need a unit direction (that sqrt is unavoidable).
- Subclasses that override `ApplyMovementFromInput` **must call `base.ApplyMovementFromInput(delta)` first** so the cache updates — `Creature.cs` does this.
- `Enemy._Process` gates animation (`AnimationPlayer.SpeedScale`) and particles on `Lod` (`GpuParticles3D.Emitting`, or `Set("paused", ...)` for the legacy `UniParticles3D` addon type), and skips the health-bar billboard `LookAt` entirely at `Far`. Property writes are skipped unless the gated value actually changed (`_lastAnimateState` / `_lastEmitState`).
- **Use `GpuParticles3D`, never the `UniParticles3D` addon, for new enemy effects.** Profiling found `UniParticles3D`'s per-particle update is plain GDScript (a script-object loop, not GPU-driven) — it was the dominant cost once animation/AI were LOD-gated, even with emission already Near-tier-only. `Creature.cs`'s ember effect (`Assets/creature.tscn` → `EmberParticles`) was rebuilt as native `GpuParticles3D` for this reason; `Enemy.cs` still recognizes `UniParticles3D` for backward compat but it must not be used going forward.
- **Far-tier AI throttle**: `Creature.cs` re-evaluates state transitions/targeting only every 4th physics frame at `Far` tier, accumulating delta across the skipped frames so accel/lerp stay time-correct on the tick that runs. Velocity integration + world collision (in `Entity._PhysicsProcess`) are untouched by this and still run every frame. This pattern is safe as-is only for flying entities with no per-tick gravity (`Creature` is `Flying`); a ground enemy with manual `vel.Y -= Gravity * dt` needs gravity applied on every tick, not just the decision tick.

Rules for any **new** `Enemy` subclass (see the doc for the full numbered list):
- Extend `Enemy`, not `Entity` directly, for anything AI-driven/hostile — that's where the LOD cache lives.
- Gate every expensive per-frame op (raycasts, `get_block()` loops, physics queries, more than one `Atan2`) behind `Lod` — full simulation at `Near`, approximate or skipped at `Far`.
- No scene-tree search (`FindChildren`, recursive `GetNode`) outside `_Ready()`/`ImHere()` — cache the reference.
- No unconditional Godot property writes per frame — compare against last-applied value first.
- Pool instances (don't `Instantiate()`/`QueueFree()` per spawn) for anything spawning more than a couple times per encounter — not yet built, tracked in `documents/project/TODO.md`.
- Profile at 50 concurrent enemies (temporarily raise `EnemySpawner.MaxEnemies`) before calling a new enemy type done.

---

## Chunk_Manager.cs — do not casually refactor

~1400 lines. Threaded chunk generation. Key methods:

- `explode(center, radius, damage)` — primary combat terrain-destruction. Requires `damage >= 1f` to instant-kill the center block.
- `damage_block(pos, 0–1)` — accumulates damage over frames, breaks when health ≤ 0.
- `damage_check(pos, damage)` — checks remaining health and calls `break_block` immediately if the hit would be lethal. Returns `true` if block broke. Use this when you need same-frame removal.
- `break_block(pos)` — instant removal, updates chunk data immediately so collision checks see air.

The 5-stage world generator pipeline in `World_Generator.cs` is meant to replace the direct FastNoise2D call in Chunk_Manager — that's the next major world system task.

---

## Arm rendering (SubViewport)

Arms render in a SubViewport with its own Camera3D that mirrors the main camera's rotation. Key rules:

- Arm nodes live in `SubViewportContainer/SubViewport/` — their `GlobalPosition` is SubViewport-space, not main-scene world space
- To convert a world-space direction to SubViewport space: `Camera.GlobalTransform.Basis.Inverse() * worldDir`
- Arm tip (`GrappleArmTip`) uses `GlobalPosition` directly — already SubViewport-space
- Rope cylinder is parented to the SubViewport (not main scene). Hook world position in SubViewport space: `svCam.GlobalPosition + Camera.GlobalTransform.Basis.Inverse() * toGrapple.Normalized() * distance`
- Left arm tracking uses `LookingAt` with a virtual target in SubViewport space

---

## Conventions

- New enemy types extend `Enemy.cs` (not `Entity.cs` directly), set `heavy` appropriately, and follow the LOD standard above
- New abilities go in `PlayerAbilities.cs`, write directly to `Velocity`
- New blocks go in `Block_Registry.cs`
- No Minecraft systems (crafting, farming, hunger, sleep, building)
- `Washed Code/` is read-only reference — don't add to it
- `interactions.gd` handles camera, block targeting, explosion trigger — add new input wiring here if needed from GDScript side

---

## Project Identity Documents

Additional design and lore documents live in `documents/design/`. Consult these when making decisions about visual style, character, aesthetic, or the game's broader identity:

- **documents/design/ANTITHESIS.md** — character identity, visual style, sound, and aesthetic principles. Reference for any decision touching how the game looks, sounds, or feels.
- **documents/design/COSMOS_LORE.md** — narrative archive for the Cosmos universe. Reference for THE PLANT boss design, the game's tonal identity, and its relationship to the original Cosmos concept.
- **documents/design/ORIGIN.md** — background context on Cosmos Enterprises and the lineage of this project. Not active design direction, but useful for understanding *why* certain decisions are what they are.

See `documents/README.md` for the full documentation index (engineering specs, performance history, project tracking).
