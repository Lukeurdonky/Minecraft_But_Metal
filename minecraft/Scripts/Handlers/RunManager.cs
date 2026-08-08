using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Drives one attempt = one solar system, per
// documents/design/solar-system-run-structure-design-log.html (Decision 04).
//
//   SolarSelect (pick 1 of 3 offers)
//     -> SolarMap  (route view, commit)
//     -> CubeLand  (node 0)
//     -> LoadingScreen (accessory pick)
//     -> CubeLand  (node 1) ... -> Sun -> run complete
//
// Warpstation nodes are auto-skipped for now — the shop doesn't exist yet. When it
// does, SkipWarpstation() is the one place that has to change.
//
// The old fixed 3-stage demo (TotalStages, StageKillTargets, GoToRandomPlanet) is
// gone: run length is now whatever the generated system's node list says. The
// PlanetSelect block at the bottom is still shelved-not-deleted, unchanged.
public partial class RunManager : Node
{
	public static RunManager Instance { get; private set; }

	private const int OfferCount = 3;

	public class SystemOffer
	{
		public string Tier;
		public int Seed;
		public SolarSystemDescriptor Preview; // generated up front so the preview is truthful
	}

	public SolarSystemDescriptor CurrentSystem { get; private set; }
	public int CurrentNodeIndex { get; private set; } = 0;
	public bool RunComplete { get; private set; } = false;
	public bool ClockExpired { get; private set; } = false;
	public float ClockRemaining { get; private set; } = 0f;
	public List<SystemOffer> CurrentOffers { get; private set; } = new();
	public List<string> CurrentAccessoryOptions { get; private set; } = new();

	// ── warp-out ──────────────────────────────────────────────────────────────
	// Hitting a node's kill target no longer teleports you off it. The exit opens and the
	// player triggers it, so a cleared planet is somewhere you can keep moving through
	// instead of a room the game yanks you out of mid-swing. The clock keeps draining
	// through both the decision and the charge, so lingering is a real cost.
	//
	// The exit is a PLACE, not a keypress: clearing the target calls down a warp point
	// structure, and interacting with it is what starts the charge. RunManager owns the
	// state machine and nothing else — WarpPoint.gd owns the object in the world and is
	// the only thing that calls StartWarp(). See "Warping out" in CLAUDE.md.
	public const float  WarpChargeSeconds = 10f;
	private const string WarpAction = "start_warp";
	public const string WarpKeyName = "J"; // display only — keep in step with WarpAction's binding

	// Where the warp point itself is in its arrival. Set by WarpPoint.gd; read by the HUD so
	// the prompt can say "inbound" rather than naming a key. Ints rather than a C# enum
	// because this crosses to GDScript.
	public const int WarpPointNone     = 0; // node not cleared yet, or no warp point in this scene
	public const int WarpPointInbound  = 1; // called down, still falling
	public const int WarpPointLanded   = 2; // on the ground and interactable
	public const int WarpPointMissing  = 3; // no such structure authored — key fallback is live

	public bool  WarpReady     { get; private set; } = false;
	public bool  WarpCharging  { get; private set; } = false;
	public float WarpRemaining { get; private set; } = 0f;
	public int   WarpPointPhase { get; private set; } = WarpPointNone;

	private bool _stageActive = false;
	private int _stageKillTarget = 0;
	private readonly Random _rng = new Random();

	public override void _Ready() => Instance = this;

	public override void _Process(double delta)
	{
		if (!_stageActive || Global.Instance == null) return;

		// Shared clock — runs only while a node is actually being played, so it doesn't
		// drain behind the map overlay or the accessory pick.
		if (!ClockExpired && ClockRemaining > 0f)
		{
			ClockRemaining = Mathf.Max(0f, ClockRemaining - (float)delta);
			// TODO(design): a timeout is meant to force the sun fight early at a defined
			// disadvantage. That mechanic is still an open question, so for now expiry is
			// recorded and surfaced on the map, and nothing is forced.
			if (ClockRemaining <= 0f) ClockExpired = true;
		}

		// Three-step exit: reach the target -> the warp offer appears -> the player starts it
		// -> it charges for WarpChargeSeconds -> the stage completes. Once WarpReady latches,
		// the kill count stops mattering; killing more can't un-clear the node.
		if (WarpCharging)
		{
			WarpRemaining = Mathf.Max(0f, WarpRemaining - (float)delta);
			if (WarpRemaining <= 0f) CompleteStage();
			return;
		}

		if (!WarpReady)
		{
			if (Global.Instance.KillCount >= _stageKillTarget) WarpReady = true;
			return;
		}

		// Normally nothing happens here: the warp point is the trigger, and WarpPoint.gd calls
		// StartWarp() when the player interacts with it.
		//
		// The key stays armed in the two cases where no warp point can be reached, because
		// both would otherwise strand the run on a planet with no way off it:
		//   Missing — the "Warp Point" structure isn't authored, or has no console marker.
		//             WarpPoint.gd says so on screen and sets this.
		//   None    — no WarpPoint node in the scene at all (a hand-built or debug CubeLand).
		//             Also covers the single frame between WarpReady latching and the node
		//             reacting to it, which is harmless.
		if (WarpPointPhase == WarpPointInbound || WarpPointPhase == WarpPointLanded) return;

		// HasAction guard: IsActionJustPressed on a missing action errors every frame, and the
		// binding lives in project.godot where it can be edited out from under this.
		if (InputMap.HasAction(WarpAction) && Input.IsActionJustPressed(WarpAction))
			StartWarp();
	}

