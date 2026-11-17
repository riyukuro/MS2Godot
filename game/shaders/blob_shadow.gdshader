// The simple ground shadow of a character (the client's CObjectShadow): a soft dark disc on the ground under the actor.
// The quad is 2 x 2 in the ground plane, so the local position is the distance from the centre.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

uniform float strength = 0.5;

varying vec2 local;

void vertex() {
	local = VERTEX.xz;
}

void fragment() {
	float d = length(local);
	ALBEDO = vec3(0.0);
	ALPHA = strength * (1.0 - smoothstep(0.3, 1.0, d));
}
