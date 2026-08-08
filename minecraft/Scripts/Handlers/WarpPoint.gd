extends Node3D

## The way off a cleared planet — a structure that falls out of the sky and lands near you.
##
## Clearing a node's kill target used to just print "PRESS J". The exit is now a PLACE:
## RunManager latches WarpReady, this node calls a warp point down, it craters into the
## terrain, and looking at it and pressing E is what starts the 10s charge.
## RunManager still owns the countdown and the stage transition — this owns the object.
##
## The warp point is the "Warp Point" structure authored in Scenes/StructureBuilder.tscn and
## stamped like any other structure. Nothing here knows its shape: change the pillar in the
## builder and press Save.
##
## You interact with ANY block of it — look at the structure and the prompt appears. It is
## deliberately not a Marker1 console like the ship's: a marker is skipped at stamp time, so
## on a solid pillar it would leave a one-block hole in the middle of the thing. The stamped
## bounding box comes from the structure itself (Structure_Registry.GetStampBounds), so a
## taller or wider pillar needs no change here.
##
## Otherwise reuses Ship.gd's patterns — chunk-readiness polling instead of a frame count,
## no Godot physics for the interaction test. See the "Ship hub" section of CLAUDE.md.

# Typed in the builder as "Warp Point"; Structure_Registry.CaptureAndSave sanitizes every
# non-alphanumeric character to '_', so the saved resource is Warp_Point.tres and the
# registry key is "Warp_Point". Both spellings are tried so neither is a silent miss.
const STRUCTURE_NAME := "Warp_point"

## Ground distance from the player the warp point lands at. Far enough not to drop on your
## head, close enough to still be on screen when it hits.
@export var land_distance := 20.0

## How far above its landing spot the warp point starts falling from, and how fast it falls.
## The fall exists to be seen — it is the signal that the planet is finished.
@export var fall_height := 70.0
@export var fall_speed := 55.0

## Blocks of clearance carved around the structure on impact, past its own footprint. The
## explode radius is derived from the structure's size plus this, so a wider pillar clears
## a wider crater without retuning anything here.
@export var clear_margin := 3.0

## Hard ceiling on that radius. explode() is O(r^3) and this fires mid-combat.
@export var max_clear_radius := 8.0

## How long "WARP POINT LANDED" stays up.
@export var banner_seconds := 4.0

# Mirrors Global.CHUNK_SIZE (a C# const — statics don't cross to GDScript).
const CHUNK_SIZE := 48

# How far down from the player's own altitude to look for ground to land on. Deliberately
# relative to the player rather than a surface scan from the sky: on a Cave-template planet
# the real surface is far overhead and a warp point up there is unreachable. Falling from
# above the player's own depth means it punches down through the cave roof instead.
const SCAN_UP := 6
const SCAN_DOWN := 48

# Give up rather than hang forever if the landing column never streams in.
const SITE_TIMEOUT := 15.0

const ACCENT := Color(0.231, 0.863, 0.902)  # #3bdce6, the project's UI cyan

enum { PHASE_IDLE, PHASE_SEEKING, PHASE_FALLING, PHASE_LANDED, PHASE_FAILED }

@onready var _prompt: Label = $UI/Prompt
@onready var _banner: Label = $UI/Banner
@onready var _status: Label = $UI/Status

var _phase := PHASE_IDLE
var _structure_key := ""          # resolved once, the spelling the registry actually knows
var _site := Vector3i.ZERO        # where Anchor (bottom-centre) lands
var _size := Vector3i.ONE
var _seek_elapsed := 0.0
var _banner_left := 0.0

var _proxy: MeshInstance3D = null      # the falling stand-in, freed on impact
var _fall_y := 0.0

# The stamped structure's world-space block box, inclusive at both ends. Looking at any
# block inside it is what offers the warp.
var _box_min := Vector3i.ZERO
var _box_max := Vector3i.ZERO
var _targeting := false


func _ready() -> void:
	_prompt.visible = false
	_banner.visible = false
	_status.visible = false


func _process(delta: float) -> void:
	if _banner_left > 0.0:
		_banner_left -= delta
		if _banner_left <= 0.0:
			_banner.visible = false

	match _phase:
		PHASE_IDLE:    _tick_idle()
		PHASE_SEEKING: _tick_seeking(delta)
		PHASE_FALLING: _tick_falling(delta)
		PHASE_LANDED:  _tick_landed()


func _unhandled_input(event: InputEvent) -> void:
	if _phase != PHASE_LANDED or not _targeting:
		return
	if not event.is_action_pressed("interact"):
		return
	# Swallowed so interactions.gd can't also read this press. E used to be a second
	# explode trigger there; that branch is gone, but the guard costs nothing.
	get_viewport().set_input_as_handled()
	_begin_warp()


