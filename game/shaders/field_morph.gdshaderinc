// NiMorphMeshModifier driven by an NiMorphWeightsController for the field shaders (field_lit and its glow pass,
// field_glow). The modifier writes POSITION = sum of weight i * target i over the MORPH_POSITION targets
// (NiMorphingKernel.cpp:30-40, weights under 0.001 skipped); with relative targets (FLAG_RELATIVETARGETS) target 0's
// weight is held at 1 (NiMorphWeightsController::Update, NiMorphWeightsController.cpp) and the others are offsets
// from it. The mesh's VERTEX is target 0 and targets 1-3 come in CUSTOM1-3 (NifModel MorphAnim), so only relative
// morphs reach here. Needs field_uv (anim_time, key_segment).

// The targets after the first (1-3, 0 = no morph); the controller's start, stop, frequency and phase; its CLAMP cycle
// (else LOOP, NiTimeController::ComputeScaledTime); per target 1-3 the key count * 4 + key type (1 linear, 2 Bezier),
// 0 for a target whose weight stays at morph_static; morph_key[32 (i - 1) + k] = target i's key k: time, value, InTan,
// OutTan.
uniform int morph_count = 0;
uniform vec4 morph_time = vec4(0.0, 1.0, 1.0, 0.0);
uniform bool morph_clamp = false;
uniform vec3 morph_static = vec3(0.0);
uniform ivec3 morph_keys = ivec3(0);
uniform vec4 morph_key[96];

// NiTimeController::ComputeScaledTime (NiTimeController.cpp:97-186): LOOP as field_uv's loop_time, CLAMP held in
// start..stop.
float morph_scaled_time() {
	if (morph_clamp) {
		return clamp(morph_time.z * anim_time() + morph_time.w, morph_time.x, morph_time.y);
	}
	return loop_time(morph_time);
}

// Target i's weight (1-3): its NiFloatInterpolator's keys at time t, the end values outside them (NiFloatKey::GenInterp).
float morph_weight(int i, float t) {
	int nk = morph_keys[i - 1] / 4, type = morph_keys[i - 1] % 4, b = 32 * (i - 1);
	if (nk == 0) {
		return morph_static[i - 1];
	}
	if (t <= morph_key[b].x) {
		return morph_key[b].y;
	}
	float r = morph_key[b + nk - 1].y;
	for (int k = 0; k < 31; k++) {
		if (k + 1 >= nk) {
			break;
		}
		vec4 p = morph_key[b + k], q = morph_key[b + k + 1];
		if (t <= q.x) {
			r = key_segment(type, (t - p.x) / max(q.x - p.x, 1e-6), p.y, q.y, p.w, q.z);
			break;
		}
	}
	return r;
}

// The morphed vertex: target 0 plus each weighted offset.
vec3 morph_vertex(vec3 v, vec3 d1, vec3 d2, vec3 d3) {
	float t = morph_scaled_time();
	v += morph_weight(1, t) * d1;
	if (morph_count >= 2) {
		v += morph_weight(2, t) * d2;
	}
	if (morph_count >= 3) {
		v += morph_weight(3, t) * d3;
	}
	return v;
}
