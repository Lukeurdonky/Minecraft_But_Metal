extends Node

const PORT = 4433

@onready var spawner_node_path = "MultiplayerSpawner"

func _ready():
	#print("awersome")
	# Start paused
	get_tree().paused = true
	# You can save bandwith by disabling server relay and peer notifications.
	multiplayer.server_relay = false

	# Automatically start the server in headless mode.
	if DisplayServer.get_name() == "headless":
		print("Automatically starting dedicated server")
		_on_host_pressed.call_deferred()
	#print("awersome2")

func _on_host_pressed():
	#print("s")
	# Start as server
	var peer = ENetMultiplayerPeer.new()
	peer.create_server(PORT)
	if peer.get_connection_status() == MultiplayerPeer.CONNECTION_DISCONNECTED:
		OS.alert("Failed to start multiplayer server")
		return
	multiplayer.multiplayer_peer = peer
	start_game()


func _on_connect_pressed():
	#print("c")
	# Start as client
	var txt : String = $UI/Net/Options/Remote.text
	if txt == "":
		OS.alert("Need a remote to connect to.")
		return
	var peer = ENetMultiplayerPeer.new()
	#print(txt)
	peer.create_client(txt, PORT)
	if peer.get_connection_status() == MultiplayerPeer.CONNECTION_DISCONNECTED:
		OS.alert("Failed to start multiplayer client")
		return
	multiplayer.multiplayer_peer = peer
	start_game()

func start_game():
	# Hide the UI and unpause to start the game.
	#print("START")
	$UI.hide()
	get_tree().paused = false
	# Only change level on the server.
	# Clients will instantiate the level via the spawner.
	if multiplayer.is_server():
		#print("START2")
		change_level.call_deferred(load("res://Scenes/CubeLand.tscn"))


func change_level(scene: PackedScene):
	var level = $Level
	for c in level.get_children():
		level.remove_child(c)
		c.queue_free()

	var instance = scene.instantiate()
	level.add_child(instance)

	# Ensure the MultiplayerSpawner node exists
	if not instance.has_node(spawner_node_path):
		print("ERROR: MultiplayerSpawner node is missing!")
		return

	# Notify clients that the server is ready
	#multiplayer.set_rpc_mode("notify_scene_ready", "remote")
	#rpc("notify_scene_ready")

# Client: Wait for the server's confirmation
@rpc("call_remote", "reliable")
func notify_scene_ready():
	print("Server initialized the scene. Game starting!")
	get_tree().paused = false

# The server can restart the level by pressing HOME.
func _input(event):
	#print("input")
	if not multiplayer.is_server():
		return
	if event.is_action("ui_home") and Input.is_action_just_pressed("ui_home"):
		change_level.call_deferred(load("res://Scenes/CubeLand.tscn"))
