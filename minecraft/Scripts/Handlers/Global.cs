using Godot;
using System;
using System.Collections.Generic;

public partial class Global : Node
{
	public static Global Instance { get; private set; }
	
	public Player Player { get; set; }
	
	[Export]
	public float SensitivityX { get; set; } = 0.3f;
	
	[Export]
	public float SensitivityY { get; set; } = 0.3f;
	
	[Export]
	public float MaxPitch { get; set; } = 90.0f;  // Limit the camera's up/down rotation
	
	[Export]
	public float MinPitch { get; set; } = -90.0f; // Limit the camera's up/down rotation

	public Vector3I WorldSpawn { get; set; } = new Vector3I(0, 5, 70);
	public const int SurfaceLevel = 0;
	public static readonly Vector2 AbyssCenter = new Vector2(0, 0); // x,z center
	public const float AbyssRadius = 120;
	
	// public static readonly Dictionary<int, float> LayerNoiseScale = new Dictionary<int, float>
	// {
	// 	{ 0, 0.02f },
	// 	{ 1, 0.04f },
	// 	{ 2, 0.07f },
	// 	{ 3, 0.1f },
	// 	{ 4, 0.16f }
	// };

	public float AirFriction { get; set; } = 0.91f;
	public float GroundFriction { get; set; } = 0.5f;
	public Chunk_Manager CubeManager { get; set; }
	public int AtlasWidth { get; set; } = 12;
	public int AtlasHeight { get; set; } = 8;
	private Vector3 _prevPos = Vector3.Zero;
	public Node3D[] Portals;

	public override void _Ready()
	{
		Instance = this;
	}

	public Vector3 GetPlayerPos()
	{
		if (Player == null)
		{
			GD.Print("NO PLAYER");
			return _prevPos;
		}
		_prevPos = Player.GlobalTransform.Origin;
		return _prevPos;
	}

	public Camera3D GetPlayerCamera()
	{
		return Player?.GetNode<Camera3D>("camera");
	}

	// public Variant GetBlockStat(string blockType, string stat)
	// {
	//     if (BlockData.ContainsKey(blockType))
	//     {
	//         var blockInfo = BlockData[blockType];
	//         return stat switch
	//         {
	//             "index" => blockInfo.Index,
	//             "hardness" => blockInfo.Hardness,
	//             "drops" => blockInfo.Drops,
	//             "drop_count" => blockInfo.DropCount,
	//             _ => default(Variant)
	//         };
	//     }
	//     return default(Variant);  // Return null or a default value
	// }

	// --------------------- the abyss ---------------------------

	public int AbyssLayer(float y)
	{
		if (y > SurfaceLevel)
			return 0; // surface rim
		else if (y > 9800)
			return 1; // upper abyss
		else if (y > 9600)
			return 2; // middle abyss
		else if (y > 9400)
			return 3; // lower abyss
		else
			return 4; // deep hell
	}

	public float AbyssStrength(float x, float z, float y)
	{
		var d = new Vector2(x, z).DistanceTo(AbyssCenterAtY(y));
		return Mathf.Clamp(1.0f - d / AbyssRadius, 0.0f, 1.0f);
	}

	public Vector2 AbyssCenterAtY(float y)
	{
		var t = (SurfaceLevel - y) * 0.02f;

		return new Vector2(
			AbyssCenter.X + Mathf.Sin(2.0f * t) * 120.0f,
			AbyssCenter.Y + Mathf.Cos(1.6f * t) * 80.0f
		);
	}
}
