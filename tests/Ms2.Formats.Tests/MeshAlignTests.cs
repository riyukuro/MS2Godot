using System.Numerics;
using NUnit.Framework;
using Ms2.Formats.Model;

namespace Ms2.Formats.Tests;

/// <summary>The NiPSMeshParticleSystem particle orientation and scale (MeshAlign): the FF kernel's columns, the rotation
/// efd::Matrix3::MakeRotation gives, PostUpdate's zero-column repair and the step's scale.</summary>
public class MeshAlignTests
{
    static void Near(Vector3 a, Vector3 b, string what) =>
        Assert.That(Vector3.Distance(a, b), Is.LessThan(1e-5f), $"{what}: {a} vs {b}");

    /// <summary>MakeRotation turns by -angle about the axis (Matrix3_Win32.cpp:127-153): a quarter turn about +Z takes
    /// +X to -Y.</summary>
    [Test]
    public void MakeRotationTurnsByMinusAngle()
    {
        MeshAlign.MakeRotation(MathF.PI / 2, Vector3.UnitZ, out var r0, out var r1, out var r2);
        Near(r0, new Vector3(0, 1, 0), "row 0");
        Near(r1, new Vector3(-1, 0, 0), "row 1");
        Near(r2, new Vector3(0, 0, 1), "row 2");
        var x = new Vector3(Vector3.Dot(r0, Vector3.UnitX), Vector3.Dot(r1, Vector3.UnitX), Vector3.Dot(r2, Vector3.UnitX));
        Near(x, -Vector3.UnitY, "+X turned");
    }

    /// <summary>Without rotations the columns are (normal, up x normal, normal x right) (NiPSSimulatorMeshAlignKernelFF.cpp:46-60).</summary>
    [Test]
    public void ColumnsWithoutRotations()
    {
        MeshAlign.Rotation(Vector3.UnitX, Vector3.UnitZ, false, 1.3f, Vector3.UnitY, out var c0, out var c1, out var c2);
        Near(c0, Vector3.UnitX, "normal"); Near(c1, Vector3.UnitY, "right"); Near(c2, Vector3.UnitZ, "up");
        MeshAlign.Rotation(Vector3.UnitX, Vector3.UnitY, false, 0f, Vector3.Zero, out c0, out c1, out c2);
        Near(c0, Vector3.UnitX, "normal"); Near(c1, -Vector3.UnitZ, "right = up x normal"); Near(c2, Vector3.UnitY, "up' = normal x right");
    }

    /// <summary>With rotations each column is MakeRotation(angle, axis) times the basis column
    /// (NiPSSimulatorKernelHelpers.inl:295-372), the keyed rotation the identity.</summary>
    [Test]
    public void ColumnsWithRotations()
    {
        MeshAlign.Rotation(Vector3.UnitX, Vector3.UnitZ, true, MathF.PI / 2, Vector3.UnitZ, out var c0, out var c1, out var c2);
        Near(c0, -Vector3.UnitY, "normal turned");
        Near(c1, Vector3.UnitX, "right turned");
        Near(c2, Vector3.UnitZ, "up about its own axis");
        // a zero angle leaves the basis
        MeshAlign.Rotation(Vector3.UnitX, Vector3.UnitY, true, 0f, Vector3.UnitZ, out c0, out c1, out c2);
        Near(c0, Vector3.UnitX, "normal"); Near(c1, -Vector3.UnitZ, "right"); Near(c2, Vector3.UnitY, "up");
    }

    /// <summary>PostUpdate's repair (NiPSMeshParticleSystem.cpp:400-429): an up along the normal leaves right and up' zero;
    /// the zero third column rebuilds from the particle's current third column (a new particle's: +Z).</summary>
    [Test]
    public void RepairZeroThirdColumn()
    {
        MeshAlign.Rotation(Vector3.UnitX, Vector3.UnitX, false, 0f, Vector3.Zero, out var c0, out var c1, out var c2);
        Assert.That((c1, c2), Is.EqualTo((Vector3.Zero, Vector3.Zero)), "UnitCross of parallel vectors is zero");
        MeshAlign.Repair(ref c0, ref c1, ref c2, Vector3.UnitX, Vector3.UnitZ);
        Near(c0, Vector3.UnitX, "normal kept"); Near(c1, Vector3.UnitY, "right = prev2 x normal"); Near(c2, Vector3.UnitZ, "up' = normal x right");
    }

    /// <summary>A zero first column takes the current first column as the normal and rebuilds from the step's third.</summary>
    [Test]
    public void RepairZeroFirstColumn()
    {
        Vector3 c0 = Vector3.Zero, c1 = new(5, 5, 5), c2 = Vector3.UnitY;
        MeshAlign.Repair(ref c0, ref c1, ref c2, Vector3.UnitX, Vector3.UnitZ);
        Near(c0, Vector3.UnitX, "prev0"); Near(c1, -Vector3.UnitZ, "right = c2 x prev0"); Near(c2, Vector3.UnitY, "up = prev0 x right");
        // a full rotation is left alone
        Vector3 a = Vector3.UnitY, b = -Vector3.UnitX, c = Vector3.UnitZ;
        MeshAlign.Repair(ref a, ref b, ref c, Vector3.UnitX, Vector3.UnitZ);
        Assert.That((a, b, c), Is.EqualTo((Vector3.UnitY, -Vector3.UnitX, Vector3.UnitZ)));
    }

    /// <summary>The scale is max(radius x size, 0) (NiPSSimulatorKernelHelpers.inl:366-371).</summary>
    [Test]
    public void ScaleIsRadiusTimesSizeClampedAtZero()
    {
        Assert.That(MeshAlign.Scale(2f, 3f), Is.EqualTo(6f));
        Assert.That(MeshAlign.Scale(-1f, 2f), Is.EqualTo(0f));
        Assert.That(MeshAlign.Scale(4f, 0f), Is.EqualTo(0f));
    }
}
