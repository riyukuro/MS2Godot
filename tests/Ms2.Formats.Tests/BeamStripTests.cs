using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The beam part's curve and rows (<see cref="BeamStrip"/>, CBeamEffect::BuildCurve / UpdateMesh), worked by hand from
/// the client's formulas.</summary>
public class BeamStripTests
{
    static EffectStripParam Param(string beamInfo, string extra = "") => EffectStripParam.Read(EffectSkipTable.TypeBeam,
        XElement.Parse($"<beam texturePath=\"a.dds,b.dds\" width=\"10\" quality=\"4\" {extra}>{beamInfo}</beam>"));

    // the magic scene's rotation facing +x (CMagic::UpdateLookAtRotation: columns right, -direction, up)
    static readonly Vector3 RightX = new(0, 1, 0), AxisX = new(-1, 0, 0), Up = Vector3.UnitZ;
    static readonly Vector3 Down = new(0, 0, -1);

    static void Near(Vector3 got, Vector3 want, string what) =>
        Assert.That(Vector3.Distance(got, want), Is.LessThan(1e-3f), $"{what}: {got} for {want}");

    [Test]
    public void APointCurvePassesThroughItsPointTurnedToTheForward()
    {
        var p = Param("<beamInfo type=\"pointCurve\" passThroughPoint=\"30, -100, 20\"/>");
        Assert.That(p.BeamCurve, Is.EqualTo(1));
        var s = new BeamStrip(p);
        var a = new Vector3(10, 20, 30);
        // forward +x: the point's -y along +x, its x along +y
        Assert.That(s.Rebuild(a, a + new Vector3(400, 0, 0), RightX, AxisX, Up, Down), Is.True);
        Assert.That(s.Count, Is.EqualTo(8), "quality samples on each of the two middle segments");
        var mid = a + new Vector3(100, 30, 20);
        Near(s.Curve[0], a, "the curve starts at the first point");
        Near(s.Curve[4], mid, "the second segment starts at the middle control point");
        Near(s.Curve[2], SwooshTrail.CatmullRom(a, a, mid, a + new Vector3(400, 0, 0), 0.5f), "a sample of the first segment");
        Near(s.Curve[7], SwooshTrail.CatmullRom(a, mid, a + new Vector3(400, 0, 0), a + new Vector3(400, 0, 0), 0.75f),
            "the last sample stops short of the last point");
        // forward +y: the point's -y along +y, its x along -x
        Assert.That(s.Rebuild(a, a + new Vector3(0, 400, 0), RightX, AxisX, Up, Down), Is.True);
        Near(s.Curve[4], a + new Vector3(-30, 100, 20), "turned with the forward");
        // the heading is planar: a rising end turns the same
        Assert.That(s.Rebuild(a, a + new Vector3(0, 400, 300), RightX, AxisX, Up, Down), Is.True);
        Near(s.Curve[4], a + new Vector3(-30, 100, 20), "the height does not tilt it");
    }

    [Test]
    public void ADirectionCurveBowsTowardTheSceneNodesForward()
    {
        var p = Param("<beamInfo type=\"directionCurve\" directionRate=\"0.5\" lengthRate=\"0.25\"/>");
        Assert.That(p.BeamCurve, Is.EqualTo(2));
        Assert.That(BeamStrip.RotationZDegrees(RightX, AxisX, Up), Is.EqualTo(90f).Within(1e-4f), "facing +x is z 90");
        var s = new BeamStrip(p);
        var a = new Vector3(0, 0, 0);
        var e = new Vector3(0, 200, 0);
        Assert.That(s.Rebuild(a, e, RightX, AxisX, Up, Down), Is.True);
        // bend = unit((1, 0, 0) + (0, 1, 0) * 0.5), middle = bend * 200 * 0.25
        var mid = Vector3.Normalize(new Vector3(1, 0.5f, 0)) * 50f;
        Near(s.Curve[4], mid, "the middle");
        // absent rates are 0.5
        var q = Param("<beamInfo type=\"directionCurve\"/>");
        Assert.That((q.DirectionRate, q.LengthRate), Is.EqualTo((0.5f, 0.5f)));
    }

