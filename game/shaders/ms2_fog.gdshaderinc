// The client's depth fog, as MS2StandardMaterial computes it (and so every material of that family: field meshes,
// characters). MS2StandardMaterial::HandleFinalVertexOutputs (ms2standardmaterial.cpp:796-808) runs the
// "CalculateBiasedFog" node (ms2materialnodelibrary.cpp:957-1075) in the vertex shader:
//   d = FogRange ? length(view position) : view position z
//   LINEAR: FogOut = saturate((FogStartEnd.y + FogBias - d) / (FogStartEnd.y - FogStartEnd.x))
// The client's fog type is FOG_Z_LINEAR -> FOG_LINEAR, FogRange false: d is the view-space depth, not the distance.
// FogStartEnd is not the mapxblock fog's (near, far) as such: ApplyEffect::SetFogRange (applyeffect.cpp:586-622) stores
// the fog property's depth and the render state's max fog value for the camera's planes, SetMaxFogValue
// (NiDX9RenderState.inl:115-122) raises a value at or below 0 to 1e-5, and SCM_DEF_FOG_NEARFAR
// (NiD3DShaderConstantMap.cpp:2379-2401) turns them back into a near and far. ms2_fog holds that pair
// (CameraFrustum.ShaderFogRange): the block's own range when its far is above its near, near to the camera's far when they
// are equal (52000104_qd and three more), and a far end past 1e9 when near is above far (63000041_cs: next to no fog).
// FogBias is the global CameraMgr::Update sets every frame from the camera
// mode (camera.cpp:828-829, CCameraMode::vfunc_19 = m_50, the camera distance; no mode overrides it).
// The pixel shader then lerps to the fog colour by it (NiStandardMaterial::HandleApplyFog, after the height fog and
// before the blend, for every blend mode). So the fogged share is 1 - saturate((far + bias - z) / (far - near)), computed
// per vertex and interpolated as the client's is: saturate((z - near - bias) / (far - near)). Only a near at or past the
// camera's far gives equal ends (both the camera's far), a step at far + bias (the 0/0 at the step is taken as no fog).
// It replaces Godot's environment fog (smoothstep over the distance,
// no bias) for the material: writing FOG turns that off.
// Only cameras that carry FOG_LAYER in their cull mask fog (the field camera); the UI character views share
// character_lit and draw no fog.
global uniform vec4 ms2_fog;        // the shaders' near, far (CameraFrustum.ShaderFogRange), bias (the camera distance), off (1: SetFogRange(false), no depth fog)
global uniform vec3 ms2_fog_color;  // gamma value

const uint FOG_LAYER = 1048576u;    // 1 << 20, Field.FogLayer

varying float fog_share;

float ms2_fog_share(mat4 view, vec3 world, uint layers) {
	if ((layers & FOG_LAYER) == 0u || ms2_fog.w != 0.0) {
		return 0.0;
	}
	float z = -(view * vec4(world, 1.0)).z;
	float range = ms2_fog.y - ms2_fog.x;
	float left = ms2_fog.y + ms2_fog.z - z;
	if (range == 0.0) {
		return left >= 0.0 ? 0.0 : 1.0;
	}
	return 1.0 - clamp(left / range, 0.0, 1.0);
}
