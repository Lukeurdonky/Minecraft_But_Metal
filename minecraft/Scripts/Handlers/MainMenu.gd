extends Control

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _on_new_run_button_pressed() -> void:
	# StartNewRun() owns the scene change now — it rolls a random planet and goes straight to
	# CubeLand. Changing scene here too would race it and land on the wrong scene.
	RunManager.StartNewRun()

func _on_quit_button_pressed() -> void:
	get_tree().quit()
