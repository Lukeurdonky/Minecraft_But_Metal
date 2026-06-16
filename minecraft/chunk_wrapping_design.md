# Chunk Wrapping & Enemy Handling Design Document
### Antithesis Conquering Simulator

---

## Overview

The planet is a finite horizontal world that wraps at its edges — when the player exits one side, they re-enter from the opposite side. Vertically, the world has a real top and bottom (sky ceiling, abyss floor). Only horizontal axes wrap.

The planet size is defined in **chunks**, not blocks. Because chunks are 16×16 blocks, the planet dimensions must be a multiple of 16 to guarantee clean alignment at the wrap seam — no partial chunks, no edge-case generation. Planet size is set via `PlanetChunksX` and `PlanetChunksZ` in `Global.cs`; block dimensions derive from these (`PlanetWidth = PlanetChunksX * 16`).

**Reference sizes:**

| Chunks per side | Blocks per side | Notes |
|---|---|---|
| 32 | 512 | Small — loops fast, tight combat density |
| 64 | 1,024 | Medium — recommended starting point |
| 128 | 2,048 | Large — likely more than a run needs |

The exact value is a playtesting decision. The system works identically at any chunk-aligned size.

This is implemented by **tiling**: the chunk manager generates the same `N×N` chunk area repeatedly in all horizontal directions. The player physically moves through these repeated tiles, but the terrain they see is always identical — the same world, looped. From the player's perspective, the planet feels round.

This document covers how wrapping is implemented at the chunk level and how enemies are handled within that system.

---

## How Wrapping Works

### World Coordinate Clamping

The planet's canonical world space spans `PlanetChunksX × 16` blocks on X and `PlanetChunksZ × 16` blocks on Z. `PlanetWidth` and `PlanetDepth` are derived constants — never hardcoded:

```csharp
// Global.cs
public const int PlanetChunksX = 64;   // Tune this
public const int PlanetChunksZ = 64;   // Tune this
public const int ChunkSize     = 16;
public const int PlanetWidth   = PlanetChunksX * ChunkSize;  // 1024
public const int PlanetDepth   = PlanetChunksZ * ChunkSize;  // 1024
```

Any world X/Z coordinate outside the canonical range is wrapped back into it:

```csharp
// Block space
canonicalX = ((worldX % PlanetWidth)  + PlanetWidth)  % PlanetWidth;
canonicalZ = ((worldZ % PlanetDepth)  + PlanetDepth)  % PlanetDepth;

// Chunk space
canonicalChunkX = ((chunkX % PlanetChunksX) + PlanetChunksX) % PlanetChunksX;
canonicalChunkZ = ((chunkZ % PlanetChunksZ) + PlanetChunksZ) % PlanetChunksZ;
```

The double-modulo handles negative coordinates correctly in C#. Block-space and chunk-space wrapping are separate operations — use the right one for the right unit.

This clamping is applied in two places:
- **Block lookup** (`get_block`) — canonical block coords used for the actual data fetch, in block space
- **Chunk key mapping** — chunk dictionary keyed by canonical chunk coord, in chunk space

### Chunk Data vs. Chunk Node — Two Separate Dictionaries

The chunk manager maintains **two separate structures**:

```
Dictionary<Vector3I, ChunkData>  _canonicalData   // keyed by canonical coord
Dictionary<Vector3I, ChunkNode>  _activeNodes      // keyed by raw physical chunk coord
```

