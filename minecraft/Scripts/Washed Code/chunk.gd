extends Node3D

const CHUNK_SIZE = Vector3i(16, 16, 16)  # Chunk dimensions
#const WORLD_SIZE = Vector3(16, 16, 16)
#var texture_atlas = preload("res://texture_atlas.png")
var chunks: Dictionary = {}  # Store chunk nodes by their positions
var active_chunks: Dictionary = {}
#var chunks = []
@export var mat: Material
@export var render_distance = 5
var thread : Thread = null
#var thread_load : Thread = null
#var grid_width = 3
#var grid_height = 2
var setTimeHandle = 0.025
var noise = FastNoiseLite.new()
var material = ShaderMaterial.new()
var generate_position: Vector3
var shader_code = """
shader_type spatial;

void fragment() {
	vec4 color = COLOR; // Use vertex color
	ALBEDO = color.rgb;
	ALPHA = color.a;
}
"""

class Cube:
	var type
	var pos
	var local_pos
	#var health
	var sides_ndx = [null,null,null,null,null,null]
	var col_sides_ndx = [null,null,null,null,null,null]
	
	func _init(my_name: String, pos: Vector3i):
		self.type = my_name
		self.pos = pos
		self.local_pos = Vector3(pos.x%CHUNK_SIZE.x, pos.y%CHUNK_SIZE.y, pos.z%CHUNK_SIZE.z)
		#self.health = Global.Block_Data[self.cube_ndx].health
		
	func get_face_uvs(face_index: int) -> Array:
		var uvs = []
		#return [Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)]
		#uvs.append_array([Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)])
		var atlas_columns = Global.AtlasWidth / 3  # Number of 3/2 sections horizontally
		var atlas_rows = Global.AtlasHeight / 2   # Number of 3/2 sections vertically

		# Calculate cube (3/2 section) offsets from cube_ndx
		var x_cube_offset = Global.GetBlockStat(self.type, "index") % atlas_columns
		var y_cube_offset = Global.GetBlockStat(self.type, "index") / atlas_columns
		
		var x_offset = (face_index % 3)
		var y_offset = (face_index / 3)
		#print(y_offset)
		# Calculate the UV coordinates for each corner of the face
		var u_start = (x_cube_offset*3 + x_offset + 0.0) / Global.AtlasWidth
		var v_start = (y_cube_offset*2 + y_offset + 1.0) / Global.AtlasHeight
		var u_end = (x_cube_offset*3 + x_offset + 1.0) / Global.AtlasWidth
		var v_end = (y_cube_offset*2 + y_offset + 0.0) / Global.AtlasHeight
		
		#if(v_end > 1): print(v_end)
		uvs.append(Vector2(u_start, v_start))
		uvs.append(Vector2(u_end, v_start))
		uvs.append(Vector2(u_end, v_end))
		uvs.append(Vector2(u_start, v_end))
		#print(str(uvs) + " " + str(face_index))
		return uvs

class Chunk:
	var position
	var generated = false
	var chunk_data: Dictionary = {}
	var chunk_build_data
	#var block_colliders: Dictionary = {}
	var collision_faces = []
	var loaded = false
	var mesh_instance
	var static_body
	#var shape_owner
	
	func _init(position: Vector3):
		self.position = position
		#self.static_body = StaticBody3D.new()

func _ready():
	#print("CHUNK")
	#init_chunks_array()
	Global.CubeManager = self
	noise.seed = 42
	#handle_spawn_chunks()
	#handle_chunks()
	#for x in range(-render_distance, render_distance): print(x)
	#print(active_chunks)
	#print("shmeah")
	#thread.start(thread_func)
	#for x in range(2):
		#for y in range(2):
			#for z in range(2):
				#print("eee")
	#for x in range(2):
		#for y in range(2):
			#for z in range(2):
				#generate_chunk(Vector3(x,y,z))
	#generate_chunk(Vector3(0,0,0))
	#generate_chunk(Vector3(1,1,0))
	#generate_chunk(Vector3(2,0,0))
	#generate_chunk(Vector3(0,2,0))

var time_elapsed = setTimeHandle
func _physics_process(delta: float) -> void:
	#print(Global.get_player_pos().distance_to(chunks[Vector3(0,0,0)].position*16))
	
	
	var cnt = 0
	#handle_chunks_art()
	#print("ok")
	#old_handle_chunks()
	time_elapsed += delta
	if(time_elapsed >= setTimeHandle): 
		time_elapsed = 0
		#handle_chunks()
		handle_chunks_art()
	#for chunk in chunks.keys():
		#if chunks[chunk].static_body == null: cnt += 1
	#print(cnt)
	#pass

