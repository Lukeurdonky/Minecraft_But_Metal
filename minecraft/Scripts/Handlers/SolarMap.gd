extends Control

# The solar system topology map. One scene, used two ways:
#
#   MODE_ROUTE   — full screen, reached from SolarSelect after committing to a
#                  system. Ends in LAUNCH, which drops you onto the current node.
#   MODE_OVERLAY — instanced over CubeLand on the `toggle_map` action. Pauses the
#                  tree, frees the mouse, and closes back to where you were.
#
# The rail is deliberately a long horizontal strip that scrolls rather than a graph
# squeezed to fit the screen: horizontal traversal is the point, and it's what keeps
# a 20-planet Hard system readable at the same node scale as a 5-planet Easy one.
#
# SKELETON. The structural nodes all live in SolarMap.tscn; only the per-node cells
# and the links between them are built here, because their count varies with the
# system. Reserved slots (currency, XP, accessories) are real nodes in the scene
# showing placeholder values — they are labelled RESERVED and nothing feeds them yet.

const PLANET_ICON := preload("res://Sprites/planet icon.png")
const PLANET_DESTROYED_ICON := preload("res://Sprites/planet destroyed icon.png")
const SUN_ICON := preload("res://Sprites/sun icon.png")
const SHOP_ICON := preload("res://Sprites/warp shop icon.png")
# Marks where you are, hovering over the current node. Antithesis is the "you" on this map.
const HERE_ICON := preload("res://Sprites/Antithesis Icon.png")
const HERE_ICON_SIZE := 72.0
const HERE_ICON_GAP := 14.0
# How far a finished node fades back. Whole-cell alpha, so icon and labels dim together.
const CLEARED_DIM := 0.4

# Rail legs. Every leg is dashed; everything ahead of you shares one colour and alpha, so
# progress is told by colour alone (dim green behind you, grey ahead).
const LINK_WIDTH := 4.0
const FUTURE_LINK_ALPHA := 0.6
const DASH_LENGTH := 14.0
const DASH_GAP := 10.0

# Current and cleared are both green now that the accent moved off cyan, so they're
# separated by brightness instead of hue: neon = happening now, muted = already done.
# Don't flatten these back together — the map's whole job is showing which is which.
const COL_CURRENT := Color(0, 1, 0)                          # #00FF00, the main-menu green
const COL_CLEARED := Color(0.243137, 0.619608, 0.290196)     # #3E9E4A, muted
const COL_WARPSTATION := Color(1.0, 0.713726, 0.282353)
const COL_SUN := Color(1.0, 0.352941, 0.156863)
const COL_LOCKED := Color(0.607843, 0.619608, 0.658824)
const COL_HIDDEN := Color(0.298039, 0.309804, 0.345098)

# Rail geometry. NODE_PITCH is what makes the map scroll instead of compress — it is
# a fixed spacing per node, never divided by the node count.
const NODE_PITCH := 250.0
const NODE_CELL := Vector2(210.0, 300.0)
const RAIL_MARGIN := 220.0
const ZIGZAG_AMPLITUDE := 110.0

# Horizontal traversal.
const KEY_SCROLL_SPEED := 1400.0
const WHEEL_SCROLL_STEP := 160.0

signal map_closed

@export var overlay_mode := false

var _scroll: ScrollContainer
var _track: Control
var _nodes_root: Control
var _links_root: Control

var _dragging := false
var _drag_last_x := 0.0
var _node_positions: PackedVector2Array = PackedVector2Array()
var _current_index := 0
var _pulse_targets: Array[Control] = []
var _pulse_t := 0.0


func _ready() -> void:
	_scroll = $Rail/Scroll
	_track = $Rail/Scroll/Track
	_nodes_root = $Rail/Scroll/Track/Nodes
	_links_root = $Rail/Scroll/Track/Links

	# Overlay runs on top of a paused game, so it has to keep processing itself.
	if overlay_mode:
		process_mode = Node.PROCESS_MODE_ALWAYS
		get_tree().paused = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

	$Footer/LaunchButton.visible = not overlay_mode
	$Footer/CloseButton.visible = overlay_mode
	$Backdrop.visible = not overlay_mode  # over CubeLand the game itself is the backdrop
	$Scrim.visible = overlay_mode

	if not RunManager.HasActiveSystem():
		_show_no_system()
		return

	_refresh_header()

	# The rail's height and the ScrollContainer's width both come from anchors, and
	# neither is valid until the first layout pass — the track can't be sized or
	# centred before then.
	await get_tree().process_frame
	_build_map()
	_center_on_current(false)


func _show_no_system() -> void:
	$Header/SystemName.text = "NO ACTIVE SYSTEM"
	$Header/Progress.text = ""
	$Header/Clock.text = "--:--"
	$Footer/LaunchButton.disabled = true
	$Empty.visible = true


