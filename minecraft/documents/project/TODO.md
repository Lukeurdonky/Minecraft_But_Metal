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
  - [x] MossCreature.cs — easy grounded melee, full model + scene (`Assets/moss_creature.tscn`). 3.5 u/s (40% over GroundRobotShooter's 2.5), 20 HP so a **weak** jackhammer blow (`JackhammerDamageWeak`) one-shots it, ~1 block collision box (width 0.8 / height 0.85, `offset.Y = -0.14` to centre the box on the model). No attack of its own — contact damage only, on a 0.75s re-hit cooldown, since the spikes are the weapon, tested against a `ContactHitbox/HitboxShape` Area3D box sized to the **visible** body (Creature.cs's pattern). Testing against the entity's own movement AABB instead did **not** work: that box is a 0.8 square while the model is 1.37 long, so the spikes visibly overlapped the player with no hit registering. Kept square in XZ (1.4 × 1 × 1.4) because the test is axis-aligned and would otherwise breathe as the creature turned. Single `Walk` clip, always playing, `LoopMode` set to Linear and `Length` pinned to 1.94s in `ImHere()` (the clip's own tail loops late). Verified live: 4 spawned, all `length=1.94 loop=LINEAR current="Walk"` with positions wrapping past the loop point, and `TakeDamage(20)` killed one outright.
- [x] `EnemySpawner` multi-type — `EnemyScenes[]` (PackedScene array) + parallel `SpawnWeights[]` (float array), weighted random pick via `PickScene()`. `CubeLand.tscn` wired with `creature.tscn` (0.6), `ground_robot_shooter.tscn` (0.4) and `moss_creature.tscn` (0.5). Add new enemy types by appending to both arrays in the Inspector.
  - [ ] **Do not set `EnemyScenes` through the godot-ai MCP `set_property`** — it serializes the array as plain path *strings* and drops the scene's `[ext_resource]` entries, which silently empties the array at load and stops all spawning (hit 2026-08-06; the game ran at 60fps with zero enemies and no error). Edit the `.tscn`'s `ExtResource(...)` list by hand, then `scene_open(force_reload=true)`.
  - [ ] MossCreature is in the global pool, not restricted to grassy biomes — biome-gated spawning needs the deferred `BiomeDescriptor` enemy-type tags below.
  - [ ] SwarmEnemy/HeavyEnemy/RangedEnemy still need models before they can join the spawner pool
- [x] Wall navigation — ground enemies auto-jump over 1-block walls when chasing
  - [ ] **`GroundRobotShooter`'s step-up test is offset-blind and only works by luck.** `blockPos.Y > floor(GlobalPosition.Y - height/2)` ignores `Entity.offset`; it happens to be right there because that enemy's offset is *positive*, which floors one cell lower. Copying it onto MossCreature (negative offset) put the threshold on the wall block's own cell, so `>` was never true and it never stepped up at all. `MossCreature.OnBlockCollision` now compares the block's top surface against the real box bottom (`GetAABB().Position.Y`, which includes offset) — correct for any offset. Worth hoisting to a shared `Enemy` helper the next time a third ground enemy needs it, rather than copying either version again.
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
- [x] Atlas expansion — grew to 12×16 cells on 2026-08-04, i.e. 32 block slots. **25 used (ids 1–25), 7 free.** New art must go *below* the existing rows or every UV in the game shifts.
- [x] Blocks 21–25 (2026-08-05): EmptyCrate, GreenEnergy, Plate, Glass, Frame — atlas indices 20–24 (index is always `Id - 1`; Air owns no art).

### Block transparency (2026-08-05)

