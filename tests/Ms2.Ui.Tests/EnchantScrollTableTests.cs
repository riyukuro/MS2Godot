using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// CEnchantScrollTable (enchantscrolldata.cpp) on the whole of a data set (MS2_DATA, then MS2_GMS2_DATA): every &lt;scroll&gt;
/// row of Table/enchantScroll.xml, and every protector item (function EnchantScroll or EnchantSuperProtect) with the rank
/// and count the item enchant dialog takes from it (GetProtectorRank / GetProtectorCount, uiitemenchantdialog.cpp:1844 /
/// :1861). Counts are printed for the feature row; no language's strings.
/// </summary>
[NonParallelizable]
public class EnchantScrollTableTests
{
    static PackSet Packs(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore(env + " is not set");
        return TestData.Packs(d);
    }

    [Test]
    public void ARowIsReadInTheClientsOrder()
    {
        var s = EnchantScroll.Read(XElement.Parse("<scroll id=\"1\" scrollType=\"4\" grade=\"3\" minGrade=\"2\" maxGrade=\"9\" minLv=\"20\""
            + " maxLv=\"70000\" isLockTrade=\"1\" disableBreak=\"true\" slot=\"30,,31\" rank=\"1,4,2\"/>"))!;
        Assert.That((s.ScrollType, s.Grade, s.MinGrade, s.MaxGrade, s.MinLv, s.MaxLv), Is.EqualTo((4, 3, 2, 9, (ushort)20, unchecked((ushort)70000))));
        Assert.That((s.LockTrade, s.DisableBreak), Is.EqualTo((true, true)));
        Assert.That(s.Slots, Is.EqualTo(new uint[] { 30, 31 }), "empty tokens are skipped");
        Assert.That(s.MaxRank, Is.EqualTo(4u));
        var random = EnchantScroll.Read(XElement.Parse("<scroll id=\"2\" scrollType=\"3\" grade=\"6,7,8\" minGrade=\"2\"/>"))!;
        Assert.That((random.Grade, random.MinGrade), Is.EqualTo((0, 0)), "type 3 reads no grade, only type 4 reads min / max grade");
        Assert.That(EnchantScroll.Read(XElement.Parse("<scroll id=\"3\" grade=\"5\"/>")), Is.Null, "scroll type 0 is no scroll");
        Assert.That(EnchantScroll.Read(XElement.Parse("<scroll id=\"3\" scrollType=\"1\"/>"))!.MaxRank, Is.EqualTo(0u), "no rank list is rank 0");
        var doc = XDocument.Parse("<ms2><scroll id=\"7\" scrollType=\"1\" grade=\"1\"/><scroll id=\"7\" scrollType=\"1\" grade=\"2\"/>"
            + "<scroll id=\"8\" scrollType=\"0\" grade=\"2\"/><scroll id=\"9\" scrollType=\"1\" grade=\"4\" skip=\"1\"/></ms2>");
        var t = new EnchantScrollTable(doc, e => (string)e.Attribute("skip") != "1");
        Assert.That(t.Find(7)!.Grade, Is.EqualTo(2), "a later row of an id replaces the earlier one (SetAt)");
        Assert.That((t.Find(8), t.Find(9), t.Count), Is.EqualTo(((EnchantScroll)null, (EnchantScroll)null, 1)));
    }

