using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item data table and ASSlot's item path (ItemInfoSlot::vfunc_8 -> ASSlot::FUN_1409c3860) on the KR data
/// (needs MS2_DATA).</summary>
public class ItemSlotTests
{
    static PackSet packs;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
        }
        return packs;
    }

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };

    static Dictionary<string, object> Slot(UiModel model, Item item, int flags)
    {
        var l = new List<(string, object)>();
        Assert.That(ItemSlot.Build(l, item, false, flags, model), Is.True);
        var d = new Dictionary<string, object>();
        foreach (var (k, v) in l) d[k] = v;     // SetMember: a later value replaces
        return d;
    }

    [Test]
    public void ItemDataReadsPropertyLimitAndFunction()
    {
        var d = new GameTables(Packs(), "en").Items.Get(20300327);
        Assert.That(d, Is.Not.Null);
        Assert.That((d.Type, d.Rank, d.ItemGroup, d.SlotMax, d.Category), Is.EqualTo((25, 1, 2, 999u, "Package")));
        Assert.That(d.RecommendJobs, Is.EqualTo(new[] { 1, 10 }));
        Assert.That(d.FunctionName, Is.EqualTo("OpenItemBox"));
        // IsJobRecommended: job 1 counts as 10
        Assert.That(d.IsJobRecommended(1), Is.True);
        Assert.That(d.IsJobRecommended(10), Is.True);
        Assert.That(d.IsJobRecommended(20), Is.False);
        Assert.That(d.IconPath, Is.EqualTo("./Data/Resource/Image/item/icon/20300327.png"));
        Assert.That(new GameTables(Packs(), "en").Items.Get(1), Is.Null);
    }

    [Test]
    public void GenderAndJobLimitsAreTheClients()
    {
        var d = new ItemData();
        Assert.That((d.IsGenderAllowed(0), d.IsGenderAllowed(1)), Is.EqualTo((true, true)), "genderLimit 2: any");
        d.GenderLimit = 1;
        Assert.That((d.IsGenderAllowed(0), d.IsGenderAllowed(1)), Is.EqualTo((false, true)));
        Assert.That((d.IsJobAllowed(0), d.IsJobAllowed(30)), Is.EqualTo((false, true)), "job 0 never; an empty jobLimit any");
        d.JobLimit.Add(10); d.JobLimit.Add(20);
        Assert.That((d.IsJobAllowed(1), d.IsJobAllowed(20), d.IsJobAllowed(30)), Is.EqualTo((true, true, false)), "job 1 counts as 10");
        d.DisableJobLimit.Add(20);
        Assert.That(d.IsJobAllowed(20), Is.False, "a disabled job");
        d.JobLimit.Add(1);
        Assert.That(d.IsJobAllowed(30), Is.True, "a jobLimit entry of 1: any");
        var t = new GameTables(Packs(), "en").Items.Get(20300327);
        Assert.That((t.GenderLimit, t.JobLimit.Count), Is.EqualTo((2, 0)));
    }

    [Test]
    public void BindNewItemSlotHasTheItemAndItsCount()
    {
        var model = NewModel();
        var binder = new ItemSlotBinder(model, "LevelUpPackageWidget");
        string name = binder.BindNewItem(20300327, 0, 2);
        Assert.That(name, Is.EqualTo("LevelUpPackageWidget_1"));
        var slot = model.ItemSlots[name];
        Assert.That((slot.IconType, slot.Flags), Is.EqualTo((0x11, 0x800)));
        Assert.That(slot.Item.Rarity, Is.EqualTo(1));         // the data's rank over the given 0
        var s = Slot(model, slot.Item, slot.IconType);
        Assert.That(s["itemID"], Is.EqualTo(20300327u));
        Assert.That(s["itemGrade"], Is.EqualTo(1));
        Assert.That(s["index"], Is.EqualTo(-1));
        Assert.That(s["iconPath"], Is.EqualTo("./Data/Resource/Image/item/icon/20300327.png"));
        Assert.That((s["itemCash"], s["itemSkin"], s["itemPcBang"], s["itemVIP"], s["itemWedding"], s["itemPiece"]), Is.EqualTo((0, 0, 0, 0, 0, 0)));
        Assert.That((s["itemIconLock"], s["itemIconUnLock"]), Is.EqualTo((false, false)));
        Assert.That(s["itemCount"], Is.EqualTo("2"));
        Assert.That(s.ContainsKey("enchant") || s.ContainsKey("ugctag") || s.ContainsKey("itemSeason"), Is.False);
        Assert.That(binder.BindNewItem(1, 0, 1), Is.EqualTo(""));
        binder.Clear();
        Assert.That(model.ItemSlots, Is.Empty);
    }

    [Test]
    public void CountIsClampedAndOnlyForStackables()
    {
        var model = NewModel();
        var item = Item.Create(model.Tables.Items, 20300327, 0);
        item.SetCountClamped(5000);
        Assert.That(Slot(model, item, 0x10)["itemCount"], Is.EqualTo("999"));
    }

    [Test]
    public void GachaCoinShowsItsBoxSeason()
    {
        // 22201001 is box 1001001's coin; the box shows its season (KR 1) on the icon; its slot icon is a bare name
        var model = NewModel();
        var item = Item.Create(model.Tables.Items, 22201001, 0);
        var s = Slot(model, item, 0x11);
        Assert.That(s["itemSeason"], Is.EqualTo("1"));
        Assert.That(s["iconPath"], Is.EqualTo("./Data/Resource/Image/item/icon/newGacha_coin_ducky.png"));
        Assert.That(s["itemGrade"], Is.EqualTo(4));
    }

    [Test]
    public void UnportedFlagsThrow()
    {
        var model = NewModel();
        var item = Item.Create(model.Tables.Items, 20300327, 0);
        Assert.Throws<NotImplementedException>(() => ItemSlot.Build(new(), item, false, 0x11 | 0x20, model));
    }
}