## ------------------------------------------------------------------ phases

func _tick_idle() -> void:
	# RunManager is the authority on whether the planet is finished. WarpReady latches and
	# never un-latches, so this fires exactly once per node.
	if RunManager == null or not RunManager.IsStageActive() or not RunManager.IsWarpReady():
		return

	# Resolve the structure before anything visible happens, so a missing one fails
	# immediately and loudly instead of after a seven-second fall to nowhere.
	_structure_key = _resolve_structure()
	if _structure_key == "":
		_fail("No '%s' structure. Build one in the Builder and save it under that name."
			% STRUCTURE_NAME)
		return

	var bounds: Dictionary = Structure_Registry.GetStampBounds(_structure_key, Vector3i.ZERO)
	_size = bounds["size"]
	_phase = PHASE_SEEKING
	_seek_elapsed = 0.0
	RunManager.ReportWarpPointInbound()


func _tick_seeking(delta: float) -> void:
	_seek_elapsed += delta
	if _seek_elapsed > SITE_TIMEOUT:
		_fail("Timed out finding ground for the warp point.")
		return

	if not _pick_site():
		return  # column not streamed in yet, or no player — try again next frame

	_build_visuals()
	_fall_y = float(_site.y) + fall_height
	_phase = PHASE_FALLING


func _tick_falling(delta: float) -> void:
	_fall_y -= fall_speed * delta
	if _fall_y <= float(_site.y):
		_impact()
		return
	_place_visuals(_fall_y)


## Offer the warp whenever the crosshair is on any block of the structure. Reuses the block
## targeting interactions.gd already does every frame rather than adding a second raycast —
## and it comes with a reach limit for free (that targeting maxes out at 5 blocks), so there
## is no separate radius to keep in step.
func _tick_landed() -> void:
	# Explicitly typed, not inferred: GDScript's analyser can't see the return type of a C#
	# method, so `:=` here fails to parse with "cannot infer the type".
	var on_it: bool = Global.HasSelectedBlock() and _contains(Global.GetSelectedBlock())
	if on_it == _targeting:
		return
	_targeting = on_it
	# Once the charge is running the prompt is spent — the HUD owns the countdown from there.
	_prompt.visible = on_it and not RunManager.IsWarpCharging()


func _contains(b: Vector3i) -> bool:
	return b.x >= _box_min.x and b.x <= _box_max.x \
		and b.y >= _box_min.y and b.y <= _box_max.y \
		and b.z >= _box_min.z and b.z <= _box_max.z


## ------------------------------------------------------------------ landing site

## Resolve the name the registry actually filed the structure under. CaptureAndSave maps
## every non-alphanumeric character to '_', so "Warp Point" is stored as "Warp_Point" —
## looking up the pretty spelling alone would miss it for a reason nothing would explain.
func _resolve_structure() -> String:
	var known: Array = Structure_Registry.GetStructureNames()
	if STRUCTURE_NAME in known:
		return STRUCTURE_NAME
	var underscored := STRUCTURE_NAME.replace(" ", "_")
	return underscored if underscored in known else ""


## Ground under a point land_distance ahead of where the player is looking, so the fall
## happens on screen. Returns false while the column hasn't streamed in — is_chunk_ready is
## the only honest test, since get_block returns 0 for "air" and "no chunk" alike.
func _pick_site() -> bool:
	var cm = Global.CubeManager
	var cam := get_viewport().get_camera_3d()
	if cm == null or cam == null:
		return false

	var player_pos: Vector3 = Global.GetPlayerPos()

	var forward := -cam.global_transform.basis.z
	forward.y = 0.0
	# Straight up or straight down degenerates the horizontal projection; any direction will
	# do in that case, since the player isn't looking at the horizon anyway.
	forward = Vector3.FORWARD if forward.length_squared() < 0.001 else forward.normalized()

	var target := player_pos + forward * land_distance
	var bx := int(floor(target.x))
	var bz := int(floor(target.z))
	var top := int(floor(player_pos.y)) + SCAN_UP

	if not _column_ready(bx, bz, top, top - SCAN_DOWN):
		return false

	for y in range(top, top - SCAN_DOWN, -1):
		if cm.get_block(Vector3i(bx, y, bz)) != 0:
			# Anchor is bottom-centre, so the structure's base sits in the first air cell.
			_site = Vector3i(bx, y + 1, bz)
			return true

	# Nothing solid within scan range — open sky or a deep chasm. Land it at the player's
	# own altitude rather than dropping it into the void; the crater carves its own footing.
	_site = Vector3i(bx, int(floor(player_pos.y)), bz)
	return true


