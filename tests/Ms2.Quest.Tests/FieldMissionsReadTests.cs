using System.Reflection;
using System.Text;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>
/// FieldMissions.Load's cheaper reads give what the reads before them gave, on the data: the field mission marker tested on
/// the bytes picks the files the decoded text did, and the reserved word tables shared from the quest table are the ones
/// FieldMissions reads itself.
/// </summary>
public class FieldMissionsReadTests
{
    static PackSet packs;

    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return packs ??= new PackSet(d);
    }

    [Test]
    public void TheByteMarkerPicksTheFilesTheTextDid()
    {
        var p = Packs();
        var marker = Encoding.ASCII.GetBytes("questType=\"4\"");
        int files = 0, picked = 0;
        foreach (var entry in p.List("Xml").Where(n => n.StartsWith("quest/", StringComparison.OrdinalIgnoreCase)
                                                    && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = p.Read("Xml", entry);
            bool text = Encoding.UTF8.GetString(bytes).Contains("questType=\"4\"", StringComparison.Ordinal);
            Assert.That(bytes.AsSpan().IndexOf(marker) >= 0, Is.EqualTo(text), entry);
            files++;
            if (text) picked++;
        }
        TestContext.WriteLine($"quest files {files}, with field missions {picked}");
        Assert.That(files, Is.GreaterThan(0));
    }

    [Test]
    public void SharedTablesAreTheOnesItReads()
    {
        var p = Packs();
        var langs = new Ms2.Formats.World.GameStrings(p, "en").Languages;
        Func<string, bool> features = f => f != "Off";
        var quests = QuestTable.Load(p, langs, "en", features);
        var own = FieldMissions.Load(p, langs, "en", features);
        var shared = FieldMissions.Load(p, langs, "en", features, quests.Words);

        var tables = typeof(ReservedWords).GetField("tables", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var a = (Dictionary<int, Dictionary<long, string>>)tables.GetValue(own.Words)!;
        var b = (Dictionary<int, Dictionary<long, string>>)tables.GetValue(shared.Words)!;
        Assert.That(b.Keys, Is.EquivalentTo(a.Keys));
        foreach (var (t, d) in a)
        {
            Assert.That(b[t].Count, Is.EqualTo(d.Count), $"table {t}");
            foreach (var (id, text) in d)
                if (!b[t].TryGetValue(id, out var other) || other != text) Assert.Fail($"table {t} id {id}: \"{text}\" vs \"{other}\"");
        }
        Assert.That(shared.CountFormat, Is.EqualTo(own.CountFormat));
        Assert.That(own.CountFormat, Is.Not.Null.And.Not.Empty);

        // the count format as the document read found it (the first s_quest_desc_with_count key of the first language)
        string dom = null;
        foreach (var lang in langs)
        {
            string entry = $"string/{lang}/stringcommon.xml";
            if (!p.Exists("Xml", entry)) continue;
            var key = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(p.Read("Xml", entry)).TrimStart('\ufeff')).Descendants("key")
                .FirstOrDefault(k => (string)k.Attribute("id") == "s_quest_desc_with_count");
            if (key != null) { dom = ((string)key.Attribute("value"))?.Replace("\\n", "\n"); break; }
        }
        Assert.That(own.CountFormat, Is.EqualTo(dom));
    }
}
