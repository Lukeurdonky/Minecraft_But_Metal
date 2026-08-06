using Godot;
using System.Collections.Generic;
using System.Linq;

// Autoload. Owns everything about saved structures: scanning them off disk, capturing a
// region of the live world into one, and stamping one back out.
//
// Same "Name is the lookup key" convention as Biome_Registry / Accessory_Registry, but the
// pool is file-backed rather than hardcoded — structures are authored in
// Scenes/StructureBuilder.tscn, not typed into a static constructor.
//
// The capture/save/load entry points are instance methods on purpose: StructureBuilder.gd
// drives them, and GDScript can call methods on a C# autoload but cannot read its plain
// public properties (confirmed empirically — see CLAUDE.md).
public partial class Structure_Registry : Node
{
	public const string ResDir  = "res://Structures";

	// res:// is read-only in an exported build, so authored-at-runtime saves land in user://.
	// Reload() scans both; res:// wins on a name clash since that's the version shipped.
	public const string UserDir = "user://Structures";

	public static string SaveDir => OS.HasFeature("editor") ? ResDir : UserDir;

	private static readonly Dictionary<string, Structure> _byName = new();
	private static bool _scanned = false;

	public override void _Ready() => Reload();

	// ---------------------------------------------------------------- lookup

	public static Structure Get(string name)
	{
		if (!_scanned) Reload();
		return _byName.TryGetValue(name, out var s) ? s : null;
	}

	public static IReadOnlyCollection<string> Names
	{
		get { if (!_scanned) Reload(); return _byName.Keys; }
	}

	public static void Reload()
	{
		_scanned = true;
		_byName.Clear();
		// user:// first so a res:// file of the same name overwrites it.
		ScanDir(UserDir);
		ScanDir(ResDir);
	}

	private static void ScanDir(string dir)
	{
		if (!DirAccess.DirExistsAbsolute(dir)) return;

		using var da = DirAccess.Open(dir);
		if (da == null) return;

		foreach (string file in da.GetFiles())
		{
			// Exported builds serve .tres as .remap; .import never applies to text resources.
			string f = file.EndsWith(".remap") ? file.Substring(0, file.Length - 6) : file;
			if (!f.EndsWith(".tres")) continue;

			// CacheMode.Replace so re-saving a structure and reloading actually picks up the
			// new bytes instead of handing back the editor's cached copy.
			var res = ResourceLoader.Load($"{dir}/{f}", "Structure", ResourceLoader.CacheMode.Replace);
			if (res is not Structure s) continue;

			if (string.IsNullOrEmpty(s.Name))
				s.Name = f.Substring(0, f.Length - 5);

			_byName[s.Name] = s;
		}
	}

	// GDScript bridge — Array<string> marshals cleanly into a PackedStringArray-ish Array.
	public Godot.Collections.Array<string> GetStructureNames()
	{
		var arr = new Godot.Collections.Array<string>();
		foreach (string n in Names.OrderBy(n => n)) arr.Add(n);
		return arr;
	}

	public void ReloadStructures() => Reload();

	// Instance wrapper — GDScript reaches members through the generated per-instance
	// property/method switch, which statics never appear in.
	public string GetSaveDir() => SaveDir;

	// ---------------------------------------------------------------- capture

