# Performance Optimization Plan

Root cause summary: at render distance 15 the chunk manager is handling ~14,147 visible chunks vs ~331 at the default of 5. That's 43× the geometry, fed through a single mesh-building thread with no LOD, no batching, and arrays that resize during the build loop. The lag spikes are the main thread waiting on a mesh queue that can never drain.

---

## Status (implemented)

Loading throughput and per-frame main-thread work were the felt bottlenecks, not GPU draw cost. Done so far, in the order found:

- **#1 mesh thread pool** — was 1 thread, now a pool sized to core count.
- **#2 mesh arrays** — sized to the realistic worst case (8192 verts); no resizing in the hot loop. **`[ThreadStatic]` (reused per thread), NOT per-call locals**: at 8192 verts the vertex/normal arrays are ~98 KB, over the .NET Large Object Heap threshold (85 KB). Per-call allocation churns the LOH and causes steadily-worsening frame times the longer you travel. Reused thread-static buffers = zero per-build allocation, still thread-safe.
- **Generation thread pool** (not in original plan) — generation, not meshing, was the real loading bottleneck: `create_chunk_data` does 8000+ 4D-simplex evals per chunk on what was a single thread feeding the mesh pool. Now pooled, sized `Clamp((cores-2)/2, 2, 4)`. `Simplex4D.Sample` is pure/stateless so concurrent calls are safe.
- **#8 neighbor-ref caching** — the 6 face-neighbor voxel arrays are resolved once per chunk instead of a `get_block` dictionary lookup per edge block.
- **Promotion via drain queue, not `CallDeferred`** — the per-chunk `CallDeferred("load_ready_chunk")` handoff was leaving mesh buffers stranded under heavy multi-threaded bursts (thousands of orphaned `pendingBuffers` pinning memory → GC-driven decay, and a re-mesh loop where chunks never reached `Loaded`). Now `MeshBuffers` carries its own counts and worker threads enqueue positions into `_readyToPromote`; `_Process` drains it every frame. Nothing can be stranded.
- **Mesh promotion throttle (time budget)** — promotion (`create_mesh_from_data` + GPU upload + `AddChild`) runs on the main thread and is what causes movement frame dips. Each frame the drain promotes until `MaxPromotionMillisPerFrame` (≈2.5 ms) is spent, then defers the rest. `MaxPromotionsPerFrame` is a coarse safety ceiling. At least one promotes per frame so loading can't stall. This removed meshing as a spike source (verified: `remeshes/s=232` at 60 fps).
- **`handle_chunks_art` per-crossing cost** — was ~6 full O(active-set) passes on every chunk-boundary crossing (≈73k entries at RD25 → huge spike). Now: reprioritization iterates the *small* pending queues instead of the full offset sphere; the active-set rebuild is gone (static `cachedOffsetSet` + relative `chunkPos - playerPos` lookup); edited-chunk eviction is throttled to every 3 s. Down to ~1 O(N) pass (the unload sweep). The per-frame scan also dropped its per-chunk `sqrt` (`LengthSquared` compare).
- **`IsFullySolid` canonical sync** — edits now clear `IsFullySolid` on the canonical entry too, so a reused edited chunk can't wrongly take the solid fast-path and render invisible.
- **Damage shader `cull_back`** — overlay cubes were double-sided (`cull_disabled`); halved their fragment cost. (Overlays were ultimately ruled out as the bottleneck — `dmg=64` still showed 29 fps.)

Reverted: a canonical-retention experiment (keeping all unedited voxel data resident, ~94 MB) — regeneration is cheap and threaded, so it wasn't worth the memory.

Skipped: **#4** (render-distance clamp — removed at user request). Deferred: **#5 LOD**, **#7 frustum cull**.

### Where it stands

From the leak/decay/permanent-degradation phase the game is now solid: ~48–60 fps typical, with dips to ~26–36 only during heavy simultaneous movement + terrain destruction at RD15 (~17k chunks). The remaining cost is **GPU-bound, not pipeline-bound**:

