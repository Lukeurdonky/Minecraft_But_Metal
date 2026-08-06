using Godot;
using System;
using System.Diagnostics;
using System.Linq;

public partial class Block_Registry : Node
{
	// Atlas is Sprites/Textures/block_texture_atlas.png, measured in 16px cells:
	// 12 cells wide x 16 cells tall (192x256 px). Each block claims a 3x2 cell region
	// (one cell per face), so capacity is (12/3) * (16/2) = 4 * 8 = 32 blocks.
	//
	// Doubled from 8 to 16 on 2026-08-04 when the atlas grew downward. Existing blocks
	// keep their exact pixels: GenerateFaceUVs divides by atlasHeight, so index 0 moved
	// from 1/8 to 1/16 of an image that also doubled — the same absolute row. Only add
	// new art *below* the existing rows, or every UV in the game shifts.
	public static readonly int atlas_width = 12;
	public static readonly int atlas_height = 16;
	public static readonly Block_Definition[] Blocks;
	public static readonly Block_Model[] Models;

	// Flat mirror of Blocks[id].Transparent, built once at the end of the static ctor.
	// Chunk_Manager's mesher reads this once per face per block — a plain array index beats
	// a null check plus a property call in that loop. Sized to match Blocks, and indexed by
	// a byte block id, so it needs no bounds guard at the call site.
	public static readonly bool[] TransparentById;