	// Public so a UI button can trigger the same sequence the key does.
	public void StartWarp()
	{
		if (!_stageActive || !WarpReady || WarpCharging) return;
		WarpCharging  = true;
		WarpRemaining = WarpChargeSeconds;
	}

	private void ResetWarpState()
	{
		WarpReady     = false;
		WarpCharging  = false;
		WarpRemaining = 0f;
		WarpPointPhase = WarpPointNone;
	}

	// WarpPoint.gd reporting where its structure is in arriving. One writer by design — the
	// node that owns the object in the world is the only thing that knows.
	//
	// Deliberately three named events rather than a SetWarpPointPhase(int): the phase
	// constants above are C# `const`s, and statics never appear in the per-instance member
	// switch Godot generates for GDScript, so `RunManager.WarpPointInbound` from a .gd file
	// reads as null and silently sets phase 0. Naming the event instead of the value keeps
	// the constants on this side of the boundary, where they work.
	public void ReportWarpPointInbound() => WarpPointPhase = WarpPointInbound;
	public void ReportWarpPointLanded() => WarpPointPhase = WarpPointLanded;

	// "There is no warp point coming" — no structure authored, no console marker, or the
	// stamp failed. Re-arms the key fallback so the run isn't stranded.
	public void ReportWarpPointUnavailable() => WarpPointPhase = WarpPointMissing;

	public int GetWarpPointPhase() => WarpPointPhase;

	// Same reason: WarpKeyName is a const, so GDScript has to be handed it by a method.
	public string GetWarpKeyName() => WarpKeyName;

	// ───────────────────────────── run lifecycle ─────────────────────────────

	private void ResetRunState()
	{
		CurrentSystem = null;
		CurrentNodeIndex = 0;
		RunComplete = false;
		ClockExpired = false;
		ClockRemaining = 0f;
		_stageActive = false;
		ResetWarpState();
		_usedBiomes.Clear();
		Global.Instance.EquippedAccessoryIds.Clear();
	}

	private const string ShipScene = "res://Scenes/Ship.tscn";

	// Called by MainMenu's "New Run". The attempt doesn't exist until a system is picked, so
	// starting one is just "go to the ship" — the same thing that happens when one ends.
	public void StartNewRun() => ReturnToShip();

	// The single exit from an attempt: abandoning it, dying in it, and clearing the sun all
	// land back on the ship. Every caller defers to this rather than changing scene itself,
	// the same convention StartNewRun/ChooseSystem already follow — a ChangeSceneToFile
	// alongside this call races the one it queues.
	//
	// Offers are rolled here rather than on the select screen so they don't reshuffle every
	// time you walk up to mission control and back out, and so a finished attempt is never
	// re-offered the system it just played (Decision 01 — generated per offer, no roster).
	public void ReturnToShip()
	{
		ResetRunState();
		GenerateOffers();
		// Safety net: `paused` is a tree flag that survives a scene change, and the map/select
		// overlays set it. The hub must never load frozen.
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(ShipScene);
	}

	// State reset only — for callers that are handling their own transition. Anything that ends
	// a run and wants to leave should call ReturnToShip() instead.
	public void EndRun() => ResetRunState();

	// ───────────────────────────── system offers ─────────────────────────────

