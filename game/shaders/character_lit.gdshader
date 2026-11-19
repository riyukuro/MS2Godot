// Character lighting after the client's MS2 character materials (MS2CharacterMaterial, MS2CharacterSkinMaterial,
// MS2CharacterHairMaterial), worked out from what the client's material code does:
//
// - Dye: where the NIF has a colour-control map (_C texture, shader map 0), the base colour is blended towards a mix
//   of three override colours: colour 0 where the control's red is high, colour 2 where it is low, plus colour 1 by
//   the green channel. The control's alpha is the blend weight. Skin parts get the player's skin colours as colours 0
//   and 2; items get their dye colours as 0, 1 and 2.
// - Diffuse: the sun term uses a squared half-lambert falloff instead of N.L, plus a rim term (a Fresnel-shaped edge
//   glow scaled by how much the normal faces away from the rim direction). The sun's own ambient colour fills
//   the part the sun leaves out.
// - Skin adds a reddish scatter band around the light's terminator (the "lambSkin" term).
// - Characters take the sun direction from the camera, not from the field's light: the camera's flat direction
//   tilted 30 degrees down; the rim direction is the camera direction tilted another 30 degrees down.
// - Normal map: a tangent-space normal (texture * 2 - 1) through the mesh's own tangent, binormal and normal.
// - Specular, for meshes with an enabled specular property: Blinn-Phong with the material glossiness as the power,
//   times the light's specular colour, the material specular colour and the gloss map. The cut-off tests the
//   half-lambert term, so it never drops to zero on the dark side. Hair averages that with an anisotropic term: its
//   tangent map gives a direction T in the surface plane and the highlight follows 1 - (T.H)^2.
// - Final: emissive + scene ambient * material ambient + diffuse * colour boost * material diffuse, clamped, times
//   the dyed texture, plus the glow map (MS2StandardMaterial meshes, times GlowModifier), plus the specular; gamma
//   space, written as the gamma value, as field_lit.gdshader.
//
// Not done: face decals.
shader_type spatial;
#include "res://shaders/ms2_lights.gdshaderinc"
render_mode unshaded, cull_back;

global uniform vec3 ms2_ambient;       // scene ambient light colour * dimmer
global uniform vec3 ms2_sun_color;     // directional light diffuse colour * dimmer
global uniform vec3 ms2_sun_ambient;   // directional light ambient colour * dimmer
global uniform vec3 ms2_sun_specular;  // directional light specular colour * dimmer

// An instance lit by its own light pair instead of the field's lights (the game menu portrait: UIPortrait::vfunc_14
// attaches a directional and an ambient light to the copy it renders). With own_lights 0, the default, nothing changes.
instance uniform float own_lights = 0.0;
instance uniform vec3 own_ambient = vec3(0.0);
instance uniform vec3 own_sun_color = vec3(0.0);
instance uniform vec3 own_sun_ambient = vec3(0.0);
instance uniform vec3 own_sun_specular = vec3(0.0);

uniform sampler2D base_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D ctrl_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_tex = false;
uniform bool has_ctrl = false;
uniform vec3 color0 = vec3(1.0, 0.0, 0.0);
uniform vec3 color1 = vec3(0.0, 1.0, 0.0);
uniform vec3 color2 = vec3(0.0, 0.0, 1.0);
uniform vec3 mat_ambient = vec3(1.0);
uniform vec3 mat_diffuse = vec3(1.0);
uniform vec3 emissive = vec3(0.0);
uniform float color_boost = 1.0;
uniform float fresnel_boost = 5.0;
uniform float fresnel_exponent = 4.0;
uniform bool skin = false;
uniform vec3 sss_color = vec3(1.0, 0.2, 0.2);   // client defaults (rim light settings)
uniform float sss_power = 1.0;
uniform float light_pitch_deg = 30.0;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gloss_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D hair_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_normal = false;
uniform bool has_gloss = false;
uniform bool has_hair = false;
uniform bool specular_on = false;
uniform vec3 mat_specular = vec3(0.0);
uniform float spec_power = 1.0;
uniform sampler2D glow_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_glow = false;
uniform float glow_scale = 1.0;
uniform bool alpha_test = false;
uniform float alpha_ref = 0.5;

