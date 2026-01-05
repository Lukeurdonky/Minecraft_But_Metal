// using Godot;
// using System;
// using System.Collections.Generic;
// using System.Diagnostics;
// using System.Linq;
// using System.Threading;
// using System.Threading.Tasks;

// public partial class Chunk_Manager : Node
// {
// 	private const int CHUNK_SIZE = 16;
// 	private readonly Vector3I CHUNK_SIZE_VEC = new(16, 16, 16);
	
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
// 	private Vector3I generatePosition;
// 	private Vector3I loadPosition;
// 	private float timeElapsed = 0f;
// 	private const float TIME_HANDLE = 0.0025f;

// 	public override void _Ready()
// 	{
// 		Global = GetNode("/root/Global");
		
// 		// Register this as the CubeManager in Global
// 		Global.Set("CubeManager", this);
		
// 		noise = new FastNoiseLite();
// 		noise.Seed = 42;
// 	}

// 	public override void _PhysicsProcess(double delta)
// 	{
// 		timeElapsed += (float)delta;
// 		if (timeElapsed >= TIME_HANDLE)
// 		{
// 			// Debug.WriteLine(Block_Registry.Blocks[0].Name);
// 			timeElapsed = 0;
			
// 			cleanup_threads();
// 			handle_chunks_art();
// 			handle_dirties();
// 			process_queues();
// 		}
// 	}

// 	public void cleanup_threads()
// 	{
// 		if (generationThread != null && !generationThread.IsAlive)
// 		{
// 			// generationThread.Join();
// 			generationThread = null;
// 		}
// 		if (loadingThread != null && !loadingThread.IsAlive)
// 		{
// 			// loadingThread.Join();
// 			loadingThread = null;
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
// 				}
// 			}
// 		}
		
// 		// Unload chunks out of range (only iterate through loaded chunks)
// 		foreach (var chunkPos in activeChunks.ToList())
// 		{
// 			if (!activeSet.Contains(chunkPos))
// 			{
// 				unload(chunkPos);
// 				activeChunks.Remove(chunkPos);
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
// 		}
// 	}

// 	private void process_queues()
// 	{
// 		// Start generation thread if idle and work available
// 		if (generationThread == null && generationQueue.Count > 0)
// 		{
// 			var nextChunk = generationQueue[0];
// 			generationQueue.RemoveAt(0);
// 			spacer_generation(nextChunk);
// 		}
		
// 		// Start loading thread if idle and work available
// 		if (loadingThread == null && loadingQueue.Count > 0)
// 		{
// 			var nextChunk = loadingQueue[0];
// 			loadingQueue.RemoveAt(0);
// 			spacer_loading(nextChunk);
// 		}
// 	}

// 	public void unload(Vector3I position)
// 	{
// 		if (!chunks.ContainsKey(position))
// 		return;
	
// 		var chunk = chunks[position];
		
// 		// Free the visual and collision objects
// 		chunk.MeshInstance?.QueueFree();
// 		chunk.CollisionShape?.QueueFree();
		
// 		// Clear references
// 		chunk.MeshInstance = null;
// 		chunk.CollisionShape = null;
		
// 		// Mark as unloaded
// 		chunk.Loaded = false;
// 	}

// 	// ---------------------------- SPACERS ----------------------------
// 	public void spacer_generation(Vector3I position)
// 	{
// 		generationThread = new Thread(() => generate_data(position));
// 		chunks[position] = new Chunk(position);
// 		// generatePosition = position;
// 		generationThread.Start();
// 	}

// 	public void spacer_loading(Vector3I position)
// 	{
// 		loadingThread = new Thread(() => load_calculate(position));
// 		loadPosition = position;
// 		loadingThread.Start();
// 	}

// 	// ---------------------------- THREAD SLOP ----------------------------
// 	// Generation Thread
// 	public void generate_data(Vector3I position)
// 	{
// 		// var position = generatePosition;
// 		// Debug.WriteLine("Generating chunk at " + position);