#func old_handle_chunks():
	#for x in range(WORLD_SIZE.x):
		#for y in range(WORLD_SIZE.y):
			#for z in range(WORLD_SIZE.z):
				##if ((Global.get_player_pos()/16).distance_to(chunks[Vector3(x,y,z)].position) > render_distance):
					##unload_chunk(Vector3(x,y,z))
				##else: load_chunk(Vector3(x,y,z))
				#var inRange = (Global.get_player_pos()/CHUNK_SIZE.x).distance_to(Vector3(x,y,z)) < render_distance
				#if(chunks.has(Vector3(x,y,z))):
					##pass
					#if (!inRange):
						#unload_chunk(Vector3(x,y,z))
					#else: 
						#load_chunk(Vector3(x,y,z))
				#else:
					#if (inRange && thread == null):
						##print("yeah")
						#spacer(Vector3(x,y,z))

#func handle_chunks():
	#var ins = 0
	#var outs = 0
	#var uns = 0
	#var los = 0
	#var pPos = Vector3i(Global.get_player_pos())
	#var cPositions = []
	#for x in range(-render_distance, render_distance):
		#for z in range(-render_distance, render_distance):
			#for y in range(-render_distance, render_distance):
				##if(pos == null): active_chunks[local_pos] = (next_pos)
				##print(local_pos)
				##print(pos)
				#var local_pos = Vector3(x,y,z)
				#var next_pos = Vector3((pPos)/CHUNK_SIZE.x + Vector3i(local_pos))
				#if(chunks.has(next_pos)):
					#var pos = active_chunks[local_pos]
					#var inRange = ((pPos)/CHUNK_SIZE.x).distance_to(next_pos) < render_distance
					##print(pos)
					#if(pos == null): pos = (next_pos)
					#else: inRange = ((pPos)/CHUNK_SIZE.x).distance_to(pos) < render_distance
					##pass
					#
					#if (!inRange):
						##print((Global.get_player_pos()/16).distance_to(pos))
						#unload_chunk(pos)
						#uns += 1
						#active_chunks[local_pos] = null
					#elif !active_chunks.has(next_pos):
						#los += 1
						#load_chunk(next_pos)
						#active_chunks[local_pos] = next_pos
						#cPositions.append(Vector3(x,y,z))
				#cPositions.append(Vector3(x,y,z))
	#
	#cPositions.sort_custom(Callable(self, "_compare_distance_from_center"))
	#
	#for local_pos in cPositions:
		#var next_pos = Vector3((pPos)/16 + Vector3i(local_pos))
		#
		#if(!chunks.has(next_pos)):
			#var inRange = ((pPos)/16).distance_to(next_pos) < render_distance
			##print(inRange)
			##if(inRange): ins += 1
			##else: outs += 1
			#if (thread == null && inRange):
				##print(next_pos)
				#spacer((next_pos))
	##print(str(ins) + "  " + str(outs))
	#
	##print(str(uns) + "  " + str(los))
	##var show = ""
	###var cnt = 0
	##for key in active_chunks.keys():
		##if active_chunks[key] == null: show += "n "
		##else: show += str(active_chunks[key].y) + " "
		###cnt+=1
	##print(show)

func handle_chunks_art():
	var pPos = Vector3i(Global.get_player_pos().floor() / CHUNK_SIZE.x)  # Player position floored to the chunk grid
	var active_set = {}  # Track which chunks should remain active
	var cnt = 0
	var cPositions = []
	#print(pPos)
	# Load and keep chunks in range
	for x in range(-render_distance, render_distance + 1):
		for y in range(-render_distance, render_distance + 1):
			for z in range(-render_distance, render_distance + 1):
				var offset = Vector3i(x, y, z)
				if offset.length() > render_distance:
					continue
				cPositions.append(offset)
				
	cPositions.sort_custom(Callable(self, "_compare_distance_from_center"))
	for offset in cPositions:
		var chunk_pos = Vector3(pPos + offset)
		active_set[chunk_pos] = true
		cnt += 1
		if chunks.has(chunk_pos):
			# Chunk exists but is not active, so activate it
			
			if !active_chunks.has(chunk_pos) && chunks[chunk_pos].generated:
				#print("c")
				load_chunk(chunk_pos)
				active_chunks[chunk_pos] = true
		elif thread == null:
			# Chunk doesn't exist, generate it using spacer
			spacer(chunk_pos)
			
	# Unload chunks out of range
	for chunk_pos in active_chunks.keys():
		if !active_set.has(chunk_pos):
			#print("u")
			unload_chunk(chunk_pos)
			active_chunks.erase(chunk_pos)
	#print(cnt)
func _compare_distance_from_center(a: Vector3, b: Vector3) -> bool:
	var dist_a = a.length_squared()  # Use squared distance for performance
	var dist_b = b.length_squared()
	if dist_a < dist_b:
		return true
	elif dist_a > dist_b:
		return false
	else:
		return false
#
#func handle_spawn_chunks():
	#for x in range(-render_distance, render_distance):
		#for z in range(-render_distance, render_distance):
			#for y in range(-render_distance, render_distance):
				##if ((Global.get_player_pos()/16).distance_to(chunks[Vector3(x,y,z)].position) > render_distance):
					##unload_chunk(Vector3(x,y,z))
				##else: load_chunk(Vector3(x,y,z))
				##var pos = Vector3i(Global.get_player_pos()/16) + Vector3i(x,y,z)
				##generate_position = pos
				##generate_data()
				##active_chunks[Vector3(x,y,z)] = pos
				###if (thread == null):
					####print("yeah")
					###spacer(pos)
				#active_chunks[Vector3(x,y,z)] = null
				##chunks[Vector3(x,y,z)] = null


