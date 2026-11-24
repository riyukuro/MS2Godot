using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>CooltimeManager::Load / Use (cooltimemanager.cpp:134-182) as CSoundEventEffectParam::FUN_1417760d0 asks it.</summary>
public class CooltimeTableTests
{
    static CooltimeTable Table(string rows) => CooltimeTable.From(XElement.Parse("<ms2>" + rows + "</ms2>").Elements("v"));

    [Test]
    public void AnIdPlaysOncePerTick()
    {
        var t = Table("<v cooltimeID=\"5\" cooltimeTick=\"100000\" />");
        Assert.That(t.Use(5, 1000), Is.True, "the first use plays");
        Assert.That(t.Use(5, 1000 + 99_999), Is.False, "cooling");
        Assert.That(t.Use(5, 1000 + 99_999), Is.False, "a refused use does not move the ready time");
        Assert.That(t.Use(5, 101_000), Is.True, "ready at the use + the tick");
        Assert.That(t.Use(5, 101_001), Is.False, "the ready time moved on");
    }

    [Test]
    public void NoIdOrNoRowAlwaysPlays()
    {
        var t = Table("<v cooltimeID=\"5\" cooltimeTick=\"100000\" />");
        Assert.That(t.Use(0, 0) && t.Use(0, 0), Is.True, "id 0 has no cooldown (FUN_1417760d0)");
        Assert.That(t.Use(6, 0) && t.Use(6, 0), Is.True, "an id with no row (Use: m_ticks.find fails)");
    }

    [Test]
    public void LoadSkipsZerosAndKeepsAnIdsFirstRow()
    {
        var t = Table("<v cooltimeID=\"0\" cooltimeTick=\"10\" /><v cooltimeID=\"1\" cooltimeTick=\"0\" />"
            + "<v cooltimeID=\"2\" cooltimeTick=\"10000\" /><v cooltimeID=\"2\" cooltimeTick=\"300000\" /><v cooltimeID=\"x\" />");
        Assert.That(t.Count, Is.EqualTo(1));
        Assert.That(t.TickOf(1), Is.Zero);
        Assert.That(t.TickOf(2), Is.EqualTo(10000), "std::map::insert keeps the first");
    }

    /// <summary>Every soundEvent part's cooltimeID against Table/CooltimeTable.xml as the client loads it (FUN_14028faa0's
    /// pick, then Load); an id with no row has no cooldown in the client either, so it is counted, not failed.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryPartsCooltimeIdAgainstTheTable(string env)
    {
        var data = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore($"{env} is not set");
        using var packs = new PackSet(data);
        var root = PackXml.Parse(packs.Read("Xml", "table/cooltimetable.xml")).Root;
        var table = CooltimeTable.From(Ms2.Ui.FeatureElements.Collect(packs, root, "v", "cooltimeID"));
        var effects = new EffectTable(packs);
        int parts = 0, withRow = 0;
        var ids = new SortedSet<int>();
        var noRow = new SortedSet<int>();
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal)))
        {
            var fx = effects.Get(entry["effect/".Length..]);
            if (fx == null) continue;
            foreach (var p in fx)
            {
                if (p.CooltimeId == 0) continue;
                parts++; ids.Add(p.CooltimeId);
                if (table.TickOf(p.CooltimeId) > 0) withRow++; else noRow.Add(p.CooltimeId);
            }
        }
        TestContext.Out.WriteLine($"{env}: {table.Count} table ids; {parts} soundEvent parts with a cooltimeID ({ids.Count} ids), {withRow} with a row; ids with no row: {string.Join(",", noRow)}");
        Assert.That(table.Count, Is.GreaterThan(0));
        Assert.That(parts, Is.GreaterThan(0));
    }
}
