extends Camera3D

## Noclip fly camera for Scenes/StructureBuilder.tscn.
##
## Deliberately NOT the gameplay Player: Player.cs drags in gravity, world collision,
## the jackhammer/laser (which destroy terrain) and the SubViewport arms. A builder wants
## none of that. Block targeting is the same DDA voxel walk interactions.gd uses.
##
## Every edit is clamped to the build volume, so the base plate and anything outside the
## save region can't be touched by accident.

const MOVE_SPEED := 14.0
const SPRINT_MULT := 3.5
const SLOW_MULT := 0.25
const ACCEL := 12.0
const REACH := 12.0

# Held mouse button repeats an edit every this many seconds — dragging out a wall is the
# single most common builder action and one-block-per-click makes it miserable.
const REPEAT_DELAY := 0.28
const REPEAT_RATE := 0.055

var build_origin := Vector3i.ZERO
var build_size := Vector3i.ONE
var editing_enabled := true  ## false while the mouse is freed for the UI

var palette: Array[int] = []
var palette_index := 0

var _vel := Vector3.ZERO
var _selection: Node3D = null
var _target_block := Vector3i.ZERO
var _target_normal := Vector3i.ZERO
var _has_target := false

var _hold_action := ""
var _hold_time := 0.0
var _next_repeat := 0.0

signal palette_changed(block_id: int)


func _ready() -> void:
	# Chunk_Manager streams around Global.GetPlayerPos(); with no Player in this scene the
	# flycam is what it follows. Cleared in _exit_tree so a later gameplay scene isn't
	# left pointing at a freed node.
	Global.StreamingAnchor = self

	palette = Block_Registry.GetPlaceableBlockIds()
	if palette.is_empty():
		palette = [1]

	_selection = preload("res://Assets/cube.tscn").instantiate()
	get_parent().add_child.call_deferred(_selection)


func _exit_tree() -> void:
	if Global.StreamingAnchor == self:
		Global.StreamingAnchor = null


func _unhandled_input(event: InputEvent) -> void:
	if not editing_enabled:
		return

	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		rotation.y -= event.relative.x * Global.SensitivityX * 0.01
		rotation.x -= event.relative.y * Global.SensitivityY * 0.01
		rotation.x = clampf(rotation.x, -PI / 2.0 + 0.001, PI / 2.0 - 0.001)

	elif event is InputEventMouseButton and event.pressed:
		match event.button_index:
			MOUSE_BUTTON_WHEEL_UP:
				_cycle_palette(-1)
			MOUSE_BUTTON_WHEEL_DOWN:
				_cycle_palette(1)
			MOUSE_BUTTON_MIDDLE:
				_pick_block()

	elif event is InputEventKey and event.pressed and not event.echo:
		# 1-9 jump straight to a palette slot.
		var slot: int = event.keycode - KEY_1
		if slot >= 0 and slot < 9 and slot < palette.size():
			palette_index = slot
			palette_changed.emit(current_block())


func _process(delta: float) -> void:
	if not editing_enabled:
		_vel = Vector3.ZERO
		_hold_action = ""
		if _selection != null:
			_selection.visible = false
		return

	_move(delta)
	_update_target()
	_handle_edit_input(delta)


## Minecraft creative flight: WASD is strictly horizontal no matter where you're looking,
## and altitude is Space/Shift only. Looking down to place a floor shouldn't sink you into it.
##
## Direction comes from yaw rather than from flattening the camera basis, which degenerates
## to a near-zero vector when you look straight down. The modifiers are read as raw keys, not
## through the gameplay actions: Shift is `sprint` and Ctrl is `crouch` in combat, and both
## mean something different here.
func _move(delta: float) -> void:
	var yaw := global_rotation.y
	var forward := Vector3(-sin(yaw), 0.0, -cos(yaw))
	var right := Vector3(cos(yaw), 0.0, -sin(yaw))

	var dir := Vector3.ZERO
	if Input.is_action_pressed("move_forward"):
		dir += forward
	if Input.is_action_pressed("move_back"):
		dir -= forward
	if Input.is_action_pressed("move_right"):
		dir += right
	if Input.is_action_pressed("move_left"):
		dir -= right
	if Input.is_action_pressed("jump"):
		dir += Vector3.UP
	if Input.is_key_pressed(KEY_SHIFT):
		dir += Vector3.DOWN

	var speed := MOVE_SPEED
	if Input.is_key_pressed(KEY_CTRL):
		speed *= SPRINT_MULT
	elif Input.is_key_pressed(KEY_ALT):
		speed *= SLOW_MULT

	# Smoothed rather than snapped so nudging into position for a single block is precise.
	_vel = _vel.lerp(dir.normalized() * speed, clampf(ACCEL * delta, 0.0, 1.0))
	global_position += _vel * delta


