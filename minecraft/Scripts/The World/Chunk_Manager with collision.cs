// using Godot;
// using System;
// using System.Collections.Generic;
// using System.Diagnostics;
// using System.Linq;
// using System.Runtime.CompilerServices;
// using System.Threading;
// using System.Threading.Tasks;

// public partial class Chunk_Manager : Node
// {
// 	private const int CHUNK_SIZE = 16;
	
// 	// Static face data - allocated once, reused forever
// 	private static readonly Vector3I[] FaceOffsets = new Vector3I[]
// 	{
// 		new Vector3I(0, 0, -1),  // Front (Z-)
// 		new Vector3I(0, 0, 1),   // Back (Z+)
// 		new Vector3I(-1, 0, 0),  // Left (X-)
// 		new Vector3I(1, 0, 0),   // Right (X+)
// 		new Vector3I(0, 1, 0),   // Top (Y+)
// 		new Vector3I(0, -1, 0)   // Bottom (Y-)
// 	};
	
// 	private static readonly Vector3[][] FaceCubeVertices = new Vector3[][]
// 	{
// 		// Front (Z-)
// 		new Vector3[] { new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0) },
// 		// Back (Z+)
// 		new Vector3[] { new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1) },
// 		// Left (X-)
// 		new Vector3[] { new(0,0,0), new(0,0,1), new(0,1,1), new(0,1,0) },
// 		// Right (X+)
// 		new Vector3[] { new(1,0,0), new(1,0,1), new(1,1,1), new(1,1,0) },
// 		// Top (Y+)
// 		new Vector3[] { new(0,1,0), new(1,1,0), new(1,1,1), new(0,1,1) },
// 		// Bottom (Y-)
// 		new Vector3[] { new(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1) }
// 	};
	
// 	private static readonly Vector3[] FaceNormals = new Vector3[]
// 	{
// 		new Vector3(0, 0, -1),  // Front
// 		new Vector3(0, 0, 1),   // Back
// 		new Vector3(-1, 0, 0),  // Left
// 		new Vector3(1, 0, 0),   // Right
// 		new Vector3(0, 1, 0),   // Top
// 		new Vector3(0, -1, 0)   // Bottom
// 	};
	
// 	// Index patterns per face (from Block_Registry)
// 	private static readonly int[][] FaceIndices = new int[][]
// 	{
// 		new int[] { 0, 1, 2, 2, 3, 0 },  // Front
// 		new int[] { 0, 2, 1, 0, 3, 2 },  // Back
// 		new int[] { 0, 2, 1, 0, 3, 2 },  // Left
// 		new int[] { 0, 1, 2, 2, 3, 0 },  // Right
// 		new int[] { 0, 1, 2, 2, 3, 0 },  // Top
// 		new int[] { 0, 2, 1, 0, 3, 2 }   // Bottom
// 	};
	
// 	private Node Global;
// 	private Dictionary<Vector3I, Chunk> chunks = new();
// 	private HashSet<Vector3I> activeChunks = new();
// 	private HashSet<Vector3I> dirtyChunks = new();
// 	private List<Vector3I> generationQueue = new();
// 	private List<Vector3I> loadingQueue = new();
	
// 	[Export] public Material Mat;
// 	[Export] public int RenderDistance = 5;
	
// 	private FastNoiseLite noise;
// 	private Thread generationThread;
// 	private Thread loadingThread;
// 	private volatile bool threadsRunning = true;
	
// 	// Thread-safe work queues
// 	private readonly Queue<Vector3I> generationWorkQueue = new Queue<Vector3I>();
// 	private readonly Queue<Vector3I> loadingWorkQueue = new Queue<Vector3I>();
// 	private readonly object generationLock = new object();
// 	private readonly object loadingLock = new object();
	
// 	// Cached data for worker threads (no Godot API access)
// 	private int surfaceLevel;
// 	private int noiseSeed = 42;
	
// 	private float timeElapsed = 0f;
// 	private const float TIME_HANDLE = 0.0025f;
	
