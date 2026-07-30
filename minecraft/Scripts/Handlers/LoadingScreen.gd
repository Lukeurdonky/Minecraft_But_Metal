extends Control

# Inter-planet interstitial, shown by RunManager.CompleteStage(). The two loading frames animate
# behind a pick-one-of-three accessory list; choosing drops the player straight onto a randomly
# chosen planet (RunManager.ChooseAccessory -> GoToRandomPlanet). This replaces the old
# UpgradeSelect -> PlanetSelect pair — PlanetSelect is shelved, not deleted, and its scene and
# script are untouched on disk (see the shelved-selection block in RunManager.cs).
#
# Despite the name, nothing here waits on real chunk generation: CubeLand still builds its own
# terrain after the scene change. This is a beat between planets, not a progress bar.

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	$Frames.play(&"travel")
	# Last stage cleared has no next planet to travel to, so the accessory pick would lead
	# nowhere — swap in the end-of-run panel instead. This is the state PlanetSelect's
	# CompletePanel used to own.
	if RunManager.RunComplete:
		$Choices.visible = false
		$Complete.visible = true
		return
	$Choices.visible = true
	$Complete.visible = false
	_build_options()

func _build_options() -> void:
	for child in $Choices/List.get_children():
		child.queue_free()
	var options: Array = RunManager.GetAccessoryOptionsForUI()
	for i in options.size():
		var opt: Dictionary = options[i]
		var btn := Button.new()
		btn.text = "%s  —  %s" % [opt["name"], opt["description"]]
		btn.pressed.connect(_on_option_pressed.bind(i))
		$Choices/List.add_child(btn)

func _on_option_pressed(index: int) -> void:
	RunManager.ChooseAccessory(index)

func _on_return_button_pressed() -> void:
	RunManager.EndRun()
	get_tree().change_scene_to_file("res://Scenes/MainMenu.tscn")