func spacer(position: Vector3):
	# Ensure any previous thread is completed before starting a new one
	#if thread != null and thread.is_alive():
		#return  # Skip if the thread is already running
	#if thread != null:
		#thread.wait_to_finish()
		##await get_tree().create_timer(1).timeout
		##OS.delay_msec(500)
		#thread = null

	thread = Thread.new()
	
	#var args = {"position": position}
	generate_position = position
	#thread.start(self, "generate_chunk", args)
	#thread.start(Callable(self, "generate_data"))
	var result = thread.start(Callable(self, "generate_data"))
	if result != OK:
		print("a")
		thread = null


	

func generate_data():
	var position = generate_position
	#if !(0 <= position.x && position.x < WORLD_SIZE.x) && (0 <= position.y && position.y < WORLD_SIZE.y) && (0 <= position.z && position.z < WORLD_SIZE.z):
		#return
	#print("generate_data")
	# Step 1: Generate chunk data
	#var chunk = Chunk.new(generate_position)
	#chunk.chunk_data = create_chunk_data(generate_position)  # Define or generate the data for the chunk
	#var chunk_build_data = chunk_to_arrays(chunk)
	#chunk.chunk_build_data = chunk_build_data
	chunks[position] = Chunk.new(generate_position)
	chunks[position].chunk_data = create_chunk_data(position)  # Define or generate the data for the chunk

	# Step 2: Generate the chunk mesh
	
	var chunk_build_data = chunk_to_arrays(chunks[position])
	chunks[position].chunk_build_data = chunk_build_data
		
	var mesh_instance = MeshInstance3D.new()
	var mesh = ArrayMesh.new()
	
	#if(chunks[position].chunk_build_data[Mesh.ARRAY_VERTEX].size() != 0):
		##print(chunks[position].chunk_build_data.size())
		#mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, chunks[position].chunk_build_data)
	#if(chunk.chunk_build_data[Mesh.ARRAY_VERTEX].size() != 0):
		##print(chunks[position].chunk_build_data.size())
		#mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, chunk.chunk_build_data)
	#mesh_instance.mesh = mesh
	#material.shader = Shader.new()
	#material.shader.set_code(shader_code)
	#mesh_instance.material_override = material
	# Step 3: Position and add the chunk to the scene
	
	mesh_instance.transform.origin = position * Vector3(CHUNK_SIZE)
	chunks[position].mesh_instance = mesh_instance
	#chunk.mesh_instance = mesh_instance
	#print("yeah")
	
	call_deferred("generate_ready_chunk", position)
	

func generate_ready_chunk(position: Vector3):
	#print("generate_ready_chunk")
	#var loaded = true
	
	#chunks[position].loaded = loaded
	#load_chunk(position)
	#chunks[chunk.position] = chunk
	
	#print("oh yeah")
	#print(thread.is_alive())
	thread.wait_to_finish()
	thread = null
	chunks[position].generated = true
	#print("oh yeah")
	#if chunks[position].mesh_instance.get_parent() != null:
		#return  # Skip if already added
	#add_child(chunks[position].mesh_instance)
	
	

func unload_chunk(position: Vector3):
	# Remove and free the chunk if it exists
	#print("die")
	if position in chunks:
		#print("die")
		if(chunks[position].loaded):
			chunks[position].mesh_instance.queue_free()
			remove_block_colliders(position)
			chunks[position].loaded = false
		
		#chunks.erase(position)
		
func load_chunk(position: Vector3):
	# Remove and free the chunk if it exists
	#print("die")
	if position in chunks:
		#print("live")
		if(!chunks[position].loaded):
			if(chunks[position].chunk_build_data == null): return
			var loaded = true
			var mesh_instance = MeshInstance3D.new()
			var mesh = ArrayMesh.new()
			if(chunks[position].chunk_build_data[Mesh.ARRAY_VERTEX].size() != 0):
				mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, chunks[position].chunk_build_data)
			mesh_instance.mesh = mesh
			#material.shader = Shader.new()
			#material.shader.set_code(shader_code)
			#mesh_instance.material_override = material
			mesh_instance.material_override = mat
			mesh_instance.transform.origin = position * Vector3(CHUNK_SIZE)
			add_child(mesh_instance)
			call_deferred("generate_block_colliders", position)
			#generate_block_colliders(position)
			chunks[position].mesh_instance = mesh_instance
			chunks[position].loaded = loaded
			