func _column_ready(bx: int, bz: int, top: int, bottom: int) -> bool:
	var cm = Global.CubeManager
	if cm == null:
		return false
	# One sample per chunk the column crosses — is_chunk_ready answers a per-chunk question,
	# so sampling finer than CHUNK_SIZE just repeats work.
	var y := bottom
	while y < top:
		if not cm.is_chunk_ready(Vector3i(bx, y, bz)):
			return false
		y += CHUNK_SIZE
	return cm.is_chunk_ready(Vector3i(bx, top, bz))


## ------------------------------------------------------------------ impact

func _impact() -> void:
	var cm = Global.CubeManager

	# Clear first, stamp second. The crater is what makes room for the pillar; doing it the
	# other way round would blow up the structure that was just placed.
	var radius := _clear_radius()
	if cm != null:
		# Centred on the structure's middle, not its base, so the clearance is around the
		# whole pillar rather than a bowl under it. damage 1.0 = instant kill on the centre
		# block (Chunk_Manager.explode's documented threshold).
		var centre := Vector3i(_site.x, _site.y + int(_size.y / 2.0), _site.z)
		cm.explode(centre, radius, 1.0)

	# clearAir so the pillar's own box is emptied too — anything with an interior fills with
	# whatever terrain it landed in otherwise.
	if not Structure_Registry.StampByName(_structure_key, _site, true):
		_fail("Failed to stamp '%s'." % _structure_key)
		return

	if _proxy != null:
		_proxy.queue_free()
		_proxy = null

	Global.ShakeCamera(1.2, 0.5)

	# Ask the structure where it ended up rather than recomputing the box from _site and an
	# assumed anchor — GetStampBounds already owns the `worldPos - Anchor` math, and this is
	# the same lookup the pre-stamp readiness check used.
	var bounds: Dictionary = Structure_Registry.GetStampBounds(_structure_key, _site)
	_box_min = bounds["min"]
	_box_max = _box_min + Vector3i(bounds["size"]) - Vector3i.ONE

	_phase = PHASE_LANDED
	RunManager.ReportWarpPointLanded()

	_banner.text = "WARP POINT LANDED"
	_banner.visible = true
	_banner_left = banner_seconds


func _begin_warp() -> void:
	_prompt.visible = false
	_targeting = false
	# RunManager owns the countdown and the stage transition; this only opens the door.
	RunManager.StartWarp()


## ------------------------------------------------------------------ visuals

func _clear_radius() -> float:
	var footprint: float = max(_size.x, _size.z) * 0.5
	return min(footprint + clear_margin, max_clear_radius)


## A solid stand-in for the pillar. Voxels cannot move, so the thing that falls is necessarily
## a proxy: it is freed on impact and the real structure is stamped in its place on the same
## frame.
##
## There used to be a translucent cyan cage around it — a fill box plus depth-ignoring edge
## lines, meant to advertise the cleared volume and stay visible from behind terrain. It read
## as a debug gizmo bolted onto the fall rather than as part of it, so it is gone: the dark
## chassis with the cyan emission is the whole visual now, and the landed pillar is marked by
## the "WARP POINT LANDED" banner and the crater instead of by an outline.
func _build_visuals() -> void:
	_proxy = MeshInstance3D.new()
	var box := BoxMesh.new()
	box.size = Vector3(_size)
	_proxy.mesh = box
	_proxy.material_override = _solid_material()
	add_child(_proxy)

	_place_visuals(float(_site.y) + fall_height)


## The proxy is positioned from the structure's BASE y, since that is what _site means. The
## mesh is centred on its origin, hence the half-height offset.
func _place_visuals(base_y: float) -> void:
	if _proxy == null:
		return
	_proxy.global_position = Vector3(_site.x + 0.5, base_y + _size.y * 0.5, _site.z + 0.5)


func _solid_material() -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	# Dark chassis, bright edge — the same logic the enemies and Antithesis itself follow.
	m.albedo_color = Color(0.05, 0.06, 0.07)
	m.emission_enabled = true
	m.emission = ACCENT
	m.emission_energy_multiplier = 0.35
	return m


## ------------------------------------------------------------------ failure

## A missing or unusable structure is an authoring mistake, and the only one in this system
## that can strand a run: with no warp point there is no way off the planet. So it is loud
## on screen AND it hands the exit back to the old key, rather than being merely correct.
func _fail(msg: String) -> void:
	push_error("WarpPoint: " + msg)
	_status.text = "%s\n\nWarping is on the %s key until then." % [msg, RunManager.GetWarpKeyName()]
	_status.visible = true
	_phase = PHASE_FAILED

	if _proxy != null:
		_proxy.queue_free()
		_proxy = null

	RunManager.ReportWarpPointUnavailable()
