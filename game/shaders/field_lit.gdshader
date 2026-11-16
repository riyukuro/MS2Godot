// Field mesh lighting in the way Gamebryo's standard material does it on D3D9: per-pixel
// emissive + ambient * material + sun * material * N.L, clamped to 0..1, times the base texture
// (and the dark map), all in gamma space: textures are sampled raw, not as sRGB.
// The material colour (ambient = diffuse) is the entity's MaterialColor, passed as the MultiMesh
// instance colour (COLOR); vertex colours come in CUSTOM0 and replace it when the NIF's
// NiVertexColorProperty says so. ModelBuilder compiles this twice: as is, and with BLEND defined
// for NiAlphaProperty blending (no depth write). No cast shadows yet.
shader_type spatial;
#ifdef BLEND
render_mode unshaded, blend_mix, depth_draw_never;
#else
render_mode unshaded;
#endif

global uniform vec3 ms2_ambient;     // ambient light colour * dimmer
global uniform vec3 ms2_sun_color;   // directional light colour * dimmer
global uniform vec3 ms2_sun_dir;     // world direction the light travels

uniform sampler2D base_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dark_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_tex = false;
uniform bool has_dark = false;
uniform bool dark_uv2 = false;
uniform bool has_vcolor = false;     // CUSTOM0 holds rgba vertex colours
uniform bool vcolor_amb_diff = false;
uniform bool alpha_test = false;
uniform float alpha_ref = 0.5;
uniform vec3 emissive = vec3(0.0);

varying vec3 world_normal;
varying vec4 vcolor;

void vertex() {
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	vcolor = has_vcolor ? CUSTOM0 : vec4(1.0);
}

vec3 to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	vec4 t = has_tex ? texture(base_tex, UV) : vec4(1.0);
	if (has_dark) {
		t.rgb *= texture(dark_tex, dark_uv2 ? UV2 : UV).rgb;
	}
	float a = t.a * vcolor.a;
	if (alpha_test && a < alpha_ref) {
		discard;
	}
	vec3 mat = vcolor_amb_diff ? vcolor.rgb : COLOR.rgb;
	float ndl = max(dot(normalize(world_normal), -ms2_sun_dir), 0.0);
	vec3 lit = clamp(emissive + ms2_ambient * mat + ms2_sun_color * mat * ndl, 0.0, 1.0);
	// the output is written in gamma space by Godot's linear -> sRGB conversion
	ALBEDO = to_linear(lit * t.rgb);
#ifdef BLEND
	ALPHA = a;
#endif
}
