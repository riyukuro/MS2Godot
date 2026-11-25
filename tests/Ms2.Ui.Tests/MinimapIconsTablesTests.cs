using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The minimap's tables read from the player's data: the streamed interact object rows and the shared field meta table
/// give what the XDocument reads of the same files gave.
/// </summary>
public class MinimapIconsTablesTests
{
    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(d);
    }

    [Test]
    public void InteractRowsMatchTheTree()
    {
        using var packs = Packs();
        const string entry = "table/interactobject.xml";
        if (!packs.Exists("Xml", entry)) Assert.Ignore("no interact object table");
        var tree = new Dictionary<int, (string, int)>();
        foreach (var row in GameStrings.Parse(packs.Read("Xml", entry)).Root!.Elements("interact"))
            if (int.TryParse((string)row.Attribute("id"), out int id))
            {
                int.TryParse((string)row.Attribute("type"), out int type);
                tree.TryAdd(id, ((string)row.Element("display")?.Attribute("minimapIcon") ?? "", type));
            }
        var streamed = MinimapIcons.InteractRows(packs);
        Assert.That(streamed, Has.Count.EqualTo(tree.Count));
        Assert.That(tree.Values.Count(v => v.Item1 != ""), Is.GreaterThan(0), "no row has a minimap icon");
        foreach (var (id, v) in tree)
            Assert.That(streamed[id], Is.EqualTo(v), $"interact {id}");
    }

    [Test]
    public void SharedFieldMetaGivesTheSamePortalNpcsAndTaxis()
    {
        using var packs = Packs();
        var entry = FieldMeta.EntryOf(packs);
        Assert.That(entry, Is.Not.Null, "no field meta table");
        // the tree read MinimapIcons did before: <npc id><field id/></npc> (npcs in the npc table only) and <taxiStation field/>
        var root = GameStrings.Parse(packs.Read(FieldMeta.Archive, entry)).Root!;
        var npcs = new NpcTable(packs, "en");
        var fieldNpcs = new Dictionary<int, List<int>>();
        var taxis = new HashSet<int>();
        foreach (var t in root.Elements("taxiStation"))
            if (int.TryParse((string)t.Attribute("field"), out int tf)) taxis.Add(tf);
        foreach (var n in root.Elements("npc"))
        {
            if (!int.TryParse((string)n.Attribute("id"), out int id) || npcs.Find(id) == null) continue;
            foreach (var f in n.Elements("field"))
                if (int.TryParse((string)f.Attribute("id"), out int fid))
                {
                    if (!fieldNpcs.TryGetValue(fid, out var list)) fieldNpcs[fid] = list = new List<int>();
                    list.Add(id);
                }
        }
        var meta = FieldMeta.Load(packs);
        Assert.That(meta.TaxiStations.Keys, Is.EquivalentTo(taxis));
        var text = new GameStrings(packs, "en");
        var before = new MinimapIcons(packs, npcs, text, fieldNpcs, taxis);
        var after = new MinimapIcons(packs, npcs, text, meta.FieldNpcs, meta.TaxiStations.Keys);
        int withNpc = 0;
        foreach (int field in fieldNpcs.Keys.Concat(meta.FieldNpcs.Keys).Distinct())
        {
            var a = before.PortalNpc(field);
            Assert.That(after.PortalNpc(field)?.Id, Is.EqualTo(a?.Id), $"field {field}");
            if (a != null) withNpc++;
            Assert.That(after.HasTaxi(field), Is.EqualTo(before.HasTaxi(field)), $"field {field} taxi");
        }
        Assert.That(withNpc, Is.GreaterThan(0), "no field has a portal npc");
    }
}
