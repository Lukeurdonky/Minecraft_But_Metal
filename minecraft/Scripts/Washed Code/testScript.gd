extends Node3D

func _init():
	print("hello world!")
	
func _ready():
	#print(position)
	position = Vector3(0, 2, 0)
	#print(position)

#func _process(delta):
	#position += Vector3(1, 0, 0)
	#_cube()
	#print(position)

func _cube():
	print("cube")
