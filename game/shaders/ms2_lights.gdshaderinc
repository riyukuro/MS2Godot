// The field's point and spot lights, as the client's light fragments evaluate each one per pixel: MS2Light for field
// meshes (ms2materialnodelibrary.cpp:23-358); for characters MS2CharacterLight (:1084-1440), MS2CharacterSkinLight (:2084)
// with LambSkin (:3244-3446) and MS2CharacterHairLight (:2853), in character_lit.gdshader. With the engine's constants (NiD3DShaderConstantManager, NiD3DShaderConstantMap.cpp:3187-3530):
//   LightDiffuse / LightAmbient / LightSpecular = the light's colour * dimmer
//   LightAttenuation = (constant, linear, quadratic); "Range is not implemented": the range only picks the receivers
//   LightSpotAttenuation = (cos inner, cos outer, exponent), the angles being half angles in degrees (NiSpotLight.inl:23-48)
//   Attenuate = 1 / max(1, C + L d + Q d^2); a spot multiplies saturate((cos a - cos outer) / (cos inner - cos outer))
//   raised to the exponent (0 where that is 0), a = the angle between the light's direction and the pixel.
// No shadow map from these lights (FieldLights), so the fragments' Shadow input keeps its default 1.0
// (ms2materialnodelibrary.cpp:88-93).
// Light data comes from FieldLights as a float texture, one column per light, six rows:
//   0 position.xyz (Godot space), flags (1 spot, 2 the ambient colour is not black)   1 diffuse * dimmer, constant
//   2 ambient * dimmer, linear                              3 specular * dimmer, quadratic
//   4 direction.xyz, cos outer                              5 cos inner, exponent
global uniform sampler2D ms2_lights : filter_nearest, repeat_disable;
global uniform float ms2_light_count;

// The Attenuate of light i (row 0 r0) at distance d along the unit vector l towards it.
float ms2_light_atten(int i, vec4 r0, vec3 l, float d) {
	float att = 1.0 / max(1.0, texelFetch(ms2_lights, ivec2(i, 1), 0).w + texelFetch(ms2_lights, ivec2(i, 2), 0).w * d
		+ texelFetch(ms2_lights, ivec2(i, 3), 0).w * d * d);
	if ((int(r0.w) & 1) != 0) {
		vec4 r4 = texelFetch(ms2_lights, ivec2(i, 4), 0);
		vec4 r5 = texelFetch(ms2_lights, ivec2(i, 5), 0);
		float ca = dot(-l, r4.xyz);
		float diff = r5.x - r4.w;
		// HLSL's saturate of x / 0 is 1 for x > 0 and 0 below
		float t = diff != 0.0 ? clamp((ca - r4.w) / diff, 0.0, 1.0) : (ca > r4.w ? 1.0 : 0.0);
		att *= t <= 0.0 ? 0.0 : pow(t, r5.y);
	}
	return att;
}

// Light i at world position p: the unit vector towards the light (l) and its Attenuate.
float ms2_light_at(int i, vec3 p, out vec3 l) {
	vec4 r0 = texelFetch(ms2_lights, ivec2(i, 0), 0);
	vec3 to = r0.xyz - p;
	float d = length(to);
	l = to / max(d, 1e-6);
	return ms2_light_atten(i, r0, l, d);
}

vec3 ms2_light_diffuse(int i) { return texelFetch(ms2_lights, ivec2(i, 1), 0).rgb; }
vec3 ms2_light_ambient(int i) { return texelFetch(ms2_lights, ivec2(i, 2), 0).rgb; }
vec3 ms2_light_specular(int i) { return texelFetch(ms2_lights, ivec2(i, 3), 0).rgb; }

// The lights' share of a field mesh's diffuse accumulator (MS2Light): per light not in the mask off,
// LightDiffuse * max(0, N.L) * Attenuate + LightAmbient * (1 - max(0, N.L)). The ambient part is not attenuated.
vec3 ms2_point_lights(vec3 p, vec3 n, int off) {
	vec3 sum = vec3(0.0);
	int count = int(ms2_light_count);
	for (int i = 0; i < count; i++) {
		if (((off >> i) & 1) != 0) {
			continue;
		}
		vec4 r0 = texelFetch(ms2_lights, ivec2(i, 0), 0);
		vec3 to = r0.xyz - p;
		float d = length(to);
		vec3 l = to / max(d, 1e-6);
		float ndl = max(dot(l, n), 0.0);
		bool amb = (int(r0.w) & 2) != 0;
		if (ndl <= 0.0 && !amb) {
			continue;   // adds nothing: N.L is 0 and the ambient colour black
		}
		sum += ms2_light_diffuse(i) * (ndl * ms2_light_atten(i, r0, l, d));
		if (amb) {
			sum += ms2_light_ambient(i) * (1.0 - ndl);
		}
	}
	return sum;
}
