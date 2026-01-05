extends CharacterBody3D

@export var camera: Camera3D
@export var collision_shape: CollisionShape3D
@export var inventory: Node3D
@export var isMultiplayer = false
@export var is_controller = null
@export var is_real_controller = null
@export var InputSynchronizer: MultiplayerSynchronizer

@export var speed = 5.0  # Movement speed
@export var sprint_mult = 2.0  # Sprint Multiplier
@export var accel = 5.0
@export var jump_strength = 10.0  # Jump force
@export var gravity: float = 9.8

@export var pitch: float = 0.0
@export var yaw: float = 0.0

var selected_cube
var selected_cube_position
var is_sprinting = false
var mouse_visible = false
var spectator_mode: bool = false
var yeah = false

var forward_direction: Vector3 = Vector3.ZERO
var right_direction: Vector3 = Vector3.ZERO
var vel = Vector3.ZERO  # Current vel
var direction = Vector3.ZERO  # Movement direction

@export var player_id = 1:
	set(id):
		print(id)
		player_id = id
		InputSynchronizer.set_multiplayer_authority(id)

# Called when the node enters the scene tree for the first time.
func _ready():
	
	if player_id == multiplayer.get_unique_id():
		$CanvasLayer2.show()
		Global.Player = self
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
func _physics_process(delta):
	#print(str(%InputSynchronizer.get_multiplayer_authority()) + " " + str(pd.player_id))
	#if(pd.InputSynchronizer.get_multiplayer_authority() == pd.player_id): 
		#print(pd.player_id)
		#print(%InputSynchronizer.get_multiplayer_authority())
		#pd.camera.current = true
		#scale = Vector3(.5,1,.5)
	rotate_camera()
	_apply_movement_from_input(delta)
	#else: 
		#pd.camera.current = false
		#scale = Vector3(1,1,1)
		
	
	
	pass

func _apply_movement_from_input(delta):
	if Input.is_action_just_pressed("toggle_mouse"):
		toggle_mouse_visibility()
	direction = Vector3.ZERO
	var temp_speed = speed
	var max = speed
	if InputSynchronizer.is_sprinting:
		toggle_player_sprint(true)
	
	if InputSynchronizer.spectator_mode:
		InputSynchronizer.spectator_mode = false
		toggle_spectator()
		
	update_facing_directions()
	if InputSynchronizer.directions[0]:
		#print(multiplayer.get_unique_id())
		direction += forward_direction
	else: toggle_player_sprint(false)
		
	if InputSynchronizer.directions[1]:
		direction -= forward_direction
		toggle_player_sprint(false)
	if InputSynchronizer.directions[2]:
		direction -= right_direction
	if InputSynchronizer.directions[3]:
		direction += right_direction
	
	if is_sprinting: 
		temp_speed *= sprint_mult
		max *= sprint_mult 
	var fricMult = Global.GroundFriction
	if !is_on_floor(): 
		temp_speed /= 8
		fricMult = Global.AirFriction
	
	direction = direction.normalized() * temp_speed
	
	velocity = Vector3(velocity.x*fricMult, velocity.y, velocity.z*fricMult)
	
	velocity.x += direction.x*delta*accel
	velocity.z += direction.z*delta*accel
	
	
	
	velocity = Vector3(velocity.x, 0, velocity.z).limit_length(max) + Vector3(0, velocity.y, 0)
	
	if(!spectator_mode): velocity.y -= gravity * delta
	else: velocity.y = 0
	
	
	if (is_on_floor() || spectator_mode) and InputSynchronizer.is_jumping:
		velocity.y = jump_strength
		InputSynchronizer.is_jumping = false
		
	if (InputSynchronizer.is_crouching):
		if(spectator_mode): velocity.y = -jump_strength
	
	move_and_slide()
		
	pass
			


func rotate_camera():
	# Rotate the player body (yaw) and camera (pitch)
	camera.rotation_degrees.y = InputSynchronizer.yaw  # Horizontal rotation (around the Y-axis)
	camera.rotation_degrees.x = InputSynchronizer.pitch  # Vertical rotation (around the X-axis)

func update_facing_directions():
	# Update the forward and right directions based on the camera's rotation
	var rot = camera.rotation_degrees.y
	forward_direction = Vector3.FORWARD.rotated(Vector3.UP, deg_to_rad(rot))
	right_direction = Vector3.RIGHT.rotated(Vector3.UP, deg_to_rad(rot))

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
