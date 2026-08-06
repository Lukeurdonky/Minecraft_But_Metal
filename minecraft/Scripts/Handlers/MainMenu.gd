extends Control

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _on_new_run_button_pressed() -> void:
	# StartNewRun() owns the scene change — it rolls three system offers and goes to
	# SolarSelect. Changing scene here too would race it and land on the wrong scene.
	RunManager.StartNewRun()

func _on_builder_button_pressed() -> void:
	# Dev tool, not part of the run — it deliberately skips RunManager entirely.
	get_tree().change_scene_to_file("res://Scenes/StructureBuilder.tscn")

func _on_quit_button_pressed() -> void:
	get_tree().quit()
