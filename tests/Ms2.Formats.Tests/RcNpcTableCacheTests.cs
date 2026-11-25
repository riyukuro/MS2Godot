using System.Diagnostics;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// perf #1: one NpcTable per PackSet and language, as the client's one CNpcDataTable (npcdata.h:385-392, LoadNpcCache
/// @0x1406ce4a0). Each field enter built one (RemoteLayer's npcs), as did each quest guide start and the replay, reading every
/// npc file again (GMS2: the per-file set under npc/). NpcTable.For gives the same instance for the same set and language.
/// The progress line gives what a field enter paid for its table before (a build) and after (a lookup), and what one table
/// keeps resident: the set keeps only the table of the language last asked, so a language change never leaves two.
/// NonParallelizable: the GC measures and the build seam are process-wide.
/// </summary>
[NonParallelizable]
public class RcNpcTableCacheTests
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference Ask(PackSet packs, string language) => new(NpcTable.For(packs, language));

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
        // the language change let the en table go: asking en again builds a new one
        var en2 = NpcTable.For(packs, null);
        Assert.That(ReferenceEquals(en2, a), Is.False, "the set still held the en table after the language changed");
        Assert.That(en2.Count, Is.EqualTo(a.Count));
        // and a table of another language is let go when en is asked again
        var de = Ask(packs, "de");
        Assert.That(ReferenceEquals(NpcTable.For(packs, null), en2), Is.False, "en was kept beside de");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.That(de.IsAlive, Is.False, "the set still held the de table after the language changed back");
        // one table resident (the reachable bytes it keeps)
        a = null; built = null;
        long live0 = GC.GetTotalMemory(true);
        var held = new NpcTable(packs);
        long resident = GC.GetTotalMemory(true) - live0;
        TestContext.Progress.WriteLine($"{data}: one npc table keeps {resident / 1048576.0:F1} MB resident");
        GC.KeepAlive(held);
        // another set of the same folder is another table
        using var other = new PackSet(d);
        Assert.That(ReferenceEquals(NpcTable.For(other, null), NpcTable.For(packs, null)), Is.False);
    }

    /// <summary>A build that throws is not kept: the next field enter builds again instead of throwing the same exception.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void AFailedBuildIsBuiltAgain(string data)
    {
        var d = Environment.GetEnvironmentVariable(data);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{data} is not set");
        using var packs = new PackSet(d);
        var build = NpcTable.Build;
        try
        {
            NpcTable.Build = (_, _) => throw new IOException("the pack read failed");
            Assert.Throws<IOException>(() => NpcTable.For(packs, null));
        }
        finally { NpcTable.Build = build; }
        var t = NpcTable.For(packs, null);
        Assert.That(t.Count, Is.GreaterThan(0));
        Assert.That(ReferenceEquals(NpcTable.For(packs, null), t), Is.True);
    }
}
