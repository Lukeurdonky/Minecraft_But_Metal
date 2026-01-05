extends Node3D

var data
var was_visible: bool

func get_adjacents():
	var space_state: PhysicsDirectSpaceState3D = get_world_3d().direct_space_state
	var query = PhysicsShapeQueryParameters3D.new()
	
	query.shape = SphereShape3D.new() #shape
	query.shape.radius = 0.5
	query.transform = Transform3D(Basis(), global_transform.origin) #origin
	query.motion = Vector3.ZERO #direction
	query.margin = 0.01 #margin
	query.collision_mask = 1
	
	var results = space_state.intersect_shape(query)
	#var cm = Global.CubeManager
	#var p = global_transform.origin
	#var results = []
	#results.append(cm.get_cube(p+Vector3(1,0,0)))
	#results.append(cm.get_cube(p+Vector3(-1,0,0)))
	#results.append(cm.get_cube(p+Vector3(0,1,0)))
	#results.append(cm.get_cube(p+Vector3(0,-1,0)))
	#results.append(cm.get_cube(p+Vector3(0,0,1)))
	#results.append(cm.get_cube(p+Vector3(0,0,-1)))
	#results = results.filter(func(x): return x != null)
	return results

func update_adjacents():
	#print("update_adjacents")
	var results = get_adjacents()
	for res in results:
		#print(res.node)
		res.collider.get_parent().get_parent().data.node.update_cube()
		

func update_cube():
	#var updAdj = get_adjacents().filter(func(x): return x.collider.get_parent().get_parent().data.full_block != false)
	var updAdj = get_adjacents()
	#print("d")
	#print(str(get_adjacents().size()) + " " + str(updAdj.size()))
	if updAdj.size() == 7:
		was_visible = false
		deactivate()
	else:
		was_visible = true
		activate()
		
func destroy_cube():
	#drop items, particles, and do a bunch of stuff
	#print("dead")
	data.collider.disabled = true
	update_adjacents()
	Global.CubeManager.spawn_cube(0, global_transform.origin)
	queue_free()
	
func activate():
	data.active = true
	data.mesh_instance.visible = true
	
func deactivate():
	data.active = false
	data.mesh_instance.visible = false
