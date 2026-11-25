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
        gms2Packs = TestData.Packs(d);
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

    /// <summary>
    /// On the GMS2 data, whole list: every item file without a rank whose constant option code has one grade gets that
    /// grade as its rank, and the gemstones (type 13) get their constant options at that rank (the socket stats; a gem whose
    /// only values are rates of flat stats, wapmin/wapmax_rate_base, keeps none, as ItemOptionTables.Gms2Values drops
    /// those). Before, every GMS2 item was rank 0, which no constant row reaches.
    /// </summary>
    [Test]
    public void TheGms2RankIsTheConstantCodesOneGrade()
    {
        var t = Gms2();
        var packs = gms2Packs;
        int ranked = 0, gems = 0, socketStats = 0;
        foreach (var (id, _) in ItemDataTable.PerItemIndex(packs.List("Xml")))
        {
            var d = t.Items.Get(id);
            if (d == null) continue;
            int grade = t.ItemOptions.SingleGrade(d.ConstantId);
            Assert.That(d.Rank, Is.EqualTo(grade), $"item {id} constant {d.ConstantId}");
            if (grade > 0) ranked++;
            if (d.Type == 13 && d.ConstantId != 0 && grade > 0)
            {
                gems++;
                var c = t.ItemOptions.ConstantOptions(d, d.Rank);
                Assert.That(c, Is.Not.Null, $"gem {id}");
                if (c.Value.Stat.Count + c.Value.Special.Count > 0) socketStats++;
            }
        }
        TestContext.Out.WriteLine($"{ranked} ranked items, {gems} ranked gems, {socketStats} with socket stats");
        Assert.That(ranked, Is.GreaterThan(20000));
        Assert.That(socketStats, Is.GreaterThan(100));
        var gem = t.Items.Get(40200001);
        Assert.That(gem.Rank, Is.EqualTo(4), "itemoptionconstant_gemstone.xml code 40200001 grade 4");
        // its int_value_base 2
        Assert.That(t.ItemOptions.ConstantOptions(gem, gem.Rank)!.Value.Stat.Values, Is.EqualTo(new[] { 2 }));
    }
}