# ─────────────────────────────── map construction ───────────────────────────────

func _build_map() -> void:
	for c in _nodes_root.get_children():
		c.queue_free()
	for c in _links_root.get_children():
		c.queue_free()
	_node_positions = PackedVector2Array()
	_pulse_targets.clear()

	var nodes: Array = RunManager.GetSystemNodesForUI()
	_current_index = RunManager.GetCurrentNodeIndex()

	var track_w: float = RAIL_MARGIN * 2.0 + max(0, nodes.size() - 1) * NODE_PITCH
	var track_h: float = $Rail.size.y
	_track.custom_minimum_size = Vector2(track_w, track_h)

	var mid_y := track_h * 0.5

	for i in nodes.size():
		# Golden-angle stagger: deterministic, so the map looks identical every time
		# it's opened, but never falls into a visible repeating pattern.
		var y := mid_y + sin(i * 2.399963) * ZIGZAG_AMPLITUDE
		_node_positions.append(Vector2(RAIL_MARGIN + i * NODE_PITCH, y))

	# Links first so node cells draw over them.
	for i in range(1, nodes.size()):
		_links_root.add_child(_make_link(i, nodes))

	for i in nodes.size():
		_nodes_root.add_child(_make_node_cell(i, nodes[i]))


func _make_link(i: int, nodes: Array) -> Node2D:
	var from: Vector2 = _node_positions[i - 1]
	var to: Vector2 = _node_positions[i]

	# A leg reads as travelled once the node it arrives at has been reached. A travelled
	# leg fades to the same CLEARED_DIM the node cells use, so the whole finished stretch
	# of the rail recedes together instead of the line staying brighter than the planets
	# it connects.
	var prev_state: String = nodes[i - 1].get("state", "Locked")
	var col: Color
	if prev_state == "Cleared":
		col = Color(COL_CLEARED, CLEARED_DIM)
	elif i <= _current_index:
		col = Color(COL_CURRENT, 0.7)
	else:
		# Every leg still ahead of you looks the same — legs used to fade further the deeper
		# into fog they went, which made the far end of a 20-node system almost invisible.
		# Fog is a property of the node, not of the track between them.
		col = Color(COL_LOCKED, FUTURE_LINK_ALPHA)

	# Every leg is dashed, travelled or not — the rail reads as a plotted course throughout,
	# and state is carried by colour alone.
	return _make_dashed_link(from, to, col)


func _make_segment(from: Vector2, to: Vector2, col: Color) -> Line2D:
	var line := Line2D.new()
	line.points = PackedVector2Array([from, to])
	line.width = LINK_WIDTH
	line.default_color = col
	return line


# Line2D has no dash support — its points are always joined — so a dashed leg is built as
# a row of short segments under one holder node.
func _make_dashed_link(from: Vector2, to: Vector2, col: Color) -> Node2D:
	var root := Node2D.new()
	var span := to - from
	var length := span.length()
	if length <= 0.0:
		return root

	var dir := span / length
	var travelled := 0.0
	while travelled < length:
		var dash_end: float = min(travelled + DASH_LENGTH, length)
		root.add_child(_make_segment(from + dir * travelled, from + dir * dash_end, col))
		travelled += DASH_LENGTH + DASH_GAP
	return root


