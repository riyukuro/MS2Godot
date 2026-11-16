// The field's point and spot lights as the client's fixed-function lighting computes them: per light
// colour * dimmer * N.L / (constant + linear d + quadratic d^2), zero beyond range, and for spots a cone term
// (1 inside the inner angle, ((cos a - cos outer) / (cos inner - cos outer))^exponent between the two, 0 outside).
// Light data comes from FieldLights as a float texture, one column per light, five rows:
// 0 position + range, 1 colour * dimmer + type (0 point, 1 spot), 2 direction + cos outer,
// 3 constant, linear, quadratic + cos inner, 4 spot exponent.
global uniform sampler2D ms2_lights : filter_nearest, repeat_disable;
global uniform float ms2_light_count;

vec3 ms2_point_lights(vec3 p, vec3 n) {
	vec3 sum = vec3(0.0);
	for (int i = 0; i < int(ms2_light_count); i++) {
		vec4 r0 = texelFetch(ms2_lights, ivec2(i, 0), 0);
		vec3 to = r0.xyz - p;
		float d = length(to);
		if (d > r0.w) {
			continue;
		}
		vec3 l = to / max(d, 1e-4);
		float ndl = max(dot(n, l), 0.0);
		if (ndl <= 0.0) {
			continue;
		}
		vec4 r1 = texelFetch(ms2_lights, ivec2(i, 1), 0);
		vec4 r3 = texelFetch(ms2_lights, ivec2(i, 3), 0);
		float att = 1.0 / max(r3.x + r3.y * d + r3.z * d * d, 1e-4);
		if (r1.w > 0.5) {
			vec4 r2 = texelFetch(ms2_lights, ivec2(i, 2), 0);
			float ca = dot(-l, r2.xyz);
			if (ca <= r2.w) {
				continue;
			}
			float exponent = texelFetch(ms2_lights, ivec2(i, 4), 0).x;
			att *= ca >= r3.w ? 1.0 : pow((ca - r2.w) / max(r3.w - r2.w, 1e-5), exponent);
		}
		sum += r1.rgb * (att * ndl);
	}
	return sum;
}
