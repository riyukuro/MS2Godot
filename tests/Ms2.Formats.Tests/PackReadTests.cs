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

    /// <summary>OpenRead's inflate is held to the entry's size, as Read's exact-size buffer and its one-byte overrun read are.</summary>
    [Test]
    public void TheStreamedInflateChecksTheEntrySize()
    {
        var text = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("<ms2><skill id=\"1\"/></ms2>", 500)));
        var packed = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(text);
        byte[] ReadAll(long size)
        {
            using var s = new PackSet.InflateStream(packed.ToArray(), size);
            var o = new MemoryStream();
            s.CopyTo(o, 777);
            return o.ToArray();
        }
        Assert.That(ReadAll(text.Length), Is.EqualTo(text));
        Assert.Throws<InvalidDataException>(() => ReadAll(text.Length + 1), "fewer bytes than the size");
        Assert.Throws<InvalidDataException>(() => ReadAll(text.Length - 1), "more bytes than the size");
    }

    /// <summary>The platform-AES route (and <see cref="PackSet.OpenRead"/>'s streamed inflate) against the package reader over
    /// 5000+ entries of every archive under the data folder.</summary>
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
        // archives whose index the client does not open (empty XmlDebug on KMS2_DEV) have nothing to read
        var files = Directory.GetFiles(data, "*.m2d", SearchOption.AllDirectories)
            .Where(f => PackIndex.Opens(Path.ChangeExtension(f, ".m2h"))).ToArray();
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
                var got = packs.Read(archive, name);
                if (got == null || !got.AsSpan().SequenceEqual(want)) { failures.Add($"{archive}: {name}"); break; }
                // the streamed form: the same bytes, read in small pieces
                using (var s = packs.OpenRead(archive, name))
                {
                    var streamed = new MemoryStream();
                    var piece = new byte[1000];
                    for (int n; (n = s.Read(piece, 0, piece.Length)) > 0;) streamed.Write(piece, 0, n);
                    if (!streamed.ToArray().AsSpan().SequenceEqual(want)) { failures.Add($"{archive}: {name} (stream)"); break; }
                }
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

    /// <summary>An entry the platform-AES route turns down before its in-place decode (not an AES flag, no key) still holds
    /// the bytes as read, so the package's decrypt runs on them without a second read from the archive file.</summary>
    [Test]
    public void AMissBeforeTheDecodeReadsTheEntryOnce()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        var name = packs.List("Xml").OrderBy(n => n, StringComparer.Ordinal)
            .First(n => packs.EntryOf("Xml", n) is { } e && ((uint)e.BufferFlag & 0xff000000) != 0xff000000 && ((uint)e.BufferFlag & 9) == 9
                && ((uint)e.BufferFlag & 0xee000000) == 0xee000000);
        var entry = packs.EntryOf("Xml", name);
        var flag = entry.BufferFlag;
        try
        {
            entry.BufferFlag = (Maple2.File.IO.Crypto.Common.Encryption)((uint)flag & ~0xee000000u);
            long misses0 = PackSet.FastAesMisses, reads0 = PackSet.EncodedReads;
            try { packs.Read("Xml", name); } catch (Exception) { }   // whatever the package's decrypt makes of the flag
            Assert.That((PackSet.FastAesMisses - misses0, PackSet.EncodedReads - reads0), Is.EqualTo((1L, 1L)), name);
        }
        finally { entry.BufferFlag = flag; }
    }

    /// <summary>A read costs the entry's encoded bytes and its output, not copies of them: the base64 decodes in place and
    /// the AES keystream is made a chunk at a time, so no array as large as the entry is made besides the read buffer and the
    /// result (the streamed read: the read buffer only).</summary>
    [Test]
    public void ALargeEntryReadAllocatesTheEncodedBytesAndTheOutputOnly()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        int checkedEntries = 0;
        var piece = new byte[4096];
        foreach (var archive in new[] { "Xml", "Resource/Exported", "Resource/Model/Map" })
        {
            var path = Path.Combine(data, archive + ".m2d");
            if (!File.Exists(path)) continue;
            using var reader = new M2dReader(path);
            var big = reader.Files.Where(f => f.FileHeader.EncodedFileSize > 256 * 1024 && f.FileHeader.FileSize < 64 << 20)
                .OrderByDescending(f => f.FileHeader.EncodedFileSize).Take(4).ToList();
            foreach (var entry in big)
            {
                var name = entry.Name.Replace('\\', '/');
                long encoded = entry.FileHeader.EncodedFileSize, size = (long)entry.FileHeader.FileSize;
                packs.Read(archive, name);   // the archive's index is opened once, not counted
                long before = GC.GetAllocatedBytesForCurrentThread();
                var got = packs.Read(archive, name);
                long read = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(got, Has.Length.EqualTo(size), name);
                Assert.That(read, Is.LessThan(encoded + size + 64 * 1024), $"{archive}: {name} read ({encoded} encoded, {size} out)");
                before = GC.GetAllocatedBytesForCurrentThread();
                long streamed = 0;
                using (var s = packs.OpenRead(archive, name))
                    for (int n; (n = s.Read(piece, 0, piece.Length)) > 0;) streamed += n;
                long open = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(streamed, Is.EqualTo(size), name);
                Assert.That(open, Is.LessThan(encoded + 64 * 1024), $"{archive}: {name} stream ({encoded} encoded)");
                checkedEntries++;
            }
        }
        Assert.That(checkedEntries, Is.GreaterThan(0));
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