- [x] `Block_Definition.Transparent` / `.Alpha` + `Block_Registry.TransparentById` flat lookup. Render-only — transparent blocks stay fully solid to collision, grapple, explosions and damage.
- [x] `Chunk_Manager.FaceVisible` — opaque neighbours hide everything; transparent neighbours hide only an identical block type. Verified numerically in all four directions (glass shell = 96 faces, stone encased in glass keeps all 6 faces, glass|glass culls the seam, glass|frame draws both).
- [x] Two-surface chunk meshes (opaque + alpha) with per-surface materials via `SurfaceSetMaterial`; transparent surface skipped when a chunk has no glass, so plain terrain is unchanged (measured: 583 chunk meshes → 583 surfaces, 60 fps).
- [x] `Materials/block_texture_atlas_transparent.tres` (`ALPHA_DEPTH_PRE_PASS` + vertex-colour albedo), wired as `TransparentMat` on `CubeLand.tscn` and `StructureBuilder.tscn`.
- [x] `IsFullySolid` now means fully *opaque* — a chunk of glass no longer takes the invisible solid fast-path, and placing glass into a solid chunk clears the flag.
- [ ] Damage overlays on transparent blocks render an opaque crack box. Works and doesn't error, just looks odd on glass — needs an alpha variant of `damageOverlayMaterial` if it ever matters.
- [ ] Mipmaps are on for the atlas; at distance, alpha can bleed between neighbouring atlas cells and soften Frame's cut-out holes. Not observed as a problem yet — fix by padding the atlas or disabling mipmaps if it shows up.
- [ ] Per-triangle sorting within the transparent surface is not done (Godot sorts per object). `ALPHA_DEPTH_PRE_PASS` hides this for the current art; deeply stacked translucent volumes could still show ordering artifacts.
- [ ] Enemy type tags on `BiomeDescriptor` — list of enemy archetypes valid for this biome; wired to `EnemySpawner` once enemy variety designs exist (deferred)
- [ ] Finite planet-shaped world (not infinite flat terrain)
- [ ] Underground depth zones (Underground Forest −10 to −300, Purple Crystal −310 to −600)

### Structures (2026-08-05)

- [x] `Scripts/The World/Structure.cs` — `Resource` holding a tight voxel box (`Size`, `Anchor`, `Voxels`), indexed the same way `Chunk_Manager` indexes a chunk. Two write paths: `Stamp()` into a live world via `place_block`, and `StampIntoChunk()` straight into a raw chunk `byte[]` — the latter is the seam `FeatureStage` will use, since a generation worker has no `Chunk_Manager` to talk to.
- [x] `Scripts/Datasets/Structure_Registry.cs` (autoload) — file-backed registry over `res://Structures/*.tres` (falls back to `user://Structures` in an exported build, where `res://` is read-only). `Get(name)` / `StampByName()` for gameplay; `CaptureAndSave()` / `LoadIntoBuildVolume()` / `ClearVolume()` for the builder. Capture trims empty margins, so an 8-block hut saved out of a 32³ volume is 8 blocks.
- [x] `Scenes/StructureBuilder.tscn` + `StructureBuilder.gd` + `BuilderCamera.gd` — offline authoring tool, reachable from the "Builder" button on `MainMenu.tscn`. Runs the real `Chunk_Manager` on a flat featureless plate (`noise_scale`/`height_amp` = 0), noclip flycam, DDA block targeting, cyan wireframe cage marking exactly what Save captures. Not part of the run — never touches `RunManager` (`_stageActive` is false outside a planet, so its `_Process` poll stays inert).
- [x] `Global.StreamingAnchor` — chunk streaming follows this `Node3D` when there's no `Player`. `Player` still wins when both exist; gameplay scenes never set it.
- [ ] `FeatureStage` consumes structures — biome-driven scatter using `StampIntoChunk` for every chunk a structure's box overlaps. Needs a placement rule (surface-snap? density?) before it's worth building.
- [ ] Anchor is bottom-centre on capture and only editable in the Inspector afterwards — an in-builder anchor marker would be better once structures need precise docking points.
- [ ] Structures are stamped additively (`clearAir = false`) by default; anything with an interior needs `clearAir = true` so the hillside it lands in doesn't fill the rooms.
- [x] `Structure_Registry.CaptureAndSave` uses `TakeOverPath`, not `ResourcePath = path` — re-saving a structure leaves an older instance of it in the resource cache, and assigning the path directly errors with "Another resource is loaded from path" (the write still lands, but the cache keeps handing back the stale copy).

### Ship hub (2026-08-06)

