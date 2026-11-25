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

    /// <summary>
    /// On the GMS2 data, whole list: every item's model (ItemModelTable.ForItem) has the colour head of the rule: its
    /// preset's where that names a palette, else its own &lt;customize&gt; where that does, else the preset's; the slots are
    /// always the preset's. It also counts the items whose own head differs from a preset head that names a palette (the
    /// rule keeps the preset's there).
    /// </summary>
    [Test]
    public void EveryGms2ItemIsDyedByItsPresetElseItsOwnHead()
    {
        var t = Gms2();
        var packs = gms2Packs;
        int files = 0, dyeable = 0, byOwn = 0, presetKept = 0, ownDiffers = 0;
        foreach (var (id, entry) in ItemDataTable.PerItemIndex(packs.List("Xml")))
        {
            var env = FeatureElements.SelectEnvironment(packs, GameStrings.Parse(packs.Read("Xml", entry)).Root);
            var d = t.Items.Get(id);
            if (env == null || d == null) continue;
            files++;
            var preset = d.ItemPreset == 0 ? null : t.ItemModels.Get(d.ItemPreset);
            var m = t.ItemModels.ForItem(d);
            if (preset == null) { Assert.That(m, Is.Null, $"item {id}"); continue; }
            var own = ItemModelTable.HeadOf(env.Element("customize"), preset.SlotTypes);
            var want = preset.Palette != 0 || own.Palette == 0 ? preset : own;
            Assert.That((m.ColorEnabled, m.Palette, m.DefaultColors), Is.EqualTo((want.ColorEnabled, want.Palette, want.DefaultColors)), $"item {id}");
            Assert.That(m.SlotTypes, Is.EqualTo(preset.SlotTypes), $"item {id}");
            if (m.ColorEnabled) dyeable++;
            if (!ReferenceEquals(want, preset)) byOwn++;
            if (preset.Palette != 0)
            {
                presetKept++;
                if (own.Palette != preset.Palette || !own.DefaultColors.SequenceEqual(preset.DefaultColors)) ownDiffers++;
            }
        }
        TestContext.Out.WriteLine($"{files} item files: {dyeable} dyeable, {byOwn} by their own head; {presetKept} keep their preset's palette ({ownDiffers} whose own head differs)");
        Assert.That(dyeable, Is.GreaterThan(10000));
        Assert.That(byOwn, Is.GreaterThan(10000));
    }

    [Test]
    public void TheSellPriceIsThePriceCustomAboveZeroElseThePrice()
    {
        Assert.That(ItemData.SellList("472,590,755", null), Is.EqualTo(new long[] { 472, 590, 755 }));
        Assert.That(ItemData.SellList("100,100,100", "1,0,-1"), Is.EqualTo(new long[] { 1, 100, 100 }));
        Assert.That(ItemData.SellList("5", "0,0,7"), Is.EqualTo(new long[] { 5, 0, 7 }));
        Assert.That(ItemData.SellList("", ""), Is.Empty);
    }

    /// <summary>
    /// On the GMS2 data, whole list: every sell price list is priceCustom above 0, else price, per rarity. Before, it was
    /// price alone.
    /// </summary>
    [Test]
    public void EveryGms2SellListIsThePriceCustomElseThePrice()
    {
        var t = Gms2();
        var packs = gms2Packs;
        int lists = 0, custom = 0;
        foreach (var (id, entry) in ItemDataTable.PerItemIndex(packs.List("Xml")))
        {
            var env = FeatureElements.SelectEnvironment(packs, GameStrings.Parse(packs.Read("Xml", entry)).Root);
            var d = t.Items.Get(id);
            if (env?.Element("property")?.Element("sell") is not { } sell || d == null) continue;
            var price = ((string)sell.Attribute("price") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ItemData.Atoi).ToList();
            var over = ((string)sell.Attribute("priceCustom") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ItemData.Atoi).ToList();
            var expect = Enumerable.Range(0, Math.Max(price.Count, over.Count))
                .Select(i => (long)(i < over.Count && over[i] > 0 ? over[i] : i < price.Count ? price[i] : 0)).ToList();
            Assert.That(d.SellPrices, Is.EqualTo(expect), $"item {id}");
            lists++;
            if (!expect.SequenceEqual(price.Select(v => (long)v))) custom++;
        }
        TestContext.Out.WriteLine($"{lists} sell lists, {custom} changed by priceCustom");
        Assert.That(lists, Is.GreaterThan(30000));
        Assert.That(custom, Is.GreaterThan(1000));
    }
}