// 	// Reusable mesh builder buffers - NO GC PRESSURE
// 	private float[] meshVerticesFlat = new float[4096 * 3]; // Store as flat floats
// 	private float[] meshNormalsFlat = new float[4096 * 3];
// 	private float[] meshUvsFlat = new float[4096 * 2];
// 	private int[] meshIndicesArray = new int[6144];
// 	private float[] meshCollisionFlat = new float[6144 * 3];
	
// 	private int vertexCount = 0;
// 	private int uvCount = 0;
// 	private int indexCount = 0;
// 	private int collisionCount = 0;

// 	public override void _Ready()
// 	{
// 		Global = GetNode("/root/Global");
		
// 		// Register this as the CubeManager in Global
// 		Global.Set("CubeManager", this);
		
// 		// Cache values for threads (avoid Godot API in threads)
// 		surfaceLevel = (int)Global.Get("SURFACE_LEVEL");
		
// 		noise = new FastNoiseLite();
// 		noise.Seed = noiseSeed;
		
// 		// Start long-lived worker threads
// 		generationThread = new Thread(GenerationWorkerLoop);
// 		generationThread.Name = "ChunkGeneration";
// 		generationThread.Start();
		
// 		loadingThread = new Thread(LoadingWorkerLoop);
// 		loadingThread.Name = "MeshGeneration";
// 		loadingThread.Start();
// 	}
	
// 	public override void _ExitTree()
// 	{
// 		// Signal threads to stop
// 		threadsRunning = false;
		
// 		// Wake threads if sleeping
// 		lock (generationLock) Monitor.Pulse(generationLock);
// 		lock (loadingLock) Monitor.Pulse(loadingLock);
		
// 		// Wait for graceful shutdown
// 		generationThread?.Join(1000);
// 		loadingThread?.Join(1000);
// 	}

// 	public override void _PhysicsProcess(double delta)
// 	{
// 		timeElapsed += (float)delta;
// 		if (timeElapsed >= TIME_HANDLE)
// 		{
// 			timeElapsed = 0;
			
// 			handle_chunks_art();
// 			handle_dirties();
// 		}
// 	}

// 	public void handle_chunks_art()
// 	{
// 		Vector3 pPos = (Vector3)Global.Call("get_player_pos");
// 		Vector3I playerPos = ((Vector3I)pPos) / CHUNK_SIZE;
// 		var activeSet = new HashSet<Vector3I>();
// 		var chunkPositions = new List<Vector3I>();
		
// 		// Build list of all chunk positions within render distance
// 		for (int x = -RenderDistance; x <= RenderDistance; x++)
// 		{
// 			for (int y = -RenderDistance; y <= RenderDistance; y++)
// 			{
// 				for (int z = -RenderDistance; z <= RenderDistance; z++)
// 				{
// 					var offset = new Vector3I(x, y, z);
					
// 					// Skip chunks outside spherical render distance
// 					if (offset.Length() > RenderDistance)
// 						continue;
					
// 					chunkPositions.Add(offset);
// 				}
// 			}
// 		}
		
// 		// Sort chunks by distance from center (closest first)
// 		chunkPositions.Sort((a, b) => a.LengthSquared().CompareTo(b.LengthSquared()));
		
// 		// Process chunks in priority order
// 		foreach (var offset in chunkPositions)
// 		{
// 			var chunkPos = playerPos + offset;
// 			activeSet.Add(chunkPos);
			
// 			if (chunks.ContainsKey(chunkPos))
// 			{
// 				var chunk = chunks[chunkPos];
				
// 				// Chunk exists and is generated but not loaded
// 				if (chunk.Generated && !chunk.Loaded)
// 				{
// 					// Only add to loading queue if not already queued
// 					if (!loadingQueue.Contains(chunkPos))
// 					{
// 						loadingQueue.Add(chunkPos);
						
