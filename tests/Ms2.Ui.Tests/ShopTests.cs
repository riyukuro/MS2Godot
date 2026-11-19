using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Shop;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The npc shop (uishopdialog) run by its own ActionScript with UIShopDialog's C++ side (needs MS2_DATA).</summary>
public class ShopTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = true };

    /// <summary>A header in the shape of an npc equipment shop: category 5, three products, a restock for 1,000 mesos.</summary>
    static ShopInfo Header() => new()
    {
        NpcId = 11000004, ShopId = 106, ResetTime = 0, ProductCount = 3, TabShopId = 5, ResetEnable = true, ResetPrice = 1000,
        ResetCurrency = 0, HideReset = false, Icon = "", CheckEnableItems = false,
    };

    static ShopProduct[] Products() => new[]
    {
        new ShopProduct { Serial = 1, ItemId = 11300001, Rarity = 1, Price = 1830, OriginalPrice = 1830, Count = 1, Categories = "CP" },
        new ShopProduct { Serial = 16, ItemId = 11400003, Rarity = 1, Price = 2360, OriginalPrice = 2360, Count = 1, Categories = "CL" },
        new ShopProduct { Serial = 91, ItemId = 13000003, Rarity = 1, Price = 1990, OriginalPrice = 2400, Count = 1, Categories = "RH" },
    };

    static (UiHost, ShopController) Load(UiModel model)
    {
        var c = new ShopController { Categories = new ShopCategoryTable(Packs()), Npcs = new Ms2.Formats.World.NpcTable(Packs()) };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        c.ReadShopInfo(Header());
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        c.ReadProducts(Products());
        for (int i = 0; i < 5; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    [Test]
    public void TheCategoryTableNestsTheEquipmentTabs()
    {
        var t = new ShopCategoryTable(Packs()).GetTabs(5);
        Assert.That(t, Is.Not.Null.And.Not.Empty);
        var all = t[0];
        // the parent tab's category grows by its sub tabs' (LoadTabs)
        Assert.That(all.SubTabs.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(all.SubTabs.Select(s => s.NameId).Take(2), Is.EqualTo(new[] { 2017, 2018 }));
        foreach (var s in all.SubTabs.Where(s => s.Category.Length > 0))
            Assert.That(all.Category.Split(','), Is.SupersetOf(s.Category.Split(',')));
    }

    [Test]
    public void IsCategoryInListMatchesWholeTokensIgnoringCase()
    {
        Assert.That(ShopController.IsCategoryInList("cp", "OP,CL,CP"), Is.True);
        Assert.That(ShopController.IsCategoryInList("C", "OP,CL,CP"), Is.False);
        Assert.That(ShopController.IsCategoryInList("CP", ""), Is.False);
    }

    [Test]
    public void ProductsFillTheTabsAndTheFirstCategoryIsListed()
    {
        Packs();
        var (h, c) = Load(NewModel());
        var rows = c.Tabs(0);
        Assert.That(rows.Count, Is.GreaterThanOrEqualTo(3));
        Assert.That((rows[0].Depth, rows[0].Products.Count), Is.EqualTo((0, 3)), "the top tab takes every product");
        var weapons = rows.First(r => r.Tab.NameId == 2017);
        var armor = rows.First(r => r.Tab.NameId == 2018);
        Assert.That((weapons.Depth, weapons.Products.Select(p => p.Record.Serial)), Is.EqualTo((1, new[] { 91 })));
        Assert.That((armor.Depth, armor.Products.Select(p => p.Record.Serial)), Is.EqualTo((1, new[] { 1, 16 })));
        // the category list selects its first row, which calls OnChangeCategory(0) and builds the list
        Assert.That(c.Category, Is.EqualTo(0));
        var list = h.Get(c.ShopView, "m_list");
        var dp = h.Get(list, "dataProvider");
        Assert.That(Rt.ToNumber(h.Get(dp, "length")), Is.EqualTo(3));
        // the discounted product carries its original price and the percent off (100 - 1990 * 100 / 2400 = 18)
        object weapon = null;
        for (int i = 0; i < 3; i++) { var r = h.Get(dp, i.ToString()); if (Rt.ToNumber(h.Get(r, "serialID")) == 91) weapon = r; }
        Assert.That(weapon, Is.Not.Null);
        Assert.That(Rt.ToNumber(h.Get(weapon, "price")), Is.EqualTo(2400));
        Assert.That(Rt.ToString(h.Get(weapon, "priceText")), Is.EqualTo("2,400"));
        Assert.That(Rt.ToString(h.Get(weapon, "bgSaleText")), Does.Contain("18"));
        Assert.That(Rt.ToString(h.Get(weapon, "salePriceText")), Does.Contain("export_common_icon_meso_h21"));
        Assert.That(Rt.ToString(h.Get(weapon, "label")), Does.StartWith("<FONT color='#"));
        Assert.That(Rt.ToString(h.Get(weapon, "bgFrame")), Is.EqualTo("normal"));
    }

    /// <summary>The product row's slot member for the serial (the list's data provider rows).</summary>
    static object RowMember(UiHost h, ShopController c, int serial, string name)
    {
        var dp = h.Get(h.Get(c.ShopView, "m_list"), "dataProvider");
        for (int i = 0; i < (int)Rt.ToNumber(h.Get(dp, "length")); i++)
        {
            var r = h.Get(dp, i.ToString());
            if (Rt.ToNumber(h.Get(r, "serialID")) == serial) return h.Get(r, name);
        }
        Assert.Fail($"no row for serial {serial}");
        return null;
    }

    [Test]
    public void ProductsTheLocalPlayerCannotUseAreMarked()
    {
        Packs();
        // GetItemUsableState: 11300001 has levelLimit 7, so a level 5 player sees it locked (error 1 -> itemLock)
        var low = NewModel();
        low.Player.Level = 5; low.Player.JobGroup = 10;
        (low.ItemSlotSources ??= new()).UsableState = new ItemUse { Player = low.Player }.UsableState;
        var (h, c) = Load(low);
        Assert.That(Rt.ToNumber(RowMember(h, c, 1, "itemLock")), Is.EqualTo(1));
        Assert.That(Rt.ToNumber(RowMember(h, c, 1, "disable")), Is.NaN);
        // job 0 is never allowed (ItemLimit::IsJobAllowed): error 3 -> disable, the red X
        var nojob = NewModel();
        nojob.Player.Level = 21; nojob.Player.JobGroup = 0;
        (nojob.ItemSlotSources ??= new()).UsableState = new ItemUse { Player = nojob.Player }.UsableState;
        (h, c) = Load(nojob);
        Assert.That(Rt.ToNumber(RowMember(h, c, 1, "disable")), Is.EqualTo(1));
        Assert.That(Rt.ToNumber(RowMember(h, c, 1, "itemLock")), Is.NaN);
        // a level 21 Knight can use it: neither member
        var ok = NewModel();
        ok.Player.Level = 21; ok.Player.JobGroup = 10;
        (ok.ItemSlotSources ??= new()).UsableState = new ItemUse { Player = ok.Player }.UsableState;
        (h, c) = Load(ok);
        Assert.That(Rt.ToNumber(RowMember(h, c, 1, "disable")), Is.NaN);
        Assert.That(Rt.ToNumber(RowMember(h, c, 1, "itemLock")), Is.NaN);
    }

    [Test]
    public void ChangingTheCategoryListsItsProducts()
    {
        Packs();
        var (h, c) = Load(NewModel());
        int weapons = c.Tabs(0).ToList().FindIndex(r => r.Tab.NameId == 2017);
        Assert.That(c.OnCallback("OnChangeCategory", new object[] { weapons }), Is.True);
        var dp = h.Get(h.Get(c.ShopView, "m_list"), "dataProvider");
        Assert.That(Rt.ToNumber(h.Get(dp, "length")), Is.EqualTo(1));
        Assert.That(Rt.ToNumber(h.Get(h.Get(dp, "0"), "serialID")), Is.EqualTo(91));
    }

    [Test]
    public void TheBuyBackTabNeedsNoProducts()
    {
        Packs();
        var (h, c) = Load(NewModel());
        Assert.That(c.OnCallback("OnChangeTab", new object[] { 1 }), Is.True);
        Assert.That(c.Group, Is.EqualTo(1));
        Assert.That(c.Tabs(1), Is.Empty);
    }

    [Test]
    public void TheDialogIsCentredByTheShopPanelSize()
    {
        // initShop runs before UIDialog's OnLoadComplete aligns the dialog, so ShopDialogProxy.alignment centres it by the
        // shop's panel on its first frame (getWindowWidth / getWindowHeight, as ints), not by the 1 x 1 fallback
        var (h, c) = Load(NewModel());
        for (int i = 0; i < 60; i++) h.Player.Tick();          // the open tween (startOpenAni) back to the aligned y
        var (x, y) = c.OpenPosition();
        var view = (AsMovieClip)c.ShopView;
        view.gotoAndStop(1);
        var panel = h.Get(view, "m_panel");
        int w = (int)Rt.ToNumber(h.Get(panel, "width")), ht = (int)Rt.ToNumber(h.Get(panel, "height"));
        Assert.That(ht, Is.GreaterThan(100));
        double dx = Rt.ToNumber(h.Get(h.Entity, "_alignX")), dy = Rt.ToNumber(h.Get(h.Entity, "_alignY"));
        Assert.That(x, Is.EqualTo((1280 - w) / 2.0 + dx).Within(0.5));
        Assert.That(y, Is.EqualTo((720 - ht) / 2.0 + dy).Within(0.5));
    }

    [Test]
    public void PriceTextUsesTheCurrencyIcon()
    {
        Assert.That(ShopController.CurrencyOf(0), Is.EqualTo(1));
        Assert.That(ShopController.CurrencyIcon(1, 0), Is.EqualTo(("export_common_icon_meso_h21", -6)));
        Assert.That(ShopController.CurrencyIcon(0, 0), Is.EqualTo(("export_common_icon_cash_r", -4)));
    }
}
