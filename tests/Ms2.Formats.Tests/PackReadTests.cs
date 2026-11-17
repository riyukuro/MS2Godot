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
}
