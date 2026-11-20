using System.Numerics;
using Ms2.Formats.Model;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// NiBillboardNode::RotateToCamera's face matrix (BillboardFace) and its mode bits, which the effect runtime turns its billboard
/// nodes with (EffectNode.BillboardMode). The field shader keeps its own billboard (ModelMesh.BillboardMode, field_billboard).
/// </summary>
public class BillboardFaceTests
{
    [Test]
    public void ModeIsTheLowThreeFlagBits()
    {
        Assert.That(BillboardFace.ModeOf(9), Is.EqualTo(BillboardFace.RotateAboutUp));     // the guardstone's flags
        Assert.That(BillboardFace.ModeOf(0x0A), Is.EqualTo(BillboardFace.RigidFaceCamera));
        Assert.That(BillboardFace.ModeOf(0), Is.EqualTo(BillboardFace.AlwaysFaceCamera));
    }

    static float Det((Vector3 c0, Vector3 c1, Vector3 c2) m) => Vector3.Dot(m.c0, Vector3.Cross(m.c1, m.c2));

    [Test]
    public void RotateAboutUpTurnsLocalZToTheCameraAboutLocalY()
    {
        var cam = new Vector3(300, 120, -400);
        var f = BillboardFace.Face(BillboardFace.RotateAboutUp, cam, -Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 2f);
        // NiBillboardNode.cpp:140-158: col0 (z, 0, -x), col1 (0, 1, 0), col2 (x, 0, z), x and z the unit horizontal direction
        Assert.That(f.c1, Is.EqualTo(Vector3.UnitY));
        Assert.That(f.c2.X, Is.EqualTo(0.6f).Within(1e-6));
        Assert.That(f.c2.Y, Is.EqualTo(0f));
        Assert.That(f.c2.Z, Is.EqualTo(-0.8f).Within(1e-6));
        Assert.That(f.c0.X, Is.EqualTo(-0.8f).Within(1e-6));
        Assert.That(f.c0.Z, Is.EqualTo(-0.6f).Within(1e-6));
        Assert.That(Det(f), Is.EqualTo(1f).Within(1e-5));
        // a camera straight above the node: the engine breaks out with the identity
        var top = BillboardFace.Face(BillboardFace.RotateAboutUp, new Vector3(0, 50, 0), -Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1f);
        Assert.That(top, Is.EqualTo((Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)));
    }

    [Test]
    public void FaceCameraModesTakeTheCameraAxes()
    {
        var d = Vector3.Normalize(new Vector3(0, -1, -1));
        var u = Vector3.Normalize(new Vector3(0, 1, -1));
        var r = Vector3.UnitX;
        var rigid = BillboardFace.Face(BillboardFace.RigidFaceCamera, new Vector3(0, 10, 10), d, u, r, 1f);
        Assert.That(rigid, Is.EqualTo((r, u, -d)));
        var always = BillboardFace.Face(BillboardFace.AlwaysFaceCamera, new Vector3(0, 10, 10), d, u, r, 1f);
        Assert.That(always.c2, Is.EqualTo(-d));
        Assert.That(always.c0.Y, Is.EqualTo(0f).Within(1e-6));      // the up kept nearest the model's (0, 1, 0)
        Assert.That(Det(always), Is.EqualTo(1f).Within(1e-5));
        // the center modes: D is the unit vector from the node to the camera
        var center = BillboardFace.Face(BillboardFace.RigidFaceCenter, new Vector3(3, 0, 4), d, u, r, 1f);
        Assert.That(Vector3.Distance(center.c2, new Vector3(0.6f, 0, 0.8f)), Is.LessThan(1e-5f));
        Assert.That(Det(center), Is.EqualTo(1f).Within(1e-5));
        Assert.That(BillboardFace.Face(7, Vector3.One, d, u, r, 1f), Is.EqualTo((Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)));
    }
}
