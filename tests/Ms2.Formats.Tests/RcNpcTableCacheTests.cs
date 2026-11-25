using System.Diagnostics;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// perf #1: one NpcTable per PackSet and language, as the client's one CNpcDataTable (npcdata.h:385-392, LoadNpcCache
/// @0x1406ce4a0). Each field enter built one (RemoteLayer's npcs), as did each quest guide start and the replay, reading every
/// npc file again (GMS2: the per-file set under npc/). NpcTable.For gives the same instance for the same set and language.
/// The progress line gives what a field enter paid for its table before (a build) and after (a lookup).
/// </summary>
public class RcNpcTableCacheTests
{
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void OneNpcTablePerPackSet(string data)
    {
        var d = Environment.GetEnvironmentVariable(data);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{data} is not set");
        using var packs = new PackSet(d);
        // the field enter's table before: a build of its own (the first build also warms the pack reads, so the second is timed)
        _ = new NpcTable(packs);
        long bytes0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var built = new NpcTable(packs);
        sw.Stop();
        double buildMs = sw.Elapsed.TotalMilliseconds; long buildBytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        var a = NpcTable.For(packs, null);
        // after: the set's table looked up
        bytes0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        var b = NpcTable.For(packs, null);
        sw.Stop();
        long hitBytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        TestContext.Progress.WriteLine($"{data}: {a.Count} rows; a field enter's npc table: build {buildMs:F0} ms, {buildBytes / 1048576.0:F1} MB " +
            $"-> lookup {sw.Elapsed.TotalMilliseconds:F3} ms, {hitBytes} bytes");
        Assert.That(ReferenceEquals(a, b), Is.True, "the same pack set reads one table");
        Assert.That(a.Count, Is.EqualTo(built.Count).And.GreaterThan(0));
        Assert.That(ReferenceEquals(NpcTable.For(packs, "en"), a), Is.True, "no language is en");
        var kr = NpcTable.For(packs, "kr");
        Assert.That(ReferenceEquals(kr, a), Is.False, "another name language is another table");
        Assert.That(ReferenceEquals(NpcTable.For(packs, "kr"), kr), Is.True);
        Assert.That(hitBytes, Is.LessThan(1024), "a lookup builds nothing");
        // another set of the same folder is another table
        using var other = new PackSet(d);
        Assert.That(ReferenceEquals(NpcTable.For(other, null), a), Is.False);
    }
}
