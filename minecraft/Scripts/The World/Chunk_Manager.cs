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
	private const int CHUNK_SIZE = 16;

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
	[Export] public RenderMode RenderModeType = RenderMode.Sphere;
	[Export] public Texture2D DamageTexture;

	[Export] public float DamageOverlayNormalOffset = 0.0025f;
	[Export] public float DamageOverlayScale = 1.0f;
	[Export] public bool DebugDamageUseSolidMaterial = false;
	[Export] public bool DebugDamageNoDepthTest = false;

	private FastNoiseLite noise;

	// Block damage system
	private Dictionary<Vector3I, BlockHealth> damagedBlocks = new();
	private Queue<Vector3I> damageQueue = new();
	private Dictionary<Vector3I, bool> damageQueueLookup = new();
	private Dictionary<int, MultiMeshInstance3D> damageOverlaysByBlock = new();
	private Dictionary<int, MultiMesh> damageMultiMeshByBlock = new();
	private Dictionary<int, List<Vector3I>> damagePositionsByBlock = new();

	private const int MAX_DAMAGED_BLOCKS = 500;
	private Material damageOverlayMaterial;

	private class BlockHealth
	{
		public float health = 1.0f;
		public int multiMeshIndex = -1;
		public int blockType = 0;
	}

	private Thread generationThread;
	private Thread loadingThread;
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
	private List<Vector3I> cachedChunkOffsets = new List<Vector3I>();
	private HashSet<Vector3I> cachedActiveSet = new HashSet<Vector3I>();
	private List<Vector3I> chunksToUnload = new List<Vector3I>();
	private List<Vector3I> dirtyChunksList = new List<Vector3I>();

	private float[] meshVerticesFlat = new float[4096 * 3];
	private float[] meshNormalsFlat = new float[4096 * 3];
	private float[] meshUvsFlat = new float[4096 * 2];
	private int[] meshIndicesArray = new int[6144];

	private int vertexCount = 0;
	private int uvCount = 0;
	private int indexCount = 0;

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
		public bool VerticesFromPool;
		public bool NormalsFromPool;
		public bool UVsFromPool;
		public bool IndicesFromPool;
	}

	// pending buffers passed from worker threads to main thread, keyed by chunk position
	private ConcurrentDictionary<Vector3I, MeshBuffers> pendingBuffers = new ConcurrentDictionary<Vector3I, MeshBuffers>();

	public override void _Ready()
	{
		Global = GetNode<Global>("/root/Global");
		Global.CubeManager = this;

		surfaceLevel = Global.SurfaceLevel;

		noise = new FastNoiseLite();
		noise.Seed = noiseSeed;

		InitializeDamageSystem();
		RecalculateChunkOffsets();

		generationThread = new Thread(GenerationWorkerLoop);
		generationThread.Name = "ChunkGeneration";
		generationThread.Start();

		loadingThread = new Thread(LoadingWorkerLoop);
		loadingThread.Name = "MeshGeneration";
		loadingThread.Start();
	}

	public override void _ExitTree()
	{
		threadsRunning = false;
		lock (generationLock) Monitor.Pulse(generationLock);
		lock (loadingLock) Monitor.Pulse(loadingLock);
		generationThread?.Join(1000);
		loadingThread?.Join(1000);
	}

	public override void _Process(double delta)
	{
		timeElapsed += (float)delta;
		if (timeElapsed >= TIME_HANDLE)
		{
			timeElapsed -= TIME_HANDLE;

			handle_chunks_art();
			handle_dirties();
		}
	}

	public void handle_chunks_art()
	{
		Vector3 pPos = Global.GetPlayerPos();
		Vector3I playerPos = ((Vector3I)pPos) / CHUNK_SIZE;

		if (playerPos != lastPlayerChunkPos)
		{
			lastPlayerChunkPos = playerPos;
			cachedActiveSet.Clear();
			foreach (var offset in cachedChunkOffsets)
			{
				cachedActiveSet.Add(playerPos + offset);
			}
			// Remove stale queue entries outside the new active set to avoid backlog
			foreach (var kp in generationQueue.Keys.ToList())
			{
				if (!cachedActiveSet.Contains(kp))
					generationQueue.TryRemove(kp, out _);
			}
			foreach (var kp in loadingQueue.Keys.ToList())
			{
				if (!cachedActiveSet.Contains(kp))
					loadingQueue.TryRemove(kp, out _);
			}
			// Clear and reprioritize queues with new closest-first order
			lock (generationLock)
			{
				generationWorkQueue.Clear();
				foreach (var offset in cachedChunkOffsets)
				{
					var chunkPos = playerPos + offset;
					if (generationQueue.ContainsKey(chunkPos))
						generationWorkQueue.Enqueue(chunkPos);
				}
				Monitor.Pulse(generationLock);
			}

			lock (loadingLock)
			{
				loadingWorkQueue.Clear();
				foreach (var offset in cachedChunkOffsets)
				{
					var chunkPos = playerPos + offset;
					if (loadingQueue.ContainsKey(chunkPos))
						loadingWorkQueue.Enqueue(chunkPos);
				}
				Monitor.Pulse(loadingLock);
			}
		}

		foreach (var offset in cachedChunkOffsets)
		{
			var chunkPos = playerPos + offset;
			bool shouldBeVisible = offset.Length() <= RenderDistance;

			if (chunks.TryGetValue(chunkPos, out var chunk))
			{
				// If the chunk exists but hasn't been generated yet, ensure it's queued
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
						Vector3I neighborPos = chunkPos + FaceOffsets[i];
						if (!chunks.ContainsKey(neighborPos))
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
							Vector3I neighborPos = chunkPos + FaceOffsets[i];
							if (!chunks[neighborPos].Generated)
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

		chunksToUnload.Clear();
		foreach (var chunkPos in chunks.Keys)
		{
			if (!cachedActiveSet.Contains(chunkPos))
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
		
	}

	public void unload(Vector3I position)
	{
		if (!chunks.TryGetValue(position, out var chunk))
			return;

		// Clear damage in this chunk and free any pending buffers
		ClearDamageInChunk(position);
		pendingBuffers.TryRemove(position, out _);

		if (chunk.MeshInstance != null && GodotObject.IsInstanceValid(chunk.MeshInstance))
		{
			if (chunk.MeshInstance.GetParent() != null)
				RemoveChild(chunk.MeshInstance);
			chunk.MeshInstance.QueueFree();
		}

		// Keep the chunk entry in memory but release voxel buffers and mark
		// it as not generated so it will be regenerated when needed.
		// Keep voxel buffers in memory so edits persist in `chunks`.
		chunk.MeshInstance = null;
		chunk.Loaded = false;
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

		// Generate voxels using one RNG per-chunk (move Random creation out of inner loops)
		chunk.Voxels = create_chunk_data(position);

		bool isFullySolid = true;
		for (int i = 0; i < chunk.Voxels.Length; i++)
		{
			if (chunk.Voxels[i] == 0)
			{
				isFullySolid = false;
				break;
			}
		}
		chunk.IsFullySolid = isFullySolid;

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

		if (chunk.IsFullySolid && adjacent_chunks_solid(position))
			{
				// No geometry to upload; defer a load_ready_chunk with zero counts
				CallDeferred("load_ready_chunk", position, 0, 0, 0);
				return;
			}

		vertexCount = 0;
		uvCount = 0;
		indexCount = 0;

		if (meshVerticesFlat.Length < 4096 * 3) meshVerticesFlat = new float[4096 * 3];
		if (meshNormalsFlat.Length < 4096 * 3) meshNormalsFlat = new float[4096 * 3];
		if (meshUvsFlat.Length < 4096 * 2) meshUvsFlat = new float[4096 * 2];
		if (meshIndicesArray.Length < 6144) meshIndicesArray = new int[6144];

		// Snapshot voxel data to avoid races with main-thread mutations
		byte[] voxels = null;
		if (chunk.Voxels != null)
		{
			voxels = new byte[CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE];
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
								int worldX = chunkX + x;
								int worldY = chunkY + y;
								int worldZ = chunkZ + z;
								isAir = get_block(new Vector3I(worldX + offset.X, worldY + offset.Y, worldZ + offset.Z)) == 0;
							}

							if (!isAir) continue;
						}

						int neededVertices = vertexCount + 12;
						int neededUvs = uvCount + 8;
						int neededIndices = indexCount + 6;

						if (neededVertices > meshVerticesFlat.Length)
						{
							Array.Resize(ref meshVerticesFlat, meshVerticesFlat.Length * 2);
							Array.Resize(ref meshNormalsFlat, meshNormalsFlat.Length * 2);
						}
						if (neededUvs > meshUvsFlat.Length)
							Array.Resize(ref meshUvsFlat, meshUvsFlat.Length * 2);
						if (neededIndices > meshIndicesArray.Length)
							Array.Resize(ref meshIndicesArray, meshIndicesArray.Length * 2);

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
		var buffers = new MeshBuffers();

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

		// Store buffers for main thread and defer chunk load processing by position + counts
		pendingBuffers[position] = buffers;
		CallDeferred("load_ready_chunk", position, vCount, uCount, indexCount);
	}

	public void load_ready_chunk(Vector3I position, int vertCount, int uvCount, int idxCount)
	{
		if (!chunks.TryGetValue(position, out var chunk))
		{
			// ensure buffers are freed if present
			if (pendingBuffers.TryRemove(position, out var _)) { }
			return;
		}

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
			return;
		}

		if (!pendingBuffers.TryRemove(position, out var buffers))
		{
			// no buffers available; nothing to do
			return;
		}

		Mesh newMesh = create_mesh_from_data(buffers.Vertices, buffers.Normals, buffers.UVs, buffers.Indices, vertCount, uvCount, idxCount);

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
	}

	public byte[] create_chunk_data(Vector3I chunkPos)
	{
		byte[] data = new byte[CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE];

		// Create one deterministic RNG per chunk for efficiency
		int seed = noiseSeed ^ (chunkPos.X * 73856093) ^ (chunkPos.Y * 19349663) ^ (chunkPos.Z * 83492791);
		var rng = new Random(seed);

		for (int x = 0; x < CHUNK_SIZE; x++)
		{
			for (int y = 0; y < CHUNK_SIZE; y++)
			{
				for (int z = 0; z < CHUNK_SIZE; z++)
				{
					int worldX = chunkPos.X * CHUNK_SIZE + x;
					int worldY = chunkPos.Y * CHUNK_SIZE + y;
					int worldZ = chunkPos.Z * CHUNK_SIZE + z;

					float height = noise.GetNoise2D(worldX, worldZ) * 10 + surfaceLevel;

					int index = voxel_index(x, y, z);
					float abyssStrength = Global.AbyssStrength(worldX, worldZ, worldY);

					if (worldY <= height && abyssStrength < 0.5f)
					{
						// Use chunk RNG with a lightweight additional scramble based on voxel coords
						int localSeed = seed + x * 7385609 + y * 1934969 + z * 834927;
						rng = new Random(localSeed);
						byte blockType = (byte)rng.Next(1, 4);
						data[index] = blockType;
					}
					else
					{
						data[index] = 0;
					}
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
			chunk.IsFullySolid = false;
			chunk.Dirty = true;
			dirtySet.Add(chunkPos);

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

		for (int x = -r; x <= r; x++)
		for (int y = -r; y <= r; y++)
		for (int z = -r; z <= r; z++)
		{
			if (x*x + y*y + z*z > r2) continue;
			var worldPos = center + new Vector3I(x, y, z);
			int current = get_block(worldPos);
			if (current == 0) continue;

			float dist = Mathf.Sqrt(x*x + y*y + z*z);
			float falloff = 1f - (dist / radius);
			if (falloff <= 0f) continue;

			if (falloff >= 1f)
			{
				batch.Add((worldPos, 0));
			}
			else
			{
				// partial damage: reduce health via damage_block which handles overlays
				damage_block(worldPos, damage * falloff);
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
		multiMesh.InstanceCount = MAX_DAMAGED_BLOCKS;  // pre-allocate fixed size
		multiMesh.VisibleInstanceCount = 0;            // nothing visible yet

		MultiMeshInstance3D instance = new MultiMeshInstance3D();
		float worldRange = (RenderDistance + 4) * CHUNK_SIZE;
		instance.CustomAabb = new Aabb(
			new Vector3(-worldRange, -worldRange, -worldRange),
			new Vector3(worldRange * 2f, worldRange * 2f, worldRange * 2f)
		);
		instance.ExtraCullMargin = CHUNK_SIZE * 2f;
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

		if (!damagePositionsByBlock.ContainsKey(blockType))
			damagePositionsByBlock[blockType] = new List<Vector3I>();

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

	public void damage_block(Vector3I position, float damage)
	{
		int blockType = get_block(position);
		if (blockType == 0) return;

		var overlayInstance = GetOrCreateDamageOverlay(blockType);
		if (overlayInstance == null) return;

		var multiMesh = overlayInstance.Multimesh;
		List<Vector3I> posList;
		lock (damageLock)
		{
			if (!damagePositionsByBlock.TryGetValue(blockType, out posList))
			{
				posList = new List<Vector3I>();
				damagePositionsByBlock[blockType] = posList;
			}

			if (!damagedBlocks.ContainsKey(position))
			{
				BlockHealth newBlock = new BlockHealth
				{
					health = 1.0f - damage,
					blockType = blockType
				};

				int newIndex = posList.Count;
				if (newIndex >= multiMesh.InstanceCount)
				{
					return;
				}

				newBlock.multiMeshIndex = newIndex;

				damagedBlocks.Add(position, newBlock);
				damageQueue.Enqueue(position);
				damageQueueLookup[position] = true;
				posList.Add(position);

				Transform3D transform = new Transform3D(Basis.Identity, position + new Vector3(0.5f, 0.5f, 0.5f));
				multiMesh.SetInstanceTransform(newIndex, transform);
				multiMesh.SetInstanceCustomData(newIndex, new Color(1.0f - newBlock.health, 0, 0, 1));

				multiMesh.VisibleInstanceCount = posList.Count;
			}
			else
			{
				BlockHealth block = damagedBlocks[position];

				if (block.blockType != blockType)
				{
					RemoveBlockDamage(position);
					damage_block(position, damage);
					return;
				}

				block.health -= damage;

				if (block.health <= 0)
				{
					RemoveBlockDamage(position);
					break_block(position);
					return;
				}

				multiMesh.SetInstanceCustomData(block.multiMeshIndex, new Color(1.0f - block.health, 0, 0, 1));
				multiMesh.VisibleInstanceCount = posList.Count;
			}
		}

		while (damagedBlocks.Count > MAX_DAMAGED_BLOCKS)
		{
			Vector3I oldest = damageQueue.Dequeue();
			if (!damageQueueLookup.ContainsKey(oldest)) continue;

			damageQueueLookup.Remove(oldest);
			RemoveBlockDamage(oldest);
		}
	}

	private void RemoveBlockDamage(Vector3I position)
	{
		lock (damageLock)
		{
			if (!damagedBlocks.ContainsKey(position)) return;

			BlockHealth block = damagedBlocks[position];
			if (!damageMultiMeshByBlock.TryGetValue(block.blockType, out var multiMesh))
			{
				damagedBlocks.Remove(position);
				return;
			}

			if (!damagePositionsByBlock.TryGetValue(block.blockType, out var posList))
			{
				damagedBlocks.Remove(position);
				damageQueueLookup.Remove(position);
				return;
			}

			int indexToRemove = block.multiMeshIndex;
			if (indexToRemove < 0 || indexToRemove >= posList.Count)
			{
				damagedBlocks.Remove(position);
				damageQueueLookup.Remove(position);
				return;
			}

			int lastIndex = posList.Count - 1;

			if (indexToRemove != lastIndex)
			{
				Vector3I swapPos = posList[lastIndex];
				if (damagedBlocks.TryGetValue(swapPos, out var swapBlock) && swapBlock.blockType == block.blockType)
				{
					Transform3D lastT = multiMesh.GetInstanceTransform(lastIndex);
					Color lastC = multiMesh.GetInstanceCustomData(lastIndex);

					multiMesh.SetInstanceTransform(indexToRemove, lastT);
					multiMesh.SetInstanceCustomData(indexToRemove, lastC);

					posList[indexToRemove] = swapPos;
					swapBlock.multiMeshIndex = indexToRemove;
				}
			}

			posList.RemoveAt(lastIndex);
			multiMesh.VisibleInstanceCount = posList.Count;

			damagedBlocks.Remove(position);
			damageQueueLookup.Remove(position);
		}
	}

	private void ClearDamageInChunk(Vector3I chunkPos)
	{
		List<Vector3I> toRemove = new List<Vector3I>();
		lock (damageLock)
		{
			foreach (var blockPos in damagedBlocks.Keys.ToList())
			{
				if (world_to_chunk(blockPos) == chunkPos)
					toRemove.Add(blockPos);
			}

			if (toRemove.Count == 0) return;

			// Remove all damages and mark for queue rebuild
			var removedSet = new HashSet<Vector3I>(toRemove);
			foreach (var blockPos in toRemove)
			{
				RemoveBlockDamage(blockPos);
			}

			// Rebuild damageQueue excluding removed positions
			var tempQueue = new Queue<Vector3I>();
			while (damageQueue.Count > 0)
			{
				var pos = damageQueue.Dequeue();
				if (!removedSet.Contains(pos))
					tempQueue.Enqueue(pos);
			}
			damageQueue = tempQueue;
			foreach (var pos in removedSet)
				damageQueueLookup.Remove(pos);
		}
	}

	public void break_block(Vector3I position)
	{
		int brokenType = get_block(position);
		RemoveBlockDamage(position);
		set_block(position, 0);

		int dropCount = Block_Registry.GetBlockDropCount(brokenType);
		string dropId = Block_Registry.GetBlockDropID(brokenType);
		for (int i = 0; i < dropCount; i++)
		{
			Item_Registry.SpawnItem(dropId, position + new Vector3(0.5f, 0.5f, 0.5f), GetTree().Root);
		}
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
