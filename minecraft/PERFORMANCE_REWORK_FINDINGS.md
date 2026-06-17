# Rework Branch — Remaining Performance Gaps

Follow-up to `PERFORMANCE.md`. After implementing the quick wins (thread pool, pre-allocated arrays, frame-budgeted promotions, RD cap, neighbor caching), baseline FPS in an open-plain/no-caves test at RD15 still didn't move much. These are the two concrete gaps found by reading the current `rework` branch (`Chunk_Manager.cs`) that explain why — neither is touched by the quick wins already done, and neither is the LOD/mesh-merging/frustum-culling work (those only affect chunks that produce real geometry, which a flat test world already keeps small).

Verified against `origin/rework` @ `bfe640e`.

---

## 1. No fast-path skip for all-air chunks

### The problem

`IsFullySolid` (set when a chunk is generated) is computed with an early-exit loop:

```csharp
bool isFullySolid = true;
for (int i = 0; i < data.Length; i++)
{
    if (data[i] == 0) { isFullySolid = false; break; }
}
chunk.IsFullySolid = isFullySolid;
```

This only detects "every block is solid." The moment it sees one air block, it sets `false` and stops — there's no equivalent `IsAllAir` flag, and you can't get one for free from this loop (confirming "all air" requires ruling out a solid block existing *anywhere*, which needs a full scan, not an early exit).

Because of this, `load_calculate()`'s only pre-emptive skip —

```csharp
if (chunk.IsFullySolid && adjacent_chunks_solid(position))
{
    pendingBuffers[position] = new MeshBuffers { VertexCount = 0 };
    _readyToPromote.Enqueue(position);
    return;
}
```

— never fires for an all-air chunk. Every pure-sky chunk falls through to the full mesh-build path and pays for:

