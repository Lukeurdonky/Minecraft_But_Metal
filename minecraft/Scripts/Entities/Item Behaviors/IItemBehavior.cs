// IItemBehavior.cs
public interface IItemBehavior
{
    void OnUse(Item item, Player player);
    void OnRelease(Item item, Player player);
    void OnHit(Item item, Node target);
}

// Simple default for items with no special behavior
public class NoOpBehavior : IItemBehavior
{
    public void OnUse(Item item, Player player) { }
    public void OnRelease(Item item, Player player) { }
    public void OnHit(Item item, Node target) { }
}