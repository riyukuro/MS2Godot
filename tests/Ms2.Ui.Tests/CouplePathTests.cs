using System.Numerics;
using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CoupleEmotionAnimInfo::Load's node lookups and the state's two uses of the path nif (statecoupleemotion.cpp).</summary>
public class CouplePathTests
{
    // Eff_center at the origin, A at (-65.8, 65.8, 0) turned 40 degrees, B at (65.8, -65.8, 0) turned 225 degrees
    const string BallToss = "coupleaction_dummy_balltoss_diff_loop.nif";

    static Ms2.Formats.Nif.NifFile Nif(string entry)
    {
        var nx = CoupleEmotionTableTests.NifXmlPath();
        if (nx == null) Assert.Ignore("MS2_NIFXML is not set and external/nifxml is not checked out");
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        return Ms2.Formats.Nif.NifFile.Load(new Ms2.Formats.Nif.NifSpec(nx), packs.Read("Resource/Model/Path", entry));
    }

    static void Near(Vector3 a, Vector3 b, string what) =>
        Assert.That(Vector3.Distance(a, b), Is.LessThan(0.01f), $"{what}: {a} vs {b}");

    [Test]
    public void LoadFindsTheNodesAndNeedsOwnerAndPeer()
    {
        var nif = Nif(BallToss);
        Assert.That(CouplePath.Load(nif, "B", "A"), Is.Not.Null);
        Assert.That(CouplePath.Load(nif, "A", "B"), Is.Not.Null);
        Assert.That(CouplePath.Load(nif, "B", "C"), Is.Null, "no peer node");
        Assert.That(CouplePath.Load(nif, "C", "A"), Is.Null, "no owner node");
        Assert.That(CouplePath.Load(nif, "b", "A"), Is.Null, "names compare with case");
        // the root has no parent: FindPlacement / SyncWithPartner would assert
        Assert.That(CouplePath.Load(nif, "Scene Root", "A"), Is.Null);
    }

    [Test]
    public void TheNodesStandWhereTheNifPutsThem()
    {
        var p = CouplePath.Load(Nif(BallToss), "B", "A");
        var b = p.OwnerWorld;
        Assert.That((b.T.X, b.T.Y), Is.EqualTo((65.8, -65.8)).Within(0.01));
        Assert.That(CouplePath.ToAngles(p.PeerWorld.R, out var a), Is.True);
        Assert.That(a.Z, Is.EqualTo(40f).Within(0.01f), "Rz(40) reads back as +40: ToEulerAnglesXYZ times -57.29");
    }

    [Test]
    public void FindPlacementPutsTheParentAndPeerAroundTheOwnersSpot()
    {
        var p = CouplePath.Load(Nif(BallToss), "B", "A");
        var start = new Vector3(1000, 2000, 300);
        var asked = new List<Vector3>();
        (Vector3 from, Vector3 dir, float len) ray = default;
        Assert.That(p.FindPlacement(start, Vector3.Zero, q => { asked.Add(q); return true; }, (f, d, l) => { ray = (f, d, l); return false; },
            out var posA, out var posB, out float yaw), Is.True);
        // the peer first, then the owner, each before its raise by 5
        Near(asked[0], start + new Vector3(-131.6f, 131.6f, 0), "peer probe");
        Near(asked[1], start, "owner probe");
        Near(posA, start + new Vector3(-131.6f, 131.6f, 5), "posA is the raised peer point");
        Near(posB, start + new Vector3(-65.8f, 65.8f, 0), "posB is the parent, not raised");
        Assert.That(yaw, Is.EqualTo(0f).Within(0.01f));
        Near(ray.from, start + new Vector3(0, 0, 5), "the ray starts at the raised owner point");
        Assert.That(ray.len, Is.EqualTo(186.11f).Within(0.05f));
        Near(ray.dir, Vector3.Normalize(new Vector3(-1, 1, 0)), "unit direction");
    }

