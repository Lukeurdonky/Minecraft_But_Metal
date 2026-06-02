# Antithesis Conquering Simulator — Project State

> You're in your spaceship. You go to randomly generated planets. You kill things.

A voxel-based action roguelike built in Godot 4 (C# + GDScript). Each run sends the player across a sequence of procedurally generated planets to clear combat encounters, collect upgrades, and reach a boss. No crafting. No base-building. Pure destructive capability.

---

## Design Pillars

- **All combat, no filler** — every system exists to make fights better
- **Fun over narrative** — mechanics first, story never
- **Still descending** — the world goes deep; exploration is vertical
- **Darker palette + bright electronic enemies** — Antithesis aesthetic
- **Destructible world** — the voxel foundation is a feature, not a legacy

---

## Game Loop

```
Choose 1 of 3 planets → Difficulty (Easy / Medium / Hard) → Clear / Survive → Upgrades → Next planet → Boss
```

- Planet map visible throughout the run
- Demo target: one full map (one planet)
- Future: multiple bosses, per-galaxy theming, an actual ending

### Win Conditions per Planet

| Type | Description |
|---|---|
| **Exploration** | Kill X enemies across caves and the surface |
| **Survival** | Survive X minutes against a massive creature or swarm |
| **Combat** | Kill everything — or reach the core |

---

## Architecture Overview

The existing voxel engine carries over almost entirely. What changes is everything above the world layer — combat, player abilities, enemy AI, run structure, and progression.

```
Scripts/
├── Handlers/       — Global singleton, debug HUD, scene transitions
├── The World/      — Chunk system, block definitions, world generator  ← KEEP, extend
│   └── Generation/ — Staged generation pipeline                        ← FILL IN for planets
├── Entities/       — Entity base, Player, Creature, Item, Projectile  ← OVERHAUL
│   ├── Player/     — Controller, abilities, interactions               ← MAJOR CHANGES
│   └── Item Behaviors/ — Replace with Weapon/Ability system           ← REPLACE
└── Datasets/       — Registries                                        ← REPURPOSE
```

**What carries over:**
- 16×16×16 chunk system with threaded generation and mesh building
- Manual AABB collision against voxel data
- Block damage, explosion system, and damage overlay shader
- Entity base class (health, physics, ground detection)
- Raycast interaction system

**What gets replaced or heavily modified:**
- Minecraft-style 36-slot inventory → compact upgrade/accessory loadout
- Item behavior system → Weapon + Ability system (Laser Arm, Grapple, Wings)
- Passive creature AI → Aggressive enemy AI with varied behaviors
- Flat terrain generation → Planet-shaped destructible terrain with caves

---

## What's Implemented (Inherited from Minecraft Base)

### World & Rendering
- 16×16×16 voxel chunks with threaded generation and mesh building
- Greedy visibility culling — only exposed faces meshed
- Sphere/cylinder render distance modes
- Chunk lifecycle — generation, loading, unloading, cold-chunk eviction
- Block texture atlas (12×8) with per-face UV mapping
- Occlusion culling enabled
- Block damage overlay — MultiMesh + `BlockDamage.gdshader`, up to 1500 damaged blocks simultaneously
- **Explosion system** — radius-based block destruction with damage falloff ← directly useful for combat

### Player
- WASD movement, mouse-look FPS camera, jump, sprint
- Spectator/no-clip mode for testing
- DDA raycast for targeting
- First-person hand mesh

### Entities
- `Entity.cs` — health, AABB physics, ground detection, fall cap
- `Creature.cs` — detects player at 15 units, chases (placeholder AI)
- `Item.cs` — world-dropped pickups with gravity and pursuit behavior

### World Generation
- FastNoise2D height terrain (bypasses the staged generator currently)
- Abyss layer system in `Global.cs` — Surface, Underground Forest (−10 to −300), Purple Crystal Area (−310 to −600)
- 5-stage generation pipeline architecture (`World_Generator.cs`) — all stages empty, ready to implement

---

## What Needs to Be Built

### Player Abilities (Core — build these first)
- **Laser Arm** — primary attack; major beam shot + jackhammer bounce mode
- **Mech Wings** — double jump + directional dash
- **Flexible Left Arm** — grappling hook; attaches to walls and enemies, release to lunge

### Accessories (Equippable modifiers)
1. Super Jump
2. Super Slam
3. Explosive Bounce
4. Destructive Laser
5. Little Friend
6. Glide
7. Dig Dig Dig!
8. Flaming Grapple *(fire on pull)*
9. Tech Vision
10. Exo Suit *(mobility)*

### Combat Systems
- Enemy damage, knockback, and death
- Player damage reception and feedback
- Enemy variety with distinct behaviors (swarm, heavy, boss)
- Combat win condition tracking (kill counter, timer, core reach)

### Run Structure
- Planet selection screen (3 choices, difficulty visible)
- Planet map HUD visible during run
- Post-planet upgrade screen
- Boss encounter trigger

### World Generation (Planet-specific)
- Surface and cave generation wired into `World_Generator.cs` stages
- Planet-shaped terrain (not infinite flat world)
- Enemy spawn points tied to terrain features
- Difficulty modifiers affecting terrain hostility and enemy density

### World Customization Parameters
- Enemy hostility level
- Environment hostility
- World modifiers
- Gravity (per planet)

---

## Notable Code Locations

| Thing | Location |
|---|---|
| Chunk generation & mesh building | `Scripts/The World/Chunk_Manager.cs` |
| World generator pipeline (empty stages) | `Scripts/The World/Generation/World_Generator.cs` |
| Block/item registries | `Scripts/Datasets/Block_Registry.cs`, `Item_Registry.cs` |
| Player controller | `Scripts/Entities/Player/Player.cs` |
| Block raycast + interaction | `Scripts/Entities/Player/interactions.gd` |
| Global constants & depth layers | `Scripts/Handlers/Global.cs` |
| Entity base physics | `Scripts/Entities/Entity.cs` |
| Explosion system | `Scripts/The World/Chunk_Manager.cs` → `explode()` |