func _make_node_cell(i: int, data: Dictionary) -> Control:
	var kind: String = data.get("kind", "Planet")
	var state: String = data.get("state", "Locked")
	var fog: String = data.get("fog", "Hidden")

	var cell := Control.new()
	cell.custom_minimum_size = NODE_CELL
	cell.size = NODE_CELL
	cell.position = _node_positions[i] - NODE_CELL * 0.5
	cell.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var accent := _accent_for(kind, state, fog)
	var icon_size := 132.0 if kind == "Sun" else 96.0

	var ring_size: float = icon_size + 44.0
	var ring_pos := Vector2((NODE_CELL.x - ring_size) * 0.5, (NODE_CELL.y - ring_size) * 0.5 - 24.0)

	# Only the current node gets a ring, and it is a bare outline — no filled disc behind
	# the icon. Nodes that aren't current build no Panel at all, so there is nothing there
	# to tint the art or the background behind it.
	if state == "Current":
		var ring := Panel.new()
		ring.position = ring_pos
		ring.size = Vector2(ring_size, ring_size)
		ring.mouse_filter = Control.MOUSE_FILTER_IGNORE

		var sb := StyleBoxFlat.new()
		sb.bg_color = Color(0, 0, 0, 0)
		sb.border_color = accent
		sb.border_width_left = 4
		sb.border_width_right = 4
		sb.border_width_top = 4
		sb.border_width_bottom = 4
		var r := int(ring_size * 0.5)
		sb.corner_radius_top_left = r
		sb.corner_radius_top_right = r
		sb.corner_radius_bottom_left = r
		sb.corner_radius_bottom_right = r
		ring.add_theme_stylebox_override("panel", sb)
		cell.add_child(ring)

		_pulse_targets.append(ring)
		cell.add_child(_make_here_marker(ring_pos.y))

	var icon := TextureRect.new()
	icon.texture = _icon_for(kind, state)
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.size = Vector2(icon_size, icon_size)
	icon.position = Vector2((NODE_CELL.x - icon_size) * 0.5, (NODE_CELL.y - icon_size) * 0.5 - 24.0)
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# Every planet draws at full brightness. Fog used to be shown by greying the art down
	# (Decision 03), but a dark washed-out planet reads as a wrecked one — which is now a
	# separate icon and has to stay the only thing that means "destroyed". Fog still shows
	# in the title and subtitle ("- - -" / "UNKNOWN"), which say it outright.
	icon.modulate = Color(1, 1, 1, 1)
	cell.add_child(icon)

	cell.add_child(_make_label(_title_for(i, kind, fog), 20, accent, 0.0, NODE_CELL.y - 96.0, true))
	cell.add_child(_make_label(_subtitle_for(data, kind, fog), 15, COL_LOCKED, 0.0, NODE_CELL.y - 70.0, false))
	cell.add_child(_make_label(_signifier_for(state, kind), 16, accent, 0.0, NODE_CELL.y - 44.0, true))

	# A finished node recedes: icon and all three labels dim together. Applied to the whole
	# cell rather than per-child so nothing added later can forget to dim with it. Cleared
	# cells have no ring, so this only touches the art and the text.
	if state == "Cleared":
		cell.modulate = Color(1, 1, 1, CLEARED_DIM)
	return cell


func _make_label(txt: String, size: int, col: Color, _x: float, y: float, bold: bool) -> Label:
	var l := Label.new()
	l.text = txt
	l.position = Vector2(0.0, y)
	l.size = Vector2(NODE_CELL.x, 26.0)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", col if bold else Color(col, 0.75))
	return l


# Antithesis hovering over the node you're on. Positioned off the ring's top edge rather
# than off the cell, so it clears the sun's larger ring without a second special case.
func _make_here_marker(ring_top: float) -> TextureRect:
	var marker := TextureRect.new()
	marker.texture = HERE_ICON
	marker.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	marker.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	marker.size = Vector2(HERE_ICON_SIZE, HERE_ICON_SIZE)
	marker.position = Vector2(
		(NODE_CELL.x - HERE_ICON_SIZE) * 0.5,
		ring_top - HERE_ICON_SIZE - HERE_ICON_GAP,
	)
	marker.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return marker


func _icon_for(kind: String, state: String) -> Texture2D:
	match kind:
		"Sun": return SUN_ICON
		"Warpstation": return SHOP_ICON
		# A beaten planet is a destroyed one, so the map shows the wreck rather than the
		# planet you were offered. Planets only: clearing the sun ends the run, and a
		# warpstation is visited rather than destroyed.
		_: return PLANET_DESTROYED_ICON if state == "Cleared" else PLANET_ICON


func _accent_for(kind: String, state: String, fog: String) -> Color:
	if state == "Cleared": return COL_CLEARED
	if state == "Current": return COL_CURRENT
	if fog == "Hidden": return COL_HIDDEN
	if kind == "Sun": return COL_SUN
	if kind == "Warpstation": return COL_WARPSTATION
	return COL_LOCKED


func _title_for(i: int, kind: String, fog: String) -> String:
	if kind == "Sun": return "THE SUN"
	if kind == "Warpstation": return "WARPSTATION"
	if fog == "Hidden": return "- - -"
	return "PLANET %d" % (i + 1)


# Category, never contents — a rough node shows its terrain family and difficulty
# band but not which biome it actually is (Decision 02 / Decision 03).
func _subtitle_for(data: Dictionary, kind: String, fog: String) -> String:
	if kind == "Warpstation": return "SHOP"
	if kind == "Sun":
		return "DESTROY TO CLEAR" if fog != "Hidden" else "UNKNOWN"
	match fog:
		"Known":
			var biome: String = str(data.get("biome", ""))
			var kills: int = int(data.get("kill_target", 0))
			return "%s · %d KILLS" % [biome, kills] if biome != "" else "%d KILLS" % kills
		"Rough":
			return "%s · %s" % [str(data.get("template", "?")).to_upper(), str(data.get("difficulty", "?")).to_upper()]
		_:
			return "UNSCANNED"


# The per-node progress signifier the map exists to show.
func _signifier_for(state: String, kind: String) -> String:
	match state:
		"Cleared": return "CLEARED" if kind != "Warpstation" else "VISITED"
		"Current": return "YOU ARE HERE"
		_: return ""


