#class_name CubeManager

extends Node3D

var cube_list: Array = []
var chunk_list: Array = []
var active_chunk_list: Array = []
var spawn_size = Vector3(3, 8, 3)
var world_size = Vector3(4, 8, 4)
var render_distance = Vector3(3, 3, 3)
var tempOffset = -16



# Enum for cube types
enum CubeType {
	AIR,
	GRASS,
	DIRT,
	STONE,
	WATER
}

#Cube class
class Cube:
	var cube_scene: PackedScene = preload("res://Assets/cube.tscn")
	var cube_type: CubeType
	var position: Vector3
	var node: Node3D
	#var vis: VisibleOnScreenNotifier3D
	var mesh_instance: MeshInstance3D
	var rigid_body: RigidBody3D
	var collider: CollisionShape3D
	var full_block = true
	var base_behavior = preload("res://Scripts/Cube_Behaviors/CubeMechanics.gd")
	#var air_behavior = load("res://Scripts/Cube_Behaviors/Air.gd")
	var health = 0
	var active = false

	# Constructor
	func _init(cube_type: CubeType, position: Vector3):
		self.cube_type = cube_type
		self.position = position
		self.node = cube_scene.instantiate()
		#self.node.set_physics_process(false)
		self.node.position = position
		self.full_block = true
		self.active = active
		
		#self.vis = self.node.get_child(0)
		self.mesh_instance = self.node.get_child(0)
		self.rigid_body = self.mesh_instance.get_child(0)
		self.collider = self.rigid_body.get_child(0)
		
		#self.mesh_instance.visible = false
		# Set the mesh and material based on the cube type
		match cube_type:
			CubeType.AIR:
				self.health = 0
				self.full_block = false
				#self.collider.disabled = true
				self.collider.queue_free()
				self.rigid_body.queue_free()
				#self.mesh_instance.visible = false
				self.mesh_instance.queue_free()
				var air_behavior = preload("res://Scripts/Cube_Behaviors/Air.gd")
				self.node.set_script(air_behavior)
				#self.mesh_instance.material_override = preload("res://Materials/grassMat.tres")
			CubeType.GRASS:
				self.health = 5
				
				self.mesh_instance.material_override = preload("res://Materials/grassMat.tres")
			CubeType.DIRT:
				self.health = 4
				self.mesh_instance.material_override = preload("res://Materials/dirtMat.tres")
			#CubeType.STONE:
				#self.mesh_instance.mesh = BoxMesh.new()
				#self.mesh_instance.material_override = preload("res://Materials/obstacle_material.tres")
			#CubeType.WATER:
				#self.mesh_instance.mesh = BoxMesh.new()
				#self.mesh_instance.material_override = preload("res://Materials/spawn_material.tres")
		
		self.node.data = self
	
# Cube class
#class Cube:
	#var cube_type: CubeType
	#var position: Vector3
	#var node: Node3D
	#var vis: VisibleOnScreenNotifier3D
	#var mesh_instance: MeshInstance3D
	#var rigid_body: RigidBody3D
	#var collision_shape: CollisionShape3D
	#var health = 0