**`ChunkData`** — the block array, damage state, spawn descriptors, and a `Dirty` flag. Lives at canonical coords. Persists for the lifetime of the run (never freed on node unload — it's cheap memory). Never regenerated from scratch once `Dirty = true`.

**`ChunkNode`** — the actual Godot `MeshInstance3D` + collision node. Lives at raw physical chunk coordinates (wherever in the player's render window it needs to be). Freed when it exits the render window.

### The One-Node Guarantee

As long as `PlanetChunksX > RenderDistanceChunks * 2` (same for Z), the render window can never span the full planet width. This means a given canonical chunk will **never have two active physical nodes simultaneously** — the seam duplicate case does not exist in practice. The planet size constraint is enforced at startup; the recommended 64-chunk minimum easily satisfies any reasonable render distance.

This eliminates all dual-node remesh complexity. Block modifications always affect exactly one active physical node.

### Chunk Key Mapping

A chunk node at raw world chunk `(70, 2, 3)` on a 64-chunk-wide planet fetches its data from canonical `(70 % 64, 2, 3 % 64)` = `(6, 2, 3)`. All block reads, writes, generation, and mesh building use the canonical data — never the raw coord.

When the render window straddles the seam (player near `x = PlanetWidth`), physical nodes at raw positions `64, 65, 66...` are spawned and wrap to canonical `0, 1, 2...`. The nodes on the other side (`62, 63`) have already been unloaded before the new ones come into range, guaranteed by the planet size constraint.

### Per-Planet NoiseScale

`NoiseScale` on `Chunk_Manager` controls terrain feature size:

```
featureBlocks ≈ PlanetWidth / (2π × NoiseScale)
```

Because `PlanetWidth` is baked into the torus mapping at generation time, a fixed `NoiseScale` produces features that are proportional to planet size — a 32-chunk planet gets smaller features than a 128-chunk planet with the same value. When planet creation sets a new planet size, it should also set `NoiseScale` to keep feature density consistent:

```csharp
// Target a specific feature size regardless of planet width
ChunkManager.NoiseScale = Global.PlanetWidth / (2f * Mathf.Pi * targetFeatureBlocks);
```

Typical reference values for a 1024-block (64-chunk) planet:

| NoiseScale | Feature size |
|---|---|
| 1.0 | ~163 blocks — sweeping, open |
| 1.5 | ~108 blocks — default terrain |
| 3.0 | ~54 blocks — dense, varied |
| 6.0 | ~27 blocks — tight caves |

### Dirty Chunk Reload

A dirty canonical chunk is **never regenerated from the WorldGenerator pipeline** on reload. If `ChunkData` exists (dirty or not), its existing block array is used directly to rebuild the mesh. Fresh generation only runs when no `ChunkData` exists yet for that canonical coord.

```csharp
// Pseudocode for chunk load
ChunkData data;
if (!_canonicalData.TryGetValue(canonicalPos, out data))
{
    data = WorldGenerator.Generate(canonicalPos);   // first time only
    _canonicalData[canonicalPos] = data;
}
BuildMesh(physicalNode, data);   // always from current canonical data
```

### Mesh Origin Offset

Physical chunk nodes are placed at their raw world position (`rawChunkX * 16, y, rawChunkZ * 16`). The block data is canonical, the mesh renders at the physical location. A chunk that wraps gets a mesh origin at `PlanetWidth + offset`, placing it seamlessly in the render window.

### No Teleportation — Infinite Tiling

**Nothing is ever teleported.** The player and all entities move through raw world space without clamping or position resets. The raw X/Z coordinate grows (or shrinks) freely as the player moves.

The loop is entirely an illusion created by generation. When the player walks far enough that a new chunk needs to load at raw position `(rawChunkX, y, rawChunkZ)`, the chunk manager canonicalizes that coord and fetches `_canonicalData`. If the data is dirty (player already broke blocks there), the saved block array is used — placed at the new raw offset so the damage is visible from the other side of the planet. If the data is clean, it regenerates fresh from the same seed, producing identical terrain. From the player's perspective the world loops. Nothing moved.

This also means **no `WrapPosition()` on entities**, **no `WrappedDelta()` for AI**, and no GrappleHook clamping. Enemy AI always works in raw world space. Enemies are spawned fresh near the player's current raw position on every chunk load, so AI vectors to the player are always short — the long-way-around problem doesn't exist.

### Vertical Behavior

Y does **not** wrap. The world has:
- A hard sky ceiling (no generation above a set height)
- An abyss floor (the existing `AbyssStrength` system handles this — blocks near the bottom are consumed by the void)

The planet is a cylinder in practice: round horizontally, bounded vertically.

---

## Enemy Handling

### Design Decision: No Persistence for Regular Enemies

Regular enemies **do not persist** across chunk unloads. When a chunk unloads, its enemies are freed. When that chunk reloads, the spawner generates fresh enemies at the positions defined by the FeatureStage.

**Why this is correct:**
- Enemies are manufactured by the planet — the world is hostile by nature, not by individual creature memory
- It prevents cheese: the player cannot kite enemies out of range to reset them strategically, because they'd just respawn fresh anyway
- Memory stays flat regardless of how much the player explores
- It's consistent with the roguelite tone — the planet doesn't remember you, it just keeps producing threats

The one exception is the boss — covered at the end of this document.

### Enemy Spawning via FeatureStage

Enemy spawn points are defined at world generation time by the `FeatureStage` in `World_Generator.cs`. Each chunk's feature data includes a list of spawn descriptors:

```csharp
public struct EnemySpawnDescriptor
{
    public Vector3I LocalPosition;  // Block position within the chunk
    public EnemyType Type;          // Swarm, Heavy, Ranged
}
```

This data is generated once per canonical chunk coordinate (same seed = same spawn layout every time). It is stored on `ChunkData` as `List<EnemySpawnDescriptor> SpawnDescriptors` — not on the physical chunk node, which gets freed on unload. `ChunkData` persists for the run, so descriptors are generated once and survive all subsequent load/unload cycles.

When a chunk loads, `EnemySpawner` reads its `SpawnDescriptors` and instantiates the appropriate enemy nodes at those world positions.

### Enemy Positions in Raw World Space

Enemies are spawned at the raw world position of their chunk at load time and move freely in raw world space — no clamping, no wrapping. Because enemies are freed on chunk unload and respawned fresh on reload, they always appear near the player's current raw position. The raw delta from enemy to player is always short. No `WrapPosition()` needed.

### Enemy–Chunk Ownership

Each enemy node tracks which canonical chunk coordinate spawned it:

```csharp
public Vector3I OwnerChunkPos;  // Set on spawn, canonical coords
```

The chunk manager's unload routine checks all live enemies and frees those whose `OwnerChunkPos` matches the unloading chunk. Enemies do not need to register/deregister themselves — the chunk manager sweeps them on unload.

### Enemy AI

All AI operates in raw world space with plain vector subtraction. No `WrappedDelta()` needed — enemies are always spawned within their chunk's load range, so they're never far from the player in raw coords.

---

## Boss Exception

The boss is the single entity that **does** persist across chunk unloads. Its state lives on the run manager, not on any chunk.

### BossState

```csharp
public struct BossState
{
    public Vector3 WorldPosition;  // Canonical, frozen on unload
    public float CurrentHealth;    // Written immediately on every hit
    public int PhaseIndex;         // Written immediately on phase change
    public bool HasBeenEngaged;    // Latches true, never resets
}
```

`RunManager` holds one `BossState?` (nullable). Null = boss not yet spawned this run.

### Chunk Load/Unload Behavior

**On boss chunk unload:**
- Boss node serializes current position, health, and phase into `BossState`
- Boss node is freed normally
- AI stops — the node is gone

**On boss chunk load:**
- Chunk manager calls `RunManager.OnBossChunkLoaded()`
- If `BossState` exists: spawn boss node, hydrate from saved state, resume from start of current phase
- If no `BossState`: first spawn — create state with full health, phase 0, `HasBeenEngaged = false`

Animation and attack state are **not** saved. The boss always resumes from the beginning of its current phase — clean, never mid-action.

### Boss Position Wrapping

The boss arena is at a fixed canonical world position. If the boss moves (during combat) and the player leaves range, the boss's position is saved as-is. On reload the boss is at wherever it was standing. It does not reset to the arena center between engagements.

The boss also applies `WrapPosition()` identically to regular entities.

### Engagement

`HasBeenEngaged` latches true the first time the player enters the arena radius. After that, the boss AI activates immediately on every chunk load — it does not idle again. A wounded boss that lost the player and respawned comes back fighting.

---

## Implementation Checklist

**Wrapping:**
- [ ] `PlanetChunksX` / `PlanetChunksZ` constants in `Global.cs` (multiples of 1, derive `PlanetWidth` / `PlanetDepth` — never hardcode block counts)
- [ ] At startup, hard-clamp: `PlanetChunksX = max(PlanetChunksX, RenderDistanceChunks * 2 + 1)` (same for Z) — print warning if clamped
- [ ] Canonical coord utilities in `Global.cs`: `CanonicalBlockX(int x)`, `CanonicalBlockZ(int z)`, `CanonicalChunkX(int cx)`, `CanonicalChunkZ(int cz)`
- [ ] Split chunk manager into `_canonicalData` (keyed by canonical coord, permanent this run) and `_activeNodes` (keyed by raw physical coord, freed on unload)
- [ ] `ChunkData.Dirty` flag — set on any block modification; prevents fresh regeneration on reload
- [ ] Apply canonical mapping in `get_block` and all block read/write paths (`break_block`, `damage_block`, `explode`)
- [ ] Physical chunk node positioned at raw world offset (`rawChunkX * 16`) — mesh at physical location, data from canonical
- [ ] No position clamping on player, entities, or GrappleHook — everything stays in raw world space

**Enemy Spawning:**
- [ ] `EnemySpawnDescriptor` struct
- [ ] `SpawnDescriptors` list on `ChunkData` (not on the physical node — `ChunkData` persists, the node does not)
- [ ] FeatureStage populates `SpawnDescriptors` using canonical chunk seed
- [ ] `EnemySpawner` reads descriptors on chunk load, instantiates nodes at raw world position
- [ ] `OwnerChunkPos` on `Entity.cs` stores the **raw** chunk coord of the chunk that spawned it — unload sweep compares directly against the unloading node's raw coord, no canonicalization needed
- [ ] Chunk manager sweeps and frees owned enemies on chunk unload

**Boss:**
- [ ] `BossState` struct
- [ ] `BossState?` + arena position on `RunManager`
- [ ] `RunManager.OnBossChunkLoaded()` hook
- [ ] Boss node serializes to `BossState` on tree exit
- [ ] Boss node hydrates from `BossState` on spawn
- [ ] `HasBeenEngaged` engagement zone check
- [ ] Arena position blocked clear in `FeatureStage`
