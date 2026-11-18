namespace Ms2.Net;

/// <summary>CCameraMode::FUN_140afdd40 (cameramode.cpp:316-331): the camera mode's Euler rotation in degrees, which the sync record carries as its camera field
/// (m_setting.m_18 = m_posBF4). The view direction is (at - eye) in MS2 axes (z up), the up vector is NiPoint3::UNIT_Z.
/// right = dir x up (normalised), up' = right x dir; the three vectors are the matrix's columns (NiMatrix3(dir, up, right)); ToEulerAnglesXYZ (Wild Magic / Gamebryo), converted to degrees with the exe's -57.2957764 (the angles come out negated, so +57.2957764 here). Checked against kms2-sniffs: a camera with hangle 0 gives (vangle + ~7, 90, 0) and a default camera (hangle 45, vangle 30) gives (90, 45, -40.89), which the int16 field truncates to the captured -40.</summary>
public static class CameraMath
{
    public static (float X, float Y, float Z) Euler(double eyeX, double eyeY, double eyeZ, double atX, double atY, double atZ)
    {
        double dx = atX - eyeX, dy = atY - eyeY, dz = atZ - eyeZ;
        double n = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (n < 1e-9) return (0, 0, 0);
        dx /= n; dy /= n; dz /= n;
        // right = dir x (0, 0, 1)
        double rx = dy, ry = -dx, rz = 0;
        n = Math.Sqrt(rx * rx + ry * ry);
        if (n < 1e-9) return (0, 0, 0);          // looking straight along the up axis
        rx /= n; ry /= n;
        double ux = ry * dz - rz * dy, uy = rz * dx - rx * dz, uz = rx * dy - ry * dx;   // right x dir
        double[,] m = { { dx, ux, rx }, { dy, uy, ry }, { dz, uz, rz } };   // NiMatrix3(dir, up, right) takes columns
        double ax, ay, az;
        if (m[0, 2] < 1)
        {
            if (m[0, 2] > -1) { ax = Math.Atan2(-m[1, 2], m[2, 2]); ay = Math.Asin(m[0, 2]); az = Math.Atan2(-m[0, 1], m[0, 0]); }
            else { ax = -Math.Atan2(-m[1, 0], m[1, 1]); ay = -Math.PI / 2; az = 0; }
        }
        else { ax = Math.Atan2(m[1, 0], m[1, 1]); ay = Math.PI / 2; az = 0; }
        const double k = 57.2957764;
        return ((float)(ax * k), (float)(ay * k), (float)(az * k));
    }
}
