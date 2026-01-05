extends Node3D

var target_node: MeshInstance3D
var raycast: PhysicsRayQueryParameters3D

#var camera: Camera3D

func _ready():
	#print(global_transform.origin)
	#camera = get_node("Camera3D")
	if target_node == null:
		#print("Warning: target_node is not assigned for ", self)
		target_node = get_child(0)
	
		#visible = false
	#target_node.hide()
	#if !target_node.visible:
		#raycast = PhysicsRayQueryParameters3D.new()
		#
		#raycast.from = global_transform.origin + (Global.get_player_pos()-global_transform.origin).normalized()*dist
		#raycast.to = Global.get_player_pos()#camera.global_transform.origin
		##raycast.collide_with_areas = true  # Optionally, collide with areas
		#raycast.collide_with_bodies = true  # Optionally, collide with physics bodies
		#var space_state = get_world_3d().direct_space_state
		#var result = space_state.intersect_ray(raycast)
#
		#if result:
			#target_node.hide()
			##print("hide")
		#else:
			#target_node.show()
	#print(raycast.global_transform.origin)
	#print(global_transform.origin)
	

	#if not is_connected("screen_entered", Callable(self, "_on_screen_entered")):
		#connect("screen_entered", Callable(self, "_on_screen_entered"))
	#if not is_connected("screen_exited", Callable(self, "_on_screen_exited")):
		#connect("screen_exited", Callable(self, "_on_screen_exited"))




#func _on_screen_entered() -> void:
	##print("show")
	##target_node.show()
	#pass
	#
#func _on_screen_exited() -> void:
	##print("hide")
	##target_node.hide()
	#pass