1. **Rendering player-destroyed jagged terrain** — high triangle count, view-dependent (standing still looking at a carved area drops fps with the chunk pipeline fully idle). The fix is **greedy meshing**, but it's complicated by the texture atlas: merged quads need *tiling* UVs, which standard atlas UVs don't support — it would require a texture array or a custom-data UV shader. High ceiling, high effort.
2. **Moving at RD15** — regeneration churn (unedited chunks regenerate on re-entry) + the remaining O(N) unload sweep + GPU. Could be reduced with an incremental (trailing-shell only) unload sweep.
3. **Enemies** — `treeNodes` climbs (~1670 → ~1768) during some idle dips; enemy particles/animation/AI may be contributing. Worth profiling.

Cheapest real lever the user keeps resisting: **RD 12 instead of 15** halves the active set (~8k vs ~17k chunks) for marginal visual loss on a 32-chunk planet.

### #6 mesh merging / chunk MultiMesh — ruled out

Draw calls are **not** the bottleneck: `meshNodes` (≈ draw calls) stays ~1,200 whether fps is 39 or 61, and the game hits the 60 fps vsync cap with all of them present. A chunk MultiMesh would reduce draw calls (not the problem) and make re-meshing/edits more expensive. Do not pursue.

---

## Enemies — the other major cost (per-frame, CPU-bound)

**Test result:** raising the spawn cap to 50 (`EnemySpawner.MaxEnemies`) dropped fps to **25** at RD12 — and identically whether the enemies were on-screen or not. Spawning them in (interval lowered to 0.1 s) dropped fps to **3–9** transiently. (Both values reverted to 5 / 2 s after the test.)

"Same fps whether looking or not" ⇒ the cost is **CPU per-frame work that runs regardless of visibility**, not GPU rendering. Per enemy, every frame:

- **Skeletal `AnimationPlayer`** — each creature animates a full skeleton continuously (the `Idle` clip is manually re-looped and never stops). This is the dominant cost for skinned models at scale.
- **`UniParticles3D`** — per-creature particle simulation.
- **`_PhysicsProcess`** (`Entity`) — AI (`Creature.ApplyMovementFromInput`, cheap) + `CheckWorldCollisions` (several `get_block` voxel lookups per tick).
- **`_Process`** (`Enemy`) — `Set("paused", …)` on each particle node, a `SpeedScale` write per `AnimationPlayer`, and a health-bar `LookAt` billboard.

The 3–9 fps spawn hitch is separate: instantiating the GLB + skeleton scene on the main thread is a heavy per-spawn stall.

### Fix: enemy LOD + pooling (not yet implemented)

For a combat game built around many enemies, each enemy must be cheap at scale. Planned, in priority order:

1. **Animation LOD** — pause / zero `AnimationPlayer` when an enemy is far or off-screen; resume when near/visible. Biggest single win (skeletal animation dominates).
2. **Particle LOD** — disable `UniParticles3D` emission when far / off-screen.
3. **AI throttle** — run AI + `CheckWorldCollisions` every few physics ticks for distant enemies, full rate when near.
4. **Spawn pooling** — reuse a pool of creature instances instead of instantiating the GLB per spawn; removes the 3–9 fps spawn hitch.

Suggested default knobs: full fidelity within ~40 u; animation + particles off beyond that; AI at ~1/4 rate beyond ~80 u. Gate on distance (optionally also a `VisibleOnScreenNotifier3D`). Do 1–3 first (1 alone recovers most of the cost); pooling is a slightly larger follow-up.

---

## Quick Wins

These are targeted edits with no architectural changes. Combined they should meaningfully raise the floor and eliminate most spikes.

---

### 1. Increase mesh-builder thread count (highest impact)

**File:** `minecraft/Scripts/The World/Chunk_Manager.cs`

