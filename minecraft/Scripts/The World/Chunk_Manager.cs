using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class Chunk_Manager : Node
{
	private const int CHUNK_SIZE = Global.CHUNK_SIZE;

	private static readonly Vector3I[] FaceOffsets = new Vector3I[]
	{
		new Vector3I(0, 0, -1), // Front (Z-)
		new Vector3I(0, 0, 1),  // Back (Z+)
		new Vector3I(-1, 0, 0), // Left (X-)
		new Vector3I(1, 0, 0),  // Right (X+)
		new Vector3I(0, 1, 0),  // Top (Y+)
		new Vector3I(0, -1, 0)  // Bottom (Y-)
	};

	private Global Global;
	public World_Generator WorldGen = new World_Generator(12345);
	private Dictionary<Vector3I, Chunk> chunks = new();
	private HashSet<Vector3I> activeChunks = new();
	private HashSet<Vector3I> dirtyChunks = new();
	private ConcurrentDictionary<Vector3I, byte> generationQueue = new();
	private ConcurrentDictionary<Vector3I, byte> loadingQueue = new();

	[Export] public Material Mat;
	[Export] public int RenderDistance = 5;
	public enum RenderMode
	{
		Cylinder,
		Sphere
	}

	private void EvictColdEditedChunks(Vector3I playerChunkPos)
	{
		// Collect edited canonical entries
		List<Vector3I> edited;
		lock (_canonicalLock)
		{
			edited = new List<Vector3I>();
			foreach (var kv in _canonicalStore)
				if (kv.Value.WasEdited) edited.Add(kv.Key);
		}

		if (edited.Count <= MaxColdEditedChunks) return;

		// Sort farthest-first from player canonical position and evict extras
		var playerWorld = chunk_to_world(Global.CanonicalChunkPos(playerChunkPos));
		var toEvict = edited
			.OrderByDescending(cp => (chunk_to_world(cp) - playerWorld).LengthSquared())
			.Take(edited.Count - MaxColdEditedChunks)
			.ToList();

		lock (_canonicalLock)
		{
			foreach (var cp in toEvict)
				_canonicalStore.Remove(cp);
		}
	}
	[Export] public RenderMode RenderModeType = RenderMode.Sphere;
	[Export] public Texture2D DamageTexture;

	[Export] public float DamageOverlayNormalOffset = 0.0025f;
	[Export] public float DamageOverlayScale = 1.0f;
	[Export] public bool DebugDamageUseSolidMaterial = false;
	[Export] public bool DebugDamageNoDepthTest = false;

	// noiseScale: feature size = PlanetWidth / (2π * noiseScale).
	// At 1.5 on a 1024-block planet that's ~108 blocks per feature.
	// Generation parameters live in Global.Instance.ActivePlanet (PlanetParams).
	// Do not add generation exports here — single source of truth is PlanetParams.

	// Block damage system — per-block health lives in Chunk.DamageData (lazy/sparse, keyed
	// by local position). These remaining structures are about the GLOBAL overlay render
	// budget (which blocks currently show a crack, FIFO-capped across the whole world),
	// not about where the health data itself lives.
	private Dictionary<int, MultiMeshInstance3D> damageOverlaysByBlock = new();
	private Dictionary<int, MultiMesh> damageMultiMeshByBlock = new();
	// Slot-based overlay bookkeeping: each BlockHealth holds a stable index (BlockHealth.overlaySlot)
	// into its block type's MultiMesh instance array. Granting/revoking/updating an overlay touches
	// only that one slot — O(1) regardless of how many other blocks of that type are tracked. This
	// replaces the old design where ANY change to a type re-walked and rewrote its ENTIRE visible
	// set every flush; at large MAX_DAMAGED_BLOCKS values that made even a single new damaged block
	// cost a multi-million-instance rebuild.
	private Dictionary<int, Stack<int>> _freeOverlaySlots = new();
	// High-water mark of slots ever allocated per type (== that type's MultiMesh.VisibleInstanceCount).
	// Only grows; freed slots go on the free-list above for reuse instead of shrinking this.
	private Dictionary<int, int> _overlayHighWater = new();
	// Reverse map slot -> owning BlockHealth (or null if that slot is currently freed), per
	// block type. Needed because growing a MultiMesh's InstanceCount clears all of its
	// existing instance data in Godot, so EnsureOverlayCapacity has to redraw every live and
	// freed slot from scratch after a resize — this is what it redraws live slots from.
	private Dictionary<int, List<BlockHealth>> _overlaySlotOwners = new();
	// Queued per-slot GPU writes, drained by FlushDirtyDamageOverlays under a per-frame time
	// budget. Frees and writes are queued separately (instead of one mixed queue) so frees
	// always drain first: a destroyed block's crack overlay disappearing late looks like the
	// explosion missed it, while a stale tint on a still-standing block is barely noticeable.
	private Dictionary<int, Queue<int>> _pendingOverlayFrees = new();
	private Dictionary<int, Queue<OverlayOp>> _pendingOverlayWrites = new();
	private LinkedList<Vector3I> _damageInsertionOrder = new LinkedList<Vector3I>();

	private struct OverlayOp
	{
		public int Slot;
		public Vector3I Pos;
		public float Health;
	}

	// Initial per-type MultiMesh capacity. Grows geometrically (EnsureOverlayCapacity) instead
	// of pre-allocating MAX_DAMAGED_BLOCKS worth of instances for every block type that's ever
	// taken a single hit — at large MAX_DAMAGED_BLOCKS values that pre-allocation cost hundreds
	// of MB of GPU buffer per type regardless of how many overlays that type actually used.
	private const int OverlayInitialCapacity = 1024;
	private const int MAX_DAMAGED_BLOCKS = 300000;
	// Blocks hit below this threshold don't get a damage overlay — cuts peripheral explosion entries.
	private const float MinDamageForOverlay = 0.15f;
	// Hits weaker than this don't even register a damage entry — at large explosion radii the
	// outer shell is mostly grazing hits with imperceptible effective damage; skipping them avoids
	// bloating per-chunk DamageData for zero gameplay-visible effect.
	private const float MinEffectiveDamage = 0.005f;
	private Material damageOverlayMaterial;

	// Persistent voxel data keyed by canonical chunk coord.
	// Survives chunk node unloads; edited entries persist for the whole run.
	private class ChunkData
	{
		public byte[] Voxels;
		public bool   IsFullySolid;
		public bool   IsAllAir;
		public bool   WasEdited;
	}
	private readonly Dictionary<Vector3I, ChunkData> _canonicalStore = new();
	private readonly object _canonicalLock = new object();

	private Thread[] generationThreads;
	private Thread[] loadingThreads;
	private volatile bool threadsRunning = true;

	// Synchronization locks for cross-thread shared state
	private readonly object queueLock = new object();
	private readonly object damageLock = new object();

	private readonly Queue<Vector3I> generationWorkQueue = new Queue<Vector3I>();
	private readonly Queue<Vector3I> loadingWorkQueue = new Queue<Vector3I>();
	private readonly object generationLock = new object();
	private readonly object loadingLock = new object();

	private int surfaceLevel;
	private int noiseSeed = 42;

	private float timeElapsed = 0f;
	private const float TIME_HANDLE = 0.015f;

	private Vector3I lastPlayerChunkPos = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
	private ulong _lastEvictMsec = 0;

	// The offset sweep below (in handle_chunks_art) used to walk all of cachedChunkOffsets every
	// tick — cost scales with render-volume (RD^3), unconditionally, even when nothing needs
	// attention. Spread it across SWEEP_SLICES ticks instead via a persisted cursor; a full sweep
	// still completes every ~SWEEP_SLICES ticks (~120ms at TIME_HANDLE=15ms), fast enough that
	// newly-needed generation/loading isn't meaningfully delayed.
	private int _sweepCursor = 0;
	private const int SWEEP_SLICES = 8;
	private List<Vector3I> cachedChunkOffsets = new List<Vector3I>();
	// Offsets (relative to the player chunk) that are in range, as a set for O(1) membership.
	// Built once in RecalculateChunkOffsets — a chunk is active iff (chunkPos - playerPos) is in it,
	// so we never rebuild a per-position active set on each crossing.
	private HashSet<Vector3I> cachedOffsetSet = new HashSet<Vector3I>();
	private List<Vector3I> chunksToUnload = new List<Vector3I>();
	private List<Vector3I> dirtyChunksList = new List<Vector3I>();


	[Export]
	public int MaxColdEditedChunks = 2000;

	// Throttle for EXPENSIVE mesh promotions (ArrayMesh build + GPU upload + scene attach) —
	// the main-thread work that causes movement frame dips. Each frame, promote until the time
	// budget is spent, then stop (the rest wait in _readyToPromote). The count cap is a coarse
	// safety ceiling; the time budget is the real throttle since mesh cost varies with size.
	// At least one chunk always promotes per frame so loading can't fully stall.
	[Export] public double MaxPromotionMillisPerFrame = 2.5;
	[Export] public int MaxPromotionsPerFrame = 32;
	private int _promotionsThisFrame = 0;

	// Caps how long FlushDirtyDamageOverlays may spend per frame rebuilding MultiMesh
	// instance data. A single mass-destruction explode() call can dirty several block
	// types at once, each with up to MAX_DAMAGED_BLOCKS entries — without a budget that's
	// a synchronous full rebuild of all of them in one frame. At least one dirty type is
	// always processed per call so the queue can't stall.
	[Export] public double MaxDamageOverlayMillisPerFrame = 2.0;
	// Worker threads push finished chunk positions here; _Process drains them under the
	// per-frame budget and promotes from pendingBuffers. Replaces the per-chunk CallDeferred
	// handoff, which left buffers stranded under heavy multi-threaded bursts.
	private readonly ConcurrentQueue<Vector3I> _readyToPromote = new();

	// Periodic runtime readout to diagnose accumulation. Toggle in Inspector.
	[Export] public bool DebugPerfReadout = false;
	private float _perfReadoutTimer = 0f;
	private int _meshRebuildsThisSecond = 0;

	// Per-thread reusable mesh scratch buffers. Allocated once per mesh thread, reused for
	// every build. MUST stay reused: at 8192 verts these arrays are ~98 KB, over the .NET
	// Large Object Heap threshold (85 KB), so allocating them per-build churns the LOH and
	// causes steadily-worsening frame times as you travel. ThreadStatic keeps each mesh
	// thread's buffers private, so no locking is needed.
	[ThreadStatic] private static float[] _tlVerts;
	[ThreadStatic] private static float[] _tlNormals;
	[ThreadStatic] private static float[] _tlUvs;
	[ThreadStatic] private static int[]   _tlIndices;
	[ThreadStatic] private static byte[]  _tlVoxels;

	private static readonly ArrayPool<Vector3> Vector3Pool = ArrayPool<Vector3>.Shared;
	private static readonly ArrayPool<Vector2> Vector2Pool = ArrayPool<Vector2>.Shared;
	private static readonly ArrayPool<int> IntPool = ArrayPool<int>.Shared;
	private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;

	public class MeshBuffers
	{
		public Vector3[] Vertices;
		public Vector3[] Normals;
		public Vector2[] UVs;
		public int[] Indices;
		// Self-contained counts so promotion never depends on out-of-band CallDeferred args.
		// VertexCount == 0 marks an empty/solid chunk (no mesh — just mark loaded).
		public int VertexCount;
		public int UvCount;
		public int IndexCount;
		public bool VerticesFromPool;
		public bool NormalsFromPool;
		public bool UVsFromPool;
		public bool IndicesFromPool;
	}

	// Diagnostic: toggle to hide all damage overlays at runtime. Lets us A/B test whether the
	// view-dependent slowdown in destroyed areas is the overlays (transparent overdraw) or the
	// chunk geometry itself.
	[Export] public bool ShowDamageOverlays = false;
	private bool _lastShowDamageOverlays = false;

	// pending buffers passed from worker threads to main thread, keyed by chunk position
	private ConcurrentDictionary<Vector3I, MeshBuffers> pendingBuffers = new ConcurrentDictionary<Vector3I, MeshBuffers>();

	public override void _Ready()
	{
		Global = GetNode<Global>("/root/Global");
		Global.CubeManager = this;

		surfaceLevel = Global.SurfaceLevel;

		Simplex4D.Reseed(noiseSeed);

		InitializeDamageSystem();
		RecalculateChunkOffsets();

		// Enforce one-node guarantee: planet must be wider than render window
		int minChunks = RenderDistance * 2 + 1;
		if (Global.PlanetChunksX <= RenderDistance * 2)
		{
			GD.PrintErr($"[ChunkManager] PlanetChunksX ({Global.PlanetChunksX}) too small for RenderDistance ({RenderDistance}). Clamping to {minChunks}.");
			Global.PlanetChunksX = minChunks;
		}
		if (Global.PlanetChunksZ <= RenderDistance * 2)
		{
			GD.PrintErr($"[ChunkManager] PlanetChunksZ ({Global.PlanetChunksZ}) too small for RenderDistance ({RenderDistance}). Clamping to {minChunks}.");
			Global.PlanetChunksZ = minChunks;
		}

		// Size both worker pools to the machine. Generation (heavy 4D-simplex terrain +
		// cave density) and meshing run as parallel stages on different chunks, so each
		// gets its own pool. Leave ~2 logical cores for the main thread + Godot servers.
		int cores = System.Environment.ProcessorCount;
		int genCount  = Mathf.Clamp((cores - 2) / 2, 2, 4);
		int meshCount = Mathf.Clamp((cores - 2) / 2, 2, 4);

		generationThreads = new Thread[genCount];
		for (int i = 0; i < generationThreads.Length; i++)
		{
			generationThreads[i] = new Thread(GenerationWorkerLoop);
			generationThreads[i].Name = $"ChunkGeneration_{i}";
			generationThreads[i].Start();
		}

		loadingThreads = new Thread[meshCount];
		for (int i = 0; i < loadingThreads.Length; i++)
		{
			loadingThreads[i] = new Thread(LoadingWorkerLoop);
			loadingThreads[i].Name = $"MeshGeneration_{i}";
			loadingThreads[i].Start();
		}
	}

	public override void _ExitTree()
	{
		threadsRunning = false;
		lock (generationLock) Monitor.PulseAll(generationLock);
		lock (loadingLock) Monitor.PulseAll(loadingLock);
		if (generationThreads != null)
			foreach (var t in generationThreads)
				t?.Join(1000);
		if (loadingThreads != null)
			foreach (var t in loadingThreads)
				t?.Join(1000);
	}

	public override void _Process(double delta)
	{
		_promotionsThisFrame = 0;
		ulong promoteStartUsec = Time.GetTicksUsec();
		ulong promoteBudgetUsec = (ulong)(MaxPromotionMillisPerFrame * 1000.0);
		while (_readyToPromote.TryPeek(out var pos))
		{
			// Stale entry (chunk unloaded / buffer gone) — drop it, costs nothing.
			if (!pendingBuffers.TryGetValue(pos, out var pb))
			{
				_readyToPromote.TryDequeue(out _);
				continue;
			}
			// Empty/solid promotions are ~free; only real mesh builds are throttled.
			bool expensive = pb.VertexCount != 0 && pb.IndexCount != 0;
			if (expensive && _promotionsThisFrame > 0)
			{
				// Stop once this frame's time budget (or the safety ceiling) is spent.
				if (_promotionsThisFrame >= MaxPromotionsPerFrame) break;
				if (Time.GetTicksUsec() - promoteStartUsec >= promoteBudgetUsec) break;
			}

			_readyToPromote.TryDequeue(out _);
			if (PromoteChunk(pos))
				_promotionsThisFrame++;
		}

		timeElapsed += (float)delta;
		if (timeElapsed >= TIME_HANDLE)
		{
			timeElapsed -= TIME_HANDLE;
			handle_chunks_art();
			handle_dirties();
		}

		FlushDirtyDamageOverlays();

		if (ShowDamageOverlays != _lastShowDamageOverlays)
		{
			_lastShowDamageOverlays = ShowDamageOverlays;
			foreach (var inst in damageOverlaysByBlock.Values)
				if (inst != null && GodotObject.IsInstanceValid(inst))
					inst.Visible = ShowDamageOverlays;
		}

		if (DebugPerfReadout)
		{
			_perfReadoutTimer += (float)delta;
			if (_perfReadoutTimer >= 1f)
			{
				_perfReadoutTimer = 0f;
				DebugPerfReadoutPrint();
			}
		}
	}

	private void DebugPerfReadoutPrint()
	{
		int meshNodes = 0;
		int damagedCount = 0;
		foreach (var kv in chunks)
		{
			if (kv.Value.MeshInstance != null) meshNodes++;
			if (kv.Value.DamageData != null) damagedCount += kv.Value.DamageData.Count;
		}

		int canonTotal = 0, canonEdited = 0;
		lock (_canonicalLock)
		{
			canonTotal = _canonicalStore.Count;
			foreach (var kv in _canonicalStore)
				if (kv.Value.WasEdited) canonEdited++;
		}

		int sceneChildren = GetChildCount();           // mesh chunks + ~16 damage overlays + any orphans
		int treeNodes = GetTree().GetNodeCount();       // whole scene tree — catches enemy/projectile leaks

		GD.Print(
			$"[PERF] fps={Engine.GetFramesPerSecond():0} " +
			$"chunks={chunks.Count} meshNodes={meshNodes} sceneChildren={sceneChildren} treeNodes={treeNodes} " +
			$"canon={canonTotal}(edited={canonEdited}) dmg={damagedCount} dirty={dirtyChunks.Count} " +
			$"readyToPromote={_readyToPromote.Count} pendingBuf={pendingBuffers.Count} " +
			$"remeshes/s={_meshRebuildsThisSecond}");

		_meshRebuildsThisSecond = 0;
	}

	public void handle_chunks_art()
	{
		Vector3 pPos = Global.GetPlayerPos();
		Vector3I playerPos = ((Vector3I)pPos) / CHUNK_SIZE;

		if (playerPos != lastPlayerChunkPos)
		{
			lastPlayerChunkPos = playerPos;

			// Prune stale entries AND rebuild the worker queues closest-first by iterating the
			// SMALL pending sets, not the full offset sphere. The old version walked all
			// cachedChunkOffsets (O(render-volume)) twice per crossing — a major spike at high RD.
			var genKeys = generationQueue.Keys.ToList();
			genKeys.RemoveAll(cp => { if (!cachedOffsetSet.Contains(cp - playerPos)) { generationQueue.TryRemove(cp, out _); return true; } return false; });
			genKeys.Sort((a, b) => (a - playerPos).LengthSquared().CompareTo((b - playerPos).LengthSquared()));
			lock (generationLock)
			{
				generationWorkQueue.Clear();
				foreach (var cp in genKeys) generationWorkQueue.Enqueue(cp);
				Monitor.Pulse(generationLock);
			}

			var loadKeys = loadingQueue.Keys.ToList();
			loadKeys.RemoveAll(cp => { if (!cachedOffsetSet.Contains(cp - playerPos)) { loadingQueue.TryRemove(cp, out _); return true; } return false; });
			loadKeys.Sort((a, b) => (a - playerPos).LengthSquared().CompareTo((b - playerPos).LengthSquared()));
			lock (loadingLock)
			{
				loadingWorkQueue.Clear();
				foreach (var cp in loadKeys) loadingWorkQueue.Enqueue(cp);
				Monitor.Pulse(loadingLock);
			}

			// Edited-chunk eviction is a full O(canonical-store) scan but rarely needs to act —
			// throttle it to a few seconds instead of running on every crossing.
			ulong nowMsec = Time.GetTicksMsec();
			if (nowMsec - _lastEvictMsec > 3000)
			{
				_lastEvictMsec = nowMsec;
				EvictColdEditedChunks(playerPos);
			}

			// Unload out-of-range chunks — only needed when active set changes.
			chunksToUnload.Clear();
			foreach (var chunkPos in chunks.Keys)
			{
				if (!cachedOffsetSet.Contains(chunkPos - playerPos))
					chunksToUnload.Add(chunkPos);
			}
			foreach (var chunkPos in chunksToUnload)
			{
				unload(chunkPos);
				lock (queueLock)
				{
					activeChunks.Remove(chunkPos);
					loadingQueue.TryRemove(chunkPos, out _);
				}
			}

		}

		int renderDistSq = RenderDistance * RenderDistance;
		int offsetCount = cachedChunkOffsets.Count;
		if (offsetCount > 0)
		{
			// Process one slice of the full offset volume per tick instead of all of it —
			// see _sweepCursor declaration for why.
			if (_sweepCursor >= offsetCount) _sweepCursor = 0;
			int sliceSize = (offsetCount + SWEEP_SLICES - 1) / SWEEP_SLICES;
			int start = _sweepCursor;
			int end = Math.Min(start + sliceSize, offsetCount);

			for (int idx = start; idx < end; idx++)
			{
				var offset = cachedChunkOffsets[idx];
				var chunkPos = playerPos + offset;
				bool shouldBeVisible = offset.LengthSquared() <= renderDistSq; // squared compare — no per-chunk sqrt

				if (chunks.TryGetValue(chunkPos, out var chunk))
				{
					if (!chunk.Generated && !generationQueue.ContainsKey(chunkPos))
					{
						generationQueue[chunkPos] = 1;
						lock (generationLock)
						{
							generationWorkQueue.Enqueue(chunkPos);
							Monitor.Pulse(generationLock);
						}
					}

					if (shouldBeVisible && chunk.Generated && !chunk.Loaded)
					{
						bool allNeighborsExist = true;
						for (int i = 0; i < 6; i++)
						{
							if (!chunks.ContainsKey(chunkPos + FaceOffsets[i]))
							{
								allNeighborsExist = false;
								break;
							}
						}
						if (allNeighborsExist)
						{
							bool allGenerated = true;
							for (int i = 0; i < 6; i++)
							{
								if (!chunks[chunkPos + FaceOffsets[i]].Generated)
								{
									allGenerated = false;
									break;
								}
							}
							if (allGenerated && !loadingQueue.ContainsKey(chunkPos))
							{
								loadingQueue[chunkPos] = 1;
								lock (loadingLock)
								{
									loadingWorkQueue.Enqueue(chunkPos);
									Monitor.Pulse(loadingLock);
								}
							}
						}
					}
				}
				else
				{
					if (!generationQueue.ContainsKey(chunkPos))
					{
						generationQueue[chunkPos] = 1;
						chunks[chunkPos] = new Chunk(chunkPos);
						lock (generationLock)
						{
							generationWorkQueue.Enqueue(chunkPos);
							Monitor.Pulse(generationLock);
						}
					}
				}
			}

			_sweepCursor = end >= offsetCount ? 0 : end;
		}
	}

	public void handle_dirties()
	{
		if (dirtyChunks.Count == 0)
			return;

		dirtyChunksList.Clear();
		foreach (var chunkPos in dirtyChunks)
		{
				if (chunks.TryGetValue(chunkPos, out var chunk))
				{
					chunk.Dirty = false;
					loadingQueue[chunkPos] = 1;
					dirtyChunksList.Add(chunkPos);
				}
		}
		dirtyChunks.Clear();

		if (dirtyChunksList.Count > 0)
		{
			lock (loadingLock)
			{
				foreach (var chunkPos in dirtyChunksList)
				{
					loadingWorkQueue.Enqueue(chunkPos);
				}
				Monitor.Pulse(loadingLock);
			}
		}
	}

	private void RecalculateChunkOffsets()
	{
		cachedChunkOffsets.Clear();
		int generationDistance = RenderDistance + 1;

		switch(RenderModeType)
		{
			case RenderMode.Cylinder: //renders in a cylinder from bottom to top
				for (int y = -generationDistance; y <= generationDistance; y++)
				{
					for (int x = -generationDistance; x <= generationDistance; x++)
					{
						for (int z = -generationDistance; z <= generationDistance; z++)
						{
							var offset = new Vector3I(x, y, z);
							var tempOffset = new Vector3I(x, 0, z);
							if (tempOffset.Length() > generationDistance)
								continue;
							cachedChunkOffsets.Add(offset);
						}
					}
				}

				cachedChunkOffsets.RemoveAll(offset => (offset.X * offset.X + offset.Z * offset.Z) > (generationDistance * generationDistance));
				break;
			case RenderMode.Sphere: //renders in a sphere sorted by closest to furthest
				for (int x = -generationDistance; x <= generationDistance; x++)
				{
					for (int y = -generationDistance; y <= generationDistance; y++)
					{
						for (int z = -generationDistance; z <= generationDistance; z++)
						{
							var offset = new Vector3I(x, y, z);
							if (offset.Length() > generationDistance)
								continue;
							cachedChunkOffsets.Add(offset);
						}
					}
				}
				// already filtered by length above
				cachedChunkOffsets.Sort((a, b) => a.LengthSquared().CompareTo(b.LengthSquared()));
				break;
		}

		// Set form for O(1) "is this offset in range" checks during the per-crossing unload sweep.
		cachedOffsetSet = new HashSet<Vector3I>(cachedChunkOffsets);
	}

	public void unload(Vector3I position)
	{
		if (!chunks.TryGetValue(position, out var chunk))
			return;

		ClearDamageInChunk(position);
		pendingBuffers.TryRemove(position, out _);

		if (chunk.MeshInstance != null && GodotObject.IsInstanceValid(chunk.MeshInstance))
		{
			if (chunk.MeshInstance.GetParent() != null)
				RemoveChild(chunk.MeshInstance);
			chunk.MeshInstance.QueueFree();
		}

		// Canonical store holds the voxel array reference for edited chunks.
		// Drop the raw chunk's ref so the array isn't kept alive redundantly.
		chunk.Voxels      = null;
		chunk.MeshInstance = null;
		chunk.Loaded      = false;

		// Drop unedited canonical data — it regenerates identically next time.
		var canonicalPos = Global.CanonicalChunkPos(position);
		lock (_canonicalLock)
		{
			if (_canonicalStore.TryGetValue(canonicalPos, out var cd) && !cd.WasEdited)
				_canonicalStore.Remove(canonicalPos);
		}

		// Always remove the raw chunk entry; canonical store is the persistent owner.
		generationQueue.TryRemove(position, out _);
		loadingQueue.TryRemove(position, out _);
		pendingBuffers.TryRemove(position, out _);
		chunks.Remove(position);
	}

	private void GenerationWorkerLoop()
	{
		while (threadsRunning)
		{
			Vector3I position;
			lock (generationLock)
			{
				while (generationWorkQueue.Count == 0 && threadsRunning)
					Monitor.Wait(generationLock);
				if (!threadsRunning) break;
				position = generationWorkQueue.Dequeue();
			}
			generate_data(position);
		}
	}

	private void LoadingWorkerLoop()
	{
		while (threadsRunning)
		{
			Vector3I position;
			lock (loadingLock)
			{
				while (loadingWorkQueue.Count == 0 && threadsRunning)
					Monitor.Wait(loadingLock);
				if (!threadsRunning) break;
				position = loadingWorkQueue.Dequeue();
			}
			//skip loading chunk if it is outside of the loading range of the player
			if((chunk_to_world(position)-Global.GetPlayerPos()).Length() > (RenderDistance + 1) * CHUNK_SIZE)
			{
				loadingQueue.TryRemove(position, out _);
				continue;
			}
			load_calculate(position);
		}
	}

	public void generate_data(Vector3I position)
	{
		if (!chunks.TryGetValue(position, out var chunk))
			return;

		var canonicalPos = Global.CanonicalChunkPos(position);

		// Check canonical store first — reuse edited (or previously generated) data
		ChunkData cd;
		lock (_canonicalLock)
			_canonicalStore.TryGetValue(canonicalPos, out cd);

		if (cd != null)
		{
			chunk.Voxels      = cd.Voxels;
			chunk.IsFullySolid = cd.IsFullySolid;
			chunk.IsAllAir    = cd.IsAllAir;
			chunk.WasEdited   = cd.WasEdited;
			CallDeferred("generate_ready_chunk", position);
			return;
		}

		// Fresh generation — use canonical position so terrain repeats across laps
		byte[] data = create_chunk_data(canonicalPos);

		// Single pass tracking both uniformity flags. Break early only once the chunk is
		// confirmed mixed (saw both a solid and an air block) — the common, expensive case
		// still exits fast. A uniformly-solid or uniformly-air chunk runs the full scan.
		bool isFullySolid = true;
		bool isAllAir = true;
		for (int i = 0; i < data.Length; i++)
		{
			if (data[i] == 0) isFullySolid = false;
			else              isAllAir = false;
			if (!isFullySolid && !isAllAir) break;
		}

		chunk.Voxels       = data;
		chunk.IsFullySolid = isFullySolid;
		chunk.IsAllAir     = isAllAir;

		// Store in canonical cache (another thread could race on the same canonical pos
		// only if planet size constraint is violated — guarded by the startup clamp)
		lock (_canonicalLock)
		{
			if (!_canonicalStore.ContainsKey(canonicalPos))
				_canonicalStore[canonicalPos] = new ChunkData { Voxels = data, IsFullySolid = isFullySolid, IsAllAir = isAllAir };
		}

		CallDeferred("generate_ready_chunk", position);
	}

	public void generate_ready_chunk(Vector3I position)
	{
		if (!chunks.TryGetValue(position, out var chunk))
			return;

		chunk.Generated = true;
		generationQueue.TryRemove(position, out _);
	}

	public void load_calculate(Vector3I position)
	{
		if (!chunks.ContainsKey(position) || generationQueue.ContainsKey(position))
			return;

		var chunk = chunks[position];

		// All-air chunks need no neighbor check — air bordering anything is still nothing
		// to draw. Fully-solid chunks only skip when surrounded by solid (no exposed faces).
		if ((chunk.IsFullySolid && adjacent_chunks_solid(position)) || chunk.IsAllAir)
			{
				// Empty/solid chunk — no mesh. Queue an empty marker for promotion.
				pendingBuffers[position] = new MeshBuffers { VertexCount = 0 };
				_readyToPromote.Enqueue(position);
				return;
			}

		int vertexCount = 0;
		int uvCount = 0;
		int indexCount = 0;

		// Reuse this thread's scratch buffers (see field declarations) — never allocate per build.
		float[] meshVerticesFlat = _tlVerts   ??= new float[8192 * 3];
		float[] meshNormalsFlat  = _tlNormals ??= new float[8192 * 3];
		float[] meshUvsFlat      = _tlUvs     ??= new float[8192 * 2];
		int[]   meshIndicesArray = _tlIndices ??= new int[12288];

		// Snapshot voxel data to avoid races with main-thread mutations (reused per thread).
		byte[] voxels = null;
		if (chunk.Voxels != null)
		{
			voxels = _tlVoxels ??= new byte[CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE];
			Array.Copy(chunk.Voxels, voxels, voxels.Length);
		}
		if (voxels == null)
		{
			// Voxel data was freed (unloaded); request regeneration and skip loading
			if (!generationQueue.ContainsKey(position))
			{
				generationQueue[position] = 1;
				lock (generationLock)
				{
					generationWorkQueue.Enqueue(position);
					Monitor.Pulse(generationLock);
				}
			}
			loadingQueue.TryRemove(position, out _);
			return;
		}
		int chunkX = position.X * CHUNK_SIZE;
		int chunkY = position.Y * CHUNK_SIZE;
		int chunkZ = position.Z * CHUNK_SIZE;

		// Resolve the 6 face-neighbor voxel arrays once, instead of a dictionary lookup
		// per edge block (get_block). A null entry means the neighbor isn't available —
		// treated as air, matching get_block's behaviour. Reads the live array, same as
		// the per-block path did, so no new race is introduced.
		byte[][] neighborVoxels = new byte[6][];
		for (int f = 0; f < 6; f++)
		{
			if (chunks.TryGetValue(position + FaceOffsets[f], out var nc) &&
				nc.Generated && nc.Voxels != null)
				neighborVoxels[f] = nc.Voxels;
		}

		for (int y = 0; y < CHUNK_SIZE; y++)
		{
			for (int z = 0; z < CHUNK_SIZE; z++)
			{
				for (int x = 0; x < CHUNK_SIZE; x++)
				{
					int voxelIdx = voxel_index(x, y, z);
					byte blockId = voxels[voxelIdx];

					if (blockId == 0) continue;

					Block_Definition blockDef = Block_Registry.Blocks[blockId];
					if (blockDef == null || blockDef.faceUVs == null) continue;

					Block_Model model = blockDef.Model;
					if (model == null || model.Vertices == null) continue;

					int numFaces = model.Vertices.Length / 4;
					bool isCube = model.type == Block_Model.Type.Cube;

					for (int face = 0; face < numFaces; face++)
					{
						if (isCube && face < 6)
						{
							Vector3I offset = FaceOffsets[face];
							int nx = x + offset.X;
							int ny = y + offset.Y;
							int nz = z + offset.Z;

							bool isAir;
							if ((uint)nx < CHUNK_SIZE && (uint)ny < CHUNK_SIZE && (uint)nz < CHUNK_SIZE)
							{
								isAir = voxels[voxel_index(nx, ny, nz)] == 0;
							}
							else
							{
								// Block lies on a chunk face — read straight from the pre-resolved
								// neighbor. Only the face axis is out of [0,CHUNK_SIZE); wrap it.
								byte[] nv = neighborVoxels[face];
								if (nv == null)
								{
									isAir = true;
								}
								else
								{
									int lx = nx < 0 ? nx + CHUNK_SIZE : (nx >= CHUNK_SIZE ? nx - CHUNK_SIZE : nx);
									int ly = ny < 0 ? ny + CHUNK_SIZE : (ny >= CHUNK_SIZE ? ny - CHUNK_SIZE : ny);
									int lz = nz < 0 ? nz + CHUNK_SIZE : (nz >= CHUNK_SIZE ? nz - CHUNK_SIZE : nz);
									isAir = nv[voxel_index(lx, ly, lz)] == 0;
								}
							}

							if (!isAir) continue;
						}

						int neededVertices = vertexCount + 12;
						int neededUvs = uvCount + 8;
						int neededIndices = indexCount + 6;

						// Rare: a chunk exceeds the pre-sized scratch. Grow and persist the larger
						// buffer back to the thread-static field so it's reused next build too.
						if (neededVertices > meshVerticesFlat.Length)
						{
							Array.Resize(ref meshVerticesFlat, meshVerticesFlat.Length * 2);
							Array.Resize(ref meshNormalsFlat, meshNormalsFlat.Length * 2);
							_tlVerts = meshVerticesFlat;
							_tlNormals = meshNormalsFlat;
						}
						if (neededUvs > meshUvsFlat.Length)
						{
							Array.Resize(ref meshUvsFlat, meshUvsFlat.Length * 2);
							_tlUvs = meshUvsFlat;
						}
						if (neededIndices > meshIndicesArray.Length)
						{
							Array.Resize(ref meshIndicesArray, meshIndicesArray.Length * 2);
							_tlIndices = meshIndicesArray;
						}

						int baseVertex = vertexCount / 3;
						float fx = x, fy = y, fz = z;

						int vertStart = face * 4;
						Vector2[][] uvs = blockDef.faceUVs;
						int uvFace = face < uvs.Length ? face : face % 6;

						for (int i = 0; i < 4; i++)
						{
							Vector3 vert = model.Vertices[vertStart + i];
							Vector3 norm = model.Normals[vertStart + i];

							meshVerticesFlat[vertexCount++] = vert.X + fx;
							meshVerticesFlat[vertexCount++] = vert.Y + fy;
							meshVerticesFlat[vertexCount++] = vert.Z + fz;

							meshNormalsFlat[vertexCount - 3] = norm.X;
							meshNormalsFlat[vertexCount - 2] = norm.Y;
							meshNormalsFlat[vertexCount - 1] = norm.Z;

							meshUvsFlat[uvCount++] = uvs[uvFace][i].X;
							meshUvsFlat[uvCount++] = uvs[uvFace][i].Y;
						}

						int indicesStart = face * 6;
						for (int i = 0; i < 6; i++)
						{
							meshIndicesArray[indexCount++] = baseVertex + (model.Indices[indicesStart + i] - vertStart);
						}
					}
				}
			}
		}

		int vCount = vertexCount / 3;
		int uCount = uvCount / 2;

		// Rent typed arrays from ArrayPool where possible to reduce allocations
		var buffers = new MeshBuffers { VertexCount = vCount, UvCount = uCount, IndexCount = indexCount };

		Vector3[] rentedVerts = ArrayPool<Vector3>.Shared.Rent(vCount);
		Vector3[] rentedNormals = ArrayPool<Vector3>.Shared.Rent(vCount);
		Vector2[] rentedUVs = ArrayPool<Vector2>.Shared.Rent(uCount);
		int[] rentedIndices = ArrayPool<int>.Shared.Rent(indexCount);

		for (int i = 0; i < vCount; i++)
		{
			int idx = i * 3;
			rentedVerts[i] = new Vector3(meshVerticesFlat[idx], meshVerticesFlat[idx + 1], meshVerticesFlat[idx + 2]);
			rentedNormals[i] = new Vector3(meshNormalsFlat[idx], meshNormalsFlat[idx + 1], meshNormalsFlat[idx + 2]);
		}

		for (int i = 0; i < uCount; i++)
		{
			int idx = i * 2;
			rentedUVs[i] = new Vector2(meshUvsFlat[idx], meshUvsFlat[idx + 1]);
		}

		for (int i = 0; i < indexCount; i++)
			rentedIndices[i] = meshIndicesArray[i];

		// If the rented arrays are exactly the requested size, mark them as from-pool and pass directly.
		// Otherwise create exact-sized arrays and copy the used portion, returning rented arrays.
		if (rentedVerts.Length == vCount)
		{
			buffers.Vertices = rentedVerts;
			buffers.VerticesFromPool = true;
		}
		else
		{
			buffers.Vertices = new Vector3[vCount];
			Array.Copy(rentedVerts, buffers.Vertices, vCount);
			ArrayPool<Vector3>.Shared.Return(rentedVerts, clearArray: true);
			buffers.VerticesFromPool = false;
		}

		if (rentedNormals.Length == vCount)
		{
			buffers.Normals = rentedNormals;
			buffers.NormalsFromPool = true;
		}
		else
		{
			buffers.Normals = new Vector3[vCount];
			Array.Copy(rentedNormals, buffers.Normals, vCount);
			ArrayPool<Vector3>.Shared.Return(rentedNormals, clearArray: true);
			buffers.NormalsFromPool = false;
		}

		if (rentedUVs.Length == uCount)
		{
			buffers.UVs = rentedUVs;
			buffers.UVsFromPool = true;
		}
		else
		{
			buffers.UVs = new Vector2[uCount];
			Array.Copy(rentedUVs, buffers.UVs, uCount);
			ArrayPool<Vector2>.Shared.Return(rentedUVs, clearArray: true);
			buffers.UVsFromPool = false;
		}

		if (rentedIndices.Length == indexCount)
		{
			buffers.Indices = rentedIndices;
			buffers.IndicesFromPool = true;
		}
		else
		{
			buffers.Indices = new int[indexCount];
			Array.Copy(rentedIndices, buffers.Indices, indexCount);
			ArrayPool<int>.Shared.Return(rentedIndices, clearArray: true);
			buffers.IndicesFromPool = false;
		}

		// Hand off to the main-thread promotion drain (see _Process). No CallDeferred —
		// the buffer carries its own counts, so nothing can be stranded by a lost deferred call.
		pendingBuffers[position] = buffers;
		_readyToPromote.Enqueue(position);
	}

	// Promotes one finished chunk. Counts come from its MeshBuffers (self-contained).
	// Returns true only when a real mesh was built and attached (an expensive promotion).
	private bool PromoteChunk(Vector3I position)
	{
		if (!chunks.TryGetValue(position, out var chunk))
		{
			// ensure buffers are freed if present
			if (pendingBuffers.TryRemove(position, out var _)) { }
			return false;
		}

		if (!pendingBuffers.TryRemove(position, out var buffers))
			return false; // already consumed (duplicate enqueue) — nothing to do

		int vertCount = buffers.VertexCount;
		int uvCount   = buffers.UvCount;
		int idxCount  = buffers.IndexCount;

		if (vertCount == 0 || idxCount == 0)
			{
			if (chunk.MeshInstance != null && GodotObject.IsInstanceValid(chunk.MeshInstance))
			{
				if (chunk.MeshInstance.GetParent() != null)
					RemoveChild(chunk.MeshInstance);
				chunk.MeshInstance.QueueFree();
				chunk.MeshInstance = null;
			}
			chunk.Loaded = true;
				loadingQueue.TryRemove(position, out _);
				lock (queueLock) { activeChunks.Add(position); }
			return false;
		}

		Mesh newMesh = create_mesh_from_data(buffers.Vertices, buffers.Normals, buffers.UVs, buffers.Indices, vertCount, uvCount, idxCount);
		_meshRebuildsThisSecond++;

		if (chunk.MeshInstance != null && GodotObject.IsInstanceValid(chunk.MeshInstance))
		{
			chunk.MeshInstance.Mesh = newMesh;
		}
		else
		{
			chunk.MeshInstance = new MeshInstance3D();
			chunk.MeshInstance.MaterialOverride = Mat;
			chunk.MeshInstance.Transform = new Transform3D(chunk.MeshInstance.Transform.Basis, position * new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE));
			chunk.MeshInstance.Mesh = newMesh;
			AddChild(chunk.MeshInstance);
		}

		chunk.Loaded = true;
		loadingQueue.TryRemove(position, out _);
		lock (queueLock)
		{
			activeChunks.Add(position);
		}

		// Return rented buffers to pools when applicable
		if (buffers != null)
		{
			if (buffers.VerticesFromPool && buffers.Vertices != null)
				ArrayPool<Vector3>.Shared.Return(buffers.Vertices, clearArray: true);
			if (buffers.NormalsFromPool && buffers.Normals != null)
				ArrayPool<Vector3>.Shared.Return(buffers.Normals, clearArray: true);
			if (buffers.UVsFromPool && buffers.UVs != null)
				ArrayPool<Vector2>.Shared.Return(buffers.UVs, clearArray: true);
			if (buffers.IndicesFromPool && buffers.Indices != null)
				ArrayPool<int>.Shared.Return(buffers.Indices, clearArray: true);
		}

		return true;
	}

	public byte[] create_chunk_data(Vector3I chunkPos)
	{
		var p = Global.Instance.ActivePlanet;
		byte[] data = new byte[CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE];

		float twoPi = 2f * Mathf.Pi;
		float invW  = twoPi / Global.PlanetWidth;
		float invD  = twoPi / Global.PlanetDepth;

		// Chasm shaft anchor: planet center in canonical space, drifts via sin so entrance is at anchor.
		float chasmOriginX = Global.PlanetWidth  / 2f;
		float chasmOriginZ = Global.PlanetDepth  / 2f;

		for (int x = 0; x < CHUNK_SIZE; x++)
		{
			int   worldX = chunkPos.X * CHUNK_SIZE + x;
			float thetaX = worldX * invW;
			float cosX   = Mathf.Cos(thetaX);
			float sinX   = Mathf.Sin(thetaX);

			for (int z = 0; z < CHUNK_SIZE; z++)
			{
				int   worldZ = chunkPos.Z * CHUNK_SIZE + z;
				float thetaZ = worldZ * invD;
				float cosZ   = Mathf.Cos(thetaZ);
				float sinZ   = Mathf.Sin(thetaZ);

				float height = p.FillSolid ? 0f : Simplex4D.Sample(
					cosX * p.NoiseScale, sinX * p.NoiseScale,
					cosZ * p.NoiseScale, sinZ * p.NoiseScale)
					* p.HeightAmplitude + surfaceLevel;

				for (int y = 0; y < CHUNK_SIZE; y++)
				{
					int worldY = chunkPos.Y * CHUNK_SIZE + y;

					bool solid = p.FillSolid ? true : worldY <= height;

					// Cave carving — true 3D density field.
					// Y is encoded as a phase offset to both torus axes so the density field
					// genuinely varies in all three dimensions while X/Z remain seam-seamless.
					// Two octaves: large chambers (base) + connecting passages (×2 freq, ×0.5 amp).
					// Cave where combined density > CaveThreshold.
					if (solid && p.CavesEnabled && (p.CaveFullRange || worldY < Global.SurfaceLevel))
					{
						float s  = p.CaveScale;
						// Phase offsets grow linearly with depth; different ratios per axis
						// so the pattern doesn't repeat symmetrically.
						float phX = worldY * invW * p.CaveYFrequency;
						float phZ = worldY * invD * p.CaveYFrequency * 0.71f;

						float d1 = Simplex4D.Sample(cosX * s + phX, sinX * s,  cosZ * s + phZ, sinZ * s);
						float d2 = Simplex4D.Sample(cosX * s * 2f + phZ, sinX * s * 2f,
													cosZ * s * 2f - phX, sinZ * s * 2f) * 0.5f;

						if (d1 + d2 > p.CaveThreshold)
							solid = false;
					}

					// Chasm carving — sinusoidal shaft anchored at planet center
					if (solid && p.ChasmEnabled)
					{
						float cx = chasmOriginX + Mathf.Sin(worldY * p.ChasmDriftScale)        * 60f;
						float cz = chasmOriginZ + Mathf.Sin(worldY * p.ChasmDriftScale * 0.7f) * 60f;
						float dx = worldX - cx;
						float dz = worldZ - cz;
						if (dx * dx + dz * dz < p.ChasmRadius * p.ChasmRadius)
							solid = false;
					}

					// Crash site — guaranteed open ellipsoid near spawn (Cave template).
					// Runs last so it can't be re-filled by any earlier carver.
					if (solid && p.SpawnClearEnabled)
					{
						int   sx = Global.Instance.WorldSpawn.X;
						int   sy = Global.Instance.WorldSpawn.Y;
						int   sz = Global.Instance.WorldSpawn.Z;
						float ex = (worldX - sx) / p.SpawnClearRadiusXZ;
						float ey = (worldY - sy) / p.SpawnClearRadiusY;
						float ez = (worldZ - sz) / p.SpawnClearRadiusXZ;
						if (ex * ex + ey * ey + ez * ez <= 1f)
							solid = false;
					}

					data[voxel_index(x, y, z)] = solid ? p.SurfaceBlock : (byte)0;
				}
			}
		}
		return data;
	}

	public Mesh create_mesh_from_data(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices, int vertCount, int uvCount, int idxCount)
	{
		// Use provided arrays directly (they are sized to the exact counts)
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);

		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

		return arrayMesh;
	}

	// Disk persistence removed — chunks are kept in memory metadata only.

	public int get_block(Vector3I position)
	{
		var chunkPos = world_to_chunk(position);

		if (!chunks.TryGetValue(chunkPos, out var chunk))
			return 0;

		if (!chunk.Generated || chunk.Voxels == null)
			return 0;

		byte[] voxels = chunk.Voxels;

		Vector3I localPos = new Vector3I(
			position.X - (chunkPos.X * CHUNK_SIZE),
			position.Y - (chunkPos.Y * CHUNK_SIZE),
			position.Z - (chunkPos.Z * CHUNK_SIZE)
		);

		if (localPos.X < 0 || localPos.X >= CHUNK_SIZE ||
			localPos.Y < 0 || localPos.Y >= CHUNK_SIZE ||
			localPos.Z < 0 || localPos.Z >= CHUNK_SIZE)
		{
			return 0;
		}

		int index = voxel_index(localPos);
		return voxels[index];
	}

	public void set_block(Vector3I position, int blockId)
	{
		var chunkPos = world_to_chunk(position);
		if (!chunks.ContainsKey(chunkPos))
			return;

		// Remove damage overlay if block is being destroyed
		if (blockId == 0)
			RemoveBlockDamage(position);

		// Mark that this chunk has been edited by the player
		chunks[chunkPos].WasEdited = true;

		// Persist the edit in canonical store so it survives unload and shows on future laps.
		// Clear IsFullySolid on removal too, or a reused edited chunk would wrongly take the
		// solid fast-path on reload and render invisible.
		lock (_canonicalLock)
		{
			if (_canonicalStore.TryGetValue(Global.CanonicalChunkPos(chunkPos), out var cd))
			{
				cd.WasEdited = true;
				if (blockId == 0) cd.IsFullySolid = false;
				else              cd.IsAllAir = false;
			}
		}

		if (!chunks[chunkPos].Dirty)
		{
			chunks[chunkPos].Dirty = true;
			dirtyChunks.Add(chunkPos);
		}

		Vector3I localPos = new Vector3I(
			position.X - (chunkPos.X * CHUNK_SIZE),
			position.Y - (chunkPos.Y * CHUNK_SIZE),
			position.Z - (chunkPos.Z * CHUNK_SIZE)
		);

		if (localPos.X < 0) localPos.X += CHUNK_SIZE;
		if (localPos.Y < 0) localPos.Y += CHUNK_SIZE;
		if (localPos.Z < 0) localPos.Z += CHUNK_SIZE;

		if (blockId == 0) chunks[chunkPos].IsFullySolid = false;
		else              chunks[chunkPos].IsAllAir = false;
		chunks[chunkPos].Voxels[voxel_index(localPos)] = (byte)blockId;

		if (localPos.X == 0) mark_neighbor_dirty(chunkPos + new Vector3I(-1, 0, 0));
		if (localPos.X == CHUNK_SIZE - 1) mark_neighbor_dirty(chunkPos + new Vector3I(1, 0, 0));
		if (localPos.Y == 0) mark_neighbor_dirty(chunkPos + new Vector3I(0, -1, 0));
		if (localPos.Y == CHUNK_SIZE - 1) mark_neighbor_dirty(chunkPos + new Vector3I(0, 1, 0));
		if (localPos.Z == 0) mark_neighbor_dirty(chunkPos + new Vector3I(0, 0, -1));
		if (localPos.Z == CHUNK_SIZE - 1) mark_neighbor_dirty(chunkPos + new Vector3I(0, 0, 1));
	}

	// Apply multiple block changes in a single batch to avoid per-block queueing
	public void set_blocks_batch(List<(Vector3I pos, int blockId)> changes)
	{
		if (changes == null || changes.Count == 0) return;

		var dirtySet = new HashSet<Vector3I>();

		foreach (var change in changes)
		{
			var chunkPos = world_to_chunk(change.pos);
			if (!chunks.ContainsKey(chunkPos)) continue;

			// Remove damage overlay if block is being destroyed
			if (change.blockId == 0)
				RemoveBlockDamage(change.pos);

			Vector3I localPos = new Vector3I(
				change.pos.X - (chunkPos.X * CHUNK_SIZE),
				change.pos.Y - (chunkPos.Y * CHUNK_SIZE),
				change.pos.Z - (chunkPos.Z * CHUNK_SIZE)
			);

			if (localPos.X < 0) localPos.X += CHUNK_SIZE;
			if (localPos.Y < 0) localPos.Y += CHUNK_SIZE;
			if (localPos.Z < 0) localPos.Z += CHUNK_SIZE;

			var chunk = chunks[chunkPos];
			chunk.Voxels[voxel_index(localPos)] = (byte)change.blockId;
			chunk.WasEdited = true;
			chunk.IsFullySolid = false;
			chunk.IsAllAir = false;
			chunk.Dirty = true;
			dirtySet.Add(chunkPos);

			lock (_canonicalLock)
			{
				if (_canonicalStore.TryGetValue(Global.CanonicalChunkPos(chunkPos), out var cd))
				{
					cd.WasEdited = true;
					cd.IsFullySolid = false;
					cd.IsAllAir = false;
				}
			}

			if (localPos.X == 0) dirtySet.Add(chunkPos + new Vector3I(-1, 0, 0));
			if (localPos.X == CHUNK_SIZE - 1) dirtySet.Add(chunkPos + new Vector3I(1, 0, 0));
			if (localPos.Y == 0) dirtySet.Add(chunkPos + new Vector3I(0, -1, 0));
			if (localPos.Y == CHUNK_SIZE - 1) dirtySet.Add(chunkPos + new Vector3I(0, 1, 0));
			if (localPos.Z == 0) dirtySet.Add(chunkPos + new Vector3I(0, 0, -1));
			if (localPos.Z == CHUNK_SIZE - 1) dirtySet.Add(chunkPos + new Vector3I(0, 0, 1));
		}

		// Mark chunks dirty once and enqueue a single batch of load work
		foreach (var cpos in dirtySet)
		{
			if (chunks.TryGetValue(cpos, out var c) && c.Generated)
			{
				c.Dirty = true;
				dirtyChunks.Add(cpos);
			}
		}
	}

	public void explode(Vector3I center, float radius, float damage)
	{
		int r = Mathf.CeilToInt(radius);
		float r2 = radius * radius;
		var batch = new List<(Vector3I pos, int blockId)>();

		// Cache the last resolved chunk across iterations — at CHUNK_SIZE 48 a radius-40
		// blast only ever touches a handful of chunks, but the naive per-voxel get_block()
		// path did a fresh dictionary lookup for every one of the ~270k voxels in the sphere.
		Vector3I cachedChunkPos = default;
		Chunk cachedChunk = null;
		bool haveCachedChunk = false;

		// One lock for the whole blast instead of one lock acquisition per voxel inside
		// damage_block — at large radii that's the difference between 1 and ~250k locks.
		lock (damageLock)
		{
			for (int x = -r; x <= r; x++)
			for (int y = -r; y <= r; y++)
			for (int z = -r; z <= r; z++)
			{
				int distSq = x * x + y * y + z * z;
				if (distSq > r2) continue;

				var worldPos = center + new Vector3I(x, y, z);
				var chunkPos = world_to_chunk(worldPos);

				if (!haveCachedChunk || chunkPos != cachedChunkPos)
				{
					if (!chunks.TryGetValue(chunkPos, out cachedChunk) || !cachedChunk.Generated || cachedChunk.Voxels == null)
						cachedChunk = null;
					cachedChunkPos = chunkPos;
					haveCachedChunk = true;
				}

				if (cachedChunk == null) continue;

				Vector3I localPos = new Vector3I(
					worldPos.X - (chunkPos.X * CHUNK_SIZE),
					worldPos.Y - (chunkPos.Y * CHUNK_SIZE),
					worldPos.Z - (chunkPos.Z * CHUNK_SIZE));

				if (localPos.X < 0 || localPos.X >= CHUNK_SIZE ||
					localPos.Y < 0 || localPos.Y >= CHUNK_SIZE ||
					localPos.Z < 0 || localPos.Z >= CHUNK_SIZE)
					continue;

				int current = cachedChunk.Voxels[voxel_index(localPos)];
				if (current == 0) continue;

				float dist = Mathf.Sqrt(distSq);
				float falloff = 1f - (dist / radius);
				if (falloff <= 0f) continue;

				if (falloff >= 1f && damage >= 1f)
				{
					batch.Add((worldPos, 0));
					continue;
				}

				float scaledDamage = damage * falloff;
				float effective = scaledDamage / GetHardness(current);
				if (effective < MinEffectiveDamage) continue;

				// partial damage: reduce health via the chunk/localPos-aware overload (lock
				// already held above) — reuses the chunk+localPos this loop already resolved
				// instead of having damage_block_locked look them up again per voxel.
				damage_block_locked(worldPos, cachedChunk, localPos, current, scaledDamage);
			}
		}

		set_blocks_batch(batch);
	}

	private void mark_neighbor_dirty(Vector3I chunkPos)
	{
		if (chunks.TryGetValue(chunkPos, out var chunk))
		{
			if (chunk.Generated && !chunk.Dirty)
			{
				chunk.Dirty = true;
				dirtyChunks.Add(chunkPos);
			}
		}
	}

	private void InitializeDamageSystem()
	{
		if (DebugDamageUseSolidMaterial)
		{
			var debugMat = new StandardMaterial3D();
			debugMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			debugMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			debugMat.AlbedoColor = new Color(1f, 0f, 1f, 0.65f);
			debugMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			debugMat.NoDepthTest = DebugDamageNoDepthTest;
			debugMat.RenderPriority = 1;
			damageOverlayMaterial = debugMat;
			GD.Print($"[DAMAGE] Using debug solid material (NoDepthTest={DebugDamageNoDepthTest})");
		}
		else
		{
			var shaderMat = new ShaderMaterial();
			shaderMat.Shader = ResourceLoader.Load<Shader>("res://Materials/BlockDamage.gdshader");
			if (DamageTexture != null)
			{
				shaderMat.SetShaderParameter("damage_texture", DamageTexture);
				// GD.Print($"Damage texture assigned: {DamageTexture.ResourcePath}");
			}
			else
			{
				GD.PrintErr("[WARNING] No DamageTexture assigned!");
			}
			shaderMat.RenderPriority = 1;
			damageOverlayMaterial = shaderMat;
		}
		// damageOverlayMaterial.TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps;
		GD.Print("Damage system initialized");
	}

	private MultiMeshInstance3D GetOrCreateDamageOverlay(int blockType)
	{
		if (damageOverlaysByBlock.ContainsKey(blockType))
			return damageOverlaysByBlock[blockType];

		Block_Definition blockDef = Block_Registry.Blocks[blockType];
		if (blockDef == null || blockDef.Model == null) return null;

		ArrayMesh blockMesh = CreateDamageBlockMesh(blockDef.Model);

		MultiMesh multiMesh = new MultiMesh();
		multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
		multiMesh.UseCustomData = true;
		multiMesh.Mesh = blockMesh;
		multiMesh.InstanceCount = OverlayInitialCapacity;  // grows on demand, see EnsureOverlayCapacity
		multiMesh.VisibleInstanceCount = 0;                // nothing visible yet

		MultiMeshInstance3D instance = new MultiMeshInstance3D();
		// Large enough to never be frustum-culled regardless of player position.
		// The node sits at world origin (TopLevel=true), so a fixed AABB would exit
		// the camera frustum as the player walks away and cull the entire MultiMesh.
		const float HalfExtent = 1e6f;
		instance.CustomAabb = new Aabb(
			new Vector3(-HalfExtent, -HalfExtent, -HalfExtent),
			new Vector3(HalfExtent * 2f, HalfExtent * 2f, HalfExtent * 2f)
		);
		instance.ExtraCullMargin = 0f;
		instance.VisibilityRangeBegin = 0f;
		instance.VisibilityRangeEnd = 0f;
		instance.TopLevel = true;
		instance.Multimesh = multiMesh;
		instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		instance.MaterialOverride = damageOverlayMaterial;
		instance.Visible = true;
		instance.IgnoreOcclusionCulling = true;

		AddChild(instance);
		instance.GlobalPosition = Vector3.Zero;

		damageOverlaysByBlock[blockType] = instance;
		damageMultiMeshByBlock[blockType] = multiMesh;

		return instance;
	}

	private ArrayMesh CreateDamageBlockMesh(Block_Model model)
	{
		Vector3[] inflatedVertices = new Vector3[model.Vertices.Length];
		bool hasNormals = model.Normals != null && model.Normals.Length == model.Vertices.Length;
		Vector3 half = new Vector3(0.5f, 0.5f, 0.5f);
		float normalOffset = Mathf.Max(0.0f, DamageOverlayNormalOffset);
		float scale = Mathf.Max(1.0f, DamageOverlayScale);

		for (int i = 0; i < model.Vertices.Length; i++)
		{
			Vector3 centered = model.Vertices[i] - half;
			if (hasNormals && normalOffset > 0.0f)
			{
				Vector3 n = model.Normals[i];
				if (n.LengthSquared() > 0.000001f)
					centered += n.Normalized() * normalOffset;
			}
			inflatedVertices[i] = centered * scale;
		}

		Vector2[] uvs = new Vector2[model.Vertices.Length];
		int numQuads = model.Vertices.Length / 4;
		for (int quad = 0; quad < numQuads; quad++)
		{
			int baseIdx = quad * 4;
			uvs[baseIdx]     = new Vector2(0, 1);
			uvs[baseIdx + 1] = new Vector2(1, 1);
			uvs[baseIdx + 2] = new Vector2(1, 0);
			uvs[baseIdx + 3] = new Vector2(0, 0);
		}
		for (int i = numQuads * 4; i < model.Vertices.Length; i++)
			uvs[i] = new Vector2(0, 0);

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = inflatedVertices;
		arrays[(int)Mesh.ArrayType.Normal] = model.Normals;
		arrays[(int)Mesh.ArrayType.TexUV]  = uvs;
		arrays[(int)Mesh.ArrayType.Index]  = model.Indices;

		ArrayMesh mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private static float GetHardness(int blockType)
	{
		var def = Block_Registry.Blocks[blockType];
		return (def != null && def.Hardness > 0f) ? def.Hardness : 1f;
	}

	public void damage_block(Vector3I position, float damage)
	{
		int blockType = get_block(position);
		if (blockType == 0) return;

		lock (damageLock)
		{
			damage_block_locked(position, blockType, damage);
		}
	}

	// Local position within a CHUNK_SIZE^3 chunk for a given world position + its already-
	// resolved chunk coord. world_to_chunk uses floor division, so this is always in
	// [0, CHUNK_SIZE) — no bounds clamping needed.
	private Vector3I WorldToLocal(Vector3I worldPos, Vector3I chunkPos)
	{
		return new Vector3I(
			worldPos.X - (chunkPos.X * CHUNK_SIZE),
			worldPos.Y - (chunkPos.Y * CHUNK_SIZE),
			worldPos.Z - (chunkPos.Z * CHUNK_SIZE));
	}

	// Resolves a world position's BlockHealth through its owning chunk's sparse DamageData.
	// Returns false if the chunk isn't loaded or has no damage tracked at that position.
	private bool TryGetBlockHealth(Vector3I worldPos, out BlockHealth health)
	{
		var chunkPos = world_to_chunk(worldPos);
		health = null;
		if (!chunks.TryGetValue(chunkPos, out var chunk) || chunk.DamageData == null)
			return false;
		return chunk.DamageData.TryGetValue(WorldToLocal(worldPos, chunkPos), out health);
	}

	// Same as damage_block, but assumes the caller already holds damageLock and already
	// knows blockType — lets batch callers (explode()) apply many hits under a single lock
	// acquisition instead of one lock + one redundant get_block per block.
	private void damage_block_locked(Vector3I position, int blockType, float damage)
	{
		var chunkPos = world_to_chunk(position);
		if (!chunks.TryGetValue(chunkPos, out var chunk)) return;
		damage_block_locked(position, chunk, WorldToLocal(position, chunkPos), blockType, damage);
	}

	// Variant for callers (explode()) that have already resolved the chunk + local position
	// themselves — skips a redundant chunk dictionary lookup per voxel on top of the one
	// the caller already did for its own chunk-caching.
	private void damage_block_locked(Vector3I position, Chunk chunk, Vector3I localPos, int blockType, float damage)
	{
		float effective = damage / GetHardness(blockType);

		chunk.DamageData ??= new Dictionary<Vector3I, BlockHealth>();

		if (!chunk.DamageData.TryGetValue(localPos, out var block))
		{
			bool hasOverlay = ShowDamageOverlays && effective >= MinDamageForOverlay;

			// Only count visible (overlay) blocks against the FIFO cap so soft fringe
			// hits don't evict blocks that have actual visible damage.
			if (hasOverlay && _damageInsertionOrder.Count >= MAX_DAMAGED_BLOCKS && _damageInsertionOrder.Count > 0)
			{
				Vector3I oldest = _damageInsertionOrder.First.Value;
				RemoveBlockOverlay(oldest);
			}

			LinkedListNode<Vector3I> node = hasOverlay ? _damageInsertionOrder.AddLast(position) : null;
			var newBlock = new BlockHealth { health = 1.0f - effective, blockType = blockType, insertionNode = node, worldPos = position };
			chunk.DamageData[localPos] = newBlock;

			if (hasOverlay)
				GrantOverlay(blockType, position, newBlock);
		}
		else
		{
			if (block.blockType != blockType)
			{
				RemoveBlockDamage(position);
				damage_block_locked(position, chunk, localPos, blockType, damage);
				return;
			}

			block.health -= effective;
			if (block.health <= 0)
			{
				RemoveBlockDamage(position);
				break_block(position);
				return;
			}

			if (ShowDamageOverlays)
			{
				if (block.overlaySlot < 0)
				{
					// Block may have been first registered below MinDamageForOverlay — grant
					// an overlay now that it has accumulated more damage.
					if (block.insertionNode == null && _damageInsertionOrder.Count >= MAX_DAMAGED_BLOCKS && _damageInsertionOrder.Count > 0)
					{
						Vector3I oldest = _damageInsertionOrder.First.Value;
						RemoveBlockOverlay(oldest);
					}
					if (block.insertionNode == null)
						block.insertionNode = _damageInsertionOrder.AddLast(position);
					GrantOverlay(blockType, position, block);
				}
				else
				{
					// Already has a slot — update just that slot's color. O(1) regardless of
					// how many other blocks of this type are currently tracked.
					QueueOverlayWrite(blockType, block.overlaySlot, position, block.health);
				}
			}
		}
	}

	// Allocates (or reuses) a slot in blockType's overlay MultiMesh for this block and writes
	// its initial transform/color. Brand-new slots (beyond anything freed for reuse) are
	// written immediately rather than queued: VisibleInstanceCount is about to grow to cover
	// them, and an un-written instance defaults to an identity transform at the world origin —
	// deferring the write would flash a stray crack quad there for a frame. Reused slots are
	// already inside the visible range and currently hidden (zero-scale, from RevokeOverlay),
	// so it's safe to queue their write for the next flush.
	private void GrantOverlay(int blockType, Vector3I position, BlockHealth block)
	{
		GetOrCreateDamageOverlay(blockType);
		if (!damageMultiMeshByBlock.TryGetValue(blockType, out var mm)) return;

		if (!_freeOverlaySlots.TryGetValue(blockType, out var freeList))
			_freeOverlaySlots[blockType] = freeList = new Stack<int>();

		if (freeList.Count > 0)
		{
			block.overlaySlot = freeList.Pop();
			SetSlotOwner(blockType, block.overlaySlot, block);
			QueueOverlayWrite(blockType, block.overlaySlot, position, block.health);
		}
		else
		{
			_overlayHighWater.TryGetValue(blockType, out int hw);
			EnsureOverlayCapacity(blockType, mm, hw + 1);

			block.overlaySlot = hw;
			_overlayHighWater[blockType] = hw + 1;
			SetSlotOwner(blockType, hw, block);

			mm.SetInstanceTransform(block.overlaySlot, new Transform3D(Basis.Identity, position + new Vector3(0.5f, 0.5f, 0.5f)));
			mm.SetInstanceCustomData(block.overlaySlot, new Color(1f - block.health, 0f, 0f, 1f));
			mm.VisibleInstanceCount = _overlayHighWater[blockType];
		}
	}

	// Frees a block's overlay slot for reuse and queues a "hide" write so it stops rendering.
	private void RevokeOverlay(int blockType, BlockHealth block)
	{
		if (block.overlaySlot < 0) return;
		int slot = block.overlaySlot;
		block.overlaySlot = -1;
		SetSlotOwner(blockType, slot, null);

		if (!_freeOverlaySlots.TryGetValue(blockType, out var freeList))
			_freeOverlaySlots[blockType] = freeList = new Stack<int>();
		freeList.Push(slot);

		QueueOverlayFree(blockType, slot);
	}

	private void SetSlotOwner(int blockType, int slot, BlockHealth owner)
	{
		if (!_overlaySlotOwners.TryGetValue(blockType, out var owners))
			_overlaySlotOwners[blockType] = owners = new List<BlockHealth>();
		while (owners.Count <= slot)
			owners.Add(null);
		owners[slot] = owner;
	}

	// Grows a block type's MultiMesh to at least neededSlots, geometrically (doubling) to
	// amortize the cost. Godot clears ALL instance data when InstanceCount changes, so every
	// slot below the current high-water mark — live or freed — has to be rewritten from
	// scratch afterward: live slots from their owning BlockHealth (worldPos/health), freed
	// slots back to the hidden zero-scale transform. This only runs on the rare occasions a
	// type's overlay count actually grows past its current capacity, not on every grant.
	private void EnsureOverlayCapacity(int blockType, MultiMesh mm, int neededSlots)
	{
		if (mm.InstanceCount >= neededSlots) return;

		int newCapacity = Mathf.Max(mm.InstanceCount, OverlayInitialCapacity);
		while (newCapacity < neededSlots)
			newCapacity *= 2;

		mm.InstanceCount = newCapacity;

		_overlaySlotOwners.TryGetValue(blockType, out var owners);
		_overlayHighWater.TryGetValue(blockType, out int hw);

		for (int slot = 0; slot < hw; slot++)
		{
			var owner = (owners != null && slot < owners.Count) ? owners[slot] : null;
			if (owner != null)
			{
				mm.SetInstanceTransform(slot, new Transform3D(Basis.Identity, owner.worldPos + new Vector3(0.5f, 0.5f, 0.5f)));
				mm.SetInstanceCustomData(slot, new Color(1f - owner.health, 0f, 0f, 1f));
			}
			else
			{
				mm.SetInstanceTransform(slot, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero));
			}
		}

		mm.VisibleInstanceCount = hw;
	}

	private void QueueOverlayWrite(int blockType, int slot, Vector3I pos, float health)
	{
		if (!_pendingOverlayWrites.TryGetValue(blockType, out var q))
			_pendingOverlayWrites[blockType] = q = new Queue<OverlayOp>();
		q.Enqueue(new OverlayOp { Slot = slot, Pos = pos, Health = health });
	}

	private void QueueOverlayFree(int blockType, int slot)
	{
		if (!_pendingOverlayFrees.TryGetValue(blockType, out var q))
			_pendingOverlayFrees[blockType] = q = new Queue<int>();
		q.Enqueue(slot);
	}

	public bool damage_check(Vector3I position, float damage)
	{
		int blockType = get_block(position);
		if (blockType == 0) return false;

		float effective = damage / GetHardness(blockType);

		lock (damageLock)
		{
			if (TryGetBlockHealth(position, out var block))
			{
				if (block.health - effective <= 0)
				{
					RemoveBlockDamage(position);
					break_block(position);
					return true;
				}
			}
			else if (effective >= 1.0f)
			{
				break_block(position);
				return true;
			}
		}

		damage_block(position, damage);
		return false;
	}

	private void RemoveBlockDamage(Vector3I position)
	{
		lock (damageLock)
		{
			var chunkPos = world_to_chunk(position);
			if (!chunks.TryGetValue(chunkPos, out var chunk) || chunk.DamageData == null) return;

			var localPos = WorldToLocal(position, chunkPos);
			if (!chunk.DamageData.TryGetValue(localPos, out var block)) return;

			int bt = block.blockType;
			chunk.DamageData.Remove(localPos);
			if (chunk.DamageData.Count == 0)
				chunk.DamageData = null; // free the sparse dict once this chunk has no damage left

			if (block.insertionNode != null)
				_damageInsertionOrder.Remove(block.insertionNode);

			RevokeOverlay(bt, block);
		}
	}

	// Evicts only the crack-overlay slot for a block, leaving its tracked health intact.
	// Used by the FIFO overlay cap: a single large explosion can create far more newly
	// damaged blocks than MAX_DAMAGED_BLOCKS in one call, so evicting via RemoveBlockDamage
	// (which deletes the whole entry) would heal blocks the same explosion just damaged
	// before the call even returns, making big explosions look like they did nothing.
	// This way an evicted block just stops rendering a crack — it keeps accumulating real
	// damage and still breaks normally once enough damage lands.
	private void RemoveBlockOverlay(Vector3I position)
	{
		lock (damageLock)
		{
			var chunkPos = world_to_chunk(position);
			if (!chunks.TryGetValue(chunkPos, out var chunk) || chunk.DamageData == null) return;

			var localPos = WorldToLocal(position, chunkPos);
			if (!chunk.DamageData.TryGetValue(localPos, out var block)) return;

			int bt = block.blockType;

			if (block.insertionNode != null)
			{
				_damageInsertionOrder.Remove(block.insertionNode);
				block.insertionNode = null;
			}

			RevokeOverlay(bt, block);
		}
	}

	// Drains queued per-slot overlay writes, budgeted per frame. Each op touches exactly one
	// MultiMesh instance slot, so cost here is proportional to how many blocks actually
	// changed since the last flush — not to how many are currently tracked in total. A single
	// mass-destruction explode() call can still queue a very large number of ops at once
	// (e.g. hundreds of thousands of newly-overlaid blocks), so this still drains under a
	// time budget, at op granularity (not just per-type), so one huge type's queue can't
	// blow the whole frame budget by itself. At least one op always completes per call so
	// the queue can't stall forever.
	private void FlushDirtyDamageOverlays()
	{
		if (_pendingOverlayFrees.Count == 0 && _pendingOverlayWrites.Count == 0) return;

		lock (damageLock)
		{
			ulong budgetUsec = (ulong)(MaxDamageOverlayMillisPerFrame * 1000.0);
			ulong startUsec = Time.GetTicksUsec();
			int processedOps = 0;
			var emptyTypes = new List<int>();

			// Phase 1: frees. Always drained first and to completion before any write is
			// touched — a block that's already gone showing a crack for several extra
			// frames reads as "the explosion missed it," which is far more noticeable than
			// a still-standing block's tint refreshing a few frames late.
			foreach (var kv in _pendingOverlayFrees)
			{
				int bt = kv.Key;
				var q = kv.Value;

				if (!damageMultiMeshByBlock.TryGetValue(bt, out var mm))
				{
					q.Clear();
					emptyTypes.Add(bt);
					continue;
				}

				while (q.Count > 0)
				{
					if (processedOps > 0 && Time.GetTicksUsec() - startUsec >= budgetUsec)
						break;

					int slot = q.Dequeue();
					mm.SetInstanceTransform(slot, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero));
					processedOps++;
				}

				if (q.Count == 0)
					emptyTypes.Add(bt);

				if (processedOps > 0 && Time.GetTicksUsec() - startUsec >= budgetUsec)
					break;
			}

			foreach (var bt in emptyTypes)
				_pendingOverlayFrees.Remove(bt);

			if (_pendingOverlayFrees.Count > 0)
				return; // frees still outstanding somewhere; writes wait for the next call

			emptyTypes.Clear();

			// Phase 2: cosmetic tint/transform writes, only once every free is caught up.
			foreach (var kv in _pendingOverlayWrites)
			{
				int bt = kv.Key;
				var q = kv.Value;

				if (!damageMultiMeshByBlock.TryGetValue(bt, out var mm))
				{
					q.Clear();
					emptyTypes.Add(bt);
					continue;
				}

				while (q.Count > 0)
				{
					if (processedOps > 0 && Time.GetTicksUsec() - startUsec >= budgetUsec)
						break;

					var op = q.Dequeue();
					mm.SetInstanceTransform(op.Slot, new Transform3D(Basis.Identity, op.Pos + new Vector3(0.5f, 0.5f, 0.5f)));
					mm.SetInstanceCustomData(op.Slot, new Color(1f - op.Health, 0f, 0f, 1f));
					processedOps++;
				}

				if (q.Count == 0)
					emptyTypes.Add(bt);

				if (processedOps > 0 && Time.GetTicksUsec() - startUsec >= budgetUsec)
					break;
			}

			foreach (var bt in emptyTypes)
				_pendingOverlayWrites.Remove(bt);
		}
	}

	private void ClearDamageInChunk(Vector3I chunkPos)
	{
		lock (damageLock)
		{
			if (!chunks.TryGetValue(chunkPos, out var chunk) || chunk.DamageData == null)
				return;

			// Snapshot first since RemoveBlockDamage mutates chunk.DamageData mid-loop
			// (including potentially nulling it out once empty).
			foreach (var localPos in chunk.DamageData.Keys.ToList())
			{
				var worldPos = new Vector3I(
					chunkPos.X * CHUNK_SIZE + localPos.X,
					chunkPos.Y * CHUNK_SIZE + localPos.Y,
					chunkPos.Z * CHUNK_SIZE + localPos.Z);
				RemoveBlockDamage(worldPos);
			}
		}
	}

	public void break_block(Vector3I position)
	{
		int brokenType = get_block(position);
		RemoveBlockDamage(position);
		set_block(position, 0);

		// Item drops disabled for now (commented out)
		/*
		int dropCount = Block_Registry.GetBlockDropCount(brokenType);
		string dropId = Block_Registry.GetBlockDropID(brokenType);
		for (int i = 0; i < dropCount; i++)
		{
			Item_Registry.SpawnItem(dropId, position + new Vector3(0.5f, 0.5f, 0.5f), GetTree().Root);
		}
		*/
	}

	public void place_block(Vector3I position, int blockId)
	{
		RemoveBlockDamage(position);
		set_block(position, blockId);
	}

	public Vector3I world_to_chunk(Vector3I worldPos)
	{
		return new Vector3I(
			Mathf.FloorToInt((float)worldPos.X / CHUNK_SIZE),
			Mathf.FloorToInt((float)worldPos.Y / CHUNK_SIZE),
			Mathf.FloorToInt((float)worldPos.Z / CHUNK_SIZE)
		);
	}

	public Vector3I chunk_to_world(Vector3I chunkPos)
	{
		return new Vector3I(
			chunkPos.X * CHUNK_SIZE,
			chunkPos.Y * CHUNK_SIZE,
			chunkPos.Z * CHUNK_SIZE
		);
	}

	public int world_to_index(Vector3I worldPos)
	{
		int x = worldPos.X % CHUNK_SIZE;
		int y = worldPos.Y % CHUNK_SIZE;
		int z = worldPos.Z % CHUNK_SIZE;
		return voxel_index(x, y, z);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int voxel_index(int x, int y, int z)
	{
		return x + (z * CHUNK_SIZE) + (y * CHUNK_SIZE * CHUNK_SIZE);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int voxel_index(Vector3I index)
	{
		return index.X + (index.Z * CHUNK_SIZE) + (index.Y * CHUNK_SIZE * CHUNK_SIZE);
	}

	private bool adjacent_chunks_solid(Vector3I chunkPos)
	{
		for (int i = 0; i < 6; i++)
		{
			Vector3I neighborPos = chunkPos + FaceOffsets[i];
			if (!chunks.TryGetValue(neighborPos, out var neighbor) || !neighbor.IsFullySolid)
				return false;
		}
		return true;
	}
}
