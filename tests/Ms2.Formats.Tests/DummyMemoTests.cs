using System;
using System.Collections.Generic;
using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The dummy point memo: a model that stands still costs no matrices, a changed input is computed again.</summary>
public class DummyMemoTests
{
    static readonly List<EffectDummy> Rows = new()
    {
        new() { Name = "Eff_UI", ParentNode = "Scene Root", ApplyWorldTransform = true, ApplyLocalScale = true, Translate = new Vector3(0, 0, 170) },
    };

    static DummyMemo.Key At(float x) => new(1, 0, 0, 0, 1, 0, 0, 0, 1, x, 0, 0);

    int computed;

    [SetUp] public void Reset() => computed = 0;

    // the lookup as DummyAnchor.Point makes it: the memo first, the matrices on a miss
    DummyMemo.Point Lookup(DummyMemo memo, IReadOnlyList<EffectDummy> rows, string name, string root, DummyMemo.Key at)
    {
        if (memo.TryGet(rows, name, root, at, out var r)) return r;
        computed++;
        var w = DummyWorld.Find(rows, name, new Xform(new Vec3(at.J, 0, 0), new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 1), root);
        r = w is { } x ? new DummyMemo.Point((float)x.T.X, (float)x.T.Y, (float)x.T.Z, true) : default;
        memo.Set(rows, name, root, at, r);
        return r;
    }

    [Test]
    public void AModelThatStandsStillIsComputedOnce()
    {
        var memo = new DummyMemo();
        var first = Lookup(memo, Rows, "Eff_UI", "Scene Root", At(5));
        Assert.That(first.Has, Is.True);
        Assert.That(first.Z, Is.EqualTo(170f));
        for (int i = 0; i < 10; i++) Assert.That(Lookup(memo, Rows, "Eff_UI", "Scene Root", At(5)), Is.EqualTo(first));
        Assert.That(computed, Is.EqualTo(1));
    }

    [Test]
    public void AMovedModelADifferentRowsOrRootIsComputedAgain()
    {
        var memo = new DummyMemo();
        Assert.That(Lookup(memo, Rows, "Eff_UI", "Scene Root", At(5)).X, Is.EqualTo(5f));
        Assert.That(Lookup(memo, Rows, "Eff_UI", "Scene Root", At(7)).X, Is.EqualTo(7f), "moved");
        Assert.That(computed, Is.EqualTo(2));
        Lookup(memo, new List<EffectDummy>(Rows), "Eff_UI", "Scene Root", At(7));
        Assert.That(computed, Is.EqualTo(3), "other rows");
        Lookup(memo, Rows, "Eff_UI", "Other", At(7));
        Assert.That(computed, Is.EqualTo(4), "other root name");
        var grown = new List<EffectDummy>(Rows);
        Lookup(memo, grown, "Eff_UI", "Other", At(7));
        grown.Add(new EffectDummy { Name = "Eff_Damage", ParentNode = "Scene Root" });
        Lookup(memo, grown, "Eff_UI", "Other", At(7));
        Assert.That(computed, Is.EqualTo(6), "a row added");
    }

    [Test]
    public void AMissingDummyIsRememberedToo()
    {
        var memo = new DummyMemo();
        Assert.That(Lookup(memo, Rows, "Eff_None", "Scene Root", At(1)).Has, Is.False);
        Lookup(memo, Rows, "Eff_None", "Scene Root", At(1));
        Assert.That(computed, Is.EqualTo(1));
    }

    [Test]
    public void ARepeatedLookupAllocatesNothing()
    {
        var memo = new DummyMemo();
        Lookup(memo, Rows, "Eff_UI", "Scene Root", At(5));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Lookup(memo, Rows, "Eff_UI", "Scene Root", At(5));
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(256), "1000 lookups");
    }
}
