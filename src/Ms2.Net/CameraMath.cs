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
        return ToEuler(m);
    }

    /// <summary>
    /// The camera rotation in degrees a matrix gives: Matrix3::ToEulerAnglesXYZ times the exe's -57.2957764
    /// (cameramode.cpp:326-330, :356-360). The matrix's columns are the view direction, the up vector and the right
    /// vector in MS2 axes.
    /// </summary>
    public static (float X, float Y, float Z) ToEuler(double[,] m) =>
        ToEuler(m[0, 0], m[0, 1], m[0, 2], m[1, 0], m[1, 1], m[1, 2], m[2, 2]);

    /// <summary><see cref="ToEuler(double[,])"/> of a row-major 3x3 in a flat array.</summary>
    public static (float X, float Y, float Z) ToEuler(ReadOnlySpan<double> m) => ToEuler(m[0], m[1], m[2], m[3], m[4], m[5], m[8]);

    static (float X, float Y, float Z) ToEuler(double m00, double m01, double m02, double m10, double m11, double m12, double m22)
    {
        double ax, ay, az;
        // ToEulerAnglesXYZ (Matrix3_Win32.cpp:938-971): the pole cases start within EE_RIGHT_ANGLE_EPSILON (1e-3 rad) of +-90 deg, not only at exactly +-1.
        const double eps = 1e-3;
        double y = Math.Asin(Math.Clamp(m02, -1, 1));
        if (y < Math.PI / 2 - eps)
        {
            if (y > -(Math.PI / 2 - eps)) { ax = Math.Atan2(-m12, m22); ay = y; az = Math.Atan2(-m01, m00); }
            else { ax = -Math.Atan2(m10, m11); ay = -Math.PI / 2; az = 0; }
        }
        else { ax = Math.Atan2(m10, m11); ay = Math.PI / 2; az = 0; }
        const double k = 57.2957764;
        return ((float)(ax * k), (float)(ay * k), (float)(az * k));
    }

    /// <summary>
    /// The matrix of a camera rotation in degrees: Matrix3::FromEulerAnglesXYZ of the angles times -0.0174532924
    /// (cameramode.cpp:339, :344). Gamebryo's axis rotations turn the other way, so with the angles negated this is the
    /// right-handed Rx(x) Ry(y) Rz(z); its columns are the view direction, the up vector and the right vector in MS2 axes
    /// (rotation (90, 0, 0) looks along +x with +z up). <see cref="ToEuler"/> takes it back.
    /// </summary>
    public static double[,] FromEuler(float x, float y, float z)
    {
        Span<double> f = stackalloc double[9];
        FromEuler(x, y, z, f);
        return new double[,] { { f[0], f[1], f[2] }, { f[3], f[4], f[5] }, { f[6], f[7], f[8] } };
    }

    /// <summary><see cref="FromEuler(float, float, float)"/> into a row-major 3x3 flat span (no allocation).</summary>
    public static void FromEuler(float x, float y, float z, Span<double> m)
    {
        const double k = 0.0174532924;
        double a = x * k, b = y * k, c = z * k;
        double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b), cc = Math.Cos(c), sc = Math.Sin(c);
        m[0] = cb * cc; m[1] = -cb * sc; m[2] = sb;
        m[3] = sa * sb * cc + ca * sc; m[4] = -sa * sb * sc + ca * cc; m[5] = -sa * cb;
        m[6] = -ca * sb * cc + sa * sc; m[7] = ca * sb * sc + sa * cc; m[8] = ca * cb;
    }

    /// <summary>
    /// CCameraMode::FUN_140afdec0 (@0x140afdec0, cameramode.cpp:335-363): the rotation a fraction t of the way from a to b.
    /// The view directions and the up vectors of the two matrices are each moved linearly and normalised; right = dir x up,
    /// up = right x dir, both normalised, and the new matrix (dir, up, right) goes back to degrees.
    /// </summary>
    public static (float X, float Y, float Z) RotationLerp(float t, (float X, float Y, float Z) a, (float X, float Y, float Z) b)
    {
        var ma = FromEuler(a.X, a.Y, a.Z);
        var mb = FromEuler(b.X, b.Y, b.Z);
        var c0 = new double[3];
        var c1 = new double[3];
        for (int i = 0; i < 3; i++)
        {
            c0[i] = ma[i, 0] + t * (mb[i, 0] - ma[i, 0]);
            c1[i] = ma[i, 1] + t * (mb[i, 1] - ma[i, 1]);
        }
        Unitize(c0);
        Unitize(c1);
        var c2 = Cross(c0, c1);
        Unitize(c2);
        c1 = Cross(c2, c0);
        Unitize(c1);
        return ToEuler(new double[,] { { c0[0], c1[0], c2[0] }, { c0[1], c1[1], c2[1] }, { c0[2], c1[2], c2[2] } });
    }

    static double[] Cross(double[] a, double[] b) =>
        new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

    /// <summary>NiPoint3::Unitize: a vector shorter than 1e-6 becomes zero.</summary>
    static void Unitize(double[] v)
    {
        double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (n > 1e-6) { v[0] /= n; v[1] /= n; v[2] /= n; }
        else { v[0] = v[1] = v[2] = 0; }
    }
}
