// The client's glow pass (MS2GlowMaterial, ms2glowmaterial.cpp): glow-mapped objects are drawn into their own target with
// rgb = the glow map's alpha (a grey intensity), times the detail map's alpha when the object has a detail map
// (HandleGlowColor @0x1427761b0). Its opacity is the colour pass's (MS2GlowMaterial::GeneratePixelShadeTree,
// ms2glowmaterial.cpp:30-108: HandlePixelMaterialInitialValues gives the material alpha, or the vertex alpha where the vertex
// colour is the term the opacity is split from; HandleBaseMap multiplies the base alpha in; HandleMS2FinalPixelOutputs then
// HandleAlphaTest test it), and it blends with that opacity when the mesh blends (BLEND: the swap keeps the NiAlphaProperty). Each map reads its own uv set
// (FindUVSetIndexForTextureEnum). The bloom's bright pass adds that target.
// MS2GlowMaterial_ps_3_0_DX9.cache: @0x8c (glow only) rgb = lerp(gkHeightFogColor, Glow.a, height fog factor);
// @0x327 (detail) rgb = lerp(gkHeightFogColor, Detail.a * Glow.a, height fog factor). The depth fog is off (GenerateShaderDescArray
// @0x142776070 clears FOGENABLED); the height fog stays.
// Here: unshaded, drawn by the glow camera only (layer 20) into GlowPass's linear HDR target, which keeps the value as written.
// The glow click swaps the material only (MS2MaterialSwapProcessor, postprocessingservice.cpp:2830-2836), so the mesh's
// NiStencilProperty draw mode culls as in the colour pass: ModelBuilder.GlowMaterial adds field_lit's cull define.
shader_type spatial;
#if defined(CULL_NONE)
#define MS2_CULL cull_disabled
#elif defined(CULL_FRONT)
#define MS2_CULL cull_front
#else
#define MS2_CULL cull_back
#endif
#ifdef BLEND
#ifdef ZWRITE
render_mode unshaded, MS2_CULL, blend_mix, depth_draw_always;
#else
render_mode unshaded, MS2_CULL, blend_mix, depth_draw_never;
#endif
#else
render_mode unshaded, MS2_CULL;
#endif

global uniform vec3 ms2_hfog_color; // height fog colour (gamma space), as field_lit
global uniform vec3 ms2_hfog;       // upper height, lower height, density at and below the lower height (0 = none)

uniform sampler2D base_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D glow_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_tex = false;
uniform bool has_detail = false;
// a chaos light layer is on: its glow map is the layer manager's 1x1 white texture (ChaosLightEffectLayer::vfunc_19,
// chaoslighttransitiondata.cpp:289-303; the pixel, :313-320), so the glow is 1, times the detail alpha
uniform bool chaos_glow = false;
// the map reads the mesh's second uv set (TexMap.UvSet 1)
uniform bool base_uv2 = false;
uniform bool glow_uv2 = false;
uniform bool detail_uv2 = false;
uniform bool alpha_test = false;
uniform float alpha_ref = 0.5;
// clip above / below / equal to alpha_ref, as field_lit (SCM_DEF_ALPHA_TEST_FUNC, D3D11ShaderConstantMap.cpp:3630-3687)
uniform vec3 alpha_clip = vec3(0.0, 1.0, 0.0);
#include "res://shaders/field_uv.gdshaderinc"
#include "res://shaders/field_billboard.gdshaderinc"
#include "res://shaders/field_morph.gdshaderinc"
uniform float mat_alpha = 1.0;   // the NiMaterialProperty alpha (1 without a material)
#define ALPHA_ANIM_TIME anim_time()
#include "res://shaders/ms2_alpha_anim.gdshaderinc"
// where the vertex alpha comes from: 0 none, 1 CUSTOM0 (field meshes), 2 COLOR (character meshes); vc_opacity: it is the opacity
uniform int vc_source = 0;
uniform bool vc_opacity = false;

varying vec2 base_uv;
varying vec2 glow_uv;
varying vec3 world_pos;
varying float valpha;

void vertex() {
	if (morph_count > 0) {
		VERTEX = morph_vertex(VERTEX, CUSTOM1.xyz, CUSTOM2.xyz, CUSTOM3.xyz);
	}
	if (bb_mode >= 0) {
		VERTEX = (bb_matrix(MODEL_MATRIX, INV_VIEW_MATRIX) * vec4(VERTEX, 1.0)).xyz;
	}
	base_uv = map_uv(0, base_uv2 ? UV2 : UV);
	glow_uv = map_uv(2, glow_uv2 ? UV2 : UV);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	valpha = vc_source == 1 ? CUSTOM0.a : vc_source == 2 ? COLOR.a : 1.0;
}

void fragment() {
	float a = (has_tex ? texture(base_tex, base_uv).a : 1.0)
		* (vc_opacity && vc_source != 0 ? valpha : (alpha_anim_count > 0 ? alpha_anim() : mat_alpha));
	if (alpha_test && (alpha_clip.x > 0.5 && a > alpha_ref || alpha_clip.y > 0.5 && a < alpha_ref || alpha_clip.z > 0.5 && a == alpha_ref)) {
		discard;
	}
	float g = chaos_glow ? 1.0 : texture(glow_tex, glow_uv).a;
	if (has_detail) {
		g *= texture(detail_tex, detail_uv2 ? UV2 : UV).a;
	}
	// the height fog share as in field_lit (CalculateHeightFog), lerped towards the fog colour
	float hf = ms2_hfog.z * (1.0 - clamp((world_pos.y - ms2_hfog.y) / (ms2_hfog.x - ms2_hfog.y), 0.0, 1.0));
	ALBEDO = mix(vec3(g), ms2_hfog_color, hf);
#ifdef BLEND
	ALPHA = a;
#endif
}