func _handle_edit_input(delta: float) -> void:
	var action := ""
	if Input.is_action_pressed("attack1"):
		action = "break"
	elif Input.is_action_pressed("attack2"):
		action = "place"

	if action == "":
		_hold_action = ""
		return

	if action != _hold_action:
		_hold_action = action
		_hold_time = 0.0
		_next_repeat = REPEAT_DELAY
		_apply_edit(action)
		return

	_hold_time += delta
	if _hold_time >= _next_repeat:
		_next_repeat += REPEAT_RATE
		_apply_edit(action)


func _apply_edit(action: String) -> void:
	if not _has_target:
		return

	var pos := _target_block if action == "break" else _target_block + _target_normal
	if not in_volume(pos):
		return

	if action == "break":
		Global.CubeManager.break_block(pos)
	else:
		# Don't place inside the camera itself — you'd be sealed in and unable to see out.
		if Vector3i(global_position.floor()) == pos:
			return
		Global.CubeManager.place_block(pos, current_block())


func _pick_block() -> void:
	if not _has_target:
		return
	var id: int = Global.CubeManager.get_block(_target_block)
	var idx := palette.find(id)
	if idx >= 0:
		palette_index = idx
		palette_changed.emit(current_block())


func _cycle_palette(step: int) -> void:
	palette_index = wrapi(palette_index + step, 0, palette.size())
	palette_changed.emit(current_block())


func current_block() -> int:
	return palette[palette_index] if palette_index < palette.size() else 1


func in_volume(p: Vector3i) -> bool:
	return (
		p.x >= build_origin.x and p.x < build_origin.x + build_size.x
		and p.y >= build_origin.y and p.y < build_origin.y + build_size.y
		and p.z >= build_origin.z and p.z < build_origin.z + build_size.z
	)


## Amanatides-Woo voxel walk — same algorithm as interactions.gd's selection(), kept
## separate because that one writes its result onto the Player and this scene has none.
func _update_target() -> void:
	var origin := global_transform.origin
	var direction := -global_transform.basis.z.normalized()
	var cur := Vector3(floor(origin.x), floor(origin.y), floor(origin.z))

	var step := Vector3(signf(direction.x), signf(direction.y), signf(direction.z))
	var delta := Vector3(
		absf(1.0 / direction.x) if direction.x != 0.0 else INF,
		absf(1.0 / direction.y) if direction.y != 0.0 else INF,
		absf(1.0 / direction.z) if direction.z != 0.0 else INF
	)

	var t_max := Vector3(
		delta.x * (ceil(origin.x) - origin.x if step.x > 0 else origin.x - floor(origin.x)),
		delta.y * (ceil(origin.y) - origin.y if step.y > 0 else origin.y - floor(origin.y)),
		delta.z * (ceil(origin.z) - origin.z if step.z > 0 else origin.z - floor(origin.z))
	)

	var distance := 0.0
	var face_normal := Vector3.ZERO
	var hit := 0

	while distance < REACH:
		var block_id: int = Global.CubeManager.get_block(Vector3i(cur))
		if block_id != 0:
			hit = block_id
			break
		if t_max.x < t_max.y and t_max.x < t_max.z:
			cur.x += step.x
			distance = t_max.x
			t_max.x += delta.x
			face_normal = Vector3(-step.x, 0, 0)
		elif t_max.y < t_max.z:
			cur.y += step.y
			distance = t_max.y
			t_max.y += delta.y
			face_normal = Vector3(0, -step.y, 0)
		else:
			cur.z += step.z
			distance = t_max.z
			t_max.z += delta.z
			face_normal = Vector3(0, 0, -step.z)

	_has_target = hit != 0
	_target_block = Vector3i(cur)
	_target_normal = Vector3i(face_normal)

	if _selection != null:
		_selection.visible = _has_target
		if _has_target:
			_selection.global_transform.origin = cur + Vector3(0.5, 0.5, 0.5)
