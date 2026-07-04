shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never;

uniform vec4 fire_color_hot : source_color = vec4(1.0, 0.9, 0.35, 1.0);
uniform vec4 fire_color_cool : source_color = vec4(0.85, 0.15, 0.03, 1.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

void fragment() {
	vec2 uv = UV;

	// Flame tapers upward: bright/opaque at the base (uv.y = 1), fading out toward the tip.
	float vertical = clamp(1.0 - uv.y, 0.0, 1.0);

	// Soft blob silhouette so it doesn't read as a hard-edged quad.
	float radial = 1.0 - smoothstep(0.0, 0.5, distance(uv, vec2(0.5, 0.7)));

	// Cheap flicker/turbulence — coarse noise cells resampled every ~1/12s.
	float flicker = hash(floor(uv * 6.0) + floor(TIME * 12.0));

	float shape = vertical * radial * (0.6 + 0.4 * flicker);

	vec3 col = mix(fire_color_cool.rgb, fire_color_hot.rgb, vertical);
	ALBEDO = col;
	EMISSION = col * 2.0;
	ALPHA = clamp(shape, 0.0, 1.0);
}
