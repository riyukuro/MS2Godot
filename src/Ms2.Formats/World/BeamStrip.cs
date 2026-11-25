using System.Numerics;

namespace Ms2.Formats.World;

/// <summary>
/// The strip of a beam part (CBeamEffect, beameffect.cpp; type 0x15), without the engine: at each update (<see cref="Rebuild"/>)
/// a Catmull-Rom curve from the first to the last point of the target's point list, bent through a third control point from
/// the param's beamInfo, and its two edge rows. All points are world points, client space (the root hangs on the effect
/// service's least loaded world root, vfunc_4 @0x140a07f80). The rows are made once at the param's size
/// (<see cref="Samples"/>), so a rebuild allocates nothing.
/// </summary>
public sealed class BeamStrip
{
    /// <summary>The client's mesh holds 8000 vertices (vfunc_3 @0x140a07320: CMesh(.., 0x1f42, 8000, 0x15)): two rows of
    /// 2 * quality, so a quality past 2000 overflows its locks; here such a beam draws nothing.</summary>
    public const int MaxQuality = 2000;

    readonly EffectStripParam param;
    readonly Vector3[] curve, left, right;

    public BeamStrip(EffectStripParam param)
    {
        this.param = param;
        int n = Samples(param);
        curve = new Vector3[n];
        left = new Vector3[n];
        right = new Vector3[n];
    }

    /// <summary>The curve samples a rebuild makes: quality per segment of the two middle segments of the five control
    /// points (BuildCurve's loops), 0 for a quality below 1 (no curve: the client's strip would index from 65535 quads) or
    /// past <see cref="MaxQuality"/>.</summary>
    public static int Samples(EffectStripParam p) => p.Quality is >= 1 and <= MaxQuality ? 2 * p.Quality : 0;

    /// <summary>The points in each edge row after the last rebuild that made a curve (0 before one).</summary>
    public int Count { get; private set; }
    public ReadOnlySpan<Vector3> Curve => curve.AsSpan(0, Count);
    /// <summary>The left row (curve + side * width / 2) and the right row (curve - side * width / 2).</summary>
    public ReadOnlySpan<Vector3> Left => left.AsSpan(0, Count);
    public ReadOnlySpan<Vector3> Right => right.AsSpan(0, Count);

    /// <summary>
    /// CBeamEffect::UpdateMesh @0x140a081d0 for the points <paramref name="first"/> and <paramref name="last"/> of the target's
    /// list (a list of fewer than 2 points does nothing; the caller passes its first and last), the target node's world
    /// rotation as its columns in client space (<paramref name="axisX"/>, <paramref name="axisY"/>, <paramref name="axisZ"/>)
    /// and the camera's world direction <paramref name="camera"/> (CameraMgr::FUN_140af0250, client space).
    /// False (the strip keeps its last shape) when <see cref="BuildCurve"/> makes no curve.
    /// - The rows (:233-246): the direction at a sample is the next minus it at the first, it minus the previous at the last,
    ///   the next minus the previous between, unitized; side = direction x camera (not unitized, so the strip narrows as
    ///   the curve turns toward the camera); left = sample + width / 2 * side, right = sample - width / 2 * side.
    /// - The texture (:225-229) is picked by the caller each update, rand() % count.
    /// </summary>
    public bool Rebuild(Vector3 first, Vector3 last, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 camera)
    {
        if (!BuildCurve(first, last, axisX, axisY, axisZ)) return false;
        int n = curve.Length;
        float half = 0.5f * param.Width;
        for (int i = 0; i < n; i++)
        {
            Vector3 dir = i == 0 ? curve[1] - curve[0] : i == n - 1 ? curve[i] - curve[i - 1] : curve[i + 1] - curve[i - 1];
            dir = Unitize(dir);
            var side = Vector3.Cross(dir, camera);
            left[i] = curve[i] + half * side;
            right[i] = curve[i] - half * side;
        }
        Count = n;
        return true;
    }

