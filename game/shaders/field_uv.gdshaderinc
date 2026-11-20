// The controller time and the texture transforms of the field shaders' maps (field_lit and its glow pass, field_glow).

// The controllers' time. A static entity (Bakeable IsStatic) is updated once, at the game time it is added
// (SceneGraphService.cpp:2380 SetIsDynamic(!IsStatic), :1255-1262 AddToUpdateOnce), so its controllers hold that value:
// frozen_time is that time. -1: the entity is dynamic and its controllers run on the scene time.
// STILL-OPEN: TIME wraps at its default 3600 s rollover and does not follow the game's pause or a reload's phase.
uniform float frozen_time = -1.0;

float anim_time() {
	return frozen_time >= 0.0 ? frozen_time : TIME;
}

// NiTimeController::ComputeScaledTime (NiTimeController.cpp:97-186) for a LOOP cycle: freq * t + phase, wrapped into
// start..stop as fmod(t' - start, span) + start (time = start, stop, frequency, phase); an empty span holds start.
float loop_time(vec4 time) {
	float span = time.y - time.x;
	if (span == 0.0) {
		return time.x;
	}
	return time.x + mod(time.z * anim_time() + time.w - time.x, span);
}

// NiFloatKey::GenInterpDefault / GenInterp (NiFloatKey.cpp:196-211, :60-90) over one segment: NiLinFloatKey (lerp,
// type 1) or NiBezFloatKey::Interpolate (NiInterpScalar::Bezier, NiInterpScalar.cpp:26-36, type 2) with the first key's
// OutTan dp and the second key's InTan dq.
float key_segment(int type, float u, float p, float q, float dp, float dq) {
	if (type == 2) {
		float A = 3.0 * (q - p) - (dq + 2.0 * dp);
		float B = dp + dq - 2.0 * (q - p);
		return p + (dp + (A + B * u) * u) * u;
	}
	return mix(p, q, u);
}

// Per drawn map (0 base, 1 dark, 2 glow, 3 decal0) its NiTextureTransform (NifModel TexTransform): uv_tt[2 m] =
// translate u, v, scale u, v; uv_tt[2 m + 1] = rotate, centre u, v, 0; uv_tt_method[m] = TransformMethod + 1, 0 for a
// map without one. The arrays are vec4: a std140 array element takes 16 bytes whatever its type.
uniform vec4 uv_tt[8];
uniform ivec4 uv_tt_method;
// Up to 12 NiTextureTransformControllers (NifModel UvAnim): uv_ctl_target[c] = map * 8 + operation (0 translate U,
// 1 translate V, 2 rotate, 3 scale U, 4 scale V); uv_ctl_keys[c] = key count (1-4) * 4 + key type; uv_ctl_time[c] =
// start, stop, frequency, phase; uv_ctl_t / _v / _in / _out[c][k] = key k's time, value and tangents.
// 12 is the most one texturing property has in the data (KR: the eff_com_portal_e_* portals).
const int UV_CTL_MAX = 12;
uniform int uv_ctl_count = 0;
uniform int uv_ctl_target[UV_CTL_MAX];
uniform int uv_ctl_keys[UV_CTL_MAX];
uniform vec4 uv_ctl_time[UV_CTL_MAX];
uniform vec4 uv_ctl_t[UV_CTL_MAX];
uniform vec4 uv_ctl_v[UV_CTL_MAX];
uniform vec4 uv_ctl_in[UV_CTL_MAX];
uniform vec4 uv_ctl_out[UV_CTL_MAX];

float uv_ctl_value(int c) {
	float t = loop_time(uv_ctl_time[c]);
	int n = uv_ctl_keys[c] / 4, type = uv_ctl_keys[c] % 4;
	vec4 kt = uv_ctl_t[c], kv = uv_ctl_v[c];
	if (t <= kt.x) {
		return kv.x;
	}
	float r = kv[n - 1];
	for (int i = 0; i < 3; i++) {
		if (i + 1 >= n) {
			break;
		}
		float t0 = kt[i], t1 = kt[i + 1];
		if (t <= t1) {
			r = key_segment(type, (t - t0) / max(t1 - t0, 1e-6), kv[i], kv[i + 1], uv_ctl_out[c][i], uv_ctl_in[c][i + 1]);
			break;
		}
	}
	return r;
}

// A map's uv through its transform, uv' = M (u, v, 1). Each running controller replaces one component of the static
// transform (NiTextureTransformController::UpdateValue, NiTextureTransformController.cpp:132-184), and M is built as
// NiTextureTransform::UpdateMatrix does (NiTextureTransform.cpp:110-250; NifModel TexTransform.Rows). A uv map is
// affine, so it is made per vertex.
vec2 map_uv(int m, vec2 uv) {
	int method = uv_tt_method[m] - 1;
	if (method < 0) {
		return uv;
	}
	vec4 a = uv_tt[2 * m], b = uv_tt[2 * m + 1];
	float tx = a.x, ty = a.y, sx = a.z, sy = a.w, rot = b.x, cx = b.y, cy = b.z;
	for (int c = 0; c < UV_CTL_MAX; c++) {
		if (c >= uv_ctl_count) {
			break;
		}
		if (uv_ctl_target[c] / 8 != m) {
			continue;
		}
		float v = uv_ctl_value(c);
		int op = uv_ctl_target[c] % 8;
		if (op == 0) {
			tx = v;
		} else if (op == 1) {
			ty = v;
		} else if (op == 2) {
			rot = v;
		} else if (op == 3) {
			sx = v;
		} else {
			sy = v;
		}
	}
	float co = cos(rot), si = sin(rot);
	vec3 r0, r1;
	if (method == 1) {        // MAX_TRANSFORM
		r0 = vec3(sx * co, sx * si, cx + sx * (co * (-cx - tx) + si * (-cy + ty)));
		r1 = vec3(-sy * si, sy * co, cy + sy * (-si * (-cx - tx) + co * (-cy + ty)));
	} else if (method == 2) { // MAYA_TRANSFORM
		r0 = vec3(co * sx, -si * sy, (tx - cx) * co + (-ty - cy + 1.0) * si + cx);
		r1 = vec3(-si * sx, -co * sy, (-tx + cx) * si + (-ty - cy + 1.0) * co + cy);
	} else {                  // MAYA_DEPRECATED_TRANSFORM
		r0 = vec3(co * sx, -si * sy, (tx - cx) * co + (ty - cy) * -si + cx);
		r1 = vec3(si * sx, co * sy, (tx - cx) * si + (ty - cy) * co + cy);
	}
	return vec2(dot(r0, vec3(uv, 1.0)), dot(r1, vec3(uv, 1.0)));
}
