using System.Text;
using Ms2.Formats.Cache;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The asset-web catalog's per-install file resolves every llid as the parse of llid.nt and relpath.nt does: the first
/// uuid of an llid, the first relpath of a uuid, nothing for an llid whose uuid has no relpath, the llid without regard
/// to case, with or without "urn:llid:".
/// </summary>
public class AssetIndexCacheTests
{
    static byte[] Nt(params (string uuid, string pred, string value)[] lines) =>
        Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => $"<urn:uuid:{l.uuid}> <http://emergent.net/aweb/1.0/{l.pred}> \"{l.value}\".\n")));

    const string U1 = "11111111-aaaa-4bbb-8ccc-000000000001", U2 = "11111111-aaaa-4bbb-8ccc-000000000002",
                 U3 = "11111111-aaaa-4bbb-8ccc-000000000003", U4 = "11111111-aaaa-4bbb-8ccc-000000000004";
    const string L1 = "2bb8afc1-0000-0000-0000-000000000000", L2 = "F69DC6C9-0000-0000-0000-000000000000",
                 L3 = "0a7ec2e6-0000-0000-0000-000000000000";

    static AssetIndex Synthetic(out AssetIndex.Table table)
    {
        var llid = Nt((U1, "llid", L1), (U2, "llid", L2), (U3, "llid", L1),   // L1 again: the first uuid stays
                      (U4, "llid", L3),                                       // U4 has no relpath
                      (U2, "llid", "not-a-guid"), (U1, "llid", " " + L3));    // the other forms
        var relpath = Nt((U1, "relpath", "/Model/Map/Tria/Cube/a.nif"), (U2, "relpath", "/Model/Npc/Ü/b.nif"),
                         (U2, "relpath", "/Model/Npc/later.nif"), (U3, "relpath", "/Model/Map/c.nif"));
        table = AssetIndex.Parse(llid, relpath);
        return new AssetIndex(AssetIndex.Table.Read(table.Body));
    }

    [Test]
    public void ResolvesAsTheParse()
    {
        var ix = Synthetic(out var table);
        Assert.That(table.Count, Is.EqualTo(4));
        Assert.That(ix.RelPath("urn:llid:" + L1), Is.EqualTo("/Model/Map/Tria/Cube/a.nif"));
        Assert.That(ix.RelPath(L1.ToUpperInvariant()), Is.EqualTo("/Model/Map/Tria/Cube/a.nif"));
        Assert.That(ix.RelPath(L2.ToLowerInvariant()), Is.EqualTo("/Model/Npc/Ü/b.nif"));
        Assert.That(ix.RelPath(L3), Is.Null);
        Assert.That(ix.RelPath("NOT-A-GUID"), Is.EqualTo("/Model/Npc/Ü/b.nif"));
        Assert.That(ix.RelPath(" " + L3), Is.EqualTo("/Model/Map/Tria/Cube/a.nif"));
        Assert.That(ix.RelPath(L3 + " "), Is.Null);
        Assert.That(ix.RelPath("2bb8afc100000000000000000000000000"), Is.Null);
        Assert.That(ix.RelPath(""), Is.Null);
        Assert.That(ix.Resolve(L1), Is.EqualTo(("Resource/Model/Map", "tria/cube/a.nif")));
    }

    [Test]
    public void ADamagedBodyIsNotRead()
    {
        Synthetic(out var table);
        Assert.Throws<InvalidDataException>(() => AssetIndex.Table.Read(table.Body[..10]));
        var big = (byte[])table.Body.Clone();
        big[0] = 0xff; big[1] = 0xff; big[2] = 0xff; big[3] = 0x7f;
        Assert.Throws<InvalidDataException>(() => AssetIndex.Table.Read(big));
    }

    /// <summary>Every llid of the client's catalog, against the dictionaries the parse used to build.</summary>
    [Test]
    public void EveryLlidOfTheClientResolvesAsBefore()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = PackSet.Shared(data);
        var llidNt = packs.Read(AssetIndex.Archive, "llid.nt");
        var relNt = packs.Read(AssetIndex.Archive, "relpath.nt");
        var llidToUuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var uuidToRelpath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AssetIndex.Load(llidNt, (u, v) => llidToUuid.TryAdd(v, u));
        AssetIndex.Load(relNt, (u, v) => uuidToRelpath.TryAdd(u, v));
        var ix = new AssetIndex(AssetIndex.Table.Read(AssetIndex.Parse(llidNt, relNt).Body));
        int resolved = 0, none = 0, wrong = 0;
        foreach (var (llid, uuid) in llidToUuid)
        {
            var want = uuidToRelpath.TryGetValue(uuid, out var p) ? p : null;
            if (want != null) resolved++; else none++;
            if (ix.RelPath(llid) != want || ix.RelPath("urn:llid:" + llid.ToUpperInvariant()) != want) wrong++;
        }
        TestContext.Progress.WriteLine($"asset-web {data}: {llidToUuid.Count} llids, {resolved} resolve, {none} resolve to nothing, {wrong} differ");
        Assert.That(wrong, Is.Zero);
        Assert.That(resolved, Is.GreaterThan(0));
    }

    /// <summary>The setup writes the file; a session reads it and parses nothing.</summary>
    [Test, NonParallelizable]   // sets the process-wide hooks
    public void TheSetupWritesTheFileASessionReads()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var dir = Path.Combine(Path.GetTempPath(), "ms2-assetweb-" + Guid.NewGuid().ToString("N"));
        var (of, hashOf, logged) = (CacheStore.Of, CacheStore.HashOf, CacheStore.Logged);
        try
        {
            var store = new CacheStore(dir);
            var outcomes = new List<CacheStore.Outcome>();
            CacheStore.Of = _ => store;
            CacheStore.HashOf = f => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(f))).ToLowerInvariant();
            CacheStore.Logged = o => { lock (outcomes) outcomes.Add(o); };
            var packs = PackSet.Shared(data);
            Assert.That(AssetIndex.BuildCache(packs, store), Is.True);
            Assert.That(AssetIndex.BuildCache(packs, store), Is.False);
            var ix = new AssetIndex(packs);
            Assert.That(outcomes.Single(o => o.Piece == "asset-web").Source, Is.EqualTo("cache"));
            var first = AssetIndex.Parse(packs.Read(AssetIndex.Archive, "llid.nt"), packs.Read(AssetIndex.Archive, "relpath.nt"));
            Assert.That(ix.RelPath("urn:llid:2bb8afc1-0000-0000-0000-000000000000"),
                        Is.EqualTo(new AssetIndex(first).RelPath("urn:llid:2bb8afc1-0000-0000-0000-000000000000")));
        }
        finally
        {
            (CacheStore.Of, CacheStore.HashOf, CacheStore.Logged) = (of, hashOf, logged);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
