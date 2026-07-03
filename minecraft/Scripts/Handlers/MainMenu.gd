extends Control

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _on_new_run_button_pressed() -> void:
	RunManager.StartNewRun()
	get_tree().change_scene_to_file("res://Scenes/PlanetSelect.tscn")

func _on_quit_button_pressed() -> void:
	get_tree().quit()
