using System;
using System.IO;
using System.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>GMS2 has no npcscript table: one script/npc/&lt;id&gt;.xml per npc, scripts directly under the root, each with a locale and a feature
/// (needs MS2_GMS2_DATA).</summary>
public class Gms2NpcScriptTests
{
    static NpcScriptTable table;

    static NpcScriptTable Table()
    {
        if (table != null) return table;
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var packs = new PackSet(d);
        return table = new NpcScriptTable(packs, new GameStrings(packs, "en"), e => FeatureElements.IsAvailable(packs, e));
    }

    [Test]
    public void TheNpcFilesAreRead()
    {
        var t = Table();
        Assert.That(t.Count, Is.GreaterThan(3000));
        // script/npc/11000001.xml: select 0, scripts 10, 11, 20, monologue 30
        var n = t.Find(11000001);
        Assert.That(n, Is.Not.Null);
        Assert.That(n.Select?.Id, Is.EqualTo(0));
        Assert.That(n.ById(10)?.Contents.Count, Is.EqualTo(1));
        Assert.That(n.ById(20)?.RandomPick, Is.True);
        Assert.That(t.Monologues(11000001)?.Select(s => s.Id), Is.EqualTo(new[] { 30 }));
    }

    [Test]
    public void OneScriptPerIdIsKept()
    {
        var t = Table();
        foreach (var n in t.AllNpcs())
        {
            var dup = n.Scripts.Where(s => s.Kind is "script" or "select" or "job").GroupBy(s => (s.Kind, s.Id)).FirstOrDefault(g => g.Count() > 1);
            Assert.That(dup, Is.Null, "npc " + n.Id + " keeps two " + dup?.Key);
        }
    }
}
