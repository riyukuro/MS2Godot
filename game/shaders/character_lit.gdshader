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
// - Final: emissive + scene ambient * material ambient + diffuse * colour boost * material diffuse, clamped, times
//   the dyed texture; gamma space, as field_lit.gdshader.
//
// Not done: normal and gloss maps (specular), hair anisotropy, face decals.
shader_type spatial;
render_mode unshaded, cull_back;

global uniform vec3 ms2_ambient;       // scene ambient light colour * dimmer
global uniform vec3 ms2_sun_color;     // directional light diffuse colour * dimmer
global uniform vec3 ms2_sun_ambient;   // directional light ambient colour * dimmer

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
uniform bool alpha_test = false;
uniform float alpha_ref = 0.5;

varying vec3 world_normal;
varying vec3 world_pos;

void vertex() {
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec3 to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
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
	vec3 diffuse = ms2_sun_color * intensity + ms2_sun_ambient * (1.0 - intensity);
	if (skin) {
		float ndl = dot(l, n);
		float band = max(0.0, smoothstep(-sss_power, 1.0, ndl) - smoothstep(0.0, 1.0, ndl));
		diffuse += ms2_sun_color * sss_color * band;
	}
	diffuse *= color_boost;

	vec3 lit = clamp(emissive + ms2_ambient * mat_ambient + diffuse * mat_diffuse, 0.0, 1.0);
	ALBEDO = to_linear(lit * t.rgb);
}