- [x] `Scenes/Ship.tscn` + `Scripts/Handlers/Ship.gd` — between-runs hub. `RunManager.StartNewRun()` now lands here instead of `SolarSelect`; offers are rolled on the way in so they don't reshuffle each time you walk up to the console and back out.
- [x] The ship is the `"Ship"` structure stamped into an empty world, not modelled geometry — it collides/meshes/lights exactly like terrain, and editing it means rebuilding it in the builder.
- [x] `PlanetParams.VoidWorld` (config key `void_world`) — `create_chunk_data` returns the zeroed array immediately. No terrain at any altitude; all-air chunks build no mesh, so a void world is cheaper than any planet.
- [x] `Chunk_Manager.is_chunk_ready(worldPos)` — chunks are created on a timer in `_Process` and `set_block` silently no-ops on a missing chunk, so a stamp from `_ready()` writes nothing. Callers poll this over the footprint (`Structure_Registry.GetStampBounds`) rather than guessing a frame count. `get_block` can't answer it: 0 means both "no chunk" and "air".
- [x] `SolarSelect.gd` gained `overlay_mode` (mirrors `SolarMap.gd`) — pauses the tree, closes on Esc, emits `select_closed`. The back button keeps the `.tscn`'s own label in both modes (the overlay does not relabel it). Committing unpauses before `ChooseSystem`, since `paused` is a tree flag that would otherwise freeze `SolarMap`.
- [x] `interact` action bound to E.
- [ ] Mission control is the only interactable. A second interaction point would justify a small trigger-volume system instead of the current distance test.

### Run entry/exit through the ship (2026-08-06)

- [x] `RunManager.ReturnToShip()` — the single exit from an attempt. Abandoning (`SolarMap`), finishing (`LoadingScreen`'s end-of-run Return), backing out of full-screen `SolarSelect`, `PlanetSelect` (shelved) and dying (`Player`'s death-restart) all route through it. `StartNewRun()` is an alias: starting and ending a run are the same event.
- [x] Rolls fresh offers on the way in, so a finished attempt is never re-offered the system it just played; forces `Paused = false` since `paused` is a tree flag that survives a scene change and the overlays set it.
- [x] Only remaining exit to `MainMenu.tscn` is the structure builder's quit — correct, it's a dev tool, not a run.
- [x] Verified live: MainMenu → Ship → SolarMap → **abandon** → Ship (254 blocks, console found, offers reshuffled); LoadingScreen → **Return** → Ship; CubeLand → **death** → jump → Ship with run state cleared. Whole session's log info-only, 60 fps.
- [x] **Latent bug fixed:** `Global.Player` now validates on read via `IsInstanceValid`. A freed Player leaves a non-null C# wrapper around a disposed object, so `Player != null` passed and the next access threw `ObjectDisposedException`. Harmless while every run ended at MainMenu; the moment ship → planet → ship became possible it broke chunk streaming on the return (streaming reads `GetPlayerPos()` while the field still points at the previous scene's corpse) and the ship silently failed to stamp.
- [ ] Death drops straight to the ship with no run summary — no score, no "you died on planet 4". The end-of-run panel in `LoadingScreen` is the natural place for that once there's anything to report.

### Terrain destruction toggle (2026-08-06)

- [x] `[Export] Chunk_Manager.TerrainDestructible` (default true), set false on `Ship.tscn`'s Game node. Universal per-scene switch: gates `break_block` (the choke point every destruction path ends at) plus `explode`, `damage_block` and `damage_check` so blocks don't accumulate cracks while never breaking. Verified in the hub: break/damage_check/damage_block/an 8-radius explode all left the ship untouched.
- [x] Gated at `Chunk_Manager` rather than by disabling player abilities — destruction comes from jackhammer, laser tunnel, ram-into-block, the explode key, Super Slam and Explosive Bounce, and disabling one leaves the rest live.
- [x] Blocks destruction only; `set_block`/`place_block` stay open so `Structure.Stamp` can still build the ship.
- [ ] **Key collision:** `interactions.gd` uses raw keycode 69 (E) as an alternate explode trigger, the same key `interact` is bound to. Harmless in the hub (destruction off) but they collide in any destructible scene — rebind one.
- [ ] Enemies/projectiles aren't considered by the flag; nothing spawns them in the hub today, but a scene wanting "no destruction" *and* combat would need the enemy damage paths checked too.

