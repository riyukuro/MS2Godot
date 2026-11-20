// Field mesh lighting in the way the client's MS2StandardMaterial does it on D3D9: per-pixel
// emissive + material ambient * ambient + material diffuse * (sun * N.L + sun ambient * (1 - N.L)), times the base
// texture (with the decal and dark maps), all in gamma space: textures are sampled raw, not as sRGB. Not clamped:
// the client renders to an HDR target, and values over 1 feed the bloom pass (Ms2Bloom).
// The output is the gamma value itself. The client blends in gamma space: D3D9 keeps D3DRS_SRGBWRITEENABLE and
// D3DSAMP_SRGBTEXTURE at their initial 0 (NiDX9RenderState.cpp:132, :209), nothing in the client or engine sets them,
// so its target holds the values the shaders write and every blend mixes those. The field's colour buffer holds gamma
// values the same way; Ms2Bloom's final pass converts it to linear once, for Godot's linear -> sRGB output (GammaOut
// does that for views without the bloom).
// The material colour (ambient = diffuse) is the entity's MaterialColor, passed as the MultiMesh
// instance colour (COLOR); an instance colour with alpha 0 (no MaterialColor) keeps the NIF's own
// ambient and diffuse. Vertex colours come in CUSTOM0 and replace both when the NIF's
// NiVertexColorProperty says so. A glow map adds its colour on top. ModelBuilder compiles this three
// times: opaque, with BLEND (NiAlphaProperty src alpha / inverse src alpha) and with BLEND and ADD
// (src alpha / one); both blended forms skip the depth write. A fourth form, SHELL (with BLEND), is the depth-only
// first pass of a fluid cube: colour unchanged (alpha 0 = blend ZERO / ONE), depth written (ModelBuilder.MakeFluidShell).
// The field's point and spot lights are added to the
// diffuse term (ms2_lights.gdshaderinc). The opaque form takes the sun's shadow from Godot's DirectionalLight3D (Field.cs)
// and nothing else: light() scales the sun's share of the colour above by the shadow, so the sun is counted once. The
// blended forms (BLEND, ADD, FADE) take it the same way: the client's receiver list (ms2shadowclickgenerator.cpp:914-975)
// and material descriptor do not filter on alpha, and a light with a shadow map turns per-vertex lighting off
// (NiStandardMaterial.cpp:652-666, SHADOWMAPFORLIGHT -> PERVERTEXFORLIGHTS 0), so a blended receiver lights per pixel like
// the opaque form; a blended copy that receives no shadow (ms2_receive 0) keeps the per-vertex env uv and no sun specular.
// Texture animation from the NIF's controllers: a flipbook (NiFlipController) on the base or dark slot plays its frames
// evenly over flip_period, looping; a uv translation (NiTextureTransformController, translate U or V) moves linearly
// between its first and last key over the controller's start..stop range, looping.
// A sphere-mapped environment map (NiTextureEffect on the mesh's node or an ancestor) adds to the specular, and
// MS2GlassMaterial turns that specular into opacity too (see fragment()).
shader_type spatial;
#include "res://shaders/ms2_lights.gdshaderinc"
#include "res://shaders/ms2_fog.gdshaderinc"
// Culling from the mesh's NiStencilProperty draw mode (ModelMesh.DrawMode, ModelBuilder.FieldShader), as NiDX9RenderState
// maps it (NiDX9RenderState.cpp:380-389, left-handed off): DRAW_BOTH = no culling, DRAW_CW = the front faces, DRAW_CCW and
// DRAW_CCW_OR_BOTH (the default) = the back faces. The client's standard pixel shaders read no VFACE, so a back face lights
// with the front face's normal, as world_normal does here.
#if defined(CULL_NONE)
#define MS2_CULL cull_disabled
#elif defined(CULL_FRONT)
#define MS2_CULL cull_front
#else
#define MS2_CULL cull_back
#endif
#ifdef SHELL
render_mode unshaded, blend_mix, depth_draw_always, MS2_CULL;
#else
#ifdef FADE
// a prop faded out by BGTransparencyMgr (world/FieldFade): TransparencyMaterial::FUN_141860140 turns blending on (the
// mesh's own NiAlphaProperty cloned, or a default one) and the z-buffer write on
#ifdef ADD
render_mode ambient_light_disabled, specular_disabled, blend_add, depth_draw_always, MS2_CULL;
#else
render_mode ambient_light_disabled, specular_disabled, blend_mix, depth_draw_always, MS2_CULL;
#endif
#else
#ifdef ADD
render_mode ambient_light_disabled, specular_disabled, blend_add, depth_draw_never, MS2_CULL;
#else
#ifdef BLEND
render_mode ambient_light_disabled, specular_disabled, blend_mix, depth_draw_never, MS2_CULL;
#else
render_mode ambient_light_disabled, specular_disabled, MS2_CULL;
#define POINT_LIT
#endif
#endif
#endif
// every form but SHELL takes the sun's shadow through light()
#define LIT
#endif