// 		chunks[position].Voxels = create_chunk_data(position);
		
// 		CallDeferred("generate_ready_chunk", position);
// 	}

// 	public void generate_ready_chunk(Vector3I position)
// 	{
// 		// Debug.WriteLine("Generated chunk at " + position);
		
// 		chunks[position].Generated = true;
// 	}

// 	// Loading Thread
// 	public void load_calculate(Vector3I position)
// 	{
// 		// GD.Print($"Block_Registry has {Block_Registry.Blocks[1].Model.Vertices} blocks");
// 		// create mesh
		
		
// 		// chunks[position].MeshInstance = mesh_instance;
		
// 		// Build mesh data arrays (vertices, normals, uvs, indices, colors)
// 		List<Vector3> vertices = new List<Vector3>();
// 		List<Vector3> normals = new List<Vector3>();
// 		List<Vector2> uvs = new List<Vector2>();
// 		List<int> indices = new List<int>();

// 		// Build collision data (flat array of triangles)
// 		List<Vector3> collisionFaces = new List<Vector3>();

// 		// create mesh and collision data from chunk voxels
// 		Vector3I[] faceOffsets = new Vector3I[]
// 		{
// 			new Vector3I(0, 0, -1),  // Front (Z-)
// 			new Vector3I(0, 0, 1),   // Back (Z+)
// 			new Vector3I(-1, 0, 0),  // Left (X-)
// 			new Vector3I(1, 0, 0),   // Right (X+)
// 			new Vector3I(0, 1, 0),   // Top (Y+)
// 			new Vector3I(0, -1, 0)   // Bottom (Y-)
// 		};

// 		byte[] voxels = chunks[position].Voxels;
// 		Vector3I worldOffset = position * CHUNK_SIZE;

// 		// Iterate through all blocks in chunk
// 		for (int x = 0; x < CHUNK_SIZE; x++)
// 		{
// 			for (int y = 0; y < CHUNK_SIZE; y++)
// 			{
// 				for (int z = 0; z < CHUNK_SIZE; z++)
// 				{
// 					Vector3I localPos = new Vector3I(x, y, z);
// 					int index = voxel_index(localPos);
// 					byte blockId = voxels[index];
					
// 					// Skip air blocks
// 					if (blockId == 0)
// 						continue;
					
// 					Block_Definition blockDef = Block_Registry.Blocks[blockId];
// 					if (blockDef == null || blockDef.Model == null || blockDef.faceUVs == null)
// 						continue;
					
// 					Block_Model model = blockDef.Model;
// 					Vector3I worldPos = worldOffset + localPos;
// 					Vector3 offset = new Vector3(localPos.X, localPos.Y, localPos.Z);
					
// 					// Check each face
// 					for (int faceIdx = 0; faceIdx < 6; faceIdx++)
// 					{
// 						Vector3I neighborPos = worldPos + faceOffsets[faceIdx];
						
// 						// Only add face if neighbor is air
// 						if (get_block(neighborPos) == 0)
// 						{
// 							int baseVertex = vertices.Count;
// 							int vertStart = faceIdx * 4;
// 							int indicesStart = faceIdx * 6; // Each face has 6 indices
							
// 							// Add 4 vertices for this face
// 							for (int i = 0; i < 4; i++)
// 							{
// 								vertices.Add(model.Vertices[vertStart + i] + offset);
// 								normals.Add(model.Normals[vertStart + i]);
// 								uvs.Add(blockDef.faceUVs[faceIdx][i]);
// 							}
							
// 							// Add 6 indices for 2 triangles from the model
// 							for (int i = 0; i < 6; i++)
// 							{
// 								// Adjust model indices by baseVertex offset
// 								indices.Add(baseVertex + (model.Indices[indicesStart + i] - vertStart));
// 							}
							
