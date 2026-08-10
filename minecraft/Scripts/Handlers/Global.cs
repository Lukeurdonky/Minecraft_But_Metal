using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public partial class Global : Node
{
	// Single source of truth for chunk dimensions — every other file referencing chunk
	// size should derive from this, not hardcode 16.
	public const int CHUNK_SIZE = 48;

	public static Global Instance { get; private set; }

	private Player _player;

	// Reads as null once the Player has been freed, which a scene change does without anyone
	// clearing this. In C# the wrapper outlives the native node as a NON-null reference to a
	// disposed object, so `Player != null` passes and the very next member access throws
	// ObjectDisposedException. Every consumer already guards with `!= null`; validating here
	// makes that guard mean what it looks like it means, at the one place they all go through.
	//
	// This bites specifically when two scenes with a Chunk_Manager run back-to-back (ship ->
	// planet -> ship): chunk streaming reads GetPlayerPos() on the new scene's first frames,
	// while this still points at the previous scene's corpse.
	public Player Player
	{
		get
		{
			if (_player != null && !IsInstanceValid(_player)) _player = null;
			return _player;
		}
		set => _player = value;
	}
	public int    EnemyCount  { get; set; } = 0;
	public int    KillCount   { get; set; } = 0;
	public float  RunTimer    { get; set; } = 0f;

	public void IncrementKills() => KillCount++;

	// Active planet configuration — persists across scene reloads.
	public PlanetParams ActivePlanet { get; set; } = PlanetParams.MakeField();

	// Names of currently-equipped accessories (Accessory_Registry keys) — persists across
	// scene reloads so Player.EquipStartingAccessories() can re-attach them on load.
	// Not touched by ApplyPlanetParams (must survive planet-to-planet transitions within
	// a run).
	public List<string> EquippedAccessoryIds { get; set; } = new();

	// GDScript can only call methods on autoloads, not read plain C# properties — these
	// wrappers are the bridge point for the F3 debug menu and the upgrade-pick screen.
	public Godot.Collections.Array<string> GetAllAccessoryNames()
	{
		var arr = new Godot.Collections.Array<string>();
		foreach (var d in Accessory_Registry.All) arr.Add(d.Name);
		return arr;
	}

	public bool IsAccessoryEquipped(string name) => EquippedAccessoryIds.Contains(name);

	public void SetAccessoryEquipped(string name, bool equipped)
	{
		if (equipped)
		{
			if (!EquippedAccessoryIds.Contains(name)) EquippedAccessoryIds.Add(name);
			Player?.EquipAccessory(name);
		}
		else
		{
			EquippedAccessoryIds.Remove(name);
			Player?.UnequipAccessory(name);
		}
	}

	// Called from PlanetConfigMenu (GDScript) before reloading the scene.
	public void SetPlanetConfig(Godot.Collections.Dictionary config)
	{
		var p = new PlanetParams();
		if (config.ContainsKey("biome"))           p.Biome           = config["biome"].AsString();
		if (config.ContainsKey("template"))        p.Template        = config["template"].AsString();
		if (config.ContainsKey("void_world"))        p.VoidWorld       = config["void_world"].AsBool();
		if (config.ContainsKey("fill_solid"))       p.FillSolid       = config["fill_solid"].AsBool();
		if (config.ContainsKey("surface_block"))    p.SurfaceBlock    = (byte)config["surface_block"].AsInt32();
		if (config.ContainsKey("noise_scale"))      p.NoiseScale      = config["noise_scale"].AsSingle();
		if (config.ContainsKey("height_amp"))       p.HeightAmplitude = config["height_amp"].AsSingle();
		if (config.ContainsKey("spawn_y"))          p.SpawnY          = config["spawn_y"].AsInt32();
		if (config.ContainsKey("caves_enabled"))    p.CavesEnabled    = config["caves_enabled"].AsBool();
		if (config.ContainsKey("cave_full_range"))  p.CaveFullRange   = config["cave_full_range"].AsBool();
		if (config.ContainsKey("cave_scale"))       p.CaveScale       = config["cave_scale"].AsSingle();
		if (config.ContainsKey("cave_y_freq"))      p.CaveYFrequency  = config["cave_y_freq"].AsSingle();
		if (config.ContainsKey("cave_threshold"))   p.CaveThreshold   = config["cave_threshold"].AsSingle();
		if (config.ContainsKey("chasm_enabled"))       p.ChasmEnabled       = config["chasm_enabled"].AsBool();
		if (config.ContainsKey("chasm_radius"))        p.ChasmRadius        = config["chasm_radius"].AsSingle();
		if (config.ContainsKey("chasm_drift"))         p.ChasmDriftScale    = config["chasm_drift"].AsSingle();
		if (config.ContainsKey("spawn_clear_enabled")) p.SpawnClearEnabled  = config["spawn_clear_enabled"].AsBool();
		if (config.ContainsKey("planet_chunks")) {
			int sz = config["planet_chunks"].AsInt32();
			PlanetChunksX = sz;
			PlanetChunksZ = sz;
		}
		ApplyPlanetParams(p);
	}

	// Shared tail of SetPlanetConfig — also called directly by RunManager,
	// which already has a fully-built PlanetParams and skips the dictionary round-trip.
	public void ApplyPlanetParams(PlanetParams p)
	{
		ActivePlanet = p;
		WorldSpawn   = new Vector3I(WorldSpawn.X, p.SpawnY, WorldSpawn.Z);
		KillCount    = 0;
		RunTimer     = 0f;
	}
	
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
	// Particle freeze is global and automatic. Every GpuParticles3D entering the tree is
	// auto-registered into HitstopParticleGroup by _OnNodeAdded, and the sweeps below run
	// only on the hitstop start/end edges — never per frame. Any future particle effect
	// participates with zero wiring; do not add per-effect hitstop handling.
	//
	// Note this uses SpeedScale, not Emitting: Emitting=false only stops NEW particles
	// spawning, leaving everything already in flight moving through the freeze. SpeedScale=0
	// zeroes the delta fed to the particle process shader, which is what actually stops them.
	private const string HitstopParticleGroup = "hitstop_particles";
	private const string BaseSpeedMeta        = "hitstop_base_speed";

	private float _hitstopTimer = 0f;

	public bool HitstopActive => _hitstopTimer > 0f;

	public void TriggerHitstop(float duration)
	{
		if (duration <= 0f) return;
		bool rising    = _hitstopTimer <= 0f; // re-triggering mid-stop only extends it
		_hitstopTimer  = Mathf.Max(_hitstopTimer, duration);
		if (rising) SetParticlesFrozen(true);
	}

	private void _OnNodeAdded(Node node)
	{
		if (node is not GpuParticles3D p) return;

		// HasMeta guard: a reparented node fires NodeAdded again, and re-reading SpeedScale
		// while frozen would latch base_speed to 0 and freeze the effect permanently.
		if (!p.HasMeta(BaseSpeedMeta)) p.SetMeta(BaseSpeedMeta, p.SpeedScale);
		p.AddToGroup(HitstopParticleGroup);

		// Effects spawned mid-freeze must start frozen — a jackhammer impact triggers its own
		// hitstop before spawning its explosion, so otherwise it plays out during its own stop.
		if (HitstopActive) p.SpeedScale = 0f;
	}

	private void SetParticlesFrozen(bool frozen)
	{
		foreach (var node in GetTree().GetNodesInGroup(HitstopParticleGroup))
		{
			if (node is not GpuParticles3D p) continue;
			// Restore the authored value — a blanket reset to 1 would clobber any effect
			// deliberately authored at a different speed scale.
			p.SpeedScale = frozen ? 0f : (float)p.GetMeta(BaseSpeedMeta, 1f);
		}
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
		// Autoloads enter the tree before the main scene, so this catches every particle
		// node the game ever creates — scene-authored or instantiated from code.
		GetTree().NodeAdded += _OnNodeAdded;
		RegisterCurveGlobals();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (_hitstopTimer > 0f)
		{
			_hitstopTimer = Mathf.Max(_hitstopTimer - dt, 0f);
			if (_hitstopTimer <= 0f) SetParticlesFrozen(false);
		}
		if (_shakeTimer   > 0f) _shakeTimer   = Mathf.Max(_shakeTimer   - dt, 0f);
		if (Player != null)     RunTimer      += dt;
		UpdateCurveOrigin();
	}

	// --------------------- planet curvature (render-only) ---------------------------
	//
	// See Materials/WorldCurve.gdshaderinc for what this does and why it's safe. These
	// two values are GLOBAL shader parameters, so any material that wants to bend with
	// the world opts in by including that file — no per-material plumbing, and no
	// exported field to forget to wire on a new scene.
	//
	// Registered at runtime rather than in project.godot's [shader_globals] because the
	// live editor re-serializes that section from its own stale in-memory copy the next
	// time anything touches project settings, which silently reverts a hand-written
	// entry. Adding them here is also idempotent-checked, so it survives a reload.

	public const string CurveStrengthParam   = "world_curve_strength";
	public const string CurveFlatRadiusParam = "world_curve_flat_radius";
	public const string CurveOriginParam     = "world_curve_origin";

	// CURVATURE IS A PROPERTY OF THE PLANET, DERIVED FROM WORLD SIZE.
	//
	// Not from RenderDistance. Render distance is a viewer preference — someone on a
	// weaker machine turns it down — and deriving the world's shape from it would mean
	// two players standing on the same planet see two differently-shaped worlds. Same
	// principle as the danger scale: it belongs to the thing, not to the screen showing it.
	//
	// World size is the physically correct source anyway. Treat the wrap distance as a
	// planet's circumference; then radius R = width / 2*PI, and the drop of a sphere's
	// surface at horizontal distance d is the standard d^2 / 2R — which rearranges to
	//
	//     strength = PI / world_width
	//
	// so a smaller planet curves harder, for free and for the right reason. That is
	// exactly the illusion a small world needs, and it now costs nothing to maintain:
	// change the planet size and the horizon tightens by itself.
	//
	// PARKED AT 0 ON PURPOSE (2026-08-10). This is not a placeholder to fill in — the
	// whole curvature system is deliberately switched off, and the reason is worth
	// keeping so nobody "fixes" it by turning it up.
	//
	// It worked, and it looked like a planet. What killed it is that curvature compresses
	// APPARENT DISTANCE past the point where the bend starts: on a 1536-block world a
	// target 450 blocks out was drawn at the screen position a target 82 blocks out would
	// occupy, and one at the render edge read as ~15. That is what a horizon IS, so it
	// isn't a bug and can't be tuned away — but this game's core loop is a continuous
	// judgement of "can I reach that?" with a 220-unit grapple. Making that judgement
	// unreliable attacks the primary verb. Confirmed empirically: raising GrappleRange to
	// 620 made things that looked ~80 blocks away suddenly grabbable.
	//
	// The secondary cost was breadth. Nothing renders through one chokepoint, so every
	// future visual — boss VFX, THE PLANT, the warpstation — would have to remember to
	// opt into the bend or visibly detach from the ground.
	//
	// Kept rather than deleted because it is genuinely good for anything where distance
	// judgement doesn't matter: the ship hub backdrop, SolarSelect art, or the
	// crashlanding entry sequence. Set this above 0 (or drag the F3 slider) and the whole
	// system comes back live — no other wiring needed.
	public const float DefaultCurveExaggeration = 0f;

	// The flat zone, as a fraction of the wrap width. Inside this radius the displacement
	// is exactly zero, so aiming and every other cross-distance operation is exact there.
	//
	// Expressed against WORLD SIZE rather than as a fixed block count on purpose. A fixed
	// 300 (to clear LaserRange) would be larger than the entire render edge of a small
	// planet — a 624-block world at RD 6 only draws 288 blocks — and would silently flatten
	// small planets completely, which is exactly where the curve matters most.
	//
	// At 0.25 a 1536-block world flattens out to 384, clearing both LaserRange (300) and
	// GrappleRange (220) entirely — aiming is exact everywhere you can reach.
	//
	// SMALL WORLDS ARE NOT COVERED BY THIS DEFAULT. Both terms scale with world size and
	// they compound the wrong way: a smaller planet gets a steeper curve (strength is
	// PI/width) AND a smaller safe radius. Measured on a 624-block world at exaggeration
	// 1.0: flat to 156, and a target at grapple range is still drawn ~21 blocks below its
	// hitbox. Raising the fraction to ~0.35 puts 218 blocks inside the safe zone and takes
	// that back to nothing.
	//
	// The underlying tension is a design one, not a rendering one: GrappleRange 220 is 35%
	// of a 624-block world. Abilities that reach a third of the way across the planet will
	// always fight a curve meant to hide the far side of it.
	public const float DefaultCurveFlatFraction = 0.25f;

	// Fallback wrap width when PlanetChunks hasn't been set yet (menus, first frames).
	private const float FallbackWorldWidth = 1536f;

	// Guarded by a plain static rather than by checking GlobalShaderParameterGetList():
	// that getter is editor-only and warns "should never be used outside the editor, it
	// can severely damage performance" on every call in a running game. A static is
	// enough — Global is an autoload, so this runs once per process, and the statics
	// reset with it.
	private static bool _curveGlobalsRegistered;
	private static float _curveExaggeration = DefaultCurveExaggeration;
	private static float _curveFlatFraction = DefaultCurveFlatFraction;

	private static void RegisterCurveGlobals()
	{
		if (_curveGlobalsRegistered) return;
		_curveGlobalsRegistered = true;

		RenderingServer.GlobalShaderParameterAdd(
			CurveStrengthParam, RenderingServer.GlobalShaderParameterType.Float, 0f);
		RenderingServer.GlobalShaderParameterAdd(
			CurveFlatRadiusParam, RenderingServer.GlobalShaderParameterType.Float, 0f);
		RenderingServer.GlobalShaderParameterAdd(
			CurveOriginParam, RenderingServer.GlobalShaderParameterType.Vec3, Vector3.Zero);
	}

	// The bend is measured from the camera, not the player: the two differ during camera
	// shake and any future cutscene, and a mismatch would make the whole world visibly
	// slosh. GetCamera3D() on the root viewport is the main-scene camera — the arms live
	// in their own SubViewport with a separate camera and are deliberately not curved
	// (they're viewmodel space, and bending them would bow the laser arm on screen).
	//
	// Strength is re-derived every frame rather than only on assignment because the planet
	// size isn't known when Global._Ready runs and changes with every planet load — this
	// way a new world curves correctly with nothing having to remember to recompute it.
	// Two float ops per frame.
	private void UpdateCurveOrigin()
	{
		RenderingServer.GlobalShaderParameterSet(CurveStrengthParam, GetCurveStrength());
		RenderingServer.GlobalShaderParameterSet(CurveFlatRadiusParam, GetCurveFlatRadius());

		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		RenderingServer.GlobalShaderParameterSet(CurveOriginParam, cam.GlobalPosition);
	}

	// Tuning/debug seam — the F3 menu drives this, and a BiomeDescriptor or the eventual
	// PlanetDescriptor could own it per-planet. 0 is a flat world. The structure builder
	// and the ship don't need that: both keep the old flat StandardMaterial3D, so neither
	// reads this at all.
	//
	// Mirrored in a field rather than read back from the RenderingServer because
	// GlobalShaderParameterGet, like GlobalShaderParameterGetList, is editor-only —
	// in a running game it returns null AND logs "should never be used outside the
	// editor". Write-only to the RenderingServer, read from here.
	public void SetCurveExaggeration(float exaggeration) =>
		_curveExaggeration = Mathf.Max(exaggeration, 0f);

	public float GetCurveExaggeration() => _curveExaggeration;

	// Clamped below 0.5 because the flat zone is a radius, and at half the wrap width it
	// would already cover everything a legal render distance can draw — leaving a world
	// that is flat everywhere and only pretending to have the setting.
	public void SetCurveFlatFraction(float fraction) =>
		_curveFlatFraction = Mathf.Clamp(fraction, 0f, 0.49f);

	public float GetCurveFlatFraction() => _curveFlatFraction;

	// Radius, in blocks, inside which there is no displacement at all.
	public float GetCurveFlatRadius() => _curveFlatFraction * GetWorldWrapWidth();

	// The wrap distance, in blocks — the smaller of the two axes, since that's the one
	// that repeats soonest and therefore sets the tightest honest horizon.
	public float GetWorldWrapWidth()
	{
		int chunks = Mathf.Min(PlanetChunksX, PlanetChunksZ);
		return chunks > 0 ? chunks * CHUNK_SIZE : FallbackWorldWidth;
	}

	// drop = strength * distance^2. See the block comment above for the derivation.
	public float GetCurveStrength() => _curveExaggeration * Mathf.Pi / GetWorldWrapWidth();

	// CPU-side twin of world_curve_drop() in WorldCurve.gdshaderinc. Anything that can't
	// go through a curved shader — an entity's imported .glb materials, a node positioned
	// in script — uses this so it lands on the same surface the terrain shader drew.
	// The two must stay in step; if the shader's formula changes, change this with it.
	//
	// Measured from the camera, matching the shader's world_curve_origin exactly. Falls
	// back to no drop when there's no camera, which is the correct answer for a frame
	// where nothing is being rendered anyway.
	public float CurveDropAt(Vector3 worldPos)
	{
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return 0f;

		Vector3 camPos = cam.GlobalPosition;
		float dx = worldPos.X - camPos.X;
		float dz = worldPos.Z - camPos.Z;

		float past = Mathf.Sqrt(dx * dx + dz * dz) - GetCurveFlatRadius();
		if (past <= 0f) return 0f;
		return past * past * GetCurveStrength();
	}

	// Standing eye height in blocks, for the horizon estimate below. Approximate on
	// purpose — this feeds a debug readout, not anything the player collides with.
	private const float EyeHeight = 2f;

	// Where the ground falls to eye level, i.e. the visible horizon this curve implies.
	// Solving drop == EyeHeight for distance. This is the number that says whether the
	// curve is fighting the player's ability to see enemies, so it's what F3 shows.
	//
	// Deliberately takes no arguments: a C# method with a DEFAULT PARAMETER does not
	// reliably appear in the member list Godot generates for GDScript, and calling it
	// from a .gd fails with "Nonexistent function" even though the build is current.
	public float GetHorizonDistance()
	{
		float s = GetCurveStrength();
		// The bend only starts at the flat radius, so the horizon is pushed out by it.
		return s <= 0f ? 99999f : GetCurveFlatRadius() + Mathf.Sqrt(EyeHeight / s);
	}

	// Largest drop the curve can apply to anything still being drawn — the displacement at
	// the far edge of the render volume. This is the frustum-cull slack chunks need, since
	// culling tests the un-displaced AABB on the CPU and knows nothing about the vertex
	// stage. Takes the render edge as an argument because Chunk_Manager owns RenderDistance.
	public float GetCurveDropAtEdge(float renderEdgeDistance)
	{
		float past = renderEdgeDistance - GetCurveFlatRadius();
		if (past <= 0f) return 0f;
		return past * past * GetCurveStrength();
	}

	// Chunk streaming only ever needs "where is the camera", not a Player. The structure
	// builder has no Player at all, so it registers its flycam here instead. Player always
	// wins when both exist — gameplay scenes never set this.
	public Node3D StreamingAnchor { get; set; }

	public Vector3 GetPlayerPos()
	{
		if (Player == null)
		{
			if (StreamingAnchor != null && IsInstanceValid(StreamingAnchor))
			{
				_prevPos = StreamingAnchor.GlobalTransform.Origin;
				return _prevPos;
			}
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

	// The block the player's crosshair is on, as set by interactions.gd's DDA targeting.
	// Wrapped here rather than read as Global.Player.SelectedCubePosition from GDScript so
	// callers don't hop through two marshaled property reads and a possibly-freed Player —
	// same reasoning as GetPlayerPos(). SelectedCube is 0 when nothing is targeted, which is
	// also Air, so "is anything selected" and "which block" are two separate questions.
	public bool HasSelectedBlock() => Player != null && Player.SelectedCube != 0;
	public Vector3I GetSelectedBlock() => Player?.SelectedCubePosition ?? Vector3I.Zero;

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
	public static int PlanetChunksX = 32;
	public static int PlanetChunksZ = 32;

	public static int PlanetWidth => PlanetChunksX * CHUNK_SIZE;
	public static int PlanetDepth => PlanetChunksZ * CHUNK_SIZE;

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
