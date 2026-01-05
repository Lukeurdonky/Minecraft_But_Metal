extends Node

var Player: Node3D
@export var sensitivity_x: float = 0.3
@export var sensitivity_y: float = 0.3
@export var max_pitch: float = 90.0  # Limit the camera's up/down rotation
@export var min_pitch: float = -90.0 # Limit the camera's up/down rotation

var WORLD_SPAWN = Vector3i(10000,10005,10000)
const SURFACE_LEVEL = 10000
const ABYSS_CENTER = Vector2(25000, 25000) # x,z center
const ABYSS_RADIUS = 120
const layer_noise_scale = {
	0: 0.02,
	1: 0.04,
	2: 0.07,
	3: 0.1,
	4: 0.16
}

var air_friction = .91
var ground_friction = .5
var CubeManager: Node3D
var atlas_width = 12
var atlas_height = 8
var prevPos = Vector3(0,0,0)

var Block_Data: Dictionary = {
	"grass": {"index": 0, "hardness": 1, "drops": "grass", "drop_count": 2},
	"dirt": {"index": 1, "hardness": 2, "drops": "dirt", "drop_count": 1},
	"stone": {"index": 2, "hardness": 5, "drops": "stone", "drop_count": 5},
	"silly": {"index": 3, "hardness": 5, "drops": "stone", "drop_count": 5},
}

var Item_Data: Dictionary = {
	"grass": {"block": 1, "max_stack": 64, "texture": "res://sprites/textures/grass.png"},
	"dirt": {"block": 2, "max_stack": 64, "texture": "res://sprites/textures/dirt.png"},
	"stone": {"block": 3, "max_stack": 64, "texture": "res://sprites/textures/stone.png"},
}


func get_player_pos():
	if(Player == null): 
		print("NO PLAYER")
		return prevPos
	prevPos = Player.global_transform.origin
	return prevPos
	
func get_player_camera():
	return Player.camera
	
func get_block_stat(block_type: String, stat: String) -> Variant:
	if block_type in Block_Data and stat in Block_Data[block_type]:
		return Block_Data[block_type][stat]
	return null  # Return null or a default value
	
func get_item_stat(item_type: String, stat: String) -> Variant:
	if item_type in Item_Data and stat in Item_Data[item_type]:
		return Item_Data[item_type][stat]
	return null  # Return null or a default value
	

# --------------------- the abyss ---------------------------

func abyss_layer(y): # this should hopefully be helpful
	if y > SURFACE_LEVEL:
		return 0 # surface rim
	elif y > 9800:
		return 1 # upper abyss
	elif y > 9600:
		return 2 # middle abyss
	elif y > 9400:
		return 3 # lower abyss
	else:
		return 4 # deep hell
		
func abyss_strength(x, z, y): #not sure if this should be here but i figured broad abyss functions could be here
	var d = Vector2(x, z).distance_to(abyss_center_at_y(y))
	return clamp(1.0 - d / ABYSS_RADIUS, 0.0, 1.0)
	
func abyss_center_at_y(y: float) -> Vector2:
	var t = (Global.SURFACE_LEVEL - y) * 0.02

	return Vector2(
		ABYSS_CENTER.x + sin(t) * 40.0,
		ABYSS_CENTER.y + cos(t * 0.7) * 25.0
	)
