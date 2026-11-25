using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>
/// G06 — quest complete@fieldID (D5): GMS2 quest/10002800.xml has &lt;complete npc="11001294" fieldID="52010004"&gt;;
/// KMS questdata.xml quest 10002800 has the same npc and no fieldID. Neither client reads the fieldID:
/// CQuestData::Load @0x1407495d0 reads &lt;complete&gt; npc only (questdata.cpp:1402-1404), and the GMS2 exe's quest loader
/// @0x14042c5b0 (the one function that names "completeReward") takes "complete" then only "npc" (0x14042e409, 0x14042e42c);
/// its one "fieldID" (0x14042feba) is &lt;summonPortal&gt;'s, as KMS's (questdata.cpp:1511, QuestData.SummonPortalField).
/// So QuestData keeps CompleteNpc and no complete field: the attribute is server data.
/// </summary>
public class RcG06QuestCompleteFieldTests
{
    [Test]
    public void TheKmsQuestKeepsItsCompleteNpc()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var q = QuestTable.Load(packs, new[] { "en" }, "KR", _ => true).Quest(10002800);
        Assert.That(q, Is.Not.Null, "kms questdata.xml quest 10002800");
        Assert.That(q.CompleteNpc, Is.EqualTo(11001294));
    }

    [Test]
    public void TheGms2QuestReadsItsCompleteNpcAndNotItsField()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var q = QuestTable.Load(packs, new[] { "en" }, "NA", _ => true).Quest(10002800);
        Assert.That(q, Is.Not.Null, "gms/quest/10002800.xml");
        Assert.That(q.CompleteNpc, Is.EqualTo(11001294));
        var raw = GameStrings.Parse(packs.Read("Xml", "quest/10002800.xml")).Descendants("complete").Select(c => (string)c.Attribute("fieldID"));
        Assert.That(raw, Does.Contain("52010004"), "the data has the field");
        Assert.That(typeof(QuestData).GetMember("CompleteField"), Is.Empty, "the client reads complete@npc only");
    }

    /// <summary>
    /// Every quest of the data set with a &lt;complete&gt; (questdata/questdata.xml, or GMS2's quest/&lt;id&gt;.xml): the loaded
    /// quest's CompleteNpc is one of its &lt;complete npc&gt; values (a GMS2 file may hold one per environment), whether or not
    /// the row also has a fieldID. Prints how many rows carry a fieldID.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryQuestReadsItsCompleteNpc(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var table = QuestTable.Load(packs, new[] { "en" }, RcGap.IsKr(packs) ? "KR" : "NA", _ => true);
        var files = packs.List("Xml").Where(e => e.Equals("questdata/questdata.xml", StringComparison.OrdinalIgnoreCase)
                                              || Regex.IsMatch(e, @"^quest/(.+/)?\d+\.xml$", RegexOptions.IgnoreCase)).ToList();
        Assert.That(files, Is.Not.Empty);
        var npcs = new Dictionary<uint, HashSet<uint>>();
        int withField = 0;
        foreach (var f in files)
        {
            XDocument doc;
            try { doc = GameStrings.Parse(packs.Read("Xml", f)); } catch (System.Xml.XmlException) { continue; }
            foreach (var c in doc.Descendants("complete"))
            {
                var quest = c.Ancestors("quest").FirstOrDefault(a => a.Attribute("id") != null);
                uint id = quest != null ? (uint)quest.Attribute("id") : uint.Parse(Path.GetFileNameWithoutExtension(f));
                if (!uint.TryParse((string)c.Attribute("npc"), out uint npc)) npc = 0;
                if (!npcs.TryGetValue(id, out var set)) npcs[id] = set = new();
                set.Add(npc);
                if (c.Attribute("fieldID") != null) withField++;
            }
        }
        var bad = new List<string>();
        int loaded = 0;
        foreach (var (id, set) in npcs)
        {
            var q = table.Quest(id);
            if (q == null) continue;           // a row the loader drops (locale, feature)
            loaded++;
            if (!set.Contains((uint)q.CompleteNpc)) bad.Add($"{id}: CompleteNpc {q.CompleteNpc}, data {string.Join("/", set)}");
        }
        TestContext.WriteLine($"{variable}: {files.Count} files, {npcs.Count} quests with a <complete>, {loaded} loaded, {withField} <complete> rows with a fieldID");
        Assert.That(loaded, Is.GreaterThan(0));
        Assert.That(bad, Is.Empty);
    }
}
