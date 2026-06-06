# ARCHIVED — Minecraft 36-slot inventory system. Superseded by weapon/ability loadout.

extends Node3D  # kept so the scene attachment stays valid

# extends Node3D
#
# @export var max_slots: int = 36
# @export var hotbar_size: int = 9
# var selected_slot = 0
# var prev_selected_item
# var items: Array = []
#
# func _ready() -> void:
# 	for i in range(max_slots):
# 		items.append(Slot.new())
#
#
# class Slot:
# 	var amount
# 	var item
# 	func _init():
# 		self.amount = 0
# 		self.item = null
#
# func add_item(item: String) -> bool:
# 	var s = get_available_slot(item)
# 	if(s != null):
# 		s.item = item
# 		s.amount += 1
# 		return true
# 	return false
#
# func get_available_slot(item: String):
# 	var the_slot = null
# 	if has_item(item): the_slot = get_stack_slot(item)
# 	if the_slot == null:
# 		the_slot = get_empty_slot(item)
# 	return the_slot
#
# func get_empty_slot(item: String):
# 	var flag = null
# 	for i in range(max_slots):
# 		if(items[i].item == null):
# 			flag = items[i]
# 			break
# 	return flag
#
# func get_stack_slot(item: String):
# 	var maxStack = Item_Registry.GetItemStat(item, "MaxStack")
# 	var flag = null
# 	for i in range(max_slots):
# 		if(items[i].item == item && items[i].amount < maxStack):
# 			flag = items[i]
# 			break
# 	return flag
#
# func has_item(item: String) -> bool:
# 	var flag = false
# 	for i in range(max_slots):
# 		if(item == items[i].item):
# 			flag = true
# 			break
# 	return flag
#
# func remove_item(ndx: int):
# 	items[ndx].amount -= 1
# 	if(items[ndx].amount <= 0): items[ndx].item = null
#
# func get_item(ndx: int):
# 	return items[ndx].item
#
# func get_items():
# 	var arr = "\n"
# 	for i in range(max_slots):
# 		if(i == selected_slot): arr += "|"
# 		arr += (str(items[i].item) + ": " + str(items[i].amount) + " ")
# 		if(i == selected_slot): arr += "|"
# 		if i % hotbar_size == 8: arr += ("\n")
# 	return arr
#
# func _unhandled_input(event: InputEvent) -> void:
# 	if event is InputEventMouseButton:
# 		if Input.is_action_just_released("MWU"):
# 			cycle_hotbar(1)
# 		elif Input.is_action_just_released("MWD"):
# 			cycle_hotbar(-1)
#
# func cycle_hotbar(direction: int) -> void:
# 	selected_slot = (selected_slot + direction) % hotbar_size
# 	if selected_slot < 0:
# 		selected_slot += hotbar_size