global uniform vec3 ms2_ambient;     // ambient light colour * dimmer
global uniform vec3 ms2_sun_color;   // directional light colour * dimmer
global uniform vec3 ms2_sun_ambient; // directional light ambient colour * dimmer
global uniform vec3 ms2_hfog_color; // height fog colour (gamma space)
global uniform vec3 ms2_hfog;       // height fog: upper height, lower height, density at and below the lower height (0 = none)
global uniform vec3 ms2_sun_dir;     // world direction the light travels
global uniform vec3 ms2_sun_specular; // directional light specular colour * dimmer

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
// NiMaterialProperty alpha (1 without a material): the opacity unless AMB_DIFF vertex colours give it (see fragment())
uniform float mat_alpha = 1.0;
// An NiAlphaController on the NiMaterialProperty (NifModel.ReadAlphaController): up to 8 keys, linear (type 1) or
// Bezier (type 2); alpha_anim_time = start, stop, frequency, phase. alpha_anim_count 0 keeps mat_alpha.
uniform int alpha_anim_count = 0;
uniform int alpha_anim_type = 1;
uniform vec4 alpha_anim_time = vec4(0.0, 1.0, 1.0, 0.0);
uniform float alpha_anim_t[8];
uniform float alpha_anim_v[8];
uniform float alpha_anim_in[8];
uniform float alpha_anim_out[8];
uniform float fade_alpha = 1.0;     // FADE: the NiMaterialProperty alpha TransparencyStandardMaterial sets (vfunc_4)
// FADE: the mesh has its own NiVertexColorProperty, which TransparencyMaterial::FUN_141860140 swaps for a new one that
// TransparencyStandardMaterial::vfunc_2 sets to source mode 3, lighting E_A_D. The engine's descriptor takes mode 3 as
// AMBDIFFEMISSIVE 3 (0x141de4a60, only with vertex colours), and its colour setup (0x141d8e600) then takes the ambient from
// the vertex colours and the diffuse and emissive from the material; the opacity is split from the diffuse, so it is the
// material alpha, not the vertex alpha.
uniform bool fade_vc_swap = false;
uniform vec3 emissive = vec3(0.0);
// sphere-mapped environment map: the effect texture (Texture Clamping 0 = clamp both), its projection axes in the mesh's
// own space (NifModel ReadEnvMap) and the gloss map that scales it
uniform sampler2D env_tex : filter_linear_mipmap, repeat_disable;
uniform bool has_env = false;
uniform vec3 env_u = vec3(0.0, 0.0, 1.0);
uniform vec3 env_v = vec3(0.0, 1.0, 0.0);
uniform sampler2D gloss_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_gloss = false;
uniform bool glass = false;          // MS2GlassMaterial
uniform vec3 mat_ambient = vec3(0.7058824);
uniform vec3 mat_diffuse = vec3(0.7058824);
uniform sampler2D glow_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_glow = false;
uniform sampler2DArray flip_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform int flip_slot = -1;          // 0 base, 1 dark
uniform int flip_count = 1;
uniform float flip_period = 1.0;
#include "res://shaders/field_uv.gdshaderinc"
#include "res://shaders/field_billboard.gdshaderinc"

