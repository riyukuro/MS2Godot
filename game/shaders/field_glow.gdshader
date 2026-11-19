// The client's glow pass (MS2GlowMaterial, ms2glowmaterial.cpp): glow-mapped objects are drawn into their own target with
// rgb = the glow map's alpha (a grey intensity), times the detail map's alpha when the object has a detail map
// (HandleGlowColor @0x1427761b0), and alpha = base alpha * vertex alpha. Each map reads its own uv set
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
render_mode unshaded, MS2_CULL;

global uniform vec3 ms2_hfog_color; // height fog colour (gamma space), as field_lit
global uniform vec3 ms2_hfog;       // upper height, lower height, density at and below the lower height (0 = none)

uniform sampler2D base_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D glow_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_tex = false;
uniform bool has_detail = false;
// the map reads the mesh's second uv set (TexMap.UvSet 1)
uniform bool base_uv2 = false;
uniform bool glow_uv2 = false;
uniform bool detail_uv2 = false;
uniform bool alpha_test = false;
uniform float alpha_ref = 0.5;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float a = has_tex ? texture(base_tex, base_uv2 ? UV2 : UV).a : 1.0;
	if (alpha_test && a < alpha_ref) {
		discard;
	}
	float g = texture(glow_tex, glow_uv2 ? UV2 : UV).a;
	if (has_detail) {
		g *= texture(detail_tex, detail_uv2 ? UV2 : UV).a;
	}
	// the height fog share as in field_lit (CalculateHeightFog), lerped towards the fog colour
	float hf = ms2_hfog.z * (1.0 - clamp((world_pos.y - ms2_hfog.y) / (ms2_hfog.x - ms2_hfog.y), 0.0, 1.0));
	ALBEDO = mix(vec3(g), ms2_hfog_color, hf);
}
