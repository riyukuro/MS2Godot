using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>NpcJumpArc against the client's arc mover (unit_14160a7f0.cpp:198-220, unit_14160bf90.cpp:38-54) on made-up jump blocks.</summary>
public class NpcJumpArcTests
{
    static void Near(Vector3 got, Vector3 want) => Assert.That(Vector3.Distance(got, want), Is.LessThan(0.001f), $"{got} != {want}");

    [Test]
    public void ARelativeJumpRunsFromTheBlockPositionInTheShortTime()
    {
        var arc = new NpcJumpArc();
        var block = new NpcSync { Sequence = -2, JumpTo = new Vector3(300, 0, 0) };
        Assert.That(arc.Begin(block, new Vector3(0, 0, 4)), Is.True);
        Assert.That(arc.Moving && arc.Name == "Jump_A", Is.True);
        Assert.That(arc.Duration, Is.EqualTo(NpcJumpArc.RelativeTime));
        Near(arc.End, new Vector3(300, 0, 2));
        float dist = new Vector3(300, 0, -2).Length();
        Assert.That(arc.Height, Is.EqualTo(150f / 212.132f * dist).Within(1e-3f));
        float yaw = 7;
        var mid = arc.Advance(NpcJumpArc.RelativeTime / 2, ref yaw);
        Near(mid, new Vector3(150, 0, 3 + arc.Height));
        Assert.That(yaw, Is.EqualTo(90f).Within(1e-4f));
        Assert.That(arc.Moving, Is.True);
        var end = arc.Advance(1f, ref yaw);
        Near(end, new Vector3(300, 0, 2));
        Assert.That(arc.Moving, Is.False);
        Assert.That(arc.Name, Is.Empty);
        Assert.That(arc.Elapsed, Is.EqualTo(arc.Duration));
    }

    [Test]
    public void AnAbsoluteJumpTakesItsOwnPointsTimeAndScale()
    {
        var arc = new NpcJumpArc();
        var block = new NpcSync { Sequence = -3, JumpAbsolute = true, JumpFrom = new Vector3(10, 10, 0), JumpTo = new Vector3(10, 110, -2), JumpWidth = 2f, JumpScale = 0.5f };
        arc.Begin(block, new Vector3(999, 999, 999));
        Assert.That(arc.Name, Is.EqualTo("Jump_B"));
        Near(arc.Start, new Vector3(10, 10, 0));
        Near(arc.End, new Vector3(10, 110, 0));
        Assert.That(arc.Duration, Is.EqualTo(2f));
        Assert.That(arc.Height, Is.EqualTo(150f / 212.132f * 100f * 0.5f).Within(1e-3f));
        float yaw = 0;
        var q = arc.Advance(0.5f, ref yaw);
        Near(q, new Vector3(10, 35, arc.Height * 4 * 0.25f * 0.75f));
        Assert.That(yaw, Is.EqualTo(180f).Within(1e-4f));
    }

    [Test]
    public void TheYawStaysForAJumpOnTheSpotAndATimeOfZeroEndsAtOnce()
    {
        var arc = new NpcJumpArc();
        arc.Set(new Vector3(5, 5, 5), new Vector3(5, 5, 5), 0f, 1f, "Jump_A");
        float yaw = 33;
        Near(arc.Advance(0f, ref yaw), new Vector3(5, 5, 5));
        Assert.That(yaw, Is.EqualTo(33f));
        Assert.That(arc.Moving, Is.False);
    }

    [Test]
    public void ABlockWithoutAJumpSequenceStartsNoArc()
    {
        var arc = new NpcJumpArc();
        Assert.That(arc.Begin(new NpcSync { Sequence = -1 }, Vector3.Zero), Is.False);
        Assert.That(arc.Begin(new NpcSync { Sequence = 4 }, Vector3.Zero), Is.False);
        Assert.That(arc.Moving, Is.False);
    }
}
