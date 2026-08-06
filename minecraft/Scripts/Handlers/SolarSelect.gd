extends Control

# First screen of an attempt: pick 1 of 3 solar systems (Easy / Medium / Hard).
# Committing is final — no switching, no returning to the two you passed on
# (Decision 01, solar-system-run-structure-design-log.html).
#
# The three art sheets are each a full 1920x1080 layer that is transparent except
# for its own vertical third, so they stack into one composed screen and each can
# be highlighted independently. The bboxes below are the measured opaque regions:
#   easy   x 0    .. 644
#   medium x 645  .. 1284
#   hard   x 1285 .. 1919
#
# All three sheets sit at IDLE_MODULATE (a grey multiply) and the hovered one goes
# to white. Because the art files themselves are full-brightness and the dimming is
# the modulate, 1,1,1 is a real brightening back to the authored colour rather than
# a no-op — no shader needed.

#
# One scene, used two ways — the same split SolarMap.gd makes:
#
#   full screen  — reached directly (F6, or the old MainMenu -> SolarSelect route). BACK
#                  ends the attempt and returns to the main menu.
#   MODE_OVERLAY — instanced over the ship hub from mission control. Pauses the tree,
#                  and BACK closes the overlay to put you back on the ship. Committing to
#                  a system still leaves for SolarMap, because that choice is final.

signal select_closed

@export var overlay_mode := false

@export var idle_modulate := Color(0.6313726, 0.6313726, 0.6313726, 1.0)
@export var hover_modulate := Color(1.0, 1.0, 1.0, 1.0)
@export var hover_fade_time := 0.12

# Tier order must match RunManager.GetOffersForUI(), which walks
# SolarSystemDescriptor.Tiers in declaration order.
const TIER_NAMES := ["EASY", "MEDIUM", "HARD"]

var _panels: Array[TextureRect] = []
var _hitboxes: Array[Button] = []
var _tweens: Array = [null, null, null]
var _hovered := -1
var _offers: Array = []


func _ready() -> void:
	# Overlay runs on top of a paused hub, so it has to keep processing itself.
	if overlay_mode:
		process_mode = Node.PROCESS_MODE_ALWAYS
		get_tree().paused = true
		$Header/BackButton.text = "BACK TO SHIP"
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

	_panels = [$Systems/Easy, $Systems/Medium, $Systems/Hard]
	_hitboxes = [$Hitboxes/EasyHit, $Hitboxes/MediumHit, $Hitboxes/HardHit]

	for i in _hitboxes.size():
		var btn := _hitboxes[i]
		btn.mouse_entered.connect(_on_hover.bind(i))
		btn.mouse_exited.connect(_on_unhover.bind(i))
		btn.focus_entered.connect(_on_hover.bind(i))
		btn.focus_exited.connect(_on_unhover.bind(i))
		btn.pressed.connect(_on_pressed.bind(i))
		_panels[i].modulate = idle_modulate

	# Reaching this scene directly (F6 in the editor) skips MainMenu, which is where
	# offers are normally rolled — generate a set so the screen is never blank.
	RunManager.EnsureOffers()
	_offers = RunManager.GetOffersForUI()
	_build_previews()

	_hitboxes[0].grab_focus()


# Topology preview only: count, clock, waystations, flavour — never the actual
# biomes or enemy composition (Decision 02).
func _build_previews() -> void:
	for i in _panels.size():
		var info := $Info.get_child(i)
		var tier: String = TIER_NAMES[i] if i < TIER_NAMES.size() else "?"

		if i >= _offers.size():
			info.get_node("Tier").text = tier
			info.get_node("Designation").text = "—"
			info.get_node("Stats").text = "no offer generated"
			info.get_node("Modifiers").text = ""
			continue

		var o: Dictionary = _offers[i]
		info.get_node("Tier").text = tier
		info.get_node("Designation").text = str(o["name"])

		var mins := int(float(o["time_limit"])) / 60
		var secs := int(float(o["time_limit"])) % 60
		var lines := [
			"%d PLANETS" % int(o["planets"]),
			"%d WAYSTATION%s" % [int(o["waystations"]), "" if int(o["waystations"]) == 1 else "S"],
			"%d:%02d CLOCK" % [mins, secs],
			"HOSTILITY x%.2f" % float(o["density"]),
		]
		info.get_node("Stats").text = "\n".join(lines)

		var mods: Array = o["modifiers"]
		info.get_node("Modifiers").text = " · ".join(mods) if not mods.is_empty() else "NO MODIFIERS"


func _on_hover(index: int) -> void:
	if _hovered == index:
		return
	if _hovered != -1:
		_fade_to(_hovered, idle_modulate)
	_hovered = index
	_fade_to(index, hover_modulate)


func _on_unhover(index: int) -> void:
	if _hovered != index:
		return
	_hovered = -1
	_fade_to(index, idle_modulate)


func _on_pressed(index: int) -> void:
	if index >= _offers.size():
		return
	# Committing is final (Decision 01), so this leaves the ship for good. The overlay's
	# pause has to be lifted here: `paused` is a tree flag, not a scene one, and it would
	# otherwise carry straight into SolarMap and freeze its buttons.
	if overlay_mode:
		get_tree().paused = false
	# ChooseSystem owns the scene change (-> SolarMap). Changing scene here too would
	# race it, the same way MainMenu's New Run button defers to StartNewRun.
	RunManager.ChooseSystem(index)


func _fade_to(index: int, target: Color) -> void:
	# Kill the in-flight tween first — sweeping the mouse across panels quickly
	# otherwise leaves two tweens writing the same modulate and it settles on neither
	# endpoint. Same guard PlanetSelect.gd uses on its slide tween.
	var t = _tweens[index]
	if t != null and t.is_valid():
		t.kill()
	var tw := create_tween()
	tw.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
	tw.tween_property(_panels[index], "modulate", target, hover_fade_time)
	_tweens[index] = tw


func _unhandled_input(event: InputEvent) -> void:
	if not overlay_mode:
		return
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		close_overlay()


func _on_back_button_pressed() -> void:
	# Backing out of the overlay returns to the ship — nothing has been committed yet, so
	# it must not end the attempt the way the full-screen route does.
	if overlay_mode:
		close_overlay()
		return
	# Full-screen route (reached directly, e.g. F6) — backing out here is an abandon, so it
	# goes to the ship like every other run ending. ReturnToShip owns the scene change.
	RunManager.ReturnToShip()


func close_overlay() -> void:
	get_tree().paused = false
	# The ship captures the mouse for camera look; handing it back is the opener's job via
	# this signal, since only it knows what the mode was before mission control opened.
	select_closed.emit()
	queue_free()