func generate_block_colliders(position: Vector3):
	if chunks[position].collision_faces.size() > 0:
		# Create a new ConcavePolygonShape3D
		var shape = ConcavePolygonShape3D.new()
		shape.data = chunks[position].collision_faces

		# Add a StaticBody3D for the chunk's collision
		var static_body = StaticBody3D.new()
		add_child(static_body)

		# Attach a CollisionShape3D to the StaticBody3D
		#var collision_shape = CollisionShape3D.new()
		#collision_shape.shape = shape
		var shape_owner = static_body.create_shape_owner(static_body)
		static_body.shape_owner_add_shape(shape_owner, shape)
		static_body.global_transform.origin = position*Vector3(CHUNK_SIZE)
		#static_body.add_child(collision_shape)
		chunks[position].static_body = static_body
		#chunks[position].shape_owner = shape_owner
		#print(chunks[position].collision_faces[0])
		#print(chunks[position].static_body)
	#var chunk = chunks[position]
	#for x in range(CHUNK_SIZE.x):
		#for y in range(CHUNK_SIZE.y):
			#for z in range(CHUNK_SIZE.z):
				#var block = chunk.chunk_data[Vector3(x,y,z)]
				#if block != null: add_collider(position*CHUNK_SIZE.x + Vector3(x,y,z))
					
func remove_block_colliders(position: Vector3):
	if(chunks[position].static_body != null): chunks[position].static_body.queue_free()

# Called when a block is added
#func add_collider(position: Vector3i):
	##pass
	##print(position)
	#var chunk = chunks[position_to_chunk(position)]
	#var updPos = Vector3(position.x % CHUNK_SIZE.x, position.y % CHUNK_SIZE.y, position.z % CHUNK_SIZE.z)
	## Check if there's already a collider at this position
	##if chunk.block_colliders.has(updPos):
		##return  # Block already exists, no need to add a collider
	#
	#
	## Create a StaticBody3D for the block
	#var static_body = StaticBody3D.new()
	#add_child(static_body)
	#static_body.global_transform.origin = position * 1.0 + Vector3(0.5, 0.5, 0.5)
	#
	##print(static_body.global_transform.origin)
	## Add a BoxShape3D for collision
	#var shape = BoxShape3D.new()
	##var collision_shape = CollisionShape3D.new()
	##collision_shape.shape = shape
	##collision_shape.transform.origin = Vector3(0.5, .5, 0.5)
	##static_body.add_child(collision_shape)
	#var shape_owner = static_body.create_shape_owner(static_body)
	#static_body.shape_owner_add_shape(shape_owner, shape)
#
	## Store the reference to the collider
	#chunk.block_colliders[updPos] = static_body
	
	
func position_to_chunk(position: Vector3) -> Vector3:
	return floor(position/CHUNK_SIZE.x)

func chunk_to_arrays(chunk: Chunk) -> Array:
	var collision_faces = []
	var vertices = PackedVector3Array()  # PackedVector3Array for 3D vertices
	var vertex_colors = PackedColorArray()  # PackedVector3Array for 3D vertices
	var normals = PackedVector3Array()   # PackedVector3Array for normals
	var uvs = PackedVector2Array()       # PackedVector2Array for UVs
	var indices = PackedInt32Array()     # PackedInt32Array for indices
	
	#var flag = false
	for block_pos in chunk.chunk_data.keys():
		var block = chunk.chunk_data[block_pos]
		if block != null:
			#flag = true
			add_block_data(collision_faces, vertices, vertex_colors, normals, uvs, indices, block)
	
	var arrays = []
	chunk.collision_faces = collision_faces
	#if flag:
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	#arrays[Mesh.ARRAY_COLOR] = vertex_colors
	arrays[Mesh.ARRAY_INDEX] = indices

	return arrays
	
# Function to get the UVs for each face based on its position in the grid


#func is_face_exposed(x, y, z) -> bool:
	## Return true if the neighboring block is air or out of bounds
	#if x < 0 or x >= CHUNK_SIZE.x or y < 0 or y >= CHUNK_SIZE.y or z < 0 or z >= CHUNK_SIZE.z:
		#return true  # Outside the chunk, exposed
	#return chunk_data[x][y][z] == AIR  # Exposed if neighbor is air

