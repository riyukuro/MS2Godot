// The simple ground shadow of a character (the client's CObjectShadow): Shadow.dds on a quad lying on the ground,
// black material, alpha = texture alpha * simpleshadow * height fade; alpha blended, no depth write, no fog.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

uniform sampler2D shadow_tex : filter_linear, repeat_disable;
uniform float strength = 0.5;

void fragment() {
	ALBEDO = vec3(0.0);
	ALPHA = texture(shadow_tex, UV).a * strength;
}
