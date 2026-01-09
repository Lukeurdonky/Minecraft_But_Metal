using Godot;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public partial class Chunk_Manager : Node
{
	private const int CHUNK_SIZE = 16;
	
	// Static face data - allocated once, reused forever
	private static readonly Vector3I[] FaceOffsets = new Vector3I[]
	{
		new Vector3I(0, 0, -1),  // Front (Z-)
		new Vector3I(0, 0, 1),   // Back (Z+)
		new Vector3I(-1, 0, 0),  // Left (X-)
		new Vector3I(1, 0, 0),   // Right (X+)
		new Vector3I(0, 1, 0),   // Top (Y+)
		new Vector3I(0, -1, 0)   // Bottom (Y-)
	};
	
	private Global Global;
	private Dictionary<Vector3I, Chunk> chunks = new();
	private HashSet<Vector3I> activeChunks = new();
	private HashSet<Vector3I> dirtyChunks = new();
	private HashSet<Vector3I> generationQueue = new();
	private HashSet<Vector3I> loadingQueue = new();
	
	[Export] public Material Mat;
	[Export] public int RenderDistance = 5;
	
	private FastNoiseLite noise;
	private Thread generationThread;
	private Thread loadingThread;
	private volatile bool threadsRunning = true;
	
	// Thread-safe work queues
	private readonly Queue<Vector3I> generationWorkQueue = new Queue<Vector3I>();
	private readonly Queue<Vector3I> loadingWorkQueue = new Queue<Vector3I>();
	private readonly object generationLock = new object();
	private readonly object loadingLock = new object();
	
	// Cached data for worker threads (no Godot API access)
	private int surfaceLevel;
	private int noiseSeed = 42;
	
	private float timeElapsed = 0f;
	private const float TIME_HANDLE = 0.015f;
	
	// Cached chunk offsets to avoid rebuilding every frame
	private Vector3I lastPlayerChunkPos = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
	private List<Vector3I> cachedChunkOffsets = new List<Vector3I>();
	private HashSet<Vector3I> cachedActiveSet = new HashSet<Vector3I>();
	private List<Vector3I> chunksToUnload = new List<Vector3I>();
	private List<Vector3I> dirtyChunksList = new List<Vector3I>();
	
	// Reusable mesh builder buffers - NO GC PRESSURE
	private float[] meshVerticesFlat = new float[4096 * 3]; // Store as flat floats
	private float[] meshNormalsFlat = new float[4096 * 3];
	private float[] meshUvsFlat = new float[4096 * 2];
	private int[] meshIndicesArray = new int[6144];
	
	private int vertexCount = 0;
	private int uvCount = 0;
	private int indexCount = 0;
	
	// GC monitoring
	private int lastGC0 = 0;
	private int lastGC1 = 0;
	private int lastGC2 = 0;
	private int frameCount = 0;
	private const int DEBUG_REPORT_INTERVAL = 60;
	
	// Array pools to reduce GC pressure
	private static readonly ArrayPool<Vector3> Vector3Pool = ArrayPool<Vector3>.Shared;
	private static readonly ArrayPool<Vector2> Vector2Pool = ArrayPool<Vector2>.Shared;
	private static readonly ArrayPool<int> IntPool = ArrayPool<int>.Shared;
	private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;

	public override void _Ready()
	{
		Global = GetNode<Global>("/root/Global");
		
		// Register this as the CubeManager in Global
		Global.CubeManager = this;
		
		// Cache values for threads (avoid Godot API in threads)
		surfaceLevel = Global.SurfaceLevel;
		
		noise = new FastNoiseLite();
		noise.Seed = noiseSeed;

		RecalculateChunkOffsets();
		
		// Start long-lived worker threads
		generationThread = new Thread(GenerationWorkerLoop);
		generationThread.Name = "ChunkGeneration";
		generationThread.Start();
		
		loadingThread = new Thread(LoadingWorkerLoop);
		loadingThread.Name = "MeshGeneration";
		loadingThread.Start();
	}
	
	public override void _ExitTree()
	{
		// Signal threads to stop
		threadsRunning = false;
		
		// Wake threads if sleeping
		lock (generationLock) Monitor.Pulse(generationLock);
		lock (loadingLock) Monitor.Pulse(loadingLock);
		
		// Wait for graceful shutdown
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
			
			// GC monitoring
			// int gc0 = GC.CollectionCount(0);
			// int gc1 = GC.CollectionCount(1);
			// int gc2 = GC.CollectionCount(2);
			
			// if (gc0 != lastGC0 || gc1 != lastGC1 || gc2 != lastGC2)
			// {
			// 	GD.Print($"[GC] Gen0: +{gc0 - lastGC0}, Gen1: +{gc1 - lastGC1}, Gen2: +{gc2 - lastGC2}");
			// 	lastGC0 = gc0;
			// 	lastGC1 = gc1;
			// 	lastGC2 = gc2;
			// }
			
			// frameCount++;
			// if (frameCount >= DEBUG_REPORT_INTERVAL)
			// {
			// 	GD.Print($"[GC REPORT] Total collections - Gen0: {gc0}, Gen1: {gc1}, Gen2: {gc2}");
			// 	frameCount = 0;
			// }
		}
	}

	public void handle_chunks_art()
	{
		Vector3 pPos = Global.GetPlayerPos();
		Vector3I playerPos = ((Vector3I)pPos) / CHUNK_SIZE;
		
		// Only recalculate chunk offsets and activeSet if player moved to a new chunk
		if (playerPos != lastPlayerChunkPos)
		{
			lastPlayerChunkPos = playerPos;
			
			
			// Rebuild activeSet only when player moves
			cachedActiveSet.Clear();
			foreach (var offset in cachedChunkOffsets)
			{
				cachedActiveSet.Add(playerPos + offset);
			}
		}
		
		// Limit work per frame to prevent main thread stalls
		// int processedThisFrame = 0;
		// const int MAX_CHUNKS_PER_FRAME = 100;
		
		// Process cached offsets with frame limit
		foreach (var offset in cachedChunkOffsets)
		{
			// if (processedThisFrame >= MAX_CHUNKS_PER_FRAME)
			// 	break;

			var chunkPos = playerPos + offset;
			
			// Determine if the chunk should be visible or just exist as data
			bool shouldBeVisible = offset.Length() <= RenderDistance;

			if (chunks.TryGetValue(chunkPos, out var chunk))
			{
				// Chunk exists and is generated but not loaded.
				// Only load it if it's within the visible render distance.
				if (shouldBeVisible && chunk.Generated && !chunk.Loaded)
				{
					// Check if all 6 neighbors are generated before loading.
					bool allNeighborsExist = true;
					for (int i = 0; i < 6; i++)
					{
						Vector3I neighborPos = chunkPos + FaceOffsets[i];
						if(!chunks.ContainsKey(neighborPos))
						{
							allNeighborsExist = false;
							break;
						}

					}
					if (allNeighborsExist)
					{
						// Still check if they're actually generated
						bool allGenerated = true;
						for (int i = 0; i < 6; i++)
						{
							Vector3I neighborPos = chunkPos + FaceOffsets[i];
							if(!chunks[neighborPos].Generated)
							{
								allGenerated = false;
								break;
							}
						}
						
						if (allGenerated && !loadingQueue.Contains(chunkPos))
						{
							loadingQueue.Add(chunkPos);
							// processedThisFrame++;
							// Enqueue to worker thread
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
				// Chunk doesn't exist - needs generation.
				// This happens for all chunks up to the generationDistance.
				if (!generationQueue.Contains(chunkPos))
				{
					generationQueue.Add(chunkPos);
					// processedThisFrame++;
					chunks[chunkPos] = new Chunk(chunkPos);
					
					// Enqueue to worker thread
					lock (generationLock)
					{
						generationWorkQueue.Enqueue(chunkPos);
						Monitor.Pulse(generationLock);
					}
				}
			}
		}
		
		// Unload chunks out of range - use cached list to avoid allocation
		chunksToUnload.Clear();
		foreach (var chunkPos in chunks.Keys)
		{
			if (!cachedActiveSet.Contains(chunkPos))
				chunksToUnload.Add(chunkPos);
		}
		
		foreach (var chunkPos in chunksToUnload)
		{
			unload(chunkPos);
			activeChunks.Remove(chunkPos);
			generationQueue.Remove(chunkPos);
			loadingQueue.Remove(chunkPos);
			chunks.Remove(chunkPos);
		}
	}

	public void handle_dirties() //stinky
	{
		if (dirtyChunks.Count == 0)
			return;
		
		// Copy dirty chunks to cached list (outside lock to avoid blocking)
		dirtyChunksList.Clear();
		foreach (var chunkPos in dirtyChunks)
		{
			if (chunks.TryGetValue(chunkPos, out var chunk))
			{
				chunk.Dirty = false;
				loadingQueue.Add(chunkPos);
				dirtyChunksList.Add(chunkPos);
			}
		}
		dirtyChunks.Clear();
		
		// Now enqueue to worker thread
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
		
		// Build list of all chunk positions within the larger generation distance
		for (int x = -generationDistance; x <= generationDistance; x++)
		{
			for (int y = -generationDistance; y <= generationDistance; y++)
			{
				for (int z = -generationDistance; z <= generationDistance; z++)
				{
					var offset = new Vector3I(x, y, z);
					
					// Skip chunks outside spherical generation distance
					if (offset.Length() > generationDistance)
						continue;
					
					cachedChunkOffsets.Add(offset);
				}
			}
		}
		
		// Sort once by distance from center (closest first)
		cachedChunkOffsets.Sort((a, b) => a.LengthSquared().CompareTo(b.LengthSquared()));
	}

	public void unload(Vector3I position)
	{
		if (chunks.TryGetValue(position, out var chunk) == false)
		return;
		
		// Free the visual objects (check if not already disposed)
		if (chunk.MeshInstance != null && GodotObject.IsInstanceValid(chunk.MeshInstance))
		{
			if (chunk.MeshInstance.GetParent() != null)
				RemoveChild(chunk.MeshInstance);
			chunk.MeshInstance.QueueFree();
		}
		
		// Clear references
		chunk.MeshInstance = null;
		
		// Mark as unloaded
		chunk.Loaded = false;
	}

	// ---------------------------- WORKER THREADS ----------------------------
	private void GenerationWorkerLoop()
	{
		while (threadsRunning)
		{
			Vector3I position;
			
			lock (generationLock)
			{
				// Wait for work
				while (generationWorkQueue.Count == 0 && threadsRunning)
				{
					Monitor.Wait(generationLock);
				}
				
				if (!threadsRunning)
					break;
				
				position = generationWorkQueue.Dequeue();
			}
			
			// Do work outside lock
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
				// Wait for work
				while (loadingWorkQueue.Count == 0 && threadsRunning)
				{
					Monitor.Wait(loadingLock);
				}
				
				if (!threadsRunning)
					break;
				
				position = loadingWorkQueue.Dequeue();
			}
			
			// Do work outside lock
			load_calculate(position);
		}
	}

	// ---------------------------- THREAD SLOP ----------------------------
	// Generation Thread
	public void generate_data(Vector3I position)
	{
		// Check if chunk still exists (may have been unloaded)
		if (!chunks.TryGetValue(position, out var chunk))
			return;

		chunk.Voxels = create_chunk_data(position);
		
		CallDeferred("generate_ready_chunk", position);
	}

	public void generate_ready_chunk(Vector3I position)
	{
		// Check if chunk still exists (may have been unloaded)
		if (!chunks.TryGetValue(position, out var chunk))
			return;
		
		chunk.Generated = true;
		generationQueue.Remove(position); // Clean up the queue
	}

	// Loading Thread
	public void load_calculate(Vector3I position)
	{
		// Check if chunk still exists (may have been unloaded)
		if (!chunks.ContainsKey(position))
			return;
		
		// Reset counters
		vertexCount = 0;
		uvCount = 0;
		indexCount = 0;
		
		// Resize arrays if needed
		if (meshVerticesFlat.Length < 4096 * 3) meshVerticesFlat = new float[4096 * 3];
		if (meshNormalsFlat.Length < 4096 * 3) meshNormalsFlat = new float[4096 * 3];
		if (meshUvsFlat.Length < 4096 * 2) meshUvsFlat = new float[4096 * 2];
		if (meshIndicesArray.Length < 6144) meshIndicesArray = new int[6144];

		byte[] voxels = chunks[position].Voxels;
		int chunkX = position.X * CHUNK_SIZE;
		int chunkY = position.Y * CHUNK_SIZE;
		int chunkZ = position.Z * CHUNK_SIZE;
		
		// CRITICAL: All loops access only LOCAL memory
		for (int y = 0; y < CHUNK_SIZE; y++)
		{
			for (int z = 0; z < CHUNK_SIZE; z++)
			{
				for (int x = 0; x < CHUNK_SIZE; x++)
				{
					int voxelIdx = voxel_index(x, y, z);
					byte blockId = voxels[voxelIdx];
					
					if (blockId == 0)
						continue;
					
					Block_Definition blockDef = Block_Registry.Blocks[blockId];
					if (blockDef == null || blockDef.faceUVs == null)
						continue;
					
					// Cache model reference once per block (small registry lookup cost)
					Block_Model model = blockDef.Model;
					if (model == null || model.Vertices == null)
						continue;
					
					// Determine number of faces based on model type
					int numFaces = model.Vertices.Length / 4;  // 4 vertices per face
					bool isCube = model.type == Block_Model.Type.Cube;
					
					// Check each face
					for (int face = 0; face < numFaces; face++)
					{
						// Only do face culling for cubes
						if (isCube && face < 6)
						{
							Vector3I offset = FaceOffsets[face];
							int nx = x + offset.X;
							int ny = y + offset.Y;
							int nz = z + offset.Z;
							
							bool isAir;
							
							// Fast path: inside chunk (99% of faces)
							if ((uint)nx < CHUNK_SIZE && (uint)ny < CHUNK_SIZE && (uint)nz < CHUNK_SIZE)
							{
								isAir = voxels[voxel_index(nx, ny, nz)] == 0;
							}
							else
							{
								// Slow path: border query (1% of faces)
								int worldX = chunkX + x;
								int worldY = chunkY + y;
								int worldZ = chunkZ + z;
								isAir = get_block(new Vector3I(worldX + offset.X, worldY + offset.Y, worldZ + offset.Z)) == 0;
							}
							
							if (!isAir)
								continue;
						}
						
						// Check if we need to resize arrays
						int neededVertices = vertexCount + 12; // 4 verts * 3 components
						int neededUvs = uvCount + 8; // 4 uvs * 2 components
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
						
						// Emit face - write directly to flat arrays (NO Vector3 allocations)
						int baseVertex = vertexCount / 3;
						float fx = x, fy = y, fz = z;
						
						// Get vertices and normals from model (supports custom shapes)
						int vertStart = face * 4;  // Each face has 4 vertices
						Vector2[][] uvs = blockDef.faceUVs;
						
						// For non-cube blocks with more than 6 faces, reuse UVs from first 6 faces
						int uvFace = face < uvs.Length ? face : face % 6;
						
						// Add 4 vertices - using model data, writing to flat arrays
						for (int i = 0; i < 4; i++)
						{
							Vector3 vert = model.Vertices[vertStart + i];  // Model lookup (small cost)
							Vector3 norm = model.Normals[vertStart + i];
							
							// Write to flat arrays (zero allocations - keeps huge performance gain)
							meshVerticesFlat[vertexCount++] = vert.X + fx;
							meshVerticesFlat[vertexCount++] = vert.Y + fy;
							meshVerticesFlat[vertexCount++] = vert.Z + fz;
							
							meshNormalsFlat[vertexCount - 3] = norm.X;
							meshNormalsFlat[vertexCount - 2] = norm.Y;
							meshNormalsFlat[vertexCount - 1] = norm.Z;
							
							meshUvsFlat[uvCount++] = uvs[uvFace][i].X;
							meshUvsFlat[uvCount++] = uvs[uvFace][i].Y;
						}
						
						// Add 6 indices - use model indices for proper winding
						int indicesStart = face * 6;  // Each face has 6 indices (2 triangles)
						for (int i = 0; i < 6; i++)
						{
							// Offset model indices to match current vertex batch
							meshIndicesArray[indexCount++] = baseVertex + (model.Indices[indicesStart + i] - vertStart);
						}
					}
				}
			}
		}

		// Rent pooled arrays - will be returned after mesh creation
		int vCount = vertexCount / 3;
		int uCount = uvCount / 2;
		
		Vector3[] vertices = Vector3Pool.Rent(vCount > 0 ? vCount : 1);
		Vector3[] normals = Vector3Pool.Rent(vCount > 0 ? vCount : 1);
		Vector2[] uvs2 = Vector2Pool.Rent(uCount > 0 ? uCount : 1);
		int[] indices = IntPool.Rent(indexCount > 0 ? indexCount : 1);
		
		for (int i = 0; i < vCount; i++)
		{
			int idx = i * 3;
			vertices[i] = new Vector3(meshVerticesFlat[idx], meshVerticesFlat[idx + 1], meshVerticesFlat[idx + 2]);
			normals[i] = new Vector3(meshNormalsFlat[idx], meshNormalsFlat[idx + 1], meshNormalsFlat[idx + 2]);
		}
		
		for (int i = 0; i < uCount; i++)
		{
			int idx = i * 2;
			uvs2[i] = new Vector2(meshUvsFlat[idx], meshUvsFlat[idx + 1]);
		}
		
		Array.Copy(meshIndicesArray, indices, indexCount);

		// Pass actual counts so we know how much of the rented arrays to use
		CallDeferred("load_ready_chunk", position, vertices, normals, uvs2, indices, vCount, uCount, indexCount);
	}

	public void load_ready_chunk(Vector3I position, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices, int vertCount, int uvCount, int idxCount)
	{
		// Check if chunk still exists (may have been unloaded)
		if (!chunks.TryGetValue(position, out var chunk))
		{
			// Return pooled arrays even if chunk was unloaded
			Vector3Pool.Return(vertices, clearArray: false);
			Vector3Pool.Return(normals, clearArray: false);
			Vector2Pool.Return(uvs, clearArray: false);
			IntPool.Return(indices, clearArray: false);
			return;
		}
		
		if (vertCount == 0 || idxCount == 0)
		{
			// Return pooled arrays
			Vector3Pool.Return(vertices, clearArray: false);
			Vector3Pool.Return(normals, clearArray: false);
			Vector2Pool.Return(uvs, clearArray: false);
			IntPool.Return(indices, clearArray: false);
			
			// If no visible faces, remove the mesh if it exists
			if (chunk.MeshInstance != null && GodotObject.IsInstanceValid(chunk.MeshInstance))
			{
				if (chunk.MeshInstance.GetParent() != null)
				{
					RemoveChild(chunk.MeshInstance);
				}
				chunk.MeshInstance.QueueFree();
				chunk.MeshInstance = null;
			}
			chunk.Loaded = true;
			return;
		}

		// Create mesh using only the valid portion of the arrays
		Mesh newMesh = create_mesh_from_data(vertices, normals, uvs, indices, vertCount, uvCount, idxCount);
		
		// Return pooled arrays after mesh is created
		Vector3Pool.Return(vertices, clearArray: false);
		Vector3Pool.Return(normals, clearArray: false);
		Vector2Pool.Return(uvs, clearArray: false);
		IntPool.Return(indices, clearArray: false);
		
		// Update existing mesh instance or create new one
		if (chunks[position].MeshInstance != null && GodotObject.IsInstanceValid(chunks[position].MeshInstance))
		{
			// Just swap the mesh - no visual interruption
			chunks[position].MeshInstance.Mesh = newMesh;
		}
		else
		{
			// Create new mesh instance
			chunks[position].MeshInstance = new MeshInstance3D();
			chunks[position].MeshInstance.MaterialOverride = Mat;
			chunks[position].MeshInstance.Transform = new Transform3D(chunks[position].MeshInstance.Transform.Basis, position * new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE));
			chunks[position].MeshInstance.Mesh = newMesh;
			AddChild(chunks[position].MeshInstance);
		}
		
		chunks[position].Loaded = true;
		activeChunks.Add(position);
		loadingQueue.Remove(position); // Clean up the queue
	}
	// ---------------------------- TERRAIN GENERATION ----------------------------
	public byte[] create_chunk_data(Vector3I chunkPos)
	{
		// Use pooled array - chunk will own this and we won't return it
		// (chunks hold voxel data for their lifetime)
		byte[] data = new byte[CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE];
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
					if (worldY <= height)
					{
						byte blockType = (byte)Random.Shared.Next(1, 4); // 1, 2, or 3
						data[index] = blockType;

					}
					else
					{
						data[index] = 0; // Air
					}
				}
			}
		}
		return data;
	}

	// ---------------------------- MESH CREATION ----------------------------
	public Mesh create_mesh_from_data(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices, int vertCount, int uvCount, int idxCount)
	{
		// Copy only the valid portion to correctly-sized arrays for Godot
		var finalVerts = new Vector3[vertCount];
		var finalNormals = new Vector3[vertCount];
		var finalUvs = new Vector2[uvCount];
		var finalIndices = new int[idxCount];
		
		Array.Copy(vertices, finalVerts, vertCount);
		Array.Copy(normals, finalNormals, vertCount);
		Array.Copy(uvs, finalUvs, uvCount);
		Array.Copy(indices, finalIndices, idxCount);
		
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		
		arrays[(int)Mesh.ArrayType.Vertex] = finalVerts;
		arrays[(int)Mesh.ArrayType.Normal] = finalNormals;
		arrays[(int)Mesh.ArrayType.TexUV] = finalUvs;
		arrays[(int)Mesh.ArrayType.Index] = finalIndices;
		
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		
		return arrayMesh;
	}


	// ---------------------------- INTERACTIONS ----------------------------
	public int get_block(Vector3I position)
	{
		var chunkPos = world_to_chunk(position);
		
		// Thread-safe: capture chunk and voxels reference before validation
		Chunk chunk;
		byte[] voxels;
		
		// Return air if chunk doesn't exist or isn't generated
		if (!chunks.TryGetValue(chunkPos, out chunk))
			return 0;
			
		if (!chunk.Generated || chunk.Voxels == null)
			return 0;
			
		voxels = chunk.Voxels; // Capture reference so it won't be nulled by main thread
		
		Vector3I localPos = new Vector3I(
			position.X - (chunkPos.X * CHUNK_SIZE),
			position.Y - (chunkPos.Y * CHUNK_SIZE),
			position.Z - (chunkPos.Z * CHUNK_SIZE)
		);
		
		// Validate bounds - must be 0-15
		if (localPos.X < 0 || localPos.X >= CHUNK_SIZE ||
			localPos.Y < 0 || localPos.Y >= CHUNK_SIZE ||
			localPos.Z < 0 || localPos.Z >= CHUNK_SIZE)
		{
			return 0; // Silent fail for out of bounds (common at chunk borders)
		}
		
		// Get the voxel index and return the block ID
		int index = voxel_index(localPos);
		return voxels[index];
	}
	
	public void set_block(Vector3I position, int blockId)
	{
		var chunkPos = world_to_chunk(position);
		if (!chunks.ContainsKey(chunkPos))
			return;
	
		// Mark chunk as dirty
		if (!chunks[chunkPos].Dirty)
		{
			chunks[chunkPos].Dirty = true;
			dirtyChunks.Add(chunkPos);
		}

		// FIX: Convert world position to local position before indexing
		Vector3I localPos = new Vector3I(
			position.X - (chunkPos.X * CHUNK_SIZE),
			position.Y - (chunkPos.Y * CHUNK_SIZE),
			position.Z - (chunkPos.Z * CHUNK_SIZE)
		);
		
		// Handle negative positions correctly
		if (localPos.X < 0) localPos.X += CHUNK_SIZE;
		if (localPos.Y < 0) localPos.Y += CHUNK_SIZE;
		if (localPos.Z < 0) localPos.Z += CHUNK_SIZE;
		
		chunks[chunkPos].Voxels[voxel_index(localPos)] = (byte)blockId;
		
		// Mark neighboring chunks as dirty if on chunk boundary
		if (localPos.X == 0) mark_neighbor_dirty(chunkPos + new Vector3I(-1, 0, 0));
		if (localPos.X == CHUNK_SIZE - 1) mark_neighbor_dirty(chunkPos + new Vector3I(1, 0, 0));
		if (localPos.Y == 0) mark_neighbor_dirty(chunkPos + new Vector3I(0, -1, 0));
		if (localPos.Y == CHUNK_SIZE - 1) mark_neighbor_dirty(chunkPos + new Vector3I(0, 1, 0));
		if (localPos.Z == 0) mark_neighbor_dirty(chunkPos + new Vector3I(0, 0, -1));
		if (localPos.Z == CHUNK_SIZE - 1) mark_neighbor_dirty(chunkPos + new Vector3I(0, 0, 1));
	}

	private void mark_neighbor_dirty(Vector3I chunkPos)
	{
		if (chunks.TryGetValue(chunkPos, out var chunk))
		{
			if(chunk.Generated && !chunk.Dirty)
			{
				chunk.Dirty = true;
				dirtyChunks.Add(chunkPos);
			}
		}
	}

	public void break_block(Vector3I position)
	{
		set_block(position, 0);
	}
	public void place_block(Vector3I position, int blockId)
	{
		set_block(position, blockId);
	}

	// ---------------------------- HELPERS ----------------------------
	public Vector3I world_to_chunk(Vector3I worldPos)
	{
		return new Vector3I(
			Mathf.FloorToInt((float)worldPos.X / CHUNK_SIZE),
			Mathf.FloorToInt((float)worldPos.Y / CHUNK_SIZE),
			Mathf.FloorToInt((float)worldPos.Z / CHUNK_SIZE)
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
}