# Main function to add block data
func add_block_data(collision_faces, vertices, vertex_colors, normals, uvs, indices, my_cube: Cube):
	var pos = my_cube.pos
	var position = my_cube.local_pos
	var start_index = vertices.size()
	var col_start_index = collision_faces.size()
	#print(str(start_index) + " a")
	if(get_block(pos + Vector3i(0,0,-1)) == null):
		# Front face (Z-)
		my_cube.sides_ndx[0] = start_index
		my_cube.col_sides_ndx[0] = col_start_index
		collision_faces.append_array([
			position + Vector3(0, 0, 0),  # Bottom-Left
			position + Vector3(1, 0, 0),  # Bottom-Right
			position + Vector3(1, 1, 0),  # Top-Right

			position + Vector3(0, 0, 0),  # Bottom-Left
			position + Vector3(1, 1, 0),  # Top-Right
			position + Vector3(0, 1, 0)   # Top-Left
		])
		vertices.append_array([
			position + Vector3(0, 0, 0),
			position + Vector3(1, 0, 0),
			position + Vector3(1, 1, 0),
			position + Vector3(0, 1, 0)
		])
		normals.append_array([Vector3(0, 0, -1), Vector3(0, 0, -1), Vector3(0, 0, -1), Vector3(0, 0, -1)])
		# Set UVs for the front face (mapped from the grid)
		uvs.append_array(my_cube.get_face_uvs(0))  # Front face uses the first part of the texture grid
		#vertex_colors.append_array([
		#Color(1, 0, 0), Color(1, 0, 0), Color(1, 0, 0), Color(1, 0, 0)  # Red
		#])
		indices.append_array([
			start_index, start_index + 1, start_index + 2,
			start_index + 2, start_index + 3, start_index
		])
		start_index += 4
		col_start_index += 6

	if(get_block(pos + Vector3i(0,0,1)) == null):
		# Back face (Z+)
		my_cube.sides_ndx[1] = start_index
		my_cube.col_sides_ndx[1] = col_start_index
		collision_faces.append_array([
			position + Vector3(0, 0, 1),  # Bottom-Left
			position + Vector3(1, 1, 1),  # Top-Right
			position + Vector3(1, 0, 1),  # Bottom-Right

			position + Vector3(0, 0, 1),  # Bottom-Left
			position + Vector3(0, 1, 1),  # Top-Left
			position + Vector3(1, 1, 1)   # Top-Right
		])
		vertices.append_array([
			position + Vector3(0, 0, 1),
			position + Vector3(1, 0, 1),
			position + Vector3(1, 1, 1),
			position + Vector3(0, 1, 1)
		])
		normals.append_array([Vector3(0, 0, 1), Vector3(0, 0, 1), Vector3(0, 0, 1), Vector3(0, 0, 1)])
		# Set UVs for the back face (mapped from the grid)
		uvs.append_array(my_cube.get_face_uvs(1))  # Back face uses the second part of the texture grid
		#vertex_colors.append_array([
		#Color(1, 1, 0), Color(1, 1, 0), Color(1, 1, 0), Color(1, 1, 0)  # Red
		#])
		indices.append_array([
			start_index, start_index + 2, start_index + 1,
			start_index, start_index + 3, start_index + 2
		])
		start_index += 4
		col_start_index += 6
	
	if(get_block(pos + Vector3i(-1,0,0)) == null):
		# Left face (X-)
		my_cube.sides_ndx[2] = start_index
		my_cube.col_sides_ndx[2] = col_start_index
		collision_faces.append_array([
			position + Vector3(0, 0, 0),  # Bottom-Left
			position + Vector3(0, 1, 1),  # Top-Right
			position + Vector3(0, 0, 1),  # Bottom-Right

			position + Vector3(0, 0, 0),  # Bottom-Left
			position + Vector3(0, 1, 0),  # Top-Left
			position + Vector3(0, 1, 1)   # Top-Right
		])
		vertices.append_array([
			position + Vector3(0, 0, 0),
			position + Vector3(0, 0, 1),
			position + Vector3(0, 1, 1),
			position + Vector3(0, 1, 0)
		])
		normals.append_array([Vector3(-1, 0, 0), Vector3(-1, 0, 0), Vector3(-1, 0, 0), Vector3(-1, 0, 0)])
		# Set UVs for the left face (mapped from the grid)
		uvs.append_array(my_cube.get_face_uvs(2))  # Left face uses the third part of the texture grid
		#vertex_colors.append_array([
		#Color(1, 0, 1), Color(1, 0, 1), Color(1, 0, 1), Color(1, 0, 1)  # Red
		#])
		indices.append_array([
			start_index, start_index + 2, start_index + 1,
			start_index, start_index + 3, start_index + 2
		])
		start_index += 4
		col_start_index += 6
	
	if(get_block(pos + Vector3i(1,0,0)) == null):
		# Right face (X+)
		my_cube.sides_ndx[3] = start_index
		my_cube.col_sides_ndx[3] = col_start_index
		collision_faces.append_array([
			position + Vector3(1, 0, 0),  # Bottom-Left
			position + Vector3(1, 0, 1),  # Bottom-Right
			position + Vector3(1, 1, 1),  # Top-Right

			position + Vector3(1, 0, 0),  # Bottom-Left
			position + Vector3(1, 1, 1),  # Top-Right
			position + Vector3(1, 1, 0)   # Top-Left
		])
		vertices.append_array([
			position + Vector3(1, 0, 0),
			position + Vector3(1, 0, 1),
			position + Vector3(1, 1, 1),
			position + Vector3(1, 1, 0)
		])
		normals.append_array([Vector3(1, 0, 0), Vector3(1, 0, 0), Vector3(1, 0, 0), Vector3(1, 0, 0)])
		# Set UVs for the right face (mapped from the grid)
		uvs.append_array(my_cube.get_face_uvs(3))  # Right face uses the fourth part of the texture grid
		#vertex_colors.append_array([
		#Color(0, 1, 1), Color(0, 1, 1), Color(0, 1, 1), Color(0, 1, 1)  # Red
		#])
		indices.append_array([
			start_index, start_index + 1, start_index + 2,
			start_index + 2, start_index + 3, start_index
		])
		start_index += 4
		col_start_index += 6
	
	if(get_block(pos + Vector3i(0,1,0)) == null):
		# Top face (Y+)
		my_cube.sides_ndx[4] = start_index
		my_cube.col_sides_ndx[4] = col_start_index
		collision_faces.append_array([
			position + Vector3(0, 1, 0),  # Bottom-Left
			position + Vector3(1, 1, 0),  # Bottom-Right
			position + Vector3(1, 1, 1),  # Top-Right

			position + Vector3(0, 1, 0),  # Bottom-Left
			position + Vector3(1, 1, 1),  # Top-Right
			position + Vector3(0, 1, 1)   # Top-Left
		])
		vertices.append_array([
			position + Vector3(0, 1, 0),
			position + Vector3(1, 1, 0),
			position + Vector3(1, 1, 1),
			position + Vector3(0, 1, 1)
		])
		normals.append_array([Vector3(0, 1, 0), Vector3(0, 1, 0), Vector3(0, 1, 0), Vector3(0, 1, 0)])
		# Set UVs for the top face (mapped from the grid)
		uvs.append_array(my_cube.get_face_uvs(4))  # Top face uses the fifth part of the texture grid
		#vertex_colors.append_array([
		#Color(0, 0, 1), Color(0, 0, 1), Color(0, 0, 1), Color(0, 0, 1)  # Red
		#])
		indices.append_array([
			start_index, start_index + 1, start_index + 2,
			start_index + 2, start_index + 3, start_index
		])
		start_index += 4
		col_start_index += 6
	
	if(get_block(pos + Vector3i(0,-1,0)) == null):
		# Bottom face (Y-)
		my_cube.sides_ndx[5] = start_index
		my_cube.col_sides_ndx[5] = col_start_index
		collision_faces.append_array([
			position + Vector3(0, 0, 0),  # Bottom-Left
			position + Vector3(1, 0, 1),  # Top-Right
			position + Vector3(1, 0, 0),  # Bottom-Right

			position + Vector3(0, 0, 0),  # Bottom-Left
			position + Vector3(0, 0, 1),  # Top-Left
			position + Vector3(1, 0, 1)   # Top-Right
		])
		vertices.append_array([
			position + Vector3(0, 0, 0),
			position + Vector3(1, 0, 0),
			position + Vector3(1, 0, 1),
			position + Vector3(0, 0, 1)
		])
		normals.append_array([Vector3(0, -1, 0), Vector3(0, -1, 0), Vector3(0, -1, 0), Vector3(0, -1, 0)])
		# Set UVs for the bottom face (mapped from the grid)
		uvs.append_array(my_cube.get_face_uvs(5))  # Bottom face uses the sixth part of the texture grid
		#vertex_colors.append_array([
		#Color(0, 1, 0), Color(0, 1, 0), Color(0, 1, 0), Color(0, 1, 0)  # Red
		#])
		indices.append_array([
			start_index, start_index + 2, start_index + 1,
			start_index, start_index + 3, start_index + 2
		])
		#start_index += 4
	#print(str(start_index) + " b")
		
	
