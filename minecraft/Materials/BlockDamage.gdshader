shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back, unshaded;

#include "res://Materials/WorldCurve.gdshaderinc"

uniform sampler2D damage_texture : source_color, filter_nearest, repeat_enable;

varying float damage_amount;

void vertex() {
	// Pass custom data to fragment shader
	damage_amount = INSTANCE_CUSTOM.r;

	// Must bend with the terrain or every crack floats off the block it belongs
	// to as soon as that block is more than a few metres away. MODEL_MATRIX on a
	// MultiMesh already carries the per-instance transform, so this is the
	// instance's real world position, and the overlay boxes are axis-aligned like
	// the chunks — same translation-only assumption as ChunkCurved.gdshader.
	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y -= world_curve_drop(world_pos);
}

void fragment() {
	vec4 tex = texture(damage_texture, UV);
	ALBEDO = tex.rgb;
	
	// Use damage amount to control visibility
	ALPHA = tex.a * damage_amount;
	
	// Add brightness to make damage more visible
	ALBEDO *= 1.3;
}