#
	## Constructor
	#func _init(cube_type: CubeType, position: Vector3):
		#self.cube_type = cube_type
		#self.position = position
		#self.node = Node3D.new()
		#self.vis = VisibleOnScreenNotifier3D.new()
		#self.node.add_child(self.vis)
		#self.mesh_instance = MeshInstance3D.new()
		#self.vis.add_child(self.mesh_instance)
		#self.rigid_body = RigidBody3D.new()
		#self.mesh_instance.add_child(self.rigid_body)
		#self.collision_shape = CollisionShape3D.new()
		#self.rigid_body.add_child(self.collision_shape)
		#
		## Set the position of the cube
		#self.mesh_instance.transform.origin = position
		#self.mesh_instance.mesh = BoxMesh.new()
		#
		#self.rigid_body.freeze = true
		#self.collision_shape.shape = BoxShape3D.new()
		#var scr = preload("res://Scripts/visibility.gd");
		#self.vis.set_script(scr)
		##if not is_connected("screen_entered", Callable(self, "_on_screen_entered")):
		##self.vis.connect("screen_entered", Callable(self.vis, "_on_screen_entered"))
		##if not is_connected("screen_exited", Callable(self, "_on_screen_exited")):
		##self.vis.connect("screen_exited", Callable(self.vis, "_on_screen_exited"))
		##self.vis.target_node = self.mesh_instance
		##var body_mode = RigidBody3D.MODE_STATIC
		##self.rigid_body.body_mode = body_mode
		##var shape = BoxShape3D.new()
		##self.collision_shape.shape = shape
		## Set the mesh and material based on the cube type
		#match cube_type:
			#CubeType.GRASS:
				#self.health = 5
				#self.mesh_instance.material_override = preload("res://Materials/grassMat.tres")
			#CubeType.DIRT:
				#self.health = 4
				#self.mesh_instance.material_override = preload("res://Materials/dirtMat.tres")
			##CubeType.STONE:
				##self.mesh_instance.mesh = BoxMesh.new()
				##self.mesh_instance.material_override = preload("res://Materials/obstacle_material.tres")
			##CubeType.WATER:
				##self.mesh_instance.mesh = BoxMesh.new()
				##self.mesh_instance.material_override = preload("res://Materials/spawn_material.tres")
		#
		
func _process(delta):
	pass
	#render_in_distance()

func render_in_distance():
	var updPos: Vector3
	var dat = ""
	for x in range(render_distance.x):
		var posX = Global.get_player_pos().x-((x-render_distance.x/2)*16) + tempOffset
		updPos.x = round(posX / 16)
		for y in range(render_distance.y):
			var posY = Global.get_player_pos().y-((y-render_distance.y/2)*16) + tempOffset
			updPos.y = round(posY / 16)
			for z in range(render_distance.z):
				var posZ = Global.get_player_pos().z-((z-render_distance.z/2)*16) + tempOffset
				updPos.z = round(posZ / 16)
					#if !chunk_in_range(active_chunk_list[x][y][z].position/16): 
						#load_chunk(active_chunk_list[x][y][z].position/16, false)
				var ch = active_chunk_list[x][y][z]
				active_chunk_list[x][y][z] = chunk_list[updPos.x][updPos.y][updPos.z]
				if(ch != null): 
					if !chunk_in_active_list(ch):
						load_chunk(ch.position/16, false)
				#if(active_chunk_list[x][y][z] == null): dat += str(active_chunk_list[x][y][z] != null) + " "
				#else: dat += str(active_chunk_list[x][y][z].position.y) + " "
				if(active_chunk_list[x][y][z] != null): 
					if(!active_chunk_list[x][y][z].loaded): 
						active_chunk_list[x][y][z].loaded = true
						render_chunk(active_chunk_list[x][y][z].position/16)
	#print(chunk_in_range(Vector3(0,0,0)))
	#print(dat)
	
func chunk_in_active_list(ch: Chunk):
	var found = false
	for i in range(active_chunk_list.size()):
		for j in range(active_chunk_list[i].size()):
			for k in range(active_chunk_list[i][j].size()):
				if active_chunk_list[i][j][k] == ch:
					found = true
					break
			if found:
				break
		if found:
			break
	return found

func spawn_cube(cube_type: int, position: Vector3):
	var new_cube = Cube.new(cube_type, position)
	#print(position)
	cube_list[position.x][position.y][position.z] = new_cube
	
	add_child(new_cube.node)  # Assuming mesh_instance is the root node

func init_cube(cube_type: int, position: Vector3):
	var new_cube = Cube.new(cube_type, position)
	new_cube.node.deactivate()
	#print(position)
	cube_list[position.x][position.y][position.z] = new_cube
	
	add_child(new_cube.node)  # Assuming mesh_instance is the root node

enum ChunkType
{
	NORMAL,
	AIR
}