### Marker blocks (2026-08-06)

- [x] Block ids 26–30 = `Marker1`..`Marker5` (atlas indices 25–29). **Atlas now 30/32 used, 2 free.** Ordinary placeable blocks in the builder; `Block_Definition.IsMarker` mirrored into `Block_Registry.MarkerById[]` (same convention as `TransparentById`).
- [x] `Structure.Stamp` / `StampIntoChunk` skip markers — they exist while authoring and never in a live world. `create_chunk_data` never emits them, so a gameplay world can't contain one.
- [x] Markers stay in the saved `Voxels` rather than being stripped at capture, so a builder Load → edit → Save round-trip preserves them. Verified: re-saving after a load kept Marker1.
- [x] `Structure.FindMarkers(number, worldPos)` + `Structure_Registry.GetMarkers(name, number, worldPos)` — world positions for a stamp that put `Anchor` on `worldPos`. Empty for a missing structure or marker.
- [x] `Ship.gd` locates mission control via Marker1 (`console_marker` export) instead of a hand-written offset. No fallback position: a missing marker shows an on-screen error and leaves the console disabled.
- [x] `Ship.tres` carries a Marker1 at structure-local height 2 above the floor; verified the stamped world contains **zero** marker blocks and the marker's cell reads as air.
- [x] `Ship.gd`'s `console_visual` is an exported `Node3D` — drag any node in (currently `Assets/Console.glb` with a green `OmniLight3D` child). Script hides it on load and moves it onto the marker, so its authored scene position doesn't matter. Typed `Node3D`, not `NodePath`, because that's what gives the Inspector a drag-and-drop slot.
- [ ] **Atlas art for Marker1–5** (indices 25–29) — check they're painted; a marker with blank art is invisible in the builder and effectively unplaceable.
- [ ] A marker replaces the block in its cell and the stamp skips it, so one buried in a wall leaves a hole. Place them in air.
- [ ] The console model is centred on the marker cell, so a `.glb` whose pivot isn't at its base floats or sinks. Currently corrected by moving the marker block; a `console_visual_offset` export would fix it at the pivot instead if that gets fiddly.
- [ ] Nothing in the hub reflects run state yet — no crew, no upgrades, no record of previous attempts. The warpstation/shop work is the natural place for that to land.
- [ ] The player carries its full combat HUD (kill counter, run clock, health bars) into the hub, where none of it means anything.
- [x] Spawn is `SHIP_ORIGIN.y + SPAWN_HEIGHT` (2). Measured: the player's resting origin on the deck is y≈10 with the floor block at y=8 — the old `+1` was spawning them *inside* the floor block, since manual AABB collision doesn't push a body out of geometry it starts in.

---

## Run Structure

> **Superseded 2026-08-04.** The linear 3-planet-stage demo is replaced by the solar-system model in `../design/solar-system-run-structure-design-log.html`: one attempt = one procedurally generated solar system of 5–20 planets plus warpstations, ending at the sun. `RunManager.TotalStages`/`StageKillTargets` are gone — run length is whatever the generated node list says. See the Solar System block below.

### Solar system run structure (2026-08-04)

