using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// One generated solar system — the container for a single attempt, per
// documents/design/solar-system-run-structure-design-log.html.
//
// A system is a linear rail of nodes ending at the sun:
//
//     P - P - P - [W] - P - P - P - SUN
//
// Systems are generated per-offer, never drawn from a fixed roster (Decision 01),
// so a bad draw is this attempt's luck rather than a standing flaw. Generation is
// fully seeded: the same Seed always rebuilds the same system, which is what lets
// SolarSelect show a real topology preview before the player commits to it.
//
// SKELETON: EnemyDensityScale, TimeLimitSeconds and Modifiers are carried and
// surfaced to the UI but nothing consumes them yet — they're the hooks for the
// difficulty work the design log leaves open. The numbers in TierConfig are
// deliberate placeholders, not tuned values.

public enum SystemNodeKind
{
	Planet,
	Warpstation, // hosts the shop; not built yet, currently auto-skipped
	Sun,        // the capstone — destroying it clears the system
}

public enum SystemNodeState
{
	Locked,
	Current,
	Cleared,
}

// Local fog (Decision 03): current node known in full, next known roughly,
// everything past that hidden. The sun is the one exception — it's the stated
// goal, so it stays on the map from the start.
public enum SystemNodeFog
{
	Known,
	Rough,
	Hidden,
}

public class SystemNode
{
	public int Index;
	public SystemNodeKind Kind;
	public SystemNodeState State = SystemNodeState.Locked;

	// Empty for Warpstation/Sun nodes.
	public string Biome = "";
	public string Template = "";
	public int Seed;

	// Kills needed to unlock the hop off this node. Derived, not hand-authored.
	public int KillTarget;
	public string DifficultyLabel = "";

	public bool IsCombat => Kind == SystemNodeKind.Planet || Kind == SystemNodeKind.Sun;
}

public class SolarSystemDescriptor
{
	// Difficulty tiers offered on SolarSelect. PlanetMin/Max are the counts the
	// player asked for; everything else is a placeholder dial for later.
	public class TierConfig
	{
		public string Tier;
		public int PlanetMin, PlanetMax;
		public int Warpstations;
		public float EnemyDensityScale;
		public float TimeLimitSeconds;
		public int ModifierCount;
		public int DangerLevel;
	}

	// UNTUNED. Planet counts are the spec; densities, clocks and modifier counts
	// are first guesses so the fields exist and show up in the preview.
	//
	// All three tiers are DangerLevel 1 on purpose — danger is its own axis, not a
	// restatement of the tier, and nothing above 1 has been designed yet. A tier
	// staying at 1 while its planet count grows is the intended reading: a longer
	// system, not a nastier one.
	public static readonly TierConfig[] Tiers =
	{
		new TierConfig { Tier = "Easy",   PlanetMin = 5,  PlanetMax = 7,  Warpstations = 1, EnemyDensityScale = 1.00f, TimeLimitSeconds = 480f,  ModifierCount = 0, DangerLevel = 1 },
		new TierConfig { Tier = "Medium", PlanetMin = 10, PlanetMax = 12, Warpstations = 2, EnemyDensityScale = 1.35f, TimeLimitSeconds = 780f,  ModifierCount = 1, DangerLevel = 1 },
		new TierConfig { Tier = "Hard",   PlanetMin = 16, PlanetMax = 20, Warpstations = 3, EnemyDensityScale = 1.80f, TimeLimitSeconds = 1080f, ModifierCount = 2, DangerLevel = 1 },
	};

	// The framework already scoped in the engineering docs (Decision 17) — nothing
	// reads these yet, they exist so the preview can show a system's flavour.
	private static readonly string[] ModifierPool =
	{
		"Low Gravity", "Heavy Fog", "Alien Surface", "Dense Terrain", "High Hostility",
	};

	private static readonly string[] Designations =
	{
		"PIANI", "SHOMAK", "THORIUM", "NECESSITY", "PROTO", "COSMOS", "DEEP SPACE", "LERP", "ARBOR", "CHIME",
	};

	// Danger is the game-wide threat scale: 1..10, then PLANT above it. It is a
	// property of the thing, not of the screen showing it — a system, a planet and
	// (later) an enemy all report on the same scale, so a "danger 4" reads the same
	// everywhere. PlantDanger is reserved and deliberately NOT generated yet: the
	// final boss is the only thing that will ever carry it.
	public const int MinDanger = 1;
	public const int MaxDanger = 10;
	public const int PlantDanger = 11;