    [Test]
    public void EachRefusedStepTurnsOneDegree()
    {
        var p = CouplePath.Load(Nif(BallToss), "B", "A");
        int calls = 0;
        // step 0: the peer fails (the owner is not probed); step 1: the owner fails; step 2: the ray hits; step 3 passes
        bool Place(Vector3 _) => ++calls switch { 1 => false, 2 => true, 3 => false, _ => true };
        int rays = 0;
        Assert.That(p.FindPlacement(Vector3.Zero, new Vector3(0, 0, 10), Place, (_, _, _) => ++rays == 1, out _, out var posB, out float yaw), Is.True);
        Assert.That(calls, Is.EqualTo(7));
        Assert.That(rays, Is.EqualTo(2));
        Assert.That(yaw, Is.EqualTo(13f).Within(0.01f), "the owner's 10 plus three steps");
        var r = 13 * MathF.PI / 180;
        Near(posB, new Vector3(-65.8f * MathF.Cos(r) - 65.8f * MathF.Sin(r), -65.8f * MathF.Sin(r) + 65.8f * MathF.Cos(r), 0), "parent turned 13 degrees");
        Assert.That(p.FindPlacement(Vector3.Zero, Vector3.Zero, _ => false, (_, _, _) => false, out _, out _, out _), Is.False, "360 refused steps");
    }

    [Test]
    public void OwnerPoseTurnsTheParentToTheEndAngles()
    {
        var p = CouplePath.Load(Nif(BallToss), "B", "A");
        var (pos, angles) = p.OwnerPose(new Vector3(10, 20, 30), new Vector3(0, 0, 90));
        // Rz(90) of B's (65.8, -65.8) is (65.8, 65.8); B's own 225 degrees on top of the 90
        Near(pos, new Vector3(75.8f, 85.8f, 30), "feet");
        Assert.That(angles, Is.Not.Null);
        Assert.That(angles.Value.Z, Is.EqualTo(-45f).Within(0.01f));
    }
    /// <summary>
    /// SyncWithPartner runs each frame for each couple PC: UpdateNif then the owner's pose. Over every couple path nif of the
    /// data (both actors), a frame allocates no managed memory once the path is loaded: the node transforms, the end angles'
    /// matrix and the pose fill buffers the path keeps.
    /// </summary>
    [Test]
    public void ASyncFrameAllocatesNothing()
    {
        var nx = CoupleEmotionTableTests.NifXmlPath();
        if (nx == null) Assert.Ignore("MS2_NIFXML is not set and external/nifxml is not checked out");
        var spec = new Ms2.Formats.Nif.NifSpec(nx);
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var table = new GameTables(packs, "en").CoupleEmotions;
        var entries = table.Rows.SelectMany(d => d.All()).SelectMany(x => x.Item3.NifPaths)
            .Select(n => n.ToLowerInvariant()).Distinct().Where(e => packs.Exists("Resource/Model/Path", e)).OrderBy(e => e).ToList();
        const int frames = 600;
        long worst = 0, total = 0;
        string worstNif = null;
        foreach (var e in entries)
        {
            var file = Ms2.Formats.Nif.NifFile.Load(spec, packs.Read("Resource/Model/Path", e));
            foreach (var (own, peer) in new[] { (CoupleEmotionTable.NodeA, CoupleEmotionTable.NodeB), (CoupleEmotionTable.NodeB, CoupleEmotionTable.NodeA) })
            {
                var p = CouplePath.Load(file, own, peer);
                Assert.That(p, Is.Not.Null, e);
                float sum = 0;
                long bytes = 0, first = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int f = 1; f <= frames; f++)
                    {
                        p.Update(f / 60.0);
                        sum += p.OwnerPose(new Vector3(100, 200, 300), new Vector3(0, 0, f)).Pos.X;
                    }
                    if (pass == 0) first = GC.GetAllocatedBytesForCurrentThread() - before;
                    else bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                if (first > 0) TestContext.WriteLine($"first pass {e} ({own}): {first} B, second {bytes} B");
                Assert.That(float.IsFinite(sum), e);
                total += bytes;
                if (bytes > worst) { worst = bytes; worstNif = $"{e} ({own})"; }
            }
        }
        TestContext.WriteLine($"{entries.Count} path nifs, {frames} frames each actor: worst {worst / (double)frames:0.#} B/frame ({worstNif ?? "none"}), "
            + $"total {total} B");
        Assert.That(entries, Has.Count.GreaterThan(10));
        Assert.That(worst, Is.Zero, worstNif);
    }
}
