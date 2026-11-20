using System.Numerics;

namespace Ms2.Formats.Model;

/// <summary>
/// NiBillboardNode's turn toward the camera: NiBillboardNode::RotateToCamera (NiBillboardNode.cpp:66-205), run from OnVisible
/// (:212-216) for every draw. The node's world rotation is post-multiplied by the face matrix (m_kWorld.m_Rotate * kFaceMat,
/// :199), so the matrix is a rotation in the node's own frame, applied before its children.
/// The mode is the file's flags field masked with FACE_MODE_MASK 0x0007 (NiBillboardNode.h:81-83, GetMode in
/// NiBillboardNode.inl; LoadBinary streams m_uFlags, NiBillboardNode.cpp:288-296); the constructor's default is
/// ROTATE_ABOUT_UP (NiBillboardNode.inl:17-21).
/// All vectors are in the node's world frame without its scale (a world vector times m_kWorld.m_Rotate, Gamebryo's row-vector
/// product): the camera position relative to the node's world translation, the camera's world direction, up and right.
/// Floats, as the engine's NiPoint3.
/// </summary>
public static class BillboardFace
{
    public const int AlwaysFaceCamera = 0, RotateAboutUp = 1, RigidFaceCamera = 2, AlwaysFaceCenter = 3, RigidFaceCenter = 4;
    public const int FaceModeMask = 0x0007;

    /// <summary>The face mode of an NiBillboardNode's flags field (GetMode).</summary>
    public static int ModeOf(long flags) => (int)(flags & FaceModeMask);

    /// <summary>The face matrix by columns. Identity where the engine breaks out of its switch (a camera at the node, a mode
    /// past RIGID_FACE_CENTER, which only asserts).</summary>
    /// <param name="scale">The node's world scale (m_kWorld.m_fScale): ROTATE_ABOUT_UP divides the camera position by it.</param>
    public static (Vector3 c0, Vector3 c1, Vector3 c2) Face(int mode, Vector3 camLoc, Vector3 camDir, Vector3 camUp, Vector3 camRight, float scale)
    {
        var id = (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        switch (mode)
        {
            case AlwaysFaceCenter:
            case AlwaysFaceCamera:
            {
                Vector3 d, u, r;
                if (mode == AlwaysFaceCenter) { if (!ToCenter(camLoc, camDir, camUp, camRight, out d, out u, out r)) return id; }
                else { d = -camDir; u = camUp; r = camRight; }
                // the rotated model up is the vector in the plane orthogonal to the camera direction nearest the model up (0,1,0)
                float root = MathF.Sqrt(r.Y * r.Y + u.Y * u.Y);
                if (root > 1e-06f)
                {
                    float inv = 1.0f / root, cos = u.Y * inv, sin = -r.Y * inv;
                    return (cos * r + sin * u, -sin * r + cos * u, d);
                }
                return (-r, -u, d);
            }
            case RotateAboutUp:
            {
                var c = camLoc / scale;
                float len = MathF.Sqrt(c.X * c.X + c.Z * c.Z);
                if (len < 1e-12f) return id;
                len = 1.0f / len;
                float x = c.X * len, z = c.Z * len;
                return (new Vector3(z, 0, -x), new Vector3(0, 1, 0), new Vector3(x, 0, z));
            }
            case RigidFaceCamera:
            case RigidFaceCenter:
            {
                Vector3 d, u, r;
                if (mode == RigidFaceCenter) { if (!ToCenter(camLoc, camDir, camUp, camRight, out d, out u, out r)) return id; }
                else { d = -camDir; u = camUp; r = camRight; }
                return (r, u, d);
            }
            default:
                return id;
        }
    }

    /// <summary>NiBillboardNode::RotateToCenter (NiBillboardNode.cpp:28-55): the camera axes turned so the direction looks straight
    /// at the node. The engine rotates by the angle between them about their cross product and sets D to the unit vector to the
    /// camera ("equivalent to kRot * kCamD"), so the turn is the one taking -direction onto that vector.</summary>
    static bool ToCenter(Vector3 camLoc, Vector3 camDir, Vector3 camUp, Vector3 camRight, out Vector3 d, out Vector3 u, out Vector3 r)
    {
        d = -camDir; u = camUp; r = camRight;
        if (camLoc.LengthSquared() < 0.001f) return false;
        var at = Vector3.Normalize(camLoc);
        float dot = Vector3.Dot(at, d);
        if (dot < 0.999999f)
        {
            var axis = Vector3.Cross(d, at);
            if (axis.LengthSquared() > 0)
            {
                var q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(dot, -1f, 1f)));
                u = Vector3.Transform(u, q);
                r = Vector3.Transform(r, q);
            }
            d = at;
        }
        return true;
    }
}
