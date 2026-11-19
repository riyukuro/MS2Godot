// The client's glow pass (MS2GlowMaterial): glow-mapped objects are drawn into their own target with
// rgb = the glow map's alpha (a grey intensity) and alpha = base alpha * vertex alpha. The bloom's bright pass adds that target.
// Here: unshaded, drawn by the glow camera only (layer 20), the height fog colour is not applied.
shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D base_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D glow_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_tex = false;
uniform bool alpha_test = false;
uniform float alpha_ref = 0.5;

void fragment() {
	float a = has_tex ? texture(base_tex, UV).a : 1.0;
	if (alpha_test && a < alpha_ref) {
		discard;
	}
	// MS2GlowMaterial ps (Shaders/Current/MS2GlowMaterial_ps_3_0_DX9.cache): rgb = lerp(gkHeightFogColor, Glow.a, fog factor)
	float g = texture(glow_tex, UV).a;
	// gamma-space value written as it is (the glow target is read raw by the bloom)
	ALBEDO = vec3(pow(g, 2.2));
}
