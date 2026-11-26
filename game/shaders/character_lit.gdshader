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
// - Standard form (standard_light): a mesh whose NIF names MS2StandardMaterial or MS2AlphaBoostMaterial (material type
//   0) on a character-path model (npcs, trigger, interact and breakable actors, items) is lit as the field's meshes are.
//   MS2StandardMaterial::HandleMS2Light (@0x1427503e0, ms2standardmaterial.cpp:858-1048) gives type 0 the "MS2Light"
//   node (ms2materialnodelibrary.cpp:20, 316-328: N.L, the light's ambient into the diffuse by 1 - N.L) with the light's
//   own world direction, not gakCharacterLightDirection; binds no view vector (the node's default (0, 0, 0), so the half
//   vector is the light vector), and no rim (FresnelBoost / FresnelExponent only for types 1-3). The colour boost
//   (HandleColorBoost, :652-662) and the dye (HandleCustomMaps, :667-676) are types 1-3 only too.
//
// Not done: face decals.
shader_type spatial;
#include "res://shaders/ms2_lights.gdshaderinc"
#include "res://shaders/ms2_fog.gdshaderinc"
// The render mode below is the opaque one. ModelBuilder.CharacterShader replaces that line with the mesh's own: its blend
// (ModelMesh.Blend: blend_mix, blend_add, or blend_premul_alpha with ALPHA 0 for ONE / ONE), its z write and z test
// (NiZBufferProperty, NiDX9RenderState::ApplyZBuffer, NiDX9RenderState.cpp:626-647) and its culling (NiStencilProperty draw
// mode, ModelMesh.Cull), and defines BLEND (blended) and ONE (ONE / ONE) above it.
render_mode unshaded, cull_back;

global uniform vec3 ms2_ambient;       // scene ambient light colour * dimmer
global uniform vec3 ms2_sun_color;     // directional light diffuse colour * dimmer
global uniform vec3 ms2_sun_ambient;   // directional light ambient colour * dimmer
global uniform vec3 ms2_sun_specular;  // directional light specular colour * dimmer
global uniform vec3 ms2_sun_dir;       // world direction the light travels (the standard form's light, as field_lit)
global uniform vec3 ms2_hfog_color;    // height fog colour (gamma space), as field_lit
global uniform vec3 ms2_hfog;          // height fog: upper height, lower height, density (0 = none)

// An instance lit by its own light pair instead of the field's lights (the game menu portrait: UIPortrait::vfunc_14
// attaches a directional and an ambient light to the copy it renders). With own_lights 0, the default, nothing changes.
instance uniform float own_lights = 0.0;
instance uniform float node_alpha = 1.0;   // SetNodeAlpha's factor on the material alpha (NodeFade)
instance uniform vec3 own_ambient = vec3(0.0);
instance uniform vec3 own_sun_color = vec3(0.0);
instance uniform vec3 own_sun_ambient = vec3(0.0);
instance uniform vec3 own_sun_specular = vec3(0.0);
// A chaoslight effect's top layer on this mesh (ChaosLightEffectLayer: the ChaosLightColor, ChaosFresnelBoost,
// ChaosFresnelExponent and ChaosDimmer extra data of the chaos material). With chaos_on 0, the default, nothing changes.
instance uniform float chaos_on = 0.0;
instance uniform vec3 chaos_color = vec3(0.0);
instance uniform float chaos_boost = 0.0;
instance uniform float chaos_exponent = 1.0;
instance uniform float chaos_dimmer = 1.0;

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
uniform bool standard_light = false;   // material type 0: the MS2Light form (header)
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
uniform float mat_alpha = 1.0;   // the NiMaterialProperty alpha (1 without a material)
#include "res://shaders/ms2_alpha_anim.gdshaderinc"
// the terms the vertex colour (COLOR) stands in for (ModelMesh.VertexColors), and LIGHTING_E (ModelMesh.LightingE)
uniform bool vc_emissive = false;
uniform bool vc_diffuse = false;
uniform bool vc_ambient = false;
uniform bool vc_opacity = false;
uniform bool lighting_e = false;
uniform float alpha_ref = 0.5;
// clip above / below / equal to alpha_ref (SCM_DEF_ALPHA_TEST_FUNC, D3D11ShaderConstantMap.cpp:3630-3687)
uniform vec3 alpha_clip = vec3(0.0, 1.0, 0.0);

varying vec3 world_normal;
varying vec3 world_pos;
varying vec3 world_tangent;
varying vec3 world_binormal;

