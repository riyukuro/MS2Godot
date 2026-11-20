// NiBillboardNode for the field shaders (field_lit and its glow pass, field_glow): a mesh under a billboard node turns
// with it to the camera of the pass that draws it. NiBillboardNode::OnVisible runs RotateToCamera on every cull
// (NiBillboardNode.cpp:214-218), so a static entity's billboard turns too.

// The nearest NiBillboardNode above the mesh (NifModel ModelMesh.Billboard): its FaceMode (NiBillboardNode.h:34-41,
// 0 ALWAYS_FACE_CAMERA .. 4 RIGID_FACE_CENTER), -1 for a mesh under none; bb_x takes the mesh's space to the billboard
// node's, bb_x_inv back.
uniform int bb_mode = -1;
uniform mat4 bb_x = mat4(1.0);
uniform mat4 bb_x_inv = mat4(1.0);

// NiBillboardNode::RotateToCenter (NiBillboardNode.cpp:28-55): the camera axes turned by the least rotation that takes
// its back direction d onto the unit direction at, from the billboard to the camera (Rodrigues for unit vectors).
vec3 bb_turn(vec3 d, vec3 at, vec3 v) {
	vec3 k = cross(d, at);
	float c = dot(d, at);
	return v * c + cross(k, v) + k * (dot(k, v) / (1.0 + c));
}

// NiBillboardNode::RotateToCamera (NiBillboardNode.cpp:67-200) as a matrix on the mesh's own space: the billboard's
// face matrix, in the billboard node's space, between bb_x and bb_x_inv. model is the mesh's MODEL_MATRIX; inv_view
// is the camera the billboard faces (field_lit passes MAIN_CAM_INV_VIEW_MATRIX, so the shadow pass keeps the eye's
// face; field_glow its own pass's INV_VIEW_MATRIX): its back direction is column 2 (Gamebryo's
// -GetWorldDirection), up column 1, right column 0.
mat4 bb_matrix(mat4 model, mat4 inv_view) {
	mat4 b = model * bb_x_inv;     // the billboard node's world transform before facing (m_kWorld)
	float s = length(b[0].xyz);
	mat3 r = mat3(b[0].xyz / s, b[1].xyz / s, b[2].xyz / s);
	mat3 rt = transpose(r);        // a point times m_kRotate: world into the node's frame
	vec3 cam = inv_view[3].xyz;
	vec3 d = normalize(inv_view[2].xyz), u = normalize(inv_view[1].xyz), rr = normalize(inv_view[0].xyz);
	mat3 face = mat3(1.0);
	if (bb_mode == 1) {
		// ROTATE_ABOUT_UP: about the model up (Y), toward the camera's position
		vec3 loc = rt * (cam - b[3].xyz) / s;
		float len = sqrt(loc.x * loc.x + loc.z * loc.z);
		if (len >= 1e-12) {
			loc /= len;
			face = mat3(vec3(loc.z, 0.0, -loc.x), vec3(0.0, 1.0, 0.0), vec3(loc.x, 0.0, loc.z));
		}
	} else {
		bool ok = true;
		if (bb_mode >= 3) {
			// ALWAYS_FACE_CENTER, RIGID_FACE_CENTER: the camera axes turned to look straight at the billboard
			vec3 at = cam - b[3].xyz;
			if (dot(at, at) < 0.001) {
				ok = false;
			} else {
				at = normalize(at);
				if (dot(at, d) < 0.999999) {
					u = bb_turn(d, at, u);
					rr = bb_turn(d, at, rr);
					d = at;
				}
			}
		}
		if (ok) {
			d = rt * d;
			u = rt * u;
			rr = rt * rr;
			if (bb_mode == 2 || bb_mode == 4) {
				// RIGID_*: the camera's axes as they are
				face = mat3(rr, u, d);
			} else {
				// ALWAYS_*: the up in the plane facing the camera nearest the model up (0, 1, 0)
				float root = sqrt(rr.y * rr.y + u.y * u.y);
				if (root > 1e-6) {
					float co = u.y / root, si = -rr.y / root;
					face = mat3(co * rr + si * u, -si * rr + co * u, d);
				} else {
					face = mat3(-rr, -u, d);
				}
			}
		}
	}
	return bb_x_inv * mat4(face) * bb_x;
}
