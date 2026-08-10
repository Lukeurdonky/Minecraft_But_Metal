shader_type spatial;
// depth_prepass_alpha is the shader equivalent of StandardMaterial3D's
// transparency = 4 (ALPHA_DEPTH_PRE_PASS): near-opaque pixels still write depth
// (Frame's struts, Glass's pane edges) while the translucent interior blends.
render_mode blend_mix, depth_prepass_alpha;

// Curved-world replacement for block_texture_atlas_transparent.tres. Must stay
// in lockstep with ChunkCurved.gdshader's vertex() — the two surfaces of one
// chunk mesh have to bend identically or glass shears away from the stone
// around it.

#include "res://Materials/WorldCurve.gdshaderinc"

uniform sampler2D albedo_texture : source_color, filter_nearest;

void vertex() {
	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y -= world_curve_drop(world_pos);
}

void fragment() {
	vec4 tex = texture(albedo_texture, UV);
	// vertex_color_use_as_albedo — this is the Block_Definition.Alpha knob,
	// delivered as vertex colour and multiplied onto the atlas's own per-pixel alpha.
	ALBEDO = tex.rgb * COLOR.rgb;
	ALPHA  = tex.a * COLOR.a;
}
