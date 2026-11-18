using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>
/// The streamed reads of the reserved word tables and of LoadBold's item, npc and dictionary files give what the document
/// reads (the code before them, kept here as the reference) gave: on synthetic files and on every table of the data.
/// </summary>
public class ReservedWordsReadTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;

    static T Field<T>(ReservedWords w, string name) => (T)typeof(ReservedWords).GetField(name, Private)!.GetValue(w)!;

    static Dictionary<int, Dictionary<long, string>> Tables(ReservedWords w) => Field<Dictionary<int, Dictionary<long, string>>>(w, "tables");

    static void AssertSameTables(ReservedWords dom, ReservedWords rows)
    {
        var a = Tables(dom); var b = Tables(rows);
        Assert.That(b.Keys, Is.EquivalentTo(a.Keys));
        foreach (var (t, d) in a)
        {
            // key by key (NUnit's EquivalentTo is quadratic on the 100k-row tables)
            Assert.That(b[t].Count, Is.EqualTo(d.Count), $"table {t}");
            foreach (var (id, text) in d)
                if (!b[t].TryGetValue(id, out var other) || other != text) Assert.Fail($"table {t} id {id}: \"{text}\" vs \"{other}\"");
        }
    }

    static bool Avail(string loc, string feature) =>
        (string.IsNullOrEmpty(loc) || loc.Equals("NA", StringComparison.OrdinalIgnoreCase)) && feature != "Off";

    static bool AvailX(XElement e) => Avail((string)e.Attribute("locale"), (string)e.Attribute("feature"));

    [Test]
    public void ARowTableMatchesTheDocumentTable()
    {
        string[] files =
        {
            "﻿<?xml version=\"1.0\" encoding=\"utf-8\"?><ms2><key id='1' name='one\\nline'/><key id='00002' name='two'/>"
                + "<key id='3' name='kr only' locale='KR'/><key id='4' name='off' feature='Off'/><key id='5' name='na' locale='na'/>"
                + "<key id='1' name='one again'/><other id='6' name='not a key'/><key id='7'/><group><key id='8' name='nested'/></group>"
                + "<key id='4294967297' name='wraps'/></ms2>",
            "<ms2><key id='2' name='second language'/><key id='9' name='filled'/></ms2>",
        };
        foreach (bool fillOnly in new[] { false, true })
        {
            var dom = new ReservedWords(); var rows = new ReservedWords();
            foreach (var f in files)
            {
                dom.AddTable(ReservedWords.NpcName, XDocument.Parse(f.TrimStart('﻿')), AvailX, fillOnly);
                rows.AddTable(ReservedWords.NpcName, Encoding.UTF8.GetBytes(f), Avail, fillOnly);
            }
            AssertSameTables(dom, rows);
        }
        var w = new ReservedWords();
        w.AddTable(ReservedWords.NpcName, Encoding.UTF8.GetBytes(files[0]), null);
        Assert.That(w.Get(ReservedWords.NpcName, 1), Is.EqualTo("wraps"), "ids are kept as uint: 4294967297 is 1, the last row of id 1");
        Assert.That(w.Get(ReservedWords.NpcName, 3), Is.EqualTo("kr only"), "no availability test without one");
    }

    // ---- LoadBold's row files ---------------------------------------------------------------------------------------

    static bool Rows(byte[] xml, string row, Action<System.Xml.XmlReader> start, Action<System.Xml.XmlReader> env, Action end) =>
        (bool)typeof(ReservedWords).GetMethod("Rows", Private)!.Invoke(null, new object[] { xml, row, start, env, end })!;

    static XElement Pick(XElement e) => e.Elements("environment").Where(AvailX).LastOrDefault();

    // the document reads as they were (LoadBold before the row reads)
    static List<long> DomItems(string xml)
    {
        XDocument d;
        try { d = XDocument.Parse(xml); } catch (System.Xml.XmlException) { return new(); }
        return d.Root!.Elements("item").Where(i => Pick(i) != null).Select(i => FieldMissions.Long((string)i.Attribute("id"))).ToList();
    }

    static List<(long, int)> DomNpcs(string xml)
    {
        XDocument d;
        try { d = XDocument.Parse(xml); } catch (System.Xml.XmlException) { return new(); }
        var l = new List<(long, int)>();
        foreach (var n in d.Root!.Elements("npc"))
            if (Pick(n) is { } env) l.Add((FieldMissions.Long((string)n.Attribute("id")), FieldMissions.Int((string)env.Attribute("friendly"))));
        return l;
    }

    static List<long> RowItems(byte[] xml)
    {
        var ids = new List<long>(); string id = null; bool picked = false;
        return Rows(xml, "item", r => { id = r.GetAttribute("id"); picked = false; }, r => picked |= Avail(r.GetAttribute("locale"), r.GetAttribute("feature")),
                    () => { if (picked) ids.Add(FieldMissions.Long(id)); }) ? ids : new();
    }

    static List<(long, int)> RowNpcs(byte[] xml)
    {
        var l = new List<(long, int)>(); string id = null, friendly = null; bool picked = false;
        return Rows(xml, "npc", r => { id = r.GetAttribute("id"); picked = false; },
                    r => { if (Avail(r.GetAttribute("locale"), r.GetAttribute("feature"))) { picked = true; friendly = r.GetAttribute("friendly"); } },
                    () => { if (picked) l.Add((FieldMissions.Long(id), FieldMissions.Int(friendly))); }) ? l : new();
    }

    [TestCase("<ms2><item id='1'><environment/></item><item id='2'><environment locale='KR'/></item><item id='3'/>"
              + "<item id='4'><environment feature='Off'/><environment locale='NA'/></item><item id='5'><x><environment/></x></item>"
              + "<item id='6'/><item id='6'><environment/></item><other><item id='7'><environment/></item></other></ms2>")]
    [TestCase("<ms2><item id='1'><environment/></item><item id='2'><environment></item></ms2>")]     // malformed: nothing kept
    [TestCase("<item id='1'><environment/></item>")]                                                    // the root is not a row
    public void ItemRowsMatchTheDocument(string xml) =>
        Assert.That(RowItems(Encoding.UTF8.GetBytes(xml)), Is.EqualTo(DomItems(xml)));

    [TestCase("<ms2><npc id='1'><environment friendly='2'/><environment friendly='1' locale='NA'/><environment friendly='3' locale='KR'/></npc>"
              + "<npc id='2'><environment friendly='0' feature='Off'/></npc><npc id='3'><environment/></npc>"
              + "<npc id='1'><environment friendly='5'/><basic><environment friendly='9'/></basic></npc></ms2>")]
    [TestCase("<ms2><npc id='1'><environment friendly='2'/></npc><npc></ms2>")]
    public void NpcRowsMatchTheDocument(string xml) =>
        Assert.That(RowNpcs(Encoding.UTF8.GetBytes(xml)), Is.EqualTo(DomNpcs(xml)));

    // ---- the data ---------------------------------------------------------------------------------------------------

    static PackSet packs;

    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return packs ??= new PackSet(d);
    }

    static string Text(byte[] b) => Encoding.UTF8.GetString(b).TrimStart('﻿');

    [Test]
    public void EveryDataTableMatches()
    {
        var p = Packs();
        var dom = new ReservedWords(); var rows = new ReservedWords();
        int files = 0;
        foreach (var (table, file) in ReservedWords.Tables)
            foreach (var lang in new[] { "en", "kr" })
            {
                string entry = $"string/{lang}/{file}.xml";
                if (!p.Exists("Xml", entry)) continue;
                var bytes = p.Read("Xml", entry);
                dom.AddTable(table, XDocument.Parse(Text(bytes)), AvailX, fillOnly: true);
                rows.AddTable(table, bytes, Avail, fillOnly: true);
                files++;
            }
        Assert.That(files, Is.GreaterThan(10));
        AssertSameTables(dom, rows);
    }

    [Test]
    public void EveryItemAndNpcFileMatches()
    {
        var p = Packs();
        int items = 0, npcs = 0;
        foreach (var entry in p.List("Xml").Where(n => n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            bool item = entry.StartsWith("itemdata/", StringComparison.OrdinalIgnoreCase), npc = entry.StartsWith("npcdata/", StringComparison.OrdinalIgnoreCase);
            if (!item && !npc) continue;
            var bytes = p.Read("Xml", entry);
            if (item) { var a = DomItems(Text(bytes)); Assert.That(RowItems(bytes), Is.EqualTo(a), entry); items += a.Count; }
            else { var a = DomNpcs(Text(bytes)); Assert.That(RowNpcs(bytes), Is.EqualTo(a), entry); npcs += a.Count; }
        }
        TestContext.WriteLine($"items {items}, npcs {npcs}");
        Assert.That(items, Is.GreaterThan(1000));
        Assert.That(npcs, Is.GreaterThan(100));
    }
    [TestCase("monsterwebfinder_npc.xml")]
    [TestCase("monsterwebfinder_mob.xml")]
    [TestCase("itemwebfinder.xml")]
    public void EveryDictionaryFileMatches(string file)
    {
        var p = Packs();
        var names = p.List("Xml").ToList();
        string entry = names.FirstOrDefault(n => string.Equals(n, "table/kr/" + file, StringComparison.OrdinalIgnoreCase))
                    ?? names.FirstOrDefault(n => string.Equals(n, "table/" + file, StringComparison.OrdinalIgnoreCase));
        if (entry == null) Assert.Ignore(file + " is not in the data");
        var bytes = p.Read("Xml", entry);
        var dom = XDocument.Parse(Text(bytes)).Root!.Elements("key").Where(k => FieldMissions.Int((string)k.Attribute("value")) != 0)
            .Select(k => FieldMissions.Long((string)k.Attribute("id"))).ToHashSet();
        var ids = new List<long>();
        Assert.That(Rows(bytes, "key", r => { if (FieldMissions.Int(r.GetAttribute("value")) != 0) ids.Add(FieldMissions.Long(r.GetAttribute("id"))); }, null, null), Is.True);
        Assert.That(ids.ToHashSet().SetEquals(dom), Is.True);
    }
}
