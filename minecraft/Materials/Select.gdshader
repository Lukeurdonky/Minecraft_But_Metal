shader_type spatial;

// The block-highlight box has to sit EXACTLY on the face it's outlining, so it takes the
// identical vertex displacement the chunk shaders do rather than an approximation. Without
// this it stays on the flat world and slides off the block as soon as you look at anything
// more than a few metres away.
#include "res://Materials/WorldCurve.gdshaderinc"

void vertex() {
	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y -= world_curve_drop(world_pos);
}

float gap(float s)
{
	s = log(s)/log(.5);
	return pow(2.0,s)-1.0;
}

void fragment() {
	// Called for every pixel the material is visible on.
	//ALBEDO = vec3(0,0,0);
	float size = 0.2;

	vec3 color = vec3(0,0,0);
	ALPHA = 0.0;
	//if(floor(UV.x*10.0) == 0.0)
	if(mod(floor(UV.x * 48.0 / size), 16.0 / size) == 0.0 || mod(floor((UV.x * 48.0 / size)), 16.0 / size) == 15.0/size+gap(size))
	{
		ALPHA = 1.0;
	}
	if(mod(floor(UV.y * 32.0 / size), 16.0 / size) == 0.0 || mod(floor((UV.y * 32.0 / size)), 16.0 / size) == 15.0/size+gap(size))
	{
		ALPHA = 1.0;
	}
	ALBEDO = color;
}

//void light() {
	// Called for every pixel for every light affecting the material.
	// Uncomment to replace the default light processing function with this one.
//}
