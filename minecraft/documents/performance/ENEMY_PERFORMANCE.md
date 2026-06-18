# Enemy Performance — Fixes + Standing Standard

Companion to `PERFORMANCE.md` (which covers chunks/terrain). This doc is enemies only:
the concrete fixes for the current cost, and the rules every future enemy type must
follow so this regression can't come back.

Confirmed root cause (test in `PERFORMANCE.md`): raising the spawn cap to 50 dropped fps
to 25 at RD12, **identically whether the enemies were on-screen or not**. That signature
means CPU work that runs unconditionally per enemy per frame, not GPU/draw cost. There is
no enemy manager, no pooling, no LOD — every `Enemy` is an independent Godot node paying
full price every tick regardless of distance or visibility.

**Status:** Fixes 1, 2, 3, 5 below are implemented in `Enemy.cs`/`Creature.cs`. Fix 4
(pooling) is not done yet. After applying Fixes 1/2/3/5, the remaining bottleneck with
many creatures clustered near the player (the realistic combat case — LOD only helps
distant enemies, Near tier is meant to be fully simulated) traced to `UniParticles3D`: a
custom addon whose per-particle update is a GDScript loop, not GPU-driven. It was the
single largest remaining cost at Near tier even with emission already gated to Near-only.
Fixed by replacing `Creature`'s particle node with a native `GpuParticles3D` (see
`Assets/creature.tscn` → `EmberParticles`) configured to the same look (sphere emission,
short randomized lifetime, shrink-over-life, warm emissive unshaded material). `Enemy.cs`
gates it via `GpuParticles3D.Emitting` instead of the `"paused"` property. **Do not add
`UniParticles3D` to any new enemy** — see the rule in `CLAUDE.md`.

---

## Part 1 — Fixes for the current code

In priority order. Each is independent; #1 alone recovers most of the cost.

### Fix 1: Centralize the player-distance calculation

**Problem:** Every subclass independently computes `(playerPos - GlobalPosition).Length()`
— a full `sqrt` — often more than once per frame. `Creature.cs` does it at lines 43, 55,
and 64 in the same tick (once in `OnAnimationFinished`, again twice in
`ApplyMovementFromInput`). `RangedEnemy.cs:38`, `HeavyEnemy.cs`, `SwarmEnemy.cs` each do
their own copy. 50 enemies independently recomputing distance to the same player position
is pure duplicated work, and it's also the natural hook point for LOD (Fix 2).

**Fix:** Add to `Enemy.cs`, computed once per frame before any subclass logic runs:

```csharp
public enum LodTier { Near, Mid, Far }

public float DistSqToPlayer { get; private set; }
public LodTier Lod { get; private set; }

private const float MidLodDistance = 40f;
private const float FarLodDistance = 80f;

protected void UpdatePlayerDistance()
{
    var player = Global.Instance?.Player;
    DistSqToPlayer = player != null
        ? GlobalPosition.DistanceSquaredTo(player.GlobalPosition)
        : float.MaxValue;
    Lod = DistSqToPlayer > FarLodDistance * FarLodDistance ? LodTier.Far
        : DistSqToPlayer > MidLodDistance * MidLodDistance ? LodTier.Mid
        : LodTier.Near;
}
```