varying vec3 world_normal;
varying vec2 base_uv;    // each drawn map's uv through its texture transform (field_uv map_uv)
varying vec2 dark_uv;
varying vec2 glow_uv;
varying vec2 decal_uv;
varying vec3 world_pos;
varying vec4 vcolor;
varying vec3 env_u_world;
varying vec3 env_v_world;
#ifndef POINT_LIT
varying vec2 env_uv;
#endif
#ifdef LIT
varying vec3 sun_share;  // the sun's part of the output colour (gamma), which the shadow scales in light()
// 0 for copies the client takes out of the shadow's receiver list (ShadowReceivers: IsReceivingShadow off, or a
// UseShellRender instancing batch below shadow level 4): the sun's share is then not scaled by the shadow
instance uniform float ms2_receive = 1.0;
#endif

// The SPHERICAL texture effect uv (NiStandardMaterial::HandleTextureEffectUVSetGen): the world reflection
// R = normalize(reflect(-(CameraPos - WorldPos), normalize(WorldNrm))) (CalculateViewVector, WorldReflect) projected by
// the effect's world projection matrix (ProjectTextureCoordinates with SCM_OBJ_WORLDPROJECTIONTRANSFORM, made by
// NiTextureEffect::UpdateProjection for SPHERE_MAP): u = 0.5 R.U + 0.5, v = -0.5 R.V + 0.5.
vec2 env_sphere_uv(vec3 wpos, vec3 wnrm, vec3 cam, vec3 u_axis, vec3 v_axis) {
	vec3 r = normalize(reflect(-(cam - wpos), normalize(wnrm)));
	return vec2(0.5 * dot(u_axis, r) + 0.5, -0.5 * dot(v_axis, r) + 0.5);
}

void vertex() {
	if (bb_mode >= 0) {
		mat4 f = bb_matrix(MODEL_MATRIX, INV_VIEW_MATRIX);
		VERTEX = (f * vec4(VERTEX, 1.0)).xyz;
		NORMAL = normalize(mat3(f) * NORMAL);
	}
	base_uv = map_uv(0, UV);
	dark_uv = map_uv(1, dark_uv2 ? UV2 : UV);
	glow_uv = map_uv(2, UV);
	decal_uv = map_uv(3, UV);
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vcolor = has_vcolor ? CUSTOM0 : vec4(1.0);
	fog_share = ms2_fog_share(VIEW_MATRIX, world_pos, CAMERA_VISIBLE_LAYERS);
	env_u_world = normalize((MODEL_MATRIX * vec4(env_u, 0.0)).xyz);
	env_v_world = normalize((MODEL_MATRIX * vec4(env_v, 0.0)).xyz);
#ifndef POINT_LIT
	// unshadowed (blended) meshes light per vertex (PERVERTEXFORLIGHTS), and the sphere-map uv is then made in the
	// vertex shader too (NiStandardMaterial, ENVMAPTYPE on the vertex descriptor without a normal map)
	env_uv = env_sphere_uv(world_pos, world_normal, INV_VIEW_MATRIX[3].xyz, env_u_world, env_v_world);
#endif
}

// The alpha controller's value (field_uv loop_time, then the end values outside the keys, else key_segment).
float alpha_anim() {
	float t = loop_time(alpha_anim_time);
	int n = alpha_anim_count;
	if (t <= alpha_anim_t[0]) {
		return alpha_anim_v[0];
	}
	float r = alpha_anim_v[n - 1];
	for (int i = 0; i < 7; i++) {
		if (i + 1 >= n) {
			break;
		}
		float t0 = alpha_anim_t[i], t1 = alpha_anim_t[i + 1];
		if (t <= t1) {
			r = key_segment(alpha_anim_type, (t - t0) / max(t1 - t0, 1e-6), alpha_anim_v[i], alpha_anim_v[i + 1], alpha_anim_out[i], alpha_anim_in[i + 1]);
			break;
		}
	}
	return r;
}

