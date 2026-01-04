extends CharacterBody3D

@export var camera: Camera3D
@export var collision_shape: CollisionShape3D
@export var inventory: Node3D

@export var speed = 5.0  # Movement speed
@export var sprint_mult = 2.0  # Sprint Multiplier
@export var accel = 5.0
@export var jump_strength = 10.0  # Jump force
@export var gravity: float = 9.8

var pitch: float = 0.0
var yaw: float = 0.0

var selected_cube
var selected_cube_position
var is_sprinting = false
var mouse_visible = false
var spectator_mode: bool = false

var forward_direction: Vector3 = Vector3.ZERO
var right_direction: Vector3 = Vector3.ZERO
var direction = Vector3.ZERO  # Movement direction

func _ready():
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	Global.Player = self
	camera.current = true

func _physics_process(delta):
	rotate_camera()
	_apply_movement_from_input(delta)

func _unhandled_input(event):
	if event is InputEventMouseMotion:
		if !mouse_visible:
			# Adjust yaw (horizontal rotation) and pitch (vertical rotation)
			yaw -= event.relative.x * Global.sensitivity_x
			pitch -= event.relative.y * Global.sensitivity_y
			
			# Clamp pitch to avoid flipping the camera
			pitch = clamp(pitch, Global.min_pitch, Global.max_pitch)

func _apply_movement_from_input(delta):
	if Input.is_action_just_pressed("toggle_mouse"):
		toggle_mouse_visibility()
		
	direction = Vector3.ZERO
	var temp_speed = speed
	var max = speed
	
	if Input.is_action_pressed("sprint"):
		toggle_player_sprint(true)
	
	if Input.is_action_just_pressed("toggle_spectator"):
		toggle_spectator()
		
	update_facing_directions()
	
	if Input.is_action_pressed("move_forward"):
		direction += forward_direction
	else:
		toggle_player_sprint(false)
		
	if Input.is_action_pressed("move_back"):
		direction -= forward_direction
		toggle_player_sprint(false)
		
	if Input.is_action_pressed("move_left"):
		direction -= right_direction
		
	if Input.is_action_pressed("move_right"):
		direction += right_direction
	
	if is_sprinting: 
		temp_speed *= sprint_mult
		max *= sprint_mult
		
	var fricMult = Global.ground_friction
	if !is_on_floor(): 
		temp_speed /= 8
		fricMult = Global.air_friction
	
	direction = direction.normalized() * temp_speed
	
	velocity = Vector3(velocity.x * fricMult, velocity.y, velocity.z * fricMult)
	
	velocity.x += direction.x * delta * accel
	velocity.z += direction.z * delta * accel
	
	velocity = Vector3(velocity.x, 0, velocity.z).limit_length(max) + Vector3(0, velocity.y, 0)
	
	if !spectator_mode:
		velocity.y -= gravity * delta
	else:
		velocity.y = 0
	
	if (is_on_floor() || spectator_mode) and Input.is_action_pressed("jump"):
		velocity.y = jump_strength
		
	if Input.is_action_pressed("crouch"):
		if spectator_mode:
			velocity.y = -jump_strength
	
	move_and_slide()

func rotate_camera():
	# Rotate the camera based on pitch and yaw
	camera.rotation_degrees.y = yaw  # Horizontal rotation (around the Y-axis)
	camera.rotation_degrees.x = pitch  # Vertical rotation (around the X-axis)

func update_facing_directions():
	# Update the forward and right directions based on the camera's rotation
	var rot = camera.rotation_degrees.y
	forward_direction = Vector3.FORWARD.rotated(Vector3.UP, deg_to_rad(rot))
	right_direction = Vector3.RIGHT.rotated(Vector3.UP, deg_to_rad(rot))

func toggle_spectator():
	spectator_mode = !spectator_mode
	if spectator_mode:
		collision_shape.disabled = true
	else:
		collision_shape.disabled = false
	
func toggle_player_sprint(flag: bool):
	is_sprinting = flag

func toggle_mouse_visibility():
	if Input.mouse_mode != Input.MOUSE_MODE_VISIBLE: 
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		mouse_visible = true
	else: 
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
		mouse_visible = false
