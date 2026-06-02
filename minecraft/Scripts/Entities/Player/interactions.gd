extends Camera3D


@export var pd: Node3D
var baseFOV = 75
var sprintFOVAdd = 15
var max_distance = 5
@export var throw_strength = 4
@export var explode_radius = 4
@export var explode_damage = 1.0
var _explode_prev = false
#@export var collision_mask: int = 0xFFFFFFFF
var shader_material
var selected_normal
var prev_selected_item
var delta_time = 0.0

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
	delta_time = delta
	
	# Hide selection outline if player is dead
	if pd == null or pd.is_queued_for_deletion() or pd.CurrentHealth <= 0:
		if yeah != null:
			yeah.visible = false
		return
	
	selection()

	# Explosion trigger: prefer an InputMap action named "explode", fallback to raw 'E' key (scancode 69)
	var e_now = false
	if Input.is_action_just_pressed("explode"):
		e_now = true
	elif Input.is_key_pressed(69) and not _explode_prev:
		e_now = true

	_explode_prev = Input.is_key_pressed(69)

	if e_now:
		# selection() already updates pd.SelectedCubePosition
		var target = pd.SelectedCubePosition
		if target != null:
			# Call C# Chunk_Manager.explode(Vector3I, float, float)
			Global.CubeManager.explode(target, explode_radius, explode_damage)
			# Optional feedback
			print("Exploded at ", target)
			# Clear selection visuals so outline doesn't persist after death/block removal
			yeah.visible = false
			pd.SelectedCube = 0
			# Reset selected position to an invalid/zero vector
			pd.SelectedCubePosition = Vector3i(0, 0, 0)
	if Input.is_action_pressed("drop_item"):
		drop_item()
		
	if Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
		leftHandler(false)
		# breakBlock()
	if Input.is_mouse_button_pressed(MOUSE_BUTTON_RIGHT):
		rightHandler(false)
		# placeBlock()
	
	var itm = pd.Inventory.get_item(pd.Inventory.selected_slot)
	if itm != prev_selected_item:
		#update the hand sprite to whatever item it is
		if itm == null: itm = ""
		update_hand_mesh(itm)
		
		pass
	if pd.IsSprinting:
		fov = floor(lerp(fov, baseFOV + sprintFOVAdd+.0, .2))
	else:
		fov = floor(lerp(fov, baseFOV+.0, .2))
		
	prev_selected_item = pd.Inventory.get_item(pd.Inventory.selected_slot)
	pass


func _unhandled_input(event):
	if event is InputEventMouseButton:
		#if event.pressed:  # True when the button is pressed down
			#match event.button_index:
				#MOUSE_BUTTON_LEFT:
					#breakBlock()
				#MOUSE_BUTTON_RIGHT:
					#placeBlock()
		#if event.is_:
			#match event.button_index:
				#MOUSE_BUTTON_LEFT:
					#breakBlock()
				#MOUSE_BUTTON_RIGHT:
					#placeBlock()
		if event.is_released():
			match event.button_index:
				MOUSE_BUTTON_LEFT:
					# breakBlock()
					leftHandler(true)
				MOUSE_BUTTON_RIGHT:
					rightHandler(true)
					# placeBlock()

# func breakBlock():
# 	if(pd.SelectedCube != 0 && pd.SelectedCube != null):
# 		var ori = pd.SelectedCubePosition
# 		var block_type = pd.SelectedCube
# 		var r = Block_Registry.GetBlockDropCount(block_type)
# 		print(r)
# 		#for i in range(Global.get_block_stat(block_type, "drop_count")):
# 		#Global.Player.inventory.add_item(Global.get_block_stat(block_type, "drops"))
# 		for i in range(r):
# 			Global.Player.Inventory.add_item(Block_Registry.GetBlockDropID(block_type))	
# 		Global.CubeManager.break_block(ori)
# 		pd.SelectedCube = null
# 		#print("break")
# 		#Global.selected_cube.destroy_cube()
		