void vertex() {
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	world_tangent = (MODEL_MATRIX * vec4(TANGENT, 0.0)).xyz;
	world_binormal = (MODEL_MATRIX * vec4(BINORMAL, 0.0)).xyz;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	fog_share = ms2_fog_share(VIEW_MATRIX, world_pos, CAMERA_VISIBLE_LAYERS);
}

// One light's specular intensity: Blinn-Phong with the material glossiness as the power, cut where the light's half-lambert
// term is 0; hair averages it with the anisotropic term of its tangent ht (1 - (T.H)^2).
float spec_term(vec3 l, vec3 n, vec3 v, vec3 ht, float hl) {
	vec3 h = normalize(l + v);
	float s = pow(max(1e-5, dot(n, h)), spec_power);
	if (has_hair) {
		float th = dot(ht, h);
		s = 0.5 * (s + pow(max(1e-5, 1.0 - th * th), spec_power));
	}
	return hl <= 0.0 ? 0.0 : s;
}

// MS2Light's specular with no view vector: the half vector is the light vector, so N.H is N.L; cut where N.L <= 0.
float std_spec(float ndl) {
	return ndl > 0.0 ? pow(max(1e-5, ndl), spec_power) : 0.0;
}

void fragment() {
	vec4 t = has_tex ? texture(base_tex, UV) : vec4(1.0);
	// The opacity (ModelMesh.Opacity): the material alpha, or the NiAlphaController's value in its place, or the vertex alpha
	// where the vertex colour is the term the opacity is split from (NiStandardMaterial.cpp:4436-4450), times the base map's
	// alpha (HandleBaseMap, :4503-4525). The alpha test runs on it (HandleAlphaTest after HandleMS2FinalPixelOutputs,
	// ms2standardmaterial.cpp:786), as field_lit's.
	// SetNodeAlpha (NodeFade) scales the material alpha of the meshes without an alpha controller (alphaanimation.cpp:478-545).
	float a = t.a * (vc_opacity ? COLOR.a : (alpha_anim_count > 0 ? alpha_anim() : mat_alpha * node_alpha));
	if (alpha_test && (alpha_clip.x > 0.5 && a > alpha_ref || alpha_clip.y > 0.5 && a < alpha_ref || alpha_clip.z > 0.5 && a == alpha_ref)) {
		discard;
	}
	if (has_ctrl && !standard_light) {
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
	bool own = own_lights > 0.5;
	// STILL-OPEN: an own light pair's direction (UIPortrait::vfunc_14's directional light) for the standard form; it keeps
	// the camera's light direction there
	bool std_form = standard_light && !own;
	vec3 l = std_form ? -ms2_sun_dir : -light_dir;

	float half_lambert = pow(0.5 * dot(l, n) + 0.5, 2.0);
	float fresnel = fresnel_boost * pow(1.0 - abs(dot(n, v)), fresnel_exponent);
	float rim = max(0.0, -dot(rim_dir, n)) * fresnel;
	// MS2Light: LightIntensity = max(0, N.L), no rim
	float intensity = std_form ? max(dot(l, n), 0.0) : half_lambert + rim;
	vec3 sun_color = own ? own_sun_color : ms2_sun_color;
	vec3 sun_ambient = own ? own_sun_ambient : ms2_sun_ambient;
	vec3 diffuse = sun_color * intensity + sun_ambient * (1.0 - intensity);
	if (skin) {
		float ndl = dot(l, n);
		float band = max(0.0, smoothstep(-sss_power, 1.0, ndl) - smoothstep(0.0, 1.0, ndl));
		diffuse += sun_color * sss_color * band;
	}
	vec3 ht = vec3(0.0);
	if (specular_on && has_hair) {
		vec2 d = texture(hair_tex, UV).rg * 2.0 - 1.0;
		ht = normalize(tg * d.x + bn * d.y);
	}
	// the standard form's half vector is the light vector (no view vector bound) and its cut-off tests N.L > 0 (MS2Light)
	vec3 spec_light = !specular_on ? vec3(0.0) : std_form ? ms2_sun_specular * std_spec(dot(l, n))
		: (own ? own_sun_specular : ms2_sun_specular) * spec_term(l, n, v, ht, half_lambert);
	// The field's point and spot lights (ms2_lights.gdshaderinc), each through the same light fragment as the sun with its own
	// vector and Attenuate (MS2CharacterLight, ms2materialnodelibrary.cpp:1336-1440): the half-lambert term plus the rim
	// (the rim does not depend on the light) times LightDiffuse * Attenuate, LightAmbient * (1 - that intensity) unattenuated,
	// the skin's LambSkin band (:3385-3446) on the raw N.L times Attenuate, and the specular times Attenuate. A character gets
	// every light of the field (FieldLightSet: OnRenderedEntityAdded attaches them all, LightService.cpp:1926-1932).
	// The standard form takes them through MS2Light as field_lit does (ms2_point_lights: N.L times Attenuate, the light's
	// ambient by 1 - N.L where the light has one).
	if (std_form) {
		diffuse += ms2_point_lights(world_pos, n, 0);
		if (specular_on) {
			int count = int(ms2_light_count);
			for (int i = 0; i < count; i++) {
				vec3 pl;
				float att = ms2_light_at(i, world_pos, pl);
				spec_light += ms2_light_specular(i) * (std_spec(dot(pl, n)) * att);
			}
		}
	} else if (!own) {
		int count = int(ms2_light_count);
		for (int i = 0; i < count; i++) {
			vec3 pl;
			float att = ms2_light_at(i, world_pos, pl);
			float hl = pow(0.5 * dot(pl, n) + 0.5, 2.0);
			float li = hl + rim;
			vec3 ld = ms2_light_diffuse(i);
			diffuse += ld * (li * att) + ms2_light_ambient(i) * (1.0 - li);
			if (skin) {
				float pndl = dot(pl, n);
				diffuse += ld * sss_color * (max(0.0, smoothstep(-sss_power, 1.0, pndl) - smoothstep(0.0, 1.0, pndl)) * att);
			}
			if (specular_on) {
				spec_light += ms2_light_specular(i) * (spec_term(pl, n, v, ht, hl) * att);
			}
		}
	}
	if (!standard_light) {
		diffuse *= color_boost;
	}

	vec3 spec = vec3(0.0);
	if (specular_on) {
		vec3 g = has_gloss ? texture(gloss_tex, UV).rgb : vec3(1.0);
		spec = spec_light * mat_specular * g;
	}

	// the vertex colour replaces the terms it stands in for; LIGHTING_E keeps the emissive alone (NiStandardMaterial.cpp:3719-3723)
	vec3 emi = vc_emissive ? COLOR.rgb : emissive;
	vec3 amb_m = vc_ambient ? COLOR.rgb : mat_ambient;
	vec3 dif_m = vc_diffuse ? COLOR.rgb : mat_diffuse;
	vec3 lit = clamp(lighting_e ? emi : emi + (own ? own_ambient : ms2_ambient) * amb_m + diffuse * dif_m, 0.0, 1.0);
	vec3 c = lit * t.rgb;
	bool chaos = chaos_on > 0.5;
	// a chaos material applies no glow map (GLOWMAPCOUNT && !m_bChaos)
	if (has_glow && !chaos) {
		c += texture(glow_tex, UV).rgb * glow_scale;
	}
	c += spec;
	// HandleChaosRim: after the lit composite, before the fog, the colour dimmed and the chaos colour added along the rim
	if (chaos) {
		float cf = chaos_boost * pow(1.0 - abs(dot(n, v)), chaos_exponent);
		c = c * chaos_dimmer + chaos_color * (max(0.0, -dot(rim_dir, n)) * cf);
	}
	// Fog after the composite of diffuse and specular, before the alpha (MS2StandardMaterial::HandleMS2FinalPixelOutputs,
	// ms2standardmaterial.cpp:774-776): first the height fog, as field_lit's (the character materials take their
	// descriptor from NiStandardMaterial::GenerateDescriptor too, so HEIGHTFOG is on unless additive), then the depth fog
	// (ms2_fog.gdshaderinc). Both only through a field camera (FOG_LAYER); the UI views draw neither.
	if ((CAMERA_VISIBLE_LAYERS & FOG_LAYER) != 0u) {
		float hf = ms2_hfog.z * (1.0 - clamp((world_pos.y - ms2_hfog.y) / (ms2_hfog.x - ms2_hfog.y), 0.0, 1.0));
		c = mix(c, ms2_hfog_color, hf);
	}
	ALBEDO = c;   // the gamma value: the colour buffer holds gamma values (field_lit.gdshader)
#ifdef BLEND
#ifdef ONE
	// ONE / ONE adds the colour unweighted: blend_premul_alpha is src + dst * (1 - ALPHA)
	ALPHA = 0.0;
#else
	ALPHA = a;
#endif
#endif
	FOG = vec4(ms2_fog_color, fog_share);
}