    /// <summary>
    /// CBeamEffect::BuildCurve @0x140a08c60 into the curve: false with no beamInfo (<see cref="EffectStripParam.BeamCurve"/>
    /// 0; another type asserts and is false too), no samples, or the end points nearly equal (IsNearlyEqualPoint3
    /// @0x14028d530: each axis within 1e-6). The control points are first, first, the middle, last, last; each of the two
    /// middle segments gives quality samples at t = j / quality (D3DXVec3CatmullRom), so the curve starts at first and
    /// stops short of last. The middle:
    /// - pointCurve (1): first + R * passThroughPoint. R is FromEulerAnglesXYZ of minus the angles FUN_140a0b080 gives for
    ///   (last, first, UNIT_Z): the matrix of columns forward = last - first, side = forward x UNIT_Z and UNIT_Z, to Euler
    ///   angles in degrees, z + 90. With UNIT_Z as the third column the x and y angles are 0 and z is the forward's heading
    ///   (atan2 of its y and x), so R turns about z by the heading plus 90 degrees: the point's -y runs along the forward.
    /// - directionCurve (2): the node's rotation to angles (FUN_140a0b1f0: ToEulerAnglesXYZ times -57.2957764), its z to the
    ///   forward axis (FUN_140a01250: (sin z, -cos z, 0)); bend = unitize(forward + unitize(last - first) * directionRate);
    ///   middle = first + bend * |last - first| * lengthRate.
    /// </summary>
    public bool BuildCurve(Vector3 first, Vector3 last, Vector3 axisX, Vector3 axisY, Vector3 axisZ)
    {
        int q = Samples(param) / 2;
        if (q == 0) return false;
        if (MathF.Abs(first.X - last.X) < 1e-6f && MathF.Abs(first.Y - last.Y) < 1e-6f && MathF.Abs(first.Z - last.Z) < 1e-6f)
            return false;
        Vector3 mid;
        switch (param.BeamCurve)
        {
            case 1:
            {
                var f = last - first;
                // the look-at angles' z: ToEulerAnglesXYZ of (forward, forward x UNIT_Z, UNIT_Z) gives -atan2(-side.x, forward.x)
                // (the exe's angles come out negated against the right-handed ones), plus 90 degrees
                float z = -MathF.Atan2(-f.Y, f.X) * 180f / 3.1415927f + 90f;
                // FromEulerAnglesXYZ(0, 0, -z in radians): the right-handed turn about z by z
                float a = z * 3.1415927f / 180f, c = MathF.Cos(a), s = MathF.Sin(a);
                var p = param.PassThroughPoint;
                mid = first + new Vector3(c * p.X - s * p.Y, s * p.X + c * p.Y, p.Z);
                break;
            }
            case 2:
            {
                var delta = last - first;
                float length = delta.Length();
                delta = Unitize(delta);
                float rz = RotationZDegrees(axisX, axisY, axisZ);
                float r = rz * 3.1415927f / 180f;
                var forward = Unitize(new Vector3(MathF.Sin(r), -MathF.Cos(r), 0));
                var bend = Unitize(forward + delta * param.DirectionRate);
                mid = first + bend * (length * param.LengthRate);
                break;
            }
            default:
                return false;
        }
        // the control points first, first, mid, last, last: segment 0 is (first, first, mid, last), 1 (first, mid, last, last)
        Segment(curve.AsSpan(0, q), first, first, mid, last);
        Segment(curve.AsSpan(q, q), first, mid, last, last);
        return true;
    }

    /// <summary><see cref="SwooshTrail.CatmullRom"/> at t = j / count for each sample j, its coefficients made once (the
    /// same operations in the same order, so the same points).</summary>
    static void Segment(Span<Vector3> into, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
    {
        Vector3 a = 2f * v1, b = v2 - v0, c = 2f * v0 - 5f * v1 + 4f * v2 - v3, d = 3f * v1 - v0 - 3f * v2 + v3;
        int q = into.Length;
        for (int j = 0; j < q; j++)
        {
            float t = (float)j / q, t2 = t * t, t3 = t2 * t;
            into[j] = 0.5f * (a + b * t + c * t2 + d * t3);
        }
    }

    /// <summary>The z angle in degrees FUN_140a0b1f0 gives for a rotation of columns x, y, z: ToEulerAnglesXYZ (0 at the
    /// poles, within 1e-3 rad of +-90 degrees of y) times -57.2957764, which is the right-handed angle
    /// (<c>CameraMath.ToEuler</c>'s decomposition).</summary>
    public static float RotationZDegrees(Vector3 axisX, Vector3 axisY, Vector3 axisZ)
    {
        // row 0 of the matrix: (x.X, y.X, z.X)
        double m00 = axisX.X, m01 = axisY.X, m02 = axisZ.X;
        const double eps = 1e-3;
        double y = Math.Asin(Math.Clamp(m02, -1, 1));
        if (y >= Math.PI / 2 - eps || y <= -(Math.PI / 2 - eps)) return 0f;
        return (float)(Math.Atan2(-m01, m00) * 57.2957764);
    }

    /// <summary>efd::Point3::Unitize: a length below 1e-6 gives zero.</summary>
    static Vector3 Unitize(Vector3 v)
    {
        float l = v.Length();
        return l > 1e-6f ? v / l : Vector3.Zero;
    }
}