class Chunk:
	var position
	var chunk_type
	#var chunk_cubes: Array = []
	var initialized = false
	var loaded = false
	
	func _init(position: Vector3, type: ChunkType):
		self.position = position
		self.chunk_type = type
		self.initialized = initialized
		self.initialized = true
		self.loaded = loaded
		
		#
		#for i in range(16):
			#var second_dimension: Array = []
			#for j in range(16):
				#var third_dimension: Array = []
				#for k in range(16):
					#third_dimension.append(null)  # Initialize each cell with null (or any default value)
				#second_dimension.append(third_dimension)
			#chunk_cubes.append(second_dimension)
		#match type:
		
func chunk_in_range(loc: Vector3):
	var flag = true
	var upper: Vector3
	var lower: Vector3
	lower.x = round((Global.get_player_pos().x-render_distance.x*16/2 + tempOffset)/16)
	lower.y = round((Global.get_player_pos().y-render_distance.y*16/2 + tempOffset)/16)
	lower.z = round((Global.get_player_pos().z-render_distance.z*16/2 + tempOffset)/16)
	upper.x = round((Global.get_player_pos().x+render_distance.x*16/2 + tempOffset)/16)
	upper.y = round((Global.get_player_pos().y+render_distance.y*16/2 + tempOffset)/16)
	upper.z = round((Global.get_player_pos().z+render_distance.z*16/2 + tempOffset)/16)
	if(loc.x < lower.x || loc.x > upper.x || loc.y < lower.y || loc.y > upper.y || loc.z < lower.z || loc.z > upper.z): flag = false
	print(flag)
	return flag

func render_chunk(pos: Vector3):
	#var start_time = Time.get_ticks_msec()
	for x in range(16):
		for y in range(16):
			for z in range(16):
				#print("load_chunk")
				if(get_cube(pos*16 + Vector3(x,y,z)).node.was_visible): get_cube(pos*16 + Vector3(x,y,z)).node.activate()
				else: get_cube(pos*16 + Vector3(x,y,z)).node.deactivate()
	#var end_time = Time.get_ticks_msec()
	#print("Total Load Time: " + str(end_time - start_time))

func load_chunk(pos: Vector3, load:bool):
	chunk_list[pos.x][pos.y][pos.z].loaded = load
	#print("load: " + str(pos) + " " + str(load))
	
	if(load):
		#if(chunk_list[pos.x][pos.y][pos.z].initialized):
		for x in range(16):
			for y in range(16):
				for z in range(16):
					#print("load_chunk")
					get_cube(pos*16 + Vector3(x,y,z)).node.update_cube()
		
		#else:
		#for x in range(16):
			#for y in range(16):
				#for z in range(16):
					##print("load_chunk")
					#if(get_cube(pos*16 + Vector3(x,y,z)).node.was_visible): get_cube(pos*16 + Vector3(x,y,z)).node.active()
					#else: get_cube(pos*16 + Vector3(x,y,z)).node.deactivate()
		
	else:
		#var start_time = Time.get_ticks_msec()
		for x in range(16):
			for y in range(16):
				for z in range(16):
					get_cube(pos*16 + Vector3(x,y,z)).node.deactivate()
		#var end_time = Time.get_ticks_msec()
		#print("Total Load Time: " + str(end_time - start_time))

func initialize_chunk(position: Vector3, chunk_type: ChunkType):
	var chunk = Chunk.new(position, chunk_type)
	chunk_list[position.x/16][position.y/16][position.z/16] = chunk
	#print(position)
	match chunk.chunk_type: 
		ChunkType.NORMAL:
			for x in range(16):
				for y in range(16):
					for z in range(16):
						if y == 15: init_cube(CubeType.GRASS, position + Vector3(x,y,z))
						else: init_cube(CubeType.DIRT, position + Vector3(x,y,z))
						#chunk.chunk_cubes[x][y][z] = cube_list[position.x + x][position.y + y][position.z + z]
		ChunkType.AIR:
			for x in range(16):
				for y in range(16):
					for z in range(16):
						init_cube(CubeType.AIR, position + Vector3(x,y,z))
						#chunk.chunk_cubes[x][y][z] = cube_list[position.x + x][position.y + y][position.z + z]
	#cube_list[position.x][position.y][position.z] = new_cube
	#match type:
		#ChunkType.NORMAL:
			#for x in range(16):
				#for y in range(16):
					#for z in range(16):
						#if y == 15: spawn_cube(CubeType.GRASS, position + Vector3(x,y,z))
						#else: spawn_cube(CubeType.DIRT, position + Vector3(x,y,z))
		#ChunkType.AIR:
			#for x in range(16):
				#for y in range(16):
					#for z in range(16):
						#spawn_cube(CubeType.AIR, position + Vector3(x,y,z))
						