**The problem:** Lines 113 and 199–201 declare and start exactly one `loadingThread`. Each chunk takes 4–7 ms to mesh. With 14K chunks in queue the backlog is measured in minutes — the single thread can never keep up with player movement. Lag spikes are the main thread stalling on near-player chunks buried deep in that queue.

**The fix:** Replace the single thread with a small pool (3 threads is a safe starting point for laptops). The `LoadingWorkerLoop` already dequeues from `loadingWorkQueue` under a lock, so multiple threads pulling from the same queue is safe — just start more of them.

**Steps:**
1. Change the field on line 113 from `private Thread loadingThread;` to `private Thread[] loadingThreads;`
2. In `_Ready()` (around line 199), replace the single thread start with a loop that creates and starts 3 threads, each running `LoadingWorkerLoop`, and stores them in the array
3. In `_ExitTree()` (around line 209), join all threads in the array instead of the single one
4. The `meshVerticesFlat`, `meshNormalsFlat`, `meshUvsFlat` flat arrays on lines 141–143 are **shared state** — each thread needs its own copy. Move them from class-level fields into local variables inside `load_calculate()`, or make them `[ThreadStatic]`

**Expected result:** 3× mesh throughput, dramatically shorter queue backlog, spikes should largely disappear.

---

### 2. Fix pre-allocated vertex array sizes

**File:** `minecraft/Scripts/The World/Chunk_Manager.cs`, lines 141–143, 577–579, 657–663

**The problem:** Arrays are pre-allocated at `4096 * 3` floats, which holds 1,365 vertices — correct for the average chunk but wrong for surface chunks with many exposed faces. When a chunk exceeds this, lines 657–663 call `Array.Resize()`, which copies the entire array every time it triggers. This is an O(n) allocation in the hot path of the mesh loop.

**The fix:** Pre-allocate at the true worst case. A 16×16×16 chunk can expose at most 6 faces × 4 vertices × 4096 blocks = 98,304 vertices, but a realistic surface chunk caps around 8,192. Allocate there.

