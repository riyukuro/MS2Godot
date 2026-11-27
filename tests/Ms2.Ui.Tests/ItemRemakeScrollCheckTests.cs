using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The remake scroll row (CItemRemakeScrollTable::Load) and its item check FUN_140616170 (itemremakescrolldata.cpp:283),
/// one case per code, in the client's order.</summary>
public class ItemRemakeScrollCheckTests
{
    static PackSet packs;
    static GameTables tables;

    static GameTables Tables()
    {
        if (tables != null) return tables;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = TestData.Packs(d);
        return tables = new GameTables(packs, "en");
    }

    /// <summary>A level 50 armour item (equip subtype 0xd) in the KMS data, as the remake option tests use.</summary>
    const int ArmourId = 11300073;

    static ItemData Armour(int level = 50) => new() { Id = 11300001, Type = 1, Subtype = 0xd, LevelLimit = level, RandomId = 1 };

    static Item With(ItemData d, int rarity, params (int Stat, int Value)[] stats)
    {
        var it = Item.Create(d, rarity);
        it.Rarity = rarity;
        var e = new EquipItem { Id = d.Id };
        foreach (var (s, v) in stats) e.Options.Stat[2][s] = v;
        it.Record = e;
        return it;
    }

    static ItemRemakeScrollData Row(uint[] items = null, int pet = 0, uint[] slots = null, uint[] ranks = null,
        int min = 1, int max = 99, int value = 1) =>
        new(min, max, ranks ?? new uint[] { 4 }, slots ?? new uint[] { 0xd }, pet, items ?? Array.Empty<uint>(), 0, 1, value);

