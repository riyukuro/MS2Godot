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
}