- [x] `Scripts/Datasets/SolarSystemDescriptor.cs` — seeded generator + data model. `SystemNodeKind` (Planet/Warpstation/Sun), `SystemNodeState` (Locked/Current/Cleared), `SystemNodeFog` (Known/Rough/Hidden). `Tiers` table: Easy 5–7 planets / 1 warpstation, Medium 10–12 / 2, Hard 16–20 / 3. Warpstations spaced evenly through the planet sequence, so a single one lands mid-system. Biomes drawn without replacement from a bag that refills (a 20-planet system needs more than the 9 registered biomes). Same seed always rebuilds the same system, which is what lets SolarSelect preview a real topology.
- [x] `Scenes/SolarSelect.tscn` + `SolarSelect.gd` — first screen of an attempt. The three art sheets (`Sprites/solar select easy|medium|hard.png`) are each full 1920x1080 layers, transparent outside their own vertical third (measured: 0–644 / 645–1284 / 1285–1919), so they stack into one composed screen. Idle `modulate` 0.6314 grey, hovered tweens to `(1,1,1)`. Preview shows planet count, warpstations, clock and modifiers — never biomes or enemy composition (Decision 02).
- [x] `Scenes/SolarMap.tscn` + `SolarMap.gd` — the topology map, one scene used two ways: `MODE_ROUTE` (full screen after committing, ends in LAUNCH) and overlay (`overlay_mode = true`, instanced over CubeLand on the `toggle_map` action / **M**, pauses the tree). Fixed 250px node pitch means the rail *scrolls* rather than compressing — a 24-node Hard system is a 6190px track at the same node scale as a 9-node Easy one. Drag, wheel and A/D all pan; RECENTER snaps to the current node. Per-node state signifiers: CLEARED / YOU ARE HERE / fogged.
- [x] Local fog (Decision 03) — `RunManager.FogFor()`. Current + cleared nodes Known (biome and kill target visible), next node Rough (terrain family and difficulty band only, biome blanked server-side so the UI never receives it), rest Hidden. The sun stays Rough from the start since it's the stated objective.
- [x] Shared clock — `RunManager.ClockRemaining`, ticks only while a node is actually being played, so it doesn't drain behind the map overlay or the accessory pick. Easy 8:00 / Medium 13:00 / Hard 18:00, all **untuned placeholders**.
### Warp-out + run meters (2026-08-07)

