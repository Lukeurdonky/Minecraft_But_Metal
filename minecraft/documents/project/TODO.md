# TODO

---

## Immediate / In Progress

- [x] Create `Assets/GrappleHook.tscn` and assign to Player node's `GrappleHookScene` export
- [x] Grapple rope — cylinder mesh in SubViewport using tentacle material
- [x] Entity grapple feel — tuned reelspeed, lunge, cooldown, jump escape, LOS filter
- [x] Verify Creature collision layer is on Layer 2 so GrappleHook's Area3D can detect them

---

## Player Abilities — Polish

- [x] Jackhammer cone hit detection — generous ~41° cone replaces crosshair raycast
- [x] Jackhammer air swing — no knockback if no block in cone
- [x] Laser VFX — orange emissive beam, block tunneling via explode(), player knockback opposite fire direction
- [x] Jackhammer committed charge — press once to commit; auto-fires at full charge, hold to delay release
- [x] Jackhammer speed-based damage tiers — weak/medium/hard at <15/15–30/>30 u/s; 0.5s descending coyote window
- [ ] ~~Dash trail / directional feedback~~ — dash deprioritized, grapple covers mobility
- [x] Ability cooldown HUD — laser bar: blue when ready/firing, gray + fills while recharging
- [x] Speed tier HUD (temp) — 3 colored segments, active tier bright, coyote tier flashes
- [x] LaserOutline arm animation — state machine: Extended (slow idle spin, poles=0.65, triangle=0) → Spinning (fast spin, both=0) → FoldPoles → FoldTriangle → Retracted → UnfoldPoles → UnfoldTriangle → Extended
- [x] Laser beam color — red emissive unshaded material
- [x] Grapple rope + hook color — dark green emissive unshaded material
- [x] Speed threshold VFX — camera shake when bulldozing terrain at high speed

---

## Combat

- [x] Enemy takes damage and dies — `Entity.TakeDamage()` + `Die()` → `QueueFree()`
- [x] Player takes damage, has health bar UI — contact damage from Creature, red bar bottom-left
- [x] Knockback on hit — `TakeDamage(amount, knockbackVector)` implemented; enemy contact applies directional knockback to player
- [x] Global camera shake — `Global.ShakeCamera(intensity, duration)` callable from any script; applied in `RotateCamera()`
- [x] Player death / run-end state — "You've met your Antithesis" screen (`Assets/character.tscn` → `CanvasLayer/DeathScreen`), jump to reload scene
- [x] Kill counter per planet (Exploration win condition) — `Global.KillCount`, resets per planet, now also drives `RunManager`'s stage-clear check
- [x] Survival timer (`Global.RunTimer`) — ticks while a player exists; not yet wired as an actual alternate win condition (only kill-count currently advances a stage)
- [ ] "Reach the core" objective (Combat win condition)

---

## Enemies

- [x] Entity → Enemy → Creature class hierarchy
- [x] Enemy base class — `AttackDamage`, `DetectionRange`, world-space health bar (green→red, faces camera)
- [x] Damage/health single source of truth — all damage through `Entity.TakeDamage()`, health bar refreshes on hit
- [x] Enemy health bar visual polish — billboard shader (fixed via BillboardMode material)
- [x] Enemy health bar polish — damage flash, hide at full health
- [ ] At least 3 distinct enemy types (swarm, heavy, ranged)
  - [x] SwarmEnemy.cs — fast, small, flying, light, group attacker (needs model + scene)
  - [x] HeavyEnemy.cs — slow, tanky, ground, heavy=true, charge attack (needs model + scene)
  - [x] RangedEnemy.cs — medium, ground, maintains distance, fires EnemyBolt (needs model + scene)
  - [x] GroundRobotShooter.cs — grounded gunner, full model + scene + script (`Assets/ground_robot_shooter.tscn`). Rotates/walks toward player, auto-jumps 1-block walls, scrubs (`Seek`, not `Play`) a single "Aim" clip on the arm skeleton to track the player's vertical angle, lerped via `AimLerpSpeed`. Killable with the standard `Enemy` health bar (`collision_layer = 3` so Jackhammer/Laser hit-detection — `CollisionMask = 2` — can find it; this was the one non-obvious gotcha, easy to forget on a hand-built scene). Does not fire yet.
- [x] `EnemySpawner` multi-type — `EnemyScenes[]` (PackedScene array) + parallel `SpawnWeights[]` (float array), weighted random pick via `PickScene()`. `CubeLand.tscn` wired with `creature.tscn` (weight 0.6) and `ground_robot_shooter.tscn` (weight 0.4). Add new enemy types by appending to both arrays in the Inspector.
  - [ ] SwarmEnemy/HeavyEnemy/RangedEnemy still need models before they can join the spawner pool
