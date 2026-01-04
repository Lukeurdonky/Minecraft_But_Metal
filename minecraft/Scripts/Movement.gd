#extends RigidBody3D
extends CharacterBody3D

@export var pd: Node3D

@export var speed = 5.0  # Movement speed
@export var sprint_mult = 2.0  # Sprint Multiplier
@export var accel = 5.0
@export var jump_strength = 10.0  # Jump force
@export var gravity: float = 9.8




var forward_direction: Vector3 = Vector3.ZERO
var right_direction: Vector3 = Vector3.ZERO
var vel = Vector3.ZERO  # Current vel
var direction = Vector3.ZERO  # Movement direction

@onready var camera = $Camera3D

# Store the current pitch and yaw of the camera
var pitch: float = 0.0
var yaw: float = 0.0

# The original mouse position
var original_mouse_pos: Vector2

func _ready():
	# Hide and lock the mouse cursor 
	#Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	#RigidBody3D.mode = Mode.Rigid
	original_mouse_pos = get_viewport().get_mouse_position()
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	Global.Player = get_parent()

func _physics_process(delta):
	# Get the mouse movement delta
	#var mouse_delta = get_viewport().get_mouse_position() - original_mouse_pos
	##print(Global.Player.global_transform.origin)
	## Update the yaw and pitch based on the mouse movement
	#yaw -= mouse_delta.x * sensitivity_x
	#pitch -= mouse_delta.y * sensitivity_y
#
	## Limit the pitch to avoid flipping the camera
	#pitch = clamp(pitch, min_pitch, max_pitch)
#
	## Rotate the camera around the player based on the yaw
	#rotation_degrees.y = yaw
#
	## Rotate the camera up/down based on the pitch
	#camera.rotation_degrees.x = pitch

	# Update the original mouse position
	#original_mouse_pos = get_viewport().get_mouse_position()
	# Get input for movement
	#velocity = Vector3.ZERO
	if Input.is_action_just_pressed("toggle_mouse"):
		pd.toggle_mouse_visibility()
	direction = Vector3.ZERO
	var temp_speed = speed
	var max = speed
	if Input.is_action_pressed("sprint"):
		pd.toggle_player_sprint(true)
	
	if Input.is_action_just_pressed("toggle_spectator"):
		pd.toggle_spectator()
	#if Input.is_action_pressed("move_forward"):
		#direction.z -= 1
	#if Input.is_action_pressed("move_back")w:
		#direction.z += 1
	#if Input.is_action_pressed("move_left"):
		#direction.x -= 1
	#if Input.is_action_pressed("move_right"):
		#direction.x += 1
	update_facing_directions()
	if Input.is_action_pressed("move_forward"):
		direction += forward_direction
	else: pd.toggle_player_sprint(false)
		
	if Input.is_action_pressed("move_back"):
		direction -= forward_direction
		pd.toggle_player_sprint(false)
	if Input.is_action_pressed("move_left"):
		direction -= right_direction
	if Input.is_action_pressed("move_right"):
		direction += right_direction
	
	if pd.is_sprinting: 
		temp_speed *= sprint_mult
		max *= sprint_mult 
	var fricMult = Global.ground_friction
	if !is_on_floor(): 
		temp_speed /= 8
		fricMult = Global.air_friction
	
	direction = direction.normalized() * temp_speed
	
	velocity = Vector3(velocity.x*fricMult, velocity.y, velocity.z*fricMult)
	
	velocity.x += direction.x*delta*accel
	velocity.z += direction.z*delta*accel
	
	
	
	velocity = Vector3(velocity.x, 0, velocity.z).limit_length(max) + Vector3(0, velocity.y, 0)
	
	if(!pd.spectator_mode): velocity.y -= gravity * delta
	else: velocity.y = 0
	
	
	if (is_on_floor() || pd.spectator_mode) and Input.is_action_pressed("jump"):
		velocity.y = jump_strength
		
	if (Input.is_action_pressed("crouch")):
		if(pd.spectator_mode): velocity.y = -jump_strength
	
	move_and_slide()
	#update_facing_directions()
	#if Input.is_action_pressed("move_forward"):
		#direction += forward_direction
	#if Input.is_action_pressed("move_back"):
		#direction -= forward_direction
	#if Input.is_action_pressed("move_left"):
		#direction -= right_direction
	#if Input.is_action_pressed("move_right"):
		#direction += right_direction
#
	## Normalize to avoid faster diagonal movement
	#direction = direction.normalized()
#
	## Apply movement
	#linear_velocity.x = direction.x * speed
	#linear_velocity.z = direction.z * speed
#
	## Jumping logic
	#if Input.is_action_just_pressed("jump"):
		#linear_velocity.y = jump_strength
	
	# Apply gravity
	#vel.y += delta * -9.8  # Simple gravity

	# Move the character
	#position += vel*delta
	
func _unhandled_input(event):
	if event is InputEventMouseMotion:
		# Apply rotation
		if(!pd.mouse_visible): 
			# Adjust yaw (horizontal rotation) and pitch (vertical rotation)
			yaw -= event.relative.x * Global.sensitivity_x
			pitch -= event.relative.y * Global.sensitivity_y
			#print(yaw)
			# Clamp pitch to avoid flipping the camera
			pitch = clamp(pitch, Global.min_pitch, Global.max_pitch)
			rotate_camera()

func rotate_camera():
	# Rotate the player body (yaw) and camera (pitch)
	if(!pd.isMultiplayer): return
	camera.rotation_degrees.y = yaw  # Horizontal rotation (around the Y-axis)
	camera.rotation_degrees.x = pitch  # Vertical rotation (around the X-axis)

func update_facing_directions():
	# Update the forward and right directions based on the camera's rotation
	if(!pd.isMultiplayer): return
	var rot = camera.rotation_degrees.y
	forward_direction = Vector3.FORWARD.rotated(Vector3.UP, deg_to_rad(rot))
	right_direction = Vector3.RIGHT.rotated(Vector3.UP, deg_to_rad(rot))
