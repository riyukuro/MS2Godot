using System.Numerics;

namespace Ms2.Formats.Model;

/// <summary>The orientation and scale of a NiPSMeshParticleSystem's particles: NiPSSimulatorMeshAlignStep's kernels and the
/// system's PostUpdate. Every mesh system in both data sets has fixed normal and up methods (ALIGN_LOCAL_FIXED), so the FF
/// kernel runs (NiPSSimulatorMeshAlignKernelFF.cpp:36-79): right = up.UnitCross(normal) and up' = normal x right; without
/// rotations the particle's rotation columns are (normal, right, up'), with them (and rotation axes, asserted there) each
/// particle gets keyed x MakeRotation(angle, axis) x basis (NiPSSimulatorKernelHelpers::UpdateMeshAlignmentFFR,
/// NiPSSimulatorKernelHelpers.inl:295-372). The step's scale amount is 0 and its rest 1 (the constructor's defaults,
/// NiPSSimulatorMeshAlignStep.cpp:29-60; LoadBinary reads only the rotation keys, :732-745), so a particle's scale is
/// max(radius x size, 0). Floats, like the kernels.
/// STILL-OPEN: the step's rotation keys (none in either data set: keyed is the identity here).</summary>
public static class MeshAlign
{
    /// <summary>efd::Matrix3::MakeRotation(angle, x, y, z) (efd/Win32/Matrix3_Win32.cpp:127-153), as its three rows: the
    /// rotation by -angle about the axis, which it takes as given (not unitized).</summary>
    public static void MakeRotation(float angle, Vector3 axis, out Vector3 row0, out Vector3 row1, out Vector3 row2)
    {
        float cs = MathF.Cos(angle), sn = MathF.Sin(angle), omcs = 1f - cs;
        float x = axis.X, y = axis.Y, z = axis.Z;
        float x2 = x * x, y2 = y * y, z2 = z * z, xym = x * y * omcs, xzm = x * z * omcs, yzm = y * z * omcs;
        float xs = x * sn, ys = y * sn, zs = z * sn;
        row0 = new Vector3(x2 * omcs + cs, xym + zs, xzm - ys);
        row1 = new Vector3(xym - zs, y2 * omcs + cs, yzm + xs);
        row2 = new Vector3(xzm + ys, yzm - xs, z2 * omcs + cs);
    }

    /// <summary>The rotation columns the FF kernel gives a particle (NiPSSimulatorMeshAlignKernelFF.cpp:46-76). normal and up
    /// are the system's fixed vectors in its simulation space; angle and axis the particle's rotation angle and axis, used
    /// when the system has rotations (rotations false: the angle and axis are left out).</summary>
    public static void Rotation(Vector3 normal, Vector3 up, bool rotations, float angle, Vector3 axis,
                                out Vector3 c0, out Vector3 c1, out Vector3 c2)
    {
        var right = AlignedQuad.UnitCross(up, normal);
        var upB = Vector3.Cross(normal, right);
        if (!rotations) { c0 = normal; c1 = right; c2 = upB; return; }
        // kRotation = keyed (identity) x speedRotate x basis: column j is speedRotate times the basis's column j
        MakeRotation(angle, axis, out var r0, out var r1, out var r2);
        c0 = new Vector3(Vector3.Dot(r0, normal), Vector3.Dot(r1, normal), Vector3.Dot(r2, normal));
        c1 = new Vector3(Vector3.Dot(r0, right), Vector3.Dot(r1, right), Vector3.Dot(r2, right));
        c2 = new Vector3(Vector3.Dot(r0, upB), Vector3.Dot(r1, upB), Vector3.Dot(r2, upB));
    }

    /// <summary>NiPSMeshParticleSystem::PostUpdate's fix for a rotation the step could not resolve
    /// (NiPSMeshParticleSystem.cpp:400-429): a zero first column takes the particle's current first column (prev0) as the
    /// normal and rebuilds right and up from the step's third column; else a zero third column keeps the first and rebuilds
    /// right and up from the particle's current third column (prev2). A new particle's current rotation is the identity
    /// (InitializeParticle, :288).</summary>
    public static void Repair(ref Vector3 c0, ref Vector3 c1, ref Vector3 c2, Vector3 prev0, Vector3 prev2)
    {
        if (c0 == Vector3.Zero)
        {
            var right = AlignedQuad.UnitCross(c2, prev0);
            var up = Vector3.Cross(prev0, right);
            c0 = prev0; c1 = right; c2 = up;
        }
        else if (c2 == Vector3.Zero)
        {
            var right = AlignedQuad.UnitCross(prev2, c0);
            c1 = right; c2 = Vector3.Cross(c0, right);
        }
    }

    /// <summary>The particle's scale (UpdateMeshAlignmentFF/FFR, NiPSSimulatorKernelHelpers.inl:366-371, amount 0 and rest 1):
    /// max(radius x size, 0).</summary>
    public static float Scale(float radius, float size) => MathF.Max(radius * size, 0f);
}