vec4 sample_slot(sampler2D tex, int slot, vec2 uv) {
	if (flip_slot == slot) {
		float frame = floor(mod(anim_time(), flip_period) / flip_period * float(flip_count));
		return texture(flip_tex, vec3(uv, min(frame, float(flip_count - 1))));
	}
	return texture(tex, uv);
}

void fragment() {
	vec4 t = has_tex ? sample_slot(base_tex, 0, base_uv) : vec4(1.0);
	if (has_decal) {
		vec4 d = texture(decal_tex, decal_uv);
		t.rgb = mix(t.rgb, d.rgb, d.a);
	}
	if (has_dark) {
		t.rgb *= sample_slot(dark_tex, 1, dark_uv).rgb;
	}
	// NiStandardMaterial::HandleInitialSpecAmbDiffEmissiveColor (NiStandardMaterial.cpp:4360-4456): the opacity is split from the diffuse colour, which
	// is the vertex colour only for source AMB_DIFF with lighting E_A_D; otherwise it is the material diffuse, whose alpha is
	// the NiMaterialProperty alpha (a mesh without its own NiVertexColorProperty has the default, SOURCE_IGNORE + E_A_D,
	// NiVertexColorProperty.inl:17-22). The base map's alpha multiplies it (HandleBaseMap, :4503-4525). FADE: the fade
	// material's alpha replaces the material alpha, and its swapped property (source mode 3) takes the diffuse from the
	// material too (:4398, "MS2: v2525").
	bool vc_opacity = vcolor_amb_diff;
	float m_alpha = alpha_anim_count > 0 ? alpha_anim() : mat_alpha;
#ifdef FADE
	if (fade_vc_swap) {
		vc_opacity = false;
	}
	m_alpha = fade_alpha;    // the faded material alpha; alpha_ref is the test reference scaled the same way
#endif
	float a = t.a * (vc_opacity ? vcolor.a : m_alpha);
	// The environment map's specular (MS2StandardMaterial::HandleMS2PostLightTextureApplication,
	// ms2standardmaterial.cpp:1489-1560, through NiStandardMaterial::HandleEnvMap): the env rgb sampled saturated
	// (TextureRGBSample) times the glossiness, the gloss map's rgb (HandleGlossMap; no gloss map: the env alone), times the
	// first directional light's specular * dimmer (SCM_OBJ_SPECULAR, HandleMS2Light) when the pixel shader lights; added
	// to the specular accumulator. Per-pixel lighting (a copy that receives the sun's shadow) has that light; per-vertex
	// lighting (a blended copy that receives none) leaves it out. This shader draws no light specular (NiSpecularProperty) to add it to.
	vec3 spec = vec3(0.0);
	if (has_env) {
		vec2 euv = env_sphere_uv(world_pos, world_normal, INV_VIEW_MATRIX[3].xyz, normalize(env_u_world), normalize(env_v_world));
#ifndef POINT_LIT
#ifdef LIT
		if (ms2_receive < 0.5) {
			euv = env_uv;   // no shadow map: per-vertex lighting, the uv from the vertex shader
		}
#else
		euv = env_uv;
#endif
#endif
		spec = clamp(texture(env_tex, euv).rgb, 0.0, 1.0);
		if (has_gloss) {
			spec *= clamp(texture(gloss_tex, UV).rgb, 0.0, 1.0);
		}
#ifdef POINT_LIT
		spec *= ms2_sun_specular;
#else
#ifdef LIT
		if (ms2_receive >= 0.5) {
			spec *= ms2_sun_specular;
		}
#endif
#endif
	}
	// MS2GlassMaterial::HandleMS2FinalPixelOutputs (ms2glassmaterial.cpp, 0x142774510): the opacity (the faded one too) is
	// saturate(opacity + (spec.r + spec.g + spec.b) / 3) (CalculateDiffuseOnlyAlpha), tested after that
	if (glass) {
		a = clamp(a + (spec.r + spec.g + spec.b) / 3.0, 0.0, 1.0);
	}
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
	vec3 unsun = emissive + ms2_ambient * amb + dif * ms2_point_lights(world_pos, normalize(world_normal));
	// Field meshes are MS2StandardMaterial type 0, whose light node is "MS2Light", not Gamebryo's "Light"
	// (MS2StandardMaterial::HandleMS2Light, ms2standardmaterial.cpp:852-875). MS2Light (ms2materialnodelibrary.cpp:316-328)
	// puts the light's ambient into the diffuse accumulator, not the ambient one:
	// DiffuseAccumOut += LightDiffuse * LightIntensity * Attenuate; DiffuseAccumOut += LightAmbient * (1 - LightIntensity * Shadow)
	// with LightIntensity = max(0, N.L) and Attenuate = Shadow for a directional light. Lit: sun * N.L + sun ambient * (1 - N.L);
	// fully in shadow: sun ambient alone.
	vec3 lit = unsun + dif * (ms2_sun_color * ndl + ms2_sun_ambient * (1.0 - ndl));
	vec3 c = lit * t.rgb;
	vec3 c0 = (unsun + dif * ms2_sun_ambient) * t.rgb;   // the same colour fully in the sun's shadow (Shadow = 0)
	if (has_glow) {
		vec3 g = texture(glow_tex, glow_uv).rgb;
		c += g;
		c0 += g;
	}
	// the final colour is diffuse + specular (CompositeFinalRGBColor), before the fog; the shadow does not scale it
	c += spec;
	c0 += spec;
	// Height fog (mapxblock heightfog), the engine's CalculateHeightFog fragment (NiStandardMaterialNodeLibrary, MS2 patch):
	// HeightFogOut = 1 above UpperZ, Weight * (z - LowerZ) / (UpperZ - LowerZ) + 1 - Weight between, 1 - Weight below LowerZ;
	// ApplyFog then lerps from the fog colour to the colour by it. Weight = percentage / 100 (ApplyEffect::SetFogEffect,
	// applyeffect.cpp:625-665). So the fog share is Weight * (1 - (z - LowerZ) / (UpperZ - LowerZ)), linear in height.
	// Not on additive meshes (dest blend ONE): the engine's descriptor sets HEIGHTFOG only for those that are not
	// (NiStandardMaterial.cpp:687-697). The depth fog (ms2_fog.gdshaderinc) follows, for every blend mode.
	float hf = ms2_hfog.z * (1.0 - clamp((world_pos.y - ms2_hfog.y) / (ms2_hfog.x - ms2_hfog.y), 0.0, 1.0));
#ifndef ADD
	c = mix(c, ms2_hfog_color, hf);
	c0 = mix(c0, ms2_hfog_color, hf);
#endif
#ifdef LIT
	// lit forms: everything but the sun is emission; the sun's share comes back in light(), times its shadow. Out of the
	// shadow the sum is c, the formula above; in full shadow it is c0, the sun ambient alone (MS2Light above). In between
	// Godot's shadow factor interpolates the two, as the client's Shadow term does.
	EMISSION = c0;
	sun_share = c - c0;
	ALBEDO = vec3(1.0);
	ROUGHNESS = 1.0;
#else
	ALBEDO = c;
#endif
#ifdef BLEND
	ALPHA = a;
#endif
	FOG = vec4(ms2_fog_color, fog_share);
#ifdef SHELL
	ALPHA = 0.0;
#endif
}

#ifdef LIT
// Only the sun's shadow is read here: the shader's own sun term (sun_share) is scaled by it. Field.cs makes no other
// Godot light that reaches field meshes (FieldLights draws the point and spot lights in ms2_lights.gdshaderinc).
void light() {
	if (LIGHT_IS_DIRECTIONAL) {
		DIFFUSE_LIGHT += sun_share * mix(1.0, ATTENUATION, ms2_receive);
	}
}
#endif
