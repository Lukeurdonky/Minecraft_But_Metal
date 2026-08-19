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

## Working discipline — read this before doing anything

These docs describe what exists. They don't by themselves limit what you're
allowed to decide on your own — assume narrow scope unless told otherwise.

- Implement exactly what was asked. Do not add error handling, polish, extra
  UI, alternate paths, or generalized abstractions that weren't requested,
  even if they seem obviously good.
- If a task leaves a design/UX/architecture question unanswered, don't pick
  "the sensible default" — stop and ask. Silence in the instructions is not
  a green light to improvise.
- Never restructure, rename, or clean up code you weren't asked to touch,
  even inside a file you're already editing for something else.
- Live scene/editor mutations via godot-ai (node_create, script_attach,
  node_set_property, project_manage, autoload_manage) apply immediately
  with no diff to review. For anything beyond a single obvious value tweak,
  state what you're about to do and wait for a go-ahead before calling it.
- Placeholder / "not yet decided" markers in these docs (plain UI text,
  curvature parked at 0, SHELVED/superseded systems) are load-bearing.
  Don't "helpfully" finish, polish, or clean them up.
- For anything bigger than one clearly-scoped change: state a short plan
  first — what gets touched, and just as importantly what doesn't — before
  writing or mutating anything.

---

## What this project is

