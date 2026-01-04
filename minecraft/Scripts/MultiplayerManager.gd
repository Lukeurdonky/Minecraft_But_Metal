extends Node3D

const SERVER_PORT = 8080
const SERVER_IP = "127.0.0.1"

var multiplayer_scene = preload("res://Assets/multiplayer_character.tscn")

@export var _players_spawn_node: Node

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	
	pass

func become_host():
	print("become host")
	
	_players_spawn_node = get_tree().get_current_scene().get_node("Players")
	
	var server_peer = ENetMultiplayerPeer.new()
	server_peer.create_server(SERVER_PORT, 4)
	
	multiplayer.multiplayer_peer = server_peer
	
	multiplayer.peer_connected.connect(_add_player_to_game)
	#multiplayer.peer_connected.connect(_remove_single_player)
	multiplayer.peer_disconnected.connect(_del_player)
	
	#rpc("_remove_single_player")
	
	_remove_single_player()
	#_del_player(1)
	#_add_player_to_game(1)
	Global.Player = _add_player_to_game(1)
	
func join_as_player():
	print("join as player")
	var client_peer = ENetMultiplayerPeer.new()
	client_peer.create_client(SERVER_IP, SERVER_PORT)
	
	multiplayer.multiplayer_peer = client_peer
	#Global.Player = _add_player_to_game(2)
	_remove_single_player()
	#multiplayer.peer_connected.connect(_on_peer_connected)
	
	# Ensure no duplicate spawns
	#if not get_players_spawn_node().has_node(str(multiplayer.get_unique_id())):
	#_add_player_to_game(multiplayer.get_unique_id())

	
func _add_player_to_game(id: int) -> Node3D:
	print("Player %s joined the game!" % id)
	
	var player_to_add = multiplayer_scene.instantiate()
	#player_to_add.camera.visible = true
	
	player_to_add.player_id = id
	player_to_add.name = str(id)
	var player_name = str(id)
	
	
	#if get_players_spawn_node().has_node(player_name):
		#print("Error: Player node with ID %s already exists!" % id)
		#return get_players_spawn_node().get_node(player_name)  # Return the existing node if it exists
		
	_players_spawn_node.add_child(player_to_add, true)
	
	#_remove_single_player(Global.Player)
	#Global.Player.queue_free()
	if multiplayer.is_server():
		#print("ccc")
		for peer_id in multiplayer.get_peers():
			if peer_id != multiplayer.get_unique_id():  # Don't call the RPC on the server
				#print(peer_id)
				rpc_id(peer_id, "_set_new_global", player_to_add.name)
	#print(Global.Player)
	
	return player_to_add

func _del_player(id: int):
	print("Player %s left the game!" % id)
	
	if not _players_spawn_node.has_node(str(id)):
		return
	_players_spawn_node.get_node(str(id)).queue_free()
	

func _remove_single_player():
	print("Removing single player on peer %s" % multiplayer.get_unique_id())
	var players_node = get_tree().get_current_scene().get_node("Players")
	if players_node and players_node.has_node("Character"):
		var pos = players_node.get_node("Character").character_body.global_transform.origin
		var player_to_remove = players_node.get_node("Character")
		player_to_remove.queue_free()
		print("Character removed.")
	else:
		print("Character not found or already removed.")
	
@rpc
func _set_new_global(player_name: String):
	var player_to_add = get_tree().get_current_scene().get_node("Players").get_node(player_name)
	#print("new global: " + player_name)
	if player_to_add:
		print("Setting Global.Player to %s on the client" % player_to_add.name)
		player_to_add.set_author(player_to_add.player_id)
		Global.Player = player_to_add
	else:
		print("Player not found!")
	
	


























#func become_host():
	#print("become host")
	#
	#_players_spawn_node = get_players_spawn_node()
	#
	#var server_peer = ENetMultiplayerPeer.new()
	#server_peer.create_server(SERVER_PORT, 4)
	#
	#multiplayer.multiplayer_peer = server_peer
	#
	#multiplayer.peer_connected.connect(_add_player_to_game)
	##multiplayer.peer_connected.connect(_remove_single_player)
	#multiplayer.peer_disconnected.connect(_del_player)
	#
	##rpc("_remove_single_player")
	#
	#_remove_single_player()
	##_del_player(1)
	#_add_player_to_game(1)
	##Global.Player = _add_player_to_game(1)
	#
#func join_as_player():
	#print("join as player")
	#var client_peer = ENetMultiplayerPeer.new()
	#client_peer.create_client(SERVER_IP, SERVER_PORT)
	#
	#multiplayer.multiplayer_peer = client_peer
	##Global.Player = _add_player_to_game(2)
	##_remove_single_player()
	#multiplayer.peer_connected.connect(_on_peer_connected)
	#
	## Ensure no duplicate spawns
	##if not get_players_spawn_node().has_node(str(multiplayer.get_unique_id())):
	##_add_player_to_game(multiplayer.get_unique_id())
#
#func _add_player_to_game(id: int) -> Node3D:
	#print("Player %s joined the game!" % id)
	#
	#var player_to_add = multiplayer_scene.instantiate()
	#player_to_add.player_id = id
	#player_to_add.name = str(id)
	#var player_name = str(id)
	#
	##if get_players_spawn_node().has_node(player_name):
		##print("Error: Player node with ID %s already exists!" % id)
		##return get_players_spawn_node().get_node(player_name)  # Return the existing node if it exists
		#
	#get_players_spawn_node().add_child(player_to_add, true)
	#
	##_remove_single_player(Global.Player)
	##Global.Player.queue_free()
	#Global.Player = player_to_add
	##print(Global.Player)
	#
	#return player_to_add
	#
#func _del_player(id: int):
	#print("Player %s left the game!" % id)
	#
	#if not _players_spawn_node.has_node(str(id)):
		#return
	#_players_spawn_node.get_node(str(id)).queue_free()
	#
##@rpc("any_peer")
#func _remove_single_player():
	#print("Removing single player on peer %s" % multiplayer.get_unique_id())
	#var players_node = get_tree().get_current_scene().get_node("Players")
	#if players_node and players_node.has_node("Character"):
		#var pos = players_node.get_node("Character").character_body.global_transform.origin
		#var player_to_remove = players_node.get_node("Character")
		#player_to_remove.queue_free()
		#print("Character removed.")
	#else:
		#print("Character not found or already removed.")
	#
	#
#func _on_peer_connected(id: int):
	#print("Peer %s connected" % id)
	## Only the host should handle adding players for other peers
	#if multiplayer.is_server():
		#print("Adding player for connected peer %s" % id)
		#_add_player_to_game(id)
	#_remove_single_player()
#
#func get_players_spawn_node() -> Node:
	#var players_node = get_tree().get_current_scene().get_node("Players")
	#if not players_node:
		#print("Error: Players node not found in the current scene!")
	#return players_node