// 						// Enqueue to worker thread
// 						lock (loadingLock)
// 						{
// 							loadingWorkQueue.Enqueue(chunkPos);
// 							Monitor.Pulse(loadingLock);
// 						}
// 					}
// 				}
// 			}
// 			else
// 			{
// 				// Chunk doesn't exist - needs generation
// 				// Only add to generation queue if not already queued
// 				if (!generationQueue.Contains(chunkPos))
// 				{
// 					generationQueue.Add(chunkPos);
// 					chunks[chunkPos] = new Chunk(chunkPos);
					
// 					// Enqueue to worker thread
// 					lock (generationLock)
// 					{
// 						generationWorkQueue.Enqueue(chunkPos);
// 						Monitor.Pulse(generationLock);
// 					}
// 				}
// 			}
// 		}
		
// 		// Unload chunks out of range
// 		foreach (var chunkPos in chunks.Keys.ToList())
// 		{
// 			if (!activeSet.Contains(chunkPos))
// 			{
// 				unload(chunkPos);
// 				activeChunks.Remove(chunkPos);
// 				generationQueue.Remove(chunkPos);
// 				loadingQueue.Remove(chunkPos);
// 				chunks.Remove(chunkPos);
// 			}
// 		}
// 	}

// 	public void handle_dirties()
// 	{
// 		foreach (var chunkPos in dirtyChunks.ToList())
// 		{
// 			chunks[chunkPos].Dirty = false;
// 			loadingQueue.Insert(0, chunkPos);
// 			dirtyChunks.Remove(chunkPos);
			
// 			// Enqueue to worker thread (high priority)
// 			lock (loadingLock)
// 			{
// 				// Add to front of queue for dirty chunks
// 				var tempQueue = new Queue<Vector3I>();
// 				tempQueue.Enqueue(chunkPos);
// 				while (loadingWorkQueue.Count > 0)
// 					tempQueue.Enqueue(loadingWorkQueue.Dequeue());
// 				while (tempQueue.Count > 0)
// 					loadingWorkQueue.Enqueue(tempQueue.Dequeue());
				
// 				Monitor.Pulse(loadingLock);
// 			}
// 		}
// 	}

// 	public void unload(Vector3I position)
// 	{
// 		if (!chunks.ContainsKey(position))
// 		return;
	
// 		var chunk = chunks[position];
		
// 		// Free the visual and collision objects (check if not already disposed)
// 		if (chunk.MeshInstance != null && GodotObject.IsInstanceValid(chunk.MeshInstance))
// 		{
// 			if (chunk.MeshInstance.GetParent() != null)
// 				RemoveChild(chunk.MeshInstance);
// 			chunk.MeshInstance.QueueFree();
// 		}
		
// 		if (chunk.CollisionShape != null && GodotObject.IsInstanceValid(chunk.CollisionShape))
// 		{
// 			if (chunk.CollisionShape.GetParent() != null)
// 				RemoveChild(chunk.CollisionShape);
// 			chunk.CollisionShape.QueueFree();
// 		}
		
// 		// Clear references
// 		chunk.MeshInstance = null;
// 		chunk.CollisionShape = null;
		
// 		// Mark as unloaded
// 		chunk.Loaded = false;
// 	}

// 	// ---------------------------- WORKER THREADS ----------------------------
// 	private void GenerationWorkerLoop()
// 	{
// 		while (threadsRunning)
// 		{
// 			Vector3I position;
			
// 			lock (generationLock)
// 			{
// 				// Wait for work
// 				while (generationWorkQueue.Count == 0 && threadsRunning)
// 				{
// 					Monitor.Wait(generationLock);
// 				}
				
// 				if (!threadsRunning)
// 					break;
				
// 				position = generationWorkQueue.Dequeue();
// 			}
			
// 			// Do work outside lock
// 			generate_data(position);
// 		}
// 	}
	
// 	private void LoadingWorkerLoop()
// 	{
// 		while (threadsRunning)
// 		{
// 			Vector3I position;
			
// 			lock (loadingLock)
// 			{
// 				// Wait for work
// 				while (loadingWorkQueue.Count == 0 && threadsRunning)
// 				{
// 					Monitor.Wait(loadingLock);
// 				}
				
