# TODO

---

## Immediate / In Progress

- [x] Create `Assets/GrappleHook.tscn` and assign to Player node's `GrappleHookScene` export
- [x] Grapple rope — cylinder mesh in SubViewport using tentacle material
- [ ] Verify Creature collision layer is on Layer 2 so GrappleHook's Area3D can detect them
- [ ] Entity grapple feel — tune `LightEntityYBoost` (8f) and `LightEntityReelSpeed` (20f)

---

## Player Abilities — Polish

- [ ] Laser VFX — visible beam while `LaserActive` is true (use `LaserTimer` / `LaserCooldown`)
- [ ] Jackhammer charge feedback — visual or audio cue as `JackhammerCharge` builds
- [ ] Dash trail / directional feedback
- [ ] Ability cooldown HUD (laser recharge bar, dash cooldown indicator)
- [ ] Speed threshold VFX — particles or screen shake when bulldozing terrain at high speed

---

## Combat

- [ ] Enemy takes damage and dies
- [ ] Player takes damage, has health bar UI
- [ ] Knockback on hit (use `TakeDamage(amount, knockbackVector)` — already on Entity)
- [ ] Player death / run-end state
- [ ] Kill counter per planet (Exploration win condition)
- [ ] Survival timer (Survival win condition)
- [ ] "Reach the core" objective (Combat win condition)

---

## Enemies

- [ ] At least 3 distinct enemy types (swarm, heavy, ranged)
- [ ] Improve Creature.cs AI — attack behavior, not just chase
- [ ] Mark some creatures as `heavy = true` (pulled toward instead of reeled in when grappled)
- [ ] Enemy spawning system (tied to terrain + difficulty)
- [ ] Enemy drops (upgrade currency)
- [ ] Boss enemy with health bar

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

## Accessories (all from NEW_VISION.md)

- [ ] Accessory slot system (equip before run or on pickup)
- [ ] Super Jump
- [ ] Super Slam — amplify jackhammer ground slam radius/damage
- [ ] Explosive Bounce — jackhammer release creates explosion at impact
- [ ] Destructive Laser — laser also destroys blocks
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
- [ ] Player hit feedback — screen flash, shake on damage
- [ ] Particles — laser impact, explosion, enemy death, dash trail

---

## Tech Debt / Cleanup

- [ ] Delete or archive `Washed Code/` once nothing left to salvage
- [ ] Remove `dummy.gd`, `portal.gd` from root (unused)
- [ ] `Mob_Registry.cs` — repurpose for enemy definitions or remove
- [ ] Step-up traversal (`AttemptStepUp`) — re-evaluate once enemy AI is in and terrain is final
- [ ] `drop_item` input action in project.godot — remove or repurpose

---
