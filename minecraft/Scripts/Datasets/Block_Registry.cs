using Godot;
using System;
using System.Diagnostics;
using System.Linq;

public partial class Block_Registry : Node
{
	static readonly int atlas_width = 12;
	static readonly int atlas_height = 8;
	public static readonly Block_Definition[] Blocks;
	public static readonly Block_Model[] Models;

	static Block_Registry()
	{
		
		
		Models = new Block_Model[8];
		Models[0] = CreateBlockModel("Cube", Block_Model.Type.Cube, ""); // Cube
		//Models[1] = CreateBlockModel("Stair", Block_Model.Type.Stair, ""); // Stair
		//Models[2] = CreateBlockModel("Slab", Block_Model.Type.Slab, ""); // Slab
		Models[3] = CreateBlockModel("Piano", Block_Model.Type.Custom, ""); // Piano
		
		Blocks = new Block_Definition[256];
		Blocks[0] = null; // Air
		Blocks[1] = new Block_Definition { Id = 1, Name = "Grass", Hardness = 1f, DropId = "grass", DropCount = 2, faceUVs = GenerateFaceUVs(0, atlas_width, atlas_height), Model = GetModel("Cube") }; // grass
		Blocks[2] = new Block_Definition { Id = 2, Name = "Dirt", Hardness = 2f, DropId = "dirt", DropCount = 1, faceUVs = GenerateFaceUVs(1, atlas_width, atlas_height), Model = GetModel("Cube") }; // dirt
		Blocks[3] = new Block_Definition { Id = 3, Name = "Stone", Hardness = 5f, DropId = "stone", DropCount = 5, faceUVs = GenerateFaceUVs(2, atlas_width, atlas_height), Model = GetModel("Cube") }; // stone
	

	}

	private static Block_Model CreateBlockModel(string name, Block_Model.Type type, string datapath)
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

	private static Vector2[][] GenerateFaceUVs(int blockIndex, int atlasWidth, int atlasHeight)
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