varying vec3 world_normal;
varying vec3 world_pos;
varying vec3 world_tangent;
varying vec3 world_binormal;

void vertex() {
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	world_tangent = (MODEL_MATRIX * vec4(TANGENT, 0.0)).xyz;
	world_binormal = (MODEL_MATRIX * vec4(BINORMAL, 0.0)).xyz;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 t = has_tex ? texture(base_tex, UV) : vec4(1.0);
	if (alpha_test && t.a < alpha_ref) {
		discard;
	}
	if (has_ctrl) {
		vec4 c = texture(ctrl_tex, UV);
		vec3 dye = color0 * c.r + color1 * c.g + color2 * (1.0 - c.r);
		t.rgb = mix(t.rgb, dye, c.a);
	}

	vec3 n = normalize(world_normal);
	vec3 tg = normalize(world_tangent);
	vec3 bn = normalize(world_binormal);
	if (has_normal) {
		vec3 m = texture(normal_tex, UV).rgb * 2.0 - 1.0;
		n = normalize(tg * m.x + bn * m.y + n * m.z);
	}
	vec3 cam_pos = INV_VIEW_MATRIX[3].xyz;
	vec3 v = normalize(cam_pos - world_pos);

	// light and rim directions from the camera direction (Y up here)
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 fwd = -INV_VIEW_MATRIX[2].xyz;
	vec3 flat_dir = normalize(vec3(fwd.x, 0.0, fwd.z) + vec3(1e-5, 0.0, 0.0));
	float tilt = radians(light_pitch_deg);
	float pitch = asin(clamp(-fwd.y, -1.0, 1.0));
	vec3 light_dir = flat_dir * cos(tilt) - up * sin(tilt);
	vec3 rim_dir = flat_dir * cos(pitch + tilt) - up * sin(pitch + tilt);
	vec3 l = -light_dir;

	float half_lambert = pow(0.5 * dot(l, n) + 0.5, 2.0);
	float fresnel = fresnel_boost * pow(1.0 - abs(dot(n, v)), fresnel_exponent);
	float intensity = half_lambert + max(0.0, -dot(rim_dir, n)) * fresnel;
	bool own = own_lights > 0.5;
	vec3 sun_color = own ? own_sun_color : ms2_sun_color;
	vec3 sun_ambient = own ? own_sun_ambient : ms2_sun_ambient;
	vec3 diffuse = sun_color * intensity + sun_ambient * (1.0 - intensity);
	if (skin) {
		float ndl = dot(l, n);
		float band = max(0.0, smoothstep(-sss_power, 1.0, ndl) - smoothstep(0.0, 1.0, ndl));
		diffuse += sun_color * sss_color * band;
	}
	if (!own) {
		diffuse += ms2_point_lights(world_pos, n);
	}
	diffuse *= color_boost;

	vec3 spec = vec3(0.0);
	if (specular_on) {
		vec3 h = normalize(l + v);
		float s = pow(max(1e-5, dot(n, h)), spec_power);
		if (has_hair) {
			vec2 d = texture(hair_tex, UV).rg * 2.0 - 1.0;
			vec3 ht = normalize(tg * d.x + bn * d.y);
			float th = dot(ht, h);
			s = 0.5 * (s + pow(max(1e-5, 1.0 - th * th), spec_power));
		}
		if (half_lambert <= 0.0) s = 0.0;
		vec3 g = has_gloss ? texture(gloss_tex, UV).rgb : vec3(1.0);
		spec = (own ? own_sun_specular : ms2_sun_specular) * s * mat_specular * g;
	}

	vec3 lit = clamp(emissive + (own ? own_ambient : ms2_ambient) * mat_ambient + diffuse * mat_diffuse, 0.0, 1.0);
	vec3 c = lit * t.rgb;
	if (has_glow) {
		c += texture(glow_tex, UV).rgb * glow_scale;
	}
	ALBEDO = c + spec;   // the gamma value: the colour buffer holds gamma values (field_lit.gdshader)
}