- [x] Clearing a node's kill target no longer teleports you off it. `RunManager` latches `WarpReady`, the HUD offers `PRESS J TO START WARP SEQUENCE`, `StartWarp()` charges for `WarpChargeSeconds` (10) and only then does `CompleteStage()` fire. New `start_warp` action bound to **J**. The clock drains through both the decision and the charge, so waiting costs.
- [x] `CompleteStage()` now lands on **`SolarMap.tscn`** instead of `LoadingScreen.tscn`. `RunComplete` is the one exception — it still goes to LoadingScreen, which owns the end-of-run panel.
- [x] Two run meters on `character.tscn`'s `RunUI`: `Label` → system clock counting **down**, `Label2` → **kills remaining** (then `AREA CLEAR`), `WarpLabel` → the prompt/countdown. All plain placeholders; treatment is the user's call.
- [x] Verified live end-to-end: commit offer → SolarMap → LAUNCH → `12 ENEMIES LEFT` / `07:52` counting down → 12 kills → stayed on the planet with the prompt shown → `StartWarp()` → `WARPING IN 5` → SolarMap at node index 1, warp state reset, clock continued 472→442 → LAUNCH → `16 ENEMIES LEFT` on a fresh node.
- [x] Cleared planets on `SolarMap` use `Sprites/planet destroyed icon.png` (planets only — the sun ends the run and a warpstation reads as VISITED, so both keep their own icon).
- [x] **Only the current node has a ring.** Every other state is border 0; state is read from the icon instead (destroyed art for cleared, fog brightness for ahead), so a ring on the map always means "you are here". `Sprites/Antithesis Icon.png` hovers above that node as the you-are-here marker, positioned off the ring's top edge rather than the cell's so it clears the sun's larger ring with no second special case. Verified live: node 0 = border 4 + Antithesis marker above the planet icon, nodes 1–2 = border 0, no marker; worst-case zigzag position still leaves the marker 115px inside the scroll viewport, so it can't clip.
- [x] **Node icons draw at full brightness and non-current nodes build no Panel at all.** The dark translucent disc behind every node was the ring Panel's `bg_color` (alpha 0.55) — now the Panel only exists for the current node and its background is fully transparent, so the ring is a bare outline. Fog-as-brightness (Decision 03: `Rough` 50% alpha, `Hidden` grey 32%) is **removed** — a greyed-out planet read as a wrecked one, and "destroyed" now has its own icon that has to be the only thing meaning that. Fog still shows in the title/subtitle (`- - -` / `UNKNOWN`). Verified live: current node = 1 panel + Antithesis marker + normal planet icon at `(1,1,1,1)`, every other node = 0 panels, and a cleared node = `planet destroyed icon.png` with no ring.
- [x] Cleared nodes fade back — `cell.modulate.a = CLEARED_DIM` (0.4) on the whole cell, so the icon and all three labels dim together and nothing added to the cell later can forget to dim with it. Travelled rail legs use the same `CLEARED_DIM` (was 0.85), so a finished stretch recedes as one piece instead of the line staying brighter than the planets it connects. Verified live: cleared cell alpha 0.40, its incoming link `COL_CLEARED @ 0.4`, current/locked cells 1.00 with their links untouched (0.55 / 0.25).
- [ ] **`Sprites/planet icon.png` is currently a byte-identical copy of `planet destroyed icon.png`** (same MD5, and it was written 84 min later) — a Krita export went to the wrong filename, so every un-cleared planet on the map draws the wreck. The code is correct; the asset is not. Original art survives as `planet icon.png~` and `planet icon.kra`. Re-export or restore from the backup.
- [x] Rail legs reworked. **All future legs share one colour and alpha** (`COL_LOCKED @ FUTURE_LINK_ALPHA` 0.6) — they used to fade with the arriving node's fog (0.55 non-hidden / 0.25 hidden), which made the far end of a 20-node system nearly invisible; fog belongs to the node, not the track. Brighter than before on both counts (was `COL_HIDDEN`, a much darker grey). **Every leg is dashed**, travelled or not — `Line2D` has no dash support, so `_make_dashed_link` returns a holder `Node2D` of short segments (`DASH_LENGTH` 14 / `DASH_GAP` 10) and `_make_link` returns `Node2D` rather than `Line2D`. Progress is carried by colour alone: dim green behind you, grey ahead. Verified live at node index 1: 0 solid legs of 13, leg→1 dashed ×11 `COL_CLEARED @ 0.4`, legs→2/3/4 dashed at `COL_LOCKED @ 0.6`.
- [ ] The marker is static — no bob, no drift. `_pulse_targets` already exists for the current ring if it should animate.
- [ ] Fog is now text-only on the map. If unscanned nodes need to read as unknown at a glance again, it wants a treatment that can't be confused with the destroyed icon (a silhouette/question-mark icon rather than a dimmed planet).
- [ ] The **J keypress itself is unverified** — synthetic input doesn't reach this environment's game window, so the test drove `StartWarp()` directly. The action and binding are in `project.godot`; needs one manual press to confirm.
- [ ] **The accessory pick is out of the loop.** Options are still generated by `CompleteStage()`, so putting a pick back is a routing change only — but right now there is no way to gain an accessory mid-run.
- [ ] Nothing signals the warp in-world — no VFX, no audio, no camera move. The 10s is currently only a number on a label.
- [ ] Clock expiry still does nothing (`ClockExpired` is recorded, nothing forced). Unchanged by this work, but the countdown is now front-and-centre on the HUD, so the lack of a consequence is more visible.

- [ ] Warpstation shop — warpstation nodes are currently auto-skipped (marked Cleared and stepped over) by `RunManager.SkipWarpstation()` and the loop in `CompleteStage()`. That's the one place to change when the shop exists.
- [ ] Timeout behaviour — clock expiry sets `ClockExpired` and surfaces on the map, but nothing is forced. The design log leaves the "forced-early sun fight at a defined disadvantage" mechanic as an open question.
- [ ] Real sun encounter — `LaunchCurrentNode()` currently plays the sun as a final planet on the Lava Walls biome with a doubled kill target (`PickSunBiome()`).
- [ ] Feed `EnemyDensityScale` / `Modifiers` into actual generation — both are carried through the descriptor and shown in the UI, but nothing consumes them yet.
- [ ] Currency / XP / Efficiency Bonus — reserved, labelled slots exist in `SolarMap.tscn`'s footer showing placeholder values.

### Danger level (2026-08-06)

