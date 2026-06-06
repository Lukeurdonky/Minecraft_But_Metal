extends Camera3D


@export var pd: Node3D
var baseFOV = 75
var sprintFOVAdd = 15
var max_distance = 5
@export var explode_radius = 4
@export var explode_damage = 1.0
var _explode_prev = false
var shader_material
var selected_normal

var box: PackedScene = preload("res://Assets/cube.tscn")
var yeah

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	shader_material = ShaderMaterial.new()
	shader_material.shader = preload("res://Materials/Select.gdshader")

	yeah = box.instantiate()
	get_parent().add_child.call_deferred(yeah)


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta):
	# Hide selection outline if player is dead
	if pd == null or pd.is_queued_for_deletion() or pd.CurrentHealth <= 0:
		if yeah != null:
			yeah.visible = false
		return

	selection()

	# Explosion trigger
	var e_now = false
	if Input.is_action_just_pressed("explode"):
		e_now = true
	elif Input.is_key_pressed(69) and not _explode_prev:
		e_now = true

	_explode_prev = Input.is_key_pressed(69)

	if e_now:
		var target = pd.SelectedCubePosition
		if target != null:
			Global.CubeManager.explode(target, explode_radius, explode_damage)
			yeah.visible = false
			pd.SelectedCube = 0
			pd.SelectedCubePosition = Vector3i(0, 0, 0)

	if pd.IsSprinting:
		fov = floor(lerp(fov, baseFOV + sprintFOVAdd+.0, .2))
	else:
		fov = floor(lerp(fov, baseFOV+.0, .2))


# ARCHIVED — Minecraft item interaction handlers. Superseded by weapon/ability system.
# func leftHandler(released: bool): ...
# func rightHandler(released: bool): ...
# func update_hand_mesh(item: String): ...
# func entityBlocking() -> Dictionary: ...
# func can_place(local: Vector3): ...
# func drop_item(): ...


func selection():
	var my_origin = global_transform.origin
	var direction = -global_transform.basis.z.normalized()
	var cur = Vector3(floor(my_origin.x), floor(my_origin.y), floor(my_origin.z))

	var step = Vector3(sign(direction.x), sign(direction.y), sign(direction.z))
	var delta = Vector3(
		abs(1.0 / direction.x) if direction.x != 0 else INF,
		abs(1.0 / direction.y) if direction.y != 0 else INF,
		abs(1.0 / direction.z) if direction.z != 0 else INF
	)

	var t_max = Vector3(
		delta.x * (ceil(my_origin.x) - my_origin.x if step.x > 0 else my_origin.x - floor(my_origin.x)),
		delta.y * (ceil(my_origin.y) - my_origin.y if step.y > 0 else my_origin.y - floor(my_origin.y)),
		delta.z * (ceil(my_origin.z) - my_origin.z if step.z > 0 else my_origin.z - floor(my_origin.z))
	)

	var distance = 0.0
	var face_normal = Vector3.ZERO
	var temp_cube = 0

	while distance < max_distance:
		var block_id = Global.CubeManager.get_block(cur)
		if block_id:
			temp_cube = block_id
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

	if temp_cube != 0:
		yeah.global_transform.origin = cur + Vector3(0.5, 0.5, 0.5)
		yeah.visible = true
	else:
		yeah.visible = false

	selected_normal = face_normal
	pd.SelectedCubePosition = Vector3i((cur))
	pd.SelectedCube = temp_cube
