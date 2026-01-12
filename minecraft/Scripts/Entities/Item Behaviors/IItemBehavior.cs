using Godot;
// IItemBehavior.cs
public class IItemBehavior
{
	public virtual void OnUse(Item item, Player player)
	{
		
	}
	public virtual void OnRelease(Item item, Player player)
	{
		
	}
	public virtual void OnHit(Item item, Node target)
	{
		
	}
	
}

// Simple default for items with no special behavior
public class NoOpBehavior : IItemBehavior
{
	// public void OnUse(Item item, Player player) { }
	// public void OnRelease(Item item, Player player) { }
	// public void OnHit(Item item, Node target) { }
}
