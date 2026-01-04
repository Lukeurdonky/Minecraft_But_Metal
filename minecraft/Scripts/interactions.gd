extends Camera3D


@export var pd: Node3D
var baseFOV = 75
var sprintFOVAdd = 15
var max_distance = 5
#@export var collision_mask: int = 0xFFFFFFFF
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
	selection()
	
	if pd.IsSprinting:
		fov = floor(lerp(fov, baseFOV + sprintFOVAdd+.0, .2))
	else:
		fov = floor(lerp(fov, baseFOV+.0, .2))
		
	


func _unhandled_input(event):
	if event is InputEventMouseButton:
		if event.pressed:  # True when the button is pressed down
			match event.button_index:
				MOUSE_BUTTON_LEFT:
					breakBlock()
				MOUSE_BUTTON_RIGHT:
					placeBlock()

func breakBlock():
	if(pd.SelectedCube != 0 && pd.SelectedCube != null):
		var ori = pd.SelectedCubePosition
		var block_type = pd.SelectedCube
		var r = Block_Registry.GetBlockDropCount(block_type)
		print(r)
		#for i in range(Global.get_block_stat(block_type, "drop_count")):
		#Global.Player.inventory.add_item(Global.get_block_stat(block_type, "drops"))
		for i in range(r):
			Global.Player.Inventory.add_item(Block_Registry.GetBlockDropID(block_type))	
		Global.CubeManager.break_block(ori)
		pd.SelectedCube = null
		#print("break")
		#Global.selected_cube.destroy_cube()
		
func placeBlock():
	var item = Global.Player.Inventory.get_item(Global.Player.Inventory.selected_slot)
	if(pd.SelectedCube != null && item != null):
		var loc = pd.SelectedCubePosition + Vector3i(selected_normal)
		if(can_place(loc)):
			var p = Global.GetItemStat(item, "block")
			if(p == null): return
			else: 
				Global.CubeManager.place_block(loc, p)
				pd.Inventory.remove_item(Global.Player.Inventory.selected_slot)
			#print(loc)
			#Global.CubeManager.set_cube(1, loc)
			#Global.CubeManager.get_cube(loc).node.update_adjacents()
			#Global.selected_cube

func selection():
	var my_origin = global_transform.origin
	var direction = -global_transform.basis.z.normalized()
	var cur = Vector3(floor(my_origin.x), floor(my_origin.y), floor(my_origin.z))
	
	# Calculate step direction and delta
	var step = Vector3(sign(direction.x), sign(direction.y), sign(direction.z))
	var delta = Vector3(
		abs(1.0 / direction.x) if direction.x != 0 else INF,
		abs(1.0 / direction.y) if direction.y != 0 else INF,
		abs(1.0 / direction.z) if direction.z != 0 else INF
	)
	
	# Calculate initial t_max values
	var t_max = Vector3(
		delta.x * (ceil(my_origin.x) - my_origin.x if step.x > 0 else my_origin.x - floor(my_origin.x)),
		delta.y * (ceil(my_origin.y) - my_origin.y if step.y > 0 else my_origin.y - floor(my_origin.y)),
		delta.z * (ceil(my_origin.z) - my_origin.z if step.z > 0 else my_origin.z - floor(my_origin.z))
	)
	
	var distance = 0.0
	var face_normal = Vector3.ZERO
	var temp_cube = 0
	#print(step)
	while distance < max_distance:
		# Check current voxel
		#print(current)
		var block_id = Global.CubeManager.get_block(cur)
		if block_id:
			temp_cube = block_id
			break
		# Step to next voxel
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
	#print(yeah.global_transform.origin)
	pd.SelectedCube = temp_cube
	#print(current)
	

#func selection():
	#var query = PhysicsRayQueryParameters3D.new()
	#query.from = global_transform.origin# - global_transform.basis.z.normalized()
	#query.to = global_transform.origin - global_transform.basis.z.normalized()*max_distance
	##query.collision_layer = 1
	#query.collision_mask = 1
	#
	## Cast the ray and check for any intersection
	#var space_state: PhysicsDirectSpaceState3D = get_world_3d().direct_space_state
	#var result = space_state.intersect_ray(query)
#
	#if result:
		##if(Global.selected_cube != null): Global.selected_cube.data.mesh_instance.material_overlay = null
		##print(floor(result.position))
		#var cube_pos = floor(result.position - result.normal/2.0)
		#yeah.global_transform.origin = floor(result.position - result.normal/2.0) + Vector3(.5,.5,.5)
		#yeah.visible = true
		#pd.selected_cube = Global.CubeManager.get_block(cube_pos)
		#selected_normal = result.normal
		##print(Global.selected_cube)
		##Global.selected_cube.data.mesh_instance.material_overlay = shader_material
		##print(Global.selected_cube.data.mesh_instance.material_overlay)
	#else:
		##if(Global.selected_cube != null): Global.selected_cube.data.mesh_instance.material_overlay = null
		#pd.selected_cube = null
		#yeah.visible = false
		
func can_place(local: Vector3):
	var flag = false
	if(Global.CubeManager.get_block(local) == 0): 
		flag = true;
		
	#var space_state: PhysicsDirectSpaceState3D = get_world_3d().direct_space_state
	#var query = PhysicsShapeQueryParameters3D.new()
	#
	#query.shape = BoxShape3D.new() #shape
	#query.shape.size = Vector3(.97, .97, .97)
	#query.transform = Transform3D(Basis(), local) #origin
	#query.motion = Vector3.ZERO #direction
	#query.margin = 0.01 #margin
	#query.collision_mask = 2
	#
	#var results = space_state.intersect_shape(query)
	##print(results.size())
	##var size = Global.CubeManager.WORLD_SIZE*16
	##print("base: " + str(local))
	##print("target: " + str(floor(local + selected_normal)))
	#if(Global.CubeManager.get_block(local)): 
		##print(local + selected_normal)
		##print(str(Global.CubeManager.get_block(floor(local + selected_normal)).pos) + "  " + str(floor(local + selected_normal)))
		#flag = false
	#if(results.size() > 0): flag = false
	#if(local.x >= size.x || local.y >= size.y || local.z >= size.z || local.x < 0 || local.y < 0 || local.z < 0): flag = false
	return flag