// 							// Add collision triangles using the same index pattern
// 							collisionFaces.Add(model.Vertices[vertStart + (model.Indices[indicesStart + 0] - vertStart)] + offset);
// 							collisionFaces.Add(model.Vertices[vertStart + (model.Indices[indicesStart + 1] - vertStart)] + offset);
// 							collisionFaces.Add(model.Vertices[vertStart + (model.Indices[indicesStart + 2] - vertStart)] + offset);
							
// 							collisionFaces.Add(model.Vertices[vertStart + (model.Indices[indicesStart + 3] - vertStart)] + offset);
// 							collisionFaces.Add(model.Vertices[vertStart + (model.Indices[indicesStart + 4] - vertStart)] + offset);
// 							collisionFaces.Add(model.Vertices[vertStart + (model.Indices[indicesStart + 5] - vertStart)] + offset);
// 						}
// 					}
// 				}
// 			}
// 		}

// 		//prepare arrays
// 		Vector3[] verticesArray = vertices.ToArray();
// 		Vector3[] normalsArray = normals.ToArray();
// 		Vector2[] uvsArray = uvs.ToArray();
// 		int[] indicesArray = indices.ToArray();
// 		Vector3[] collisionData = collisionFaces.ToArray();

// 		//make it real
// 		CallDeferred("load_ready_chunk", position, collisionData, verticesArray, normalsArray, uvsArray, indicesArray);
// 	}

// 	public void load_ready_chunk(Vector3I position, Vector3[] collisionData, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices)
// 	{
// 		if (vertices.Length == 0 || indices.Length == 0)
// 		{
// 			// GD.Print($"Chunk {position} has no visible faces, skipping mesh creation");
// 			chunks[position].Loaded = true;
// 			return;
// 		}

// 		// Remove old mesh if it exists and has a parent
// 		if (chunks[position].MeshInstance != null)
// 		{
// 			if (chunks[position].MeshInstance.GetParent() != null)
// 			{
// 				RemoveChild(chunks[position].MeshInstance);
// 			}
// 			chunks[position].MeshInstance.QueueFree();
// 		}

// 		// Remove old collision if it exists
// 		if (chunks[position].CollisionShape != null)
// 		{
// 			if (chunks[position].CollisionShape.GetParent() != null)
// 			{
// 				RemoveChild(chunks[position].CollisionShape);
// 			}
// 			chunks[position].CollisionShape.QueueFree();
// 		}
		
// 		// bring the mesh instance and collision shape into the real world
// 		object[] meshData = new object[] { vertices, normals, uvs, indices };
		
// 		Mesh mesh = create_mesh_from_data(meshData);
// 		ConcavePolygonShape3D shape = create_collision_from_data(collisionData);


// 		var static_body = new StaticBody3D();
// 		AddChild(static_body);

// 		//  Attach a CollisionShape3D to the StaticBody3D
// 		var shape_owner = static_body.CreateShapeOwner(static_body);
// 		static_body.ShapeOwnerAddShape(shape_owner, shape);
// 		static_body.GlobalTransform = new Transform3D(static_body.GlobalTransform.Basis, position*new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE));
// 		chunks[position].CollisionShape = static_body;

// 		chunks[position].MeshInstance = new MeshInstance3D();
// 		chunks[position].MeshInstance.MaterialOverride = Mat;
// 		chunks[position].MeshInstance.Transform = new Transform3D(chunks[position].MeshInstance.Transform.Basis, position * new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE));
// 		chunks[position].MeshInstance.Mesh = mesh;
// 		AddChild(chunks[position].MeshInstance);
// 		chunks[position].Loaded = true;
// 	}

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
					
// 					float height = noise.GetNoise2D(worldX, worldZ) * 10 + (int)Global.Get("SURFACE_LEVEL");
					
// 					int index = voxel_index(new Vector3I(x, y, z));
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
// 		return voxel_index(new Vector3I(x, y, z));
// 	}
// 	public int voxel_index(Vector3I index)
// 	{
// 		return index.X + CHUNK_SIZE * (index.Y + CHUNK_SIZE * index.Z);
// 	}
// }
