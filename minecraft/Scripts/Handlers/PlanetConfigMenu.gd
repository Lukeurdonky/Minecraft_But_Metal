extends CanvasLayer

# Toggle with F3. Calls Global.SetPlanetConfig(dict) then reloads the scene.

var _panel: PanelContainer
var _fields := {}                          # key -> Control (SpinBox or CheckButton)
var _selected_biome_name: String = ""      # tracks the OptionButton selection

# Per-biome presets. Selecting a biome pre-fills all param spinboxes.
# Values are midpoints of each biome's valid range.
const BIOMES := {
	"Bouncy Cloud Plains": {
		"biome": "Bouncy Cloud Plains", "template": "Field",
		"fill_solid": false, "surface_block": 8,
		"noise_scale": 1.0,  "height_amp": 9.0,   "spawn_y": 20,
		"caves_enabled": false, "cave_full_range": false,
		"cave_scale": 3.0, "cave_y_freq": 0.05, "cave_threshold": 0.25,
		"chasm_enabled": false, "chasm_radius": 18.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": false, "planet_chunks": 32,
	},
	"Grassy Plains": {
		"biome": "Grassy Plains", "template": "Field",
		"fill_solid": false, "surface_block": 1,
		"noise_scale": 1.25, "height_amp": 10.0,  "spawn_y": 20,
		"caves_enabled": false, "cave_full_range": false,
		"cave_scale": 3.0, "cave_y_freq": 0.05, "cave_threshold": 0.25,
		"chasm_enabled": false, "chasm_radius": 18.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": false, "planet_chunks": 32,
	},
	"Metallic Mountains": {
		"biome": "Metallic Mountains", "template": "Field",
		"fill_solid": false, "surface_block": 6,
		"noise_scale": 2.75, "height_amp": 30.0,  "spawn_y": 20,
		"caves_enabled": false, "cave_full_range": false,
		"cave_scale": 3.0, "cave_y_freq": 0.05, "cave_threshold": 0.25,
		"chasm_enabled": false, "chasm_radius": 18.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": false, "planet_chunks": 32,
	},
	"Tight Stone Tunnels": {
		"biome": "Tight Stone Tunnels", "template": "Cave",
		"fill_solid": true, "surface_block": 3,
		"noise_scale": 0.0,  "height_amp": 0.0,   "spawn_y": 0,
		"caves_enabled": true, "cave_full_range": true,
		"cave_scale": 3.75, "cave_y_freq": 1.0, "cave_threshold": 0.40,
		"chasm_enabled": false, "chasm_radius": 18.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": true, "planet_chunks": 32,
	},
	"Crystal Caverns": {
		"biome": "Crystal Caverns", "template": "Cave",
		"fill_solid": true, "surface_block": 11,
		"noise_scale": 0.0,  "height_amp": 0.0,   "spawn_y": 0,
		"caves_enabled": true, "cave_full_range": true,
		"cave_scale": 2.0, "cave_y_freq": 1.0, "cave_threshold": 0.285,
		"chasm_enabled": false, "chasm_radius": 18.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": true, "planet_chunks": 32,
	},
	"The Moss Grotto": {
		"biome": "The Moss Grotto", "template": "Cave",
		"fill_solid": true, "surface_block": 14,
		"noise_scale": 0.0,  "height_amp": 0.0,   "spawn_y": 0,
		"caves_enabled": true, "cave_full_range": true,
		"cave_scale": 2.5, "cave_y_freq": 1.0, "cave_threshold": 0.33,
		"chasm_enabled": false, "chasm_radius": 18.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": true, "planet_chunks": 32,
	},
	"Dark Descent": {
		"biome": "Dark Descent", "template": "Abyss",
		"fill_solid": false, "surface_block": 3,
		"noise_scale": 1.5,  "height_amp": 9.0,   "spawn_y": 20,
		"caves_enabled": false, "cave_full_range": false,
		"cave_scale": 3.0, "cave_y_freq": 0.05, "cave_threshold": 0.25,
		"chasm_enabled": true, "chasm_radius": 16.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": false, "planet_chunks": 32,
	},
	"The Virus": {
		"biome": "The Virus", "template": "Abyss",
		"fill_solid": false, "surface_block": 16,
		"noise_scale": 1.5,  "height_amp": 7.0,   "spawn_y": 20,
		"caves_enabled": false, "cave_full_range": false,
		"cave_scale": 3.0, "cave_y_freq": 0.05, "cave_threshold": 0.25,
		"chasm_enabled": true, "chasm_radius": 12.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": false, "planet_chunks": 32,
	},
	"Lava Walls": {
		"biome": "Lava Walls", "template": "Abyss",
		"fill_solid": false, "surface_block": 15,
		"noise_scale": 1.5,  "height_amp": 13.0,  "spawn_y": 20,
		"caves_enabled": false, "cave_full_range": false,
		"cave_scale": 3.0, "cave_y_freq": 0.05, "cave_threshold": 0.25,
		"chasm_enabled": true, "chasm_radius": 22.0, "chasm_drift": 0.006,
		"spawn_clear_enabled": false, "planet_chunks": 32,
	},
}

func _ready() -> void:
	layer = 10
	_build_ui()
	_panel.visible = false

func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_F3:
			_panel.visible = not _panel.visible
			if _panel.visible:
				Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
			else:
				Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
			get_viewport().set_input_as_handled()