	// Read every block in [origin, origin+size), trim the empty margins, and save the result
	// as res://Structures/<name>.tres. Returns false (and prints why) if the region is empty
	// or there's no world to read — the builder surfaces that as a status line.
	public bool CaptureAndSave(string name, Vector3I origin, Vector3I size)
	{
		name = SanitizeName(name);
		if (name.Length == 0)
		{
			GD.PushWarning("Structure_Registry: refusing to save a structure with an empty name.");
			return false;
		}

		var cm = Global.Instance?.CubeManager;
		if (cm == null)
		{
			GD.PushWarning("Structure_Registry: no Chunk_Manager — nothing to capture.");
			return false;
		}

		// Tight bounds pass first, so an 8-block hut in a 64³ volume saves as 8 blocks.
		Vector3I min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
		Vector3I max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
		bool any = false;

		for (int y = 0; y < size.Y; y++)
		for (int z = 0; z < size.Z; z++)
		for (int x = 0; x < size.X; x++)
		{
			if (cm.get_block(origin + new Vector3I(x, y, z)) == 0) continue;
			any = true;
			if (x < min.X) min.X = x; if (x > max.X) max.X = x;
			if (y < min.Y) min.Y = y; if (y > max.Y) max.Y = y;
			if (z < min.Z) min.Z = z; if (z > max.Z) max.Z = z;
		}

		if (!any)
		{
			GD.PushWarning($"Structure_Registry: build volume is empty, nothing to save as '{name}'.");
			return false;
		}

		var s = new Structure { Name = name };
		s.Allocate(max - min + Vector3I.One);

		for (int y = 0; y < s.Size.Y; y++)
		for (int z = 0; z < s.Size.Z; z++)
		for (int x = 0; x < s.Size.X; x++)
			s.Set(x, y, z, (byte)cm.get_block(origin + min + new Vector3I(x, y, z)));

		// Bottom-centre: stamping a building means "put its footprint here", not
		// "put its north-west corner here". Editable per-structure in the Inspector after.
		s.Anchor = new Vector3I(s.Size.X / 2, 0, s.Size.Z / 2);

		DirAccess.MakeDirRecursiveAbsolute(SaveDir);
		string path = $"{SaveDir}/{name}.tres";

		var err = ResourceSaver.Save(s, path);
		if (err != Error.Ok)
		{
			GD.PushError($"Structure_Registry: failed to save '{path}' ({err}).");
			return false;
		}

		// TakeOverPath, not `ResourcePath = path`: re-saving a structure means an older
		// instance loaded from this same file is still sitting in the resource cache, and
		// assigning the path directly errors with "Another resource is loaded from path".
		// This claims the cache entry instead, so later loads hand back the version we
		// just wrote rather than the stale one.
		s.TakeOverPath(path);
		_byName[name]  = s;
		_scanned       = true;
		GD.Print($"Structure_Registry: saved '{name}' {s.Size} ({s.BlockCount} blocks) -> {path}");
		return true;
	}

	// ---------------------------------------------------------------- stamping

	// The world-space box a StampByName(name, worldPos) would fill, as {"min", "size"}.
	// Empty dictionary if the structure isn't known.
	//
	// GDScript bridge: it spares callers from redoing the `worldPos - Anchor` math, and a
	// hub scene needs the box anyway to poll Chunk_Manager.is_chunk_ready() before stamping.
	public Godot.Collections.Dictionary GetStampBounds(string name, Vector3I worldPos)
	{
		var s = Get(name);
		if (s == null) return new Godot.Collections.Dictionary();
		return new Godot.Collections.Dictionary
		{
			{ "min", worldPos - s.Anchor },
			{ "size", s.Size },
		};
	}

	// Gameplay/generation entry point: place a saved structure so its Anchor sits on worldPos.
	public bool StampByName(string name, Vector3I worldPos, bool clearAir = false)
	{
		var s  = Get(name);
		var cm = Global.Instance?.CubeManager;
		if (s == null || cm == null) return false;
		s.Stamp(cm, worldPos, clearAir);
		return true;
	}

	// World positions of a structure's marker N, for a stamp that put Anchor on worldPos.
	// Empty when the structure or the marker is missing.
	//
	// This is the lookup that replaces hand-written offsets: a scene asks where the structure
	// says its console is, instead of restating it. GDScript-facing, hence the instance method.
	public Godot.Collections.Array<Vector3I> GetMarkers(string name, int markerNumber, Vector3I worldPos)
	{
		var s = Get(name);
		return s == null ? new Godot.Collections.Array<Vector3I>() : s.FindMarkers(markerNumber, worldPos);
	}

	// Builder entry point: wipe the working volume and lay the structure back down with its
	// min corner at origin — ignoring Anchor, so save -> load is an exact round trip.
	public bool LoadIntoBuildVolume(string name, Vector3I origin, Vector3I size)
	{
		var s  = Get(name);
		var cm = Global.Instance?.CubeManager;
		if (s == null || cm == null) return false;

		ClearVolume(origin, size);

		for (int y = 0; y < s.Size.Y; y++)
		for (int z = 0; z < s.Size.Z; z++)
		for (int x = 0; x < s.Size.X; x++)
		{
			byte id = s.Get(x, y, z);
			if (id == 0) continue;
			if (x >= size.X || y >= size.Y || z >= size.Z) continue; // too big for the volume — clip
			cm.place_block(origin + new Vector3I(x, y, z), id);
		}
		return true;
	}

	public void ClearVolume(Vector3I origin, Vector3I size)
	{
		var cm = Global.Instance?.CubeManager;
		if (cm == null) return;

		for (int y = 0; y < size.Y; y++)
		for (int z = 0; z < size.Z; z++)
		for (int x = 0; x < size.X; x++)
		{
			var p = origin + new Vector3I(x, y, z);
			if (cm.get_block(p) != 0) cm.break_block(p);
		}
	}

	// ---------------------------------------------------------------- helpers

	private static string SanitizeName(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return "";
		var chars = name.Trim()
						.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_')
						.ToArray();
		return new string(chars);
	}
}
