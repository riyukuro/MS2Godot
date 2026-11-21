using System.Buffers.Binary;
using Ms2.Formats.Cache;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A client install can hold archives whose index the client does not open (an empty XmlDebug.m2h/.m2d pair): the
/// setup's table cache build and a session's open list them as empty, as the client does, instead of failing on the
/// package reader's map of an empty file. And a cache file left empty or cut short is built again, never read.
/// </summary>
[NonParallelizable]   // sets process-wide hooks the product reads
public class EmptyArchiveTests
{
    static string NewDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-emptypack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Resource"));
        return dir;
    }

    // a version-3 index header (magic 'OS2F', then CPackStreamVer3Data) with the given list size
    static byte[] Ver3Header(ulong listSize)
    {
        var h = new byte[4 + 0x38];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0x4632534f);
        BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(4 + 4 + 4 + 8 + 8), listSize);
        return h;
    }

    [Test]
    public void IndexesTheClientDoesNotOpenListNothing()
    {
        var data = NewDataDir();
        try
        {
            File.WriteAllBytes(Path.Combine(data, "XmlDebug.m2h"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(data, "XmlDebug.m2d"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(data, "Short.m2h"), new byte[] { 0x4f, 0x53 });               // shorter than the magic
            File.WriteAllBytes(Path.Combine(data, "Short.m2d"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(data, "Magic.m2h"), new byte[64]);                             // no known magic
            File.WriteAllBytes(Path.Combine(data, "Magic.m2d"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(data, "Cut.m2h"), Ver3Header(100)[..40]);                     // header cut short
            File.WriteAllBytes(Path.Combine(data, "Cut.m2d"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(data, "NoList.m2h"), Ver3Header(0));                          // empty file list
            File.WriteAllBytes(Path.Combine(data, "NoList.m2d"), Array.Empty<byte>());
            var names = new[] { "Cut", "Magic", "NoList", "Short", "XmlDebug" };
            Assert.That(PackIndex.Archives(data), Is.EqualTo(names));
            foreach (var a in names) Assert.That(PackIndex.Opens(Path.Combine(data, a + ".m2h")), Is.False, a);
            Assert.That(PackIndex.Opens(Path.Combine(data, "Missing.m2h")), Is.False);
            Assert.That(PackIndex.Opens(WriteTemp(data, Ver3Header(1))), Is.True, "a whole header with a list");

            // the setup's build (DataCache.Build) and a session's open, with and without the store
            var store = new CacheStore(Path.Combine(data, "store"));
            var (of, hashOf) = (CacheStore.Of, CacheStore.HashOf);
            using var packs = new PackSet(data);
            try
            {
                CacheStore.Of = p => p == packs ? store : null;
                CacheStore.HashOf = p => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
                foreach (var a in names)
                {
                    Assert.DoesNotThrow(() => PackIndex.BuildCache(a, Path.Combine(data, a + ".m2d"), store), a);
                    Assert.That(packs.List(a), Is.Empty, a);
                    Assert.That(packs.Exists(a, "table/itemname.xml"), Is.False, a);
                }
            }
            finally { (CacheStore.Of, CacheStore.HashOf) = (of, hashOf); }
            using var plain = new PackSet(data);
            foreach (var a in names) Assert.That(plain.List(a), Is.Empty, a);
        }
        finally { Directory.Delete(data, true); }
    }

    static string WriteTemp(string dir, byte[] bytes)
    {
        var f = Path.Combine(dir, "Whole.m2h");
        File.WriteAllBytes(f, bytes);
        return f;
    }

    [Test]
    public void EmptyOrCutCacheFilesAreBuiltAgain()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-cutcache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CacheStore(dir);
            var key = CacheStore.Key("piece", "config", "build");
            var path = store.PathOf("piece", key);
            Directory.CreateDirectory(dir);
            byte[] body = { 1, 2, 3, 4, 5, 6, 7, 8 };
            int parses = 0;
            byte[] parse() { parses++; return body; }
            CacheFile.Write(path, key, body);
            var whole = File.ReadAllBytes(path);
            foreach (var seeded in new[] { Array.Empty<byte>(), whole[..(whole.Length - 3)], whole[..10] })
                foreach (var check in new[] { false, true })
                {
                    File.WriteAllBytes(path, seeded);
                    var was = CacheStore.CheckBodies;
                    CacheStore.CheckBodies = check;
                    try { Assert.That(store.Ensure("piece", "config", "build", parse, b => b), Is.True, $"{seeded.Length} bytes, bodies {check}"); }
                    finally { CacheStore.CheckBodies = was; }
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(whole), "written whole again");
                    File.WriteAllBytes(path, seeded);
                    var got = store.Get("piece", "config", "build", parse, b => b, b => b, (a, b) => null, out var outcome);
                    Assert.That(got, Is.EqualTo(body));
                    Assert.That(outcome.Source, Is.EqualTo("parse"), "a cut file is never read");
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(whole));
                }
            Assert.That(parses, Is.EqualTo(12));
            Assert.That(Directory.GetFiles(dir), Is.EqualTo(new[] { path }), "no temporary file is left");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