	// Three fresh topologies, one per difficulty tier, regenerated every time the
	// select screen is reached (Decision 01 — no fixed roster to memorise). Each is
	// fully generated now rather than at pick time so the preview shows the real
	// planet count and modifiers, not a guess.
	public void GenerateOffers()
	{
		CurrentOffers = SolarSystemDescriptor.Tiers.Take(OfferCount).Select(cfg =>
		{
			int seed = _rng.Next();
			return new SystemOffer
			{
				Tier = cfg.Tier,
				Seed = seed,
				Preview = SolarSystemDescriptor.Generate(cfg.Tier, seed),
			};
		}).ToList();
	}

	// SolarSelect calls this if offers are somehow missing (e.g. the scene was run
	// directly from the editor rather than reached through MainMenu).
	public void EnsureOffers()
	{
		if (CurrentOffers.Count == 0) GenerateOffers();
	}

	// Topology preview only — category, never contents (Decision 02). Planet count,
	// tier, clock, warpstations and modifier flavour; no biome or enemy composition.
	public Godot.Collections.Array<Godot.Collections.Dictionary> GetOffersForUI()
	{
		var arr = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		foreach (var o in CurrentOffers)
		{
			var p = o.Preview;
			arr.Add(new Godot.Collections.Dictionary
			{
				{ "tier", o.Tier },
				{ "name", p.Name },
				{ "planets", p.PlanetCount },
				{ "warpstations", p.WarpstationCount },
				// The meter draws itself from these two — it never hardcodes 10, so raising
				// MaxDanger (or the PLANT level landing above it) needs no UI change.
				{ "danger", p.DangerLevel },
				{ "danger_max", SolarSystemDescriptor.MaxDanger },
				{ "time_limit", p.TimeLimitSeconds },
				{ "density", p.EnemyDensityScale },
				{ "modifiers", new Godot.Collections.Array<string>(p.Modifiers) },
			});
		}
		return arr;
	}

	// Commit to one of the three offers. No switching, no going back (Decision 01).
	public void ChooseSystem(int offerIndex)
	{
		if (offerIndex < 0 || offerIndex >= CurrentOffers.Count) return;
		var offer = CurrentOffers[offerIndex];

		CurrentSystem = offer.Preview;
		CurrentNodeIndex = 0;
		RunComplete = false;
		ClockExpired = false;
		ClockRemaining = CurrentSystem.TimeLimitSeconds;
		CurrentSystem.Nodes[0].State = SystemNodeState.Current;

		GetTree().ChangeSceneToFile("res://Scenes/SolarMap.tscn");
	}

	// ───────────────────────────── node traversal ─────────────────────────────

	// Drops the player onto the current node. Called by SolarMap's LAUNCH button and
	// by ChooseAccessory once the between-planet pick is made.
	public void LaunchCurrentNode()
	{
		if (CurrentSystem == null) return;
		var node = CurrentSystem.NodeAt(CurrentNodeIndex);
		if (node == null) return;

		// A warpstation has nothing to launch into yet — step over it and land on
		// whatever follows.
		if (node.Kind == SystemNodeKind.Warpstation)
		{
			SkipWarpstation();
			node = CurrentSystem.NodeAt(CurrentNodeIndex);
			if (node == null) return;
		}

		// The sun is the capstone fight; no boss scene exists yet, so it currently
		// plays as a final planet with a doubled kill target.
		var descriptor = node.Kind == SystemNodeKind.Sun
			? PickSunBiome()
			: Biome_Registry.Get(node.Biome);
		if (descriptor == null) return;

		Global.Instance.ApplyPlanetParams(descriptor.MakePlanetParams(node.Seed));
		_stageKillTarget = node.KillTarget;
		_stageActive = true;
		ResetWarpState(); // every node starts un-cleared, including one re-entered mid-run
		GetTree().ChangeSceneToFile("res://Scenes/CubeLand.tscn");
	}

	// TODO(boss): replace with the real sun encounter. Lava Walls is the closest
	// existing biome to "you are standing on a star".
	private BiomeDescriptor PickSunBiome() =>
		Biome_Registry.Get("Lava Walls") ?? Biome_Registry.All[0];

	// TODO(shop): a warpstation should open the shop. Until that exists it's marked
	// cleared and stepped past so it still reads as a visited node on the map.
	private void SkipWarpstation()
	{
		var node = CurrentSystem.NodeAt(CurrentNodeIndex);
		if (node == null || node.Kind != SystemNodeKind.Warpstation) return;
		node.State = SystemNodeState.Cleared;
		CurrentNodeIndex++;
		var next = CurrentSystem.NodeAt(CurrentNodeIndex);
		if (next != null) next.State = SystemNodeState.Current;
	}

