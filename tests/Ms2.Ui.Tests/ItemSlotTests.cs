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
        var t = new GameTables(Packs(), "en");
        var d = t.Items.Get(20300327);
        Assert.That(d, Is.Not.Null);
        // KMS itemdata property@type=25/rank=1; GMS2 maps OpenItemBox type=10/subtype=17 to type 25 and constant=1 grade to rank 1.
        Assert.That((d.Type, d.Rank, d.ItemGroup, d.SlotMax, d.Category), Is.EqualTo((25, 1, 2, 999u, "Package")));
        Assert.That(d.RecommendJobs, Is.EqualTo(new[] { 1, 10 }));
        Assert.That(d.FunctionName, Is.EqualTo("OpenItemBox"));
        // IsJobRecommended: job 1 counts as 10
        Assert.That(d.IsJobRecommended(1), Is.True);
        Assert.That(d.IsJobRecommended(10), Is.True);
        Assert.That(d.IsJobRecommended(20), Is.False);
        Assert.That(d.IconPath, Is.EqualTo("./Data/Resource/Image/item/icon/20300327.png"));
        int missing = UiDataRows.MissingItemId(t.Items);
        if (UiDataRows.IsKr(Packs())) Assert.That(missing, Is.EqualTo(1));
        Assert.That(t.Items.Get(missing), Is.Null);
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
        // the data's rank over the given 0 (both KMS itemdata and GMS2 constant=1 give rank 1)
        Assert.That(slot.Item.Rarity, Is.EqualTo(1));
        var s = Slot(model, slot.Item, slot.IconType);
        Assert.That(s["itemID"], Is.EqualTo(20300327u));
        Assert.That(s["itemGrade"], Is.EqualTo(1));
        Assert.That(s["index"], Is.EqualTo(-1));
        Assert.That(s["iconPath"], Is.EqualTo("./Data/Resource/Image/item/icon/20300327.png"));
        Assert.That((s["itemCash"], s["itemSkin"], s["itemPcBang"], s["itemVIP"], s["itemWedding"], s["itemPiece"]), Is.EqualTo((0, 0, 0, 0, 0, 0)));
        Assert.That((s["itemIconLock"], s["itemIconUnLock"]), Is.EqualTo((false, false)));
        Assert.That(s["itemCount"], Is.EqualTo("2"));
        Assert.That(s.ContainsKey("enchant") || s.ContainsKey("ugctag") || s.ContainsKey("itemSeason"), Is.False);
        int missing = UiDataRows.MissingItemId(model.Tables.Items);
        if (UiDataRows.IsKr(Packs())) Assert.That(missing, Is.EqualTo(1));
        Assert.That(binder.BindNewItem(missing, 0, 1), Is.EqualTo(""));
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
        // KR: 22201001 is box 1001001's coin; the box shows its season (1) on the icon; its slot icon is a bare name.
        // GMS2 NA: box 1001001 has no randomBoxSeasonNA; box 1001004 (coin 22201004, season 1) is the first with a season on its icon.
        var model = NewModel();
        var (coin, box, season) = UiDataRows.SeasonCoin(Packs()) ?? throw new AssertionException("no gacha box shows a season in this data");
        var prop = UiDataRows.ItemPart(Packs(), coin, "property");
        string rawIcon = (string)prop?.Attribute("slotIcon") ?? "";
        string expectedIcon = rawIcon.Length == 0 ? $"./Data/Resource/Image/item/icon/{coin}.png"
            : rawIcon[0] == '.' ? rawIcon : $"./Data/Resource/Image/item/icon/{rawIcon}.png";
        var item = Item.Create(model.Tables.Items, coin, 0);
        var s = Slot(model, item, 0x11);
        Assert.That(s["itemSeason"], Is.EqualTo(season));
        Assert.That(s["iconPath"], Is.EqualTo(expectedIcon));
        Assert.That(s["itemGrade"], Is.EqualTo(4));
        if (UiDataRows.IsKr(Packs()))
        {
            Assert.That((coin, box, season), Is.EqualTo((22201001, 1001001, "1")));
            Assert.That(s["iconPath"], Is.EqualTo("./Data/Resource/Image/item/icon/newGacha_coin_ducky.png"));
        }
        else
        {
            Assert.That((coin, box, season), Is.EqualTo((22201004, 1001004, "1")));
            Assert.That(s["iconPath"], Is.EqualTo("./Data/Resource/Image/item/icon/newGacha_coin_ducky.png"));
        }
    }

    [Test]
    public void LapenshardPanelFlagsSetDropShadowEnchantAndGradeBg()
    {
        var model = NewModel();
        var item = Item.Create(model.Tables.Items, 20300327, 0);
        // asitemslot.cpp:1372-1381: 0x2000 DisableDropShadow, 0x20000 ClearEnchant, 0x40000 ClearGradeBG
        var s = Slot(model, item, 0x11 | 0x2000 | 0x20000 | 0x40000);
        Assert.That((s["dropShadowDisable"], s["enchant"], s["gradeBG"]), Is.EqualTo((1, 0, 0)));
        var plain = Slot(model, item, 0x11);
        Assert.That(plain.ContainsKey("dropShadowDisable") || plain.ContainsKey("enchant") || plain.ContainsKey("gradeBG"), Is.False);
    }

    [Test]
    public void UnportedFlagsThrow()
    {
        var model = NewModel();
        var item = Item.Create(model.Tables.Items, 20300327, 0);
        Assert.Throws<NotImplementedException>(() => ItemSlot.Build(new(), item, false, 0x11 | 0x20, model));
    }
}
