extends Control

# EvilPlan1 is a full-screen-sized sprite whose art sits against the right edge, with a grey
# handle tab drawn on the panel's left side. Toggling slides the whole sprite right until only
# that tab is still on screen. ToggleButton is parented to the sprite so it rides along — which
# is the point: a button pinned to the panel body would slide off with it and leave nothing to
# click to bring the panel back.
@export var evil_plan_slide_distance := 318.0 # px right; whatever's left over is the visible tab
@export var evil_plan_slide_time := 0.35

var _evil_plan_shown_pos: Vector2
var _evil_plan_open := true
var _evil_plan_tween: Tween

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	# Captured before the RunComplete early-out below, so the toggle still works on the win screen.
	_evil_plan_shown_pos = $EvilPlan1.position
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

func _on_evil_plan_toggle_pressed() -> void:
	_evil_plan_open = not _evil_plan_open
	# Kill the in-flight tween before starting the next one. Clicking mid-slide otherwise leaves
	# two tweens writing the same property, and the panel settles on neither endpoint.
	if _evil_plan_tween != null and _evil_plan_tween.is_valid():
		_evil_plan_tween.kill()
	var target := _evil_plan_shown_pos
	if not _evil_plan_open:
		target += Vector2(evil_plan_slide_distance, 0)
	_evil_plan_tween = create_tween()
	_evil_plan_tween.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	_evil_plan_tween.tween_property($EvilPlan1, "position", target, evil_plan_slide_time)


func _on_return_button_pressed() -> void:
	# SHELVED screen — kept working rather than deleted. ReturnToShip owns the scene change.
	RunManager.ReturnToShip()
