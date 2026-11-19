using System.Diagnostics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The per-install guide cache gives the same tables as the XML.</summary>
[NonParallelizable]
public class GuideCacheTests
{
    static string TempDir() => Path.Combine(Path.GetTempPath(), "ms2-guide-cache-" + Guid.NewGuid().ToString("N"));

    static GuideTable Sample()
    {
        var t = new GuideTable();
        t.BadFiles.Add("table/bad.xml");
        var doc = XElement.Parse("<ms2><event id='7' a='x &amp; y'><open><condition name='Trigger' value='ShopOpen,7'/></open>"
                                 + "<action>text<![CDATA[<raw>]]><!-- note --><c/></action></event><event id='8'/></ms2>",
                                 LoadOptions.None);
        t.Files.Add(new GuideTableFile("table/a.xml", doc.Elements("event").ToList()));
        t.Files.Add(new GuideTableFile("table/b.xml", new List<XElement>()));
        return t;
    }

    [Test]
    public void WriteThenReadGivesTheSameTable()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, GuideCache.FileName("test"));
        try
        {
            var t = Sample();
            GuideCache.Write(t, path);
            Assert.That(File.Exists(path + ".tmp"), Is.False);
            var back = GuideCache.Read(path);
            Assert.That(back, Is.Not.Null);
            Assert.That(GuideCache.FirstDifference(t, back), Is.Null);
            Assert.That(back.Files[0].Events[0].Element("action").Nodes().Select(n => n.NodeType),
                        Is.EqualTo(new[] { System.Xml.XmlNodeType.Text, System.Xml.XmlNodeType.CDATA, System.Xml.XmlNodeType.Comment, System.Xml.XmlNodeType.Element }));

            // a changed attribute is a difference
            back.Files[0].Events[0].SetAttributeValue("a", "z");
            Assert.That(GuideCache.FirstDifference(t, back), Does.Contain("id 7"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void BadFilesReadAsNull()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, GuideCache.FileName("test"));
        try
        {
            Assert.That(GuideCache.Read(path), Is.Null);
            Assert.That(GuideCache.Read(null), Is.Null);
            GuideCache.Write(Sample(), path);
            var bytes = File.ReadAllBytes(path);

            File.WriteAllBytes(path, bytes[..^6]);                 // cut short
            Assert.That(GuideCache.Read(path), Is.Null);
            File.WriteAllBytes(path, bytes.Concat(new byte[] { 0 }).ToArray()); // trailing byte
            Assert.That(GuideCache.Read(path), Is.Null);
            var v = (byte[])bytes.Clone(); v[4]++;                 // other version
            File.WriteAllBytes(path, v);
            Assert.That(GuideCache.Read(path), Is.Null);
            var m = (byte[])bytes.Clone(); m[^1] ^= 0xFF;          // end mark
            File.WriteAllBytes(path, m);
            Assert.That(GuideCache.Read(path), Is.Null);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void CacheMatchesTheXml()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var dir = TempDir();
        var path = Path.Combine(dir, GuideCache.FileName("test"));
        try
        {
            using var packs = new PackSet(d);
            var sw = Stopwatch.StartNew();
            GuideCache.Build(packs, path);
            TestContext.Progress.WriteLine($"guide cache: built in {sw.Elapsed.TotalSeconds:F2} s, {new FileInfo(path).Length / 1024} KB");

            sw.Restart();
            var parsed = GuideTable.Parse(packs);
            double parseMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var cached = GuideCache.Read(path);
            TestContext.Progress.WriteLine($"guide cache: parse {parseMs:F1} ms, cache read {sw.Elapsed.TotalMilliseconds:F1} ms");

            Assert.That(cached, Is.Not.Null);
            Assert.That(GuideCache.FirstDifference(parsed, cached), Is.Null);
            Assert.That(cached.Events.Count(), Is.EqualTo(parsed.Events.Count()), "the cache holds every parsed event");

            // the manager loads the cached table as the parsed one
            var a = new GuideManager(new GuideTests.World());
            var b = new GuideManager(new GuideTests.World());
            a.Load(parsed);
            b.Load(cached);
            Assert.That(b.Entities.Select(e => e.Id), Is.EqualTo(a.Entities.Select(e => e.Id)));
            Assert.That(b.EntityCount, Is.EqualTo(a.EntityCount));
            Assert.That(b.LoadErrors, Is.EqualTo(a.LoadErrors));

            // a bad body reads as null and the load falls back to the parse
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.That(GuideCache.Read(path), Is.Null);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    /// <summary>The store's flow over the guide pieces: built at setup (once), read from the cache, another language
    /// another file, =check reads both and reports, =off parses (the GameTables cache test's shape).</summary>
    [Test]
    public void TheStoreBuildsReadsAndChecksTheGuidePieces()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var dir = Path.Combine(Path.GetTempPath(), "ms2-guide-store-" + Guid.NewGuid().ToString("N"));
        var store = new Ms2.Formats.Cache.CacheStore(dir);
        var outcomes = new List<Ms2.Formats.Cache.CacheStore.Outcome>();
        var (of, logged) = (Ms2.Formats.Cache.CacheStore.Of, Ms2.Formats.Cache.CacheStore.Logged);
        string keepMode = Environment.GetEnvironmentVariable("MS2_UICACHE");
        try
        {
            using var packs = new PackSet(d);
            Ms2.Formats.Cache.CacheStore.Of = p => p == packs ? store : null;
            Ms2.Formats.Cache.CacheStore.Logged = outcomes.Add;

            // built at setup, once; another language is another file
            Assert.That(GuideTable.BuildCache(packs, store), Is.True);
            Assert.That(GuideTable.BuildCache(packs, store), Is.False, "built once");
            Assert.That(GuideStrings.BuildCache(packs, "en", store), Is.True);
            Assert.That(GuideStrings.BuildCache(packs, "en", store), Is.False, "built once");
            Assert.That(GuideStrings.BuildCache(packs, "kr", store), Is.True, "another language another file");

            outcomes.Clear();
            var cachedTable = GuideTable.Load(packs);
            var cachedStrings = GuideStrings.Load(packs, "en");
            Assert.That(outcomes.Select(o => (o.Piece, o.Source)),
                Is.EqualTo(new[] { ("guide", "cache"), ("guidestrings", "cache") }));
            var parsedTable = GuideTable.Parse(packs);
            var parsedStrings = GuideStrings.Parse(packs, "en");
            Assert.That(GuideCache.FirstDifference(parsedTable, cachedTable), Is.Null);
            Assert.That(GuideCache.FirstStringsDifference(parsedStrings, cachedStrings), Is.Null);
            Assert.That(cachedStrings.Text(420), Is.Empty, "the SurvivalTutorial key is skipped either way");

            // =check reads both and reports the first difference; the table stays the cache's
            Environment.SetEnvironmentVariable("MS2_UICACHE", "check");
            outcomes.Clear();
            Assert.That(GuideCache.FirstDifference(parsedTable, GuideTable.Load(packs)), Is.Null);
            Assert.That(outcomes.All(o => o.Source == "check" && o.Diff == null), Is.True);

            // a damaged file reads as nothing (its checksum fails in CacheFile) and the piece is parsed — the cache is
            // never why a table fails — and the parse rewrites it, so the next load reads it again
            var file = Directory.GetFiles(store.Dir, "guide-*.bin").Single();
            var body = File.ReadAllBytes(file);
            body[body.Length / 2] ^= 0xFF;
            File.WriteAllBytes(file, body);
            outcomes.Clear();
            Assert.That(GuideCache.FirstDifference(parsedTable, GuideTable.Load(packs)), Is.Null);
            Assert.That(outcomes.Single().Source, Is.EqualTo("parse"));
            Assert.That(outcomes.Single().Diff, Is.Null, "the damaged file is quietly not used");
            outcomes.Clear();
            Assert.That(GuideCache.FirstDifference(parsedTable, GuideTable.Load(packs)), Is.Null);
            Assert.That(outcomes.Single().Source, Is.EqualTo("check"), "the rewritten file is read again (check compares it)");
            Assert.That(outcomes.Single().Diff, Is.Null);

            Environment.SetEnvironmentVariable("MS2_UICACHE", "off");
            outcomes.Clear();
            Assert.That(GuideCache.FirstDifference(parsedTable, GuideTable.Load(packs)), Is.Null);
            Assert.That(outcomes.All(o => o.Source == "off"), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MS2_UICACHE", keepMode);
            (Ms2.Formats.Cache.CacheStore.Of, Ms2.Formats.Cache.CacheStore.Logged) = (of, logged);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