- [x] Game-wide 1–10 threat scale on `SolarSystemDescriptor` (`MinDanger`/`MaxDanger`), with `PlantDanger` (11) **reserved and never generated** — THE PLANT is the only thing that will carry it. Danger belongs to the thing, not the screen, so systems/planets/enemies can all report on one scale.
- [x] `TierConfig.DangerLevel` — all three tiers are **1** on purpose. Hard is a longer system, not a nastier one; danger is a separate axis from tier. Changing it is a data edit in `Tiers`.
- [x] Danger Meter on `SolarSelect` — `Info/*Info/Danger` (Title / Meter / Readout) authored in the `.tscn`, segments built in `SolarSelect.gd` from `danger` + `danger_max` in the offer dict, so the UI never hardcodes 10. Verified live: 10 segments spanning the container, 1 lit green, "LEVEL 1 / 10" on all three panels.
- [ ] PLANT-level rendering — the meter has no special case for `PlantDanger` yet (deliberately deferred). Needs a distinct look, not an 11th segment.
- [ ] Per-node danger — `SystemNode` has no `DangerLevel`; planets currently carry only the cosmetic `DifficultyLabel`. That's the natural next step for "everything has a danger level", and where the `SolarMap` node cards would show it.
- [ ] Nothing consumes danger yet — it's displayed, not simulated. Same open state as `EnemyDensityScale`/`Modifiers`.

### Earlier (3-stage demo — historical)

- [x] Debug planet config menu (F3) — interim stand-in for manual planet configuration; still used for ad-hoc testing, no longer the only way to start a planet
- [x] `MainMenu.tscn` + `MainMenu.gd` — new `run/main_scene` (previously booted directly into `CubeLand.tscn`, no menu existed). "New Run" calls `RunManager.StartNewRun()`; "Quit".
- [x] `PlanetSelect.tscn` + `PlanetSelect.gd` — shows `RunManager.CurrentOptions` as 3 buttons (biome + cosmetic difficulty label); picking one calls `RunManager.ChooseOption(index)`. Also owns the temporary "3 planets cleared — boss coming soon" placeholder panel shown once `RunManager.RunComplete` is true, so the loop doesn't dead-end pending the real boss.
- [x] `RunManager.cs` singleton (autoload, registered after `Global`) — tracks `CurrentStageIndex` (0–2) through the 3 demo planet stages, generates 3 non-repeating-per-run biome options per stage (`StageOption`: biome, template, seed, cosmetic difficulty label), applies the chosen biome via `Global.ApplyPlanetParams` + `ChangeSceneToFile(CubeLand.tscn)`, and polls `Global.KillCount` each frame against a placeholder per-stage target (15/20/25) to auto-advance. `CurrentOptions` is a generic `List<StageOption>`, not a hardcoded 3-branch tuple — `CompleteStage()` is the only place assuming "next = index+1", so a real branching node-graph map can replace it later without touching the rest of `RunManager`'s public surface. See the 2026-07-02/03 roadmap artifact for the phased plan this came from.
- [x] Kill counter per planet fed into `RunManager` (Exploration win condition) — stage auto-advances on threshold
- [x] Survival timer per planet (`Global.RunTimer`) — exists and ticks, not yet wired as an alternate win condition
- [~] Planet selection screen (3 choices, difficulty shown) — `PlanetSelect.tscn`; built and working, then **SHELVED on 2026-07-30** at the user's call. Planets are now rolled randomly by `RunManager.GoToRandomPlanet()` and the accessory pick is the only between-planet decision. The scene, script, `CurrentOptions`, `GetOptionsForUI()`, `ChooseOption()` and `GenerateOptionsForStage()` are all left intact and correct — re-enabling means pointing `StartNewRun`/`ChooseAccessory` back at `PlanetSelect.tscn`. Explicitly framed as "for now", so this is reversible, not cancelled. Still not a visual branching map (see `demo_run_structure` decision).
- [ ] `PlanetDescriptor` class — full implementation; holds atmosphere (SkyColor, FogColor, FogDensity, AmbientLight) + gameplay modifiers (Gravity, EnemyDensity, EnemyHostility, enemy type tags). Currently `RunManager`'s difficulty label ("Easy"/"Medium"/"Hard") is **cosmetic only** — no gameplay effect — until this exists.
- [x] `AtmosphereSystem` — reads `Global.ActivePlanet.Biome` → `Biome_Registry` on scene `_Ready()`; applies fog color/density, background color, ambient light to `WorldEnvironment` (exponential fog mode). Node in `CubeLand.tscn` under `Game`. Will swap to reading `PlanetDescriptor.Atmosphere` once that class exists.
  - [x] **Cave-template planets get no directional light** (2026-08-06) — a sun underground reads as a hole in the ceiling that isn't there. `AtmosphereSystem` hides `../DirectionalLight3D` and switches ambient from the default **Bg** source to an explicit **Color** so it can be brightened past the background: `fog.Lerp(White, CaveAmbientLift)` at `CaveAmbientEnergy`. Both are `[Export]`s (0.45 / 1.8) since "bright enough" is an eyeball call. Non-cave planets are restored explicitly, not just left alone, so a cached `Environment` sub-resource can't carry a cave's lighting onto a field. Verified live: Crystal Caverns → `sunVisible=false, source=Color(2), energy=1.8`; Grassy Plains → `sunVisible=true, source=Bg(0), energy=1`.
  - [ ] Gated on `Template == "Cave"` only — **Abyss** keeps its sun, since it's a surface world with deep shafts rather than a roofed one. Revisit if abyss planets read as too bright at depth.
  - [ ] With no directional light, cave terrain is lit by flat ambient only, so block faces lose their directional shading and rely on SSAO for shape. If it reads too flat, the fix is a very dim non-shadowing `DirectionalLight3D` rather than raising ambient further.
