// The NiMaterialProperty alpha an NiAlphaController drives (field_lit, character_lit, field_glow; ModelBuilder.SetAlphaAnim).
// An NiAlphaController on the NiMaterialProperty (NifModel.ReadAlphaController): up to 8 keys, linear (type 1) or
// Bezier (type 2); alpha_anim_time = start, stop, frequency, phase. alpha_anim_count 0 keeps mat_alpha.
// ALPHA_ANIM_TIME is the controller time: the field shaders define it as field_uv's anim_time() before the include, so a
// static entity's alpha holds at its frozen_time as its uv controllers do; the character shader runs on the scene time.
#ifndef ALPHA_ANIM_TIME
#define ALPHA_ANIM_TIME TIME
#endif
uniform int alpha_anim_count = 0;
uniform int alpha_anim_type = 1;
uniform vec4 alpha_anim_time = vec4(0.0, 1.0, 1.0, 0.0);
uniform float alpha_anim_t[8];
uniform float alpha_anim_v[8];
uniform float alpha_anim_in[8];
uniform float alpha_anim_out[8];
// NiTimeController::ComputeScaledTime (NiTimeController.cpp:97-186) for a LOOP cycle, as field_uv's loop_time: freq * t +
// phase, wrapped into start..stop as fmod(t' - start, span) + start; an empty span holds start. Then the end values
// outside the keys, else the segment's normalized time into NiLinFloatKey (lerp) or NiBezFloatKey::Interpolate
// (NiInterpScalar::Bezier, NiInterpScalar.cpp:26-36, with key 0's OutTan and key 1's InTan), as field_uv's key_segment.
float alpha_anim() {
	float span = alpha_anim_time.y - alpha_anim_time.x;
	float t = span == 0.0 ? alpha_anim_time.x
		: alpha_anim_time.x + mod(alpha_anim_time.z * ALPHA_ANIM_TIME + alpha_anim_time.w - alpha_anim_time.x, span);
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
			float u = (t - t0) / max(t1 - t0, 1e-6);
			float p = alpha_anim_v[i], q = alpha_anim_v[i + 1];
			if (alpha_anim_type == 2) {
				float dp = alpha_anim_out[i], dq = alpha_anim_in[i + 1];
				float A = 3.0 * (q - p) - (dq + 2.0 * dp);
				float B = dp + dq - 2.0 * (q - p);
				r = p + (dp + (A + B * u) * u) * u;
			} else {
				r = mix(p, q, u);
			}
			break;
		}
	}
	return r;
}
