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
// Waystation nodes are auto-skipped for now — the shop doesn't exist yet. When it
// does, SkipWaystation() is the one place that has to change.
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

		if (Global.Instance.KillCount >= _stageKillTarget)
			CompleteStage();
	}

	// ───────────────────────────── run lifecycle ─────────────────────────────

	private void ResetRunState()
	{
		CurrentSystem = null;
		CurrentNodeIndex = 0;
		RunComplete = false;
		ClockExpired = false;
		ClockRemaining = 0f;
		_stageActive = false;
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
	// tier, clock, waystations and modifier flavour; no biome or enemy composition.
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
				{ "waystations", p.WaystationCount },
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

		// A waystation has nothing to launch into yet — step over it and land on
		// whatever follows.
		if (node.Kind == SystemNodeKind.Waystation)
		{
			SkipWaystation();
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
		GetTree().ChangeSceneToFile("res://Scenes/CubeLand.tscn");
	}

	// TODO(boss): replace with the real sun encounter. Lava Walls is the closest
	// existing biome to "you are standing on a star".
	private BiomeDescriptor PickSunBiome() =>
		Biome_Registry.Get("Lava Walls") ?? Biome_Registry.All[0];

	// TODO(shop): a waystation should open the shop. Until that exists it's marked
	// cleared and stepped past so it still reads as a visited node on the map.
	private void SkipWaystation()
	{
		var node = CurrentSystem.NodeAt(CurrentNodeIndex);
		if (node == null || node.Kind != SystemNodeKind.Waystation) return;
		node.State = SystemNodeState.Cleared;
		CurrentNodeIndex++;
		var next = CurrentSystem.NodeAt(CurrentNodeIndex);
		if (next != null) next.State = SystemNodeState.Current;
	}

	private void CompleteStage()
	{
		_stageActive = false;
		if (CurrentSystem == null) return;

		var cleared = CurrentSystem.NodeAt(CurrentNodeIndex);
		if (cleared != null) cleared.State = SystemNodeState.Cleared;

		bool wasSun = cleared != null && cleared.Kind == SystemNodeKind.Sun;
		CurrentNodeIndex++;

		// Step over any waystations sitting between here and the next combat node.
		while (CurrentSystem.NodeAt(CurrentNodeIndex) is SystemNode w && w.Kind == SystemNodeKind.Waystation)
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

		// LoadingScreen reads RunComplete to decide between the accessory pick and the
		// end-of-run panel, so options are generated unconditionally and go unused on
		// the final clear.
		GenerateAccessoryOptions();
		GetTree().ChangeSceneToFile("res://Scenes/LoadingScreen.tscn");
	}

	// ───────────────────────── GDScript-facing marshaling ─────────────────────────
	// Everything the .gd screens need is exposed as a method call, not a property —
	// C# property reads from GDScript are unreliable in this project.

	public bool HasActiveSystem() => CurrentSystem != null;
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
			{ "waystations", CurrentSystem.WaystationCount },
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