- [x] Wall navigation — ground enemies auto-jump over 1-block walls when chasing
- [x] Improve Creature.cs AI — attack behavior (deal `AttackDamage` on contact), not just chase
- [x] Creature rework — 3-state AI (Idle/Chase/Grab), range-based detection, Idle animation during chase, Grab animation only on attack, 3-phase lunge (charge/impulse/recovery), forward-direction lunge, GrabHitbox Area3D in scene, upward knockback factor, pitch tracked on mesh child, BoxShape3D collider, hitstop freezes particles + animations via auto-scan in Enemy
- [x] Mark some creatures as `heavy = true` (pulled toward instead of reeled in when grappled)
- [x] Enemy spawning system (tied to terrain + difficulty)
- [ ] Enemy drops (upgrade currency)
- [ ] A* block pathfinding — for enemies that get stuck behind complex geometry (low priority while terrain is open)
- [ ] Boss enemy with large health bar UI

---

## World Wrapping

> The loop is an illusion of generation, not teleportation. Player and entities move freely in raw world space forever. The chunk manager maps any raw chunk coord to canonical data via modulo — dirty chunks reload their saved state at the new offset, clean chunks regenerate identically from the same seed. Nothing moves, everything repeats.

- [x] `PlanetChunksX` / `PlanetChunksZ` constants in `Global.cs`; derive `PlanetWidth` / `PlanetDepth` from them (never hardcode block counts)
- [x] At startup, hard-clamp: `PlanetChunksX = max(PlanetChunksX, RenderDistanceChunks * 2 + 1)` (same for Z); print warning if clamped
- [x] Canonical coord utilities in `Global.cs`: `CanonicalBlockX`, `CanonicalBlockZ`, `CanonicalChunkX`, `CanonicalChunkZ`, `CanonicalChunkPos`
- [x] Split `Chunk_Manager` into `_canonicalStore` (canonical coord → `ChunkData` with voxels + WasEdited, permanent per run) and `chunks` (raw physical coord → scene node, always freed on unload)
- [x] `ChunkData.WasEdited` flag — edited canonical chunks persist in `_canonicalStore` across unloads; unedited canonical data is dropped on unload and regenerates identically from seed
- [x] `generate_data` checks `_canonicalStore` first; uses canonical position for `create_chunk_data` so terrain repeats across laps
- [x] `set_block` / `set_blocks_batch` mark canonical `WasEdited` so damage survives future unloads
- [x] Physical chunk node always removed from `chunks` on unload; canonical store owns the voxel array

---

## Enemy Spawning (chunk-based)

- [ ] `EnemySpawnDescriptor` struct (`LocalPosition`, `EnemyType`) in `Chunk.cs` or shared types file
- [ ] `SpawnDescriptors` list on `ChunkData` — not on the physical chunk node, which gets freed on unload
- [ ] `FeatureStage` populates `SpawnDescriptors` using canonical chunk seed (same seed = same layout every time)
- [ ] `EnemySpawner` reads `SpawnDescriptors` on chunk load and instantiates enemy nodes (Creature is reference)
- [ ] `OwnerChunkPos` field on `Entity.cs` — set at spawn to the **raw** chunk coord; unload sweep matches directly against the unloading node's raw coord, no canonicalization needed
- [ ] Chunk manager sweeps live enemies on unload and frees those matching the raw coord (no persistence — enemies respawn fresh on next load)

---

## World Generation

