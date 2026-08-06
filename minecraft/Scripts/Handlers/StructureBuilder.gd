extends Node3D

## Root of Scenes/StructureBuilder.tscn — the offline structure authoring tool.
##
## Runs the real Chunk_Manager on a deliberately flat, featureless planet so blocks look
## and mesh exactly as they will in gameplay, then captures a bounded region of that world
## into a Structure resource (res://Structures/*.tres) via Structure_Registry.
##
## This is a dev tool, not part of the run flow — nothing here touches RunManager.

const DEFAULT_SIZE := Vector3i(32, 32, 32)

# Base plate fills y <= Global.SurfaceLevel (0), so the volume starts one block above it.
# Keeping the plate outside the volume means it can never be captured into a structure or
# accidentally dug through.
const VOLUME_FLOOR_Y := 1

# The volume is centred on this column. Matches Global.WorldSpawn's default X/Z, but is a
# local constant on purpose — the builder's world belongs to the builder, and nothing here
# should shift because a gameplay spawn point moved.
const BUILD_CENTER := Vector2i(512, 512)

@onready var _cam: Camera3D = $BuilderCamera
@onready var _volume_box: MeshInstance3D = $VolumeBox

@onready var _block_label: Label = $UI/Readout/BlockLabel
@onready var _coord_label: Label = $UI/Readout/CoordLabel
@onready var _name_edit: LineEdit = $UI/Panel/VBox/NameRow/NameEdit
@onready var _size_x: SpinBox = $UI/Panel/VBox/SizeRow/SizeX
@onready var _size_y: SpinBox = $UI/Panel/VBox/SizeRow/SizeY
@onready var _size_z: SpinBox = $UI/Panel/VBox/SizeRow/SizeZ
@onready var _load_list: OptionButton = $UI/Panel/VBox/LoadRow/LoadList
@onready var _status: Label = $UI/Panel/VBox/StatusLabel
@onready var _quit_confirm: Control = $UI/QuitConfirm
@onready var _quit_no: Button = $UI/QuitConfirm/Center/Box/VBox/Buttons/QuitNo

var _origin := Vector3i.ZERO
var _size := DEFAULT_SIZE
var _ui_mode := false
var _camera_placed := false
var _quit_pending := false


func _enter_tree() -> void:
	# Must beat Chunk_Manager's _ready(): children are ready before their parent, and the
	# generation threads start there. A parent's _enter_tree() runs before any child enters.
	#
	# noise_scale/height_amp of 0 collapse the terrain sampler to a constant, so the world is
	# a perfectly flat plate filled up to Global.SurfaceLevel — no caves, no chasm, no spawn
	# carve. Steel because it reads as a workbench rather than as terrain.
	Global.SetPlanetConfig({
		"template": "Builder",
		"biome": "",
		"fill_solid": false,
		"surface_block": 6,
		"noise_scale": 0.0,
		"height_amp": 0.0,
		"spawn_y": VOLUME_FLOOR_Y,
		"caves_enabled": false,
		"chasm_enabled": false,
		"spawn_clear_enabled": false,
	})


func _ready() -> void:
	_size_x.value = DEFAULT_SIZE.x
	_size_y.value = DEFAULT_SIZE.y
	_size_z.value = DEFAULT_SIZE.z

	_apply_volume(DEFAULT_SIZE)

	_cam.palette_changed.connect(_on_palette_changed)
	_on_palette_changed(_cam.current_block())

	_refresh_structure_list()
	_set_ui_mode(false)
	_status.text = "Tab frees the mouse for the panel."


func _process(_delta: float) -> void:
	var p := _cam.global_position
	_coord_label.text = "x %d  y %d  z %d   |   volume %d x %d x %d @ (%d, %d, %d)" % [
		floori(p.x), floori(p.y), floori(p.z),
		_size.x, _size.y, _size.z,
		_origin.x, _origin.y, _origin.z,
	]


func _unhandled_input(event: InputEvent) -> void:
	if not (event is InputEventKey and event.pressed and not event.echo):
		return

	if event.keycode == KEY_TAB:
		get_viewport().set_input_as_handled()
		# Don't let Tab toggle the panel out from under the quit prompt.
		if not _quit_pending:
			_set_ui_mode(not _ui_mode)

	elif event.keycode == KEY_ESCAPE:
		get_viewport().set_input_as_handled()
		# Escape unwinds one layer at a time rather than leaving outright: quit prompt →
		# panel → ask about quitting. Nothing here is saved automatically, so leaving is
		# always the last step and always deliberate.
		if _quit_pending:
			_close_quit_confirm()
		elif _ui_mode:
			_set_ui_mode(false)
		else:
			_open_quit_confirm()


func _set_ui_mode(on: bool) -> void:
	_ui_mode = on
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE if on else Input.MOUSE_MODE_CAPTURED
	# The camera keeps running its _process either way; this is what stops it consuming
	# movement input and re-capturing the pointer while you're typing a name.
	_cam.editing_enabled = not on
	$UI/Panel.modulate.a = 1.0 if on else 0.45


