using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ms2Godot.Tests;

/// <summary>Godot-bound code cannot run in the test host, so these read the game sources and fail when the wiring of a reviewed
/// cost fix is reverted: the helper tests prove the helper, these prove the game still calls it in the right order.</summary>
public class WiringGuardTests
{
    static string Source(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, rel);
            if (File.Exists(p)) return Regex.Replace(File.ReadAllText(p), @"\s+", " ");
        }
        Assert.Fail("source not found above the test directory: " + rel);
        return null;
    }

    [Test]
    public void As3ViewSkipsAnUnchangedChildOrderAndRemembersAChangedOne()
    {
        var s = Source("game/ui/As3View.cs");
        Assert.That(s, Does.Match(@"applied\.IsApplied\(into, want\)\) return; applied\.Set\(into, want\);"),
            "the sync must return before touching the node tree when the order is already applied, and record it otherwise");
        Assert.That(s, Does.Contain("var wanted = applied.Rent();"), "the per-container list is rented");
        Assert.That(s, Does.Contain("applied.Return(wanted);"), "and given back");
        Assert.That(s, Does.Contain("clipped = applied.Rent()"), "clip lists are rented too");
        Assert.That(s, Does.Contain("applied.Return(kids);"));
    }

    [Test]
    public void As3ViewHolderNameTableStopsGrowingAtItsCap()
    {
        var s = Source("game/ui/As3View.cs");
        Assert.That(s, Does.Contain("internal const int HolderNameCap = 4096;"));
        Assert.That(s, Does.Contain("if (holderNames.Count < HolderNameCap) holderNames[name] = sn;"),
            "a name past the cap is not stored (AS3 sets instance names at runtime)");
        Assert.That(s, Does.Not.Match(@"holderNames\[name\] = sn = "), "no unconditional store");
    }

    [Test]
    public void DummyAnchorAsksTheMemoBeforeTheMatricesAndStoresTheAnswer()
    {
        var s = Source("game/scripts/world/DummyAnchor.cs");
        int get = s.IndexOf("memo.TryGet(rows, name, rootName, at, out var r)", StringComparison.Ordinal);
        int find = s.IndexOf("DummyWorld.Find(", StringComparison.Ordinal);
        int set = s.IndexOf("memo.Set(rows, name, rootName, at, r);", StringComparison.Ordinal);
        Assert.That(get, Is.GreaterThan(-1), "the memo lookup");
        Assert.That(find, Is.GreaterThan(get), "the matrices are walked only after a memo miss");
        Assert.That(set, Is.GreaterThan(find), "and the answer, a miss included, is stored");
        Assert.That(s, Does.Contain("memos.GetValue(model"), "one memo per model");
    }
}