func _build_ui() -> void:
	_panel = PanelContainer.new()
	_panel.custom_minimum_size = Vector2(340, 0)
	_panel.position = Vector2(20, 20)
	add_child(_panel)

	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size = Vector2(340, 600)
	_panel.add_child(scroll)

	var vbox := VBoxContainer.new()
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(vbox)

	var title := Label.new()
	title.text = "Planet Config  [F3]"
	title.add_theme_font_size_override("font_size", 16)
	vbox.add_child(title)

	vbox.add_child(HSeparator.new())

	# Biome selector
	var biome_label := Label.new()
	biome_label.text = "Biome"
	vbox.add_child(biome_label)
	var biome_btn := OptionButton.new()
	for b in BIOMES.keys():
		biome_btn.add_item(b)
	biome_btn.selected = 0
	biome_btn.item_selected.connect(_on_biome_selected)
	vbox.add_child(biome_btn)

	vbox.add_child(HSeparator.new())

	# Param rows
	_add_int_row(vbox,   "planet_chunks",  "World Size (chunks)", 12, 256, 1, 32)
	_add_int_row(vbox,   "surface_block",  "Surface Block",   1,    255,   1,    8)
	_add_float_row(vbox, "noise_scale",    "Noise Scale",     0.0,  20.0,  0.001, 1.5)
	_add_float_row(vbox, "height_amp",     "Height Amplitude",0.0,  300.0, 1.0,  10.0)
	_add_int_row(vbox,   "spawn_y",        "Spawn Y",        -500,  500,   1,    20)
	_add_bool_row(vbox,  "fill_solid",     "Fill Solid",      false)

	vbox.add_child(HSeparator.new())
	var cave_lbl := Label.new(); cave_lbl.text = "— Caves —"; vbox.add_child(cave_lbl)
	_add_bool_row(vbox,  "caves_enabled",      "Caves Enabled",      false)
	_add_bool_row(vbox,  "cave_full_range",    "Cave Full Range",     false)
	_add_float_row(vbox, "cave_threshold",     "Cave Threshold",     -1.5,  1.5,   0.05,  0.3)
	_add_float_row(vbox, "cave_scale",         "Cave Scale",          0.1,  20.0,  0.1,   2.0)
	_add_float_row(vbox, "cave_y_freq",        "Cave Depth Shift",    0.1,   5.0,  0.1,   1.0)
	_add_bool_row(vbox,  "spawn_clear_enabled","Spawn Clear",         false)

	vbox.add_child(HSeparator.new())
	var abyss_lbl := Label.new(); abyss_lbl.text = "— Abyss —"; vbox.add_child(abyss_lbl)
	_add_bool_row(vbox,  "chasm_enabled",  "Shaft Enabled",   false)
	_add_float_row(vbox, "chasm_radius",   "Shaft Radius",    1.0,   200.0, 1.0,   18.0)
	_add_float_row(vbox, "chasm_drift",    "Shaft Drift",     0.0001,0.1,   0.0001,0.006)

	vbox.add_child(HSeparator.new())

	var gen_btn := Button.new()
	gen_btn.text = "Generate"
	gen_btn.pressed.connect(_on_generate)
	vbox.add_child(gen_btn)

	# Pre-fill with first biome
	_selected_biome_name = BIOMES.keys()[0]
	_apply_preset(BIOMES.values()[0])

func _add_float_row(parent: Control, key: String, label: String,
		mn: float, mx: float, step: float, default_val: float) -> void:
	var row := HBoxContainer.new()
	var lbl := Label.new()
	lbl.text = label
	lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(lbl)
	var spin := SpinBox.new()
	spin.min_value = mn
	spin.max_value = mx
	spin.step = step
	spin.value = default_val
	spin.custom_minimum_size = Vector2(110, 0)
	row.add_child(spin)
	parent.add_child(row)
	_fields[key] = spin

func _add_int_row(parent: Control, key: String, label: String,
		mn: int, mx: int, step: int, default_val: int) -> void:
	_add_float_row(parent, key, label, mn, mx, step, default_val)

func _add_bool_row(parent: Control, key: String, label: String, default_val: bool) -> void:
	var row := HBoxContainer.new()
	var lbl := Label.new()
	lbl.text = label
	lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(lbl)
	var check := CheckButton.new()
	check.button_pressed = default_val
	row.add_child(check)
	parent.add_child(row)
	_fields[key] = check

func _on_biome_selected(index: int) -> void:
	var names := BIOMES.keys()
	if index >= names.size():
		return
	_selected_biome_name = names[index]
	_apply_preset(BIOMES[_selected_biome_name])

func _apply_preset(preset: Dictionary) -> void:
	for key in preset:
		if not _fields.has(key):
			continue
		var ctrl = _fields[key]
		if ctrl is SpinBox:
			ctrl.value = float(preset[key])
		elif ctrl is CheckButton:
			ctrl.button_pressed = bool(preset[key])

func _on_generate() -> void:
	var config := {}
	for key in _fields:
		var ctrl = _fields[key]
		if ctrl is SpinBox:
			config[key] = ctrl.value
		elif ctrl is CheckButton:
			config[key] = ctrl.button_pressed
	# Pass biome identity so AtmosphereSystem and future systems can look it up
	var preset: Dictionary = BIOMES.get(_selected_biome_name, {})
	if preset.has("biome"):    config["biome"]    = preset["biome"]
	if preset.has("template"): config["template"] = preset["template"]
	Global.SetPlanetConfig(config)
	get_tree().reload_current_scene()
