using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>A lightning part's strip (LightningEffect::Rebuild and BuildPath, lightningeffectparam.cpp:312, :411).</summary>
public class LightningPathTests
{
    static EffectStripParam Param(string attrs) =>
        EffectStripParam.Read(EffectSkipTable.TypeLightning, XElement.Parse("<effect type=\"lightning\" " + attrs + "/>"));

    const string Three = "texturePathA=\"a0.dds,a1.dds,a2.dds\" texturePathB=\"b0.dds,b1.dds\" texturePathC=\"c0.dds\"";
    static readonly Vector3 Down = new(0, 0, -1);
    static readonly Func<int> Mid = () => 16383;

    [Test]
    public void TheParamsReadTheirDefaults()
    {
        var p = Param("");
        Assert.That((p.Spread, p.JointLen, p.Width, p.AlphaMode, p.TextureChangeTick, p.TextureSequence),
            Is.EqualTo((1f, 32f, 10f, 0, 0u, false)), "LightningEffectParam's ctor (:109)");
        p = Param(Three + " alphaMode=\"add\" width=\"96\" spread=\"0.2\" jointLen=\"40\" textureChangeTick=\"50\" textureSequence=\"true\"");
        Assert.That((p.Spread, p.JointLen, p.Width, p.AlphaMode, p.TextureChangeTick, p.TextureSequence),
            Is.EqualTo((0.2f, 40f, 96f, EffectStripParam.AlphaAdd, 50u, true)));
        Assert.That((p.TexturesA.Length, p.TexturesB.Length, p.TexturesC.Length), Is.EqualTo((3, 2, 1)));
    }

    [Test]
    public void NoSpreadWalksStraightInJointsAndEndsAtTheEnd()
    {
        var path = new LightningPath(Param(Three + " spread=\"0\" jointLen=\"32\" width=\"10\""));
        Assert.That(path.Rebuild(Vector3.Zero, new Vector3(320, 0, 0), Down, 1000, Mid), Is.True);
        // joints at 32 .. 256 (the end is then 64 = 2 jointLen away), then the end
        var want = new List<Vector3> { Vector3.Zero };
        for (int x = 32; x <= 256; x += 32) want.Add(new Vector3(x, 0, 0));
        want.Add(new Vector3(320, 0, 0));
        Assert.That(path.Path.ToArray(), Is.EqualTo(want.ToArray()));
        Assert.That(path.Count, Is.EqualTo(10));
        // the side: flat direction x camera direction = (1,0,0) x (0,0,-1) = (0,1,0); the rows at +-width/2
        for (int i = 0; i < path.Count; i++)
        {
            Assert.That(path.Left[i], Is.EqualTo(path.Path[i] + new Vector3(0, 5, 0)));
            Assert.That(path.Right[i], Is.EqualTo(path.Path[i] - new Vector3(0, 5, 0)));
        }
    }

    [Test]
    public void TheWalkFollowsTheSlopeAndStopsBehindTheStart()
    {
        var path = new LightningPath(Param(Three + " spread=\"0\" jointLen=\"32\""));
        var end = new Vector3(0, -300, 120);
        path.Rebuild(Vector3.Zero, end, Down, 1000, Mid);
        var dir = Vector3.Normalize(end);
        var p = path.Path;
        Assert.That((p[0], p[^1]), Is.EqualTo((Vector3.Zero, end)));
        for (int i = 1; i < p.Length - 1; i++)
        {
            float flat = MathF.Sqrt((p[i].X - p[i - 1].X) * (p[i].X - p[i - 1].X) + (p[i].Y - p[i - 1].Y) * (p[i].Y - p[i - 1].Y));
            Assert.That(flat, Is.LessThanOrEqualTo(32f + 1e-3f), "x / y of a step unitized with its z, times jointLen");
            Assert.That(flat, Is.GreaterThan(0f));
        }
        // a point's z comes from the start along the direction, measured with the point's x / y and the previous z
        var cur = p[1];
        Assert.That(cur.Z, Is.EqualTo(dir.Z * Vector3.Dot(dir, new Vector3(cur.X, cur.Y, 0))).Within(1e-3f));

        // the end straight above the start: the first joint is not ahead of the start, so the walk stops there
        path.Rebuild(Vector3.Zero, new Vector3(0, 0, 400), Down, 1000, Mid);
        Assert.That(path.Count, Is.EqualTo(3), "the start, one joint, the end");
    }