#func _ready():
	#Global.CubeManager = self
	#var start_time = Time.get_ticks_msec()
	#init_array()
	#var array_time = Time.get_ticks_msec()
	#for x in range(spawn_size.x):
		#for y in range(spawn_size.y):
			#for z in range(spawn_size.z):
				#if(y < 4): initialize_chunk(Vector3(x*16,y*16,z*16), ChunkType.NORMAL)
				#else: initialize_chunk(Vector3(x*16,y*16,z*16), ChunkType.AIR)
	#var spawn_time = Time.get_ticks_msec()
	#for x in range(spawn_size.x):
		#for y in range(spawn_size.y):
			#for z in range(spawn_size.z):
				#load_chunk(Vector3(x,y,z), true)
	##render_in_distance()
	##for x in range(spawn_size.x*16):
		##for y in range(spawn_size.y*16):
			##for z in range(spawn_size.z*16):
				###get_cube(Vector3(x,y,z)).node.set_physics_process(true)
				##get_cube(Vector3(x,y,z)).node.update_cube()
	#var update_time = Time.get_ticks_msec()
	#var end_time = Time.get_ticks_msec()
	#print("Total Load Time: " + str(end_time - start_time) + "\nArray Time: " + str(array_time - start_time) + "\nSpawn Cube Time: " + str(spawn_time - array_time) + "\nUpdate Time: " + str(update_time - spawn_time))
			

func init_array():
	for i in range(world_size.x*16):
		var second_dimension: Array = []
		for j in range(world_size.y*16):
			var third_dimension: Array = []
			for k in range(world_size.z*16):
				third_dimension.append(null)  # Initialize each cell with null (or any default value)
			second_dimension.append(third_dimension)
		cube_list.append(second_dimension)
		
	for i in range(world_size.x):
		var second_dimension: Array = []
		for j in range(world_size.y):
			var third_dimension: Array = []
			for k in range(world_size.z):
				third_dimension.append(null)  # Initialize each cell with null (or any default value)
			second_dimension.append(third_dimension)
		chunk_list.append(second_dimension)
		
	for i in range(render_distance.x):
		var second_dimension: Array = []
		for j in range(render_distance.y):
			var third_dimension: Array = []
			for k in range(render_distance.z):
				third_dimension.append(null)  # Initialize each cell with null (or any default value)
			second_dimension.append(third_dimension)
		active_chunk_list.append(second_dimension)
#func _process(delta):
	#cube_list[cube_list.size()-1].node.destroy_cube()
	#cube_list.remove_at(cube_list.size()-1)
	
func get_cube(loc: Vector3):
	var c = null
	#print(loc)
	if((0 <= loc.x && loc.x < world_size.x*16) && (0 <= loc.y && loc.y < world_size.y*16) && (0 <= loc.z && loc.z < world_size.z*16)):
		c = cube_list[loc.x][loc.y][loc.z]
	return c
	
func set_cube(type: CubeType, loc: Vector3):
	if((0 <= loc.x && loc.x < world_size.x*16) && (0 <= loc.y && loc.y < world_size.y*16) && (0 <= loc.z && loc.z < world_size.z*16)):
		if(cube_list[loc.x][loc.y][loc.z] != null): cube_list[loc.x][loc.y][loc.z].node.queue_free()
		spawn_cube(type, loc)
