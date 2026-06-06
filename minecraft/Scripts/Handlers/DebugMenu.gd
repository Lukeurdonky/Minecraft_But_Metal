extends Label

@export var Player: Node3D

# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	# Collect statistics
	var fps = Engine.get_frames_per_second()
	var frame_time = delta * 1000  # Convert frame time to milliseconds
	var memory = (OS.get_static_memory_usage() + 0.0) / (1024 * 1024)  # Convert bytes to MB
	var coords = Vector3i(Player.global_transform.origin)
	# var inventory = Player.Inventory.get_items()  # ARCHIVED: inventory system removed

	text = "FPS: %d\nFrame Time: %.2f ms\nMemory Usage: %.2f MB\nCoordinates: %s" % [
		fps, frame_time, memory, coords
	]
