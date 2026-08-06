extends Node3D

## Root of Scenes/Ship.tscn — the between-runs hub.
##
## The ship is not modelled geometry. It is the "Ship" structure authored in
## Scenes/StructureBuilder.tscn, stamped into an otherwise empty voxel world, so it
## collides, meshes, lights and streams exactly like terrain does. Changing the ship means
## rebuilding it in the builder and pressing Save — nothing here needs to know its shape.
##
## Mission control opens SolarSelect as an OVERLAY rather than a scene change, so backing
## out puts you back on the ship instead of dumping you at the main menu.

const SHIP_STRUCTURE := "Ship"
const SOLAR_SELECT := preload("res://Scenes/SolarSelect.tscn")

# Where the ship's anchor (bottom-centre) lands. X/Z match Global.WorldSpawn's default
# column; Y is arbitrary in a void world, since nothing else occupies any altitude.
const SHIP_ORIGIN := Vector3i(512, 8, 512)

# Blocks above the ship's floor to drop the player in at. 1 would put their feet exactly on the
# deck; the extra block gives a short settle onto it instead of spawning flush with the surface.
const SPAWN_HEIGHT := 2

# Mirrors Global.CHUNK_SIZE, which is a C# const — statics don't cross to GDScript.
const CHUNK_SIZE := 48

# If the world never shows up, say so instead of hanging on a silent await forever.
const STAMP_TIMEOUT := 10.0

## Which numbered marker in the Ship structure is mission control. Place a Marker1 block in
## the builder where you want the console and it moves with the ship — nothing here needs
## editing. Markers never render in the stamped world; Structure.Stamp skips them.
@export var console_marker := 1

## How close you have to stand before mission control can be used.
@export var console_radius := 3.0

## Whatever should sit on the console — drag any Node3D in here: a light, a mesh, a particle
## effect, a whole instanced scene. It gets moved onto the marker and shown once the marker is
## found, so leave it wherever is convenient in the editor; its authored position is ignored.
##
## Hidden on load and only revealed when the marker resolves, so it can't sit glowing in the
## wrong place when the structure has no Marker1. Optional — leave it empty for no visual.
@export var console_visual: Node3D

@onready var _prompt: Label = $UI/Prompt
@onready var _status: Label = $UI/Status

var _console_pos := Vector3.ZERO
var _console_found := false
var _player: Node3D = null
var _select_instance: Control = null
var _mouse_mode_before := Input.MOUSE_MODE_CAPTURED
var _in_range := false


func _enter_tree() -> void:
	# Must beat Chunk_Manager's _ready(): children are ready before their parent, and the
	# generation threads start there. A parent's _enter_tree() runs before any child enters.
	#
	# void_world skips terrain generation entirely, so the only solid blocks in this world
	# are the ship's own. Every other field is left at a harmless default because nothing
	# downstream of VoidWorld reads them.
	Global.SetPlanetConfig({
		"template": "Ship",
		"biome": "",
		"void_world": true,
		"spawn_y": SHIP_ORIGIN.y + SPAWN_HEIGHT,
		"caves_enabled": false,
		"chasm_enabled": false,
		"spawn_clear_enabled": false,
	})


func _ready() -> void:
	# Chunk streaming follows Global.GetPlayerPos(), and the player cannot exist yet — it
	# has to wait for a floor to stand on. Without an anchor the world would generate around
	# (0, 0, 0), the ship's own chunks would never arrive, and the stamp below would wait
	# forever. Player takes priority again the moment it spawns.
	$StreamAnchor.global_position = Vector3(SHIP_ORIGIN)
	Global.StreamingAnchor = $StreamAnchor

	_prompt.visible = false
	# Hidden here rather than in the scene so a hand-dragged node doesn't have to remember to
	# be invisible — the script owns when the console appears.
	if console_visual != null:
		console_visual.visible = false

	if await _stamp_ship():
		_locate_console()
		_spawn_player()


func _exit_tree() -> void:
	# Don't leave a later gameplay scene pointing at a freed node.
	if Global.StreamingAnchor == $StreamAnchor:
		Global.StreamingAnchor = null


func _process(_delta: float) -> void:
	# A plain distance test rather than an Area3D: there is one interaction point in the
	# whole scene, and this needs no collision layers, no shape resource and no mask that
	# could silently stop matching if the player's layers change.
	if _player == null or not _console_found or _select_instance != null:
		return

	var near := _player.global_position.distance_to(_console_pos) <= console_radius
	if near != _in_range:
		_in_range = near
		_prompt.visible = near


func _unhandled_input(event: InputEvent) -> void:
	if _in_range and _select_instance == null and event.is_action_pressed("interact"):
		get_viewport().set_input_as_handled()
		_open_mission_control()


## ------------------------------------------------------------------ world setup

