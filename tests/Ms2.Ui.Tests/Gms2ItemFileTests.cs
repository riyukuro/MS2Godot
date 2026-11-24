using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>What the GMS2 data keeps in its per-item files (item/&lt;a&gt;/&lt;bb&gt;/&lt;id&gt;.xml) where the KMS data has a
/// property attribute or a table of its own.</summary>
[NonParallelizable]
public class Gms2ItemFileTests
{
    static GameTables gms2;
    static PackSet gms2Packs;

    static GameTables Gms2()
    {
        if (gms2 != null) return gms2;
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        gms2Packs = new PackSet(d);
        return gms2 = new GameTables(gms2Packs, "en");
    }

    static ItemData Load(string env) => ItemData.Load(1, XElement.Parse(env));

    [Test]
    public void TheMaidIsTheMaidElementsOrTheKmsParam2()
    {
        // KMS: param2 of a type 6 / subtype 7 item, nothing for another subtype
        Assert.That(Load("<environment><property type=\"6\" subtype=\"7\" param2=\"11000512\"/></environment>").MaidId, Is.EqualTo(11000512));
        Assert.That(Load("<environment><property type=\"6\" subtype=\"5\" param2=\"11000512\"/></environment>").MaidId, Is.Zero);
        // GMS2: the <maid maidID>, its subtype 5 and no param2; a maidID of 0 (every other GMS2 item) is none
        var gms2 = Load("<environment><property type=\"6\" subtype=\"5\"/><maid maidID=\"11000512\"/></environment>");
        Assert.That((gms2.MaidId, gms2.HasMaidId), Is.EqualTo((11000512, true)));
        var none = Load("<environment><property type=\"1\" subtype=\"0\"/><maid maidID=\"0\"/></environment>");
        Assert.That((none.MaidId, none.HasMaidId), Is.EqualTo((0, false)));
    }

    /// <summary>On the GMS2 data: every maid contract (the items whose file names a maid) has that maid, with its
    /// MaidInfo row; before, none had one (GetMaidId read param2 of a subtype 7 item, and GMS2's are subtype 5 without it).</summary>
    [Test]
    public void TheGms2MaidContractsHaveTheirMaid()
    {
        var t = Gms2();
        var packs = gms2Packs;
        var maids = new Dictionary<int, int>();
        foreach (var (id, entry) in ItemDataTable.PerItemIndex(packs.List("Xml")))
            if (GameStrings.Parse(packs.Read("Xml", entry)).Root?.Descendants("maid").FirstOrDefault() is { } m
                && int.TryParse((string)m.Attribute("maidID"), out int maid) && maid > 0)
                maids[id] = maid;
        // the maids table/maidproperty.xml has (six GMS2 contracts, 50800031..36, name a maid it lacks)
        var rows = GameStrings.Parse(packs.Read("Xml", "table/maidproperty.xml")).Root!.Elements("Property")
            .Select(e => ItemData.Atoi((string)e.Attribute("MaidID") ?? "")).ToHashSet();
        int withInfo = 0;
        foreach (var (id, maid) in maids)
        {
            var d = t.Items.Get(id);
            Assert.That(d, Is.Not.Null, $"item {id}");
            Assert.That((d.MaidId, d.HasMaidId), Is.EqualTo((maid, true)), $"item {id}");
            Assert.That(t.MaidInfo(maid) != null, Is.EqualTo(rows.Contains(maid)), $"item {id} maid {maid}");
            if (rows.Contains(maid)) withInfo++;
        }
        TestContext.Out.WriteLine($"{maids.Count} maid contracts, {withInfo} with a maid property row");
        Assert.That(maids.Count, Is.GreaterThan(30));
        Assert.That(withInfo, Is.GreaterThan(25));
    }
}
