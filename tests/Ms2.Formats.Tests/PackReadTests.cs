using Maple2.File.IO;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

[NonParallelizable]   // reads the process-wide PackSet counters and runs its own parallel loop
public class PackReadTests
{
    /// <summary>PackSet.Read takes its own cheaper route to an entry's bytes; they must equal the package reader's.</summary>
    [Test]
    public void ReadMatchesThePackageReaderByteForByte()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        var rnd = new Random(7);
        int checkedEntries = 0;
        foreach (var archive in new[] { "Resource/Exported", "Resource/Model/Map", "Resource/Model/Character", "Resource/Image", "Resource/Gfx", "Xml", "Resource/Model/Effect", "Resource/Model/Textures" })
        {
            var path = Path.Combine(data, archive + ".m2d");
            if (!File.Exists(path)) continue;
            using var reader = new M2dReader(path);
            foreach (var entry in reader.Files.OrderBy(_ => rnd.Next()).Take(40))
            {
                var name = entry.Name.Replace('\\', '/');
                Assert.That(packs.Read(archive, name), Is.EqualTo(reader.GetBytes(entry)), $"{archive}: {name}");
                checkedEntries++;
            }
        }
        Assert.That(checkedEntries, Is.GreaterThan(0));
    }

    /// <summary>The platform-AES route against the package reader over 5000+ entries of every archive under the data folder.</summary>
    [Test]
    public void PlatformAesMatchesThePackageReaderOverEveryArchive()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        // the counters are static and other tests read through PackSet too: this test checks only what its own reads add
        // (the fixture is NonParallelizable, so no other fixture reads meanwhile)
        long hits0 = PackSet.FastAesHits, misses0 = PackSet.FastAesMisses, fallbacks0 = PackSet.Fallbacks;
        int total = 0, skipped = 0;
        var files = Directory.GetFiles(data, "*.m2d", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty);
        Array.Sort(files, StringComparer.Ordinal);
        // one task per archive: each has its own reader and its own seeded shuffle, so the sample does not depend on the schedule
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, files.Length, i =>
        {
            var path = files[i];
            var archive = Path.ChangeExtension(Path.GetRelativePath(data, path), null).Replace('\\', '/');
            var rnd = new Random(11 + i);
            using var reader = new M2dReader(path);
            var all = reader.Files.ToList();
            int take = Math.Min(all.Count, Math.Max(250, 12000 / files.Length));
            int done = 0, skip = 0;
            foreach (var entry in all.OrderBy(_ => rnd.Next()).Take(take))
            {
                var name = entry.Name.Replace('\\', '/');
                byte[] want;
                try { want = reader.GetBytes(entry); }
                catch (Exception) { skip++; continue; }   // the package cannot read it either (no baseline to compare)
                if (!packs.Read(archive, name).AsSpan().SequenceEqual(want)) { failures.Add($"{archive}: {name}"); break; }
                done++;
            }
            Interlocked.Add(ref total, done);
            Interlocked.Add(ref skipped, skip);
        });
        Assert.That(failures, Is.Empty, "entries whose bytes differ from the package reader's");
        TestContext.Out.WriteLine($"skipped {skipped}");
        long hits = PackSet.FastAesHits - hits0, misses = PackSet.FastAesMisses - misses0, fallbacks = PackSet.Fallbacks - fallbacks0;
        TestContext.Out.WriteLine($"entries {total}, fast {hits}, AES misses {misses}, package fallbacks {fallbacks}");
        Assert.That(total, Is.GreaterThanOrEqualTo(5000));
        Assert.That(hits, Is.GreaterThan(0));
        // every route off the fast path is counted (a null from the AES route for any reason, any read that ends in GetBytes)
        Assert.That(misses, Is.EqualTo(0));
        Assert.That(fallbacks, Is.EqualTo(0));
    }

    /// <summary>Archives opened side by side by Prefetch (and by reads from several threads at once) hold the same entries and bytes as opened one by one.</summary>
    [Test]
    public void ArchivesOpenedSideBySideReadTheSameAsOneByOne()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var names = new[] { "Resource/Exported", "Resource/Model/Map", "Resource/Model/Effect", "Resource/Model/Npc", "Resource/Image", "Xml" }
            .Where(n => File.Exists(Path.Combine(data, n + ".m2d"))).ToArray();
        Assert.That(names, Is.Not.Empty);
        var serial = new PackSet(data);
        var side = new PackSet(data);
        side.Prefetch(names);
        side.Prefetch(names);   // a second request while the first is still opening
        foreach (var n in names)
        {
            var want = serial.List(n).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var got = side.List(n).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.That(got, Is.EqualTo(want), n);
            var pick = want.Where((_, i) => i % Math.Max(1, want.Count / 12) == 0).Take(12);
            Parallel.ForEach(pick, e => Assert.That(side.Read(n, e), Is.EqualTo(serial.Read(n, e)), $"{n}: {e}"));
        }
    }

    [Test]
    public void PrefetchOfAMissingArchiveLeavesTheReadToReport()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Resource"));
        try
        {
            var packs = new PackSet(dir);
            packs.Prefetch("Resource/Nope", "Resource/Nope");
            Thread.Sleep(100);
            Assert.Throws<FileNotFoundException>(() => packs.Read("Resource/Nope", "a.txt"));
            Assert.Throws<FileNotFoundException>(() => packs.List("Resource/Nope"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
