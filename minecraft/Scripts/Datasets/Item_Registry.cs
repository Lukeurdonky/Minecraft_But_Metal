using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;

public partial class Item_Registry : Node
{
	public static readonly int atlas_width = 12;
	public static readonly int atlas_height = 8;
	public static readonly Dictionary<string, Item_Definition> ItemData;
	static Item_Registry()
	{
		ItemData = new Dictionary<string, Item_Definition>
		{
			{ "grass", CreateItem(Item_Definition.ItemType.Placeable, 1, 64, 0, new Vector2(0, 5)) },
			{ "dirt", CreateItem(Item_Definition.ItemType.Placeable, 2, 64, 1, new Vector2(6, 11)) },
			{ "stone", CreateItem(Item_Definition.ItemType.Placeable, 3, 64, 2, new Vector2(12, 17)) },

			{ "apple", CreateItem(Item_Definition.ItemType.Consumable, -1, 16, 12, null) },

			{ "wooden_sword", CreateItem(Item_Definition.ItemType.Tool, -1, 1, 24, null) }
		};
	}
	
	public static Item_Definition CreateItem(Item_Definition.ItemType itemType, int block, int maxStack, int iconAtlasIndex, Vector2? modelAtlasIndex = null)
	{
		return new Item_Definition
		{
			Type = itemType,
			Block = block,
			MaxStack = maxStack,
			// Icon = icon,
			IconAtlasIndex = iconAtlasIndex,
			ModelAtlasIndex = modelAtlasIndex ?? new Vector2(),
			// Models are generated lazily when first needed, not during static initialization
			EntityModel = null,
			HeldModel = null
		};
	}

	public Variant GetItemStat(string itemType, string stat)
	{
		if (Item_Registry.ItemData.TryGetValue(itemType, out var itemInfo))
		{
			return stat switch
			{
				"Block" => itemInfo.Block,
				"MaxStack" => itemInfo.MaxStack,
				"IconAtlasIndex" => itemInfo.IconAtlasIndex,
				_ => default(Variant)
			};
		}
		return default(Variant);  // Return null or a default value
	}

	public static Block_Model FetchEntityModel(int iconAtlasIndex, bool isBlock) // if a block, fetch from block registry, if not use generator
	{
		Block_Model model;
		if(isBlock)
		{
			// Fetch from Block Registry logic here
			model = Block_Registry.GetModel("Cube");
		}
		else
		{
			model = Generator(iconAtlasIndex);
		}
		return model;
	}

	public static Block_Model FetchHeldModel(int iconAtlasIndex, bool isBlock)  // if a block, fetch from block registry, if not use generator, and apply transformations
	{
		Block_Model model;
		if(isBlock)
		{
			// Fetch from Block Registry logic here
			model = Block_Registry.GetModel("Cube");


			//rotate and offset for held model
		}
		else
		{
			model = Generator(iconAtlasIndex);

			//rotate and offset for held model
		}
		return model;
	}

	public static Block_Model Generator(int iconAtlasIndex) // generate a 1 pixel thick model for non-block items
	{
		Block_Model model = new Block_Model 
		{ 
			name = "Item_" + iconAtlasIndex, 
			type = Block_Model.Type.Custom, 
			datapath = "" 
		};

		float thickness = 0.0625f; // 1/16th of a block (1 pixel thick in Minecraft scale)
		float halfThickness = thickness / 2f;

		// Create a flat billboard with front and back faces
		model.Vertices = new Vector3[]
		{
			// Front face (Z-)
			new Vector3(-.5f, -.5f, .5f),      // Bottom-left
			new Vector3(.5f, -.5f, .5f),      // Bottom-right
			new Vector3(.5f, .5f, .5f),      // Top-right
			new Vector3(-.5f, .5f, .5f),      // Top-left
			
			// // Back face (Z+)
			// new Vector3(0, 0, 0.5f + halfThickness),      // Bottom-left
			// new Vector3(1, 0, 0.5f + halfThickness),      // Bottom-right
			// new Vector3(1, 1, 0.5f + halfThickness),      // Top-right
			// new Vector3(0, 1, 0.5f + halfThickness)       // Top-left
		};

		model.Normals = new Vector3[]
		{
			// Front face normals
			new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1),
			// Back face normals
			// new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1)
		};

