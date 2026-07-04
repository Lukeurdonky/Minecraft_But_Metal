extends Control

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	if RunManager.RunComplete:
		$Center/OptionsPanel.visible = false
		$Center/CompletePanel.visible = true
		return
	$Center/OptionsPanel.visible = true
	$Center/CompletePanel.visible = false
	_build_options()

func _build_options() -> void:
	for child in $Center/OptionsPanel/List.get_children():
		child.queue_free()
	var options: Array = RunManager.GetOptionsForUI()
	for i in options.size():
		var opt: Dictionary = options[i]
		var btn := Button.new()
		btn.text = "%s  —  %s" % [opt["biome"], opt["difficulty"]]
		btn.pressed.connect(_on_option_pressed.bind(i))
		$Center/OptionsPanel/List.add_child(btn)

func _on_option_pressed(index: int) -> void:
	RunManager.ChooseOption(index)

func _on_return_button_pressed() -> void:
	RunManager.EndRun()
	get_tree().change_scene_to_file("res://Scenes/MainMenu.tscn")