    [Test]
    public void TheRowReadsEveryAttribute()
    {
        var t = Tables();
        var path = TablePath.Resolve(packs, "itemremakescroll.xml");
        Assert.That(path, Is.Not.Null);
        var root = GameStrings.Parse(packs.Read("Xml", path)).Root!;
        var e = root.Elements("remakeScroll").First(x => ItemData.Atoi((string)x.Attribute("id") ?? "") != 0
            && FeatureElements.IsAvailable(packs, x));
        int id = ItemData.Atoi((string)e.Attribute("id"));
        var last = root.Elements("remakeScroll").Last(x => ItemData.Atoi((string)x.Attribute("id") ?? "") == id
            && FeatureElements.IsAvailable(packs, x));
        var r = t.RemakeScroll(id);
        Assert.That(r, Is.Not.Null);
        int Num(string n) => ItemData.Atoi((string)last.Attribute(n) ?? "");
        Assert.That((r.LevelMin, r.LevelMax, r.PetKind, r.LockOption, r.OptionKind, r.OptionValue),
            Is.EqualTo((Num("minLv"), Num("maxLv"), Num("onlyPet"), Num("representOpValue"), Num("addOpKind"), Num("addOpValue"))));
        Assert.That(r.Ranks.Length, Is.EqualTo(((string)last.Attribute("rank") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Length));
        Assert.That(r.Slots.Length, Is.EqualTo(((string)last.Attribute("slot") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Length));
        Assert.That(r.OptionValue, Is.Not.Zero, "every shipped row has an option value");
        Assert.That(t.RemakeScroll(0), Is.Null);
        TestContext.Out.WriteLine($"row {id}: lv {r.LevelMin}-{r.LevelMax}, {r.Ranks.Length} ranks, {r.Slots.Length} slots, lock {r.LockOption}, kind {r.OptionKind}, value {r.OptionValue}");
    }

    [Test]
    public void TheChecksBeforeTheOptionsGoInTheClientOrder()
    {
        var o = ItemOptionTables.Empty;
        var good = With(Armour(), 4, (1, 10));
        Assert.That(ItemRemakeScroll.Check(null, null, o), Is.EqualTo(1), "no item");
        Assert.That(ItemRemakeScroll.Check(good, null, o), Is.EqualTo(10), "no scroll");
        var off = Armour(); off.RemakeDisable = true;
        Assert.That(ItemRemakeScroll.Check(With(off, 4, (1, 10)), Row(), o), Is.EqualTo(7), "remakeDisable");
        var bare = Item.Create(Armour(), 4);
        Assert.That(ItemRemakeScroll.Check(bare, Row(), o), Is.EqualTo(3), "no option record");
        // an item list decides alone: the slot list is not read
        Assert.That(ItemRemakeScroll.Check(good, Row(items: new uint[] { 1 }), o), Is.EqualTo(8));
        Assert.That(ItemRemakeScroll.Check(good, Row(items: new uint[] { 11300001 }, slots: new uint[] { 0x12 }, ranks: Array.Empty<uint>()), o),
            Is.EqualTo(5), "the item list passes, the rank is next");
        // a pet scroll takes a pet of any slot, a non-pet is 1
        Assert.That(ItemRemakeScroll.Check(good, Row(pet: 1), o), Is.EqualTo(1));
        var pet = With(new ItemData { Id = 60000001, Type = 0xb, LevelLimit = 50 }, 4, (1, 10));
        Assert.That(ItemRemakeScroll.Check(pet, Row(pet: 1, slots: Array.Empty<uint>(), ranks: Array.Empty<uint>()), o), Is.EqualTo(5));
        Assert.That(ItemRemakeScroll.Check(good, Row(slots: new uint[] { 0x12 }), o), Is.EqualTo(4), "slot");
        Assert.That(ItemRemakeScroll.Check(good, Row(ranks: new uint[] { 3, 5 }), o), Is.EqualTo(5), "rank");
        Assert.That(ItemRemakeScroll.Check(good, Row(min: 51), o), Is.EqualTo(6), "level below");
        Assert.That(ItemRemakeScroll.Check(good, Row(max: 49), o), Is.EqualTo(6), "level above");
        Assert.That(ItemRemakeScroll.Check(With(Armour(), 4, (1, 10)), Row(min: 50, max: 50, value: 0), o), Is.EqualTo(0xb), "no option value");
        Assert.That(ItemRemakeScroll.Check(With(Armour(), 4), Row(), o), Is.EqualTo(3), "no group 2 option");
        Assert.That(ItemRemakeScroll.Check(good, Row(), o), Is.EqualTo(3), "no random option");
    }

    [Test]
    public void APetRandomCodeFollowsTheRank()
    {
        var pet = new ItemData { Type = 0xb, ConstantId = 5, RandomId = 77 };
        Assert.That(Enumerable.Range(0, 8).Select(r => ItemOptionTables.RandomCode(pet, r)),
            Is.EqualTo(new[] { 0, 0, 0x3a2c941, 0x3a2c942, 0x3a2c943, 0x3a2c944, 0x3a2c944, 0 }));
        Assert.That(ItemOptionTables.RandomCode(new ItemData { Type = 0xb, RandomId = 77 }, 2), Is.Zero, "no constantID");
        Assert.That(ItemOptionTables.RandomCode(new ItemData { Type = 1, RandomId = 77 }, 2), Is.EqualTo(77));
    }

    [Test]
    public void EachGroupTwoOptionMustBeInTheRandomAndOneExclusiveOnArmour()
    {
        var t = Tables();
        var o = t.ItemOptions;
        var d = t.Items.Get(ArmourId);
        if (d == null) Assert.Ignore("the data has no item " + ArmourId);
        Assert.That(ItemRemakeScroll.IsArmor(d), Is.True);
        int code = ItemOptionTables.RandomCode(d, 4);
        var inRandom = Enumerable.Range(0, 0x23).Where(s => o.RandomHas(code, s, 0) == true).ToList();
        int outside = Enumerable.Range(0, 0x23).First(s => o.RandomHas(code, s, 0) == false);
        var exclusive = inRandom.Where(s => o.IsExclusiveOptionName(o.StatOptionName(s))).ToList();
        var plain = inRandom.Where(s => !o.IsExclusiveOptionName(o.StatOptionName(s))).ToList();
        TestContext.Out.WriteLine($"random {code}: in {string.Join(",", inRandom)}, exclusive {string.Join(",", exclusive)}, outside {outside}");
        Assert.That(exclusive.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(plain, Is.Not.Empty);
        var row = new ItemRemakeScrollData(1, 99, new uint[] { 4 }, new uint[] { (uint)d.EquipSubtype }, 0, Array.Empty<uint>(), 0, 1, 1);
        Assert.That(ItemRemakeScroll.Check(With(d, 4, (plain[0], 1)), row, o), Is.EqualTo(0));
        Assert.That(ItemRemakeScroll.Check(With(d, 4, (plain[0], 1), (outside, 1)), row, o), Is.EqualTo(3), "a stat outside the random");
        Assert.That(ItemRemakeScroll.Check(With(d, 4, (exclusive[0], 1), (plain[0], 1)), row, o), Is.EqualTo(0), "one exclusive");
        Assert.That(ItemRemakeScroll.Check(With(d, 4, (exclusive[0], 1), (exclusive[1], 1)), row, o), Is.EqualTo(3), "two exclusive on armour");
        // not armour: the exclusive count is not kept (the same random, another subtype)
        var ring = new ItemData { Id = d.Id, Type = 1, Subtype = 0x14, LevelLimit = d.LevelLimit, RandomId = d.RandomId };
        var ringRow = row with { Slots = new uint[] { 0x14 } };
        Assert.That(ItemRemakeScroll.Check(With(ring, 4, (exclusive[0], 1), (exclusive[1], 1)), ringRow, o), Is.EqualTo(0));
        // a special in the group goes through HasSpecialStat
        var it = With(d, 4, (plain[0], 1));
        int special = Enumerable.Range(1, 0xce).First(x => o.RandomHas(code, -1, x) == false);
        it.Record.Options.Special[2][special] = 1;
        Assert.That(ItemRemakeScroll.Check(it, row, o), Is.EqualTo(3), "a special outside the random");
    }
}
