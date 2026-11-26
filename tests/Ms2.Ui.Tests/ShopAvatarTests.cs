using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using Ms2.Ui.Shop;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The gacha shop's avatar (UIShopDialog's m_spPreview, ItemPreview mode 1, uishopdialog.cpp:888; needs MS2_DATA).</summary>
public class ShopAvatarTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
        }
        return packs;
    }

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = true };

    // the equipment the gacha tests dress: a cap, clothes and a weapon of the shop fixture
    static readonly int[] Ids = { 11300001, 11400003, 13000003 };

    static Item Make(UiModel model, int id) => Item.Create(model.Tables.Items, id, 1);

    static ShopAvatarPreview NewPreview(UiModel model, int gender = 1)
    {
        var p = new ShopAvatarPreview(model.Tables.ItemModels) { Gender = () => gender, Now = () => 100 };
        return p;
    }

    static EquipItem Own(int id, int slot) => new() { Id = id, Uid = 900 + slot, Slot = (EquipSlot)slot };

    /// <summary>The ids of the items a preview can show on the avatar, from the fixture's.</summary>
    static List<Item> Dressable(UiModel model, ShopAvatarPreview p) =>
        Ids.Select(i => Make(model, i)).Where(i => i?.Data != null && p.IsDressed(i.Data) && i.Data.IsGenderAllowed(1)).ToList();

    [Test]
    public void InitWithItemsKeepsThePreviewableItemsWithOwnUids()
    {
        Packs();
        var model = NewModel();
        var p = NewPreview(model);
        var items = Dressable(model, p);
        Assert.That(items, Is.Not.Empty, "the data has dressable items");
        int changed = 0;
        p.Changed += () => changed++;
        var expired = Make(model, items[0].ItemId); expired.ExpiryTime = 50;         // before Now (100)
        var future = Make(model, items[0].ItemId); future.ExpiryTime = 500;
        p.InitWithItems(new[] { expired, future });
        Assert.That(p.Items.Select(i => i.ExpiryTime), Is.EqualTo(new long[] { 500 }), "an expired item is left out");
        Assert.That(changed, Is.EqualTo(1));

        p.InitWithItems(items);
        Assert.That(p.Items.Select(i => i.ItemId), Is.EqualTo(items.Select(i => i.ItemId)));
        Assert.That(p.Items.Select(i => i.Uid).Distinct().Count(), Is.EqualTo(items.Count), "each preview item has its own uid");
        Assert.That(p.Items.All(i => i.Uid != 0 && !ReferenceEquals(i, items[0])), Is.True, "copies (CItem::Clone)");

        var noPreview = Make(model, items[0].ItemId);
        noPreview.Data.DoNotPreview = 1;
        try { p.InitWithItems(new[] { noPreview }); Assert.That(p.Items, Is.Empty, "doNotPreview"); }
        finally { noPreview.Data.DoNotPreview = 0; }
    }

    [Test]
    public void TryOnReplacesTheItemOfTheSameSlotAndRefusesAWornItem()
    {
        Packs();
        var model = NewModel();
        var p = NewPreview(model);
        var items = Dressable(model, p);
        var first = items[0];
        Assert.That(p.TryOnItem(first), Is.True);
        Assert.That(p.IsItemWorn(first), Is.True);
        Assert.That(p.TryOnItem(first), Is.False, "IsItemWorn (@0x140d3fda0): the same id is not put on twice");
        Assert.That(p.Items, Has.Count.EqualTo(1));
        // another item of the same slot (a copy with another id and the same model slot) leaves the list's item
        var other = items.FirstOrDefault(i => i.ItemId != first.ItemId && p.SlotOf(i.Data) == p.SlotOf(first.Data));
        if (other != null)
        {
            Assert.That(p.TryOnItem(other), Is.True);
            Assert.That(p.Items.Select(i => i.ItemId), Is.EqualTo(new[] { other.ItemId }), "AddWornItem replaces the slot's item");
        }
    }

    [Test]
    public void DressPutsThePreviewItemsOverTheOwnItemsOfTheSlotsTheyCover()
    {
        Packs();
        var model = NewModel();
        var p = NewPreview(model);
        var items = Dressable(model, p);
        var item = items[0];
        var covered = model.Tables.ItemModels.ForItem(item.Data).SlotTypes.Select(t => t == 0x13 ? 5 : t).ToList();
        int free = Enumerable.Range(1, 60).First(s => !covered.Contains(s));
        var own = new List<EquipItem> { Own(111, covered[0]), Own(222, free) };

        Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 111, 222 }), "nothing worn: the player's own items");
        p.TryOnItem(item);
        var worn = p.Dress(own, null);
        Assert.That(worn.Select(e => e.Id), Is.EqualTo(new[] { 222, item.ItemId }), "the covered slot's own item is off, the preview item is on");
        Assert.That(worn.Last().Uid, Is.EqualTo(p.Items[0].Uid));
        Assert.That(worn.Last().Slot, Is.EqualTo((EquipSlot)p.Items[0].Slot));

        p.ShowMyEquip();                                                                 // onClickShowMyEquip
        Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 111, 222 }));
        p.TryOnItem(item);
        p.ResetAvatarOutfit();                                                           // onClickUnequipAll
        Assert.That(p.Items, Is.Empty);
        p.ShowMyEquip();
        p.TryOnItem(item);
        Assert.That(p.Unequip(p.Items[0].Uid), Is.True);                                 // OnUnEquip
        Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 111, 222 }), "the own item shows again");
        Assert.That(p.Unequip(12345), Is.False);
    }

    [Test]
    public void UnequipAllLeavesTheSlotsThePreviewCoveredBareUntilShowMyEquip()
    {
        // onClickUnequipAll -> ResetAvatarOutfit -> UIAvatarPC::ClearPreview (uiavatarbase.cpp:1214) -> Equipment::ClearPreview
        // (customize.cpp:1315): the preview items come off, the own items they had taken off stay off. onClickShowMyEquip -> RestoreAll
        // (customize.cpp:1753) puts them back.
        Packs();
        var model = NewModel();
        var p = NewPreview(model);
        var item = Dressable(model, p)[0];
        var covered = model.Tables.ItemModels.ForItem(item.Data).SlotTypes.Select(t => t == 0x13 ? 5 : t).ToList();
        int free = Enumerable.Range(1, 60).First(s => !covered.Contains(s));
        var own = new List<EquipItem> { Own(111, covered[0]), Own(222, free) };

        p.TryOnItem(item);
        p.ResetAvatarOutfit();
        Assert.That(p.Items, Is.Empty);
        Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 222 }), "Unequip All: the covered slot stays bare");
        Assert.That(p.BareSlots, Is.EquivalentTo(covered));

        p.TryOnItem(item);                                                               // a new try-on dresses the bare slot again
        Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 222, item.ItemId }));
        p.Unequip(p.Items[0].Uid);
        Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 222 }), "still bare: only RestoreAll brings the own item back");

        p.ShowMyEquip();
        Assert.That(p.BareSlots, Is.Empty);
        Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 111, 222 }), "Show My Equip: the own items again");

        p.TryOnItem(item);
        p.ResetAvatarOutfit();
        p.InitWithItems(null);                                                           // CreatePcAvatar: a new copy of the player
        Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 111, 222 }), "a new avatar starts from the own look");
    }

    [Test]
    public void ResetAndShowMyEquipResetTheCamera()
    {
        // ResetAvatarOutfit (@0x140d3f9a0) and FUN_140d3fa30 (@0x140d3fa30) end in ResetCamera (@0x140d3e130)
        Packs();
        var model = NewModel();
        var (h, c) = LoadGacha(model, true);
        c.OnCallback("OnAvatarMouseDown", new object[] { 1 });
        c.OnCallback("OnAvatarMouseMove", new object[] { 40, 20, 1 });
        var turned = c.Avatar.Angle;
        Assert.That(turned, Is.Not.EqualTo((0f, 0f, 90f)));
        int resets = 0, turns = 0;
        c.Avatar.CameraReset += () => resets++;
        c.Avatar.Turned += () => turns++;
        c.OnCallback("onClickUnequipAll", Array.Empty<object>());
        Assert.That(c.Avatar.Angle, Is.EqualTo((0f, 0f, 90f)), "the angle is back at the start");
        Assert.That((resets, turns), Is.EqualTo((1, 1)));
        c.OnCallback("OnAvatarMouseDown", new object[] { 1 });
        c.OnCallback("OnAvatarMouseMove", new object[] { 40, 20, 1 });
        c.OnCallback("onClickShowMyEquip", Array.Empty<object>());
        Assert.That(c.Avatar.Angle, Is.EqualTo((0f, 0f, 90f)));
        Assert.That(resets, Is.EqualTo(2));
    }

    [Test]
    public void ASecondLoadOfTheDialogKeepsItsPreviewAndACloseDropsIt()
    {
        // uishopdialog.cpp:888 makes the preview only while m_spPreview is 0; the render target never outlives the dialog (vfunc_26)
        Packs();
        var model = NewModel();
        var (h, c) = LoadGacha(model, true);
        var preview = c.Preview;
        var avatar = c.Avatar;
        Assert.That(preview, Is.Not.Null);
        c.OnLoadComplete();
        Assert.That(c.Preview, Is.SameAs(preview), "no second preview over the first");
        Assert.That(c.Avatar, Is.SameAs(avatar));

        c.CloseAvatar();                                                                 // the host took the movie away: no Close
        Assert.That(c.Preview, Is.Null);
        Assert.That(c.Avatar, Is.Null);
        Assert.That(c.PreviewIconSlots, Is.Empty);
        int turned = 0;
        avatar.Turned += () => turned++;
        preview.TryOnItem(Dressable(model, preview)[0]);                                 // the old preview no longer reaches the controller
        c.OnCallback("OnAvatarMouseDown", new object[] { 1 });
        Assert.That(turned, Is.Zero);
        Assert.That(c.PreviewIconSlots, Is.Empty, "the dropped preview adds no icons");

        c.OnLoadComplete();
        Assert.That(c.Preview, Is.Not.Null.And.Not.SameAs(preview), "a new dialog load makes a new preview");
        Assert.That(c.Avatar, Is.Not.SameAs(avatar));
    }

    [Test]
    public void AnItemNoPreviewItemCanBeMadeOfStillGetsItsIconAndLeavesTheAvatarAlone()
    {
        // TryOnItem (@0x140d3f400) returns true after the IsItemWorn check even when CreatePreviewItem gives null (itempreview.cpp:685-720)
        Packs();
        var model = NewModel();
        var p = NewPreview(model);
        var item = Dressable(model, p)[0];
        var own = new List<EquipItem> { Own(111, 3) };
        string fn = item.Data.FunctionName;
        try
        {
            item.Data.FunctionName = "ItemChangeBeauty";                                 // CreatePreviewItem's package branch: not ported, null
            Assert.That(p.TryOnItem(item, out var shown), Is.True);
            Assert.That(shown, Is.SameAs(item), "the product's own icon");
            Assert.That(p.Items, Is.Empty);
            Assert.That(p.Dress(own, null).Select(e => e.Id), Is.EqualTo(new[] { 111 }));
        }
        finally { item.Data.FunctionName = fn; }
    }

    [Test]
    public void AnItemWithNoModelSlotIsListedButNotDressed()
    {
        Packs();
        var model = NewModel();
        var p = NewPreview(model);
        var d = model.Tables.Items.Get(Ids[0]);
        int type = d.Type;
        try
        {
            d.Type = 0x18;                       // an emotion item: ApplyItemToAvatar's emotion branch (not ported), the icon only
            Assert.That(p.IsDressed(d), Is.False);
            Assert.That(p.Dress(new[] { Own(1, 3) }, null).Select(e => e.Id), Is.EqualTo(new[] { 1 }));
        }
        finally { d.Type = type; }
    }

    [TestCase(false, false, 5)]
    [TestCase(true, false, 5)]
    [TestCase(false, true, 4)]
    [TestCase(true, true, 4)]
    public void TheHandSlotOfADualWieldItemFollowsTheIconsHeld(bool slot4Taken, bool slot5Taken, int expected)
    {
        // GetHandSlot (@0x140e2a7e0): both free 5; only 4 free 4; only 5 free 5; both held: m_70 (0 in the shop) is not 4, so 4
        var d = new ItemData { Type = 1, Subtype = 0x1f };
        Assert.That(ShopAvatarPreview.HandSlot(d, slot4Taken, slot5Taken, 0), Is.EqualTo(expected));
        Assert.That(ShopAvatarPreview.HandSlot(new ItemData { Type = 1, Subtype = 3 }, false, false, 0), Is.Zero, "not dual-wield");
        Assert.That(ShopAvatarPreview.HandSlot(d, true, true, 4), Is.EqualTo(5));
    }

    // ---- the dialog ----------------------------------------------------------------------------------------------------------

    static ShopProduct[] MainProducts() => Ids.Select((id, n) => new ShopProduct
    {
        Serial = 1 + n, ItemId = id, Rarity = 1, Price = 1000, OriginalPrice = 1000, Count = 1, Categories = "CP", MainItem = true,
    }).ToArray();

    static (UiHost, ShopController) LoadGacha(UiModel model, bool main)
    {
        int shopId = model.Tables.GachaBoxes.ShopIds().First();
        var c = new ShopController { Categories = new ShopCategoryTable(Packs()), Npcs = new Ms2.Formats.World.NpcTable(Packs()), ItemCount = (_, _) => 5 };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        c.ReadShopInfo(new ShopInfo { NpcId = 0, ShopId = shopId, ShopType = 4, ProductCount = 3, TabShopId = 5, Icon = "" });
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        var products = MainProducts();
        if (!main) foreach (var pr in products) pr.MainItem = false;
        c.ReadProducts(products);
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(rt.Errors, Is.Empty, string.Join("; ", rt.Errors));
        return (h, c);
    }

    [Test]
    public void TheGachaDialogMakesItsPreviewAndAvatarAndTheMouseCallsTurnIt()
    {
        Packs();
        var model = NewModel();
        var (h, c) = LoadGacha(model, true);
        Assert.That(c.Preview, Is.Not.Null, "uishopdialog.cpp:888: new ItemPreview(pMyPC, this, 1)");
        Assert.That(c.Avatar, Is.Not.Null);
        var before = c.Avatar.Angle;
        Assert.That(c.OnCallback("OnAvatarMouseDown", new object[] { 1 }), Is.True);
        Assert.That(c.OnCallback("OnAvatarMouseMove", new object[] { 20, 0, 1 }), Is.True);
        Assert.That(c.Avatar.Angle, Is.Not.EqualTo(before), "a drag with the right button turns the avatar");
        int zooms = 0;
        c.Avatar.Zoom += _ => zooms++;
        Assert.That(c.OnCallback("OnAvatarMouseWheel", new object[] { 1 }), Is.True);
        Assert.That(zooms, Is.EqualTo(1));
        Assert.That(h.RT.Errors, Is.Empty);

        c.Close();
        Assert.That(c.Preview, Is.Null);
        Assert.That(c.Avatar, Is.Null, "the avatar goes with the dialog");
        Assert.That(c.OnCallback("OnAvatarMouseWheel", new object[] { 1 }), Is.False, "no avatar: the call is not the dialog's");
    }

    [Test]
    public void TheMainItemsAreTheAvatarsFirstOutfitAndTheButtonsChangeIt()
    {
        Packs();
        var model = NewModel();
        var (h, c) = LoadGacha(model, true);
        var preview = c.Preview;
        var dressable = Dressable(model, preview);
        int changed = 0;
        preview.Changed += () => changed++;
        c.OnCallback("onClickShowMainItems", Array.Empty<object>());                      // RefreshCategories: InitWithItems
        Assert.That(changed, Is.GreaterThan(0));
        Assert.That(preview.Items.Select(i => i.ItemId), Is.SubsetOf(Ids));
        Assert.That(preview.Items.Select(i => i.ItemId), Is.EquivalentTo(dressable.Select(i => i.ItemId)), "the main items the avatar can wear");

        c.OnCallback("onClickUnequipAll", Array.Empty<object>());
        Assert.That(preview.Items, Is.Empty);
        Assert.That(c.PreviewIconSlots, Is.Empty);

        // PreviewGachaItem: a try-on adds the item, and its icon, once
        var item = dressable[0];
        int serial = Array.FindIndex(Ids, i => i == item.ItemId) + 1;
        var row = h.Obj(("serialID", serial), ("productType", 0), ("itemID", item.ItemId));
        Assert.That(c.OnCallback("OnUsePreview", new object[] { row }), Is.True);
        Assert.That(preview.Items.Select(i => i.ItemId), Is.EqualTo(new[] { item.ItemId }));
        Assert.That(c.PreviewIconSlots, Has.Count.EqualTo(1), "AddPreviewIcon: the item's icon in m_equipContainer");
        int clipChildren = (int)Rt.ToNumber(h.Get(h.Get(c.ShopView, "m_equipContainer"), "numChildren"));
        Assert.That(clipChildren, Is.EqualTo(1), "the movie's container holds the icon");
        Assert.That(c.OnCallback("OnUsePreview", new object[] { row }), Is.True);
        Assert.That(preview.Items, Has.Count.EqualTo(1), "the same item is not put on twice");
        Assert.That(c.PreviewIconSlots, Has.Count.EqualTo(1));

        // OnUnEquip: off the avatar and the container
        int slot = c.PreviewIconSlots.First();
        Assert.That(c.OnCallback("OnUnEquip", new object[] { slot.ToString(), h.Obj(("itemGUID", preview.Items[0].Uid.ToString())) }), Is.True);
        Assert.That(preview.Items, Is.Empty);
        Assert.That(c.PreviewIconSlots, Is.Empty);
        Assert.That((int)Rt.ToNumber(h.Get(h.Get(c.ShopView, "m_equipContainer"), "numChildren")), Is.Zero);

        c.OnCallback("OnUsePreview", new object[] { row });
        c.OnCallback("onClickShowMyEquip", Array.Empty<object>());
        Assert.That(preview.Items, Is.Empty);
        Assert.That(c.PreviewIconSlots, Is.Empty);
        Assert.That(h.RT.Errors, Is.Empty, string.Join("; ", h.RT.Errors));
    }

    [Test]
    public void AnIdleAvatarPreviewControllerAllocatesNothingPerTick()
    {
        Packs();
        var model = NewModel();
        var (h, c) = LoadGacha(model, true);
        for (int i = 0; i < 30; i++) h.Player.Tick();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) { c.Process(); }
        long per = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;
        Assert.That(per, Is.LessThanOrEqualTo(64), "the dialog's own per-frame work does not build the avatar again");
    }
}