		model.Indices = new int[]
		{
			// Front face
			0, 1, 2, 2, 3, 0,
			// Back face
			// 4, 6, 5, 4, 7, 6
		};

		return model;
	}

	public static Vector2[] GenerateItemUVs(int iconIndex, int atlasWidth, int atlasHeight)
	{
		// Calculate position in atlas grid (assuming single-cell icons)
		int xOffset = iconIndex % atlasWidth;
		int yOffset = iconIndex / atlasWidth;
		
		// Calculate UV coordinates for the icon
		// In UV space: U goes 0(left) to 1(right), V goes 0(top) to 1(bottom)
		float uStart = xOffset / (float)atlasWidth;
		float uEnd = (xOffset + 1) / (float)atlasWidth;
		float vStart = yOffset / (float)atlasHeight;
		float vEnd = (yOffset + 1) / (float)atlasHeight;
		
		// Return UVs for single face (4 total)
		// Vertices are ordered: bottom-left, bottom-right, top-right, top-left
		return new Vector2[]
		{
			// Front face only
			new Vector2(uStart, vEnd),    // Bottom-left (V=bottom)
			new Vector2(uEnd, vEnd),      // Bottom-right
			new Vector2(uEnd, vStart),    // Top-right (V=top)
			new Vector2(uStart, vStart)   // Top-left
		};
	}

	public static Item SpawnItem(string itemType, Vector3 position, Node parent)
	{
		// Logic to spawn the item in the game world at the specified position
		Item itemEntity;
		if (ItemData.TryGetValue(itemType, out var itemInfo))
		{
			// Create and configure the item entity based on itemInfo
			itemEntity = new Item();
			itemEntity.Position = position;
			itemEntity.name = itemType;
			itemEntity.meshInstance = new MeshInstance3D();
			itemEntity.AddChild(itemEntity.meshInstance);

			// Generate model on-demand (lazy initialization)
			Block_Model entityModel = FetchEntityModel(itemInfo.IconAtlasIndex, itemInfo.Block != -1);

			var arrayMesh = new ArrayMesh();
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			
			arrays[(int)Mesh.ArrayType.Vertex] = entityModel.Vertices;
			arrays[(int)Mesh.ArrayType.Normal] = entityModel.Normals;

			var material = new StandardMaterial3D();
			material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
			material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;

			if(itemInfo.Block == -1) 
			{
				arrays[(int)Mesh.ArrayType.TexUV] = GenerateItemUVs(itemInfo.IconAtlasIndex, atlas_width, atlas_height);
				material.AlbedoTexture = ResourceLoader.Load<Texture2D>("res://Sprites/Textures/item_texture_atlas.png");
			}
			else 
			{
				arrays[(int)Mesh.ArrayType.TexUV] = Block_Registry.GenerateFaceUVs((int)itemInfo.ModelAtlasIndex.X, Block_Registry.atlas_width, Block_Registry.atlas_height).SelectMany(v => v).ToArray();
				material.AlbedoTexture = ResourceLoader.Load<Texture2D>("res://Sprites/Textures/block_texture_atlas.png");
			}
			arrays[(int)Mesh.ArrayType.Index] = entityModel.Indices;
			
			arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			itemEntity.meshInstance.Mesh = arrayMesh;
			itemEntity.meshInstance.MaterialOverride = material;

			// Additional setup based on itemInfo can be done here
			parent.AddChild(itemEntity);

			return itemEntity;
		}
		return null; // Item type not found
	}
}
