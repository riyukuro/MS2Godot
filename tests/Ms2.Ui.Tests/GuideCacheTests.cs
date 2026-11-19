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
        var keepOf = GuideTable.CacheOf;
        var keepLog = GuideTable.Log;
        var keepMode = Environment.GetEnvironmentVariable("MS2_UICACHE");
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
            Assert.That(cached.Events.Zip(parsed.Events).Count(p => XNode.DeepEquals(p.First, p.Second)), Is.EqualTo(750));

            // the manager loads the cached table as the parsed one
            var a = new GuideManager(new GuideTests.World());
            var b = new GuideManager(new GuideTests.World());
            a.Load(parsed);
            b.Load(cached);
            Assert.That(b.Entities.Select(e => e.Id), Is.EqualTo(a.Entities.Select(e => e.Id)));
            Assert.That(b.Entities.Count(), Is.EqualTo(750));
            Assert.That(b.LoadErrors, Is.EqualTo(a.LoadErrors));

            // Load: the cache when it is there; off parses; check parses and logs a difference
            var logged = new List<string>();
            GuideTable.CacheOf = _ => path;
            GuideTable.Log = logged.Add;
            Environment.SetEnvironmentVariable("MS2_UICACHE", null);
            Assert.That(GuideCache.FirstDifference(parsed, GuideTable.Load(packs)), Is.Null);
            Environment.SetEnvironmentVariable("MS2_UICACHE", "off");
            Assert.That(GuideCache.FirstDifference(parsed, GuideTable.Load(packs)), Is.Null);
            Environment.SetEnvironmentVariable("MS2_UICACHE", "check");
            GuideTable.Load(packs);
            Assert.That(logged, Is.Empty);

            var changed = GuideCache.Read(path);
            changed.Files.First(f => f.Events.Count > 0).Events[0].SetAttributeValue("id", "0");
            GuideCache.Write(changed, path);
            var fromCheck = GuideTable.Load(packs);
            Assert.That(logged, Has.Count.EqualTo(1).And.Some.Contains("differs"));
            Assert.That(GuideCache.FirstDifference(parsed, fromCheck), Is.Null);

            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Environment.SetEnvironmentVariable("MS2_UICACHE", null);
            Assert.That(GuideCache.FirstDifference(parsed, GuideTable.Load(packs)), Is.Null); // a bad cache falls back to the parse
        }
        finally
        {
            Environment.SetEnvironmentVariable("MS2_UICACHE", keepMode);
            GuideTable.CacheOf = keepOf;
            GuideTable.Log = keepLog;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
