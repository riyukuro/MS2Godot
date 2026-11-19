using System;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2Godot.Tests;

/// <summary>The sync's child-order lists: an unchanged container costs no list, a changed one keeps its own.</summary>
public class AppliedChildListsTests
{
    sealed class P { }
    sealed class N { }

    [Test]
    public void AnOrderIsAppliedOnlyWhenTheSameNodesInTheSameOrderWereGiven()
    {
        var lists = new AppliedChildLists<P, N>();
        var p = new P(); N a = new(), b = new();
        var want = lists.Rent(); want.Add(a); want.Add(b);
        Assert.That(lists.IsApplied(p, want), Is.False, "nothing given yet");
        lists.Set(p, want);
        Assert.That(lists.IsApplied(p, want), Is.True);
        var swapped = lists.Rent(); swapped.Add(b); swapped.Add(a);
        Assert.That(lists.IsApplied(p, swapped), Is.False, "order");
        var shorter = lists.Rent(); shorter.Add(a);
        Assert.That(lists.IsApplied(p, shorter), Is.False, "count");
        lists.Remove(p);
        Assert.That(lists.IsApplied(p, want), Is.False, "dropped parent");
    }

    [Test]
    public void TheKeptOrderIsACopyOfTheListThatIsReused()
    {
        var lists = new AppliedChildLists<P, N>();
        var p = new P(); N a = new();
        var want = lists.Rent(); want.Add(a);
        lists.Set(p, want);
        lists.Return(want);                 // cleared and reused for the next container
        var next = lists.Rent();
        Assert.That(next, Is.SameAs(want));
        Assert.That(next, Is.Empty);
        next.Add(a);
        Assert.That(lists.IsApplied(p, next), Is.True, "the parent still holds its order");
    }

    [Test]
    public void AnUnchangedSyncOfManyContainersAllocatesNothing()
    {
        var lists = new AppliedChildLists<P, N>();
        var ps = new P[50]; var ns = new N[50];
        for (int i = 0; i < 50; i++) { ps[i] = new P(); ns[i] = new N(); var w = lists.Rent(); w.Add(ns[i]); lists.Set(ps[i], w); lists.Return(w); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int sync = 0; sync < 100; sync++)
            for (int i = 0; i < 50; i++)
            {
                var w = lists.Rent(); w.Add(ns[i]);
                if (!lists.IsApplied(ps[i], w)) Assert.Fail("changed");
                lists.Return(w);
            }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(512), "5000 containers");
    }
}
