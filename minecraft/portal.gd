extends Area3D

#@export var teleportPosition: Vector3;
@export var PortalLink: Node3D;

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass

func _placePortal() -> void:
	Global.portals.add();

func _on_area_entered(area: Area3D) -> void:
	area.global_position = PortalLink.global_position;
	pass # Replace with function body.

func _setLink() -> void:
	
	var index = 0;
	PortalLink = Global.portals[index];