func _stamp_ship() -> bool:
	var bounds: Dictionary = Structure_Registry.GetStampBounds(SHIP_STRUCTURE, SHIP_ORIGIN)
	if bounds.is_empty():
		_fail("No structure named '%s'. Build one in the Builder and save it under that name."
			% SHIP_STRUCTURE)
		return false

	# Chunks are created on a timer in Chunk_Manager._Process, not in _Ready, and set_block
	# silently no-ops on a chunk that doesn't exist — so stamping on the first frame writes
	# nothing at all. Poll the footprint instead of sleeping a fixed number of frames, which
	# would be a race that only loses on a slow machine.
	var elapsed := 0.0
	while not _footprint_ready(bounds["min"], bounds["size"]):
		await get_tree().process_frame
		elapsed += get_process_delta_time()
		if elapsed > STAMP_TIMEOUT:
			_fail("Timed out waiting for the world around the ship to generate.")
			return false

	# clearAir stays false: in a void world every cell is already air, so carving the
	# bounding box first would just be redundant writes.
	if not Structure_Registry.StampByName(SHIP_STRUCTURE, SHIP_ORIGIN, false):
		_fail("Failed to stamp '%s'." % SHIP_STRUCTURE)
		return false

	_status.visible = false
	return true


## Ask the structure where its console is. Deliberately no fallback position: a missing
## marker is an authoring mistake, and quietly interacting with the wrong empty spot is
## exactly the silent drift markers exist to prevent.
func _locate_console() -> void:
	var found: Array = Structure_Registry.GetMarkers(SHIP_STRUCTURE, console_marker, SHIP_ORIGIN)
	if found.is_empty():
		# Not fatal — the ship is still walkable, so say what's wrong and let it be explored.
		# _console_found stays false, which is what keeps _process off a zeroed position.
		var msg := "The '%s' structure has no Marker%d — place one in the Builder where mission control should be." \
			% [SHIP_STRUCTURE, console_marker]
		push_error("Ship: " + msg)
		_status.text = msg
		_status.visible = true
		return

	if found.size() > 1:
		push_warning("Ship: %d Marker%d blocks in '%s'; using the first."
			% [found.size(), console_marker, SHIP_STRUCTURE])

	# Centre of the marker's cell — block coords address a corner, and the prompt radius
	# should measure from the middle of the console, not its near edge.
	_console_pos = Vector3(found[0]) + Vector3(0.5, 0.5, 0.5)
	_console_found = true

	if console_visual != null:
		console_visual.global_position = _console_pos
		console_visual.visible = true


func _footprint_ready(mn: Vector3i, sz: Vector3i) -> bool:
	var cm = Global.CubeManager
	if cm == null:
		return false

	var mx := mn + sz - Vector3i.ONE
	for x in _axis_samples(mn.x, mx.x):
		for y in _axis_samples(mn.y, mx.y):
			for z in _axis_samples(mn.z, mx.z):
				if not cm.is_chunk_ready(Vector3i(x, y, z)):
					return false
	return true


## One sample per chunk the span crosses. is_chunk_ready answers a per-chunk question, so
## sampling finer than CHUNK_SIZE only repeats work; the far end is always included so a
## span that stops just inside the next chunk still gets checked.
func _axis_samples(lo: int, hi: int) -> Array[int]:
	var out: Array[int] = []
	var v := lo
	while v < hi:
		out.append(v)
		v += CHUNK_SIZE
	out.append(hi)
	return out


func _spawn_player() -> void:
	# Deliberately after the stamp: the player has gravity and the world is empty until the
	# ship exists, so spawning first drops them through the floor into an endless void.
	var character = preload("res://Assets/character.tscn").instantiate()
	character.name = "1"
	character.position = Vector3(SHIP_ORIGIN) + Vector3(0.5, SPAWN_HEIGHT, 0.5)
	$Game/Players.add_child(character, true)
	_player = character


func _fail(msg: String) -> void:
	push_error("Ship: " + msg)
	_status.text = msg
	_status.visible = true
	# Without a ship there is no floor to stand on, so leave the mouse free rather than
	# capturing it for a camera that has nothing to look at.
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE


## ------------------------------------------------------------------ mission control

func _open_mission_control() -> void:
	_mouse_mode_before = Input.mouse_mode
	_prompt.visible = false
	_in_range = false

	# Its own CanvasLayer so the select screen sits above the player HUD, which
	# character.tscn brings along on its own CanvasLayer. Same pattern level.gd uses for
	# the solar map.
	var layer := CanvasLayer.new()
	layer.layer = 100
	layer.process_mode = Node.PROCESS_MODE_ALWAYS
	add_child(layer)

	var select := SOLAR_SELECT.instantiate()
	# Must be set before it enters the tree — its _ready() branches on it.
	select.overlay_mode = true
	select.select_closed.connect(_on_mission_control_closed.bind(layer))
	layer.add_child(select)
	_select_instance = select


func _on_mission_control_closed(layer: CanvasLayer) -> void:
	_select_instance = null
	# Only the opener knows the mouse was captured for camera look before this.
	Input.mouse_mode = _mouse_mode_before
	layer.queue_free()