#--------------------- world generation to look beautiful <3 -------------------------------
func create_chunk_data(pos: Vector3):
	var chunk: Dictionary = {}
	for x in range(CHUNK_SIZE.x):
		for y in range(CHUNK_SIZE.y):
			for z in range(CHUNK_SIZE.z):
				# Example: Fill with blocks (non-null) or leave as air (null)
				#if y == 15:  # Ground level
					#chunk[x][y].append(null)  # Block type
				#else:
					#chunk[x][y].append(null) # Air
				#if (pos.y*CHUNK_SIZE.y + y < 64):
				var p = pos*CHUNK_SIZE.x + Vector3(x,y,z)
				var c = null
				#var my_noise = noise.get_noise_3dv(p)# + p.y/5
				##if(int(pos.y) % 2 == 1):
					##c = Cube.new("dirt", p)
				##elif (my_noise < 0.1):
					##c = Cube.new("stone", p)
				#if(my_noise < 0.1):
					#c = Cube.new("stone", p)
				#else: c = null
				
				var abyss = Global.AbyssStrength(p.x, p.z, p.y)
				var layer = Global.AbyssLayer(p.y)
				
				var surface_height = base_surface(p)
				if p.y > surface_height:
					chunk[Vector3(x,y,z)] = null
					continue
				
				var cave_noise = noise.get_noise_3dv(p * Global.LayerNoiseScale[layer])
				var threshold = 0.6
				threshold -= abyss * 0.8
				threshold -= abs(p.y - Global.SurfaceLevel) * 0.002
				#print(threshold)
				if cave_noise < threshold:
					c = Cube.new("stone", p)
					
				if c: chunk[Vector3(x,y,z)] = c 
				#else: chunk[x][y].append(null) # Air
	return chunk

func base_surface(p: Vector3):
	return noise.get_noise_2d(p.x, p.z) * 5.0 + Global.SurfaceLevel
	
func surface_ease(p: Vector3, surface: float, strength: float):
	return clamp(strength - (p.y - surface), 0.0, 1.0)