A voxel-based action roguelike built in **Godot 4** (C# + GDScript). NOT a Minecraft clone. Game loop: choose a planet → fight → collect upgrades → next planet → boss. All combat, no crafting, no inventory. See `documents/design/NEW_VISION.md` for full design doc, `documents/project/PROGRESS.md` for current state, `documents/project/TODO.md` for next steps, and `documents/README.md` for the full documentation index (design, engineering specs, performance history, project tracking).

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
├── Handlers/         Global.cs (autoload, world/run state), RunManager.cs (autoload, run flow — see "Run flow" below),
│                     AtmosphereSystem.cs (biome → WorldEnvironment fog/light), MainMenu.gd,
│                     LoadingScreen.gd (travel animation + accessory pick), PlanetSelect.gd (SHELVED),
│                     UpgradeSelect.gd (superseded), PlanetConfigMenu.gd (F3 debug menu), DebugMenu.gd, level.gd,
│                     StructureBuilder.gd + BuilderCamera.gd (dev tool — see "Structure builder" below),
│                     Ship.gd (between-runs hub — see "Ship hub" below)
├── The World/        Chunk_Manager.cs (~1400 lines, terrain/damage — see below), Chunk.cs, Block_Definition.cs,
│                     Block_Model.cs, BiomeDescriptor.cs, PlanetParams.cs, Structure.cs
│   └── Generation/   World_Generator.cs  ← 5-stage pipeline, ALL STAGES EMPTY (cave/abyss carving still lives
│                     directly in Chunk_Manager.create_chunk_data — see documents/engineering/generation_plan.md),
│                     Simplex4D.cs (4D simplex noise for seamless torus wrapping)
├── Entities/         Entity.cs (base, manual AABB collision), Enemy.cs (LOD-cached AI base — see below),
│                     Creature.cs, GrappleHook.cs, Projectile.cs, EnemySpawner.cs, EnemyBullet.cs, EnemyBolt.cs,
│                     GroundRobotShooter.cs, HeavyEnemy.cs/RangedEnemy.cs/SwarmEnemy.cs (scripted, no model/scene yet)
│   └── Player/       Player.cs, PlayerAbilities.cs, PlayerAccessories.cs, PlayerHUD.cs,
│                     interactions.gd (camera/block targeting), inventory.gd (ARCHIVED)
│       └── Accessories/  Accessory.cs (base class, lifecycle hooks) + one file per accessory — see "Accessories" below
└── Datasets/         Block_Registry.cs, Biome_Registry.cs, Accessory_Registry.cs + AccessoryDescriptor.cs,
                      Structure_Registry.cs, Item_Registry.cs (ARCHIVED stub),
                      Mob_Registry.cs (unused, TODO: repurpose or remove)
Structures/           Authored structures (.tres). Written by the structure builder, read by Structure_Registry.
Assets/               character.tscn, creature.tscn, GrappleHook.tscn, ground_robot_shooter.tscn,
                      left_arm.tscn, right_arm.tscn
Scenes/               MainMenu.tscn (run/main_scene) → CubeLand.tscn (gameplay) → LoadingScreen.tscn
                      (travel animation + post-stage-clear accessory pick) → CubeLand.tscn ...
                      PlanetSelect.tscn (SHELVED) and UpgradeSelect.tscn (superseded) are unreachable
Materials/            Shaders (Fire.gdshader, BlockDamage.gdshader, Select.gdshader) + .tres materials
                      (LaserMaterial, GrappleMaterial, explosion, Debris)
documents/            All project docs except this file — see "Project Identity Documents" below and
                      documents/README.md for the full index
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

**Imported clips do not loop by default.** `Creature.cs` re-`Play()`s on `AnimationFinished` to fake
it; `MossCreature.cs` instead sets `animation.LoopMode = Linear` once in `ImHere()`, which is the
better pattern for a single always-on clip. `MossCreature` also pins `animation.Length` (1.94s, the
clip's own tail loops late) — Godot's scene importer can set an animation's loop mode but **not** its
length, so a Blender re-export would silently restore the late loop if this lived in the `.import`.
Mutating the imported `Animation` is shared across every instance of that scene, which is what you
want here and is idempotent.

Rules for any **new** `Enemy` subclass (see the doc for the full numbered list):
- Extend `Enemy`, not `Entity` directly, for anything AI-driven/hostile — that's where the LOD cache lives.
- Gate every expensive per-frame op (raycasts, `get_block()` loops, physics queries, more than one `Atan2`) behind `Lod` — full simulation at `Near`, approximate or skipped at `Far`.
- No scene-tree search (`FindChildren`, recursive `GetNode`) outside `_Ready()`/`ImHere()` — cache the reference.
- No unconditional Godot property writes per frame — compare against last-applied value first.
- Pool instances (don't `Instantiate()`/`QueueFree()` per spawn) for anything spawning more than a couple times per encounter — not yet built, tracked in `documents/project/TODO.md`.
- Profile at 50 concurrent enemies (temporarily raise `EnemySpawner.MaxEnemies`) before calling a new enemy type done.

---

## Run flow — MainMenu → CubeLand → LoadingScreen → CubeLand

`RunManager.cs` (autoload, registered right after `Global` in `project.godot`) drives the demo's linear **3-planet-stage + placeholder boss** loop. `MainMenu.tscn` is `run/main_scene` — the game no longer boots directly into `CubeLand.tscn`.

- **Planets are chosen randomly; the player never picks one.** `GoToRandomPlanet()` is the single entry point into a planet: rolls a biome not yet seen this run → `Global.Instance.ApplyPlanetParams(descriptor.MakePlanetParams(seed))` → `ChangeSceneToFile(CubeLand.tscn)`. Same `ApplyPlanetParams` tail the F3 debug menu (`PlanetConfigMenu.gd` → `Global.SetPlanetConfig`) already used — don't duplicate the reset logic (`WorldSpawn`/`KillCount`/`RunTimer`) anywhere else.
- `MainMenu.gd`'s "New Run" calls `StartNewRun()` and **does not change scene itself** — `StartNewRun()` owns the transition via `GoToRandomPlanet()`. Same for `LoadingScreen.gd`'s accessory pick → `ChooseAccessory()`. Adding a `change_scene_to_file` alongside either call races the one already queued.
- `Scenes/LoadingScreen.tscn` is the only between-planets screen: a looping 2-frame `AnimatedSprite2D` (`Sprites/loading frame 1|2.png`, `travel` animation) behind the pick-1-of-3 accessory list, plus a hidden `Complete` panel it swaps to when `RunComplete`. It is **not** tied to real chunk generation — CubeLand still builds its own terrain after the scene change.
- **Player-facing planet selection is SHELVED, not deleted.** `PlanetSelect.tscn`/`.gd`, `RunManager.CurrentOptions`, `GetOptionsForUI()`, `ChooseOption()` and `GenerateOptionsForStage()` are all still present and correct, just unreachable. Re-enabling = point `StartNewRun`/`ChooseAccessory` back at `PlanetSelect.tscn`. Keep `CurrentOptions` a generic `List<StageOption>` and keep `CompleteStage()` the only method that assumes "next stage = index + 1" — that's still the seam for a real branching node-graph map.
- `Scenes/UpgradeSelect.tscn` + `UpgradeSelect.gd` are **superseded** by LoadingScreen and orphaned. Safe to delete.
- Stage-clear is currently a placeholder: `RunManager._Process` polls `Global.KillCount` against a per-stage constant. There is no `PlanetDescriptor` yet, so `DifficultyLabels` is cosmetic and currently unused.
- `Player.Die()`'s jump-to-restart calls `RunManager.Instance.EndRun()` (resets stage index, `RunComplete`, used-biomes, `Global.EquippedAccessoryIds`) and goes to `MainMenu.tscn` — dying forfeits the whole run, not just the current planet. `LoadingScreen.gd`'s win-path "Return to Main Menu" button calls `EndRun()` too.
- **Editing `project.godot` while the editor is open:** use the godot-ai MCP `project_manage(op="settings_set")` / `autoload_manage` ops, not a raw file edit — the editor's live in-memory settings will silently re-clobber a manual text edit (`run/main_scene`, `[autoload]`) the next time any MCP call re-serializes the file.

## Warping out — how a node is actually left

Reaching a node's kill target does **not** end the stage. `RunManager` runs a three-step exit and
`CompleteStage()` fires only at the end of it:

1. `KillCount >= _stageKillTarget` → `WarpReady` latches true. Nothing else happens; you keep
   playing a cleared planet. Once latched the kill count stops mattering.
2. A **warp point falls out of the sky and lands** near the player (below). Walking up to its
   console and pressing `interact` (**E**) → `StartWarp()` → `WarpCharging`, with
   `WarpRemaining` counting down from `WarpChargeSeconds` (10).
3. It hits zero → `CompleteStage()`.

- **The clock keeps draining through both steps**, so deciding and charging cost real time. That
  is the point — the exit is a choice with a price, not a cutscene.
- `StartWarp()` is public so a UI button can drive the same sequence as the key.
- **`CompleteStage()` goes to `SolarMap.tscn`, not `LoadingScreen.tscn`** — the map is where you
  see what the hop cost and launch again. The single exception is `RunComplete` (sun cleared),
  which still routes to `LoadingScreen` because that scene owns the end-of-run panel and the map
  has no "you won" state. The accessory pick is currently **not** in the loop; options are still
  generated, so re-inserting a pick screen is a routing change only.
- `ResetWarpState()` is called from `ResetRunState()`, `LaunchCurrentNode()` and `CompleteStage()`.
  Launch is the one that matters — every node must start un-cleared, including a re-entered one.

### The warp point — Scripts/Handlers/WarpPoint.gd

**The exit is a place, not a keypress.** `RunManager` owns the countdown and the stage
transition; `WarpPoint.gd` (a node under `CubeLand.tscn`'s `Game`) owns the object in the
world and is the only thing that calls `StartWarp()`.

- **It's the `"Warp Point"` structure**, authored in the builder like the ship. Nothing in
  code knows its shape — rebuild the pillar in the builder and press Save.
- **You interact with any block of it**, not with a marker console. Deliberately *not* the
  ship's Marker1 pattern: markers are skipped at stamp time, so on a solid pillar one would
  leave a one-block hole in the middle of the thing. `_tick_landed` tests the crosshair's
  target block (`Global.HasSelectedBlock()` / `GetSelectedBlock()`, wrapping the DDA
  targeting `interactions.gd` already runs) against the stamped box from
  `Structure_Registry.GetStampBounds`. That reuses one raycast instead of adding a second,
  and inherits its 5-block reach, so there's no separate interaction radius to keep in step.
- **Name gotcha:** `CaptureAndSave` maps every non-alphanumeric character to `_`, so typing
  "Warp Point" in the builder files it as `Warp_Point.tres` under the registry key
  `Warp_Point`. `_resolve_structure()` tries both spellings so neither is a silent miss.
- **Landing site** is `land_distance` (20) ahead of where the **camera** is looking, so the
  fall happens on screen, then straight down to the first solid block. The scan starts from
  the *player's own altitude*, not from the sky: on a Cave planet the real surface is far
  overhead and a warp point up there is unreachable, so it punches down through the cave
  roof instead. `is_chunk_ready` gates the scan — `get_block` returns 0 for "air" and "no
  chunk" alike and would happily land it on nothing.
- **Voxels can't move, so the falling thing is a proxy mesh.** It's freed on impact and the
  real structure is stamped in its place on the same frame. Impact order is
  **explode, then stamp** — the crater makes room for the pillar; the other way round would
  blow up what was just placed. Stamped with `clearAir = true` so an interior doesn't fill
  with the hillside it landed in.
- **The see-through cage** around the cleared volume is a translucent box plus 12 edge lines
  (`ImmediateMesh`, `PRIMITIVE_LINES` — `StandardMaterial3D` has no per-object wireframe
  mode). The fill is depth-tested normally so terrain occludes it; the **edges set
  `no_depth_test`** so the warp point stays findable once you've wandered off behind a hill.
- **A missing structure re-arms the J key.** This is the one authoring mistake in the
  system that can strand a run — with no warp point there's no way off the
  planet — so `_fail()` is loud on screen *and* calls
  `RunManager.ReportWarpPointUnavailable()`. `RunManager._Process` only accepts `start_warp`
  when the phase is `Missing` or `None`, so the key is dead on the normal path.
- **Phase constants never cross to GDScript.** `WarpPointInbound`/`Landed`/`Missing` and
  `WarpKeyName` are C# `const`s, and statics never appear in the per-instance member switch
  Godot generates — `RunManager.WarpPointInbound` from a `.gd` reads as null and silently
  sets phase 0. Hence `ReportWarpPointInbound()` / `ReportWarpPointLanded()` /
  `ReportWarpPointUnavailable()` / `GetWarpKeyName()`: name the event, not the value.
- **`interactions.gd` no longer explodes on raw keycode 69 (E).** That second trigger sat on
  the same key as `interact`, which was harmless while the only interactable was in the
  indestructible ship — but on a destructible planet every warp interaction also blew a
  crater. `explode` (**F**) still covers it.

### The two run meters

`PlayerHUD` drives them from `RunManager`, both in `RunUI` on `character.tscn`:

- **`RunUI/Label` → `TimerLabel`** — the system clock, counting **down** (`ClockRemaining`). This is
  the "time before it happens" meter, not a stopwatch.
- **`RunUI/Label2` → `KillLabel`** — kills **remaining** (`GetKillsRemaining()`), then `AREA CLEAR`.
- **`RunUI/WarpLabel`** — hidden until `WarpReady`, then the warp point's status
  (`WARP POINT INBOUND` → `WARP POINT STANDING BY`), then the countdown. This is the
  run-wide status line only; the "press E" prompt belongs to `WarpPoint.gd`, which is the
  only thing that knows you're standing next to the console.

Both meters fall back to the old per-planet readouts (`Global.RunTimer` / `KillCount`) when
`IsStageActive()` is false, because CubeLand is still reachable without a run (F6, the F3 debug
menu) and a clock frozen at 00:00 there reads as a bug rather than as "no run".

Text and layout here are deliberately plain placeholders — the treatment is the user's call.

## Danger level — the game-wide threat scale

**1–10, then PLANT above it.** Danger is a property of the *thing*, not of the screen showing
it, so a solar system, a planet and (later) an enemy all report on one scale and a "danger 4"
means the same everywhere. Constants live on `SolarSystemDescriptor`: `MinDanger` (1),
`MaxDanger` (10), `PlantDanger` (11).

- **`PlantDanger` is reserved and never generated.** THE PLANT is the only thing that will ever
  carry it. Nothing renders it specially yet — that's deliberate, not an oversight.
- **All three SolarSelect tiers are `DangerLevel = 1`.** Danger is its own axis, not a
  restatement of the tier: Hard is a *longer* system (18 planets, more warpstations, a bigger
  clock), not currently a nastier one. Change it in `SolarSystemDescriptor.Tiers`, never in UI.
- **The Danger Meter reads `danger` *and* `danger_max` off the offer dictionary**
  (`RunManager.GetOffersForUI`), so it never learns the number 10 — widening the scale is a data
  change with no UI edit. `SolarSelect.tscn` owns the container (`Info/*Info/Danger` — Title,
  Meter, Readout); `SolarSelect.gd` builds only the segments, whose count is data. Same
  container-in-scene / leaves-in-code split as `PlayerHUD`'s `RunUI/AccessoryRow`.
- Fill colour bands Low/Moderate/Severe on the same thirds as
  `SolarSystemDescriptor.LabelFor`, reusing this screen's existing green and amber.

## Accessories — PlayerAccessories.cs + Accessory.cs

Full design list and win-condition tie-in: `documents/design/NEW_VISION.md`. Live implementation status per accessory: the "Accessories" section of `documents/project/TODO.md`.

- `Scripts/Entities/Player/Accessories/Accessory.cs` — abstract base with `OnEquip`/`OnUnequip`/`Process`/`PhysicsProcess` lifecycle plus a deliberately small set of discrete hooks (`ModifyJackhammerRadius/Damage/Impulse`, `OnJackhammerImpact`, `OnSpeedImpact`, `ModifyJumpStrength`, `ModifyLaserTunnelRadius`/`ModifyLaserBeamRadius`, `OnGrappleAttach`). Continuous effects (Glide, Little Friend, Tech Vision, Exo Suit) should read `Player`'s existing public state from inside a subclass's own `Process`/`PhysicsProcess` — only add a new hook to the base class when an accessory genuinely can't be expressed that way.
- `Scripts/Datasets/AccessoryDescriptor.cs` + `Accessory_Registry.cs` — mirrors the `BiomeDescriptor`/`Biome_Registry` convention (`Name` is the lookup key, `CreateInstance` factory, `IconIndex` into `item_texture_atlas.png`'s 12×8 grid).
- `Scripts/Entities/Player/PlayerAccessories.cs` — partial class of `Player` (same pattern as `PlayerAbilities.cs`): equip/unequip, `EquipStartingAccessories()`, and the hook-aggregation helpers. Wired into `Player.ImHere()`, `_Process`, `ApplyMovementFromInput`, and the grapple-attach/jackhammer-fire points in `PlayerAbilities.cs`.
- Equipped state lives in `Global.EquippedAccessoryIds` (persists across planet loads within a run; cleared by `RunManager.ResetRunState()`). GDScript (F3 menu, HUD) must go through `Global.GetAllAccessoryNames()`/`IsAccessoryEquipped()`/`SetAccessoryEquipped()` — **GDScript can call methods on a C# autoload but cannot read its plain public properties**, confirmed empirically.
- `PlayerHUD.cs` renders equipped accessories as atlas icons in `RunUI/AccessoryRow`, a real scene node in `character.tscn` (not runtime-only) — rebuilds children only when the equipped set changes.

## Planet curvature — BUILT, DELIBERATELY PARKED AT 0

A curved-world vertex displacement that bends terrain down away from the viewer so the far
edge falls below a horizon instead of ending at a visible chunk boundary. **It works. It is
switched off on purpose** (`Global.DefaultCurveExaggeration = 0`, 2026-08-10). Do not "fix"
it by turning it up without reading why it's off.

- **Why it's off:** curvature compresses *apparent distance* past where the bend starts. On
  a 1536-block world a target 450 out was drawn where one 82 out would sit; at the render
  edge, ~15. That is what a horizon is — not a bug, not tunable away. But this game's core
  loop is a continuous "can I reach that?" judgement with a 220-unit grapple, so it attacks
  the primary verb. Confirmed empirically: raising `GrappleRange` to 620 made things that
  looked ~80 blocks away suddenly grabbable.
- **Secondary cost:** nothing renders through one chokepoint, so every future visual (boss
  VFX, THE PLANT, the warpstation) would have to opt in or visibly detach from the ground.
- **Where it's still a good idea:** anywhere distance judgement doesn't matter — the ship
  hub backdrop, SolarSelect art, the crashlanding entry sequence.
- **To re-enable:** raise `DefaultCurveExaggeration` or drag the F3 slider. Everything else
  is already wired; no other change needed.

How it's built, if it comes back:

- **`Materials/WorldCurve.gdshaderinc`** — three global shader uniforms (`world_curve_strength`,
  `world_curve_flat_radius`, `world_curve_origin`) plus `world_curve_drop()`. A material opts
  in by `#include`-ing it and subtracting the result from `VERTEX.y`. Currently included by
  `ChunkCurved`, `ChunkCurvedTransparent`, `BlockDamage` and `Select`.
- **Globals are registered at runtime** in `Global.RegisterCurveGlobals()`, not in
  project.godot's `[shader_globals]` — the live editor re-serializes that section from its
  stale in-memory copy and silently reverts hand-written entries.
- **`RenderingServer.GlobalShaderParameterGet`/`GetList` are EDITOR-ONLY.** In a running game
  they return null *and* log "should never be used outside the editor". Values are mirrored
  in C# fields and written one-way to the RenderingServer.
- **The origin is the camera's world position, passed explicitly** rather than derived from
  view space: the shadow pass runs the same vertex code with the *light's* matrices, so a
  view-space formulation slides shadows off the geometry casting them.
- **Curvature is derived from WORLD SIZE, never render distance** (`strength = exaggeration *
  PI / wrap_width`). Render distance is a viewer preference; deriving world shape from it
  would mean two players on one planet see differently-shaped worlds.
- **The flat zone** (`drop = strength * max(0, d - flat_radius)^2`, radius = 0.25 × wrap
  width) exists because everything at the same XZ gets the same drop — so local relationships
  (bullet vs block, enemy vs ground) stay exact, but anything comparing *across* distances
  breaks, and aiming is exactly that. Inside the radius there is no displacement to disagree
  about.
- **Frustum culling must be compensated.** Culling tests the un-displaced AABB on the CPU and
  knows nothing about the vertex stage, so chunks blink out along the screen edges. Fixed via
  `MeshInstance3D.ExtraCullMargin = Global.GetCurveDropAtEdge(...)`.
- **Entities curve on the CPU, not in a shader** (`Entity.ApplyCurveToVisuals`): it drops the
  entity's *visual children* only, leaving its own transform — and therefore collision, AI and
  every `get_block` consumer — flat. Deliberately not done by reparenting under an offset
  pivot, because `AnimationPlayer` tracks address targets by NodePath and inserting a node
  breaks every animation on every imported enemy. `CollisionShape3D`/`Area3D` children are
  excluded, which is also why enemy hitboxes stay at the true position while models are drawn
  low past the flat radius.
- **Never curved:** anything in the arms' SubViewport (viewmodel space — bending it bows the
  laser arm on screen). Still uncurved when parked: particles already in flight, the warp
  point cage, the grapple hook mesh, the laser beam.

## Ability range is capped by loaded terrain — a real trap on small planets

`get_block` returns 0 for **"air" and "chunk not loaded" alike**, so any ability that marches
through voxels silently stops at the edge of loaded terrain rather than erroring. The grapple
(`PlayerAbilities` line ~898) and the laser both do this.

Loaded radius is `RenderDistance × CHUNK_SIZE`. Combined with the one-node guarantee below,
this produces a hard law: **no ability can reach more than half the wrap width**, because the
terrain it would need is terrain you are forbidden to have loaded. Ability ranges therefore
set a *minimum planet size*: GrappleRange 220 needs ≥ 11 chunks (528 blocks), LaserRange 300
needs ≥ 15 chunks (720). Below that they fail by finding air, with no error.

## One-node guarantee — the clamp runs on RENDER DISTANCE, not world size

`Chunk_Manager._Ready` clamps `RenderDistance` down to fit the planet. **It used to do the
opposite** (grow the planet to fit render distance), which was backwards: world size is a
property of the planet, render distance is a viewer preference that must stay tunable for a
weaker machine, and a graphics setting must never silently reshape the world.

- The rule is `PlanetChunks > RenderDistance * 2`. Violate it and two *visible* chunks resolve
  to one canonical entry — and since a physical chunk takes its voxel array from the canonical
  store **by reference** (`generate_data`), they wouldn't merely look identical, they'd BE
  identical: a hole blown in one appears instantly in its twin.
- Must run **before** `RecalculateChunkOffsets()`, which caches its offset volume from
  `RenderDistance`.
- Consequence: on a small planet render distance has a low ceiling (a 13-chunk world caps it
  at 6). That's correct physics — a small planet has a near horizon — but see the ability-range
  trap above before shrinking a world.

## Block transparency

Transparency is a **render concern only**. A glass block is fully solid to collision, grapple,
explosions, `damage_block` and every other `get_block` consumer — nothing outside the mesher and
the material knows or cares.

- **`Block_Definition.Transparent`** — the authoritative flag. Two consequences: the block meshes
  into a separate alpha-blended surface, and it stops hiding the face of whatever is behind it.
- **`Block_Definition.Alpha`** (default 1) — uniform tint alpha, delivered as vertex colour and
  multiplied onto the atlas texture's own per-pixel alpha. Anything below 1 implies `Transparent`,
  so setting `Alpha` alone is enough. **Glass and Frame carry their alpha in the art** (Glass is
  painted ~33% with opaque pane edges; Frame is a hard cut-out with alpha-0 holes) — they are left
  at `Alpha = 1` so the art renders as painted. Lower it to fade a block without repainting.
- **`Block_Registry.TransparentById`** — flat `bool[]` mirror of the flag, built once at the end of
  the static ctor. The mesher reads it once per face per block; use it there, not `Blocks[id].Transparent`.
- **Face culling (`Chunk_Manager.FaceVisible`)** — an opaque neighbour hides everything, as before.
  A transparent neighbour hides *nothing except an identical block type*: a solid pane of glass
  doesn't draw its own internal seams, glass against frame draws both, and stone behind glass stays
  visible. Verified numerically — a 4×4×4 glass cube meshes to exactly its 96-face shell.
- **Two surfaces per chunk mesh** — opaque (`Mat`) then transparent (`TransparentMat`), assigned via
  `ArrayMesh.SurfaceSetMaterial`. **Never set `MeshInstance3D.MaterialOverride` on a chunk** — it
  wins over per-surface materials and collapses both passes into one look. The transparent surface
  is skipped entirely when a chunk has no transparent blocks, so ordinary terrain still builds one
  surface and costs exactly what it did before.
- **`Materials/block_texture_atlas_transparent.tres`** — `transparency = 4`
  (`ALPHA_DEPTH_PRE_PASS`) so near-opaque pixels still write depth (Frame's struts, Glass's pane
  edges) while the translucent interior blends; `vertex_color_use_as_albedo = true` for the `Alpha`
  knob. A scene that forgets to wire `TransparentMat` falls back to `Mat` and renders transparent
  blocks opaque — wrong-looking but non-crashing, by design.
- **`IsFullySolid` means fully *opaque*.** It drives two skips (this chunk builds no mesh; and via
  `adjacent_chunks_solid`, neighbours skip too), both of which would erase a glass chunk's geometry.
  `generate_data` and `set_block` both account for this — placing a transparent block clears it just
  like removing a block does.
- Per-thread mesh scratch is now one `MeshScratch` per pass (`_tlOpaque` / `_tlTransparent`), still
  `[ThreadStatic]` and still grown-and-kept rather than reallocated. The transparent one starts at
  1/8 the capacity since most chunks have no glass.

## Structure builder — Scenes/StructureBuilder.tscn (dev tool)

Reachable from the "Builder" button on `MainMenu.tscn`. Build a thing by hand, save it as a
`Structure` resource, stamp it back into the world from code. Not part of the run flow — it
never touches `RunManager`.

- **`Structure.cs`** (`Resource`, `[GlobalClass]`) — `Size` / `Anchor` / `Voxels`. Voxels are
  indexed `x + z*SX + y*SX*SZ`, deliberately identical to `Chunk_Manager.voxel_index`, which is
  what makes `StampIntoChunk` a straight copy loop. Two write paths:
  - `Stamp(cm, worldPos, clearAir)` — live world, via `place_block`. `worldPos` is where `Anchor`
    lands, not the min corner.
  - `StampIntoChunk(chunkVoxels, chunkPos, worldPos, clearAir)` — writes into one raw chunk array,
    clipped to that chunk. No `Chunk_Manager`, no meshing, no main thread. **This is the seam
    `FeatureStage` should use** — a generation worker has no `Chunk_Manager` to call.
  - `clearAir = false` (default) is additive; pass `true` for anything with an interior, or the
    terrain it lands in fills the rooms.
- **`Structure_Registry.cs`** (autoload) — same "Name is the lookup key" convention as
  `Biome_Registry`, but file-backed: scans `res://Structures/*.tres`, plus `user://Structures`
  because `res://` is read-only in an exported build (`GetSaveDir()` picks the right one).
  `CaptureAndSave` trims empty margins, so volume size costs nothing in the saved file, and sets
  `Anchor` to bottom-centre (editable in the Inspector after).
- **The builder world is a flat plate**, not a planet: `StructureBuilder.gd._enter_tree()` calls
  `Global.SetPlanetConfig` with `noise_scale`/`height_amp` = 0. It must be `_enter_tree` — children
  are ready before their parent and `Chunk_Manager._Ready` starts the generation threads.
- **The base plate (y ≤ 0) sits outside the build volume** (`VOLUME_FLOOR_Y = 1`) so it can't be
  captured or dug through. Every edit is clamped to the volume; the cyan cage is exactly what Save
  captures.
- **Movement is Minecraft creative flight**, not a look-direction flycam: WASD is strictly
  horizontal (derived from yaw, so looking straight down doesn't degenerate the forward vector),
  Space/Shift are the only vertical control, Ctrl boosts, Alt is precision. The modifiers are read
  as raw keys rather than through `sprint`/`crouch` — those actions are Shift/Ctrl in combat and
  mean something different here.
- **Escape unwinds one layer at a time**: quit prompt → panel → ask about quitting. Nothing in the
  builder is auto-saved, so leaving is always the last step and always confirmed
  (`UI/QuitConfirm`, focus defaults to "Keep Building" so a stray Enter can't discard a build).
- **`Global.StreamingAnchor`** — chunk streaming follows this `Node3D` when `Global.Player` is null,
  which is how a scene with no Player streams at all. `Player` wins when both exist; gameplay scenes
  must not set it. `BuilderCamera` clears it in `_exit_tree`.
- GDScript reaching C# here goes through **instance methods on the autoload**
  (`Structure_Registry.GetSaveDir()`, `Block_Registry.GetBlockName()`) — statics are never in the
  generated per-instance member switch, so `Structure_Registry.SaveDir` would silently fail.
- **Gotcha:** editing a `.gd` that names a C# autoload right after an out-of-band `dotnet build`
  can recompile it against a stale global-class table — "Identifier not found: Structure_Registry"
  even though the autoload is correctly registered in `project.godot`. Fix is
  `filesystem_manage(op="scan")` (the headless equivalent of the editor regaining focus), not
  re-registering the autoload.

## Ship hub — Scenes/Ship.tscn

The between-runs hub. `RunManager.StartNewRun()` lands here (not on `SolarSelect` any more);
walking up to mission control opens the system-select screen **as an overlay**, so backing out
returns you to the ship instead of the main menu.

- **The ship is voxels, not a model.** It's the `"Ship"` structure authored in the builder,
  stamped into an empty world — so it collides, meshes, lights and streams exactly like terrain.
  Changing the ship = rebuild it in the builder and press Save; `Ship.gd` never knows its shape.
- **`PlanetParams.VoidWorld`** (config key `void_world`) makes `create_chunk_data` return the
  zeroed array immediately — no terrain at any altitude, so a stamped structure is the only solid
  thing in the world. Cheaper than any terrain: an all-air chunk builds no mesh at all.
- **Two ordering traps, both already handled — don't "simplify" either away:**
  - Chunks are created on a timer in `Chunk_Manager._Process`, not `_Ready`, and `set_block`
    silently no-ops on a chunk that doesn't exist. Stamping from `_ready()` writes **nothing**.
    `Ship.gd` polls **`Chunk_Manager.is_chunk_ready(worldPos)`** over the structure's footprint
    (`Structure_Registry.GetStampBounds`) instead of sleeping a fixed number of frames.
    `get_block` can't answer this — it returns 0 for "no chunk" and "air" alike.
  - Streaming follows `Global.GetPlayerPos()`, and the player can't spawn until there's a floor to
    stand on. Without `Global.StreamingAnchor` pointed at the ship first, the world generates
    around `(0,0,0)`, the ship's chunks never arrive and the stamp waits forever. The player is
    spawned **after** the stamp for the same reason — spawn first and it falls through the void.
- **Mission control is a distance test**, not an `Area3D` — one interaction point, so no collision
  layers or masks that could silently stop matching. Its position comes from a **marker block**
  (below), not from a hand-written offset.
- **`console_visual` is an exported `Node3D`** — drag any node onto it in the Inspector (a light,
  a mesh, an imported `.glb`, a whole instanced scene). The script hides it on load and moves it
  onto the marker when the marker resolves, so its authored position in the scene is irrelevant
  and it can't sit glowing in the wrong place when there's no marker. Optional; empty = no visual.
  Typed `Node3D` rather than `NodePath` specifically because that's what gives the Inspector a
  drag-and-drop slot.
- **`SolarSelect.gd` has an `overlay_mode`**, mirroring `SolarMap.gd`'s: pauses the tree, closes on
  Esc, emits `select_closed`. The back button keeps whatever label the `.tscn` carries in both
  modes — the overlay deliberately does not relabel it. Committing to a system must
  **unpause before `ChooseSystem`** — `paused` is a tree flag, not a scene one, and it would
  otherwise carry into `SolarMap` and freeze its buttons.

## The ship is the only way in and out of a run

`RunManager.ReturnToShip()` is the **single exit from an attempt** — abandoning, dying, and
clearing the sun all land back on `Ship.tscn`. `StartNewRun()` is just an alias for it, because
starting a run and ending one are the same event: "be on the ship with fresh offers".

- Callers **defer to it** and never change scene themselves (same convention as `ChooseSystem`
  and `ChooseAccessory`) — a `ChangeSceneToFile` alongside it races the one it queues. Wired
  into `SolarMap._on_abandon_button_pressed`, `LoadingScreen._on_return_button_pressed`,
  `SolarSelect._on_back_button_pressed` (full-screen route), `PlanetSelect` (shelved) and
  `Player`'s death-restart.
- It rolls fresh offers, so a finished attempt is never re-offered the system it just played,
  and it forces `Paused = false` — `paused` is a tree flag that survives a scene change, and the
  map/select overlays set it.
- The only remaining exit to `MainMenu.tscn` is the structure builder's quit, which is correct:
  the builder is a dev tool, not a run.
- **`Global.Player` validates on read.** A freed Player leaves a NON-null C# wrapper around a
  disposed object, so `Player != null` passes and the next member access throws
  `ObjectDisposedException`. The getter nulls it via `IsInstanceValid`. `Player.SelectedEnemy`
  and `PlayerAbilities.GrappledEntity` do the same, for the same reason — **`??` is not a
  safe fallback between two entity references**, since the disposed wrapper wins the coalesce
  even when `IsInstanceValid` already rejected it (this was a real per-frame crash in
  `PlayerHUD.UpdateEnemyIndicator`). Note the guards in `ProcessGrapple` are not enough on
  their own: abilities run in `_PhysicsProcess` and the HUD reads in `_Process`, so there is
  always a frame where a just-freed entity is still referenced. This only started
  mattering when two `Chunk_Manager` scenes could run back-to-back (ship → planet → ship):
  chunk streaming calls `GetPlayerPos()` on the new scene's first frames while the field still
  points at the previous scene's corpse, which silently broke streaming and stopped the ship
  from ever stamping.

## Terrain destruction — per-scene toggle

`[Export] public bool Chunk_Manager.TerrainDestructible` (default true). Set it **false** on a
scene's `Game` node and that world becomes indestructible. `Ship.tscn` uses it: the hub is a set,
not a level, and a hole in the floor drops the player into an infinite void with no way back.

- **Gated at `Chunk_Manager`, not by disabling player abilities.** Destruction arrives from the
  jackhammer, the laser tunnel, ram-into-block, the explode key, Super Slam and Explosive Bounce;
  turning off one ability leaves the rest working. Four gates cover every path:
  `break_block` (the true choke point — every path ends there), plus `explode`, `damage_block`
  and `damage_check` so blocks don't accumulate damage state and visible cracks while never
  breaking. `set_blocks_batch` is only reachable via `explode`; `set_block(pos, 0)` only via
  `break_block`.
- **Destruction only.** `set_block`/`place_block` stay open — otherwise `Structure.Stamp`
  couldn't build the ship in the first place.
- **Fixed 2026-08-07:** `interactions.gd` used to fire an explosion on raw keycode 69 (**E**),
  the same key `interact` is bound to. Harmless in the hub (destruction off), but it collided
  everywhere destructible — and became a real bug the moment E meant "use the warp point" on a
  planet. The raw branch is gone; `explode` (**F**) is the only trigger.

## Marker blocks — positions authored in the builder, not restated in script

**Rule: a positional fact about a structure lives in the structure.** A script that stamps one
should *look it up*, never restate it as an offset — two copies of the same fact drift silently,
and the only symptom is that the game feels subtly wrong.

- **Block ids 26–30 = `Marker1`..`Marker5`** (atlas indices 25–29, filling the atlas to 30/32).
  Ordinary placeable blocks in the builder palette; `Block_Definition.IsMarker` is mirrored into
  `Block_Registry.MarkerById[]` for the hot path, same convention as `TransparentById`.
- **Both stamp paths skip markers**, so they exist while authoring and never in a live world.
  `create_chunk_data` never emits them either — a gameplay world cannot contain one.
- **Markers stay in the saved `Voxels`**; they are *not* stripped at capture. Baking them into
  metadata at save time would mean Load → edit → Save silently destroys every marker in the
  builder. Skipping at stamp time gets a clean world *and* an exact builder round-trip.
- **Lookup:** `Structure_Registry.GetMarkers(name, number, worldPos)` → world positions for a
  stamp that put `Anchor` on `worldPos`. Empty when the structure or marker is missing.
- **Numbered, not named** — a structure's roles are its own business. `Ship.tscn` decides
  Marker1 means mission control (`console_marker` export); a warpstation can mean something else.
- **No fallback position on a missing marker.** `Ship.gd` reports it on screen and leaves the
  console disabled — silently interacting with a guessed empty spot is the exact failure markers
  exist to prevent.
- **Gotcha:** a marker replaces the block in its cell, and the stamp skips it, so a marker buried
  in a wall leaves a hole. Place them in air.

## Hitstop — global, automatic for particles

`Global.TriggerHitstop(duration)` sets a timer (`Global.HitstopActive`); systems that must freeze read that flag. Player deliberately keeps processing through it (`Entity.FreezeDuringHitstop => false` on Player, plus input buffering in `Player.cs`), which is why `Engine.TimeScale` / `GetTree().Paused` are **not** used — tree pause would also stall the chunk generation threads and the `_readyToPromote` drain queue.

**Particle freezing needs no per-effect code.** `Global._OnNodeAdded` (wired to `SceneTree.NodeAdded` in `_Ready()`) auto-registers every `GpuParticles3D` entering the tree into the `hitstop_particles` group. `SetParticlesFrozen` then sweeps that group on the hitstop **start/end edges only** — never per frame.

- **Use `SpeedScale`, not `Emitting`.** `Emitting = false` only stops *new* particles spawning; everything already in flight keeps moving through the freeze. `SpeedScale = 0` zeroes the delta fed to the particle process shader, which is what actually stops them.
- Each node's authored speed is stashed in a `hitstop_base_speed` meta on registration and restored from it — a blanket reset to `1.0` would clobber effects authored at another value (Creature's `EmberParticles` runs at `2.0`).
- Effects instantiated *during* a freeze start frozen too — a jackhammer impact triggers its own hitstop before spawning its explosion, so otherwise the explosion would play out during the stop it caused.
- Groups auto-clean on node free, so there are no dangling references to sweep.
- **Do not add hitstop handling to individual effects or enemy scripts.** `Enemy._Process` gates particles on `Lod` only, for this reason. The legacy `UniParticles3D` type is not covered by the group, but it's deprecated project-wide (see the Enemy LOD section).

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
