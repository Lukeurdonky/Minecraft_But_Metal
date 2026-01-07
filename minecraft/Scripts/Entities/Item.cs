using Godot;
using System;
using System.Collections.Generic;

public partial class Item : Entity
{
    public string name;
    // public Item_Definition Data { get; set; }
    [Export]
    public MeshInstance3D meshInstance;

    public override void ImHere()
    {
        base.ImHere();

        // Additional initialization for Item if needed

    }
}