// 				if (!threadsRunning)
// 					break;
				
// 				position = loadingWorkQueue.Dequeue();
// 			}
			
// 			// Do work outside lock
// 			load_calculate(position);
// 		}
// 	}

// 	// ---------------------------- THREAD SLOP ----------------------------
// 	// Generation Thread
// 	public void generate_data(Vector3I position)
// 	{
// 		// Check if chunk still exists (may have been unloaded)
// 		if (!chunks.ContainsKey(position))
// 			return;

// 		chunks[position].Voxels = create_chunk_data(position);
		
// 		CallDeferred("generate_ready_chunk", position);
// 	}

// 	public void generate_ready_chunk(Vector3I position)
// 	{
// 		// Check if chunk still exists (may have been unloaded)
// 		if (!chunks.ContainsKey(position))
// 			return;
		
// 		chunks[position].Generated = true;
// 	}

// 	// Loading Thread
// 	public void load_calculate(Vector3I position)
// 	{
// 		// Check if chunk still exists (may have been unloaded)
// 		if (!chunks.ContainsKey(position))
// 			return;
		
// 		// Reset counters
// 		vertexCount = 0;
// 		uvCount = 0;
// 		indexCount = 0;
// 		collisionCount = 0;
		
// 		// Resize arrays if needed
// 		if (meshVerticesFlat.Length < 4096 * 3) meshVerticesFlat = new float[4096 * 3];
// 		if (meshNormalsFlat.Length < 4096 * 3) meshNormalsFlat = new float[4096 * 3];
// 		if (meshUvsFlat.Length < 4096 * 2) meshUvsFlat = new float[4096 * 2];
// 		if (meshIndicesArray.Length < 6144) meshIndicesArray = new int[6144];
// 		if (meshCollisionFlat.Length < 6144 * 3) meshCollisionFlat = new float[6144 * 3];

// 		byte[] voxels = chunks[position].Voxels;
// 		int chunkX = position.X * CHUNK_SIZE;
// 		int chunkY = position.Y * CHUNK_SIZE;
// 		int chunkZ = position.Z * CHUNK_SIZE;
		
// 		// CRITICAL: All loops access only LOCAL memory
// 		for (int y = 0; y < CHUNK_SIZE; y++)
// 		{
// 			for (int z = 0; z < CHUNK_SIZE; z++)
// 			{
// 				for (int x = 0; x < CHUNK_SIZE; x++)
// 				{
// 					int voxelIdx = voxel_index(x, y, z);
// 					byte blockId = voxels[voxelIdx];
					
// 					if (blockId == 0)
// 						continue;
					
// 					Block_Definition blockDef = Block_Registry.Blocks[blockId];
// 					if (blockDef == null || blockDef.faceUVs == null)
// 						continue;
					
// 					// Check each face
// 					for (int face = 0; face < 6; face++)
// 					{
// 						Vector3I offset = FaceOffsets[face];
// 						int nx = x + offset.X;
// 						int ny = y + offset.Y;
// 						int nz = z + offset.Z;
						
// 						bool isAir;
						
// 						// Fast path: inside chunk (99% of faces)
// 						if ((uint)nx < CHUNK_SIZE && (uint)ny < CHUNK_SIZE && (uint)nz < CHUNK_SIZE)
// 						{
// 							isAir = voxels[voxel_index(nx, ny, nz)] == 0;
// 						}
// 						else
// 						{
// 							// Slow path: border query (1% of faces)
// 							int worldX = chunkX + x;
// 							int worldY = chunkY + y;
// 							int worldZ = chunkZ + z;
// 							isAir = get_block(new Vector3I(worldX + offset.X, worldY + offset.Y, worldZ + offset.Z)) == 0;
// 						}
						
// 						if (!isAir)
// 							continue;
						
// 						// Check if we need to resize arrays
// 						int neededVertices = vertexCount + 12; // 4 verts * 3 components
// 						int neededUvs = uvCount + 8; // 4 uvs * 2 components
// 						int neededIndices = indexCount + 6;
// 						int neededCollision = collisionCount + 18; // 6 verts * 3 components
						
