shader_type spatial;

// Curved-world replacement for the StandardMaterial3D in block_texture_atlas.tres.
// That material was only ever albedo texture + nearest filtering — its
// proximity_fade_distance / distance_fade_min_distance values were inert
// leftovers (neither proximity_fade_enabled nor distance_fade_mode was set), so
// nothing is lost by dropping them here.

#include "res://Materials/WorldCurve.gdshaderinc"

uniform sampler2D albedo_texture : source_color, filter_nearest;

void vertex() {
	// Chunk MeshInstance3Ds are translation-only (no rotation, no scale), so a
	// world-space Y offset is also a model-space Y offset and needs no inverse
	// basis transform.
	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y -= world_curve_drop(world_pos);
}

void fragment() {
	ALBEDO = texture(albedo_texture, UV).rgb;
}
