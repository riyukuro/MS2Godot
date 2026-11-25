using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// CEquipment's slots as ApplyEquipItem (FUN_1411ec190, equipment.cpp:2174) fills them: a worn item answers for every slot of
/// its model's groups (GetItemAt, :806). Then every equipment model of the data set (MS2_DATA) that takes more than one slot.
/// </summary>
public class EquipmentSlotsTests
{
    static Item Worn(long uid)
    {
        var it = Item.Create(new ItemData { Id = 11000000 + (int)uid, Type = 1 }, 1);
        it.Uid = uid;
        return it;
    }

    [Test]
    public void AWornItemFillsEverySlotOfItsModel()
    {
        var s = new EquipmentSlots();
        Item outfit = Worn(1), twoHander = Worn(2), dagger = Worn(3), bare = Worn(4), skin = Worn(5);
        s.Apply(outfit, 8, 0, new[] { 8, 9 });        // a top-and-bottom outfit: CL and PA
        s.Apply(twoHander, 5, 0, new[] { 5, 4 });     // a two-hander: RH and LH
        Assert.That((s.At(8, 0), s.At(9, 0)), Is.EqualTo((outfit, outfit)), "the outfit is also the item at PA");
        Assert.That((s.At(5, 0), s.At(4, 0)), Is.EqualTo((twoHander, twoHander)), "the two-hander is also the item at LH");
        s.Apply(dagger, 4, 0, new[] { 0x13 });        // OH: the item's own slot only
        Assert.That((s.At(4, 0), s.At(5, 0)), Is.EqualTo((dagger, twoHander)), "a later item replaces the slot it takes only");
        s.Apply(bare, 14, 0, Array.Empty<int>());
        s.Apply(bare, 15, 0, null);
        Assert.That((s.At(14, 0), s.At(15, 0)), Is.EqualTo(((Item)null, (Item)null)), "a model without groups takes no slot");
        s.Apply(skin, 16, 1, new[] { 16, 0 });
        Assert.That((s.At(16, 1), s.At(16, 0), s.At(0, 1)), Is.EqualTo((skin, (Item)null, (Item)null)), "its layer only; group type 0 none");
        s.Apply(Worn(6), 0, 0, new[] { 0x13 });
        Assert.That(s.At(0, 0), Is.Null, "an OH group without an own slot (FUN_141420b30) is skipped");
        Assert.That((s.At(-1, 0), s.At(20, 0), s.At(8, 3)), Is.EqualTo(((Item)null, (Item)null, (Item)null)), "outside m_slots and the layers");
        Assert.That(EquipmentSlots.SlotsOf(new[] { 5, 4, 5, 0x13, 0 }, 4), Is.EqualTo(new[] { 5, 4 }));
    }

    /// <summary>
    /// Every equipment item (type 1) of the data whose model takes more than one slot, worn alone at its packet slot (the first
    /// group's; an OH first group at RH) through <see cref="EquipmentSlots.Of"/>: the item is at each slot of its model and
    /// nowhere else. The cases the auto-equip's empty-slot test meets: every top-and-bottom outfit (subtype 0x16) takes CL and
    /// PA, and every two-hander of the subtype rule (FUN_14053d460, 0x32-0x38 and 0x50-0x53) with a model takes RH and LH.
    /// Prints the slot sets by subtype.
    /// </summary>
    [Test]
    public void EveryMultiSlotModelFillsEachOfItsSlots()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = TestData.Packs(d);
        var t = new GameTables(packs, "en");
        var all = EmotionCastTests.ItemIds(packs).Distinct().Select(t.Items.Get).Where(x => x is { Type: 1 }).ToList();
        var sets = new Dictionary<string, int>();
        var bad = new List<string>();
        int multi = 0;
        foreach (var x in all)
        {
            var m = t.ItemModels.Get(x.ItemPreset);
            if (m == null || m.SlotTypes.Count == 0) continue;
            int own = m.FirstSlotType == EquipmentSlots.OwnSlotGroup ? 5 : m.FirstSlotType;
            var slots = EquipmentSlots.SlotsOf(m.SlotTypes, own);
            bool outfit = x.EquipSubtype == 0x16, twoHander = x.EquipSubtype is (>= 0x32 and <= 0x38) or (>= 0x50 and <= 0x53);
            if (outfit && !(slots.Contains(8) && slots.Contains(9))) bad.Add($"{x.Id} outfit: {string.Join("+", slots)}");
            if (twoHander && !(slots.Contains(5) && slots.Contains(4))) bad.Add($"{x.Id} two-hander: {string.Join("+", slots)}");
            if (slots.Count < 2) continue;
            multi++;
            var record = new PcInfo { Equips = new[] { new EquipItem { Id = x.Id, Uid = 1, Slot = (EquipSlot)own, Amount = 1 } } };
            var worn = EquipmentSlots.Of(record, t.Items, t.ItemModels);
            int layer = x.HasSkinType ? 1 : 0;
            for (int slot = 0; slot < EquipmentSlots.SlotCount; slot++)
                if ((worn.At(slot, layer)?.Uid == 1) != slots.Contains(slot)) { bad.Add($"{x.Id} slot {slot}"); break; }
            string key = $"subtype {x.EquipSubtype}: {string.Join("+", slots.Select(ItemModelTable.SlotName))}";
            sets[key] = sets.GetValueOrDefault(key) + 1;
        }
        TestContext.WriteLine($"{d}: {all.Count} equipment items, {multi} take more than one slot");
        foreach (var (k, n) in sets.OrderBy(s => s.Key)) TestContext.WriteLine($"  {k}: {n}");
        Assert.That(multi, Is.GreaterThan(0));
        Assert.That(sets.Keys.Any(k => k.EndsWith(": CL+PA")) && sets.Keys.Any(k => k.EndsWith(": RH+LH")), Is.True, "outfits and two-handers in the data");
        Assert.That(bad, Is.Empty);
    }
}
