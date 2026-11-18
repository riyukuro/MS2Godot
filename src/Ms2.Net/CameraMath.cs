namespace Ms2.Net;

/// <summary>CCameraMode::FUN_140afdd40 (cameramode.cpp:316-331): the camera mode's Euler rotation in degrees, which the sync record carries as its camera field
/// (m_setting.m_18 = m_posBF4). The view direction is (at - eye) in MS2 axes (z up), the up vector is NiPoint3::UNIT_Z.
/// right = dir x up (normalised), up' = right x dir; the three vectors are the matrix's rows (this order gives the sniffs' (90, 45, -40) for a 45 degree elevation at eye yaw 310; the
/// column order gives other values, e.g. (-90, ...) for the same view, which the captures never show); ToEulerAnglesXYZ (Wild Magic / Gamebryo) is negated and converted to degrees.</summary>
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
        double[,] m = { { dx, dy, dz }, { ux, uy, uz }, { rx, ry, rz } };
        double ax, ay, az;
        if (m[0, 2] < 1)
        {
            if (m[0, 2] > -1) { ax = Math.Atan2(-m[1, 2], m[2, 2]); ay = Math.Asin(m[0, 2]); az = Math.Atan2(-m[0, 1], m[0, 0]); }
            else { ax = -Math.Atan2(-m[1, 0], m[1, 1]); ay = -Math.PI / 2; az = 0; }
        }
        else { ax = Math.Atan2(m[1, 0], m[1, 1]); ay = Math.PI / 2; az = 0; }
        const double k = -57.2957764;
        return ((float)(ax * k), (float)(ay * k), (float)(az * k));
    }
}
