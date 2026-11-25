using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The worn items a list shows (CEquipment::FUN_1411e3000 @0x1411e3000) and the colouring dialog's rows (needs MS2_DATA).</summary>
public class WornListedTests
{
    static PackSet packs;
    static GameTables Tables()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
        }
        return new GameTables(packs, "en");
    }

    static EquipItem E(int id, long uid, EquipSlot slot) => new() { Id = id, Uid = uid, Slot = slot, Rarity = 1 };
    static WornEquipment Worn(GameTables t, params EquipItem[] e) => WornEquipment.Of(new PcInfo { Equips = e }, t.Items);
    static List<long> Uids(IEnumerable<Item> l) => l.Select(i => i.Uid).ToList();
    static readonly Func<Item, bool> Any = _ => true;

    [Test]
    public void TheTopLayerOfASlotIsListedAndTheLayerBelowOnlyWhenTheTopIsUnusable()
    {
        var t = Tables();
        var w = Worn(t, E(11400001, 1, EquipSlot.CL), E(11400021, 2, EquipSlot.CL));
        var none = new HashSet<Item>();
        Assert.That(Uids(w.Listed(4, true, false, true, Any, none, t.Items, t.ItemModels)), Is.EqualTo(new long[] { 2 }));
        Assert.That(Uids(w.Listed(4, true, false, true, i => i.Uid != 2, none, t.Items, t.ItemModels)), Is.EqualTo(new long[] { 1 }));
        Assert.That(Uids(w.Listed(1, true, false, true, Any, none, t.Items, t.ItemModels)), Is.EqualTo(new long[] { 1 }), "maxLayer 1 stops at the base");
    }

    [Test]
    public void BothHandsAreListedAndATwoHandedItemInTheLeftSlotIsNot()
    {
        var t = Tables();
        var none = new HashSet<Item>();
        var w = Worn(t, E(13000001, 1, EquipSlot.RH), E(14000001, 2, EquipSlot.LH));
        Assert.That(Uids(w.Listed(4, true, false, true, Any, none, t.Items, t.ItemModels)), Is.EquivalentTo(new long[] { 1, 2 }));
        var two = Worn(t, E(15300001, 3, EquipSlot.LH));
        Assert.That(two.Listed(4, true, false, true, Any, none, t.Items, t.ItemModels), Is.Empty);
    }

    [Test]
    public void AMissMatchTaggedItemIsSkippedUnlessTheWearerIsAClone()
    {
        var t = Tables();
        var w = Worn(t, E(11400001, 1, EquipSlot.CL));
        var item = w.Listed(4, true, false, false, Any, null, t.Items, t.ItemModels)[0];
        var tagged = new HashSet<Item> { item };
        Assert.That(w.Listed(4, true, false, true, Any, tagged, t.Items, t.ItemModels), Is.Empty, "own player: the tag counts");
        Assert.That(w.Listed(4, true, false, false, Any, tagged, t.Items, t.ItemModels), Has.Count.EqualTo(1), "a ClonePC ignores it");
    }

    [Test]
    public void NoHandItemsMeansNothingIsTagged()
    {
        var t = Tables();
        Assert.That(Worn(t, E(11400001, 1, EquipSlot.CL)).MissMatched(0, true, Any, t.ItemModels), Is.Empty);
        Assert.That(Worn(t, E(13000001, 1, EquipSlot.RH)).MissMatched(0, false, i => false, t.ItemModels), Is.Empty, "no layer-1 item to tag");
    }

    [Test]
    public void FitsFollowsTheGenderAndTheWeaponCategory()
    {
        var t = Tables();
        var cl = t.Items.Get(11400001);
        if (cl.GenderLimit != 2)
        {
            Assert.That(WornEquipment.Fits(cl, cl.GenderLimit, 0, t.ItemModels), Is.True);
            Assert.That(WornEquipment.Fits(cl, 1 - cl.GenderLimit, 0, t.ItemModels), Is.False);
        }
        var rh = t.Items.Get(13000001);
        Assert.That(WornEquipment.Fits(rh, 2, rh.EquipSubtype + 1000, t.ItemModels), Is.False, "another weapon category fails");
    }

    [Test]
    public void TheColouringRowsNeedTheModelTextureTestAndOnlyCustomizableItemsPass()
    {
        var t = Tables();
        var w = Worn(t, E(11400001, 1, EquipSlot.CL), E(11400021, 2, EquipSlot.CL));
        string N(int id) => "n" + id;
        Assert.That(ItemColoringDialogController.WornRows(w, 0, false, false, Any, null, N, t.Items, t.ItemModels), Is.Empty,
            "no model test (not ported): nothing is listed");
        Assert.That(ItemColoringDialogController.WornRows(null, 0, false, false, Any, Any, N, t.Items, t.ItemModels), Is.Empty);
        foreach (var r in ItemColoringDialogController.WornRows(w, 0, false, false, Any, Any, N, t.Items, t.ItemModels))
        {
            var d = t.Items.Get(r.ItemId);
            Assert.That(WornEquipment.IsCustomizable(d, t.ItemModels), Is.True);
            Assert.That(WornEquipment.IsCustomizeKind(d, t.ItemModels), Is.False);
            Assert.That(r.Name, Is.EqualTo(N(r.ItemId)));
        }
        Assert.That(ItemColoringDialogController.WornRows(w, 0, false, false, Any, _ => false, N, t.Items, t.ItemModels), Is.Empty,
            "an item without a textured mesh is skipped");
    }

    [Test]
    public void TheAvatarColourItemIsACustomizeKind()
    {
        var t = Tables();
        Assert.That(WornEquipment.IsCustomizeKind(new ItemData { Id = WornEquipment.AvatarColorItemId }, t.ItemModels), Is.True);
        Assert.That(WornEquipment.IsCustomizeKind(null, t.ItemModels), Is.False);
    }
}
