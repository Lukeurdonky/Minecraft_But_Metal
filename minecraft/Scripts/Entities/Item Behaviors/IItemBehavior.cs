using Godot;
// IItemBehavior.cs
public partial class IItemBehavior : RefCounted
{
	public float strength = 1.0f;
	public virtual void OnUse(string itemName, Player player)
	{
		
	}
	public virtual void OnRelease(string itemName, Player player)
	{
		
	}
	public virtual void OnHit(string itemName, Node target)
	{
		
	}

	public virtual void BreakBlock(string itemName, Player player, double delta)
	{
		
		// Default block breaking behavior - works for any item
		if (player.SelectedCube == 0 || player.SelectedCubePosition == null) return;
		Global.Instance.CubeManager.damage_block(player.SelectedCubePosition, strength * (float)delta);
		// var blockPosition = player.SelectedCubePosition;
		// var blockType = player.SelectedCube;
		
		// // Get drop count and add items to inventory
		// int dropCount = Block_Registry.GetBlockDropCount(blockType);
		// string dropId = Block_Registry.GetBlockDropID(blockType);
		
		// for (int i = 0; i < dropCount; i++)
		// {
		// 	player.Inventory.Call("add_item", dropId);
		// }
		
		// // Break the block in the world
		// Global.Instance.CubeManager.break_block(blockPosition);
		// player.SelectedCube = 0;
		// GD.Print("hit block");
	}
}

// // Simple default for items with no special behavior
// public partial class NoOpBehavior : IItemBehavior
// {
// 	// Inherits default implementations from IItemBehavior
// }