	public string Tier = "Easy";
	public string Name = "";
	public int Seed;
	public int DangerLevel = MinDanger;
	public float EnemyDensityScale = 1f;
	public float TimeLimitSeconds = 480f;
	public List<string> Modifiers = new();
	public List<SystemNode> Nodes = new();

	public int PlanetCount => Nodes.Count(n => n.Kind == SystemNodeKind.Planet);
	public int WarpstationCount => Nodes.Count(n => n.Kind == SystemNodeKind.Warpstation);
	public int ClearedCount => Nodes.Count(n => n.State == SystemNodeState.Cleared);

	public SystemNode NodeAt(int i) => (i >= 0 && i < Nodes.Count) ? Nodes[i] : null;

	public static TierConfig ConfigFor(string tier) =>
		Tiers.FirstOrDefault(t => t.Tier == tier) ?? Tiers[0];

	// Builds one system from a seed. Everything downstream of `seed` is deterministic,
	// so SolarSelect can preview a system and RunManager can rebuild the identical one.
	public static SolarSystemDescriptor Generate(string tier, int seed)
	{
		var cfg = ConfigFor(tier);
		var rng = new Random(seed);

		var sys = new SolarSystemDescriptor
		{
			Tier = cfg.Tier,
			Seed = seed,
			DangerLevel = Math.Clamp(cfg.DangerLevel, MinDanger, MaxDanger),
			EnemyDensityScale = cfg.EnemyDensityScale,
			TimeLimitSeconds = cfg.TimeLimitSeconds,
			Name = $"{Designations[rng.Next(Designations.Length)]}-{rng.Next(100, 1000)}",
		};

		var modPool = ModifierPool.OrderBy(_ => rng.Next()).ToList();
		sys.Modifiers = modPool.Take(Math.Min(cfg.ModifierCount, modPool.Count)).ToList();

		int planetCount = rng.Next(cfg.PlanetMin, cfg.PlanetMax + 1);

		// Warpstation slots, expressed as "after this many planets". Spaced evenly through
		// the planet sequence so a single warpstation lands mid-system (the shop-in-the-
		// middle case) and multiples split it into even legs. Detour cost isn't modelled
		// yet — on this linear rail every node is on the path.
		var warpstationAfter = new HashSet<int>();
		for (int w = 1; w <= cfg.Warpstations; w++)
		{
			int after = (int)Math.Round(planetCount * (w / (float)(cfg.Warpstations + 1)));
			after = Math.Clamp(after, 1, planetCount - 1);
			warpstationAfter.Add(after);
		}

		// Biomes are drawn without replacement and the bag refills when it empties —
		// a 20-planet Hard system needs more planets than the 9 registered biomes.
		var bag = new List<BiomeDescriptor>();

		for (int p = 0; p < planetCount; p++)
		{
			if (bag.Count == 0) bag = Biome_Registry.All.OrderBy(_ => rng.Next()).ToList();
			var biome = bag[0];
			bag.RemoveAt(0);

			// Ramps 0 -> 1 across the system; feeds both the kill target and the label.
			float t = planetCount > 1 ? p / (float)(planetCount - 1) : 0f;

			sys.Nodes.Add(new SystemNode
			{
				Index = sys.Nodes.Count,
				Kind = SystemNodeKind.Planet,
				Biome = biome.Name,
				Template = biome.Template,
				Seed = rng.Next(),
				KillTarget = KillTargetFor(t, cfg.EnemyDensityScale),
				DifficultyLabel = LabelFor(t),
			});

			if (warpstationAfter.Contains(p + 1))
				sys.Nodes.Add(new SystemNode
				{
					Index = sys.Nodes.Count,
					Kind = SystemNodeKind.Warpstation,
					DifficultyLabel = "Warpstation",
				});
		}

		sys.Nodes.Add(new SystemNode
		{
			Index = sys.Nodes.Count,
			Kind = SystemNodeKind.Sun,
			KillTarget = KillTargetFor(1f, cfg.EnemyDensityScale) * 2,
			DifficultyLabel = "Sun",
		});

		return sys;
	}

	// Placeholder curve: 12 kills on the first planet up to ~30 on the last, scaled by
	// the tier's density. Low enough that a player having fun clears it without farming,
	// which is the one constraint the design log actually states.
	private static int KillTargetFor(float t, float densityScale) =>
		Mathf.RoundToInt(Mathf.Lerp(12f, 30f, t) * densityScale);

	private static string LabelFor(float t) => t < 0.34f ? "Low" : (t < 0.67f ? "Moderate" : "Severe");
}
