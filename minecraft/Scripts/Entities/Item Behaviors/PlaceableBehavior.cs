using Godot;
public partial class PlaceableBehavior : IItemBehavior
{
	public override void OnUse(string itemName, Player player)
	{
		// Placeable items don't have a generic "use" - they place blocks
	}

	public void Place(int blockType, Player player, Vector3I location)
	{
		// Place block logic - called from GDScript when placing
		Global.Instance.CubeManager.place_block(location, blockType);
		int selectedSlot = (int)player.Inventory.Get("selected_slot");
		player.Inventory.Call("remove_item", selectedSlot);
	}

	public void UseOnEntity(Node entity, Player player, string itemName)
	{
		// Handle using placeable item on an entity (e.g., feeding animals)
		GD.Print($"Used {itemName} on entity: {entity.Name}");
	}
}
