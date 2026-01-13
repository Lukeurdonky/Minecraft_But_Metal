shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, unshaded;

uniform sampler2D damage_texture : source_color, filter_nearest, repeat_enable;

varying float damage_amount;

void vertex() {
	// Pass custom data to fragment shader
	damage_amount = INSTANCE_CUSTOM.r;
}

void fragment() {
	vec4 tex = texture(damage_texture, UV);
	ALBEDO = tex.rgb;
	
	// Use damage amount to control visibility
	ALPHA = tex.a * damage_amount;
	
	// Add brightness to make damage more visible
	ALBEDO *= 1.3;
}