    /// <summary>Every row of the data set's table: each id whose last available row has a scroll type is in the table with that
    /// row's values; nothing else is.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryScrollRowOfTheDataIsRead(string env)
    {
        var packs = Packs(env);
        var t = new GameTables(packs, "en").EnchantScrolls;
        var path = TablePath.Resolve(packs, "enchantscroll.xml");
        Assert.That(path, Is.Not.Null);
        var rows = GameStrings.Parse(packs.Read("Xml", path)).Root!.Elements("scroll").ToList();
        var last = new Dictionary<int, XElement>();
        int unavailable = 0;
        foreach (var e in rows)
        {
            if (!FeatureElements.IsAvailable(packs, e)) { unavailable++; continue; }
            last[ItemData.Atoi((string)e.Attribute("id") ?? "")] = e;
        }
        var expect = last.Where(kv => ItemData.Atoi((string)kv.Value.Attribute("scrollType") ?? "") != 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.That(t.Ids.OrderBy(i => i), Is.EqualTo(expect.Keys.OrderBy(i => i)));
        var byType = new SortedDictionary<int, int>();
        foreach (var (id, e) in expect)
        {
            var s = t.Find(id)!;
            int type = ItemData.Atoi((string)e.Attribute("scrollType"));
            Assert.That(s.ScrollType, Is.EqualTo(type), $"scroll {id}");
            Assert.That(s.Grade, Is.EqualTo(type == 3 ? 0 : ItemData.Atoi((string)e.Attribute("grade") ?? "")), $"scroll {id} grade");
            var ranks = ((string)e.Attribute("rank") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ItemData.Atoi).ToList();
            Assert.That(s.MaxRank, Is.EqualTo((uint)(ranks.Count == 0 ? 0 : ranks.Max())), $"scroll {id} rank");
            byType[type] = byType.GetValueOrDefault(type) + 1;
        }
        TestContext.Out.WriteLine($"{env}: rows {rows.Count}, unavailable {unavailable}, scrolls {t.Count}, by type "
            + string.Join(", ", byType.Select(kv => $"{kv.Key}: {kv.Value}")));
        Assert.That(t.Count, Is.GreaterThan(0));
    }

    /// <summary>Every protector item of the data set: an EnchantSuperProtect item's rank and count are its function arguments 0
    /// and 1; an EnchantScroll item's are its scroll's max rank and grade (0 / 0 with no scroll).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryProtectorItemHasItsRankAndCount(string env)
    {
        var packs = Packs(env);
        var tables = new GameTables(packs, "en");
        var scrolls = tables.EnchantScrolls;
        int super = 0, scrollItems = 0, noScroll = 0;
        var ranks = new SortedDictionary<int, int>();
        foreach (int id in ItemFunctionSweepTests.ItemIds(packs))
        {
            if (tables.Items.Get(id) is not { HasFunction: true } d) continue;
            if (d.FunctionName == "EnchantSuperProtect")
            {
                super++;
                Assert.That(EnchantScrollTable.ProtectorRank(d, scrolls), Is.EqualTo(d.FunctionArgInt(0, 0)), $"item {id}");
                Assert.That(EnchantScrollTable.ProtectorCount(d, scrolls), Is.EqualTo(d.FunctionArgInt(1, 0)), $"item {id}");
            }
            else if (d.FunctionName == "EnchantScroll")
            {
                scrollItems++;
                var s = scrolls.Find(d.FunctionArgInt(0, 0));
                if (s == null) noScroll++;
                int rank = EnchantScrollTable.ProtectorRank(d, scrolls);
                Assert.That(rank, Is.EqualTo((int)(s?.MaxRank ?? 0)), $"item {id}");
                Assert.That(EnchantScrollTable.ProtectorCount(d, scrolls), Is.EqualTo(s?.Grade ?? 0), $"item {id}");
                ranks[rank] = ranks.GetValueOrDefault(rank) + 1;
            }
        }
        TestContext.Out.WriteLine($"{env}: super protectors {super}, scroll items {scrollItems} (no scroll {noScroll}), by protector rank "
            + string.Join(", ", ranks.Select(kv => $"{kv.Key}: {kv.Value}")));
        Assert.That(super, Is.GreaterThan(0));
        Assert.That(scrollItems, Is.GreaterThan(0));
        // the one super protector of both regions, 31000205 ("5,9,14"), and two scroll items of both
        var sp = tables.Items.Get(31000205);
        Assert.That((EnchantScrollTable.ProtectorRank(sp, scrolls), EnchantScrollTable.ProtectorCount(sp, scrolls)), Is.EqualTo((5, 9)));
        var g3 = tables.Items.Get(31000003);
        Assert.That((EnchantScrollTable.ProtectorRank(g3, scrolls), EnchantScrollTable.ProtectorCount(g3, scrolls)), Is.EqualTo((1, 3)));
        var r4 = tables.Items.Get(31000111);
        Assert.That((EnchantScrollTable.ProtectorRank(r4, scrolls), EnchantScrollTable.ProtectorCount(r4, scrolls)), Is.EqualTo((4, 3)));
        Assert.That(EnchantScrollTable.ProtectorRank(tables.Items.Get(ItemFunctionSweepTests.ItemIds(packs).First()), scrolls), Is.Zero,
            "an item of another function is rank 0");
    }
}