    [Test]
    public void TheRowsAreHalfTheWidthEachSideAcrossTheCamera()
    {
        // the middle halfway along, so the curve runs on +x the whole way (the control points evenly spaced)
        var s = new BeamStrip(Param("<beamInfo type=\"pointCurve\" passThroughPoint=\"0,-400,0\"/>"));
        var a = new Vector3(0, 0, 0);
        Assert.That(s.Rebuild(a, new Vector3(800, 0, 0), RightX, AxisX, Up, Down), Is.True);
        for (int i = 0; i < s.Count; i++)
        {
            // direction +x, camera straight down: side = (1, 0, 0) x (0, 0, -1) = (0, 1, 0)
            Near(s.Left[i], s.Curve[i] + new Vector3(0, 5, 0), $"left {i}");
            Near(s.Right[i], s.Curve[i] - new Vector3(0, 5, 0), $"right {i}");
        }
        // looking along the curve the side is not unitized: the strip has no width
        Assert.That(s.Rebuild(a, new Vector3(800, 0, 0), RightX, AxisX, Up, Vector3.UnitX), Is.True);
        Near(s.Left[3], s.Right[3], "edge on");
    }

    [Test]
    public void NoCurveKeepsTheLastShape()
    {
        var s = new BeamStrip(Param("<beamInfo type=\"pointCurve\" passThroughPoint=\"0,-50,0\"/>"));
        var a = new Vector3(5, 5, 5);
        Assert.That(s.Rebuild(a, a + new Vector3(1e-7f, 0, 0), RightX, AxisX, Up, Down), Is.False, "nearly equal ends");
        Assert.That(s.Count, Is.EqualTo(0));
        Assert.That(s.Rebuild(a, a + new Vector3(100, 0, 0), RightX, AxisX, Up, Down), Is.True);
        var kept = s.Curve[4];
        Assert.That(s.Rebuild(a, a, RightX, AxisX, Up, Down), Is.False);
        Assert.That(s.Count, Is.EqualTo(8));
        Assert.That(s.Curve[4], Is.EqualTo(kept));

        Assert.That(new BeamStrip(Param("")).Rebuild(a, a + Vector3.UnitX * 100, RightX, AxisX, Up, Down), Is.False, "no beamInfo");
        Assert.That(new BeamStrip(Param("<beamInfo type=\"wave\"/>")).Rebuild(a, a + Vector3.UnitX * 100, RightX, AxisX, Up, Down),
            Is.False, "another type");
        var none = Param("<beamInfo type=\"pointCurve\"/>", "");
        none.Quality = 0;
        Assert.That(BeamStrip.Samples(none), Is.EqualTo(0));
        Assert.That(new BeamStrip(none).Rebuild(a, a + Vector3.UnitX * 100, RightX, AxisX, Up, Down), Is.False, "quality 0");
    }

    [Test]
    public void TheSamplesAreTheCatmullRomPointsBitForBit()
    {
        foreach (var info in new[] { "<beamInfo type=\"directionCurve\" directionRate=\"0.7\" lengthRate=\"0.3\"/>",
                     "<beamInfo type=\"pointCurve\" passThroughPoint=\"0,-250,100\"/>" })
        {
            var p = Param(info);
            p.Quality = 50;
            var s = new BeamStrip(p);
            var a = new Vector3(13.25f, -7.5f, 30.125f);
            var e = a + new Vector3(337.7f, -451.3f, 41.9f);
            Assert.That(s.Rebuild(a, e, RightX, AxisX, Up, Down), Is.True);
            var mid = s.Curve[50];
            for (int j = 0; j < 50; j++)
            {
                Assert.That(s.Curve[j], Is.EqualTo(SwooshTrail.CatmullRom(a, a, mid, e, j / 50f)), $"{info} sample {j}");
                Assert.That(s.Curve[50 + j], Is.EqualTo(SwooshTrail.CatmullRom(a, mid, e, e, j / 50f)), $"{info} sample {50 + j}");
            }
        }
    }

    [Test]
    public void ARebuildAllocatesNothing()
    {
        var s = new BeamStrip(Param("<beamInfo type=\"directionCurve\"/>"));
        var a = new Vector3(1, 2, 3);
        Rebuilds(s, a, 200);
        // the least of a few passes: a tier-up of the loop while measuring is the runtime's, not the rebuild's
        long least = long.MaxValue;
        for (int pass = 0; pass < 5 && least != 0; pass++) least = Math.Min(least, Rebuilds(s, a, 1000));
        Assert.That(least, Is.EqualTo(0));
    }

    static long Rebuilds(BeamStrip s, Vector3 a, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) s.Rebuild(a, a + new Vector3(300 + i, 40, 10), RightX, AxisX, Up, Down);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
