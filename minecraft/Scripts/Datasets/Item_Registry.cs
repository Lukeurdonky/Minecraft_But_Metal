using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;

public partial class Item_Registry : Node
{
    public static readonly Dictionary<string, Item_Definition> ItemData;
    static Item_Registry()
    {
        ItemData = new Dictionary<string, Item_Definition>
        {
            { "grass", new Item_Definition { Type = Item_Definition.ItemType.Placeable, Block = 1, MaxStack = 64, Texture = "res://sprites/textures/grass.png" } },
            { "dirt", new Item_Definition { Type = Item_Definition.ItemType.Placeable, Block = 2, MaxStack = 64, Texture = "res://sprites/textures/dirt.png" } },
            { "stone", new Item_Definition { Type = Item_Definition.ItemType.Placeable, Block = 3, MaxStack = 64, Texture = "res://sprites/textures/stone.png" } }
        };
    }
    
    public Variant GetItemStat(string itemType, string stat)
    {
        if (Item_Registry.ItemData.TryGetValue(itemType, out var itemInfo))
        {
            return stat switch
            {
                "block" => itemInfo.Block,
                "max_stack" => itemInfo.MaxStack,
                "texture" => itemInfo.Texture,
                _ => default(Variant)
            };
        }
        return default(Variant);  // Return null or a default value
    }

}