extends Node3D

@export var camera: Camera3D
@export var character_body: CharacterBody3D
@export var collision_shape: CollisionShape3D
@export var inventory: Node3D
@export var isMultiplayer = false
@export var is_controller = null
@export var is_real_controller = null
@export var InputSynchronizer: MultiplayerSynchronizer
var selected_cube

var is_sprinting = false
var mouse_visible = false
var spectator_mode: bool = false
var yeah = false

@export var player_id = 1:
	set(id):
		player_id = id
		%InputSynchronizer.set_multiplayer_authority(id)
		#if(multiplayer != null): 
		
		#%InputSynchronizer.set_multiplayer_authority(player_id)
		#if is_inside_tree():
			#print("inside")
			#if is_multiplayer_authority():
		#print("SET: " + str(id))
		
#@onready var input = %InputSynchronizer

# Called when the node enters the scene tree for the first time.
func _ready():
	if player_id == multiplayer.get_unique_id():
		camera.current = true
	#if multiplayer:
		#if multiplayer.get_unique_id() == player_id: 
			#print(multiplayer.get_unique_id())
			#set_author(player_id)
	#print(camera.current)
	#if(!isMultiplayer): 
		#print("off")
		#camera.current = false
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	#if(isMultiplayer):
		#set_author(multiplayer.get_unique_id())
		#print(str(InputSynchronizer.get_multiplayer_authority()) + " " + str(multiplayer.get_unique_id()))
		#if multiplayer.get_unique_id() == player_id: 
			#is_controller = true
			#if(player_id == multiplayer.get_unique_id()): 
				##print("prev auth: " + str($InputSynchronizer.get_multiplayer_authority()))
				#set_author(player_id)
				##print("AAAAAAAAAAAAAAAA")
				##print("new auth: " + str($InputSynchronizer.get_multiplayer_authority()))
		#else: 
			#is_controller = false
			#set_author(-1)
		#
		#if %InputSynchronizer.get_multiplayer_authority() == player_id:
			#is_real_controller = true
		#else: is_real_controller = false
	#if(isMultiplayer): print(str(player_id) + ": " + str(%InputSynchronizer.get_multiplayer_authority()))
	pass

func toggle_spectator():
	spectator_mode = !spectator_mode
	if(spectator_mode):
		collision_shape.disabled = true
	else:
		collision_shape.disabled = false
	
func toggle_player_sprint(flag: bool):
	is_sprinting = flag

func toggle_mouse_visibility():
	if(Input.mouse_mode != Input.MOUSE_MODE_VISIBLE): 
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		mouse_visible = true
	else: 
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
		mouse_visible = false
		
#func set_author(my_id):
	#InputSynchronizer.set_multiplayer_authority(my_id)
