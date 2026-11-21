using Maple2.File.IO;
using Ms2.Formats.Cache;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>An archive opened from its index cache file lists and reads what the package reader's open does.</summary>
[NonParallelizable]   // sets process-wide hooks the product reads
public class PackIndexTests
{
    [Test]
    public void CachedIndexEqualsThePackageReaders()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var dir = Path.Combine(Path.GetTempPath(), "ms2-index-" + Guid.NewGuid().ToString("N"));
        var store = new CacheStore(dir);
        var outcomes = new List<CacheStore.Outcome>();
        var (of, logged, hashOf) = (CacheStore.Of, CacheStore.Logged, CacheStore.HashOf);
        var archives = PackIndex.Archives(data).ToList();
        Assert.That(archives, Does.Contain("Xml").And.Contain("Resource/Gfx"));
        using var cachedPacks = new PackSet(data);
        try
        {
            CacheStore.Of = p => p == cachedPacks ? store : null;
            CacheStore.Logged = o => { lock (outcomes) outcomes.Add(o); };
            CacheStore.HashOf = p => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
            foreach (var a in archives) Assert.That(PackIndex.BuildCache(a, Path.Combine(data, a + ".m2d"), store), Is.True, a);
            Assert.That(PackIndex.BuildCache("Xml", Path.Combine(data, "Xml.m2d"), store), Is.False, "built once");
            var rnd = new Random(5);
            foreach (var a in archives)
            {
                using var reader = new M2dReader(Path.Combine(data, a + ".m2d"));
                var names = reader.Files.Select(f => f.Name.Replace('\\', '/')).ToList();
                // the name table as the reader's open built it (the first spelling of a name keeps its place)
                var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Count; i++) byName[names[i]] = i;
                Assert.That(cachedPacks.List(a), Is.EqualTo(byName.Keys), a);
                foreach (var f in reader.Files.OrderBy(_ => rnd.Next()).Take(25))
                {
                    var n = f.Name.Replace('\\', '/');
                    Assert.That(cachedPacks.Exists(a, n), Is.True, $"{a}: {n}");
                    Assert.That(cachedPacks.FindByFileName(a, Path.GetFileName(n)),
                        Is.EqualTo(names.First(m => string.Equals(Path.GetFileName(m), Path.GetFileName(n), StringComparison.OrdinalIgnoreCase))), $"{a}: {n}");
                    if (f.FileHeader.FileSize > 0 && f.FileHeader.FileSize < 4 << 20 && ((uint)f.FileHeader.BufferFlag & 0xff000000) != 0xff000000)
                        Assert.That(cachedPacks.Read(a, n), Is.EqualTo(reader.GetBytes(f)), $"{a}: {n}");
                }
            }
            Assert.That(outcomes.Select(o => o.Source).Distinct(), Is.EqualTo(new[] { "cache" }));
            Assert.That(outcomes.Count, Is.EqualTo(archives.Count));
            // the check mode compares the cached list with the reader's entry by entry
            var mode = Environment.GetEnvironmentVariable("MS2_UICACHE");
            Environment.SetEnvironmentVariable("MS2_UICACHE", "check");
            try
            {
                outcomes.Clear();
                using var checkPacks = new PackSet(data);
                CacheStore.Of = p => p == checkPacks ? store : null;
                foreach (var a in archives) checkPacks.List(a);
                Assert.That(outcomes.Select(o => (o.Source, o.Diff)).Distinct(), Is.EqualTo(new[] { ("check", (string)null) }));
            }
            finally { Environment.SetEnvironmentVariable("MS2_UICACHE", mode); }
            // a damaged file reads as missing: the archive opens with the reader and the file is written again
            var xml = Directory.GetFiles(dir, "index-xml-*.bin").Single();
            var bytes = File.ReadAllBytes(xml);
            bytes[^7] ^= 0x5a;
            File.WriteAllBytes(xml, bytes);
            outcomes.Clear();
            using var again = new PackSet(data);
            CacheStore.Of = p => p == again ? store : null;
            Assert.That(again.List("Xml").Count(), Is.EqualTo(cachedPacks.List("Xml").Count()));
            Assert.That(outcomes.Single().Source, Is.EqualTo("parse"));
            Assert.That(CacheFile.Read(xml, CacheFile.KeyOf(xml)), Is.Not.Null, "rewritten whole");
        }
        finally
        {
            (CacheStore.Of, CacheStore.Logged, CacheStore.HashOf) = (of, logged, hashOf);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
