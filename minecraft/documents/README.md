# Documents Index

All project documentation lives here, sorted by category. `CLAUDE.md` (repo root) is the only doc kept outside this folder — it must stay at the project root for tooling to find it.

| Folder | Contents |
|---|---|
| [`design/`](design/) | Identity, lore, and the original design vision. Read before any decision touching how the game looks, sounds, feels, or what it's about. |
| [`engineering/`](engineering/) | Living design specs for systems still being built (world generation, chunk wrapping). These describe target architecture, not necessarily 100% current code — check the "Status"/"Implementation Order" sections in each for what's actually wired up. |
| [`performance/`](performance/) | Optimization history — root-cause analysis and fixes, in chronological order. |
| [`project/`](project/) | Current state and task tracking: what's built, what's next, session startup steps. |

## Design (`design/`)

- **NEW_VISION.md** — design pillars, game loop, accessories, win conditions. The active design doc for *this* game.
- **ANTITHESIS.md** — character identity, visual style, sound, aesthetic principles for the player character and world.
- **COSMOS_LORE.md** — narrative archive for the Cosmos universe this game extends from. Reference for THE PLANT boss and tonal identity.
- **ORIGIN.md** — background on Cosmos Enterprises and why this project exists in its current form. Historical context, not active direction.

## Engineering (`engineering/`)

- **generation_plan.md** — planet generation architecture: `PlanetParams`, `PlanetDescriptor`, `BiomeDescriptor`, the stage pipeline (Terrain/Cave/Chasm/Feature), and the 9-biome system. Has its own "What is and isn't built" table.
- **chunk_wrapping_design.md** — how the finite, wrapping planet works (canonical vs. raw coordinates, the one-node guarantee, enemy spawn/despawn on chunk load, boss persistence). Most of the "Wrapping" and "Enemy Spawning" checklists here are now implemented — see `project/TODO.md`'s "World Wrapping" section for the live checklist.

## Performance (`performance/`)

- **PERFORMANCE.md** — the original chunk-pipeline optimization pass (thread pools, array pre-allocation, promotion throttling, mesh-merging analysis). Most "Quick Wins" are done; enemy LOD and greedy meshing remain open.
- **PERFORMANCE_REWORK_FINDINGS.md** — follow-up gaps found after the first pass (all-air chunk fast path, RD³ sweep cost) plus the damage-overlay rework (lazy/sparse per-chunk storage, slot-based incremental MultiMesh updates, dynamic capacity growth, free-priority flush ordering). Read this one for the most recent state.

## Project (`project/`)

- **PROGRESS.md** — current state snapshot: what's implemented, what's not started, architecture overview.
- **TODO.md** — actionable checklist, organized by system. More granular than PROGRESS.md and updated more often.
- **STARTUP.md** — two steps to resume a session with the `godot-ai` MCP server.
- **HANDOFF.md** — run when ending a session: update docs/TODO to reflect the session's work, then shut down the `godot-ai` MCP server.