1. A full 4096-byte voxel snapshot copy (`Array.Copy`)
2. Six neighbor-chunk dictionary lookups to resolve `neighborVoxels[]`
3. The complete 4096-iteration triple-nested loop (each air block hits `if (blockId == 0) continue;` immediately, so the per-block cost is small, but it's still 4096 of them every time)
4. A `MeshBuffers` allocation and four `ArrayPool<T>.Shared.Rent(0)` calls
5. A `ConcurrentDictionary` write and `ConcurrentQueue` enqueue

Only *after* all of that does the result — `VertexCount == 0` — get checked in `PromoteChunk()`. That check happens too late to save any of the above; it can only decide what happens next (skip creating a `MeshInstance3D`, skip the per-frame promotion throttle since "empty" promotions are treated as free). The expensive work already ran on the mesh thread before this check is reached.

### Why it matters in practice

This isn't a constant per-frame tax — it's burst-shaped, tied to how fast new chunks enter the loaded volume:

- **World load-in / RD increase**: in an open plain, roughly half the RD15 sphere (everything above terrain height) is pure air. All of it gets meshed for the first time in a short window, each one paying the cost above for zero visual payoff.
- **Sustained movement**: the leading edge of the render sphere continuously introduces new chunks as you walk. A large fraction of newly-entering chunks above the terrain line are all-air, so this cost recurs the entire time you're moving, not just at world load.
- **Thread contention**: the real-world impact isn't just wasted microseconds — it's that this work competes with *actual* shell chunks (the ones with real geometry) for the same mesh thread(s), delaying when terrain you're looking at actually gets meshed.
- **Unthrottled promotion bursts**: in `PromoteChunk`, the per-frame promotion budget only applies when `VertexCount != 0 && IndexCount != 0` ("expensive"). All-air results are exempt from throttling, so if hundreds finish meshing in the same tick (e.g. right after a load-in burst), they all get promoted in that single frame with no spreading.

### The fix

Give all-air chunks the same kind of advance knowledge `IsFullySolid` has — a flag computed once at generation time, checked *before* any of the expensive steps run, instead of being derived after the fact.

**Steps:**
1. In the generation code where `IsFullySolid` is computed, replace the early-exit loop with a single pass that tracks two flags — `sawSolid` and `sawAir`. Break early only once *both* are true (that's a mixed/shell chunk — the common, expensive case — so it still exits fast). If the loop completes without ever setting both, the chunk is uniformly solid or uniformly air.
2. Add an `IsAllAir` field to `Chunk` (alongside `IsFullySolid`) and set it from this pass.
3. In `load_calculate()`, extend the existing fast-skip check to also cover the all-air case:
   ```csharp
   if ((chunk.IsFullySolid && adjacent_chunks_solid(position)) || chunk.IsAllAir)
   {
       pendingBuffers[position] = new MeshBuffers { VertexCount = 0 };
       _readyToPromote.Enqueue(position);
       return;
   }
   ```
   (Air chunks don't need a neighbor-solidity check the way solid ones do — air bordering air is still nothing to draw regardless of what's next door.)
4. Make sure `IsAllAir` gets invalidated/recomputed anywhere `IsFullySolid` already gets reset (block edits, chunk reuse) — search for existing `IsFullySolid = false` assignments and mirror the same call sites.

**Expected result:** All-air chunks skip the snapshot copy, neighbor resolution, and full 4096-iteration loop entirely — same order-of-magnitude saving the fully-solid case already gets. Reduces mesh-thread contention during world load-in and while moving, which should reduce stutter when entering new terrain specifically (not necessarily steady-state FPS while standing still — see issue #2 for that).

---

## 2. `handle_chunks_art`'s per-tick sweep scales with render distance cubed

### The problem

Inside `handle_chunks_art()`, this loop runs every single `_Process` tick (gated only by the 15ms `TIME_HANDLE` timer, not by player movement):

```csharp
foreach (var offset in cachedChunkOffsets)
{
    var chunkPos = playerPos + offset;
    ...
    if (chunks.TryGetValue(chunkPos, out var chunk)) { ... }
    else { ... }
}
```

`cachedChunkOffsets` holds every offset within `RenderDistance + 1` of the player — i.e. the full sphere/cylinder volume, not just chunks that need attention. Volume scales with distance **cubed**:

| Render Distance | generationDistance | Approx. offset count |
|---|---|---|
| 8 | 9 | ~3,054 |
| 15 | 16 | ~17,157 |

Going from RD8 to RD15 isn't a ~2x increase in this loop's per-frame cost — it's roughly **5.6x**, because `16³ / 9³ ≈ 5.6`. And this runs unconditionally every tick, regardless of whether the terrain is an open plain or dense caves — it's pure bookkeeping (dictionary lookups, neighbor-generated checks) with a cost that's entirely about volume, not content.

This is separate from the chunk-crossing logic above it in the same function (queue rebuilds, eviction, unloading), which *is* correctly gated to only run when the player crosses a chunk boundary. The offset sweep is not gated the same way — it runs every tick no matter what.

### Why it matters in practice

This is the most likely explanation for why baseline FPS while standing still in a loaded area didn't improve much from the quick wins — none of those touched this loop. It's a continuous, ambient cost that exists purely because of render distance, independent of how much actual geometry is being drawn. Mesh-skip optimizations (including fix #1 above) don't touch it at all, since this loop runs whether or not any chunk needs meshing.

### The fix

Avoid re-scanning the entire RD³ volume every tick. Two viable approaches:

**Option A — spread the sweep across multiple frames.** Instead of iterating all of `cachedChunkOffsets` every tick, keep an index into the list and process a fixed slice (e.g. 1/8th) per tick, wrapping around. Steady-state chunks that need no action are still checked, just not all in the same frame.

**Steps:**
1. Add a `_sweepCursor` index field, persisted across ticks
2. In `handle_chunks_art()`, replace the full `foreach` with a loop that processes `cachedChunkOffsets.Count / N` entries starting at `_sweepCursor`, advancing and wrapping the cursor each tick
3. Choose `N` (e.g. 8) so a full sweep still completes roughly every 8 ticks (~120ms) — fast enough that newly-needed generation/loading isn't meaningfully delayed, but spreads the cost instead of paying it all in one frame

**Option B — maintain a smaller "needs attention" set.** Most ticks, the vast majority of chunks in range are already generated and loaded and need zero action. Track a separate, much smaller set of chunks that are mid-transition (just entered range, just generated, waiting on neighbors) and only iterate that set per tick. Only recompute the full set when the player crosses a chunk boundary (where the cached offsets shift), which is already a gated, infrequent path.

Option A is the smaller change; Option B is more invasive but removes the cost almost entirely rather than just spreading it out. Start with A, move to B if the spread-out cost is still measurable in profiling.

**Expected result:** This is the fix most likely to move the baseline FPS number directly, since it's a guaranteed per-frame cost that scales with render distance regardless of terrain content — exactly the kind of cost that wouldn't show up as "fewer draw calls" or "fewer vertices" but still eats frame budget every tick.

---

## Priority

1. **Fix #2 (RD³ sweep)** — do this first. It's the one most likely to move steady-state FPS, since it's an unconditional per-frame cost independent of terrain content.
2. **Fix #1 (AllAir skip)** — do this second. Helps more with stutter during load-in and movement than with steady-state FPS while standing still, but it's a small, low-risk change mirroring a pattern that already exists in the code (`IsFullySolid`).
