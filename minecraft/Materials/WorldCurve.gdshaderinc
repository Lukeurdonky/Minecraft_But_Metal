// Planet curvature — shared by every world-space material.
//
// The world bends DOWN away from the viewer so the far edge falls below the
// horizon instead of ending at a visible chunk boundary. This is a RENDER-ONLY
// displacement: collision, the DDA block targeting in interactions.gd, entity
// AABBs and every get_block consumer still run on the flat world. Nothing
// outside a vertex shader knows this happened.
//
// THE FLAT ZONE. Displacement is held at exactly zero out to world_curve_flat_radius
// and only then begins:
//
//     drop = strength * max(0, distance - flat_radius)^2
//
// Without it the lie has a real cost. Everything at the same XZ gets the same drop,
// so local relationships (bullet against block, enemy against ground) stay exact —
// but anything comparing ACROSS distances breaks, and aiming is exactly that: it
// spans from the camera at 0 to a target far away. With LaserRange 300 and
// GrappleRange 220, a target was being drawn up to 28 blocks below its own hitbox,
// so you aimed at what you saw and the ray passed under it.
//
// Inside the flat radius there is no displacement at all, so there is nothing to
// disagree about: aiming, grappling, the laser, bullet trails and the beam mesh are
// all provably exact there. The bend still happens, just past the range where you
// can act on it.
//
// Both the strength and the radius are derived from WORLD SIZE, never from render
// distance — render distance is a viewer preference and must not reshape the world.
//
// All three uniforms are GLOBAL and registered at runtime by Global.cs — a material
// opts in just by including this file. Do not redeclare them locally.
//
// world_curve_origin is the CAMERA'S WORLD POSITION, passed explicitly rather than
// derived from view space. The shadow pass runs this same vertex code with
// MODELVIEW/CAMERA_POSITION_WORLD belonging to the LIGHT, so a view-space
// formulation would bend shadows around the sun and slide them off the geometry
// casting them. An explicit world-space origin gives both passes the same bend.

global uniform float world_curve_strength;
global uniform float world_curve_flat_radius;
global uniform vec3 world_curve_origin;

// How far down a point at this world position is pushed. Callers subtract it from Y.
// Voxel terrain is uniformly tessellated at 1 vertex per block, so this is evaluated
// densely enough to read as a smooth curve with no extra geometry.
//
// Kept in exact lockstep with Global.CurveDropAt() on the C# side, which is what
// entities use — if this formula changes, change that one with it.
float world_curve_drop(vec3 world_pos) {
	float d = length(world_pos.xz - world_curve_origin.xz);
	float past = max(d - world_curve_flat_radius, 0.0);
	return past * past * world_curve_strength;
}
