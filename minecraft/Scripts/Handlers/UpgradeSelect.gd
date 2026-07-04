extends Control

# Shown after every planet clear (RunManager.CompleteStage), before PlanetSelect.
# Picking one calls RunManager.ChooseAccessory(index), which equips it and
# transitions on to PlanetSelect.tscn.

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_build_options()

func _build_options() -> void:
	for child in $Center/OptionsPanel/List.get_children():
		child.queue_free()
	var options: Array = RunManager.GetAccessoryOptionsForUI()
	for i in options.size():
		var opt: Dictionary = options[i]
		var btn := Button.new()
		btn.text = "%s  —  %s" % [opt["name"], opt["description"]]
		btn.pressed.connect(_on_option_pressed.bind(i))
		$Center/OptionsPanel/List.add_child(btn)

func _on_option_pressed(index: int) -> void:
	RunManager.ChooseAccessory(index)
