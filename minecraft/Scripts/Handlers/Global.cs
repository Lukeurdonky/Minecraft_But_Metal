using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public partial class Global : Node
{
	public static Global Instance { get; private set; }
	
	public Player Player      { get; set; }
	public int    EnemyCount  { get; set; } = 0;
	
	[Export]
	public float SensitivityX { get; set; } = 0.3f;
	
	[Export]
	public float SensitivityY { get; set; } = 0.3f;
	
	[Export]
	public float MaxPitch { get; set; } = 90.0f;  // Limit the camera's up/down rotation
	
	[Export]
	public float MinPitch { get; set; } = -90.0f; // Limit the camera's up/down rotation

	public Vector3I WorldSpawn { get; set; } = new Vector3I(512, 20, 512);
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

	public float AirFriction { get; set; } = 0.97f;
	public float GroundFriction { get; set; } = .91f;
	public Chunk_Manager CubeManager { get; set; }
	public int AtlasWidth { get; set; } = 12;
	public int AtlasHeight { get; set; } = 8;
	private Vector3 _prevPos = Vector3.Zero;
	public Node3D[] Portals;

	// ── Hitstop ──────────────────────────────────────────────────────────────
	private float _hitstopTimer = 0f;

	public bool HitstopActive => _hitstopTimer > 0f;

	public void TriggerHitstop(float duration)
	{
		_hitstopTimer = Mathf.Max(_hitstopTimer, duration);
	}

	// ── Camera shake ─────────────────────────────────────────────────────────
	private float _shakePeak     = 0f;
	private float _shakeDuration = 0f;
	private float _shakeTimer    = 0f;

	public float CurrentShake => _shakeDuration > 0f
		? _shakePeak * Mathf.Clamp(_shakeTimer / _shakeDuration, 0f, 1f)
		: 0f;

	public void ShakeCamera(float intensity, float duration)
	{
		if (intensity > _shakePeak || _shakeTimer <= 0f)
			_shakePeak = intensity;
		_shakeDuration = duration;
		_shakeTimer    = Mathf.Max(_shakeTimer, duration);
	}

	public override void _Ready()
	{
		Instance = this;
	}

	public override void _Process(double delta)
	{
		if (_hitstopTimer > 0f)
			_hitstopTimer = Mathf.Max(_hitstopTimer - (float)delta, 0f);
		if (_shakeTimer > 0f)
			_shakeTimer = Mathf.Max(_shakeTimer - (float)delta, 0f);
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

	// --------------------- planet wrapping ---------------------------

	// Planet size in chunks. Clamped at startup by Chunk_Manager to satisfy
	// PlanetChunksX > RenderDistance * 2 (one-node guarantee).
	public static int PlanetChunksX = 64;
	public static int PlanetChunksZ = 64;

	public static int PlanetWidth => PlanetChunksX * 16;
	public static int PlanetDepth => PlanetChunksZ * 16;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int CanonicalChunkX(int cx) => ((cx % PlanetChunksX) + PlanetChunksX) % PlanetChunksX;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int CanonicalChunkZ(int cz) => ((cz % PlanetChunksZ) + PlanetChunksZ) % PlanetChunksZ;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int CanonicalBlockX(int bx) => ((bx % PlanetWidth)   + PlanetWidth)   % PlanetWidth;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int CanonicalBlockZ(int bz) => ((bz % PlanetDepth)   + PlanetDepth)   % PlanetDepth;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector3I CanonicalChunkPos(Vector3I cp) =>
		new Vector3I(CanonicalChunkX(cp.X), cp.Y, CanonicalChunkZ(cp.Z));

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
