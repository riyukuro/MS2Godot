using Maple2.File.IO;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

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
        var rnd = new Random(11);
        int total = 0, skipped = 0;
        var files = Directory.GetFiles(data, "*.m2d", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty);
        foreach (var path in files)
        {
            var archive = Path.ChangeExtension(Path.GetRelativePath(data, path), null).Replace('\\', '/');
            using var reader = new M2dReader(path);
            var all = reader.Files.ToList();
            int take = Math.Min(all.Count, Math.Max(250, 12000 / files.Length));
            foreach (var entry in all.OrderBy(_ => rnd.Next()).Take(take))
            {
                var name = entry.Name.Replace('\\', '/');
                byte[] want;
                try { want = reader.GetBytes(entry); }
                catch (Exception) { skipped++; continue; }   // the package cannot read it either (no baseline to compare)
                Assert.That(packs.Read(archive, name), Is.EqualTo(want), $"{archive}: {name}");
                total++;
            }
        }
        TestContext.Out.WriteLine($"skipped {skipped}");
        TestContext.Out.WriteLine($"entries {total}, fast {PackSet.FastAesHits}, fallback {PackSet.FastAesMisses}");
        Assert.That(total, Is.GreaterThanOrEqualTo(5000));
        Assert.That(PackSet.FastAesHits, Is.GreaterThan(0));
        Assert.That(PackSet.FastAesMisses, Is.EqualTo(0));
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
