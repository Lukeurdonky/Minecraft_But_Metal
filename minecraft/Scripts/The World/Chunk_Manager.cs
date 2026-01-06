using Godot;
using System;
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
	private const float TIME_HANDLE = 0.05f;
	
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

	public override void _Ready()
	{
		Global = GetNode<Global>("/root/Global");
		
		// Register this as the CubeManager in Global
		Global.CubeManager = this;
		
		// Cache values for threads (avoid Godot API in threads)
		surfaceLevel = Global.SurfaceLevel;
		
		noise = new FastNoiseLite();
		noise.Seed = noiseSeed;
		
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

	public override void _PhysicsProcess(double delta)
	{
		timeElapsed += (float)delta;
		if (timeElapsed >= TIME_HANDLE)
		{
			timeElapsed = 0;
			
			handle_chunks_art();
			handle_dirties();
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
			RecalculateChunkOffsets();
			
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

			if (chunks.ContainsKey(chunkPos))
			{
				var chunk = chunks[chunkPos];
				
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
			if (chunks.ContainsKey(chunkPos))
			{
				chunks[chunkPos].Dirty = false;
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
		if (!chunks.ContainsKey(position))
		return;
	
		var chunk = chunks[position];
		
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
		if (!chunks.ContainsKey(position))
			return;

		chunks[position].Voxels = create_chunk_data(position);
		
		CallDeferred("generate_ready_chunk", position);
	}

	public void generate_ready_chunk(Vector3I position)
	{
		// Check if chunk still exists (may have been unloaded)
		if (!chunks.ContainsKey(position))
			return;
		
		chunks[position].Generated = true;
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

		// Convert to Godot types ONLY at the end (main thread will handle this)
		Vector3[] vertices = new Vector3[vertexCount / 3];
		Vector3[] normals = new Vector3[vertexCount / 3];
		Vector2[] uvs2 = new Vector2[uvCount / 2];
		int[] indices = new int[indexCount];
		
		for (int i = 0; i < vertices.Length; i++)
		{
			int idx = i * 3;
			vertices[i] = new Vector3(meshVerticesFlat[idx], meshVerticesFlat[idx + 1], meshVerticesFlat[idx + 2]);
			normals[i] = new Vector3(meshNormalsFlat[idx], meshNormalsFlat[idx + 1], meshNormalsFlat[idx + 2]);
		}
		
		for (int i = 0; i < uvs2.Length; i++)
		{
			int idx = i * 2;
			uvs2[i] = new Vector2(meshUvsFlat[idx], meshUvsFlat[idx + 1]);
		}
		
		Array.Copy(meshIndicesArray, indices, indexCount);

		CallDeferred("load_ready_chunk", position, vertices, normals, uvs2, indices);
	}

	public void load_ready_chunk(Vector3I position, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices)
	{
		// Check if chunk still exists (may have been unloaded)
		if (!chunks.ContainsKey(position))
			return;
		
		if (vertices.Length == 0 || indices.Length == 0)
		{
			// If no visible faces, remove the mesh if it exists
			if (chunks[position].MeshInstance != null && GodotObject.IsInstanceValid(chunks[position].MeshInstance))
			{
				if (chunks[position].MeshInstance.GetParent() != null)
				{
					RemoveChild(chunks[position].MeshInstance);
				}
				chunks[position].MeshInstance.QueueFree();
				chunks[position].MeshInstance = null;
			}
			chunks[position].Loaded = true;
			return;
		}

		// Create NEW mesh BEFORE removing old one (prevents flash)
		object[] meshArrays = new object[] { vertices, normals, uvs, indices };
		Mesh newMesh = create_mesh_from_data(meshArrays);
		
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
	}
	// ---------------------------- TERRAIN GENERATION ----------------------------
	public byte[] create_chunk_data(Vector3I chunkPos)
	{
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
						data[index] = 1; // Solid block
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
	public Mesh create_mesh_from_data(object[] meshData)
	{
		Vector3[] vertices = (Vector3[])meshData[0];
		Vector3[] normals = (Vector3[])meshData[1];
		Vector2[] uvs = (Vector2[])meshData[2];
		int[] indices = (int[])meshData[3];
		
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


	// ---------------------------- INTERACTIONS ----------------------------
	public int get_block(Vector3I position)
	{
		var chunkPos = world_to_chunk(position);
		
		// Thread-safe: capture chunk and voxels reference before validation
		Chunk chunk;
		byte[] voxels;
		
		// Return air if chunk doesn't exist or isn't generated
		if (!chunks.ContainsKey(chunkPos))
			return 0;
			
		chunk = chunks[chunkPos];
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
		if (chunks.ContainsKey(chunkPos) && chunks[chunkPos].Generated && !chunks[chunkPos].Dirty)
		{
			chunks[chunkPos].Dirty = true;
			dirtyChunks.Add(chunkPos);
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