// 						if (neededVertices > meshVerticesFlat.Length)
// 						{
// 							Array.Resize(ref meshVerticesFlat, meshVerticesFlat.Length * 2);
// 							Array.Resize(ref meshNormalsFlat, meshNormalsFlat.Length * 2);
// 						}
// 						if (neededUvs > meshUvsFlat.Length)
// 							Array.Resize(ref meshUvsFlat, meshUvsFlat.Length * 2);
// 						if (neededIndices > meshIndicesArray.Length)
// 							Array.Resize(ref meshIndicesArray, meshIndicesArray.Length * 2);
// 						if (neededCollision > meshCollisionFlat.Length)
// 							Array.Resize(ref meshCollisionFlat, meshCollisionFlat.Length * 2);
						
// 						// Emit face - write directly to flat arrays (NO Vector3 allocations)
// 						int baseVertex = vertexCount / 3;
// 						float fx = x, fy = y, fz = z;
						
// 						Vector3[] faceVerts = FaceCubeVertices[face];
// 						Vector3 normal = FaceNormals[face];
// 						Vector2[][] uvs = blockDef.faceUVs;
						
// 						// Add 4 vertices
// 						for (int i = 0; i < 4; i++)
// 						{
// 							meshVerticesFlat[vertexCount++] = faceVerts[i].X + fx;
// 							meshVerticesFlat[vertexCount++] = faceVerts[i].Y + fy;
// 							meshVerticesFlat[vertexCount++] = faceVerts[i].Z + fz;
							
// 							meshNormalsFlat[vertexCount - 3] = normal.X;
// 							meshNormalsFlat[vertexCount - 2] = normal.Y;
// 							meshNormalsFlat[vertexCount - 1] = normal.Z;
							
// 							meshUvsFlat[uvCount++] = uvs[face][i].X;
// 							meshUvsFlat[uvCount++] = uvs[face][i].Y;
// 						}
						
// 						// Add 6 indices
// 						int[] faceIndices = FaceIndices[face];
// 						for (int i = 0; i < 6; i++)
// 						{
// 							meshIndicesArray[indexCount++] = baseVertex + faceIndices[i];
// 						}
						
// 						// Add collision triangles
// 						for (int i = 0; i < 6; i++)
// 						{
// 							int vi = faceIndices[i];
// 							meshCollisionFlat[collisionCount++] = faceVerts[vi].X + fx;
// 							meshCollisionFlat[collisionCount++] = faceVerts[vi].Y + fy;
// 							meshCollisionFlat[collisionCount++] = faceVerts[vi].Z + fz;
// 						}
// 					}
// 				}
// 			}
// 		}

// 		// Convert to Godot types ONLY at the end (main thread will handle this)
// 		Vector3[] vertices = new Vector3[vertexCount / 3];
// 		Vector3[] normals = new Vector3[vertexCount / 3];
// 		Vector2[] uvs2 = new Vector2[uvCount / 2];
// 		int[] indices = new int[indexCount];
// 		Vector3[] collision = new Vector3[collisionCount / 3];
		
// 		for (int i = 0; i < vertices.Length; i++)
// 		{
// 			int idx = i * 3;
// 			vertices[i] = new Vector3(meshVerticesFlat[idx], meshVerticesFlat[idx + 1], meshVerticesFlat[idx + 2]);
// 			normals[i] = new Vector3(meshNormalsFlat[idx], meshNormalsFlat[idx + 1], meshNormalsFlat[idx + 2]);
// 		}
		
// 		for (int i = 0; i < uvs2.Length; i++)
// 		{
// 			int idx = i * 2;
// 			uvs2[i] = new Vector2(meshUvsFlat[idx], meshUvsFlat[idx + 1]);
// 		}
		
// 		Array.Copy(meshIndicesArray, indices, indexCount);
		
