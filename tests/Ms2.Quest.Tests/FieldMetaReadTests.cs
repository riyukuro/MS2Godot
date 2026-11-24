using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>The streamed FieldMeta read gives the tables the document read (the code before it, kept here) gave.</summary>
public class FieldMetaReadTests
{
    // the document read as it was
    static FieldMeta DomParse(string xml)
    {
        var m = new FieldMeta();
        var root = XDocument.Parse(xml).Root;
        foreach (var npc in root.Elements("npc"))
        {
            int id = Int(npc, "id");
            foreach (var f in npc.Elements("field"))
            {
                if (!m.NpcFields.TryGetValue(id, out var l)) m.NpcFields[id] = l = new List<int>();
                l.Add(Int(f, "id"));
                if (!m.FieldNpcs.TryGetValue(Int(f, "id"), out var n)) m.FieldNpcs[Int(f, "id")] = n = new List<int>();
                n.Add(id);
                if (f.Attribute("startTime") != null && f.Attribute("endTime") != null
                    && FieldMeta.ParseTimeString((string)f.Attribute("startTime")) && FieldMeta.ParseTimeString((string)f.Attribute("endTime")))
                    m.TimedNpcFields.Add((id, Int(f, "id")));
            }
        }
        foreach (var t in root.Elements("taxiStation"))
            m.TaxiStations[Int(t, "field")] = (string)t.Attribute("IsHomePrivilege") is "1" or "true";
        var indoorPortals = m.IndoorPortals;
        var outdoor = new Dictionary<int, bool>();          // outdoor[nField] = (indoor == 0): a later fieldPortal of the field decides
        foreach (var fp in root.Elements("fieldPortal"))
        {
            int field = Int(fp, "field");
            var targets = new List<int>();
            foreach (var p in fp.Elements("portal"))
            {
                if (Int(p, "portalType") == 0) targets.Add(Int(p, "targetField"));
                else if (Int(p, "portalType") == 8)
                {
                    if (!m.PortalType8.TryGetValue(field, out var t8)) m.PortalType8[field] = t8 = new List<int>();
                    if (!t8.Contains(Int(p, "id"))) t8.Add(Int(p, "id"));
                }
            }
            m.PortalFields.Add(field);
            m.PortalTargets[field] = targets;
            var indoor = new List<int>();
            foreach (var p in fp.Elements("portal"))
                if (Int(p, "portalIndoor") != 0 && !indoor.Contains(Int(p, "targetField"))) indoor.Add(Int(p, "targetField"));
            indoorPortals[field] = indoor;
            outdoor[field] = Int(fp, "indoor") == 0;
        }
        foreach (int field in FieldMeta.AtlMapOrder(indoorPortals.Keys).Where(f => outdoor[f]))
        {
            var done = new List<int>();
            var from = new List<int> { field };
            while (from.Count > 0)
            {
                var merged = new List<int>();
                foreach (int f in from)
                    if (indoorPortals.TryGetValue(f, out var targets))
                        foreach (int t in targets)
                            if (!done.Contains(t)) { merged.Add(t); done.Add(t); }
                from = merged;
            }
            foreach (int f in done) m.ParentFields[f] = field;
        }
        return m;
        return m;
    }

    static int Int(XElement e, string a) =>
        int.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static string Dump(FieldMeta m)
    {
        string L(IEnumerable<int> l) => string.Join(",", l);
        string D(Dictionary<int, List<int>> d) => string.Join(";", d.Select(kv => kv.Key + ":" + L(kv.Value)));
        return string.Join("\n", new[]
        {
            "portalFields " + L(m.PortalFields), "targets " + D(m.PortalTargets), "npcFields " + D(m.NpcFields),
            "fieldNpcs " + D(m.FieldNpcs), "type8 " + D(m.PortalType8), "timed " + string.Join(";", m.TimedNpcFields),
            "parents " + string.Join(";", m.ParentFields.Select(kv => kv.Key + ":" + kv.Value)),
            "taxi " + string.Join(";", m.TaxiStations.Select(kv => kv.Key + ":" + kv.Value)), "indoor " + D(m.IndoorPortals),
        });
    }

    [Test]
    public void SyntheticTableMatchesTheDocumentRead()
    {
        // kinds interleaved, empty and nested elements, a namespaced look-alike, comments, a field portal repeated with
        // another indoor flag, timed npc fields, entities and odd numbers
        var xml = @"<?xml version=""1.0""?><!DOCTYPE ms2 [<!ENTITY f ""2000001"">]>
<ms2 xmlns:x=""urn:x"">
  <fieldPortal field=""&f;"" indoor=""0""><portal portalType=""0"" targetField=""2000002"" portalIndoor=""1""/>
    <portal portalType=""8"" id=""7"" targetField=""0""/><portal portalType=""8"" id=""7""/><!-- c --><wrap><portal portalType=""0"" targetField=""9""/></wrap></fieldPortal>
  <npc id=""11""><field id=""2000001""/><field id=""2000002"" startTime=""2020-01-01-00-00-00"" endTime=""2030-01-01-00-00-00""/><x:field id=""5""/></npc>
  <taxiStation field=""2000001"" IsHomePrivilege=""true""/><taxiStation field=""2000003"" IsHomePrivilege=""0""/>
  <npc id=""12""/><npc id="" 13 ""><field id=""2000002""><field id=""77""/></field></npc>
  <fieldPortal field=""2000002"" indoor=""1""><portal portalType=""0"" targetField=""2000001"" portalIndoor=""2""/><portal portalType=""0"" targetField=""2000003"" portalIndoor=""1""/></fieldPortal>
  <fieldPortal field=""2000003""/>
  <fieldPortal field=""2000002"" indoor=""0""><portal portalType=""0"" targetField=""2000004"" portalIndoor=""1""/></fieldPortal>
  <other><npc id=""99""><field id=""1""/></npc></other>
  <x:npc id=""98""/>
</ms2>";
        Assert.That(Dump(FieldMeta.Parse(xml)), Is.EqualTo(Dump(DomParse(xml))));
        Assert.That(Dump(FieldMeta.Parse("<ms2/>")), Is.EqualTo(Dump(DomParse("<ms2/>"))));
        Assert.Throws<XmlException>(() => FieldMeta.Parse("<ms2><npc id=\"1\"></ms2>"));
    }

    [Test]
    public void RealTableMatchesTheDocumentRead()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(data);
        var entry = FieldMeta.EntryOf(packs) ?? FieldMeta.Entry;
        var xml = System.Text.Encoding.UTF8.GetString(packs.Read(FieldMeta.Archive, entry)).TrimStart('\ufeff');
        Assert.That(Dump(FieldMeta.Load(packs)), Is.EqualTo(Dump(DomParse(xml))));
    }
}