- [ ] RunManager modifier system — framework for run-level modifiers applied on top of biome after `MakePlanetParams`; modifiers include: Low Gravity, Heavy Fog, Alien Surface, and others TBD. Deferred past the demo.
- [ ] RunManager modifier: **Alien Surface** — weighted table across all registered blocks; overrides the biome's natural surface block pick; makes familiar biomes feel visually alien on certain runs. Deferred past the demo.
- [ ] Planet map HUD visible during run — not needed while the demo is select-screen-only (no persistent map to show)
- [x] Post-planet upgrade screen (choose 1 of 3 accessories) — now `Scenes/LoadingScreen.tscn`/`.gd`, shown after every `RunManager.CompleteStage()`. Picks from accessories not yet equipped, falls back to full pool if <3 remain; choosing one calls `ChooseAccessory()` which equips it and launches the next random planet. Supersedes `Scenes/UpgradeSelect.tscn`/`.gd`, which are now orphaned and safe to delete.
- [x] Inter-planet loading screen — `Scenes/LoadingScreen.tscn`, a looping 2-frame `AnimatedSprite2D` (`Sprites/loading frame 1|2.png`, 2 fps) of the ship leaving a destroyed planet for the next one, with the accessory pick over it. Purely an interstitial: it does **not** gate on real chunk generation, which still happens inside `CubeLand` after the scene change. Also owns the "3 planets cleared — boss coming soon" panel that `PlanetSelect.tscn` used to hold.
- [ ] Boss encounter trigger — replaces the current "3 planets cleared" placeholder panel in `LoadingScreen.tscn`
- [x] Run lose-state wiring — `Player.Die()` on jump-press now calls `RunManager.EndRun()` (resets stage index, `RunComplete`, used-biomes, and `Global.EquippedAccessoryIds`) and goes to `MainMenu.tscn`, instead of reloading the current planet in place. `LoadingScreen.gd`'s "Return to Main Menu" button (the win path) calls the same `EndRun()` first. `RunManager.RunComplete` is still a placeholder flag — no real win screen yet, just the "3 planets cleared" panel.

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
- [x] Hitstop freezes all GPU particles automatically — `Global` auto-registers every `GpuParticles3D` entering the tree into the `hitstop_particles` group via `SceneTree.NodeAdded`, and sweeps `SpeedScale` 0/restore on the hitstop start/end edges only. Zero per-effect wiring for any future effect. Replaces the old `Emitting = false` gating in `Enemy._Process`, which only stopped new spawns and let in-flight particles fly through the freeze. Authored speeds preserved via a `hitstop_base_speed` meta.
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