## ------------------------------------------------------------------ quit prompt

func _open_quit_confirm() -> void:
	_quit_pending = true
	_quit_confirm.visible = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	# BuilderCamera polls Input directly rather than going through UI focus, so without
	# this it would keep flying and placing blocks behind the prompt.
	_cam.editing_enabled = false
	# Default to the harmless option — a stray Enter shouldn't discard the build.
	_quit_no.grab_focus()


func _close_quit_confirm() -> void:
	_quit_pending = false
	_quit_confirm.visible = false
	# Restores mouse capture and camera control for whichever mode we came from.
	_set_ui_mode(_ui_mode)


func _on_quit_no_pressed() -> void:
	_close_quit_confirm()


func _on_quit_yes_pressed() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	get_tree().change_scene_to_file("res://Scenes/MainMenu.tscn")


## ------------------------------------------------------------------ build volume

func _apply_volume(size: Vector3i) -> void:
	_size = size
	# Kept centred on BUILD_CENTER through every resize, so growing the volume expands it
	# around what you've already built instead of sliding it off one corner.
	# Integer division is deliberate — the origin is a block coordinate.
	@warning_ignore("integer_division")
	_origin = Vector3i(BUILD_CENTER.x - size.x / 2, VOLUME_FLOOR_Y, BUILD_CENTER.y - size.z / 2)

	_cam.build_origin = _origin
	_cam.build_size = _size

	if not _camera_placed:
		_camera_placed = true
		_cam.global_position = Vector3(
			BUILD_CENTER.x, VOLUME_FLOOR_Y + 6, BUILD_CENTER.y + size.z * 0.5 + 10.0)
		_cam.look_at(Vector3(BUILD_CENTER.x, VOLUME_FLOOR_Y + size.y * 0.35, BUILD_CENTER.y))

	_rebuild_volume_box()


## Wireframe cage marking exactly what Save will capture. Unshaded and depth-test-free so
## it stays readable from inside a finished build.
func _rebuild_volume_box() -> void:
	var mesh := ImmediateMesh.new()
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = Color("3bdce6")
	mat.no_depth_test = true
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA

	var o := Vector3(_origin)
	var s := Vector3(_size)
	var corners: Array[Vector3] = [
		o + Vector3(0, 0, 0), o + Vector3(s.x, 0, 0), o + Vector3(s.x, 0, s.z), o + Vector3(0, 0, s.z),
		o + Vector3(0, s.y, 0), o + Vector3(s.x, s.y, 0), o + Vector3(s.x, s.y, s.z), o + Vector3(0, s.y, s.z),
	]
	var edges := [
		[0, 1], [1, 2], [2, 3], [3, 0],
		[4, 5], [5, 6], [6, 7], [7, 4],
		[0, 4], [1, 5], [2, 6], [3, 7],
	]

	mesh.surface_begin(Mesh.PRIMITIVE_LINES, mat)
	for e in edges:
		mesh.surface_add_vertex(corners[e[0]])
		mesh.surface_add_vertex(corners[e[1]])
	mesh.surface_end()

	_volume_box.mesh = mesh


## ------------------------------------------------------------------ UI callbacks

func _on_palette_changed(block_id: int) -> void:
	_block_label.text = "[%d] %s" % [block_id, Block_Registry.GetBlockName(block_id)]


func _on_apply_size_pressed() -> void:
	_apply_volume(Vector3i(int(_size_x.value), int(_size_y.value), int(_size_z.value)))
	_status.text = "Volume resized. Blocks outside it are no longer captured."


func _on_save_pressed() -> void:
	var n := _name_edit.text.strip_edges()
	if n.is_empty():
		_status.text = "Give the structure a name first."
		return

	if Structure_Registry.CaptureAndSave(n, _origin, _size):
		_status.text = "Saved '%s' to %s/%s.tres" % [n, Structure_Registry.GetSaveDir(), n]
		_refresh_structure_list()
	else:
		_status.text = "Save failed — is the volume empty? (check the output log)"


func _on_load_pressed() -> void:
	if _load_list.selected < 0:
		_status.text = "No structure selected."
		return
	var n := _load_list.get_item_text(_load_list.selected)
	if Structure_Registry.LoadIntoBuildVolume(n, _origin, _size):
		_name_edit.text = n
		_status.text = "Loaded '%s' into the volume." % n
	else:
		_status.text = "Could not load '%s'." % n


func _on_clear_pressed() -> void:
	Structure_Registry.ClearVolume(_origin, _size)
	_status.text = "Volume cleared."


func _on_refresh_pressed() -> void:
	Structure_Registry.ReloadStructures()
	_refresh_structure_list()
	_status.text = "Structure list refreshed."


func _refresh_structure_list() -> void:
	var previous := ""
	if _load_list.selected >= 0:
		previous = _load_list.get_item_text(_load_list.selected)

	_load_list.clear()
	var names: Array = Structure_Registry.GetStructureNames()
	for i in names.size():
		_load_list.add_item(names[i])
		if names[i] == previous:
			_load_list.select(i)
