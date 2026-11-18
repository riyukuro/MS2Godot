// Field mesh lighting in the way Gamebryo's standard material does it on D3D9: per-pixel
// emissive + ambient * material + material * (sun * N.L + sun ambient * (1 - N.L)), times the base
// texture (with the decal and dark maps), all in gamma space: textures are sampled raw, not as sRGB. Not clamped:
// the client renders to an HDR target, and values over 1 feed the bloom pass (Ms2Bloom).
// The material colour (ambient = diffuse) is the entity's MaterialColor, passed as the MultiMesh
// instance colour (COLOR); an instance colour with alpha 0 (no MaterialColor) keeps the NIF's own
// ambient and diffuse. Vertex colours come in CUSTOM0 and replace both when the NIF's
// NiVertexColorProperty says so. A glow map adds its colour on top. ModelBuilder compiles this three
// times: opaque, with BLEND (NiAlphaProperty src alpha / inverse src alpha) and with BLEND and ADD
// (src alpha / one); both blended forms skip the depth write. A fourth form, SHELL (with BLEND), is the depth-only
// first pass of a fluid cube: colour unchanged (alpha 0 = blend ZERO / ONE), depth written (ModelBuilder.MakeFluidShell). The field's point and spot lights are added to the
// diffuse term (ms2_lights.gdshaderinc). The opaque form is also lit by the field's point and spot
// lights (FieldLights) through Godot's own lighting; the blended forms are not. No cast shadows yet.
// Texture animation from the NIF's controllers: a flipbook (NiFlipController) on the base or dark slot plays its frames
// evenly over flip_period, looping; a uv translation (NiTextureTransformController, translate U or V) moves linearly
// between its first and last key over the controller's start..stop range, looping.
shader_type spatial;
#include "res://shaders/ms2_lights.gdshaderinc"
#ifdef SHELL
render_mode unshaded, blend_mix, depth_draw_always;
#else
#ifdef FADE
// a prop faded out by BGTransparencyMgr (world/FieldFade): TransparencyMaterial::FUN_141860140 turns blending on (the
// mesh's own NiAlphaProperty cloned, or a default one) and the z-buffer write on
#ifdef ADD
render_mode unshaded, blend_add, depth_draw_always;
#else
render_mode unshaded, blend_mix, depth_draw_always;
#endif
#else
#ifdef ADD
render_mode unshaded, blend_add, depth_draw_never;
#else
#ifdef BLEND
render_mode unshaded, blend_mix, depth_draw_never;
#else
render_mode ambient_light_disabled, specular_disabled;
#define POINT_LIT
#endif
#endif
#endif
#endif

global uniform vec3 ms2_ambient;     // ambient light colour * dimmer
global uniform vec3 ms2_sun_color;   // directional light colour * dimmer
global uniform vec3 ms2_sun_ambient; // directional light ambient colour * dimmer
global uniform vec3 ms2_hfog_color; // height fog colour (gamma space)
global uniform vec3 ms2_hfog;       // height fog: upper height, lower height, density at and below the lower height (0 = none)
global uniform vec3 ms2_sun_dir;     // world direction the light travels

uniform sampler2D base_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dark_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_tex = false;
uniform bool has_dark = false;
uniform sampler2D decal_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_decal = false;      // decal slot 0: laid over the base by its alpha (banner emblems)
uniform bool dark_uv2 = false;
uniform bool has_vcolor = false;     // CUSTOM0 holds rgba vertex colours
uniform bool vcolor_amb_diff = false;
uniform bool alpha_test = false;
uniform float alpha_ref = 0.5;
uniform float fade_alpha = 1.0;     // FADE: the NiMaterialProperty alpha TransparencyStandardMaterial sets (vfunc_4)
// FADE: the mesh has its own NiVertexColorProperty, which TransparencyMaterial::FUN_141860140 swaps for a new one that
// TransparencyStandardMaterial::vfunc_2 sets to source mode 3, lighting E_A_D. The engine's descriptor takes mode 3 as
// AMBDIFFEMISSIVE 3 (0x141de4a60, only with vertex colours), and its colour setup (0x141d8e600) then takes the ambient from
// the vertex colours and the diffuse and emissive from the material; the opacity is split from the diffuse, so it is the
// material alpha, not the vertex alpha.
uniform bool fade_vc_swap = false;
uniform vec3 emissive = vec3(0.0);
uniform vec3 mat_ambient = vec3(0.7058824);
uniform vec3 mat_diffuse = vec3(0.7058824);
uniform sampler2D glow_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_glow = false;
uniform sampler2DArray flip_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform int flip_slot = -1;          // 0 base, 1 dark
uniform int flip_count = 1;
uniform float flip_period = 1.0;
uniform int base_scroll_axis = -1;   // 0 u, 1 v
uniform vec4 base_scroll_key;        // first time, last time, first value, last value
uniform vec2 base_scroll_range;      // controller start, stop
uniform int dark_scroll_axis = -1;
uniform vec4 dark_scroll_key;
uniform vec2 dark_scroll_range;

