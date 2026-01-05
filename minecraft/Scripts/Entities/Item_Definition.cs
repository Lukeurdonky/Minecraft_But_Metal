using Godot;
using System;
using System.Diagnostics;
public sealed class Item_Definition
{
    public enum ItemType
    {
        Placeable,
        Consumable,
        Tool,
        Miscellaneous
    }
    public ItemType Type { get; set; }
    public int Block { get; set; }
    public int MaxStack { get; set; }
    public string Texture { get; set; }
}