func get_block(pos: Vector3i):
	var ptc = position_to_chunk(pos)
	if !chunks.has(ptc):
		return null
	
	var chunk = chunks[ptc]
	var updPos = Vector3(pos.x % CHUNK_SIZE.x, pos.y % CHUNK_SIZE.y, pos.z % CHUNK_SIZE.z)
	
	# Use .get() with default value
	return chunk.chunk_data.get(Vector3(updPos.x, updPos.y, updPos.z), null)
	# If key doesn't exist, returns null (air block)
	
func set_block(pos: Vector3i, value):
	var ptc = position_to_chunk(pos)
	if !chunks.has(ptc):
		return
	
	var chunk = chunks[ptc]
	var updPos = Vector3(pos.x % CHUNK_SIZE.x, pos.y % CHUNK_SIZE.y, pos.z % CHUNK_SIZE.z)
	var key = Vector3(updPos.x, updPos.y, updPos.z)
	
	# Remove old block data if it exists
	if chunk.chunk_data.has(key):
		if chunk.chunk_data[key]: remove_block_data(chunk.chunk_data[key])
	
	if value == null:
		# Setting to air - remove from dictionary!
		chunk.chunk_data.erase(key)  # ← DELETE the entry!
	else:
		# Setting to solid block
		var c = Cube.new(value, pos)
		chunk.chunk_data[key] = c
		update_cube(c)
	
func break_block(pos: Vector3i):
	#print(chunks[updPos].chunk_build_data[Mesh.ARRAY_INDEX][0])
	#print(chunks[updPos].chunk_build_data[Mesh.ARRAY_INDEX][1])
	#print(chunks[updPos].chunk_build_data[Mesh.ARRAY_INDEX][2])
	#print(chunks[updPos].chunk_build_data[Mesh.ARRAY_INDEX][3])
	#for i in range(200):
		#chunks[updPos].chunk_build_data[Mesh.ARRAY_INDEX][i] = 0
		
	#erase current block visual and collider
	
	set_block(pos, null)
	var updPos = position_to_chunk(pos)
	
	#erase and replace (updated) visuals for adjacent blocks
	update_adjacents(pos)
	refresh_chunk(updPos)
	refresh_chunk_adjacents(updPos)
	#chunks[updPos].chunk_build_data = chunk_to_arrays(chunks[updPos])
	#if(chunks[updPos].chunk_build_data.size() != 0):
		#chunks[updPos].mesh_instance.mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, chunks[updPos].chunk_build_data)
	#unload_chunk(updPos)
	#load_chunk(updPos)
	
func place_block(pos: Vector3, type: String):
	#if !in_bounds(pos): 
		#print("out of bounds")
		#return
	print("place")
	set_block(pos, type)
	var updPos = position_to_chunk(pos)
	
	#erase and replace (updated) visuals for adjacent blocks
	update_adjacents(pos)
	refresh_chunk(updPos)
	refresh_chunk_adjacents(updPos)
	
func refresh_chunk(pos):
	
	if(chunks.has(pos)):
		var chunk = chunks[pos]
		if(chunk.loaded):
			if(chunk.chunk_build_data[Mesh.ARRAY_VERTEX].size() != 0):
			#if chunks[pos].chunk_build_data[Mesh.ARRAY_VERTEX].size() != 0:
				chunk.mesh_instance.mesh.clear_surfaces()
				chunk.mesh_instance.mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, chunk.chunk_build_data)
			if(chunk.collision_faces.size() != 0 && chunk.static_body != null):
				var shape_owner = chunk.static_body.get_shape_owners()[0]
				var shape = chunk.static_body.shape_owner_get_shape(shape_owner, 0)
				shape.data = chunk.collision_faces
				chunk.static_body.shape_owner_remove_shape(shape_owner, 0)
				chunk.static_body.shape_owner_add_shape(shape_owner, shape)

func refresh_chunk_adjacents(pos: Vector3):
	refresh_chunk((pos + Vector3(0,0,-1)))
	refresh_chunk((pos + Vector3(0,0,1)))
	refresh_chunk((pos + Vector3(-1,0,0)))
	refresh_chunk((pos + Vector3(1,0,0)))
	refresh_chunk((pos + Vector3(0,1,0)))
	refresh_chunk((pos + Vector3(0,-1,0)))
	pass

func update_adjacents(pos: Vector3i):
	update_cube(get_block(pos + Vector3i(0,0,-1)))
	update_cube(get_block(pos + Vector3i(0,0,1)))
	update_cube(get_block(pos + Vector3i(-1,0,0)))
	update_cube(get_block(pos + Vector3i(1,0,0)))
	update_cube(get_block(pos + Vector3i(0,1,0)))
	update_cube(get_block(pos + Vector3i(0,-1,0)))
	pass
	
