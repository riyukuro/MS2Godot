using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CItemData::FUN_14053d2b0 / FUN_14053d460 / FUN_14053d340 and FUN_141420f10 on synthetic item data.</summary>
public class BeautyItemSlotsTests
{
    static ItemModelTable.Model Model(params int[] types) => new(false, 0, new int[0], types);

    static (BeautyItemSlots s, Dictionary<int, ItemData> items) Make()
    {
        var items = new Dictionary<int, ItemData>();
        var models = new Dictionary<int, ItemModelTable.Model> { [1] = Model(8), [2] = Model(5, 4), [3] = Model(5), [4] = Model(1) };
        return (new BeautyItemSlots(id => items.GetValueOrDefault(id), p => models.GetValueOrDefault(p)), items);
    }

    [Test]
    public void TopBottomIsSubtype0x16AndGoesOnSlot8()
    {
        var (s, items) = Make();
        items[10] = new ItemData { Type = 1, Subtype = 0x16, ItemPreset = 4 };
        Assert.That(s.IsTopBottom(items[10]), Is.True);
        Assert.That((int)s.SlotOfId(10), Is.EqualTo(8));
        Assert.That(s.IsTwoHanded(items[10]), Is.False);
    }

    [TestCase(0x32, true), TestCase(0x38, true), TestCase(0x39, false), TestCase(0x4f, false), TestCase(0x50, true), TestCase(0x53, true), TestCase(0x54, false)]
    public void TwoHandedSubtypes(int subtype, bool expected)
    {
        var (s, _) = Make();
        Assert.That(s.IsTwoHanded(new ItemData { Type = 1, Subtype = subtype }), Is.EqualTo(expected));
    }

    [Test]
    public void ATypeSeventeenItemIsTwoHandedWhenItsModelHasBothHands()
    {
        var (s, _) = Make();
        Assert.That(s.IsTwoHanded(new ItemData { Type = 0x17, ItemPreset = 2 }), Is.True);
        Assert.That(s.IsTwoHanded(new ItemData { Type = 0x17, ItemPreset = 3 }), Is.False);
        Assert.That(s.IsHandItem(new ItemData { Type = 0x17, ItemPreset = 3 }), Is.True);
        Assert.That(s.IsHandItem(new ItemData { Type = 0x17, ItemPreset = 2 }), Is.False);
    }

    [Test]
    public void AnOrdinaryItemGoesOnItsModelsFirstGroup()
    {
        var (s, items) = Make();
        items[20] = new ItemData { Type = 1, Subtype = 5, ItemPreset = 4 };
        Assert.That((int)s.SlotOfId(20), Is.EqualTo(1));
        Assert.That((int)s.SlotOfId(99), Is.EqualTo(0));
    }

    [Test]
    public void ATwoHandedItemIsSlot5AndAPlainHandItemTakesItsModelsFirstGroupWithoutWornState()
    {
        var (s, items) = Make();
        items[30] = new ItemData { Type = 1, Subtype = 0x32, ItemPreset = 2 };
        Assert.That((int)s.SlotOfId(30), Is.EqualTo(5));
        items[31] = new ItemData { Type = 1, Subtype = 0x1e, ItemPreset = 3 };
        Assert.That((int)s.SlotOfId(31), Is.EqualTo(5));
        s.WornPairSlot = _ => 4;
        Assert.That((int)s.SlotOfId(30), Is.EqualTo(5), "worn state does not change a two-handed item");
        Assert.That((int)s.SlotOfId(31), Is.EqualTo(5));
    }

    [TestCase(0x1f), TestCase(0x22)]
    public void ASubtype0x1fOr0x22ItemNeedsTheWornPairAnswer(int subtype)
    {
        var (s, items) = Make();
        items[32] = new ItemData { Type = 1, Subtype = subtype, ItemPreset = 3 };
        Assert.That((int)s.SlotOfId(32), Is.EqualTo(0));
        Assert.That(s.NewItem(32), Is.Null, "slot 0 is the skin slot: no preview item");
        s.WornPairSlot = _ => 4;
        Assert.That((int)s.SlotOfId(32), Is.EqualTo(4));
        s.WornPairSlot = _ => 0;
        Assert.That((int)s.SlotOfId(32), Is.EqualTo(5), "no answer: the model's first group");
    }

    [Test]
    public void ANewPreviewItemTakesTheDataRankAndTheSlotOfItsId()
    {
        var (s, items) = Make();
        items[40] = new ItemData { Type = 1, Subtype = 0x16, ItemPreset = 4, Rank = 3 };
        items[41] = new ItemData { Type = 1, Subtype = 5, ItemPreset = 4 };
        var a = s.NewItem(40);
        Assert.That((a.Id, a.Rarity, (int)a.Slot), Is.EqualTo((40, 3, 8)));
        Assert.That(s.NewItem(41).Rarity, Is.EqualTo(1));
        Assert.That(s.NewItem(99), Is.Null);
    }
}
