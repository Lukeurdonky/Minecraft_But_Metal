using Godot;

public partial class EnemySpawner : Node
{
    [Export] public PackedScene CreatureScene { get; set; }

    private const int   MaxEnemies     = 5;
    private const float SpawnInterval  = 2f;
    private const float SpawnRadiusMin = 20f;
    private const float SpawnRadiusMax = 120f;
    private const int   ScanAttempts   = 20;

    private float _timer = 0f;

    public override void _Process(double delta)
    {
        if (CreatureScene == null || Global.Instance?.CubeManager == null || Global.Instance?.Player == null)
            return;

        _timer += (float)delta;
        if (_timer < SpawnInterval) return;
        _timer = 0f;

        if (Global.Instance.EnemyCount >= MaxEnemies) return;

        TrySpawn();
    }

    private void TrySpawn()
    {
        var playerPos = Global.Instance.Player.GlobalPosition;

        for (int i = 0; i < ScanAttempts; i++)
        {
            float angle = GD.Randf() * Mathf.Tau;
            float dist  = (float)GD.RandRange(SpawnRadiusMin, SpawnRadiusMax);
            int   bx    = Mathf.RoundToInt(playerPos.X + Mathf.Cos(angle) * dist);
            int   bz    = Mathf.RoundToInt(playerPos.Z + Mathf.Sin(angle) * dist);

            var cm = Global.Instance.CubeManager;
            int yStart = Mathf.RoundToInt(playerPos.Y) + 10;
            int yEnd   = Mathf.RoundToInt(playerPos.Y) - 50;

            for (int y = yStart; y > yEnd; y--)
            {
                var floor = new Vector3I(bx, y, bz);
                var head1 = floor + Vector3I.Up;
                var head2 = floor + Vector3I.Up * 2;

                if (cm.get_block(floor) != 0 && cm.get_block(head1) == 0 && cm.get_block(head2) == 0)
                {
                    var spawnPos = new Vector3(bx + 0.5f, y + 2f, bz + 0.5f);
                    var creature = CreatureScene.Instantiate<Node3D>();
                    GetTree().CurrentScene.AddChild(creature);
                    creature.GlobalPosition = spawnPos;
                    return;
                }
            }
        }
    }
}
