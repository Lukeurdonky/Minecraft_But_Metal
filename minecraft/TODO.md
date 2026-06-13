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
- [x] Player death / run-end state — SIGNAL LOST screen, jump to reload scene
- [ ] Kill counter per planet (Exploration win condition)
- [ ] Survival timer (Survival win condition)
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
  - [ ] Assign scenes to EnemySpawner once models are built
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

- [ ] `PlanetChunksX` / `PlanetChunksZ` constants in `Global.cs`; derive `PlanetWidth` / `PlanetDepth` from them (never hardcode block counts)
- [ ] At startup, hard-clamp: `PlanetChunksX = max(PlanetChunksX, RenderDistanceChunks * 2 + 1)` (same for Z); print warning if clamped
- [ ] Canonical coord utilities in `Global.cs`: `CanonicalBlockX`, `CanonicalBlockZ`, `CanonicalChunkX`, `CanonicalChunkZ`
- [ ] Split `Chunk_Manager` into `_canonicalData` (canonical coord → block array + dirty flag + spawn descriptors, permanent per run) and `_activeNodes` (raw physical coord → scene node, freed on unload)
- [ ] `ChunkData.Dirty` flag — set on any block modification; dirty chunks never regenerate from `World_Generator`, existing block array rebuilds the mesh directly
- [ ] Apply canonical mapping in all block read/write paths (`get_block`, `break_block`, `damage_block`, `explode`)
- [ ] Physical chunk node positioned at raw world offset (`rawChunkX * 16`) — same canonical data can render at x=16 and x=1040 on separate visits without any position tricks

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

- [ ] Wire `World_Generator.cs` into `Chunk_Manager` (replace raw FastNoise2D)
- [ ] `TerrainStage` — planet surface height map
- [ ] `CaveStage` — cave carving
- [ ] `FeatureStage` — enemy spawn markers, points of interest
- [ ] Finite planet-shaped world (not infinite flat terrain)
- [ ] Per-planet gravity setting
- [ ] Difficulty modifiers (terrain hostility, enemy density)
- [ ] Underground depth zones (Underground Forest −10 to −300, Purple Crystal −310 to −600)

---

## Run Structure

- [ ] Planet selection screen (3 choices, difficulty shown)
- [ ] Planet map HUD visible during run
- [ ] Post-planet upgrade screen (choose 1 of 3)
- [ ] Boss encounter trigger
- [ ] Run win / lose states

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

## Accessories (all from NEW_VISION.md)

- [ ] Accessory slot system (equip before run or on pickup)
- [ ] Super Jump
- [ ] Super Slam — amplify jackhammer ground slam radius/damage
- [ ] Explosive Bounce — jackhammer release creates explosion at impact
- [x] Destructive Laser — laser already destroys blocks via `explode()` tunneling (base behavior, not an accessory upgrade)
- [ ] Little Friend
- [ ] Glide — slow fall while holding jump in air
- [ ] Dig Dig Dig! — jackhammer mines blocks faster
- [ ] Flaming Grapple — fire applied on grapple pull/lunge
- [ ] Tech Vision — enemy highlight through walls
- [ ] Exo Suit — mobility buffs (dash speed/cooldown)

---

## Polish & Atmosphere

- [ ] Antithesis aesthetic — dark terrain palette, bright electronic enemy materials
- [ ] Crashlanding entry sequence (player enters planet via crash)
- [ ] Sound effects — weapons, enemies, environment
- [x] Player hit feedback — red full-screen flash (0.4s fade) + global camera shake on damage
- [ ] Particles — laser impact, explosion, enemy death, dash trail

---

## Tech Debt / Cleanup

- [x] Block damage overlay FIFO eviction — oldest tracked block evicted (health + visual) when cap (1500) is hit; `LinkedList` + node pointer for O(1) removal
- [ ] Delete or archive `Washed Code/` once nothing left to salvage
- [ ] Remove `dummy.gd`, `portal.gd` from root (unused)
- [ ] `Mob_Registry.cs` — repurpose for enemy definitions or remove
- [ ] Step-up traversal (`AttemptStepUp`) — re-evaluate once enemy AI is in and terrain is final
- [ ] `drop_item` input action in project.godot — remove or repurpose

---