Call it once at the top of `Enemy`'s physics step (wrap `ApplyMovementFromInput`, don't
override it per-subclass), and have subclasses read `DistSqToPlayer` / `Lod` instead of
calling `Length()`/`DistanceTo()` themselves. Use `Mathf.Sqrt(DistSqToPlayer)` only in the
rare spot that genuinely needs a linear distance (e.g. normalizing a direction already
needs `Normalized()`, which is fine — that's one sqrt, not three).

### Fix 2: Animation + particle LOD (the dominant cost)

**Problem:** `Creature.cs:34,40` plays and manually re-loops an `AnimationPlayer` clip
forever — full skeleton evaluation + skinning, every enemy, every frame, regardless of
distance. Every creature's `UniParticles3D` emits continuously. `Enemy._Process`
(`Enemy.cs:66-70`) writes `paused`/`SpeedScale` every frame even when nothing changed.

**Fix:** Gate both on `Lod` from Fix 1, in `Enemy._Process`, and only write when the value
actually changes:

```csharp
bool shouldAnimate = Lod != LodTier.Far && !hitstop;
if (shouldAnimate != _lastAnimateState)
{
    foreach (var node in _animPlayers)
        if (node is AnimationPlayer ap) ap.SpeedScale = shouldAnimate ? 1f : 0f;
    _lastAnimateState = shouldAnimate;
}

bool shouldEmit = Lod == LodTier.Near && !hitstop;
if (shouldEmit != _lastEmitState)
{
    foreach (var node in _particles)
        if (node is GpuParticles3D p) p.Emitting = shouldEmit;
        else node.Set("paused", !shouldEmit);
    _lastEmitState = shouldEmit;
}
```

This is the single biggest win — it's what's actually evaluating bone transforms and
skinning meshes for 50 entities every frame whether or not anyone can see them.

### Fix 3: Throttle AI decision-making for distant enemies

**Problem:** `Entity.CheckWorldCollisions` (`Entity.cs:125-219`) runs a triple-nested
voxel-grid loop on 3 axes every physics tick for every enemy. `RangedEnemy.HasLOS`
(`RangedEnemy.cs:95-111`) steps a ray through the voxel grid at 0.5m increments (40-60
`get_block()` calls) every frame it's in range. None of this needs full rate at distance.

**Fix:** For `Lod == LodTier.Far`, run the *decision* part of AI (state transitions,
target finding, LOS checks) every 4th physics frame instead of every frame, accumulating
skipped delta so movement stays time-correct on the tick that does run. Keep gravity/
`MoveAndSlide`/world collision at full rate (cheap relative to the AI logic, and skipping
it causes visible clipping). Concretely: each subclass's `ApplyMovementFromInput` checks
`Lod` and a frame counter before doing the expensive branch (LOS raycast, state-machine
re-evaluation); the velocity integration / collision response underneath is unaffected.

### Fix 4: Pool enemy instances instead of instantiating per spawn

**Problem:** `EnemySpawner.cs` calls `Instantiate()` + `AddChild()` per spawn, which loads
the GLB + skeleton on the main thread. Lowering the spawn interval to 0.1s dropped fps to
3-9 transiently — this is a separate spike from the steady-state cost above.

**Fix:** Pre-instantiate a pool of creature nodes at level load (hidden, processing
disabled), and on "spawn" just reposition + re-enable an inactive one from the pool. On
death, return it to the pool instead of `QueueFree()`. Same applies to `EnemyBolt` if
ranged enemies fire often.

### Fix 5: Gate non-essential `_Process` work the same way

**Problem:** Health bar billboard `LookAt` (`Enemy.cs:75-77`) runs every frame for every
enemy with a visible bar, including ones far enough that the bar is unreadable.

**Fix:** Skip the `LookAt` call (and skip even checking `_healthBarRoot.Visible`) when
`Lod == LodTier.Far`.

---

## Part 2 — Standard for every enemy created from here on

The fixes above only hold if new enemy types can't reintroduce the same cost. Treat these
as required, not optional, for any new `Enemy` subclass:

1. **Extend `Enemy`, not `Entity` directly**, for anything AI-driven/hostile. `Enemy` is
   where the distance cache and LOD tier live — extending `Entity` directly opts out of
   them and you'll be back to a from-scratch distance calc and no LOD.

2. **Never call `(playerPos - GlobalPosition).Length()` / `.DistanceTo()` yourself.** Use
   the inherited `DistSqToPlayer` / `Lod`. If you need a unit direction, `Normalized()` is
   fine (it's one sqrt, already required); don't *also* call `Length()` first to check
   range — use the squared comparison.

3. **Every expensive per-frame operation must be tier-gated.** "Expensive" means: any
   raycast/voxel raycast, any `get_block()` loop, any physics query, any trig beyond a
   single `Atan2` for facing. Far-tier enemies should look approximately right, not be
   perfectly simulated. If you're not sure where to put the check, put it at the top of
   `ApplyMovementFromInput`, before the expensive branch.

4. **No scene-tree search in anything that runs every frame.** `FindChildren`, `GetNode`
   with a wildcard/recursive path, anything that walks the tree — do it once in
   `ImHere()`/`_Ready()` and cache the reference as a field. Never inside `_Process` or
   `ApplyMovementFromInput`.

5. **No unconditional property writes every frame.** If you're calling `Set()` or
   assigning a Godot property (`SpeedScale`, `Emitting`, `Visible`, `Paused`, ...) inside a
   per-frame method, compare against the last value you applied and skip the write if it
   hasn't changed. Godot property writes aren't free, and at 50 entities the redundant
   writes add up.

6. **Animations must respect LOD.** Don't manually re-loop a clip (the
   `AnimationFinished` → `Play()` pattern `Creature.cs` uses) without checking `Lod` first.
   A looping idle animation on an off-screen Far-tier enemy should have its
   `AnimationPlayer` stopped, not ticking at full rate waiting to be useful.

7. **Particles must respect LOD.** Any `UniParticles3D`/`GpuParticles3D` on an enemy
   needs its emission gated the same way — Near only, or Near+Mid at most. Never leave a
   continuously-emitting particle system running on something nobody can see.

8. **Voxel/world queries are the most expensive primitive in this codebase** — each
   `get_block()` call is a dictionary lookup plus a chunk-array read, and it's multiplied
   by entity count and by however many cells you query. Budget roughly O(10) such calls
   per enemy per frame at Near tier; anything beyond that needs a specific reason and
   should drop to near-zero at Mid/Far.

9. **No O(n²) cross-entity scans.** If a new behavior needs awareness of other enemies
   (flocking, avoidance, "don't all attack at once"), do not have every enemy iterate
   every other enemy each frame. That doesn't exist yet in this codebase — keep it that
   way. If it becomes necessary, it belongs in a shared manager pass (see below), not
   inside each entity's own update.

10. **Pool, don't instantiate, for anything that spawns more than a couple of times per
    encounter.** Enemies, projectiles (`EnemyBolt` and friends) — `Instantiate()` +
    `AddChild()` on the main thread is a per-spawn stall, not just a per-frame cost.

11. **Profile at 50 concurrent, not 1-2, before calling a new enemy type done.** Something
    that's "fine alone" but skips tier-gating will pass every manual test and then quietly
    reintroduce this exact bug the next time someone bumps the spawn cap. `EnemySpawner`
    has a `MaxEnemies` export — temporarily raise it, confirm fps holds, then revert.

### Looking further ahead: a real EnemyManager

Not required for the fixes above (the per-enemy `Lod` cache is enough on its own), but if
enemy behaviors grow to need cross-entity awareness, the right shape is a central manager
that:

- Runs once per frame, owns a simple spatial index (grid or just a flat array — 50-200
  entities doesn't need anything fancier) so any enemy that needs "nearby allies" gets it
  from one shared O(n) pass instead of every enemy scanning every other enemy.
- Staggers AI re-evaluation across frames (e.g. round-robin a fraction of the Far-tier
  population each tick) instead of every enemy deciding on the same frame, which smooths
  out spikes rather than just lowering average cost.

Don't build this preemptively — the LOD + throttle fixes above are sufficient for the
current behavior set. Build it when a behavior actually needs cross-entity data, not
before.
