extends Node3D


#const CubeManager = preload("res://Scripts/cube.gd")
## Called when the node enters the scene tree for the first time.
#func _ready() -> void:
	#pass # Replace with function body.
#
#
## Called every frame. 'delta' is the elapsed time since the previous frame.
#func _process(delta: float) -> void:
	#pass

# Function to spawn a cube at a given position
#func spawn_cube(position: Vector3):
	## Instance the cube from the prefab
	#var cube_instance = cube_scene.instantiate()
#
	## Set the position of the cube
	#cube_instance.transform.origin = position
#
	## Add the cube instance to the scene
	#add_child(cube_instance)
	
#func spawn_chunk(position: Vector3):
	#for x in range(16):
		#for y in range(16):
			#for z in range(16):
				#CubeManager.spawn_cube(Cube.CubeType.GRASS, position + Vector3(x,y,z))
