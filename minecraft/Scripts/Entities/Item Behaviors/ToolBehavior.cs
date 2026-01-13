using Godot;
public partial class ToolBehavior : IItemBehavior
{
    public int durability = 100;
    
    // Parameterless constructor required by Godot
    public ToolBehavior()
    {
    }
    
    public ToolBehavior(float str, int dur)
    {
        strength = str;
        durability = dur;
    }
	public override void OnUse(string itemName, Player player)
	{
		
	}
	public override void OnRelease(string itemName, Player player)
	{
		
	}
	public override void OnHit(string itemName, Node target)
	{
		
	}
}