func _refresh_header() -> void:
	var info: Dictionary = RunManager.GetSystemInfoForUI()
	if info.is_empty():
		return

	$Header/SystemName.text = "SYSTEM %s" % str(info.get("name", "?"))
	$Header/Tier.text = "%s · HOSTILITY x%.2f" % [str(info.get("tier", "?")).to_upper(), float(info.get("density", 1.0))]

	var cleared := int(info.get("cleared", 0))
	var total := int(info.get("total_nodes", 0))
	$Header/Progress.text = "%d / %d NODES CLEARED" % [cleared, total]

	var remaining := float(info.get("clock_remaining", 0.0))
	$Header/Clock.text = "TIME EXPIRED" if bool(info.get("clock_expired", false)) \
		else "%d:%02d" % [int(remaining) / 60, int(remaining) % 60]
	$Header/Clock.add_theme_color_override("font_color",
		COL_SUN if remaining < 60.0 else COL_CURRENT)

	var mods: Array = info.get("modifiers", [])
	$Footer/Modifiers.text = ("MODIFIERS: " + " · ".join(mods)) if not mods.is_empty() else "NO MODIFIERS"

	if bool(info.get("complete", false)):
		$Footer/LaunchButton.text = "SYSTEM CLEARED"
		$Footer/LaunchButton.disabled = true


# ─────────────────────────── horizontal traversal ───────────────────────────

func _process(delta: float) -> void:
	# Held A/D and arrows pan the rail. Read directly rather than through the input
	# map so the map doesn't depend on the gameplay bindings.
	var dir := 0.0
	if Input.is_key_pressed(KEY_D) or Input.is_key_pressed(KEY_RIGHT):
		dir += 1.0
	if Input.is_key_pressed(KEY_A) or Input.is_key_pressed(KEY_LEFT):
		dir -= 1.0
	if dir != 0.0 and _scroll != null:
		_scroll.scroll_horizontal += int(dir * KEY_SCROLL_SPEED * delta)

	if not _pulse_targets.is_empty():
		_pulse_t += delta
		var a := 0.55 + 0.45 * sin(_pulse_t * 4.0)
		for p in _pulse_targets:
			if is_instance_valid(p):
				p.modulate = Color(1, 1, 1, a)


func _on_rail_gui_input(event: InputEvent) -> void:
	# Wheel scrolls horizontally. The ScrollContainer has vertical scrolling disabled,
	# so wheel-up/down would otherwise do nothing at all here.
	if event is InputEventMouseButton and event.pressed:
		match event.button_index:
			MOUSE_BUTTON_WHEEL_UP, MOUSE_BUTTON_WHEEL_LEFT:
				_scroll.scroll_horizontal -= int(WHEEL_SCROLL_STEP)
			MOUSE_BUTTON_WHEEL_DOWN, MOUSE_BUTTON_WHEEL_RIGHT:
				_scroll.scroll_horizontal += int(WHEEL_SCROLL_STEP)

	# Click-drag panning.
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		_dragging = event.pressed
		_drag_last_x = event.position.x
	elif event is InputEventMouseMotion and _dragging:
		var motion := event as InputEventMouseMotion
		var dx: float = motion.position.x - _drag_last_x
		_drag_last_x = motion.position.x
		_scroll.scroll_horizontal -= int(dx)


func _center_on_current(animated: bool = true) -> void:
	if _node_positions.is_empty() or _scroll == null:
		return
	var idx: int = clampi(_current_index, 0, _node_positions.size() - 1)
	var target: int = int(_node_positions[idx].x - _scroll.size.x * 0.5)
	if animated:
		var tw := create_tween()
		tw.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		tw.tween_property(_scroll, "scroll_horizontal", target, 0.35)
	else:
		_scroll.scroll_horizontal = target


func _unhandled_input(event: InputEvent) -> void:
	if not overlay_mode:
		return
	if event.is_action_pressed("toggle_map") or event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		close_overlay()


# ─────────────────────────────── buttons ───────────────────────────────

func _on_recenter_button_pressed() -> void:
	_center_on_current()


func _on_launch_button_pressed() -> void:
	# LaunchCurrentNode owns the scene change into CubeLand.
	RunManager.LaunchCurrentNode()


func _on_close_button_pressed() -> void:
	close_overlay()


func _on_abandon_button_pressed() -> void:
	if overlay_mode:
		get_tree().paused = false
	# ReturnToShip owns the scene change — abandoning drops you back on the ship, not at the
	# main menu, so the hub stays the one place a run begins and ends.
	RunManager.ReturnToShip()


func close_overlay() -> void:
	get_tree().paused = false
	# CubeLand captures the mouse for camera look; handing it back is the caller's
	# job via this signal, since only it knows what the mode was before the map opened.
	map_closed.emit()
	queue_free()