    [Test]
    public void TheWalkStopsAtAHundredPoints()
    {
        var path = new LightningPath(Param(Three + " spread=\"0\" jointLen=\"1\""));
        path.Rebuild(Vector3.Zero, new Vector3(1000, 0, 0), Down, 1000, Mid);
        Assert.That(path.Count, Is.EqualTo(LightningPath.MaxRow));
        Assert.That(path.Path[99], Is.EqualTo(new Vector3(99, 0, 0)));
        Assert.That(path.Path[100], Is.EqualTo(new Vector3(1000, 0, 0)));
    }

    [Test]
    public void TheSpreadTurnsEachStepByTheRandoms()
    {
        // R = -1 + 2 * rand / 32767: rand 32767 is 1, so step (x, y) becomes (x + s * y, y + s * x) before the unitize
        var path = new LightningPath(Param(Three + " spread=\"0.5\" jointLen=\"32\""));
        path.Rebuild(Vector3.Zero, new Vector3(0, -320, 0), Down, 1000, () => 32767);
        var first = path.Path[1];
        var want = Vector3.Normalize(new Vector3(0 + 0.5f * -320, -320 + 0.5f * 0, 0)) * 32;
        Assert.That(Vector3.Distance(first, want), Is.LessThan(1e-3f));
    }

    [Test]
    public void TheDistanceClassPicksTheTextureList()
    {
        var path = new LightningPath(Param(Three));
        foreach (var (len, cls) in new[] { (100f, 0), (256f, 0), (256.5f, 1), (512f, 1), (513f, 2) })
        {
            path.Rebuild(Vector3.Zero, new Vector3(len, 0, 0), Down, 1000, () => 4);
            Assert.That(path.TextureClass, Is.EqualTo(cls), $"length {len}");
            Assert.That(path.TextureIndex, Is.EqualTo(4 % path.Textures(cls).Length), "rand() % count");
        }
        var empty = new LightningPath(Param("texturePathA=\"\""));
        empty.Rebuild(Vector3.Zero, new Vector3(10, 0, 0), Down, 1000, Mid);
        Assert.That(empty.TextureIndex, Is.EqualTo(-1), "an empty list picks none");
    }

    [Test]
    public void SequenceStartsAtTheSecondTextureAndTheTickHoldsTheStrip()
    {
        var path = new LightningPath(Param(Three + " textureSequence=\"true\" textureChangeTick=\"100\""));
        var end = new Vector3(100, 0, 0);
        Assert.That(path.Rebuild(Vector3.Zero, end, Down, 50, Mid), Is.False, "now < last (0) + tick");
        Assert.That(path.Count, Is.EqualTo(0));
        Assert.That(path.Rebuild(Vector3.Zero, end, Down, 1000, Mid), Is.True);
        Assert.That(path.TextureIndex, Is.EqualTo(1), "(m_nSequence 0 + 1) % 3");
        Assert.That(path.Rebuild(Vector3.Zero, end * 2, Down, 1099, Mid), Is.False);
        Assert.That(path.Path[^1], Is.EqualTo(end), "kept until the tick passed");
        Assert.That(path.Rebuild(Vector3.Zero, end * 2, Down, 1100, Mid), Is.True);
        Assert.That((path.TextureIndex, path.Path[^1]), Is.EqualTo((2, end * 2)));
        path.Rebuild(Vector3.Zero, end, Down, 1200, Mid);
        Assert.That(path.TextureIndex, Is.EqualTo(0));
    }

    [Test]
    public void ARebuildAllocatesNothing()
    {
        var path = new LightningPath(Param(Three + " spread=\"0.2\" jointLen=\"32\" width=\"96\""));
        var rng = new Random(1);
        Func<int> rand = () => rng.Next(0x8000);
        var camera = Vector3.Normalize(new Vector3(0.3f, 0.5f, -0.8f));
        Rebuilds(path, camera, rand, 200);
        // The JIT can tier Rebuild up (call counting, backpatching) inside a measured window and charge its own bytes to this
        // thread (24 B seen once in a run). A real allocation in Rebuild shows on every pass, so the least of five must be 0.
        long least = long.MaxValue;
        for (int pass = 0; pass < 5 && least != 0; pass++) least = Math.Min(least, Rebuilds(path, camera, rand, 1000));
        Assert.That(least, Is.EqualTo(0));
    }

    // fully optimized from the start: a Release build's tiered loop (on-stack replacement) allocates on its own
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    static long Rebuilds(LightningPath path, Vector3 camera, Func<int> rand, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) path.Rebuild(new Vector3(i, 0, 0), new Vector3(600, -200 + i, 40), camera, (uint)(1000 + i), rand);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
