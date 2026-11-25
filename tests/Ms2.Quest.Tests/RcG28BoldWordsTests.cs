using System.Xml.Linq;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>
/// G28 — BoldWords item ids and NPC friendly (D23): GMS2 has no itemdata/ (items are item/&lt;..&gt;/&lt;id&gt;.xml) and its
/// NPCs write friendly on environment/basic (npc/23/50/23503021.xml friendly="1", npc/22/00/22000052.xml friendly="0").
/// LoadBold now takes the item files' ids where there is no itemdata/ (PerItemFiles) and reads basic@friendly (the GMS2
/// exe's npc reader @0x1403fd8a0 reads friendly on &lt;basic&gt;, 0x1403fde2c). KMS itemdata/112.xml and npcdata/220.xml are unchanged.
/// </summary>
public class RcG28BoldWordsTests
{
    static ReservedWords Load(PackSet packs)
    {
        var w = new ReservedWords();
        w.LoadBold(packs, (_, _) => true, new Dictionary<int, int>());
        return w;
    }

    [Test]
    public void TheKmsBoldTablesAreUnchanged()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var w = Load(packs);
        Assert.That(RcGap.Field<HashSet<long>>(w, "itemIds"), Does.Contain(11200001L), "kms itemdata/112.xml");
        Assert.That(RcGap.Field<Dictionary<long, int>>(w, "npcFriendly")[22000052], Is.EqualTo(0), "kms npcdata/220.xml environment has no friendly");
    }

    [Test]
    public void TheGms2ItemsAndNpcFriendlyLoad()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var w = Load(packs);
        Assert.That(RcGap.Field<HashSet<long>>(w, "itemIds"), Does.Contain(11200001L), "gms/item/1/12/11200001.xml");
        var friendly = RcGap.Field<Dictionary<long, int>>(w, "npcFriendly");
        Assert.That(friendly[22000052], Is.EqualTo(0), "npc/22/00/22000052.xml basic friendly=0");
        // npc/23/50/23503021.xml has basic friendly="1" on its only environment, feature Kritias_Turka01, which this data's
        // features leave off: no environment is picked and the npc is not loaded (PerFileTable.Rows), as in the client
        Assert.That(friendly.Count(f => f.Value == 1), Is.GreaterThan(0), "friendly npcs come from basic@friendly");
    }

    /// <summary>
    /// Every npc and item of the data set: each npc's bold friendly is the friendly one of its environments carries (on
    /// &lt;basic&gt; in GMS2's npc/ files, on the environment in KMS npcdata/; none = 0), and every item id is bold (itemdata/
    /// rows, or one per item/ file). Prints the counts and how many GMS2 item files have no environment the data's locale and
    /// features pick (those count as items here: STILL-OPEN in LoadBold).
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryNpcAndItemOfTheDataIsInTheBoldTables(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var w = Load(packs);
        var itemIds = RcGap.Field<HashSet<long>>(w, "itemIds");
        var friendly = RcGap.Field<Dictionary<long, int>>(w, "npcFriendly");
        var names = packs.List("Xml").ToList();
        bool perFile = !names.Any(n => n.StartsWith("npcdata/", StringComparison.OrdinalIgnoreCase));
        var bad = new List<string>();
        int npcs = 0, friendlyNpcs = 0, unpickedNpcs = 0;
        foreach (var n in names.Where(n => n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                          && n.StartsWith(perFile ? "npc/" : "npcdata/", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument doc;
            try { doc = Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", n)); } catch (System.Xml.XmlException) { continue; }
            var rows = perFile
                ? new[] { (Id: long.Parse(Path.GetFileNameWithoutExtension(n)), Envs: doc.Root.Elements("environment").ToList()) }
                : doc.Descendants("npc").Where(x => x.Attribute("id") != null).Select(x => (Id: (long)x.Attribute("id"), Envs: x.Elements("environment").ToList())).ToArray();
            foreach (var (id, envs) in rows)
            {
                if (envs.Count == 0) continue;
                if (perFile && Ms2.Ui.FeatureElements.SelectEnvironment(packs, doc.Root) == null) { unpickedNpcs++; continue; }     // not an npc in this data
                npcs++;
                var values = envs.Select(e => int.TryParse((string)(perFile ? e.Element("basic")?.Attribute("friendly") : e.Attribute("friendly")), out int f) ? f : 0).ToHashSet();
                if (!friendly.TryGetValue(id, out int got)) bad.Add($"npc {id}: not loaded");
                else
                {
                    if (!values.Contains(got)) bad.Add($"npc {id}: friendly {got}, data {string.Join("/", values)}");
                    if (got == 1) friendlyNpcs++;
                }
            }
        }
        var itemFiles = Ms2.Formats.World.PerItemFiles.Index(names);
        int items = 0;
        if (itemFiles.Count > 0)
        {
            foreach (var id in itemFiles.Keys) { items++; if (!itemIds.Contains(id)) bad.Add($"item {id}: not bold"); }
        }
        else
            foreach (var n in names.Where(n => n.StartsWith("itemdata/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                foreach (var x in Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", n)).Descendants("item"))
                    if (long.TryParse((string)x.Attribute("id"), out long id) && x.Elements("environment").Any()) { items++; if (!itemIds.Contains(id)) bad.Add($"item {id}: not bold"); }
        int unpicked = 0;
        if (itemFiles.Count > 0)
            foreach (var entry in itemFiles.Values)
            {
                XElement root;
                try { root = Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", entry)).Root; } catch (System.Xml.XmlException) { continue; }
                if (Ms2.Ui.FeatureElements.SelectEnvironment(packs, root) == null) unpicked++;
            }
        TestContext.WriteLine($"{variable}: {npcs} npcs ({friendlyNpcs} friendly, {unpickedNpcs} npc files with no picked environment), {items} items ({(itemFiles.Count > 0 ? "item/ files" : "itemdata/ rows")}, {unpicked} files with no picked environment); bold {itemIds.Count} items, {friendly.Count} npcs");
        Assert.That(npcs, Is.GreaterThan(0));
        Assert.That(friendlyNpcs, Is.GreaterThan(0), "some npcs are friendly");
        Assert.That(items, Is.GreaterThan(0));
        Assert.That(bad.Take(20), Is.Empty, $"{bad.Count} wrong");
    }
}
