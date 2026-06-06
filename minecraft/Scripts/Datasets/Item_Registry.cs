// ARCHIVED — Minecraft item registry. Superseded by weapon/ability system.
// Stub class kept so the project.godot autoload entry doesn't crash on startup.
using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;

public partial class Item_Registry : Node { }

/*
public partial class Item_Registry : Node
{
	public static readonly int atlas_width = 12;
	public static readonly int atlas_height = 8;
	public static readonly Dictionary<string, Item_Definition> ItemData;
	static Item_Registry()
	{
		ItemData = new Dictionary<string, Item_Definition>
		{
			{ "hand", CreateItem(Item_Definition.ItemType.Tool, -1, 1, 0, null, new ToolBehavior(5.0f, -1)) },

			{ "grass", CreateItem(Item_Definition.ItemType.Placeable, 1, 64, 0, new Vector2(0, 5), new PlaceableBehavior()) },
			{ "dirt", CreateItem(Item_Definition.ItemType.Placeable, 2, 64, 1, new Vector2(6, 11), new PlaceableBehavior()) },
			{ "stone", CreateItem(Item_Definition.ItemType.Placeable, 3, 64, 2, new Vector2(12, 17), new PlaceableBehavior()) },

			{ "apple", CreateItem(Item_Definition.ItemType.Consumable, -1, 16, 12, null, new ConsumableBehavior()) },

			{ "wooden_sword", CreateItem(Item_Definition.ItemType.Tool, -1, 1, 24, null, new ToolBehavior(1.0f, 100)) },
			{ "wooden_pickaxe", CreateItem(Item_Definition.ItemType.Tool, -1, 1, 25, null, new ToolBehavior(2.0f, 100)) },
			{ "wooden_bow", CreateItem(Item_Definition.ItemType.Tool, -1, 1, 26, null, new ToolBehavior(1.0f, 100)) }
		};
	}

	public static Item_Definition CreateItem(Item_Definition.ItemType itemType, int block, int maxStack, int iconAtlasIndex, Vector2? modelAtlasIndex = null, IItemBehavior behavior = null)
	{
		return new Item_Definition
		{
			Type = itemType,
			Block = block,
			MaxStack = maxStack,
			IconAtlasIndex = iconAtlasIndex,
			ModelAtlasIndex = modelAtlasIndex ?? new Vector2(),
			EntityModel = null,
			HeldModel = null,
			Behavior = behavior
		};
	}

	public Variant GetItemStat(string itemType, string stat)
	{
		if (ItemData.TryGetValue(itemType, out var itemInfo))
		{
			return stat switch
			{
				"Block" => itemInfo.Block,
				"MaxStack" => itemInfo.MaxStack,
				"IconAtlasIndex" => itemInfo.IconAtlasIndex,
				"Type" => (int)itemInfo.Type,
				_ => default(Variant)
			};
		}
		return default(Variant);
	}

	public IItemBehavior GetItemBehavior(string itemType)
	{
		if (ItemData.TryGetValue(itemType, out var itemInfo))
			return itemInfo.Behavior;
		return GetItemBehavior("hand");
	}

	public bool IsPlaceable(string itemType)
	{
		if (ItemData.TryGetValue(itemType, out var itemInfo))
			return itemInfo.Type == Item_Definition.ItemType.Placeable;
		return false;
	}

	public static Block_Model FetchEntityModel(int iconAtlasIndex, bool isBlock)
	{
		if(isBlock) return Block_Registry.GetModel("Cube");
		return Generator(iconAtlasIndex);
	}

	public static Block_Model FetchHeldModel(int iconAtlasIndex, bool isBlock)
	{
		Block_Model model;
		if(isBlock)
		{
			model = Block_Registry.GetModel("Cube");
			model.heldScale = new Vector3(0.25f, 0.25f, 0.25f);
			model.heldOffset = new Vector3(0.5f, .2f, -.6f) - model.heldScale * 0.5f;
			model.heldRotation = new Vector3(0, 10.0f, 10.0f);
		}
		else
		{
			model = Generator(iconAtlasIndex);
			model.heldScale = new Vector3(0.5f, 0.5f, 0.5f);
			model.heldOffset = new Vector3(0.8f, 0, -.9f);
			model.heldRotation = new Vector3(10, -63.0f, 30.0f);
		}
		return model;
	}

	public static Block_Model Generator(int iconAtlasIndex)
	{
		Block_Model model = new Block_Model { name = "Item_" + iconAtlasIndex, type = Block_Model.Type.Custom, datapath = "" };
		float thickness = 0.0625f;
		model.Vertices = new Vector3[]
		{
			new Vector3(0, 0, .5f), new Vector3(1, 0, .5f), new Vector3(1, 1, .5f), new Vector3(0, 1, .5f),
		};
		model.Normals = new Vector3[]
		{
			new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1),
		};
		model.Indices = new int[] { 0, 1, 2, 2, 3, 0 };
		return model;
	}

	public static Vector2[] GenerateItemUVs(int iconIndex, int atlasWidth, int atlasHeight)
	{
		int xOffset = iconIndex % atlasWidth;
		int yOffset = iconIndex / atlasWidth;
		float uStart = xOffset / (float)atlasWidth;
		float uEnd = (xOffset + 1) / (float)atlasWidth;
		float vStart = yOffset / (float)atlasHeight;
		float vEnd = (yOffset + 1) / (float)atlasHeight;
		return new Vector2[]
		{
			new Vector2(uStart, vEnd), new Vector2(uEnd, vEnd),
			new Vector2(uEnd, vStart), new Vector2(uStart, vStart)
		};
	}

	public static Item SpawnItem(string itemType, Vector3 position, Node parent)
	{
		Item itemEntity;
		if (ItemData.TryGetValue(itemType, out var itemInfo))
		{
			itemEntity = new Item();
			itemEntity.Position = position;
			itemEntity.name = itemType;
			itemEntity.meshInstance = new MeshInstance3D();
			itemEntity.AddChild(itemEntity.meshInstance);
			Block_Model entityModel = itemInfo.EntityModel ?? (itemInfo.EntityModel = FetchEntityModel(itemInfo.IconAtlasIndex, itemInfo.Block != -1));
			var arrayMesh = new ArrayMesh();
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = entityModel.Vertices;
			arrays[(int)Mesh.ArrayType.Normal] = entityModel.Normals;
			var material = new StandardMaterial3D();
			material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
			material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
			if(itemInfo.Block == -1)
			{
				material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
				arrays[(int)Mesh.ArrayType.TexUV] = GenerateItemUVs(itemInfo.IconAtlasIndex, atlas_width, atlas_height);
				material.AlbedoTexture = ResourceLoader.Load<Texture2D>("res://Sprites/Textures/item_texture_atlas.png");
				itemEntity.Scale = new Vector3(0.5f, 0.5f, 0.5f);
				itemEntity.width = 0.5f;
				itemEntity.height = 0.5f;
			}
			else
			{
				material.CullMode = BaseMaterial3D.CullModeEnum.Back;
				arrays[(int)Mesh.ArrayType.TexUV] = Block_Registry.Blocks[itemInfo.Block].faceUVs.SelectMany(v => v).ToArray();
				material.AlbedoTexture = ResourceLoader.Load<Texture2D>("res://Sprites/Textures/block_texture_atlas.png");
				itemEntity.Scale = new Vector3(0.25f, 0.25f, 0.25f);
				itemEntity.width = 0.25f;
				itemEntity.height = 0.25f;
			}
			arrays[(int)Mesh.ArrayType.Index] = entityModel.Indices;
			arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			itemEntity.meshInstance.Mesh = arrayMesh;
			itemEntity.meshInstance.MaterialOverride = material;
			itemEntity.meshInstance.Position -= new Vector3(.5f, .5f, 0.5f);
			itemEntity.maxStack = itemInfo.MaxStack;
			parent.AddChild(itemEntity);
			return itemEntity;
		}
		return null;
	}

	public static bool ChangeMesh(MeshInstance3D meshInstance, string itemType)
	{
		if (meshInstance == null || !ItemData.TryGetValue(itemType, out var itemInfo)) return false;
		Block_Model held = itemInfo.HeldModel ?? (itemInfo.HeldModel = FetchHeldModel(itemInfo.IconAtlasIndex, itemInfo.Block != -1));
		var arrayMesh = new ArrayMesh();
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = held.Vertices;
		arrays[(int)Mesh.ArrayType.Normal] = held.Normals;
		arrays[(int)Mesh.ArrayType.Index] = held.Indices;
		var material = new StandardMaterial3D();
		material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
		material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
		if (itemInfo.Block == -1)
		{
			material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			arrays[(int)Mesh.ArrayType.TexUV] = GenerateItemUVs(itemInfo.IconAtlasIndex, atlas_width, atlas_height);
			material.AlbedoTexture = ResourceLoader.Load<Texture2D>("res://Sprites/Textures/item_texture_atlas.png");
		}
		else
		{
			material.CullMode = BaseMaterial3D.CullModeEnum.Back;
			arrays[(int)Mesh.ArrayType.TexUV] = Block_Registry.Blocks[itemInfo.Block].faceUVs.SelectMany(v => v).ToArray();
			material.AlbedoTexture = ResourceLoader.Load<Texture2D>("res://Sprites/Textures/block_texture_atlas.png");
		}
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		meshInstance.Mesh = arrayMesh;
		meshInstance.MaterialOverride = material;
		meshInstance.Position = held.heldOffset;
		meshInstance.RotationDegrees = held.heldRotation;
		meshInstance.Scale = held.heldScale;
		return true;
	}
}
*/