func remove_block_data(my_cube: Cube):
	#print(chunks[position_to_chunk(my_cube.pos)].chunk_build_data[Mesh.ARRAY_INDEX].size())
	#var indices = (chunks[position_to_chunk(my_cube.pos)].chunk_build_data[Mesh.ARRAY_INDEX])
	
	var chunk = chunks[position_to_chunk(my_cube.pos)]
	var vertices = chunk.chunk_build_data[Mesh.ARRAY_VERTEX]
	var col_vertices = chunk.collision_faces

	for i in range(6):
		var s = my_cube.sides_ndx[i]
		if s != null:
			for g in range(4):  # Loop through the 3 indices of the face
				var index = s+g#indices[s + g]
				vertices[index] = Vector3(0, 0, 0)
		var cs = my_cube.col_sides_ndx[i]
		if cs != null:
			for j in range(6):  # Loop through the 3 indices of the face
				var index = cs+j#indices[s + g]
				col_vertices[index] = Vector3(0, 0, 0)
			#print(my_cube.sides_ndx)
			#for g in range(5, -1, -1):  # Reverse order to avoid shifting
				#indices.remove_at(s + g)
			#my_cube.sides_ndx[i] = null
			
			
	#chunks[position_to_chunk(my_cube.pos)].chunk_build_data[Mesh.ARRAY_INDEX] = PackedInt32Array(indices)
	#print(shape_owner)
	pass

func update_cube(my_cube: Cube):
	if(my_cube == null): return
	#print(my_cube.pos)
	var chunk = chunks[position_to_chunk(my_cube.pos)]
	#if my_cube.sides_ndx.any(func(element): return element != null): 
	#if chunk.chunk_build_data != []:
	remove_block_data(my_cube)
	add_block_data(chunk.collision_faces, chunk.chunk_build_data[Mesh.ARRAY_VERTEX], chunk.chunk_build_data[Mesh.ARRAY_COLOR], chunk.chunk_build_data[Mesh.ARRAY_NORMAL], chunk.chunk_build_data[Mesh.ARRAY_TEX_UV], chunk.chunk_build_data[Mesh.ARRAY_INDEX], my_cube)

	#chunk.chunk_data[Mesh.ARRAY_VERTEX] = vertices
	#arrays[Mesh.ARRAY_NORMAL] = normals
	#arrays[Mesh.ARRAY_TEX_UV] = uvs
	#arrays[Mesh.ARRAY_COLOR] = vertex_colors
	#arrays[Mesh.ARRAY_INDEX] = indices
#func init_chunks_array():
	#for i in range(WORLD_SIZE.x*16):
		#var second_dimension: Array = []
		#for j in range(WORLD_SIZE.y*16):
			#var third_dimension: Array = []
			#for k in range(WORLD_SIZE.z*16):
				#third_dimension.append(null)  # Initialize each cell with null (or any default value)
			#second_dimension.append(third_dimension)
		#chunks.append(second_dimension)
				
#func in_bounds(pos: Vector3):
	#pos /= CHUNK_SIZE.x
	#var flag = true
	#if pos.x < 0 or pos.x >= WORLD_SIZE.x or pos.y < 0 or pos.y >= WORLD_SIZE.y or pos.z < 0 or pos.z >= WORLD_SIZE.z:
		#flag = false
	#return flag
	
#func become_host():
	##print("become host")
	#$MultiplayerHub.hide()
	
	#MultiplayerManager.become_host()
	#
#func join_as_player():
	##print("join as player")
	#$MultiplayerHub.hide()
	#MultiplayerManager.join_as_player()

func _process(delta):
	if Input.is_action_just_pressed("ui_cancel"):  # Press ESC
		print("\n=== DETAILED MEMORY DEBUG ===")
		print("chunks.size(): ", chunks.size())
		print("active_chunks.size(): ", active_chunks.size())
		
		# Count actual objects
		var total_cubes = 0
		var total_mesh_instances = 0
		var total_static_bodies = 0
		var chunks_with_data = 0
		var chunks_loaded = 0
		
		for pos in chunks.keys():
			var chunk = chunks[pos]
			
			if chunk.loaded:
				chunks_loaded += 1
			
			if chunk.chunk_data.size() > 0:
				chunks_with_data += 1
				total_cubes += chunk.chunk_data.size()
			
			if chunk.mesh_instance != null:
				total_mesh_instances += 1
			
			if chunk.static_body != null:
				total_static_bodies += 1
		
		print("\nChunks with data: ", chunks_with_data)
		print("Chunks loaded: ", chunks_loaded)
		print("Total Cube objects: ", total_cubes)
		print("Total MeshInstance3D: ", total_mesh_instances)
		print("Total StaticBody3D: ", total_static_bodies)
		
		# Sample a single chunk to see its data
		if chunks.size() > 0:
			var sample_pos = chunks.keys()[0]
			var sample = chunks[sample_pos]
			print("\n=== SAMPLE CHUNK ===")
			print("Position: ", sample_pos)
			print("Loaded: ", sample.loaded)
			print("Generated: ", sample.generated)
			print("Block count: ", sample.chunk_data.size())
			
			if sample.chunk_build_data != null:
				print("Has chunk_build_data: YES")
				if sample.chunk_build_data.has(Mesh.ARRAY_VERTEX):
					print("Vertices: ", sample.chunk_build_data[Mesh.ARRAY_VERTEX].size())
			else:
				print("Has chunk_build_data: NO")
			
			print("Collision faces: ", sample.collision_faces.size())
