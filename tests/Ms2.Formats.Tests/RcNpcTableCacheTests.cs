using System.Diagnostics;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// perf #1: one NpcTable per PackSet. Today every field enter builds one (game/net/RemoteLayer.cs:244-255), which reads the
/// 11,343-file per-file set again (NpcTable.cs:157-172, PerFileTable.cs:31-47). SkillTable.For (SkillTable.cs:111-118) is the
/// pattern. Expected API: NpcTable.For(PackSet), the same instance for the same set.
/// </summary>
public class RcNpcTableCacheTests
{
    [Ignore("NpcTable has no per-set cache")]
    [Test]
    public void OneNpcTablePerPackSet()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var sw = Stopwatch.StartNew();
        var a = RcLane.Static<NpcTable>("Ms2.Formats.World.NpcTable", "For", packs);
        sw.Stop();
        TestContext.Progress.WriteLine($"NpcTable.For: {sw.Elapsed.TotalMilliseconds:F0} ms, {a.Count} rows");
        var b = RcLane.Static<NpcTable>("Ms2.Formats.World.NpcTable", "For", packs);
        Assert.That(ReferenceEquals(a, b), Is.True, "the same pack set reads one table");
        Assert.That(a.Count, Is.GreaterThan(0));
    }
}