**Steps:**
1. Change line 141 from `new float[4096 * 3]` to `new float[8192 * 3]`
2. Change line 142 from `new float[4096 * 3]` to `new float[8192 * 3]`
3. Change line 143 from `new float[4096 * 2]` to `new float[8192 * 2]`
4. Update the guard checks on lines 577–579 to match the new size
5. (After doing fix #1 above and making these local/ThreadStatic, apply the same sizes there)

**Expected result:** Eliminates array resizing in the mesh loop entirely. Small but removes a class of O(n) allocations that happen per chunk.

---

### 3. Cap mesh promotions per frame on the main thread

**File:** `minecraft/Scripts/The World/Chunk_Manager.cs`, `load_ready_chunk()` at line 787

**The problem:** `CallDeferred("load_ready_chunk", ...)` queues mesh-ready chunks to be promoted to `MeshInstance3D` nodes on the main thread. When the mesh thread (or threads, after fix #1) finishes a burst of chunks, `load_ready_chunk` is called for all of them in the same deferred flush. Each call creates a node, builds a mesh, and adds it to the scene tree — which is not free. A burst of 50+ in one frame causes a visible hitch.

**The fix:** Limit how many chunks get promoted to the scene in a single `_Process` tick. Defer the rest to the next frame.

**Steps:**
1. Add a counter field like `private int _meshPromotionsThisFrame = 0` (or a local in `_Process`)
2. Reset it to 0 at the start of each `_Process` call
3. In `load_ready_chunk()`, check the counter at entry — if it has reached the cap (start with 8), store the incoming data in a small pending list and return
4. Drain that list at the start of each `_Process` up to the cap before processing new deferred calls
5. Tune the cap: lower = smoother frames, higher = faster world load-in

**Expected result:** Eliminates the "burst hitch" when moving into new chunk territory. World loads in slightly slower but frames stay smooth.

---

### 4. Add a render distance runtime cap

**File:** `minecraft/Scripts/The World/Chunk_Manager.cs`, line 35

**The problem:** `[Export] public int RenderDistance = 5;` is set to 15 in the Inspector. At RD 15 the visible chunk count is ~14,147. At RD 8 it's ~2,100. The game cannot feel smooth at 15 until fixes #5 (LOD) and #6 (batching) are done.

**The fix:** Enforce a maximum in code so the Inspector can't accidentally be set too high, and document what the safe range is.

**Steps:**
1. In `_Ready()`, add a clamp: `RenderDistance = Mathf.Clamp(RenderDistance, 2, 10);`
2. Set the Inspector value back to 8 as the high-quality target (yields ~2,100 chunks)
3. Use RD 5–6 as the "laptop" preset until LOD is implemented

**Expected result:** Immediate FPS improvement purely by reducing chunk count. RD 8 with fixes #1–#3 should be playable.

---

## Medium Effort — Architectural Changes

These require more design work but are where the real ceiling raises.

---

### 5. Implement a Level of Detail (LOD) system

**Files:** `Chunk_Manager.cs`, `Chunk.cs`, potentially a new `Chunk_LOD.cs`

**The problem:** Every chunk from 1 block away to 240 blocks away is meshed at full 16×16×16 resolution. A chunk at distance 14 is 2–3 pixels on screen but costs identical mesh work to one at distance 1. There is no LOD system at all.

**The approach:** Three LOD tiers based on chunk distance from player.

| Tier | Distance | Mesh Detail | How |
|------|----------|-------------|-----|
| LOD0 | ≤ 6 chunks | Full 16×16×16 | Existing pipeline unchanged |
| LOD1 | 7–12 chunks | 2×2×2 macro blocks (8×8×8 resolution) | Downsample voxels in groups of 8, one block per group |
| LOD2 | > 12 chunks | Flat top-surface-only slab | Only mesh the top-visible layer per column |

**Steps:**
1. Add a `ChunkLOD` enum (`Full`, `Coarse`, `Slab`) to `Chunk.cs`
2. In `handle_chunks_art()`, when enqueuing a chunk to `loadingQueue`, compute which LOD tier it belongs to based on `offset.Length()` and store it alongside the position
3. In `load_calculate()`, branch on LOD tier:
   - **Full**: existing triple-nested loop unchanged
   - **Coarse**: loop in steps of 2 (`x += 2`), sample the majority block in the 2×2×2 region, emit one block-sized face
   - **Slab**: for each (x, z) column find the highest non-air Y, emit a single top face
4. When a chunk's LOD tier changes (player moves closer), re-enqueue it for remeshing at the new tier
5. Store the current LOD on `Chunk` so you can skip remeshing if the tier hasn't changed

**Expected result:** Distant chunks cost 4–16× less mesh work. Total vertex count at RD 15 drops from ~5.6M to ~800K. RD 15 becomes achievable.

---

### 6. Reduce draw calls via mesh merging

**Files:** `Chunk_Manager.cs`, new helper class

**The problem:** Each chunk is its own `MeshInstance3D`, resulting in ~14,147 draw calls per frame at RD 15, and ~2,100 at RD 8. Modern laptops handle 1,000–3,000 draw calls comfortably. The GPU command buffer overhead alone accounts for a chunk of frame time.

**The approach:** Merge groups of adjacent chunks (e.g., 2×2 or 4×4 footprint) into a single combined mesh ("super-chunks"). This collapses 4–16 draw calls into 1 with no change to the visible result.

**Steps:**
1. Define a super-chunk grid: group chunks in 2×2 tiles on the XZ plane (same Y). Each super-chunk covers a 32×16×32 block volume
2. When all chunks in a group have finished meshing (check `pendingBuffers` for all 4), merge their vertex arrays by offsetting positions to world space and concatenating into a single `ArrayMesh`
3. Assign the merged mesh to one `MeshInstance3D` at the super-chunk's center
4. On individual chunk remesh (e.g., block break), flag the super-chunk as dirty and re-merge only that group
5. For LOD1/LOD2 chunks (after fix #5), consider 4×4 grouping since their meshes are smaller

**Expected result:** Draw calls at RD 8 drop from ~2,100 to ~530. Draw calls at RD 15 drop from ~14,147 to ~890. GPU frame time drops significantly.

---

### 7. Frustum-cull the mesh queue

**File:** `Chunk_Manager.cs`, `LoadingWorkerLoop()` at line 485, `handle_chunks_art()` at line 226

**The problem:** The mesh thread generates meshes for all chunks in the active set regardless of whether they're in the player's view frustum. If the player is facing north, chunks behind them (south) are meshed anyway, consuming thread time and producing meshes that are immediately discarded when the player doesn't turn around.

**The approach:** Before enqueuing a chunk into `loadingWorkQueue`, check whether it overlaps the camera frustum. Only mesh it if visible (or within a 1-chunk buffer for smooth look-around).

**Steps:**
1. Expose the main `Camera3D`'s frustum planes to `Chunk_Manager` — either pass the `Camera3D` reference or compute frustum planes from its projection and transform each tick and store them
2. In `handle_chunks_art()`, when deciding to enqueue a chunk into `loadingQueue` (lines ~329, ~368), add a frustum overlap check: compute the chunk's AABB in world space and test it against the 6 frustum planes
3. Keep a small "recently frustum-culled" set — chunks that were culled but are close enough to re-enqueue immediately on camera rotation
4. When the player's camera yaw changes significantly (e.g., >15°), mark all culled nearby chunks for immediate re-evaluation

**Expected result:** Mesh thread workload drops by ~40–60% during normal gameplay (player faces one direction at a time). Queue stays shallow, reducing spikes when entering new areas.

---

### 8. Cache neighbor chunk references in `load_calculate()`

**File:** `Chunk_Manager.cs`, inside `load_calculate()` around lines 640–648

**The problem:** For every block on a chunk edge, `get_block()` is called on the neighboring chunk. `get_block()` does a dictionary lookup (`chunks.TryGetValue(chunkPos)`) to find the neighbor. For a 16×16 face this is 256 lookups, and there are up to 6 faces per chunk — 1,536 dictionary lookups per chunk, across 14K chunks. Not catastrophic alone, but it stacks with everything else.

**The fix:** Before the main triple-nested loop in `load_calculate()`, resolve the 6 neighbor chunk references once and store them in local variables.

**Steps:**
1. At the top of `load_calculate()`, look up all 6 neighbors by position and store in local `Chunk? neighborFront, neighborBack, neighborLeft, neighborRight, neighborTop, neighborBottom` variables (nullable — null means not yet generated)
2. In the edge-block check, instead of calling `get_block(worldPos)`, read directly from the pre-fetched neighbor's `Voxels` array using the local offset
3. If a neighbor is null, treat edge blocks as non-occluded (existing behavior)

**Expected result:** Eliminates 1,500+ dictionary lookups per chunk mesh build. Meaningful speedup for surface chunks with lots of exposed edges.

---

## Priority Order

If time is short, do them in this order — each one independently improves things:

1. **Fix #4** (lower RD to 8) — zero code risk, instant result
2. **Fix #1** (3 mesh threads) — biggest single impact on spikes
3. **Fix #2** (larger pre-allocated arrays) — prevents resize allocations
4. **Fix #3** (cap promotions per frame) — smooths burst hitches
5. **Fix #5** (LOD) — unlocks smooth RD 12–15
6. **Fix #7** (frustum cull queue) — reduces thread wasted work
7. **Fix #8** (cache neighbor refs) — mesh loop micro-optimization
8. **Fix #6** (mesh merging) — GPU draw call reduction, largest effort