// 		for (int i = 0; i < collision.Length; i++)
// 		{
// 			int idx = i * 3;
// 			collision[i] = new Vector3(meshCollisionFlat[idx], meshCollisionFlat[idx + 1], meshCollisionFlat[idx + 2]);
// 		}

// 		CallDeferred("load_ready_chunk", position, collision, vertices, normals, uvs2, indices);
// 	}

// 	public void load_ready_chunk(Vector3I position, Vector3[] collisionData, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices)
// 	{
// 		// Check if chunk still exists (may have been unloaded)
// 		if (!chunks.ContainsKey(position))
// 			return;
		
// 		if (vertices.Length == 0 || indices.Length == 0)
// 		{
// 			// GD.Print($"Chunk {position} has no visible faces, skipping mesh creation");
// 			chunks[position].Loaded = true;
// 			return;
// 		}

// 		// Remove old mesh if it exists and has a parent
// 		if (chunks[position].MeshInstance != null && GodotObject.IsInstanceValid(chunks[position].MeshInstance))
// 		{
// 			if (chunks[position].MeshInstance.GetParent() != null)
// 			{
// 				RemoveChild(chunks[position].MeshInstance);
// 			}
// 			chunks[position].MeshInstance.QueueFree();
// 		}

// 		// Remove old collision if it exists
// 		if (chunks[position].CollisionShape != null && GodotObject.IsInstanceValid(chunks[position].CollisionShape))
// 		{
// 			if (chunks[position].CollisionShape.GetParent() != null)
// 			{
// 				RemoveChild(chunks[position].CollisionShape);
// 			}
// 			chunks[position].CollisionShape.QueueFree();
// 		}
	
// 	// Create mesh and collision together
// 	object[] meshArrays = new object[] { vertices, normals, uvs, indices };
// 	Mesh mesh = create_mesh_from_data(meshArrays);
// 	ConcavePolygonShape3D shape = create_collision_from_data(collisionData);

// 	chunks[position].MeshInstance = new MeshInstance3D();
// 	chunks[position].MeshInstance.MaterialOverride = Mat;
// 	chunks[position].MeshInstance.Transform = new Transform3D(chunks[position].MeshInstance.Transform.Basis, position * new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE));
// 	chunks[position].MeshInstance.Mesh = mesh;
// 	AddChild(chunks[position].MeshInstance);
	
// 	var static_body = new StaticBody3D();
// 	AddChild(static_body);

// 	var shape_owner = static_body.CreateShapeOwner(static_body);
// 	static_body.ShapeOwnerAddShape(shape_owner, shape);
// 	static_body.GlobalTransform = new Transform3D(static_body.GlobalTransform.Basis, position * new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE));
// 	chunks[position].CollisionShape = static_body;
	
// 	chunks[position].Loaded = true;
// 	activeChunks.Add(position);
// }
// 	// ---------------------------- TERRAIN GENERATION ----------------------------
// 	public byte[] create_chunk_data(Vector3I chunkPos)
// 	{
// 		byte[] data = new byte[CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE];
// 		for (int x = 0; x < CHUNK_SIZE; x++)
// 		{
// 			for (int y = 0; y < CHUNK_SIZE; y++)
// 			{
// 				for (int z = 0; z < CHUNK_SIZE; z++)
// 				{
// 					int worldX = chunkPos.X * CHUNK_SIZE + x;
// 					int worldY = chunkPos.Y * CHUNK_SIZE + y;
// 					int worldZ = chunkPos.Z * CHUNK_SIZE + z;
					
// 					float height = noise.GetNoise2D(worldX, worldZ) * 10 + surfaceLevel;
					
// 					int index = voxel_index(x, y, z);
// 					if (worldY <= height)
// 					{
// 						data[index] = 1; // Solid block
// 					}
// 					else
// 					{
// 						data[index] = 0; // Air
// 					}
// 				}
// 			}
// 		}
// 		return data;
// 	}

// 	// ---------------------------- COLLISION AND VISUAL CALCULATIONS ----------------------------
// 	public Mesh create_mesh_from_data(object[] meshData)
// 	{
// 		Vector3[] vertices = (Vector3[])meshData[0];
// 		Vector3[] normals = (Vector3[])meshData[1];
// 		Vector2[] uvs = (Vector2[])meshData[2];
// 		int[] indices = (int[])meshData[3];
		
