# Antithesis Conquering Simulator — Project State

> You're in your spaceship. You go to randomly generated planets. You kill things.

A voxel-based action roguelike built in **Godot 4** (C# + GDScript). Each run: choose a planet → fight → collect upgrades → boss. All combat, no crafting, no inventory. See `../design/NEW_VISION.md` for the full design doc.

---

## Design Pillars

- All combat, no filler
- Fun over narrative
- Still descending — the world goes deep
- Darker palette + bright electronic enemies
- Destructible world

## Game Loop

```
Ship → pick 1 of 3 solar systems → map → planet → clear it → warp out → map → … → sun
```

**One attempt = one procedurally generated solar system** (superseded the 3-planet demo on 2026-08-04; spec in `../design/solar-system-run-structure-design-log.html`). Implemented end-to-end as far as the sun:

`MainMenu.tscn` → `Ship.tscn` (hub) → mission control opens `SolarSelect` **as an overlay** → `SolarMap.tscn` (route) → LAUNCH → `CubeLand.tscn` → hit the node's kill target → **press J to start a 10s warp sequence** → back to `SolarMap.tscn` → LAUNCH the next node → … → sun → `LoadingScreen.tscn`'s end-of-run panel.

Run length is whatever the generated node list says (Easy 5–7 planets, Medium 10–12, Hard 16–20, plus warpstations). Warpstations are auto-skipped pending the shop. The accessory pick is currently **out of the loop** — clearing a node goes to the map, not to an upgrade screen. See "Run Structure" below.

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
- `CHUNK_SIZE`-cubed chunk system (`Global.CHUNK_SIZE = 48`, single source of truth — never hardcode), **threaded generation pool + mesh-builder pool** (both sized `Clamp((cores-2)/2, 2, 4)`)
- **Mesh promotion** (main-thread `ArrayMesh` build + GPU upload) runs via a `_readyToPromote` drain queue in `_Process`, throttled by a per-frame time budget (`MaxPromotionMillisPerFrame`). Generation still signals readiness via `CallDeferred("generate_ready_chunk")`; the **mesh-upload handoff no longer uses `CallDeferred`** (it stranded buffers under multi-threaded bursts — see `../performance/PERFORMANCE.md`).
- Greedy face culling, sphere/cylinder render distance, chunk eviction
- Block damage overlay — lazy/sparse per-chunk damage storage (`Chunk.DamageData`, null until first damaged block), slot-based incremental MultiMesh updates (each block owns a stable instance index; granting/revoking touches one slot, O(1)), per-block-type MultiMesh capacity starts small and doubles on demand instead of pre-allocating worst-case size, free-priority flush ordering (a destroyed block's crack disappearing is drained before any cosmetic tint refresh, so large explosions don't show a visible trailing "ghost crack" effect). Global FIFO eviction cap `MAX_DAMAGED_BLOCKS = 300,000` across all block types. See `../performance/PERFORMANCE_REWORK_FINDINGS.md` for the full rationale.
- Explosion system (`explode()` in Chunk_Manager) — damage = 1 required to instant-kill center block
- `damage_check()` — instant break when accumulated damage would be lethal
- **Performance pass (see `../performance/PERFORMANCE.md`)** — fixed LOH-churn frame decay, an orphaned-mesh-buffer leak/re-mesh loop, and per-chunk-crossing O(active-set) spikes in `handle_chunks_art`. Chunk pipeline is now solid (~48–60 fps at RD15). Follow-up fixes (all-air chunk fast path, RD³ sweep spreading) and the damage-overlay rework above are in `../performance/PERFORMANCE_REWORK_FINDINGS.md`. Remaining cost is GPU-bound destroyed-terrain triangles (greedy meshing not yet built).
- **Enemy performance pass (see `../performance/ENEMY_PERFORMANCE.md`)** — 50 concurrent enemies now run with no lag (previously 25 fps regardless of on/off-screen). `Enemy.cs` caches per-frame `DistSqToPlayer`/`Lod` (Near/Mid/Far); `Creature.cs` reads it instead of computing its own distance, throttles state/targeting decisions to every 4th physics frame at Far tier, and gates animation/particles/health-bar tracking off at distance. Root cause of the *remaining* lag once those landed: `UniParticles3D` (the addon used for Creature's ember effect) runs its per-particle update as a GDScript loop, not GPU-driven — replaced with a native `GpuParticles3D` (`Assets/creature.tscn` → `EmberParticles`, downward-drift ember look). **`UniParticles3D` must not be used on any new enemy** — see `CLAUDE.md`.

### Planet Generation
- `PlanetParams.cs` — single source of truth for all generation values; `Global.ActivePlanet` set before scene load. Three presets: `MakeField()`, `MakeCave()`, `MakeAbyss()`
- `PlanetConfigMenu.gd` — F3 debug UI (CanvasLayer autoload): biome selector (9 biomes) pre-fills all param spinboxes; Generate button calls `Global.SetPlanetConfig` → `reload_current_scene()`. World size (chunks) configurable; default 32 chunks (512-block planet).
- Three planet templates in `create_chunk_data`:
  - **Field** — height-map surface via 4D simplex torus noise. Block: Cloud (8) default. `NoiseScale=1.5`, `HeightAmplitude=10`.
  - **Abyss** — Field + sinusoidal shaft from planet center. Block: Steel (6) default. `ChasmRadius=18`, drift amplitude 60 blocks.
  - **Cave** — fully solid mass, all-Y cave carving, no surface. Block: Crystal (10) default. `FillSolid=true`, `CaveFullRange=true`.
- `AtmosphereSystem.cs` (node under `Game` in `CubeLand.tscn`) — reads `Global.ActivePlanet.Biome` → `Biome_Registry` on `_Ready()` and applies fog colour/density, background colour and ambient light to the `WorldEnvironment` (exponential fog mode). Swaps to `PlanetDescriptor.Atmosphere` once that exists.
  - **Cave-template planets get no directional light** — a sun underground reads as a hole in the ceiling that isn't there. The system hides `../DirectionalLight3D` and switches ambient from the default **Bg** source to an explicit **Color** so it can be brightened past the background: `fog.Lerp(White, CaveAmbientLift)` at `CaveAmbientEnergy` (both `[Export]`s, 0.45 / 1.8). Note `AmbientLightColor` was a no-op before this — with the Bg source it just re-used the background at energy 1. Non-cave planets are restored explicitly so a cached `Environment` sub-resource can't carry cave lighting onto a field. Abyss deliberately keeps its sun.
- Cave carving: true 3D two-octave density field. Y encoded as additive phase offsets to torus coords (`phX = worldY * invW * CaveYFreq`). Preserves X/Z seam seamlessness while varying in all three spatial dimensions. Two octaves: large chambers (base) + connecting passages (×2 freq, ×0.5 amp). Cave where `d1+d2 > CaveThreshold`.
- Spawn clear: `SpawnClearEnabled` carves a guaranteed open ellipsoid (`SpawnClearRadiusXZ=10`, `SpawnClearRadiusY=6`) centered at `WorldSpawn`, runs last in `create_chunk_data` so it cannot be re-filled. Required for Cave template.
- Block palette IDs 1–16 in `Block_Registry.cs`. Atlas is full at 16/16 slots — expanding blocks requires resizing atlas. Key blocks: Grass(1), Stone(3), Steel(6), Cloud(8), Crystal(10), LightCrystal(11), Sand(13), Moss(14), Lava(15), Virus(16).
- **Biome system** — `BiomeDescriptor.cs` + `Biome_Registry.cs`. 9 hardcoded biomes across 3 templates. Each biome owns: template tag, surface block, terrain param ranges, fog color. `MakePlanetParams(seed)` randomises within ranges for RunManager. F3 menu biome selector pre-fills spinboxes with midpoint values.
  - Field: Bouncy Cloud Plains · Grassy Plains · Metallic Mountains
  - Cave: Tight Stone Tunnels · Crystal Caverns · The Moss Grotto
  - Abyss: Dark Descent · The Virus · Lava Walls
- Enemy unload fix: `Enemy._ExitTree` decrements `EnemyCount` via `_counted` guard (idempotent with `Die()`). Distance despawn at 160 units keeps counter accurate as player loads new chunks.

### Run Structure
- `SolarSystemDescriptor.cs` — seeded system generator + data model. `SystemNodeKind` (Planet/Warpstation/Sun), `SystemNodeState` (Locked/Current/Cleared), `SystemNodeFog` (Known/Rough/Hidden). `Tiers` table holds planet counts, warpstation counts, clock, `EnemyDensityScale`, modifier count and `DangerLevel`. Same seed always rebuilds the same system, which is what lets SolarSelect preview a real topology.
- **Danger level** — game-wide 1–10 threat scale (`MinDanger`/`MaxDanger`), with `PlantDanger` (11) reserved and never generated. All three tiers are DangerLevel 1 on purpose: danger is its own axis, not a restatement of tier. Shown by the Danger Meter on `SolarSelect`, which reads both `danger` and `danger_max` off the offer so it never hardcodes 10.
- `RunManager.cs` (autoload, right after `Global`) — drives one attempt = one system. `CurrentSystem`, `CurrentNodeIndex`, `ClockRemaining`, `RunComplete`.
  - `ReturnToShip()` is the **single exit from an attempt** — abandoning, dying and clearing the sun all route through it. `StartNewRun()` is an alias, because starting and ending a run are the same event ("be on the ship with fresh offers").
  - `ChooseSystem(index)` commits to one of three generated offers (final, no going back) → `SolarMap.tscn`. `LaunchCurrentNode()` applies the node's biome and drops into `CubeLand.tscn`.
  - **Warp-out**: hitting `_stageKillTarget` latches `WarpReady` rather than ending the stage. The player presses `start_warp` (**J**) → `StartWarp()` → `WarpCharging` counts down `WarpChargeSeconds` (10) → `CompleteStage()`. The shared clock drains through both, so lingering costs. `CompleteStage()` goes to **`SolarMap.tscn`**; the only exception is `RunComplete`, which goes to `LoadingScreen.tscn` for its end-of-run panel.
  - Local fog (`FogFor`) — current + cleared nodes Known, next Rough, rest Hidden; the sun stays Rough from the start as the stated objective. Hidden nodes have their contents blanked server-side so the UI never receives them.
  - `CompleteStage()` is still the only place assuming "next = index + 1" — the seam if the linear rail ever becomes a branching graph.
- `Scenes/Ship.tscn` — between-runs hub, built as the `"Ship"` voxel structure stamped into a `VoidWorld`; mission control located via a Marker1 block, opens `SolarSelect` as an overlay.
- `Scenes/SolarSelect.tscn` / `SolarMap.tscn` — offer select and topology map. Map read (all in `SolarMap.gd` consts):
  - A **cleared planet swaps to `Sprites/planet destroyed icon.png`** — planets only; the sun ends the run and a warpstation reads "VISITED".
  - **Only the current node has a ring**, and it's a bare outline (no filled disc). `Sprites/Antithesis Icon.png` hovers above it as the you-are-here marker, anchored off the ring's top edge so the sun's larger ring pushes it up automatically.
  - **Cleared nodes fade to `CLEARED_DIM`** (0.4) via whole-cell modulate, so icon and all three labels dim together; travelled rail legs use the same value.
  - **Every rail leg is dashed** (`_make_dashed_link` — `Line2D` can't dash, so a leg is a row of segments). All future legs share one colour/alpha; progress is colour-only: dim green behind, grey ahead.
  - Icons draw at full brightness. Fog-as-brightness was removed — a greyed planet read as a wrecked one, and "destroyed" now has its own icon. Fog shows in the title/subtitle (`- - -` / `UNKNOWN`) only.
  - ⚠ **`Sprites/planet icon.png` is currently a byte-identical copy of the destroyed icon** (bad Krita export, 2026-08-07), so un-cleared planets draw the wreck. Code is correct; restore from `planet icon.png~` or re-export from `planet icon.kra`.
- **Run HUD meters** (`PlayerHUD`, `RunUI` on `character.tscn`): `Label` → system clock counting **down**, `Label2` → **kills remaining** then `AREA CLEAR`, `WarpLabel` → the warp prompt/countdown. All plain placeholders. Both meters fall back to the per-planet `RunTimer`/`KillCount` when no stage is active, since CubeLand is still reachable without a run.
- `Scenes/PlanetSelect.tscn` + `ChooseOption`/`GetOptionsForUI`/`GenerateOptionsForStage` are **shelved, not deleted** — still on disk, still correct, superseded by the solar map.
- `Global.ApplyPlanetParams(PlanetParams p)` — extracted from the tail of `SetPlanetConfig` (apply params, reset `WorldSpawn`/`KillCount`/`RunTimer`) so `RunManager` can skip the dictionary round-trip; the F3 debug menu path (`SetPlanetConfig`) now calls this too.
- `Scenes/MainMenu.tscn` (`MainMenu.gd`, GDScript) — new `run/main_scene` (previously `CubeLand.tscn` directly — there was no menu). Title, "New Run" (`RunManager.StartNewRun()` → `PlanetSelect.tscn`), "Quit".
- `Scenes/PlanetSelect.tscn` (`PlanetSelect.gd`, GDScript) — two toggled `VBoxContainer` panels under a `CenterContainer`: `OptionsPanel` (3 buttons built at runtime from `RunManager.GetOptionsForUI()`) and `CompletePanel` (shown instead, once `RunManager.RunComplete`, with a "3 planets cleared — boss coming soon" placeholder and a Return-to-MainMenu button — avoids dead-ending the loop pending the real boss).
- Both new scenes hand-styled (dark `#0a0c0f` background, `#3bdce6` cyan accent) — no shared Theme resource exists in the project yet.
- `Scenes/UpgradeSelect.tscn` (`UpgradeSelect.gd`) — inserted between a stage clearing and `PlanetSelect.tscn`. `RunManager.CompleteStage()` always generates 3 accessory options (`GenerateAccessoryOptions()`, excludes already-equipped, same pool-fallback pattern as biome options) and routes here first, on every stage clear including the final one. Picking one (`RunManager.ChooseAccessory(index)`) equips it (persists via `Global.EquippedAccessoryIds`) then continues to `PlanetSelect.tscn` as normal.
- Run lose/win reset: `RunManager.ResetRunState()` (stage index, `RunComplete`, used-biomes, `Global.EquippedAccessoryIds`) is shared by `StartNewRun()` and a new `EndRun()`. `Player.Die()`'s jump-to-restart now calls `RunManager.Instance.EndRun()` + goes to `MainMenu.tscn` (previously just reloaded the current planet in place, no run consequence at all). `PlanetSelect.gd`'s "Return to Main Menu" button (the win path) calls `EndRun()` too, so a completed run resets the same way a failed one does.
- Verified end-to-end (MCP `game_eval` + `game_manage`): full MainMenu → PlanetSelect → CubeLand → auto-advance ×3 → completion-placeholder → MainMenu loop, including the correct biome/template actually reaching `Chunk_Manager` for each stage.

### Accessories
- `Scripts/Entities/Player/Accessories/Accessory.cs` — base class. Lifecycle (`OnEquip`/`OnUnequip`/`Process`/`PhysicsProcess`) plus a deliberately small set of discrete hooks added only as real accessories needed them: `ModifyJackhammerRadius/Damage/Impulse` + `OnJackhammerImpact`, `ModifyJumpStrength`, `OnGrappleAttach`, `ModifyLaserTunnelRadius/BeamRadius`, `OnSpeedImpact`. Continuous effects (Glide, Super Jump) just read/write `Player` state directly from `Process`/`PhysicsProcess` — no hook needed.
- `Scripts/Datasets/Accessory_Registry.cs` / `AccessoryDescriptor.cs` — mirrors the `Biome_Registry`/`BiomeDescriptor` convention (`Name` is the lookup key, `CreateInstance` factory, `IconIndex` for the atlas icon).
- `Scripts/Entities/Player/PlayerAccessories.cs` (partial class of `Player`) — equip/unequip, `EquipStartingAccessories()` (reads `Global.EquippedAccessoryIds` on `Player.ImHere()`), and the hook-aggregation helpers called from `PlayerAbilities.cs`/`Player.cs`.
- **GDScript↔C# bridge**: GDScript can call methods on C# autoloads but cannot read their plain properties (confirmed empirically) — `Global.cs` exposes `GetAllAccessoryNames()`/`IsAccessoryEquipped()`/`SetAccessoryEquipped()` as the one bridge point, used by both the F3 debug menu and the upgrade-pick screen.
- F3 debug menu (`PlanetConfigMenu.gd`) — "Accessories" section, one `CheckButton` per accessory, applies instantly. Checkboxes re-sync to real equipped state every time the panel opens (`_refresh_accessory_checks`), not just at first build.
- HUD (`PlayerHUD.cs`) — `RunUI/AccessoryRow` (a real `HBoxContainer` node in `character.tscn`, not runtime-only) shows equipped accessories as icons cropped from `Sprites/Textures/item_texture_atlas.png` (12×8 grid, 16px cells) via `AtlasTexture` + `IconIndex`. Rebuilds only when the equipped set changes.
- **Implemented:** Super Slam (jackhammer release always explodes at impact, even entity-only hits), Explosive Bounce (hooks the existing ram-into-a-block-and-it-breaks mechanic in `PlayerAbilities.ProcessSpeedThreshold` — bigger explosion + velocity-reflect bounce, cooldown-gated), Destructive Laser (2.5x wider tunnel, 1.6x thicker beam), Super Jump (cooldown 5s, new `super_jump` input action bound to C, launches straight up), Glide (holding jump while airborne caps fall speed at -4 u/s, vertical only), Flaming Grapple (grapple hit sets an enemy on fire 3s — see Enemy burn system below).
- **Not implemented:** Little Friend, Dig Dig Dig! (concept: hotkey → human drill, keep drilling while submerged in blocks — still being workshopped, not settled), Tech Vision, Exo Suit.

### Player Movement
- WASD, mouse-look FPS camera, sprint, spectator mode (V)
- Single velocity system with Quake-style directional air movement
- Delta-time correct friction via `Mathf.Pow`
- Air friction only applies when pressing movement keys AND slower than input speed in that direction
- Ability momentum (grapple/dash) carries freely through open air, no friction unless steering
- `PhysicallyOnFloor()` / `OnFloor()` split for correct coyote behavior

### Player Abilities
- **Jackhammer** (`attack1` press-to-commit) — press once to commit a 0.5s charge; charge runs automatically to full. Holding the button at full charge holds the pose; release fires. Explosion at targeted block (full radius). Damage determined by speed at fire time — 3 tiers: weak (<15 u/s, 20 dmg), medium (15–30 u/s, 50 dmg), hard (>30 u/s, 100 dmg). Player bounced opposite camera look at full impulse. A 0.5s coyote window keeps the effective tier active after speed drops, so grapple/laser momentum can be cashed in even as you decelerate. Hitstop durations are `[Export]` on Player: `HitstopMed` (0.25s), `HitstopHard` (0.5s).
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
- Full 16-block palette wired across all three templates via the biome system (see Planet Generation above) — `World_Generator.cs`'s 5-stage pipeline itself is still empty; the inline generation in `create_chunk_data` already uses the full palette and is the thing that pipeline is meant to absorb.
- `Entity.cs` base: health, AABB physics, `heavy` bool, `Grappled` bool (suppresses movement during reel)
- `Enemy.cs` (extends Entity): `AttackDamage`, `DetectionRange`, `Flying`, procedural world-space health bar (green→red, camera-facing billboard, damage flash, hidden at full health). Tracks `EnemyCount` in Global on spawn/death. **LOD cache** — `DistSqToPlayer`/`Lod` (Near/Mid/Far, 40u/80u thresholds) computed once per physics tick in `ApplyMovementFromInput` before subclass logic runs; gates animation (`AnimationPlayer.SpeedScale`), particles (`GpuParticles3D.Emitting`), and health-bar `LookAt` by distance. See `../performance/ENEMY_PERFORMANCE.md`.
- **Burning (Flaming Grapple accessory)** — `Enemy.cs` also owns a static `List<Enemy>` registry (populated in `ImHere`, removed via the existing `DecrementCount()` path) and `IsBurning`/`SetOnFire(duration)`/`UpdateBurning(delta)`. Damage tick (5 dmg/0.5s) always runs; the O(n) spread scan (4-unit radius, every 1s, ignites nearby non-burning members of the registry) is gated behind `Lod != LodTier.Far` per the LOD conventions above. Fire visual is a lazily-built `GpuParticles3D` (matches `PlayerAbilities`'s dynamic-VFX-in-code pattern) whose draw-pass material is the new `Materials/Fire.gdshader` (stylized additive-blend flame billboard) — kept out of the shared `_particles` LOD list since that list would force-emit fire on every Near-tier enemy regardless of burning state.
- `Creature.cs` (extends Enemy): flying 3-state AI. **Idle** — hovers in place, Y-rotates and pitch-tracks toward player, loops Idle animation. Transitions to Chase when player enters `DetectionRange`. **Chase** — flies toward player (Idle animation still playing), pitch-tracks player vertically. Transitions to Grab when within `AttackRange` (default 6u). **Grab** — 3-phase lunge: charge (bleeds velocity to stop over `GrabDamageStart`), lunge impulse (single velocity burst = `LungeSpeed` in the creature's forward direction at animation start), recovery (decelerates after `GrabDamageEnd`). Damage window `GrabDamageStart`–`GrabDamageEnd`, once per grab, checked against a scene-placed `GrabHitbox` Area3D (BoxShape3D) — editor-positionable hitbox in front of the creature, read as an AABB in code. Knockback has an upward component: `KnockbackStrength * KnockbackUpFactor` added to Y. Uses `TentacleCreature.glb` model. Pitch rotation applied to `TentacleCreature` mesh child only (root stays upright for clean physics); collision shape is BoxShape3D (replaced capsule). Lunge direction = `GlobalTransform.Basis * _mesh.Transform.Basis.Z`. Reads inherited `DistSqToPlayer`/`Lod` instead of computing its own distance; throttles state/targeting decisions to every 4th physics frame at Far tier (delta accumulated across skipped frames). `EmberParticles` child is a native `GpuParticles3D` (downward-drift embers, replaced the GDScript-CPU-bound `UniParticles3D` addon — see `../performance/ENEMY_PERFORMANCE.md`). `Enemy._Process` auto-scans for `GpuParticles3D`/`UniParticles3D` and `AnimationPlayer` descendants on spawn and freezes them (SpeedScale/paused or `Emitting`) during hitstop — zero per-enemy setup required.
- `SwarmEnemy.cs` (extends Enemy): fast (12 u/s), small (0.6×0.7), flying, `heavy=false`. Random jitter each 0.4s prevents all swarm members taking identical paths. Short attack cooldown (0.6s). Needs model + scene.
- `HeavyEnemy.cs` (extends Enemy): slow (3.5 u/s), large (1.4×2.2), ground, `heavy=true`. Charge attack (18 u/s burst, 0.4s, 4s cooldown) at range > 12. Auto-jumps 1-block walls via `OnBlockCollision`. Needs model + scene.
- `RangedEnemy.cs` (extends Enemy): medium (4.5 u/s), ground, `heavy=false`. Maintains 20-unit ideal range, strafes perpendicular to player. Fires `EnemyBolt` every 2.5s when in LOS. LOS via block ray march. Auto-jumps walls. Needs model + scene.
- `GroundRobotShooter.cs` (extends Enemy): grounded gunner, first fully-modeled enemy beyond Creature (`Assets/ground_robot_shooter.tscn`, model `Assets/ground_shooter_real.tscn`). Yaws to face player and walks toward them at `MoveSpeed` while within `DetectionRange` (30u), auto-jumps 1-block walls via `OnBlockCollision`. Arm aim: single "Aim" clip on `GroundShooterReal/AnimationPlayer` (2s, drives a 5-bone arm skeleton through its full -90..+90 pitch sweep) is never `Play()`ed forward — `ImHere` calls `Play("Aim")` once just to assign it as the current animation, then every tick `SpeedScale` is re-zeroed and `Seek(t, true)` scrubs to the position matching the player's vertical angle (`Mathf.Atan2(toPlayer.Y, flat.Length())`, lerped via `AimLerpSpeed` so it swings instead of snapping). `Seek()` is a no-op without an assigned current animation — that was the first bug (arm looked static). Skipped at Far LOD tier per the enemy-performance standard. Killable via the inherited `Enemy` health bar/`TakeDamage` — required `collision_layer = 3` on the root (hand-built scenes don't inherit `Creature.tscn`'s layer setup; Jackhammer/Laser hit-detection queries `CollisionMask = 2`, so layer-1-only enemies are immune to player damage). Does not fire its gun yet.
- `MossCreature.cs` (extends Enemy): the easy enemy (`Assets/moss_creature.tscn`). Grounded melee, 3.5 u/s (40% over GroundRobotShooter), 20 HP so a **weak** jackhammer blow one-shots it, ~1 block collision box (width 0.8 / height 0.85, `offset.Y = -0.14` centring the box on the model). **No attack of its own** — contact damage only on a 0.75s re-hit cooldown, tested against a `ContactHitbox/HitboxShape` Area3D sized to the *visible* body; testing against the entity's own 0.8-square movement AABB did not work, because the model is 1.37 long and the spikes visibly overlapped the player with no hit registering. Single `Walk` clip always playing, `LoopMode` set to Linear and `Length` pinned to 1.94s in `ImHere()` (Godot's importer can set loop mode but not length). Auto-jumps 1-block walls, but **not** with GroundRobotShooter's formula — that one ignores `Entity.offset` and only works there because its offset is positive; this compares the block's top surface against the real box bottom from `GetAABB()`.
- `EnemySpawner` in `CubeLand.tscn` is wired with ground_robot_shooter (0.4), creature (0.6) and moss_creature (0.5).
- `EnemyBolt.cs` (extends Projectile): orange emissive box, slight arc (gravity 4), 4s lifetime. Damages player on contact with directional knockback.
- Explosion system wired to E key in interactions.gd
- `PlayerHUD.cs`: jump indicator, enemy soft-aim indicator, crosshair color, player health bar (red, bottom-left), laser charge bar (blue when ready/firing, gray while recharging), speed tier indicator (3 segments, temp), red full-screen flash on player hit (fades over 0.4s)

### Planet curvature — built 2026-08-10, deliberately parked at 0

A curved-world vertex displacement (terrain bends down away from the viewer so the far edge
falls below a horizon instead of ending at a chunk boundary). **Fully working and switched
off on purpose** — `Global.DefaultCurveExaggeration = 0`. Raise it or drag the F3 slider and
the whole system comes back; nothing else needs rewiring.

It was parked because curvature compresses *apparent distance* past where the bend begins: on
a 1536-block world a target 450 out was drawn at the screen position a target 82 out would
occupy, and one at the render edge read as ~15. That IS a horizon — not a bug, not tunable
away — but this game's core loop is a continuous "can I reach that?" judgement with a
220-unit grapple, so it attacks the primary verb. Proven by raising `GrappleRange` to 620 and
watching apparently-close things become grabbable.

Still a good fit anywhere distance judgement doesn't matter: ship hub backdrop, SolarSelect
art, the crashlanding entry sequence. Full technical detail in `../../CLAUDE.md`.

### Two findings that outlived the curve

- **Ability range is capped by loaded terrain.** `get_block` returns 0 for "air" and "chunk
  not loaded" alike, so the grapple's and laser's voxel marches stop silently at the edge of
  loaded terrain. Combined with the one-node guarantee (render distance ≤ half the world),
  nothing can reach past half the wrap width — so ability ranges set a **minimum planet
  size**: GrappleRange 220 needs ≥ 11 chunks (528 blocks), LaserRange 300 needs ≥ 15 chunks
  (720). Below that they fail by finding air, with no error. No startup check warns yet.
- **The one-node clamp was inverted.** `Chunk_Manager._Ready` now clamps *RenderDistance*
  down to fit the planet, where it used to grow the *planet* to fit RenderDistance. World
  size is a property of the planet; render distance is a viewer preference that must stay
  tunable on weaker hardware, and a graphics setting must never silently reshape the world.

### Archived (do not restore)
- Minecraft inventory (36-slot), item registry, item behaviors, placeable/consumable/tool system, world-dropped items. See `../../CLAUDE.md` for file list.

---

## What's Not Started Yet

| System | Notes |
|---|---|
| World_Generator pipeline | Three templates (Field/Cave/Abyss) live inline in `create_chunk_data`. `World_Generator.cs` 5-stage pipeline is empty — TerrainStage, CaveStage, AbyssStage, FeatureStage need to absorb the inline code. |
| FeatureStage | Biome-driven feature placement (vines, spikes, pillars, glow veins, etc.). Modular feature classes, biome holds a feature list. |
| PlanetDescriptor | Doc-only stub. Needs full C# implementation: atmosphere fields + gameplay modifiers (gravity, enemy density/hostility). `RunManager`'s difficulty label is cosmetic-only until this exists. |
| Boss (THE PLANT) | Not started. `PlanetSelect.tscn` shows a "boss coming soon" placeholder after 3 planet clears instead of triggering an encounter. |
| RunManager modifier system | Low Gravity, Heavy Fog, Alien Surface (weighted block table override), others TBD. Deferred past the demo. |
| Run win/lose states | `RunManager.RunComplete` is still a placeholder flag (no real win screen). Lose-path now exists: death calls `RunManager.EndRun()` and returns to `MainMenu.tscn`, resetting stage progress + accessories. |
| Enemy AI | 3 enemy type skeletons (Swarm/Heavy/Ranged) coded, waiting on models. `EnemySpawner` is multi-type/weighted and live with Creature + GroundRobotShooter. A* pathfinding not yet implemented. |
| Enemy type tags | `BiomeDescriptor` has placeholder field. Wiring deferred until enemy designs exist. |
| Accessories | 6 of 10 implemented (Super Slam, Explosive Bounce, Destructive Laser, Super Jump, Glide, Flaming Grapple — see Accessories section above). Remaining: Little Friend, Dig Dig Dig! (concept unsettled), Tech Vision, Exo Suit. |
| VFX | Laser beam ✅. Grapple rope ✅. No dash trail, no block break particles, no enemy death particles. |
| Sound | Nothing. |
| World save/load | Explicitly removed. Roguelike — no persistence between runs. |

---

## Notable Code Locations

| Thing | File |
|---|---|
| Run flow (3-stage loop, stage-clear check) | `Scripts/Handlers/RunManager.cs` |
| Entry point / New Run | `Scenes/MainMenu.tscn`, `Scripts/Handlers/MainMenu.gd` |
| Planet stage-select UI | `Scenes/PlanetSelect.tscn`, `Scripts/Handlers/PlanetSelect.gd` |
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
| Accessory base class + hooks | `Scripts/Entities/Player/Accessories/Accessory.cs` |
| Accessory registry/descriptor | `Scripts/Datasets/Accessory_Registry.cs`, `AccessoryDescriptor.cs` |
| Accessory equip/unequip + hook wiring | `Scripts/Entities/Player/PlayerAccessories.cs` |
| Individual accessory implementations | `Scripts/Entities/Player/Accessories/*Accessory.cs` |
| Enemy burn/fire system | `Scripts/Entities/Enemy.cs` → `SetOnFire`/`UpdateBurning`, `Materials/Fire.gdshader` |
| Upgrade-pick screen | `Scenes/UpgradeSelect.tscn`, `Scripts/Handlers/UpgradeSelect.gd` |
| Enemy soft-aim + LOS check | `Player.cs` → `UpdateEnemySelection`, `HasBlockLOS` |
