using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Drives the demo run: a linear sequence of TotalStages planet stages, each
// offering a choice of CurrentOptions (biome + cosmetic difficulty), followed
// by a placeholder end-state until a real boss exists. CurrentOptions is a
// plain List, and CompleteStage() is the only place that assumes "next stage
// = index + 1" — swapping in a real branching node graph later only means
// changing that one method and adding a NextNodeId to StageOption.
public partial class RunManager : Node
{
	public static RunManager Instance { get; private set; }

	public const int TotalStages = 3;

	// Cosmetic only — PlanetDescriptor doesn't exist yet, so difficulty has no
	// gameplay effect yet. Kill targets are untuned placeholders.
	private static readonly int[] StageKillTargets = { 15, 20, 25 };
	private static readonly string[] DifficultyLabels = { "Easy", "Medium", "Hard" };

	public class StageOption
	{
		public string Biome;
		public string Template;
		public int Seed;
		public string DifficultyLabel;
	}

	public int CurrentStageIndex { get; private set; } = 0;
	public bool RunComplete { get; private set; } = false;
	public List<StageOption> CurrentOptions { get; private set; } = new();
	public List<string> CurrentAccessoryOptions { get; private set; } = new();

	private bool _stageActive = false;
	private int _stageKillTarget = 0;
	private readonly Random _rng = new Random();
	private readonly HashSet<string> _usedBiomes = new();

	public override void _Ready() => Instance = this;

	public override void _Process(double delta)
	{
		if (!_stageActive || Global.Instance == null) return;
		if (Global.Instance.KillCount >= _stageKillTarget)
			CompleteStage();
	}

	private void ResetRunState()
	{
		CurrentStageIndex = 0;
		RunComplete = false;
		_stageActive = false;
		_usedBiomes.Clear();
		Global.Instance.EquippedAccessoryIds.Clear();
	}

	// Called by MainMenu's "New Run" button.
	public void StartNewRun()
	{
		ResetRunState();
		GenerateOptionsForStage();
	}

	// Called on death (Player.Die()) and on returning to MainMenu after a run-complete —
	// both are the run actually ending, so both forfeit stage progress and accessories.
	public void EndRun()
	{
		ResetRunState();
	}

	// Picks 3 biomes not yet seen this run (falls back to the full pool once
	// exhausted, since there are only 9 biomes total across a 3-stage run).
	private void GenerateOptionsForStage()
	{
		var pool = Biome_Registry.All.Where(b => !_usedBiomes.Contains(b.Name)).ToList();
		if (pool.Count < 3) pool = Biome_Registry.All.ToList();

		var picks = pool.OrderBy(_ => _rng.Next()).Take(3).ToList();
		CurrentOptions = picks.Select(b => new StageOption
		{
			Biome = b.Name,
			Template = b.Template,
			Seed = _rng.Next(),
			DifficultyLabel = DifficultyLabels[_rng.Next(DifficultyLabels.Length)],
		}).ToList();
	}

	// GDScript-facing marshaling, mirrors the dict convention already used by Global.SetPlanetConfig.
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

	// Picks 3 accessories not yet equipped this run (falls back to the full pool once
	// exhausted, mirroring GenerateOptionsForStage's biome fallback).
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

	// Called by UpgradeSelect when the player picks one of CurrentAccessoryOptions.
	public void ChooseAccessory(int index)
	{
		if (index < 0 || index >= CurrentAccessoryOptions.Count) return;
		Global.Instance.SetAccessoryEquipped(CurrentAccessoryOptions[index], true);
		GetTree().ChangeSceneToFile("res://Scenes/PlanetSelect.tscn");
	}

	// Called by PlanetSelect when the player picks one of CurrentOptions.
	public void ChooseOption(int index)
	{
		if (index < 0 || index >= CurrentOptions.Count) return;
		var opt = CurrentOptions[index];
		var descriptor = Biome_Registry.Get(opt.Biome);
		if (descriptor == null) return;

		_usedBiomes.Add(opt.Biome);
		Global.Instance.ApplyPlanetParams(descriptor.MakePlanetParams(opt.Seed));
		_stageKillTarget = StageKillTargets[Math.Min(CurrentStageIndex, StageKillTargets.Length - 1)];
		_stageActive = true;
		GetTree().ChangeSceneToFile("res://Scenes/CubeLand.tscn");
	}

	private void CompleteStage()
	{
		_stageActive = false;
		CurrentStageIndex++;
		if (CurrentStageIndex >= TotalStages)
			RunComplete = true; // placeholder end-state — no boss stage yet
		else
			GenerateOptionsForStage();

		// Upgrade pick always comes first — UpgradeSelect.ChooseAccessory() is what
		// actually transitions on to PlanetSelect.tscn (next options or complete panel).
		GenerateAccessoryOptions();
		GetTree().ChangeSceneToFile("res://Scenes/UpgradeSelect.tscn");
	}
}
