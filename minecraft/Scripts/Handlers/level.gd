extends Node3D

const SPAWN_RANDOM := 5.0

# The solar map is checkable at any time mid-run. It's instanced on demand rather
# than living in the scene so there's exactly one construction path for it, and so a
# CubeLand opened without an active system (F6, debug menu) costs nothing.
const SOLAR_MAP := preload("res://Scenes/SolarMap.tscn")

var _map_instance: Control = null
var _mouse_mode_before_map := Input.MOUSE_MODE_CAPTURED

func _ready():
	add_player(1)
	#print("real")
	# We only need to spawn players on the server.
	#if not multiplayer.is_server():
		#return
#
	#multiplayer.peer_connected.connect(add_player)
	#multiplayer.peer_disconnected.connect(del_player)
#
	## Spawn already connected players
	#for id in multiplayer.get_peers():
		#add_player(id)
#
	## Spawn the local player unless this is a dedicated server export.
	#if not OS.has_feature("dedicated_server"):
		#add_player(1)


func _unhandled_input(event: InputEvent) -> void:
	# Closing is the map's own job (it also handles Esc), so this only ever opens.
	if _map_instance == null and event.is_action_pressed("toggle_map"):
		get_viewport().set_input_as_handled()
		_open_solar_map()


func _open_solar_map() -> void:
	_mouse_mode_before_map = Input.mouse_mode

	# Its own CanvasLayer so the map sits above the player HUD regardless of what
	# CubeLand's 2D tree looks like.
	var layer := CanvasLayer.new()
	layer.layer = 100
	layer.process_mode = Node.PROCESS_MODE_ALWAYS
	add_child(layer)

	var map := SOLAR_MAP.instantiate()
	# Must be set before the map enters the tree — its _ready() branches on it.
	map.overlay_mode = true
	map.map_closed.connect(_on_solar_map_closed.bind(layer))
	layer.add_child(map)
	_map_instance = map


func _on_solar_map_closed(layer: CanvasLayer) -> void:
	_map_instance = null
	# Only the opener knows whether the mouse was captured for camera look before.
	Input.mouse_mode = _mouse_mode_before_map
	layer.queue_free()


func _exit_tree():
	if not multiplayer.is_server():
		return
	# _ready()'s matching connect() calls are commented out for single-player, so these
	# are normally not connected — disconnecting unconditionally threw on every exit
	# from CubeLand. Guarded rather than removed so re-enabling multiplayer still cleans up.
	if multiplayer.peer_connected.is_connected(add_player):
		multiplayer.peer_connected.disconnect(add_player)
	if multiplayer.peer_disconnected.is_connected(del_player):
		multiplayer.peer_disconnected.disconnect(del_player)


func add_player(id: int):
	var character = preload("res://Assets/character.tscn").instantiate()
	# Set player id.
	#character.player_id = id
	# Randomize character position.
	#var pos := Vector2.from_angle(randf() * 2 * PI)
	#character.position = Vector3(pos.x * SPAWN_RANDOM * randf(), 0, pos.y * SPAWN_RANDOM * randf())
	character.name = str(id)
	character.position = Global.WorldSpawn
	$Game/Players.add_child(character, true)
	#print(Global.Player)
	#Global.Player = character
	#print(Global.Player)


func del_player(id: int):
	if not $Game/Players.has_node(str(id)):
		return
	$Game/Players.get_node(str(id)).queue_free()