# func placeBlock():
# 	var item = Global.Player.Inventory.get_item(Global.Player.Inventory.selected_slot)
# 	if(pd.SelectedCube != null && item != null):
# 		var loc = pd.SelectedCubePosition + Vector3i(selected_normal)
# 		if(can_place(loc) && Item_Registry.IsPlaceable(item)):
# 			var p = Item_Registry.GetItemStat(item, "Block")
# 			if(p == null): return
# 			else: 
# 				Global.CubeManager.place_block(loc, p)
# 				pd.Inventory.remove_item(Global.Player.Inventory.selected_slot)
# 			#print(loc)
# 			#Global.CubeManager.set_cube(1, loc)
# 			#Global.CubeManager.get_cube(loc).node.update_adjacents()
# 			#Global.selected_cube

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

func leftHandler(released: bool):
	var result = entityBlocking()

	var item_name = pd.Inventory.get_item(pd.Inventory.selected_slot)
	if item_name == null:
		item_name = "hand"
	
	var behavior = Item_Registry.GetItemBehavior(item_name)
	
	if !released:
		if result["blocking"]:
			# Entity is blocking - use item on entity instead
			print("hit entity")
			behavior.OnHit(item_name, result["entity"])
			
		else:
			if pd.SelectedCube != 0 && pd.SelectedCube != null:
				behavior.BreakBlock(item_name, pd, delta_time)
	else:
		behavior.OnRelease(item_name, pd)
	pass

func rightHandler(released: bool):
	var result = entityBlocking()
	var item_name = pd.Inventory.get_item(pd.Inventory.selected_slot)
	if item_name == null:
		return
	
	var behavior = Item_Registry.GetItemBehavior(item_name)
	
	if !released:
		if result["blocking"]:
			# Entity is blocking - use item on entity instead
			if Item_Registry.IsPlaceable(item_name):
				behavior.UseOnEntity(result["entity"], pd, item_name)
			else:
				behavior.OnUse(item_name, pd)
		else:
			# No entity blocking - place the block
			if Item_Registry.IsPlaceable(item_name):
				if pd.SelectedCube != 0 && pd.SelectedCube != null:
					var loc = pd.SelectedCubePosition + Vector3i(selected_normal)
					var block_type = Item_Registry.GetItemStat(item_name, "Block")
					if can_place(loc) and block_type != null:
						behavior.Place(block_type, pd, loc)
			else:
				# Item is not placeable - use normally
				behavior.OnUse(item_name, pd)
	else:
		behavior.OnRelease(item_name, pd)
	pass
	
func entityBlocking() -> Dictionary:
	var result = {"blocking": false, "entity": null}
	
	
	var my_origin = global_transform.origin
	var target_pos = Vector3(pd.SelectedCubePosition) + Vector3(0.5, 0.5, 0.5)
	var block_distance = my_origin.distance_to(target_pos)
	
	var space_state = get_world_3d().direct_space_state
	var query = PhysicsRayQueryParameters3D.create(my_origin, target_pos)
	query.collision_mask = 2  # Adjust mask to match your entity collision layer
	query.collide_with_bodies = true
	query.collide_with_areas = false
	
	var ray_result = space_state.intersect_ray(query)
	
	if ray_result and ray_result.collider is CharacterBody3D:
		var entity_distance = my_origin.distance_to(ray_result.position)
		if entity_distance < block_distance:
			result["blocking"] = true
			result["entity"] = ray_result.collider
	
	return result
		
func can_place(local: Vector3):
	var flag = false
	if(Global.CubeManager.get_block(local) == 0 && pd.SelectedCube != 0): 
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
	
	
func drop_item():
	# Item dropping disabled for testing
	#var item = Global.Player.Inventory.get_item(Global.Player.Inventory.selected_slot)
	#if(item != null):
	#	var dir = -global_transform.basis.z.normalized()
	#	var drop = Item_Registry.SpawnItem(item, global_position + dir/4, get_tree().root)
	#	drop.global_rotation.y = rotation.y+PI/4;
	#	drop.velocity = dir*throw_strength;
	#	pd.Inventory.remove_item(Global.Player.Inventory.selected_slot)
	return
				

func update_hand_mesh(item: String):
	if(item == ""):
		pd.HandMesh.mesh = null
	else:
		Item_Registry.ChangeMesh(pd.HandMesh, item);
	pass
