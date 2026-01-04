extends Label

@export var Player: Node3D

# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	# Collect statistics
	var fps = Engine.get_frames_per_second()
	var frame_time = delta * 1000  # Convert frame time to milliseconds
	var memory = (OS.get_static_memory_usage() + 0.0) / (1024 * 1024)  # Convert bytes to MB
	var coords = Vector3i(Player.global_transform.origin)
	var inventory = Player.inventory.get_items()
	#var coords_x = "%.1f" % coords.x
	#var coords_y = "%.1f" % coords.y
	#var coords_z = "%.1f" % coords.z
	#var video_memory = RenderingServer.get_video_memory_usage() / (1024 * 1024)  # Video memory usage in MB

	# Update the text with the stats
	text = "FPS: %d\nFrame Time: %.2f ms\nMemory Usage: %.2f MB\nCoordinates: %s\nInventory: %s" % [
		fps, frame_time, memory, coords, inventory
	]
