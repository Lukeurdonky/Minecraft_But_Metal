extends MultiplayerSynchronizer

@export var directions = [false, false, false, false]
# Store the current pitch and yaw of the camera
@export var pitch: float = 0.0
@export var yaw: float = 0.0
#var camera_enabled
@export var is_sprinting: bool
@export var is_jumping: bool
@export var spectator_mode: bool
@export var is_crouching: bool
var jump_grace = .2
var jg = 0

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	#print("input sync")
	set_process(get_multiplayer_authority() == multiplayer.get_unique_id())
	set_physics_process(get_multiplayer_authority() == multiplayer.get_unique_id())
	#direction = Vector3.ZERO
	#if get_multiplayer_authority() != multiplayer.get_unique_id():
		#print("guest player: " + str(multiplayer.get_unique_id()))
		#set_process(false)
		#set_physics_process(false)
		#camera_enabled = false
	#else: camera_enabled = true
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _physics_process(delta: float) -> void:
	#if Input.is_action_pressed("sprint"):
		#pd.toggle_player_sprint(true)
	#
	#if Input.is_action_just_pressed("toggle_spectator"):
		#pd.toggle_spectator()
	if Input.is_action_pressed("jump"):
		jg = jump_grace
		is_jumping = true
	jg -= delta
	if jg < 0: is_jumping = false
	
	if Input.is_action_pressed("sprint"):
		is_sprinting = true
	else: is_sprinting = false
	
	if Input.is_action_just_pressed("toggle_spectator"):
		spectator_mode = true
	
	set_dir_zero()
	if Input.is_action_pressed("move_forward"):
		#print(multiplayer.get_unique_id())
		directions[0] = true
	#else: pd.toggle_player_sprint(false)
		
	if Input.is_action_pressed("move_back"):
		directions[1] = true
		#pd.toggle_player_sprint(false)
	if Input.is_action_pressed("move_left"):
		directions[2] = true
	if Input.is_action_pressed("move_right"):
		directions[3] = true
		
	if (Input.is_action_pressed("crouch")):
		is_crouching = true
	else: is_crouching = false
	pass

func set_dir_zero():
	for i in range(0,4):
		directions[i] = false

func _unhandled_input(event):
	if event is InputEventMouseMotion:
		#print("s")
		# Apply rotation
		#if(Input.MOUSE_MODE_VISIBLE): 
		# Adjust yaw (horizontal rotation) and pitch (vertical rotation)
		yaw -= event.relative.x * Global.SensitivityX
		pitch -= event.relative.y * Global.SensitivityY
		#print(yaw)
		# Clamp pitch to avoid flipping the camera
		pitch = clamp(pitch, Global.MinPitch, Global.MaxPitch)
		#print(str(yaw) + " " + str(pitch))