varying vec3 world_normal;
varying vec3 world_pos;
varying vec4 vcolor;

void vertex() {
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vcolor = has_vcolor ? CUSTOM0 : vec4(1.0);
}

vec3 to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

vec2 scroll(int axis, vec4 key, vec2 range) {
	if (axis < 0) {
		return vec2(0.0);
	}
	float t = range.x + mod(TIME, max(range.y - range.x, 1e-3));
	float v = mix(key.z, key.w, clamp((t - key.x) / max(key.y - key.x, 1e-6), 0.0, 1.0));
	return axis == 0 ? vec2(v, 0.0) : vec2(0.0, v);
}

vec4 sample_slot(sampler2D tex, int slot, vec2 uv) {
	if (flip_slot == slot) {
		float frame = floor(mod(TIME, flip_period) / flip_period * float(flip_count));
		return texture(flip_tex, vec3(uv, min(frame, float(flip_count - 1))));
	}
	return texture(tex, uv);
}

void fragment() {
	vec2 buv = UV + scroll(base_scroll_axis, base_scroll_key, base_scroll_range);
	vec4 t = has_tex ? sample_slot(base_tex, 0, buv) : vec4(1.0);
	if (has_decal) {
		vec4 d = texture(decal_tex, UV);
		t.rgb = mix(t.rgb, d.rgb, d.a);
	}
	if (has_dark) {
		vec2 duv = (dark_uv2 ? UV2 : UV) + scroll(dark_scroll_axis, dark_scroll_key, dark_scroll_range);
		t.rgb *= sample_slot(dark_tex, 1, duv).rgb;
	}
	float a = t.a * vcolor.a;
#ifdef FADE
	if (fade_vc_swap) {
		a = t.a;
	}
	a *= fade_alpha;    // the faded material alpha; alpha_ref is the test reference scaled the same way
#endif
	if (alpha_test && a < alpha_ref) {
		discard;
	}
	vec3 amb = vcolor_amb_diff ? vcolor.rgb : COLOR.a > 0.5 ? COLOR.rgb : mat_ambient;
	vec3 dif = vcolor_amb_diff ? vcolor.rgb : COLOR.a > 0.5 ? COLOR.rgb : mat_diffuse;
#ifdef FADE
	if (fade_vc_swap) {
		amb = has_vcolor ? vcolor.rgb : COLOR.a > 0.5 ? COLOR.rgb : mat_ambient;
		dif = COLOR.a > 0.5 ? COLOR.rgb : mat_diffuse;
	}
#endif
	float ndl = max(dot(normalize(world_normal), -ms2_sun_dir), 0.0);
	vec3 lit = emissive + ms2_ambient * amb + dif * (ms2_sun_color * ndl + ms2_sun_ambient * (1.0 - ndl) + ms2_point_lights(world_pos, normalize(world_normal)));
	vec3 c = lit * t.rgb;
	if (has_glow) {
		c += texture(glow_tex, UV).rgb;
	}
	// Height fog (mapxblock heightfog): a standard height blend, none above `upper`, full density at and below `lower`.
	// The exact client curve is not recoverable from the on-disk shaders; the numbers are the map's own.
	float hf = ms2_hfog.z * (1.0 - smoothstep(ms2_hfog.y, ms2_hfog.x, world_pos.y));
#ifndef ADD
	c = mix(c, ms2_hfog_color, hf);
#endif
	// the output is written in gamma space by Godot's linear -> sRGB conversion
#ifdef POINT_LIT
	// opaque: the sun and ambient stay in the term above (emission); Godot's point and spot lights add on the diffuse
	EMISSION = to_linear(c);
	ALBEDO = to_linear(t.rgb * dif) * (1.0 - hf);
	ROUGHNESS = 1.0;
#else
	ALBEDO = to_linear(c);
#endif
#ifdef BLEND
	ALPHA = a;
#endif
#ifdef SHELL
	ALPHA = 0.0;
#endif
}