	private void CompleteStage()
	{
		_stageActive = false;
		ResetWarpState();
		if (CurrentSystem == null) return;

		var cleared = CurrentSystem.NodeAt(CurrentNodeIndex);
		if (cleared != null) cleared.State = SystemNodeState.Cleared;

		bool wasSun = cleared != null && cleared.Kind == SystemNodeKind.Sun;
		CurrentNodeIndex++;

		// Step over any warpstations sitting between here and the next combat node.
		while (CurrentSystem.NodeAt(CurrentNodeIndex) is SystemNode w && w.Kind == SystemNodeKind.Warpstation)
		{
			w.State = SystemNodeState.Cleared;
			CurrentNodeIndex++;
		}

		var next = CurrentSystem.NodeAt(CurrentNodeIndex);
		if (next == null || wasSun)
		{
			CurrentNodeIndex = Math.Min(CurrentNodeIndex, CurrentSystem.Nodes.Count - 1);
			RunComplete = true; // sun destroyed — clean clear
		}
		else next.State = SystemNodeState.Current;

		// Warping out lands on the solar map, not the accessory pick — the map is where you
		// see what the hop cost you and choose to launch again. The accessory options are
		// still generated because LoadingScreen (and whatever replaces it) still reads them.
		GenerateAccessoryOptions();

		// The one case that still goes to LoadingScreen: it owns the end-of-run panel, and
		// the map has no "you won" state to show. Swap this the moment a real one exists.
		GetTree().ChangeSceneToFile(RunComplete
			? "res://Scenes/LoadingScreen.tscn"
			: "res://Scenes/SolarMap.tscn");
	}

	// ───────────────────────── GDScript-facing marshaling ─────────────────────────
	// Everything the .gd screens need is exposed as a method call, not a property —
	// C# property reads from GDScript are unreliable in this project.

	public bool HasActiveSystem() => CurrentSystem != null;
	public bool IsStageActive() => _stageActive;
	public int GetStageKillTarget() => _stageKillTarget;

	// What the HUD shows: how many more you need, not how many you have.
	public int GetKillsRemaining() =>
		Math.Max(0, _stageKillTarget - (Global.Instance?.KillCount ?? 0));

	public bool IsWarpReady() => WarpReady;
	public bool IsWarpCharging() => WarpCharging;
	public float GetWarpRemaining() => WarpRemaining;

	public int GetCurrentNodeIndex() => CurrentNodeIndex;
	public bool IsRunComplete() => RunComplete;
	public bool IsClockExpired() => ClockExpired;
	public float GetClockRemaining() => ClockRemaining;

	public Godot.Collections.Dictionary GetSystemInfoForUI()
	{
		if (CurrentSystem == null) return new Godot.Collections.Dictionary();
		return new Godot.Collections.Dictionary
		{
			{ "name", CurrentSystem.Name },
			{ "tier", CurrentSystem.Tier },
			{ "planets", CurrentSystem.PlanetCount },
			{ "warpstations", CurrentSystem.WarpstationCount },
			{ "cleared", CurrentSystem.ClearedCount },
			{ "total_nodes", CurrentSystem.Nodes.Count },
			{ "time_limit", CurrentSystem.TimeLimitSeconds },
			{ "clock_remaining", ClockRemaining },
			{ "clock_expired", ClockExpired },
			{ "density", CurrentSystem.EnemyDensityScale },
			{ "modifiers", new Godot.Collections.Array<string>(CurrentSystem.Modifiers) },
			{ "current_index", CurrentNodeIndex },
			{ "complete", RunComplete },
		};
	}

	// One dict per node, already fogged — the UI never sees a hidden node's contents,
	// so there's nothing there for it to accidentally render.
	public Godot.Collections.Array<Godot.Collections.Dictionary> GetSystemNodesForUI()
	{
		var arr = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		if (CurrentSystem == null) return arr;

		foreach (var n in CurrentSystem.Nodes)
		{
			var fog = FogFor(n);
			var d = new Godot.Collections.Dictionary
			{
				{ "index", n.Index },
				{ "kind", n.Kind.ToString() },
				{ "state", n.State.ToString() },
				{ "fog", fog.ToString() },
			};

			// Rough = you know roughly what's next; Hidden = you know nothing.
			if (fog == SystemNodeFog.Known)
			{
				d["biome"] = n.Biome;
				d["template"] = n.Template;
				d["difficulty"] = n.DifficultyLabel;
				d["kill_target"] = n.KillTarget;
			}
			else if (fog == SystemNodeFog.Rough)
			{
				d["biome"] = "";
				d["template"] = n.Template;
				d["difficulty"] = n.DifficultyLabel;
				d["kill_target"] = 0;
			}
			else
			{
				d["biome"] = "";
				d["template"] = "";
				d["difficulty"] = "";
				d["kill_target"] = 0;
			}
			arr.Add(d);
		}
		return arr;
	}