	static Block_Registry()
	{
		
		
		Models = new Block_Model[8];
		Models[0] = CreateBlockModel("Cube", Block_Model.Type.Cube, ""); // Cube
		//Models[1] = CreateBlockModel("Stair", Block_Model.Type.Stair, ""); // Stair
		//Models[2] = CreateBlockModel("Slab", Block_Model.Type.Slab, ""); // Slab
		Models[3] = CreateBlockModel("Piano", Block_Model.Type.Custom, ""); // Piano
		
		Blocks = new Block_Definition[256];
		Blocks[0] = null; // Air
		Blocks[1] = new Block_Definition { Id = 1, Name = "Grass", Hardness = 1f, DropId = "grass", DropCount = 1, faceUVs = GenerateFaceUVs(0, atlas_width, atlas_height), Model = GetModel("Cube") }; // grass
		Blocks[2] = new Block_Definition { Id = 2, Name = "Dirt", Hardness = 2f, DropId = "dirt", DropCount = 1, faceUVs = GenerateFaceUVs(1, atlas_width, atlas_height), Model = GetModel("Cube") }; // dirt
		Blocks[3] = new Block_Definition { Id = 3, Name = "Stone", Hardness = 5f, DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(2, atlas_width, atlas_height), Model = GetModel("Cube") }; // stone
		Blocks[4] = new Block_Definition { Id = 4, Name = "Blue",  Hardness = 5f, DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(3, atlas_width, atlas_height), Model = GetModel("Cube") }; // blue
		Blocks[5] = new Block_Definition { Id = 5, Name = "Red",   Hardness = 5f,  DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(4, atlas_width, atlas_height), Model = GetModel("Cube") }; // red
		Blocks[6] = new Block_Definition { Id = 6, Name = "Steel", Hardness = 10f, DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(5, atlas_width, atlas_height), Model = GetModel("Cube") }; // steel
		Blocks[7] = new Block_Definition { Id = 7, Name = "Wire",         Hardness = 3f,  DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(6,  atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[8] = new Block_Definition { Id = 8, Name = "Cloud",        Hardness = 1f,  DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(7,  atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[9] = new Block_Definition { Id = 9, Name = "Smaug",        Hardness = 2f,  DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(8,  atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[10] = new Block_Definition { Id = 10, Name = "Crystal",     Hardness = 2f,  DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(9,  atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[11] = new Block_Definition { Id = 11, Name = "LightCrystal",Hardness = 1f,  DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(10, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[12] = new Block_Definition { Id = 12, Name = "Brick",       Hardness = 5f,  DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(11, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[13] = new Block_Definition { Id = 13, Name = "Sand",        Hardness = 1f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(12, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[14] = new Block_Definition { Id = 14, Name = "Moss",        Hardness = 2f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(13, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[15] = new Block_Definition { Id = 15, Name = "Lava",        Hardness = 3f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(14, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[16] = new Block_Definition { Id = 16, Name = "Virus",       Hardness = 2f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(15, atlas_width, atlas_height), Model = GetModel("Cube") };

		// NOTE: the atlas index is always Id - 1 (Blocks[1] "Grass" is atlas index 0).
		// Block id 0 is Air, which owns no art, so the two sequences are permanently
		// off by one. Get this wrong and the block renders as its neighbour.
		Blocks[17] = new Block_Definition { Id = 17, Name = "Ice",         Hardness = 1f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(16, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[18] = new Block_Definition { Id = 18, Name = "HardIce",     Hardness = 8f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(17, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[19] = new Block_Definition { Id = 19, Name = "Cactus",      Hardness = 1f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(18, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[20] = new Block_Definition { Id = 20, Name = "Crate",       Hardness = 2f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(19, atlas_width, atlas_height), Model = GetModel("Cube") };

		// Added 2026-08-05 with the transparency system. Glass and Frame carry their alpha in
		// the atlas art itself (Glass ~33% with opaque pane edges; Frame is a hard cut-out with
		// fully transparent holes) — Transparent=true only tells the mesher to route them into
		// the alpha surface and stop culling the faces behind them. Alpha stays 1 so the art is
		// shown as painted; lower it to fade a block without repainting.
		Blocks[21] = new Block_Definition { Id = 21, Name = "EmptyCrate",  Hardness = 2f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(20, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[22] = new Block_Definition { Id = 22, Name = "GreenEnergy", Hardness = 1f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(21, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[23] = new Block_Definition { Id = 23, Name = "Plate",       Hardness = 6f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(22, atlas_width, atlas_height), Model = GetModel("Cube") };
		Blocks[24] = new Block_Definition { Id = 24, Name = "Glass",       Hardness = 1f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(23, atlas_width, atlas_height), Model = GetModel("Cube"), Transparent = true };
		Blocks[25] = new Block_Definition { Id = 25, Name = "Frame",       Hardness = 4f,  DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(24, atlas_width, atlas_height), Model = GetModel("Cube"), Transparent = true };

		// Markers (2026-08-06). Builder-only blocks: placeable and visible while authoring, and
		// skipped by both Structure stamp paths so they never appear in a live world. Numbered
		// rather than named because a structure's roles are its own business — Ship.tscn decides
		// that marker 1 is mission control, and a future waystation can mean something else by it.
		// Hardness is low so a misplaced one is a single quick break.
		for (int n = 1; n <= MarkerCount; n++)
		{
			int id = FirstMarkerId + n - 1;
			Blocks[id] = new Block_Definition { Id = (ushort)id, Name = $"Marker{n}", Hardness = 1f, DropId = "stone", DropCount = 1, faceUVs = GenerateFaceUVs(id - 1, atlas_width, atlas_height), Model = GetModel("Cube"), IsMarker = true };
		}

		TransparentById = new bool[Blocks.Length];
		MarkerById      = new bool[Blocks.Length];
		for (int i = 0; i < Blocks.Length; i++)
		{
			TransparentById[i] = Blocks[i] != null && Blocks[i].Transparent;
			MarkerById[i]      = Blocks[i] != null && Blocks[i].IsMarker;
		}
	}

	// --- Markers ---

	// Ids 26-30 = Marker1..Marker5, atlas indices 25-29. That fills the atlas to 30/32.
	public const int FirstMarkerId = 26;
	public const int MarkerCount   = 5;

	// Flat mirror of Blocks[id].IsMarker, same reasoning as TransparentById: the stamp loops
	// read it once per voxel, and an array index beats a null check plus a field read.
	public static readonly bool[] MarkerById;

	// Marker number (1..MarkerCount) -> block id. Returns 0 for a number out of range, which is
	// Air and therefore matches nothing — callers get "no such marker" rather than an exception.
	public static byte MarkerBlockId(int markerNumber) =>
		markerNumber < 1 || markerNumber > MarkerCount ? (byte)0 : (byte)(FirstMarkerId + markerNumber - 1);

	// Per-block tint alpha for the mesher's vertex colours. Out-of-range/undefined ids fall
	// back to opaque, matching how the mesher already treats a null Block_Definition.
	public static float GetAlpha(int id)
	{
		if (id <= 0 || id >= Blocks.Length || Blocks[id] == null) return 1f;
		return Blocks[id].Alpha;
	}

	public static Block_Model CreateBlockModel(string name, Block_Model.Type type, string datapath)
	{
		Block_Model model = new Block_Model { name = name, type = type, datapath = datapath };
		if(Block_Model.Type.Cube == type)
		{
			// Build cube model - 24 vertices (4 per face), 36 indices (2 triangles per face)
			model.Vertices = new Vector3[]
			{
				// Front face (Z-)
				new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
				// Back face (Z+)
				new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
				// Left face (X-)
				new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0),
				// Right face (X+)
				new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0),
				// Top face (Y+)
				new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
				// Bottom face (Y-)
				new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1)
			};
			
			model.Normals = new Vector3[]
			{
				// Front face (Z-)
				new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1),
				// Back face (Z+)
				new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1),
				// Left face (X-)
				new Vector3(-1, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, 0),
				// Right face (X+)
				new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0),
				// Top face (Y+)
				new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0),
				// Bottom face (Y-)
				new Vector3(0, -1, 0), new Vector3(0, -1, 0), new Vector3(0, -1, 0), new Vector3(0, -1, 0)
			};
			
			model.Indices = new int[]
			{
				// Front face
				0, 1, 2, 2, 3, 0,
				// Back face
				4, 6, 5, 4, 7, 6,
				// Left face
				8, 10, 9, 8, 11, 10,
				// Right face
				12, 13, 14, 14, 15, 12,
				// Top face
				16, 17, 18, 18, 19, 16,
				// Bottom face
				20, 22, 21, 20, 23, 22
			};
		}
		else if(Block_Model.Type.Stair == type)
		{
			// Build stair model - stairs are two boxes stacked (bottom full width, top half at back)
			model.Vertices = new Vector3[]
			{
				// Bottom step (half height)
				// Front face (Z-)
				new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0.5f, 0), new Vector3(0, 0.5f, 0),
				// Back face (Z+)
				new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0.5f, 1), new Vector3(0, 0.5f, 1),
				// Left face (X-)
				new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0.5f, 1), new Vector3(0, 0.5f, 0),
				// Right face (X+)
				new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(1, 0.5f, 1), new Vector3(1, 0.5f, 0),
				// Top face (Y+)
				new Vector3(0, 0.5f, 0), new Vector3(1, 0.5f, 0), new Vector3(1, 0.5f, 0.5f), new Vector3(0, 0.5f, 0.5f),
				// Bottom face (Y-)
				new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1),
				
				// Top step (half width at back)
				// Front face (Z- at midpoint)
				new Vector3(0, 0.5f, 0.5f), new Vector3(1, 0.5f, 0.5f), new Vector3(1, 1, 0.5f), new Vector3(0, 1, 0.5f),
				// Back face (Z+)
				new Vector3(0, 0.5f, 1), new Vector3(1, 0.5f, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
				// Left face (X-)
				new Vector3(0, 0.5f, 0.5f), new Vector3(0, 0.5f, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0.5f),
				// Right face (X+)
				new Vector3(1, 0.5f, 0.5f), new Vector3(1, 0.5f, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0.5f),
				// Top face (Y+)
				new Vector3(0, 1, 0.5f), new Vector3(1, 1, 0.5f), new Vector3(1, 1, 1), new Vector3(0, 1, 1)
			};
			
			model.Normals = new Vector3[]
			{
				// Bottom step
				// Front
				new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1),
				// Back
				new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1),
				// Left
				new Vector3(-1, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, 0),
				// Right
				new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0),
				// Top
				new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0),
				// Bottom
				new Vector3(0, -1, 0), new Vector3(0, -1, 0), new Vector3(0, -1, 0), new Vector3(0, -1, 0),
				
				// Top step
				// Front
				new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1),
				// Back
				new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1),
				// Left
				new Vector3(-1, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, 0),
				// Right
				new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0),
				// Top
				new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0)
			};
			
			model.Indices = new int[]
			{
				// Bottom step
				// Front
				0, 1, 2, 2, 3, 0,
				// Back
				4, 6, 5, 4, 7, 6,
				// Left
				8, 10, 9, 8, 11, 10,
				// Right
				12, 13, 14, 14, 15, 12,
				// Top
				16, 17, 18, 18, 19, 16,
				// Bottom
				20, 22, 21, 20, 23, 22,
				
				// Top step
				// Front
				24, 25, 26, 26, 27, 24,
				// Back
				28, 30, 29, 28, 31, 30,
				// Left
				32, 34, 33, 32, 35, 34,
				// Right
				36, 37, 38, 38, 39, 36,
				// Top
				40, 41, 42, 42, 43, 40
			};
		}
		else if(Block_Model.Type.Slab == type)
		{
			// Build slab model - half-height cube
			model.Vertices = new Vector3[]
			{
				// Front face (Z-)
				new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0.5f, 0), new Vector3(0, 0.5f, 0),
				// Back face (Z+)
				new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0.5f, 1), new Vector3(0, 0.5f, 1),
				// Left face (X-)
				new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0.5f, 1), new Vector3(0, 0.5f, 0),
				// Right face (X+)
				new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(1, 0.5f, 1), new Vector3(1, 0.5f, 0),
				// Top face (Y+)
				new Vector3(0, 0.5f, 0), new Vector3(1, 0.5f, 0), new Vector3(1, 0.5f, 1), new Vector3(0, 0.5f, 1),
				// Bottom face (Y-)
				new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1)
			};
			
			model.Normals = new Vector3[]
			{
				// Front face (Z-)
				new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1),
				// Back face (Z+)
				new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1),
				// Left face (X-)
				new Vector3(-1, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, 0),
				// Right face (X+)
				new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0),
				// Top face (Y+)
				new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0),
				// Bottom face (Y-)
				new Vector3(0, -1, 0), new Vector3(0, -1, 0), new Vector3(0, -1, 0), new Vector3(0, -1, 0)
			};
			
			model.Indices = new int[]
			{
				// Front face
				0, 1, 2, 2, 3, 0,
				// Back face
				4, 6, 5, 4, 7, 6,
				// Left face
				8, 10, 9, 8, 11, 10,
				// Right face
				12, 13, 14, 14, 15, 12,
				// Top face
				16, 17, 18, 18, 19, 16,
				// Bottom face
				20, 22, 21, 20, 23, 22
			};
		}
		else if(Block_Model.Type.Custom == type)
		{
			// Load custom model from datapath
			// TODO: Implement custom model loading later
		}
		return model;
	}


	// --- GDScript bridge (StructureBuilder.gd's palette) ---
	// Instance methods because GDScript can call methods on a C# autoload but can't reach
	// the static Blocks array. Air is excluded — the builder breaks blocks, it doesn't place 0.

	public Godot.Collections.Array<int> GetPlaceableBlockIds()
	{
		var ids = new Godot.Collections.Array<int>();
		for (int i = 1; i < Blocks.Length; i++)
			if (Blocks[i] != null) ids.Add(i);
		return ids;
	}

	public string GetBlockName(int id)
	{
		if (id <= 0 || id >= Blocks.Length || Blocks[id] == null) return "Air";
		return Blocks[id].Name;
	}

	public static Block_Model GetModel(string n)
	{
		Block_Model modelDef = null;
		foreach (var m in Models)
		{
			if (m == null) break;
			if (m.name == n)
			{
				modelDef = m;
				// Debug.WriteLine("Model found: " + modelDef.name + " Model vertices: " + modelDef.Vertices.Length);
				return modelDef;
			}
		}
		// Debug.WriteLine("Model not found: " + modelDef);
		return modelDef;
	}

	public static Vector2[][] GenerateFaceUVs(int blockIndex, int atlasWidth, int atlasHeight)
	{
		// Debug.WriteLine("ssss");
		int cols = atlasWidth / 3;
		int rows = atlasHeight / 2;

		int xCubeOffset = blockIndex % cols;
		int yCubeOffset = blockIndex / cols;

		Vector2[][] uvs = new Vector2[6][]; // 6 faces

		for (int face = 0; face < 6; face++)
		{
			int xOffset = face % 3;
			int yOffset = face / 3;

			float uStart = (xCubeOffset * 3 + xOffset) / (float)atlasWidth;
			float vStart = (yCubeOffset * 2 + yOffset + 1) / (float)atlasHeight;
			float uEnd = (xCubeOffset * 3 + xOffset + 1) / (float)atlasWidth;
			float vEnd = (yCubeOffset * 2 + yOffset) / (float)atlasHeight;

			uvs[face] = new Vector2[]
			{
				new Vector2(uStart, vStart),
				new Vector2(uEnd, vStart),
				new Vector2(uEnd, vEnd),
				new Vector2(uStart, vEnd)
			};
		}

		return uvs;
	}

	public static int GetBlockDropCount(int blockType)
	{
		if (blockType >= 0 && blockType < Blocks.Length)
		{
			if (Blocks[blockType] != null)
			{
				var block = Blocks[blockType];
				// Debug.WriteLine("Block Drop Count for Block Type " + blockType + " is " + block.DropCount);
				return block.DropCount;
			}
		}
		return 0;
	}

	public static string GetBlockDropID(int blockType)
	{
		if (blockType >= 0 && blockType < Blocks.Length)
		{
			if (Blocks[blockType] != null)
			{
				var block = Blocks[blockType];
				// Debug.WriteLine("Block Drop Count for Block Type " + blockType + " is " + block.DropCount);
				return block.DropId;
			}
		}
		return null;
	}

	public static Variant GetBlockStat(int blockType, string stat)
	{
		// if (blockType >= 0 && blockType < Blocks.Length && Blocks[blockType] != null)
		// {
		// 	var block = Blocks[blockType];
		// 	var property = typeof(Block_Definition).GetProperty(stat);
		// 	if (property != null)
		// 	{
		// 		Debug.WriteLine("Block Stat " + stat + " for Block Type " + blockType + " is " + property.GetValue(block));
		// 		return (Variant)property.GetValue(block);
		// 	}
		// }
		return default;
	}
	
}
