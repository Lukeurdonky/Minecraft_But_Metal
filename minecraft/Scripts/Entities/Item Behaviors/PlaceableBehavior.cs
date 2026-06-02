// ARCHIVED — Minecraft block placement behavior. Superseded by weapon/ability system.
using Godot;

/*
public partial class PlaceableBehavior : IItemBehavior
{
	public override void OnUse(string itemName, Player player) { }

	public void Place(int blockType, Player player, Vector3I location)
	{
		Global.Instance.CubeManager.place_block(location, blockType);
		int selectedSlot = (int)player.Inventory.Get("selected_slot");
		player.Inventory.Call("remove_item", selectedSlot);
	}

	public void UseOnEntity(Node entity, Player player, string itemName)
	{
		GD.Print($"Used {itemName} on entity: {entity.Name}");
	}
}
*/
