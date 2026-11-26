using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The client's enchant level tables on the whole of a data set (MS2_DATA, then MS2_GMS2_DATA): every equip item against
/// CItemEnchantLimitTable (Table/enchantLimitTable.xml, itemenchantlimittable.cpp:150; CanEnchant :185 and the dialog's list
/// test FUN_14058e230 :218), and every input item of CItemEnchantTransformTable (Table/ItemEnchantTransform.xml,
/// itemenchanttransformtable.cpp:97). The client has no success rate table: the rates come in the server's packets.
/// Counts are printed for the feature row; no language's strings.
/// </summary>
[NonParallelizable]
public class EnchantLimitTableTests
{
    static PackSet Packs(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore(env + " is not set");
        return TestData.Packs(d);
    }

    /// <summary>Every equip item: the dialog's list test and CanEnchant on a fresh +0 item give what the limit row of its slot says
    /// (the required level inside minLv..maxLv, maxGrade above the enchant level, exceptEnchant off).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryEquipItemFollowsItsSlotsLimitRow(string env)
    {
        var packs = Packs(env);
        var tables = new GameTables(packs, "en");
        Assert.That(packs.Exists("Xml", "table/enchantlimittable.xml"), Is.True);
        var root = GameStrings.Parse(packs.Read("Xml", "table/enchantlimittable.xml")).Root!;
        var rows = new Dictionary<int, (int Min, int Max, int Grade)>();
        foreach (var e in FeatureElements.Collect(packs, root, "limit", "slot"))
            rows[ItemData.Atoi((string)e.Attribute("slot") ?? "0")] = ((ushort)ItemData.Atoi((string)e.Attribute("minLv") ?? "0"),
                (ushort)ItemData.Atoi((string)e.Attribute("maxLv") ?? "0"), ItemData.Atoi((string)e.Attribute("maxGrade") ?? "0"));
        int equips = 0, inLimit = 0, canEnchant = 0, excepted = 0, noRow = 0, outOfLevel = 0;
        foreach (int id in ItemFunctionSweepTests.ItemIds(packs))
        {
            if (tables.Items.Get(id) is not { Type: 1 } d) continue;
            equips++;
            bool row = rows.TryGetValue(d.EquipSubtype, out var r);
            int lv = (ushort)d.LevelLimit;
            bool inside = row && lv >= r.Min && lv <= r.Max;
            if (!row) noRow++;
            else if (!inside) outOfLevel++;
            else if (d.ExceptEnchant) excepted++;
            bool expectList = inside && !d.ExceptEnchant;
            var item = Item.Create(d, 1);
            Assert.That(tables.ItemOptions.InEnchantLimit(item), Is.EqualTo(expectList), $"item {id} list test");
            Assert.That(tables.ItemOptions.CanEnchant(item), Is.EqualTo(expectList && r.Grade > 0), $"item {id} CanEnchant");
            if (expectList) inLimit++;
            if (expectList && r.Grade > 0) canEnchant++;
            if (row && r.Grade > 0)
            {
                item.EnchantLevel = r.Grade;
                Assert.That(tables.ItemOptions.CanEnchant(item), Is.False, $"item {id} at the max grade {r.Grade}");
            }
        }
        TestContext.Out.WriteLine($"{env}: limit rows {rows.Count} (slots {string.Join(",", rows.Keys.OrderBy(k => k))}; levels "
            + string.Join(", ", rows.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value.Min}-{kv.Value.Max}/+{kv.Value.Grade}"))
            + $"), equip items {equips}: in the dialog's list {inLimit}, can enchant {canEnchant}, exceptEnchant {excepted}, "
            + $"no row {noRow}, level outside {outOfLevel}");
        Assert.That(rows.Count, Is.GreaterThan(0));
        Assert.That(canEnchant, Is.GreaterThan(0));
    }

    /// <summary>Every transform row up to the first bad one: each input item's lowest inputEnchantLevel is the table's limit
    /// (GetLowestLimitLevel :254); an item with no row gives 0.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryTransformInputHasItsLowestLevel(string env)
    {
        var packs = Packs(env);
        var t = new GameTables(packs, "en").EnchantTransform;
        var path = TablePath.Resolve(packs, "itemenchanttransform.xml") ?? "table/itemenchanttransform.xml";
        Assert.That(packs.Exists("Xml", path), Is.True);
        var lowest = new Dictionary<int, int>();
        int rows = 0, unavailable = 0, read = 0;
        bool stopped = false;
        foreach (var e in GameStrings.Parse(packs.Read("Xml", path)).Root!.Elements("transform"))
        {
            rows++;
            if (stopped) continue;
            if (!FeatureElements.IsAvailable(packs, e)) { unavailable++; continue; }
            int Num(string a) => ItemData.Atoi((string)e.Attribute(a) ?? "0");
            var ids = ((string)e.Attribute("inputItemId") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ItemData.Atoi).ToList();
            if (Num("inputRank") == 0 || string.IsNullOrWhiteSpace((string)e.Attribute("inputSlot")) || ids.Count == 0) { stopped = true; continue; }
            foreach (int id in ids) lowest[id] = Math.Min(lowest.GetValueOrDefault(id, int.MaxValue), Num("inputEnchantLevel"));
            read++;
            if (Num("output1Id") == 0 || Num("output1Rank") == 0 || Num("output1Count") == 0) stopped = true;
        }
        Assert.That(t.ItemIds.OrderBy(i => i), Is.EqualTo(lowest.Keys.OrderBy(i => i)));
        foreach (var (id, level) in lowest)
            Assert.That(t.LowestLimitLevel(id), Is.EqualTo((uint)level), $"item {id}");
        Assert.That(t.LowestLimitLevel(13000022), Is.EqualTo(0u), "an item with no row");
        TestContext.Out.WriteLine($"{env}: transform rows {rows}, unavailable {unavailable}, read {read}, stopped early {stopped}, "
            + $"input items {lowest.Count}, by lowest level "
            + string.Join(", ", lowest.GroupBy(kv => kv.Value).OrderBy(g => g.Key).Select(g => $"+{g.Key}: {g.Count()}")));
    }
}