// 		var arrays = new Godot.Collections.Array();
// 		arrays.Resize((int)Mesh.ArrayType.Max);
		
// 		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
// 		arrays[(int)Mesh.ArrayType.Normal] = normals;
// 		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
// 		arrays[(int)Mesh.ArrayType.Index] = indices;
		
// 		var arrayMesh = new ArrayMesh();
// 		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		
// 		return arrayMesh;
// 	}

// 	public ConcavePolygonShape3D create_collision_from_data(Vector3[] collisionData)
// 	{
// 		var shape = new ConcavePolygonShape3D();
// 		shape.Data = collisionData;
// 		return shape;
// 	}

// 	// ---------------------------- INTERACTIONS ----------------------------
// 	public int get_block(Vector3I position)
// 	{
// 		var chunkPos = world_to_chunk(position);
		
// 		// Return air if chunk doesn't exist or isn't generated
// 		if (!chunks.ContainsKey(chunkPos) || !chunks[chunkPos].Generated)
// 			return 0;
		
// 		Vector3I localPos = new Vector3I(
// 			position.X - (chunkPos.X * CHUNK_SIZE),
// 			position.Y - (chunkPos.Y * CHUNK_SIZE),
// 			position.Z - (chunkPos.Z * CHUNK_SIZE)
// 		);
		
// 		// Validate bounds - must be 0-15
// 		if (localPos.X < 0 || localPos.X >= CHUNK_SIZE ||
// 			localPos.Y < 0 || localPos.Y >= CHUNK_SIZE ||
// 			localPos.Z < 0 || localPos.Z >= CHUNK_SIZE)
// 		{
// 			GD.PrintErr($"Invalid localPos: {localPos} from worldPos: {position}, chunkPos: {chunkPos}");
// 			return 0;
// 		}
		
// 		// Get the voxel index and return the block ID
// 		int index = voxel_index(localPos);
// 		return chunks[chunkPos].Voxels[index];
// 	}
	
// 	public void set_block(Vector3I position, int blockId)
// 	{
// 		//this is it man
// 		var chunkPos = world_to_chunk(position);
// 		if (!chunks.ContainsKey(chunkPos))
// 			return;
// 		//mark chunk as dirty
// 		if (!chunks[chunkPos].Dirty)
// 		{
// 			chunks[chunkPos].Dirty = true;
// 			dirtyChunks.Add(chunkPos);
// 		}
// 	}
// 	public int break_block(Vector3I position)
// 	{
// 		return 0;
// 	}
// 	public int place_block(Vector3I position, int blockId)
// 	{
// 		return 0;
// 	}

// 	// ---------------------------- HELPERS ----------------------------
// 	public Vector3I world_to_chunk(Vector3I worldPos)
// 	{
// 		return new Vector3I(
// 			Mathf.FloorToInt((float)worldPos.X / CHUNK_SIZE),
// 			Mathf.FloorToInt((float)worldPos.Y / CHUNK_SIZE),
// 			Mathf.FloorToInt((float)worldPos.Z / CHUNK_SIZE)
// 		);
// 	}

// 	public int world_to_index(Vector3I worldPos)
// 	{
// 		int x = worldPos.X % CHUNK_SIZE;
// 		int y = worldPos.Y % CHUNK_SIZE;
// 		int z = worldPos.Z % CHUNK_SIZE;
// 		return voxel_index(x, y, z);
// 	}

// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public int voxel_index(int x, int y, int z)
// 	{
// 		return x + (z * CHUNK_SIZE) + (y * CHUNK_SIZE * CHUNK_SIZE);
// 	}
	
// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public int voxel_index(Vector3I index)
// 	{
// 		return index.X + (index.Z * CHUNK_SIZE) + (index.Y * CHUNK_SIZE * CHUNK_SIZE);
// 	}
// }