	// Decision 03: current known in full, next known roughly, rest hidden. Cleared
	// nodes stay known — you've been there. The sun is always at least roughly known
	// because it's the stated objective and anchors the right end of the map.
	private SystemNodeFog FogFor(SystemNode n)
	{
		if (n.State == SystemNodeState.Cleared || n.Index <= CurrentNodeIndex) return SystemNodeFog.Known;
		if (n.Index == CurrentNodeIndex + 1) return SystemNodeFog.Rough;
		if (n.Kind == SystemNodeKind.Sun) return SystemNodeFog.Rough;
		return SystemNodeFog.Hidden;
	}

	// ───────────────────────── accessory pick (unchanged) ─────────────────────────

	private void GenerateAccessoryOptions()
	{
		var equipped = Global.Instance.EquippedAccessoryIds;
		var pool = Accessory_Registry.All.Where(a => !equipped.Contains(a.Name)).ToList();
		if (pool.Count < 3) pool = Accessory_Registry.All.ToList();

		CurrentAccessoryOptions = pool.OrderBy(_ => _rng.Next()).Take(3).Select(a => a.Name).ToList();
	}

	public Godot.Collections.Array<Godot.Collections.Dictionary> GetAccessoryOptionsForUI()
	{
		var arr = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		foreach (var name in CurrentAccessoryOptions)
		{
			var descriptor = Accessory_Registry.Get(name);
			arr.Add(new Godot.Collections.Dictionary
			{
				{ "name", name },
				{ "description", descriptor?.Description ?? "" },
			});
		}
		return arr;
	}

	// Called by LoadingScreen. The pick is the only gate between planets, so equipping
	// it also launches the next node.
	public void ChooseAccessory(int index)
	{
		if (index < 0 || index >= CurrentAccessoryOptions.Count) return;
		Global.Instance.SetAccessoryEquipped(CurrentAccessoryOptions[index], true);
		LaunchCurrentNode();
	}

	// ─────────────────────────── SHELVED: PlanetSelect ───────────────────────────
	// The old per-hop "choose 1 of 3 biomes" screen. Superseded by the solar map, but
	// PlanetSelect.tscn/.gd are still on disk and still work against this API, so it
	// stays callable rather than being deleted.

	private readonly HashSet<string> _usedBiomes = new();
	private static readonly string[] DifficultyLabels = { "Easy", "Medium", "Hard" };

	public class StageOption
	{
		public string Biome;
		public string Template;
		public int Seed;
		public string DifficultyLabel;
	}

	public List<StageOption> CurrentOptions { get; private set; } = new();

	// Public rather than private so the shelved screen stays self-sufficient: nothing
	// in the solar-system flow calls it, and it would otherwise be dead code.
	public void GenerateOptionsForStage()
	{
		var pool = Biome_Registry.All.Where(b => !_usedBiomes.Contains(b.Name)).ToList();
		if (pool.Count < 3) pool = Biome_Registry.All.ToList();

		CurrentOptions = pool.OrderBy(_ => _rng.Next()).Take(3).Select(b => new StageOption
		{
			Biome = b.Name,
			Template = b.Template,
			Seed = _rng.Next(),
			DifficultyLabel = DifficultyLabels[_rng.Next(DifficultyLabels.Length)],
		}).ToList();
	}

	public Godot.Collections.Array<Godot.Collections.Dictionary> GetOptionsForUI()
	{
		var arr = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		foreach (var o in CurrentOptions)
			arr.Add(new Godot.Collections.Dictionary
			{
				{ "biome", o.Biome },
				{ "template", o.Template },
				{ "difficulty", o.DifficultyLabel },
			});
		return arr;
	}

	public void ChooseOption(int index)
	{
		if (index < 0 || index >= CurrentOptions.Count) return;
		var opt = CurrentOptions[index];
		var descriptor = Biome_Registry.Get(opt.Biome);
		if (descriptor == null) return;

		_usedBiomes.Add(opt.Biome);
		Global.Instance.ApplyPlanetParams(descriptor.MakePlanetParams(opt.Seed));
		_stageActive = true;
		GetTree().ChangeSceneToFile("res://Scenes/CubeLand.tscn");
	}
}