- [x] Seamless terrain via 4D simplex noise on flat torus (`Simplex4D.cs`) — replaces FastNoiseLite
- [x] `PlanetParams.cs` — single source of truth for all generation values; `Global.ActivePlanet` set before scene load; `MakeField()`, `MakeCave()`, `MakeAbyss()` presets
- [x] Removed all generation `[Export]` fields from `Chunk_Manager`; `create_chunk_data` reads `Global.Instance.ActivePlanet`
- [x] `PlanetConfigMenu.gd` — F3 debug UI: biome selector pre-fills presets; SpinBox/CheckButton rows for all params; Generate button calls `Global.SetPlanetConfig` → `reload_current_scene()`
- [x] Block palette IDs 1–16 in `Block_Registry.cs`. Notable blocks: Cloud (8), Crystal (10), LightCrystal (11), Sand (13), Moss (14), Lava (15), Virus (16). Atlas is full at 16/16 slots.
- [x] `CaveStage` — true 3D two-octave density field: Y encoded as additive torus phase offsets, not worm rotation. Lives in `create_chunk_data`; migrate to `CaveStage.Generate()` when WorldGenerator is wired
- [x] `BiomeDescriptor.cs` + `Biome_Registry.cs` — 9 hardcoded biomes across 3 templates (Field/Cave/Abyss). Each owns surface block, param ranges, fog color. `MakePlanetParams(seed)` randomises within ranges for RunManager use.
- [x] World size param in F3 menu — `planet_chunks` SpinBox sets `Global.PlanetChunksX/Z` on generate; default 32 chunks (512 blocks)
- [ ] `TerrainStage` — port height-map fill from `create_chunk_data` into `World_Generator.cs` stage
- [ ] `CaveStage` migration — move cave carver from `create_chunk_data` into `CaveStage.Generate()`; gate on `CavesEnabled` / `CaveFullRange`
- [ ] `AbyssStage` — sinusoidal shaft carver; already live in `create_chunk_data`, port to stage
- [ ] `FeatureStage` — crash-site carve-out, enemy spawn markers, biome-driven feature placement (see below)
- [ ] `FeatureStage` biome features — modular self-contained feature classes (`VineFeature`, `SpikeFeature`, `PillarFeature`, `GlowVeinFeature`, etc.); `BiomeDescriptor` holds a feature list; `FeatureStage` iterates and places. Each feature has `Place(chunkData, chunkPos, rng, density)`.
- [ ] Wire `World_Generator.cs` into `Chunk_Manager` (shrink `create_chunk_data` as each stage absorbs its piece)
- [ ] Surface block palette on `BiomeDescriptor` — replace single `SurfaceBlock` with a small list (2–3 natural candidates per biome); `MakePlanetParams` picks one via seed
- [ ] Atlas expansion — `atlas_width`/`atlas_height` in `Block_Registry.cs` must be resized before any new blocks can be added (currently full at 16/16 slots)
- [ ] Enemy type tags on `BiomeDescriptor` — list of enemy archetypes valid for this biome; wired to `EnemySpawner` once enemy variety designs exist (deferred)
- [ ] Finite planet-shaped world (not infinite flat terrain)
- [ ] Underground depth zones (Underground Forest −10 to −300, Purple Crystal −310 to −600)

---

## Run Structure

> Demo scope is confirmed as a linear 3-planet-stage run followed by a boss (Inscryption-style funnel, not the full ~10-planet vision). See `../design/NEW_VISION.md` and the "Game Loop" note below.

- [x] Debug planet config menu (F3) — interim stand-in for manual planet configuration; still used for ad-hoc testing, no longer the only way to start a planet
- [x] `MainMenu.tscn` + `MainMenu.gd` — new `run/main_scene` (previously booted directly into `CubeLand.tscn`, no menu existed). "New Run" calls `RunManager.StartNewRun()`; "Quit".
- [x] `PlanetSelect.tscn` + `PlanetSelect.gd` — shows `RunManager.CurrentOptions` as 3 buttons (biome + cosmetic difficulty label); picking one calls `RunManager.ChooseOption(index)`. Also owns the temporary "3 planets cleared — boss coming soon" placeholder panel shown once `RunManager.RunComplete` is true, so the loop doesn't dead-end pending the real boss.
- [x] `RunManager.cs` singleton (autoload, registered after `Global`) — tracks `CurrentStageIndex` (0–2) through the 3 demo planet stages, generates 3 non-repeating-per-run biome options per stage (`StageOption`: biome, template, seed, cosmetic difficulty label), applies the chosen biome via `Global.ApplyPlanetParams` + `ChangeSceneToFile(CubeLand.tscn)`, and polls `Global.KillCount` each frame against a placeholder per-stage target (15/20/25) to auto-advance. `CurrentOptions` is a generic `List<StageOption>`, not a hardcoded 3-branch tuple — `CompleteStage()` is the only place assuming "next = index+1", so a real branching node-graph map can replace it later without touching the rest of `RunManager`'s public surface. See the 2026-07-02/03 roadmap artifact for the phased plan this came from.
- [x] Kill counter per planet fed into `RunManager` (Exploration win condition) — stage auto-advances on threshold
- [x] Survival timer per planet (`Global.RunTimer`) — exists and ticks, not yet wired as an alternate win condition
- [x] Planet selection screen (3 choices, difficulty shown) — `PlanetSelect.tscn`; not yet a visual branching map, by design for the demo (see `demo_run_structure` decision)
- [ ] `PlanetDescriptor` class — full implementation; holds atmosphere (SkyColor, FogColor, FogDensity, AmbientLight) + gameplay modifiers (Gravity, EnemyDensity, EnemyHostility, enemy type tags). Currently `RunManager`'s difficulty label ("Easy"/"Medium"/"Hard") is **cosmetic only** — no gameplay effect — until this exists.
- [x] `AtmosphereSystem` — reads `Global.ActivePlanet.Biome` → `Biome_Registry` on scene `_Ready()`; applies fog color/density, background color, ambient light to `WorldEnvironment` (exponential fog mode). Node in `CubeLand.tscn` under `Game`. Will swap to reading `PlanetDescriptor.Atmosphere` once that class exists.
- [ ] RunManager modifier system — framework for run-level modifiers applied on top of biome after `MakePlanetParams`; modifiers include: Low Gravity, Heavy Fog, Alien Surface, and others TBD. Deferred past the demo.
- [ ] RunManager modifier: **Alien Surface** — weighted table across all registered blocks; overrides the biome's natural surface block pick; makes familiar biomes feel visually alien on certain runs. Deferred past the demo.
- [ ] Planet map HUD visible during run — not needed while the demo is select-screen-only (no persistent map to show)
- [x] Post-planet upgrade screen (choose 1 of 3 accessories) — `Scenes/UpgradeSelect.tscn`/`.gd`, shown after every `RunManager.CompleteStage()` (including the final one, before the complete panel). Picks from accessories not yet equipped, falls back to full pool if <3 remain.
- [ ] Boss encounter trigger — replaces the current "3 planets cleared" placeholder panel in `PlanetSelect.tscn`
- [x] Run lose-state wiring — `Player.Die()` on jump-press now calls `RunManager.EndRun()` (resets stage index, `RunComplete`, used-biomes, and `Global.EquippedAccessoryIds`) and goes to `MainMenu.tscn`, instead of reloading the current planet in place. `PlanetSelect.gd`'s "Return to Main Menu" button (the win path) calls the same `EndRun()` first. `RunManager.RunComplete` is still a placeholder flag — no real win screen yet, just the "3 planets cleared" panel.

