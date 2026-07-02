using Godot;

public partial class EnemySpawner : Node
{
	[Export] public PackedScene[] EnemyScenes  { get; set; } = System.Array.Empty<PackedScene>();
	[Export] public float[]       SpawnWeights { get; set; } = System.Array.Empty<float>();

	[Export] public int   MaxEnemies     = 50;
	[Export] public float SpawnInterval  = .1f;
	[Export] public float SpawnRadiusMin = 20f;
	[Export] public float SpawnRadiusMax = 120f;
	[Export] public int   ScanAttempts   = 20;

	private float _timer       = 0f;
	private float _totalWeight = 0f;

	public override void _Ready()
	{
		_totalWeight = 0f;
		if (SpawnWeights != null)
			foreach (var w in SpawnWeights)
				_totalWeight += w;
	}

	public override void _Process(double delta)
	{
		if (EnemyScenes == null || EnemyScenes.Length == 0
			|| Global.Instance?.CubeManager == null || Global.Instance?.Player == null)
			return;

		_timer += (float)delta;
		if (_timer < SpawnInterval) return;
		_timer = 0f;

		if (Global.Instance.EnemyCount >= MaxEnemies) return;

		TrySpawn();
	}

	private PackedScene PickScene()
	{
		if (EnemyScenes.Length == 1 || _totalWeight <= 0f)
			return EnemyScenes[0];

		float roll = GD.Randf() * _totalWeight;
		float acc  = 0f;
		for (int i = 0; i < EnemyScenes.Length; i++)
		{
			float w = (SpawnWeights != null && i < SpawnWeights.Length) ? SpawnWeights[i] : 1f;
			acc += w;
			if (roll <= acc) return EnemyScenes[i];
		}
		return EnemyScenes[EnemyScenes.Length - 1];
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

			var cm     = Global.Instance.CubeManager;
			int yStart = Mathf.RoundToInt(playerPos.Y) + 10;
			int yEnd   = Mathf.RoundToInt(playerPos.Y) - 50;

			for (int y = yStart; y > yEnd; y--)
			{
				var floor = new Vector3I(bx, y, bz);
				var head1 = floor + Vector3I.Up;
				var head2 = floor + Vector3I.Up * 2;

				if (cm.get_block(floor) != 0 && cm.get_block(head1) == 0 && cm.get_block(head2) == 0)
				{
					var scene = PickScene();
					if (scene == null) return;
					var enemy = scene.Instantiate<Node3D>();
					GetTree().CurrentScene.AddChild(enemy);
					enemy.GlobalPosition = new Vector3(bx + 0.5f, y + 2f, bz + 0.5f);
					return;
				}
			}
		}
	}
}
