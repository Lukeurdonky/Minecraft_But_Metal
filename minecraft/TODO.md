# TODO

---

## Player Abilities — Build These First

- [ ] **Laser Arm** — primary beam attack, hitscan or projectile
- [ ] **Laser Arm** — jackhammer bounce mode (secondary fire)
- [ ] **Mech Wings** — double jump
- [ ] **Mech Wings** — directional dash (air + ground)
- [ ] **Flexible Left Arm** — grappling hook, attach to walls
- [ ] **Flexible Left Arm** — attach to enemies, release to lunge toward target

---

## Combat

- [ ] Enemy takes damage and dies
- [ ] Player takes damage and has health
- [ ] Knockback on hit (player and enemies)
- [ ] Player death / run-end state
- [ ] Kill counter per planet (for Exploration win condition)
- [ ] Survival timer (for Survival win condition)
- [ ] "Reach the core" objective marker (for Combat win condition)
- [ ] Win condition detection and transition

---

## Enemies

- [ ] Enemy variety — define at least 3 distinct types (swarm, heavy, ranged)
- [ ] Improve AI — idle wander, aggro on sight/sound, attack patterns
- [ ] Enemy spawning system tied to terrain and difficulty
- [ ] Enemy drops (upgrade currency or pickups)
- [ ] Boss enemy — unique behavior, health bar UI

---

## Run Structure

- [ ] Planet selection screen (3 random choices, difficulty shown)
- [ ] Planet map HUD visible during run
- [ ] Post-planet upgrade screen (choose 1 of 3 upgrades)
- [ ] Run end state — win (cleared boss) and lose (player death)
- [ ] Boss encounter trigger (end of run or zone)

---

## Accessories

- [ ] Accessory slot system (equip/swap before run or on pickup)
- [ ] Super Jump
- [ ] Super Slam
- [ ] Explosive Bounce
- [ ] Destructive Laser
- [ ] Little Friend
- [ ] Glide
- [ ] Dig Dig Dig!
- [ ] Flaming Grapple *(fire applied on grapple pull)*
- [ ] Tech Vision
- [ ] Exo Suit *(mobility buffs)*

---

## World Generation

- [ ] Wire `World_Generator.cs` into `Chunk_Manager` (replace raw FastNoise2D calls)
- [ ] `TerrainStage` — planet surface height map
- [ ] `CaveStage` — cave carving (3D noise or worm algorithm)
- [ ] `FeatureStage` — enemy spawn markers, points of interest
- [ ] Planet-shaped finite world (not infinite flat terrain)
- [ ] Per-planet gravity setting (hook into `Global.cs` or planet config)
- [ ] Difficulty modifiers affecting terrain density and enemy count
- [ ] Underground depth zones matching existing abyss layer definitions

---

## World Customization (Planet Parameters)

- [ ] Enemy hostility parameter
- [ ] Environment hostility parameter (lava, toxic gas, collapse zones)
- [ ] World modifier system (low gravity, darkness, etc.)
- [ ] Gravity parameter per planet

---

## Polish & Atmosphere

- [ ] Antithesis aesthetic pass — dark palette, bright electronic enemy materials
- [ ] Crashlanding entry sequence (player enters planet via crash)
- [ ] Sound effects — weapons, enemies, environment
- [ ] Player feedback — hit flash, screen shake on damage
- [ ] Particle effects — laser impact, explosion, enemy death

---

## Tech Debt / Cleanup

- [ ] Remove or archive Minecraft-specific inventory UI (36-slot hotbar)
- [ ] Replace `Item_Registry` / `IItemBehavior` with weapon/ability system
- [ ] Delete or archive `Washed Code/` once nothing is being salvaged
- [ ] Remove `dummy.gd`, `portal.gd` from root (unused)
- [ ] `Mob_Registry.cs` — repurpose for enemy definitions or remove

---