---

## Boss

- [ ] `BossState` struct (`WorldPosition`, `CurrentHealth`, `PhaseIndex`, `HasBeenEngaged`)
- [ ] `BossState?` + arena position on `RunManager` (null = not yet spawned this run)
- [ ] `RunManager.OnBossChunkLoaded()` — spawn or hydrate boss from `BossState`
- [ ] Boss node serializes to `BossState` on tree exit (position, health, phase only — animation state not saved)
- [ ] Boss node hydrates from `BossState` on spawn; resumes from start of current phase
- [ ] `HasBeenEngaged` engagement zone check — latches true, never resets; AI activates immediately on all subsequent loads
- [ ] Arena spawn point blocked clear in `FeatureStage` (no terrain generation in arena footprint)

---

## Accessories (all from `../design/NEW_VISION.md`)

- [x] Accessory runtime shell — `Accessory` base class + `Accessory_Registry`/`AccessoryDescriptor` + `PlayerAccessories.cs` (equip/unequip, hook points into jump/jackhammer/grapple/laser).
- [x] Accessory slot system — `Global.SetAccessoryEquipped`/`IsAccessoryEquipped`/`GetAllAccessoryNames` bridge methods (GDScript can only call autoload methods, not read C# properties — confirmed empirically). F3 debug menu (`PlanetConfigMenu.gd`) has a checkbox per accessory, applies instantly via the same bridge. HUD (`PlayerHUD.cs`) shows equipped accessories as atlas icons (`item_texture_atlas.png`, 12x8 grid, 16px cells) in a real scene node (`RunUI/AccessoryRow` in `character.tscn`), rebuilt only when the equipped set changes.
- [x] Super Slam — jackhammer release always explodes at the impact point, even on entity-only hits (`SuperSlamAccessory`)
- [x] Explosive Bounce — hooks the existing ram-into-a-block-and-it-breaks mechanic (`ProcessSpeedThreshold`); triggers a bigger explosion + bounces you back, cooldown-gated (`ExplosiveBounceAccessory`, numbers untuned)
- [x] Destructive Laser — tunnels a much wider hole through blocks with a thicker beam (`DestructiveLaserAccessory`)
- [x] Super Jump — cooldown-based (5s), press `super_jump` (bound to C) to launch straight up (`SuperJumpAccessory`)
- [ ] Little Friend
- [x] Glide — holding jump while airborne caps fall speed to a slow constant, vertical only, no horizontal push (`GlideAccessory`, "The Messenger" style not Minecraft elytra)
- [ ] Dig Dig Dig! — reworked concept: dedicated hotkey turns you into a human drill, keep drilling while submerged in blocks. Still being workshopped, not implemented.
- [x] Flaming Grapple — grappling an enemy sets it on fire for 3s (damage-over-time + spreads to nearby enemies); new `Enemy.SetOnFire`/burn system + `Materials/Fire.gdshader` (`FlamingGrappleAccessory`)
- [ ] Tech Vision — enemy highlight through walls
- [ ] Exo Suit — mobility buffs (dash speed/cooldown)

---

## Performance (see `../performance/PERFORMANCE.md` and `../performance/PERFORMANCE_REWORK_FINDINGS.md` for full detail)

Chunk pipeline pass — DONE:
- [x] Threaded generation pool + mesh-builder pool (sized to core count)
- [x] `[ThreadStatic]` mesh scratch buffers (fixed LOH-churn frame decay)
- [x] Mesh promotion via `_readyToPromote` drain queue (fixed orphaned-buffer leak + re-mesh loop) with per-frame time-budget throttle
- [x] `handle_chunks_art` per-crossing cost cut from ~6 O(active-set) passes to ~1 (static offset-set, small-queue reprioritize, throttled eviction, no per-chunk sqrt)
- [x] `IsFullySolid` synced to canonical store on edit; damage shader `cull_back`
- [x] All-air chunk fast path — `IsAllAir` flag skips the full mesh-build path the same way `IsFullySolid` does (see PERFORMANCE_REWORK_FINDINGS.md #1)
- [x] `handle_chunks_art` RD³ sweep spread across `SWEEP_SLICES = 8` ticks via a persisted cursor instead of scanning the full offset volume every tick (see PERFORMANCE_REWORK_FINDINGS.md #2)
- [x] Damage overlay rework — lazy/sparse per-chunk `DamageData`, slot-based incremental MultiMesh updates, dynamic per-type MultiMesh capacity (starts at 1024, doubles on demand instead of pre-allocating worst-case), free-priority flush ordering (frees fully drained before writes, so a destroyed block's crack disappears before any cosmetic tint refresh — fixes the visible "ghost crack" trailing a large explosion)

Enemy performance pass — DONE (see `../performance/ENEMY_PERFORMANCE.md`):
- [x] **Enemy LOD** — `Enemy.cs` caches `DistSqToPlayer`/`Lod` (Near/Mid/Far) once per physics tick; gates animation (`AnimationPlayer.SpeedScale`), particles (`GpuParticles3D.Emitting`), and health-bar `LookAt` by tier; `Creature.cs` throttles state/targeting decisions to every 4th frame at Far tier
- [x] **Replace `UniParticles3D` with native `GpuParticles3D`** on Creature (`EmberParticles`) — the addon's per-particle update was plain GDScript, not GPU-driven, and was the dominant remaining cost once animation/AI were LOD-gated (creatures cluster Near the player in actual combat, where LOD doesn't throttle). Confirmed: 50 concurrent enemies now run with no lag. `UniParticles3D` must not be used on any new enemy type.

Remaining:
- [ ] **Enemy spawn pooling** — reuse instances instead of instantiating the GLB per spawn (kills the 3–9 fps spawn hitch)
- [ ] Greedy meshing — cuts destroyed-terrain triangle count (GPU-bound view-dependent dips); requires texture array / custom-UV shader to tile the atlas across merged quads
- [ ] Incremental unload sweep — only scan the trailing-edge shell on crossing instead of all loaded chunks (reduces remaining moving spikes)

---

## Polish & Atmosphere

- [ ] Antithesis aesthetic — dark terrain palette, bright electronic enemy materials
- [ ] Crashlanding entry sequence (player enters planet via crash)
- [ ] Sound effects — weapons, enemies, environment
- [x] Player hit feedback — red full-screen flash (0.4s fade) + global camera shake on damage
- [ ] Particles — laser impact, explosion, enemy death, dash trail

---

## Tech Debt / Cleanup

- [x] Block damage overlay FIFO eviction — oldest tracked block evicted (health + visual) when global cap (`MAX_DAMAGED_BLOCKS = 300,000`, across all block types) is hit; `LinkedList` + node pointer for O(1) removal
- [ ] Delete or archive `Washed Code/` once nothing left to salvage
- [ ] Remove `dummy.gd`, `portal.gd` from root (unused)
- [ ] `Mob_Registry.cs` — repurpose for enemy definitions or remove
- [ ] Step-up traversal (`AttemptStepUp`) — re-evaluate once enemy AI is in and terrain is final
- [ ] `drop_item` input action in project.godot — remove or repurpose

---
