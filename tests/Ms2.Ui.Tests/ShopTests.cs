using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
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

    sealed class MemoryFolder : ICharacterFolder
    {
        public readonly Dictionary<string, string> Files = new();
        public string Read(string file) => Files.TryGetValue(file, out var t) ? t : null;
        public void Write(string file, string text) => Files[file] = text;
    }

    static (UiHost, ShopController) Load(UiModel model, ICharacterFolder folder = null)
    {
        var c = new ShopController { Categories = new ShopCategoryTable(Packs()), Npcs = new Ms2.Formats.World.NpcTable(Packs()) };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model) { Folder = folder };
        c.ReadShopInfo(Header());
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        c.ReadProducts(Products());
        for (int i = 0; i < 5; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    [Test]
    public void TheShopOpensWhereItsSettingsFilePlacedIt()
    {
        // UIShopDialog::vfunc_52 returns 1 (its drag bar is inside m_dialog), so vfunc_54 reads the saved transform and
        // vfunc_43 keeps the dialog there instead of re-running the proxy's alignment
        var (plain, _) = Load(NewModel());
        double alignedY = Rt.ToNumber(plain.Get(plain.Entity, "y"));
        var folder = new MemoryFolder();
        folder.Files["ShopDialog.xml"] = "<ShopDialog>\n\t<transform editAlign=\"0\" x=\"224\" y=\"145.45\" rateX=\"0.122093\" rateY=\"0.14094\" />\n</ShopDialog>";
        var (h, _) = Load(NewModel(), folder);
        for (int i = 0; i < 30; i++) h.Player.Tick();
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), y = Rt.ToNumber(h.Get(h.Entity, "y"));
        Assert.That(x, Is.EqualTo(0.122093 * 1280).Within(0.05));
        Assert.That(y, Is.EqualTo(0.14094 * 720).Within(0.05));
        Assert.That(y, Is.Not.EqualTo(alignedY).Within(1));
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

    [Test]
    public void StarShopItemsListBeforeTheShopsOwnAndAreFoundBySerial()
    {
        Packs();
        var model = NewModel();
        var (h, c) = Load(model);
        var bundle = new PremiumProduct
        {
            Id = 7001, Type = 1, ItemId = 11300001, Grade = 2, Currency = 1, Price = 1000, SalePrice = 800, SaleTag = 3, ItemCount = 5,
            BuyLimit = 1, BuyLimitMax = 2, BuyCount = 2,
        };
        var plain = new PremiumProduct { Id = 7002, Type = 2, ItemId = 11400003, Grade = 1, Currency = 0, Price = 500, SalePrice = 500, SaleTag = 0 };
        var items = new[] { new ShopMeratItem { Product = bundle, DisplayOrder = 4 }, new ShopMeratItem { Product = plain, DisplayOrder = 9 } };

        // the start clears the list (ReadMeratStart), the items append, RefreshPremium lists group 0 again
        c.ReadMeratStart(2);
        c.ReadMeratItems(items);
        Assert.That(c.FindMeratItem(7001)?.DisplayOrder, Is.EqualTo(4));
        Assert.That(c.FindMeratItem(7003), Is.Null);
        c.RefreshPremium();
        var dp = h.Get(h.Get(c.ShopView, "m_list"), "dataProvider");
        Assert.That(Rt.ToNumber(h.Get(dp, "length")), Is.EqualTo(5), "two star items, then the three products");
        var first = h.Get(dp, "0");
        Assert.That(Rt.ToNumber(h.Get(first, "serialID")), Is.EqualTo(7001));
        Assert.That(h.Get(first, "meratItem"), Is.EqualTo(true));
        Assert.That(Rt.ToNumber(h.Get(first, "productType")), Is.EqualTo(1));
        Assert.That(Rt.ToNumber(h.Get(first, "displayOrder")), Is.EqualTo(4));
        Assert.That(Rt.ToNumber(h.Get(first, "salePrice")), Is.EqualTo(800));
        Assert.That(Rt.ToNumber(h.Get(first, "price")), Is.EqualTo(1000));
        Assert.That(Rt.ToString(h.Get(first, "bgSaleText")), Does.Contain("20"));
        Assert.That(Rt.ToString(h.Get(first, "salePriceText")), Does.Contain("export_common_icon_merat"));
        Assert.That(h.Get(first, "soldOut"), Is.EqualTo(true), "the buy limit is reached");
        Assert.That(Rt.ToNumber(h.Get(first, "saleTag")), Is.EqualTo(3));
        var second = h.Get(dp, "1");
        Assert.That(Rt.ToNumber(h.Get(second, "serialID")), Is.EqualTo(7002));
        Assert.That(Rt.ToString(h.Get(second, "salePriceText")), Does.Contain("export_common_icon_meso_h21"));
        Assert.That(h.Get(second, "soldOut"), Is.EqualTo(false));
        Assert.That(Rt.ToNumber(h.Get(second, "price")), Is.NaN, "no price when the sale price is the price");
        Assert.That(Rt.ToNumber(h.Get(second, "starPoint")), Is.NaN, "no star point event, no member");

        // a new start clears them
        c.ReadMeratStart(0);
        Assert.That(c.FindMeratItem(7001), Is.Null);
    }

    [Test]
    public void BuyingAStarItemOpensTheBillAndQueuesOnePacketPerRow()
    {
        Packs();
        var model = NewModel();
        var (h, c) = Load(model);
        var ok = new PremiumProduct { Id = 7101, Type = 2, ItemId = 11400003, Grade = 1, Currency = 1, Price = 500, SalePrice = 400 };
        var full = new PremiumProduct { Id = 7102, Type = 1, ItemId = 11300001, Currency = 1, Price = 100, SalePrice = 100, BuyLimit = 1, BuyLimitMax = 1, BuyCount = 1 };
        c.ReadMeratStart(2);
        c.ReadMeratItems(new[] { new ShopMeratItem { Product = ok, DisplayOrder = 1 }, new ShopMeratItem { Product = full, DisplayOrder = 2 } });
        c.RefreshPremium();
        var notices = new List<string>();
        var sent = new List<MeretMarketBuy>();
        var boxes = new List<(string, string)>();
        c.SystemNotice = notices.Add;
        c.Purchases.Send = b => { sent.Add(b); return true; };
        c.Purchases.ShowResult = (cap, msg, _) => boxes.Add((cap, msg));
        c.Purchases.Text = id => "{0}|{1}|{2}";
        MarketBillParam bill = null;
        c.OpenMarketBill += b => bill = b;

        Assert.That(c.BuyMeratItem(9999), Is.False, "no such star item");
        Assert.That(c.BuyMeratItem(7102), Is.False, "the buy limit is reached: a notice only");
        Assert.That(notices.Count, Is.EqualTo(1));
        Assert.That(bill, Is.Null);

        model.Player.Merats = 0;
        notices.Clear();
        Assert.That(c.BuyMeratItem(7101), Is.False, "too few merat");
        Assert.That(notices.Count, Is.EqualTo(1));

        model.Player.Merats = 1000;
        Assert.That(c.BuyMeratItem(7101), Is.True);
        Assert.That(bill, Is.Not.Null);
        Assert.That((bill.Type, bill.DialogId, bill.SourceKind, bill.ShopId, bill.Rows.Count), Is.EqualTo((3, 0x4e3b, 4, c.Info.ShopId, 1)));
        Assert.That((bill.Rows[0].SerialId, bill.Rows[0].Price, bill.Rows[0].PayType), Is.EqualTo((7101, 400L, 3)));

        bill.Buy(bill, new Dictionary<int, long>());
        Assert.That(sent.Count, Is.EqualTo(1));
        Assert.That((sent[0].Kind, sent[0].PremiumId, sent[0].Quantity, sent[0].Price, sent[0].PayType, sent[0].Index, sent[0].Total, sent[0].Gift),
            Is.EqualTo((4, 7101, 1, 400L, 3, 0, 1, 0)));
        Assert.That(c.Purchases.Running, Is.True);

        // the answer echoes the packet: the buy count follows it and the box lists the bought product
        c.OnMeretMarket(MeretMarketPacket.Read(new Maple2.PacketLib.Tools.ByteReader(MeretMarketPacket.WriteSucceeded(sent[0], 1, 0, 0, 0))));
        Assert.That(c.FindMeratItem(7101).Product.BuyCount, Is.EqualTo(1));
        Assert.That(c.Purchases.Running, Is.False);
        Assert.That(boxes.Count, Is.EqualTo(1));
    }

    /// <summary>Shop with one star product (Id 7201, price 500, sale 400) and the bill opened for it; the queue's sends are collected.</summary>
    (ShopController c, UiHost h, UiModel model, MarketBillParam bill, List<MeretMarketBuy> sent, List<string> notices, List<(string, string)> boxes)
        MeratBill(PremiumProduct prem = null, Action<ShopController> setup = null)
    {
        Packs();
        var model = NewModel();
        var (h, c) = Load(model);
        prem ??= new PremiumProduct { Id = 7201, Type = 2, ItemId = 11400003, Grade = 1, Currency = 1, Price = 500, SalePrice = 400 };
        c.ReadMeratStart(1);
        c.ReadMeratItems(new[] { new ShopMeratItem { Product = prem, DisplayOrder = 1 } });
        c.RefreshPremium();
        var notices = new List<string>();
        var sent = new List<MeretMarketBuy>();
        var boxes = new List<(string, string)>();
        c.SystemNotice = notices.Add;
        c.Purchases.Send = b => { sent.Add(b); return true; };
        c.Purchases.ShowResult = (cap, msg, _) => boxes.Add((cap, msg));
        c.Purchases.Text = id => "{0}|{1}|{2}";
        setup?.Invoke(c);
        model.Player.Merats = 100000;
        MarketBillParam bill = null;
        c.OpenMarketBill += b => bill = b;
        Assert.That(c.BuyMeratItem(prem.Id), Is.True);
        return (c, h, model, bill, sent, notices, boxes);
    }

    [Test]
    public void MeratBillPriceFollowsTheSaleEvent()
    {
        // GetSalePrice (:685): a type 2 merat product takes the best running event covering id 0: 400 less 10% = ceil(400 * 9000 / 10000)
        var events = new ShopSaleEvents();
        events.Add(new PremiumMarketSaleEvent(1, 1000, new[] { 0 }, 100000));
        var (c, _, _, bill, sent, _, _) = MeratBill(setup: s => s.SaleEvents = events);
        Assert.That(bill.Rows[0].Price, Is.EqualTo(360L));
        bill.Buy(bill, new Dictionary<int, long>());
        Assert.That(sent[0].Price, Is.EqualTo(360L));
    }

    [Test]
    public void MeratType8ProductIsCutByItsPercent()
    {
        var prem = new PremiumProduct { Id = 7202, Type = 8, ItemId = 11400003, Grade = 1, Currency = 1, Price = 500, SalePrice = 400, RandomSaleRate = 25 };
        Assert.That(new ShopSaleEvents().SalePrice(prem), Is.EqualTo(300L));
        Assert.That(new ShopSaleEvents().SalePrice(new PremiumProduct { Type = 8, SalePrice = 400 }), Is.EqualTo(0L), "a rate of 0 gives 0");
        Assert.That(new ShopSaleEvents().SalePrice(new PremiumProduct { Type = 3, SalePrice = 400, RandomSaleRate = 50 }), Is.EqualTo(400L));
    }

    [Test]
    public void MeratConfirmRefusesWhatTheClientRefuses()
    {
        // payable (FUN_140ebc590): the bill is opened with enough merat, then the purse is emptied before the confirm
        var (c, _, model, bill, sent, notices, boxes) = MeratBill();
        model.Player.Merats = 0;
        bill.Buy(bill, new Dictionary<int, long>());
        Assert.That(sent, Is.Empty);
        Assert.That(notices.Count, Is.EqualTo(1));
        Assert.That(c.Purchases.Running, Is.False);

        // bag room (FUN_140ebcc20): a tab short of slots shows 0xa01 and sends nothing
        (c, _, model, bill, sent, notices, boxes) = MeratBill(setup: s => { s.ItemTab = _ => 2; s.EmptySlotCount = _ => 0; });
        var shown = new List<object>();
        c.ShowBox = shown.Add;
        bill.Buy(bill, new Dictionary<int, long>());
        Assert.That(sent, Is.Empty);
        Assert.That(shown.Count, Is.EqualTo(1));

        // CanBuyPremiumProduct: a level limit, an achievement not reached, a product at its buy limit
        foreach (var limited in new[]
        {
            new PremiumProduct { Id = 7301, Type = 2, ItemId = 11400003, Currency = 1, Price = 500, SalePrice = 400, MinLevel = 90 },
            new PremiumProduct { Id = 7302, Type = 2, ItemId = 11400003, Currency = 1, Price = 500, SalePrice = 400, AchieveId = 12345, AchieveGrade = 1 },
            new PremiumProduct { Id = 7303, Type = 2, ItemId = 11400003, Currency = 1, Price = 500, SalePrice = 400, JobMask = 3 },
        })
        {
            (c, _, model, bill, sent, notices, boxes) = MeratBill(limited, s => s.HasAchieve = (_, _) => false);
            model.Player.Level = 10;
            bill.Buy(bill, new Dictionary<int, long>());
            Assert.That(sent, Is.Empty, $"product {limited.Id}");
            Assert.That(c.Purchases.Running, Is.False);
        }
    }

    [Test]
    public void MeratQueueEndsWhenTheSendFailsTheLinkDropsOrTheDialogCloses()
    {
        // a failed send (no game link) finishes the run
        var (c, _, _, bill, sent, notices, boxes) = MeratBill();
        c.Purchases.Send = _ => false;
        c.Purchases.Notice = notices.Add;
        bill.Buy(bill, new Dictionary<int, long>());
        Assert.That(c.Purchases.Running, Is.False);
        Assert.That(notices.Count + boxes.Count, Is.GreaterThan(0));

        // the link drops mid run
        (c, _, _, bill, sent, notices, boxes) = MeratBill();
        bill.Buy(bill, new Dictionary<int, long>());
        Assert.That(c.Purchases.Running, Is.True);
        c.Purchases.Cancel();
        Assert.That(c.Purchases.Running, Is.False);

        // the shop closes mid run (OnDialogClosed: Finish(1))
        (c, _, _, bill, sent, notices, boxes) = MeratBill();
        bill.Buy(bill, new Dictionary<int, long>());
        Assert.That(c.Purchases.Running, Is.True);
        c.Close();
        Assert.That(c.Purchases.Running, Is.False);
    }

    [Test]
    public void MeratAnswerOfAnotherRowIsIgnoredAndAnOutOfRangeIndexFinishes()
    {
        var (c, _, _, bill, sent, _, boxes) = MeratBill();
        bill.Buy(bill, new Dictionary<int, long>());
        var good = MeretMarketPacket.Read(new Maple2.PacketLib.Tools.ByteReader(MeretMarketPacket.WriteSucceeded(sent[0], 1, 0, 0, 0)));
        var other = new MeretMarketBuy { Kind = 4, PremiumId = 9999, Quantity = 1, Index = 0, Total = 1 };
        c.OnMeretMarket(MeretMarketPacket.Read(new Maple2.PacketLib.Tools.ByteReader(MeretMarketPacket.WriteSucceeded(other, 1, 0, 0, 0))));
        Assert.That(c.Purchases.Running, Is.True, "an answer for another product is ignored");
        var far = new MeretMarketBuy { Kind = 4, PremiumId = sent[0].PremiumId, Quantity = 1, Index = 5, Total = 6 };
        c.OnMeretMarket(MeretMarketPacket.Read(new Maple2.PacketLib.Tools.ByteReader(MeretMarketPacket.WriteSucceeded(far, 1, 0, 0, 0))));
        Assert.That(c.Purchases.Running, Is.False, "an index past the queue ends the run");
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

    static ShopProduct StackableProduct() => new()
    {
        Serial = 200, ItemId = 20000028, Rarity = 1, Price = 100, OriginalPrice = 100, Count = 1, Bundle = 1, Categories = "CP",
    };

    [Test]
    public void BuySingleItemBelowConfirmPriceSendsImmediatelyWithoutLocalMutation()
    {
        Packs();
        var model = NewModel();
        model.Player.Mesos = 10_000;
        model.Tables.ShopBuyConfirmPrice = 5_000;
        var (h, c) = Load(model);
        var sent = new List<IOutPacket>();
        var boxes = new List<MessageBoxData>();
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;

        var row = h.Obj(("serialID", 1), ("productType", 0));
        Assert.That(c.OnCallback("OnBuyItem", new object[] { row, 0 }), Is.True);
        Assert.That(boxes, Is.Empty, "below ShopBuyConfirmPrice sends directly");
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(new ShopBuyRequest(1, 1, 106)));
        Assert.That(c.ShopEnabled, Is.False, "shop disabled until server answers");
        Assert.That(model.Player.Mesos, Is.EqualTo(10_000), "mesos never mutated before server answer");
        Assert.That(c.Products(0)[1].Record.Bought, Is.EqualTo(0), "bought count never mutated before server answer");

        // Server answers with cmd 2 (product update) and cmd 4 (purchase result)
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(ShopPacket.WriteProductUpdate(1, 1))));
        Assert.That(c.ShopEnabled, Is.True);
        Assert.That(c.Products(0)[1].Record.Bought, Is.EqualTo(1));

        var notices = new List<(string Text, int Type)>();
        c.NoticeWithType = (t, ty) => notices.Add((t, ty));
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(
            ShopPacket.WritePurchaseResult(new ShopPurchaseResult(11300001, 1, 1830, 1, true)))));
        Assert.That(notices, Has.Count.EqualTo(1));
        Assert.That(notices[0].Type, Is.EqualTo(0x14));
    }

    [Test]
    public void BuyAtOrAboveConfirmPriceAndNonMesoRequireConfirmation()
    {
        Packs();
        var model = NewModel();
        model.Player.Mesos = 10_000;
        model.Tables.ShopBuyConfirmPrice = 1_500;
        var (h, c) = Load(model);
        var sent = new List<IOutPacket>();
        var boxes = new List<MessageBoxData>();
        bool closed = false;
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.CloseBox = () => closed = true;

        var row = h.Obj(("serialID", 1), ("productType", 0));
        c.OnCallback("OnBuyItem", new object[] { row, 0 });
        Assert.That(sent, Is.Empty, "confirmation required before sending");
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Type, Is.EqualTo(0x11));
        Assert.That(boxes[0].Extra, Is.EqualTo("buyItem,1,1"));
        Assert.That(boxes[0].Listener, Is.SameAs(c));

        // Cancel sends nothing and mutates nothing
        c.OnMessageBox(null, 1, "buyItem,1,1");
        Assert.That(sent, Is.Empty);
        Assert.That(closed, Is.True);

        // Confirm sends ShopBuyRequest(1, 1, 106)
        closed = false;
        Assert.That(c.OnMessageBox(null, 0, "buyItem,1,1"), Is.True);
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(new ShopBuyRequest(1, 1, 106)));
        Assert.That(c.ShopEnabled, Is.False);
        Assert.That(closed, Is.True);
    }

    [Test]
    public void BuyStackableItemOpensQuantityBoxAndUpdatesPrice()
    {
        Packs();
        var model = NewModel();
        model.Player.Mesos = 550; // affords 5 at 100 each
        var c = new ShopController { Categories = new ShopCategoryTable(Packs()), Npcs = new Ms2.Formats.World.NpcTable(Packs()) };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        var header = Header() with { ProductCount = 1 };
        c.ReadShopInfo(header);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        c.ReadProducts(new[] { StackableProduct() });
        for (int i = 0; i < 5; i++) p.Tick();

        var sent = new List<IOutPacket>();
        var boxes = new List<MessageBoxData>();
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.CloseBox = () => { };

        var row = h.Obj(("serialID", 200), ("productType", 0));
        c.OnCallback("OnBuyItem", new object[] { row, 0 });
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Type, Is.EqualTo(0x12));
        Assert.That(boxes[0].Extra, Is.EqualTo("buyItem,200"));
        Assert.That(boxes[0].ItemEntry.Max, Is.EqualTo(5));
        Assert.That(boxes[0].Values["shopSerialID"], Is.EqualTo(200));
        Assert.That(boxes[0].Values["shopTradeChangePriceFuntion"], Is.InstanceOf<Func<object[], object>>());

        // OnPriceChanged updates htmlText on the supplied text field
        var field = h.Obj(("htmlText", ""));
        ((Func<object[], object>)boxes[0].Values["shopTradeChangePriceFuntion"])(new object[] { 200, field, 300 });
        Assert.That(Rt.ToString(h.Get(field, "htmlText")), Does.Contain("300"));

        // Confirming quantity 3 sends mode 4 (200, 3, 106)
        Assert.That(c.OnMessageBox(null, 0, "buyItem,200,3"), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { new ShopBuyRequest(200, 3, 106) }));
    }

    [Test]
    public void BuyRejectedCasesSendNothingAndMutateNothing()
    {
        Packs();
        var model = NewModel();
        model.Player.Mesos = 100; // too poor for 1830
        model.Tables.ShopBuyConfirmPrice = 10_000;
        var (h, c) = Load(model);
        var sent = new List<IOutPacket>();
        var notices = new List<string>();
        var boxes = new List<MessageBoxData>();
        c.Send = sent.Add;
        c.SystemNotice = notices.Add;
        c.ShowBox = boxes.Add;
        c.ServerTime = () => 1_000;

        // 1. Not enough mesos
        c.BuyProduct(1, 0);
        Assert.That(sent, Is.Empty);
        Assert.That(notices, Has.Count.EqualTo(1));
        Assert.That(model.Player.Mesos, Is.EqualTo(100));
        Assert.That(c.ShopEnabled, Is.True);

        // 2. Sold out
        model.Player.Mesos = 100_000;
        c.Products(0)[1].Record.Limit = 1;
        c.Products(0)[1].Record.Bought = 1;
        c.BuyProduct(1, 0);
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Has.Count.EqualTo(1));
        c.Products(0)[1].Record.Limit = 0;
        c.Products(0)[1].Record.Bought = 0;

        // 3. Outside buy period
        c.Products(0)[1].Record.Period = new ShopBuyPeriod { HasTime = true, Start = 2_000, End = 3_000 };
        c.BuyProduct(1, 0);
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Has.Count.EqualTo(2));
        c.Products(0)[1].Record.Period = null;

        // 4. Expired item life (type 2)
        c.Products(0)[1].Record.Life = new ShopItemLife(2, 0, 500);
        c.BuyProduct(1, 0);
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Has.Count.EqualTo(3));
        c.Products(0)[1].Record.Life = null;

        // 5. Star point currency without event
        c.Products(0)[1].Record.Currency = 0xd;
        model.Player.StarPointEvent = false;
        c.BuyProduct(1, 0);
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Has.Count.EqualTo(4));
        c.Products(0)[1].Record.Currency = 0;

        // 6. Zero / negative quantity
        Assert.That(c.SendPurchase(1, 0), Is.False);
        Assert.That(c.SendPurchase(1, -2), Is.False);
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void SellRejectedAndAcceptedFlowsMatchClientRules()
    {
        Packs();
        var model = NewModel();
        model.Tables.ShopSellConfirmPrice = 10_000;
        var (_, c) = Load(model);
        var sent = new List<IOutPacket>();
        var notices = new List<string>();
        var boxes = new List<MessageBoxData>();
        c.Send = sent.Add;
        c.SystemNotice = notices.Add;
        c.ShowBox = boxes.Add;
        c.ServerTime = () => 1_000;

        var sellable = Item.Create(model.Tables.Items, 11300001, 1);
        sellable.Uid = 5001; sellable.Count = 1; sellable.Data.ShopSell = true; sellable.Data.SellPrices = new List<long> { 500 };

        // Rejected: shop shutdown
        c.ShopShutdown = true;
        Assert.That(c.CanSellItem(sellable), Is.False);
        Assert.That(sent, Is.Empty);
        c.ShopShutdown = false;

        // Rejected: sell-only shop
        c.Info.SellOnly = true;
        Assert.That(c.CanSellItem(sellable), Is.False);
        Assert.That(sent, Is.Empty);
        c.Info.SellOnly = false;

        // Rejected: locked item (shows box 0x22)
        sellable.LockState = 1;
        Assert.That(c.CanSellItem(sellable), Is.False);
        Assert.That(boxes.Last().Type, Is.EqualTo(0x22));
        Assert.That(sent, Is.Empty);
        sellable.LockState = 0;

        // Rejected: unsellable item (ShopSell == false)
        sellable.Data.ShopSell = false;
        Assert.That(c.CanSellItem(sellable), Is.False);
        Assert.That(sent, Is.Empty);
        sellable.Data.ShopSell = true;

        // Rejected: socketed gemstone
        sellable.Record = new EquipItem { Id = sellable.ItemId, Rarity = 1, Amount = 1, Socket = new ItemSocket { MaxSlot = 1 } };
        sellable.Record.Socket.Gems.Add(new SocketGem { GemId = 11300001 });
        Assert.That(c.CanSellItem(sellable), Is.False);
        Assert.That(sent, Is.Empty);
        sellable.Record.Socket.Gems.Clear();

        // Rejected: restricted sale item
        c.IsSaleRestricted = id => id == sellable.ItemId;
        Assert.That(c.CanSellItem(sellable), Is.False);
        Assert.That(sent, Is.Empty);
        c.IsSaleRestricted = _ => false;

        // Rejected: summoned pet
        c.IsSummonedPet = uid => uid == 5001;
        Assert.That(c.RequestSellItem(sellable, 1), Is.False);
        Assert.That(sent, Is.Empty);
        c.IsSummonedPet = _ => false;

        // InventoryController sell flow: single cheap item sends immediately; rare item asks sellconfirm; stack asks sell qty
        var book = new ItemBook { TabOf = _ => 0, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(0, new List<EquipItem> {
            new() { Uid = 5001, Id = 11300001, Rarity = 1, Amount = 1, InventorySlot = 0 },
            new() { Uid = 5002, Id = 11300001, Rarity = 4, Amount = 1, InventorySlot = 1 },
            new() { Uid = 5003, Id = 20000028, Rarity = 1, Amount = 5, InventorySlot = 2 },
        }));
        var inv = new InventoryController
        {
            View = new InventoryView(book) { ServerTime = () => 1_000 },
            Shop = () => c,
            DialogHead = () => ShopController.UiId,
            ShowBox = boxes.Add,
        };
        var (_, invPlayer) = src.Open(inv.Movie, r => r.Budget = 20_000_000);
        invPlayer.StageW = 1280; invPlayer.StageH = 720;
        var invHost = new UiHost(invPlayer, inv, model);
        Assert.That(invHost.Load(), Is.True);

        // 1. Cheap rarity-1 item -> sends mode 5 immediately
        var cheap = inv.FindItem(5001);
        cheap.Data.ShopSell = true; cheap.Data.SellPrices = new List<long> { 500 };
        Assert.That(inv.TrySellItem(cheap), Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new[] { new ShopSellRequest(5001, 1) }));
        sent.Clear();

        // 2. Rarity-4 item -> opens sellconfirm box, sends nothing until SellConfirmStep
        boxes.Clear();
        var rare = inv.FindItem(5002);
        rare.Data.ShopSell = true; rare.Data.SellPrices = new List<long> { 500 };
        Assert.That(inv.TrySellItem(rare), Is.EqualTo(1));
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That((boxes[0].Type, boxes[0].Extra, boxes[0].ItemUid), Is.EqualTo((0x11, "sellconfirm,1", 5002L)));
        inv.SellConfirmStep(5002, 1);
        Assert.That(sent, Is.EqualTo(new[] { new ShopSellRequest(5002, 1) }));
        sent.Clear();

        // 3. Stack of 5 -> opens box 0x12 "sell", sends nothing until confirmed
        boxes.Clear();
        var stack = inv.FindItem(5003);
        stack.Data.ShopSell = true; stack.Data.SellPrices = new List<long> { 10 };
        Assert.That(inv.TrySellItem(stack), Is.EqualTo(1));
        Assert.That(sent, Is.Empty);
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That((boxes[0].Type, boxes[0].Extra, boxes[0].ItemUid), Is.EqualTo((0x12, "sell", 5003L)));
        Assert.That(inv.IsSellCountValid(5003, 0), Is.False);
        Assert.That(inv.IsSellCountValid(5003, 6), Is.False);
        Assert.That(inv.IsSellCountValid(5003, 3), Is.True);
        inv.SellCountStep(5003, 3);
        Assert.That(sent, Is.EqualTo(new[] { new ShopSellRequest(5003, 3) }));
    }

    [Test]
    public void ResetErrorTimerRolloverAndCloseSendExpectedPackets()
    {
        Packs();
        var model = NewModel();
        var (_, c) = Load(model);
        var sent = new List<IOutPacket>();
        var sounds = new List<string>();
        var notices = new List<string>();
        var boxes = new List<MessageBoxData>();
        bool talkEnded = false;
        (int Kind, int State)? sysClose = null;
        c.Send = sent.Add;
        c.Sound = sounds.Add;
        c.SystemNotice = notices.Add;
        c.ShowBox = boxes.Add;
        c.SendNpcTalkEnd = () => talkEnded = true;
        c.SendSystemShop = (k, s) => sysClose = (k, s);

        // OnResetList sends mode 9 with resetPrice and disables shop
        c.OnCallback("OnResetList", Array.Empty<object>());
        Assert.That(sent, Is.EqualTo(new[] { new ShopResetListRequest(1000) }));
        Assert.That(c.ShopEnabled, Is.False);

        // Cmd 15 (ShopError 15) re-enables shop, posts notice and plays warning sound
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(ShopPacket.WriteError(new ShopError(15, 9, 0)))));
        Assert.That(c.ShopEnabled, Is.True);
        Assert.That(notices, Has.Count.EqualTo(1));
        Assert.That(sounds, Does.Contain("system_notice_error_warningBag"));

        // Cmd 9 (ShopResetResult) clears group 0 products and plays shop_reset
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(
            ShopPacket.WriteResetResult(new ShopResetResult(true, 30000001, 2)))));
        Assert.That(c.Products(0), Is.Empty);
        Assert.That(sounds, Does.Contain("shop_reset"));

        // Process() past ResetTime sends mode 10; steady-state Process() allocates 0 bytes
        long now = 500;
        c.ServerTime = () => now;
        c.ReadShopInfo(Header() with { ResetTime = 1_000, ResetEnable = true, ResetLimited = true });
        for (int i = 0; i < 50; i++) c.Process(); // warm up past tiered JIT & timezone cache
        var noBytes = Is.Zero; // made before the window (see TextureTransformTests)
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) c.Process();
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - allocBefore, noBytes, "Process() must allocate 0 bytes per frame");

        sent.Clear();
        now = 1_500;
        c.Process();
        Assert.That(sent, Is.EqualTo(new[] { new ShopRequestListRequest() }));

        // Close() sends NpcTalk mode 0 and SystemShop close when SortKind > 0
        c.SortKind = 8;
        c.Close();
        Assert.That(talkEnded, Is.True);
        Assert.That(sysClose, Is.EqualTo((8, 0)));
    }

    [Test]
    public void BuybackTabReceivesAddsRepurchasesAndRemovesProducts()
    {
        Packs();
        var model = NewModel();
        model.Player.Mesos = 500;
        var (h, c) = Load(model);
        var sent = new List<IOutPacket>();
        var notices = new List<string>();
        c.Send = sent.Add;
        c.SystemNotice = notices.Add;

        // Cmd 6 (RepurchaseStart, 1 item) + Cmd 7 (RepurchaseAdd, price 1200)
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(ShopPacket.WriteRepurchaseStart(1))));
        var sold = new ShopProduct { Serial = 77, ItemId = 11300001, Rarity = 2, Price = 1200, Count = 1 };
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(ShopPacket.WriteRepurchaseAdd(new[] { sold }))));
        Assert.That(c.Products(1).ContainsKey(77), Is.True);
        Assert.That(c.Products(1)[77].Record.Price, Is.EqualTo(1200));

        // Switch to buyback tab (group 1)
        Assert.That(c.OnCallback("OnChangeTab", new object[] { 1 }), Is.True);
        var dp = h.Get(h.Get(c.ShopView, "m_list"), "dataProvider");
        Assert.That(Rt.ToNumber(h.Get(dp, "length")), Is.EqualTo(1));

        // Repurchase rejected when player lacks mesos -> notice 0x34, sends NOTHING, mutates NOTHING
        var row = h.Obj(("serialID", 77), ("productType", 0));
        Assert.That(c.OnCallback("OnBuyItem", new object[] { row, 0 }), Is.True);
        Assert.That(sent, Is.Empty);
        Assert.That(notices, Has.Count.EqualTo(1));
        Assert.That(c.ShopEnabled, Is.True);
        Assert.That(model.Player.Mesos, Is.EqualTo(500));

        // With enough mesos -> sends ShopRepurchaseRequest(77) (mode 3) and disables shop
        model.Player.Mesos = 2_000;
        Assert.That(c.OnCallback("OnBuyItem", new object[] { row, 0 }), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { new ShopRepurchaseRequest(77) }));
        Assert.That(c.ShopEnabled, Is.False);
        Assert.That(model.Player.Mesos, Is.EqualTo(2_000), "mesos unchanged until server answers");

        // Cmd 8 (RepurchaseRemoval) removes serial 77 and re-enables shop
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(ShopPacket.WriteRepurchaseRemoval(77))));
        Assert.That(c.ShopEnabled, Is.True);
        Assert.That(c.Products(1), Is.Empty);
    }

    [Test]
    public void NewItemsTabRequestsListReceivesNewProductsAndPopulatesRows()
    {
        Packs();
        var model = NewModel();
        var c = new ShopController { Categories = new ShopCategoryTable(Packs()), Npcs = new Ms2.Formats.World.NpcTable(Packs()) };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        c.ReadShopInfo(Header() with { HasNewTab = true });
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        c.ReadProducts(Products());
        for (int i = 0; i < 5; i++) p.Tick();

        var sent = new List<IOutPacket>();
        c.Send = sent.Add;

        // Switching to group 2 before new products arrive sends mode 13 and disables shop
        Assert.That(c.OnCallback("OnChangeTab", new object[] { 2 }), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { new ShopRequestNewItemsRequest() }));
        Assert.That(c.ShopEnabled, Is.False);

        // Cmd 13 with mismatched shopId is ignored
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(
            ShopPacket.WriteNewStart(new ShopNewStart(11000004, 999, 0, 1)))));
        Assert.That(c.ShopEnabled, Is.False);

        // Matching Cmd 13 + Cmd 14 populates group 2 with productType == 2
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(
            ShopPacket.WriteNewStart(new ShopNewStart(11000004, 106, 0, 1)))));
        c.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(
            ShopPacket.WriteNewProducts(new[] { new ShopNewProduct { ItemId = 11300001, Rarity = 2, Categories = "CP", ProductType = 4, ProductKind = 1 } }))));
        Assert.That(c.ShopEnabled, Is.False, "ReadNewProducts does not call EnableShop");
        Assert.That(c.Products(2), Has.Count.EqualTo(1));
        Assert.That(c.Products(2)[0].ProductKind, Is.EqualTo(2));
        var dp = h.Get(h.Get(c.ShopView, "m_list"), "dataProvider");
        Assert.That(Rt.ToNumber(h.Get(dp, "length")), Is.EqualTo(1));
        Assert.That(Rt.ToNumber(h.Get(h.Get(dp, "0"), "productType")), Is.EqualTo(2));
    }

    [Test]
    public void RemainingCallbacksAnswerLinkPreviewRecipeEquipUgcConfirmAndCloseShop()
    {
        Packs();
        var model = NewModel();
        model.Player.Mesos = 100_000;
        var (h, c) = Load(model);
        var sent = new List<IOutPacket>();
        var boxes = new List<MessageBoxData>();
        var openedDialogs = new List<int>();
        var linkedShop = new List<(int ItemId, int Rarity, int ShopId, int Serial)>();
        var previewed = new List<Item>();
        bool unequipAll = false, showMyEquip = false, boxClosed = false;
        (int Slot, ulong Uid)? unequipped = null;
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.CloseBox = () => boxClosed = true;
        c.OpenDialog += openedDialogs.Add;
        c.LinkShopItem = (id, r, sid, ser) => linkedShop.Add((id, r, sid, ser));
        c.PreviewItem = previewed.Add;
        c.PreviewUnequipAll = () => unequipAll = true;
        c.PreviewShowMyEquip = () => showMyEquip = true;
        c.PreviewUnequipSlot = (slot, uid) => unequipped = (slot, uid);

        var row = h.Obj(("serialID", 1), ("productType", 0), ("itemID", 11300001));

        // OnUseLink links shop item (kind 0)
        Assert.That(c.OnCallback("OnUseLink", new object[] { row }), Is.True);
        Assert.That(linkedShop, Is.EqualTo(new[] { (11300001, 2, 106, 1) }));

        // OnRightClick does nothing when preview HUD is off, previews when on
        Assert.That(c.OnCallback("OnRightClick", new object[] { row }), Is.True);
        Assert.That(previewed, Is.Empty);
        c.IsPreviewHudOn = () => true;
        Assert.That(c.OnCallback("OnRightClick", new object[] { row }), Is.True);
        Assert.That(previewed, Has.Count.EqualTo(1));
        c.IsPreviewHudOn = () => false;

        // OnUsePreview forces preview even when preview HUD is off
        Assert.That(c.OnCallback("OnUsePreview", new object[] { row }), Is.True);
        Assert.That(previewed, Has.Count.EqualTo(2));

        // onClickUnequipAll / onClickShowMyEquip / onClickShowMainItems / OnUnEquip
        Assert.That(c.OnCallback("onClickUnequipAll", Array.Empty<object>()), Is.True);
        Assert.That(c.OnCallback("onClickShowMyEquip", Array.Empty<object>()), Is.True);
        Assert.That(c.OnCallback("onClickShowMainItems", Array.Empty<object>()), Is.True);
        Assert.That(c.OnCallback("OnUnEquip", new object[] { "5", h.Obj(("itemGUID", "987654321")) }), Is.True);
        Assert.That((unequipAll, showMyEquip, unequipped), Is.EqualTo((true, true, (5, 987654321UL))));

        // onClickRecipe & onClickPreview
        int maidArg = 0;
        int interiorArg = 0;
        c.OpenMaidRecipe = (m, _, _) => maidArg = m;
        c.OpenInteriorPreview = (id, _, _) => interiorArg = id;
        var itemData = model.Tables.Items.Get(11300001);
        itemData.Type = 6; itemData.Subtype = 7; itemData.Param2 = 42;
        Assert.That(c.OnCallback("onClickRecipe", new object[] { row, 10.0, 20.0 }), Is.True);
        Assert.That(maidArg, Is.EqualTo(42));
        Assert.That(openedDialogs, Does.Contain(0x4ecb));
        itemData.FunctionName = "UGCMapPackage";
        Assert.That(c.OnCallback("onClickPreview", new object[] { row }), Is.True);
        Assert.That(interiorArg, Is.EqualTo(11300001));
        Assert.That(openedDialogs, Does.Contain(0x4ee3));

        // UGCMapPackage double confirm (0x1f box) in OnMessageBox -> ConfirmUgcPurchase sends mode 4
        c.CanBuyUgcPackage = _ => true;
        Assert.That(c.OnMessageBox(null, 0, "buyItem,1,1"), Is.True);
        Assert.That(sent, Is.Empty, "UGCMapPackage asks 0x1f confirmation before sending");
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Type, Is.EqualTo(0x1f));
        boxes[0].InvokeCallback(0, null);
        Assert.That(sent, Is.EqualTo(new[] { new ShopBuyRequest(1, 1, 106) }));

        // FUN_1418ffba0 rule (uicommon.cpp:1183-1209), with the live default (state unwired) asking for the agreement
        c.CanBuyUgcPackage = null;
        Assert.That(c.NeedsUgcAgreement(itemData), Is.True, "unwired home state never buys silently");
        c.PackageIndoorSize = _ => "large";
        c.UgcHome = () => new ShopController.UgcHomeState(true, 5, "large", false);
        Assert.That(c.NeedsUgcAgreement(itemData), Is.False, "same size, has plot");
        c.UgcHome = () => new ShopController.UgcHomeState(true, 5, "small", false);
        Assert.That(c.NeedsUgcAgreement(itemData), Is.True, "package size differs from the plot");
        c.UgcHome = () => new ShopController.UgcHomeState(true, 0, null, false);
        Assert.That(c.NeedsUgcAgreement(itemData), Is.True, "has a home but no plot");
        c.UgcHome = () => new ShopController.UgcHomeState(true, 0, null, true);
        Assert.That(c.NeedsUgcAgreement(itemData), Is.False, "no home at all");
        c.UgcHome = () => new ShopController.UgcHomeState(false, 0, null, false);
        Assert.That(c.NeedsUgcAgreement(itemData), Is.False, "GetUGCMapInfo null");
        c.CanBuyUgcPackage = _ => true;

        // CanPreviewItem (uishopdialog.cpp:3434 -> FUN_141b48950): not expired, doNotPreview == 0, no item type test
        var pv = Item.Create(model.Tables.Items, 11300001, 1);
        itemData.Type = 2; itemData.DoNotPreview = 0; pv.ExpiryTime = 0;
        Assert.That(c.CanPreviewItem(pv), Is.True, "a non-equip item is previewable");
        pv.ExpiryTime = 5; c.ServerTime = () => 10;
        Assert.That(c.CanPreviewItem(pv), Is.False, "expired");
        pv.ExpiryTime = 0; itemData.DoNotPreview = 1;
        Assert.That(c.CanPreviewItem(pv), Is.False, "doNotPreview");
        itemData.DoNotPreview = 0;

        // CloseShop closes active buyItem message box
        boxClosed = false;
        c.CurrentBox = () => MessageBoxQueue.Create(0x11, "", c, "buyItem,1,1");
        c.CloseShop();
        Assert.That(boxClosed, Is.True);

        // OnEnterKey (vfunc_13) returns false when closed or unfocused, runs search when m_inputSearch is focused
        var (h2, c2) = Load(NewModel());
        Assert.That(c2.OnEnterKey(), Is.False);
        var input = h2.Child("m_inputSearch", c2.ShopView);
        Assert.That(input, Is.Not.Null);
        h2.Set(input, "focused", true);
        h2.Set(input, "text", "___no_match___");
        Assert.That(c2.OnEnterKey(), Is.True);
        var listDp = h2.Get(h2.Get(c2.ShopView, "m_list"), "dataProvider");
        Assert.That(Rt.ToNumber(h2.Get(listDp, "length")), Is.EqualTo(0));
    }

    [Test]
    public void ShopTooltipsIncludeCompareRowsAchieveRequirementAndGachaCoin()
    {
        Packs();
        var model = NewModel();
        var (h, c) = Load(model);
        c.Info.ComparesItems = true;
        c.Products(0)[1].Record.Achieve = 1001;
        c.Products(0)[1].Record.AchieveGrade = 2;
        c.HasAchieve = (_, _) => false;
        c.RequireAchieveTooltipText = (_, _) => "Test Trophy";
        c.Wearer = () => new PcInfo { Equips = new List<EquipItem> { new() { Id = 11300001, Uid = 999, Rarity = 1 } } };

        var row = h.Obj(("serialID", 1), ("productType", 0));
        var tip = c.GetTooltip(new object[] { "", row });
        Assert.That(tip, Is.Not.Null);
        Assert.That(tip.ContainsKey("itemGuideDescription"), Is.True);
        Assert.That(tip["itemGuideDescription"]?.ToString(), Does.Contain("Test Trophy"));
        Assert.That(c.GetTooltip(new object[] { "m_coinInfo" }), Is.Null, "non-gacha shop has no coin tooltip");
    }

    [Test]
    public void CompareTooltipCarriesShopGuideTextOnItemSide()
    {
        Packs();
        var model = NewModel();
        var worn = Item.Create(model.Tables.Items, 20000028, 1);
        var item = Item.Create(model.Tables.Items, 20000028, 1);
        var tip = new ItemTooltip(model, 4) { ShopGuideText = "Need Trophy" };
        Assert.That(tip.FillCompare(worn, item), Is.True);
        var rhs = (IDictionary<string, object>)tip.Data["rhsTooltip"];
        var lhs = (IDictionary<string, object>)tip.Data["lhsTooltip"];
        Assert.That(rhs["itemGuideDescription"]?.ToString(), Does.Contain("Need Trophy"));
        Assert.That(lhs.ContainsKey("itemGuideDescription") && lhs["itemGuideDescription"]?.ToString()?.Contains("Need Trophy") == true, Is.False);
    }

    [Test]
    public void BuyProductRequirementGatesRefuseAndAllowMatchingClient()
    {
        Packs();
        var model = NewModel();
        model.Tables.ShopBuyConfirmPrice = 10_000;
        model.Player.Mesos = 100_000;
        var (h, c) = Load(model);
        long now = 1_700_000_000;
        c.ServerTime = () => now;
        var sent = new List<IOutPacket>();
        var notices = new List<string>();
        var boxes = new List<MessageBoxData>();
        c.Send = sent.Add;
        c.SystemNotice = notices.Add;
        c.ShowBox = boxes.Add;

        var rec = c.Products(0)[1].Record;
        rec.Price = 0; // free product: only the requirement gates can refuse

        // 1. Guild trophy gate: outside guild -> silent refuse, sends nothing, opens no bill
        rec.GuildTrophy = 1;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 0)));

        // In guild, joined < 7 days ago -> s_err_lack_guild_require_date notice, refuses
        c.IsInGuild = () => true;
        c.GuildMemberJoinTime = () => now - 100;
        c.GuildTotalTrophy = () => 10;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 1)));

        // In guild >= 7 days, trophies insufficient -> 0x5b notice, refuses
        notices.Clear();
        c.GuildMemberJoinTime = () => now - 604800;
        c.GuildTotalTrophy = () => 0;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 1)));
        c.GuildTotalTrophy = () => 5;

        // 2. Guild master gate (currency 7): non-master refuses with notice
        notices.Clear();
        rec.Currency = 7;
        c.GuildFund = () => 10_000;
        c.IsGuildMaster = () => false;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 1)));
        c.IsGuildMaster = () => true;
        rec.Currency = 0;

        // 3. Achievement gate: default HasAchieve refuses when absent
        notices.Clear();
        rec.Achieve = 21100001;
        rec.AchieveGrade = 1;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 1)));
        c.HasAchieve = (id, g) => id == 21100001 && g <= 1;

        // 4. Fame grade gate: default FameGradeOf(0) refuses when absent
        notices.Clear();
        rec.Alliance = 1;
        rec.FameGrade = 2;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 1)));
        // the alliance is named by its s_quest_alliance_<name> string (questmasktable.cpp:556-558), not its number
        string allianceName = model.Tables.Common("s_quest_alliance_darkwind");
        Assert.That(allianceName, Is.Not.Empty);
        Assert.That(notices[0], Does.Contain(allianceName));
        string gradeName = model.Tables.Common("s_fame_grade_2");
        Assert.That(gradeName, Is.Not.Empty);
        // the client strips the grade's tags (uishopdialog.cpp:3628)
        Assert.That(notices[0], Does.Contain(System.Text.RegularExpressions.Regex.Replace(gradeName, "<[^>]*>", "")));
        c.FameGradeOf = _ => 2;

        // 5. Championship gate: rank and member score
        notices.Clear();
        rec.ChampionshipRank = 3;
        rec.ChampionshipScore = 500;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 1)), "default rank 0 refuses");
        notices.Clear();
        c.GuildChampionshipRank = () => 3;
        c.GuildMemberChampionshipScore = () => 100;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 1)), "insufficient member score refuses");
        c.GuildMemberChampionshipScore = () => 500;

        // 6. Guild NPC level gate: default level 0 refuses when absent
        notices.Clear();
        rec.GuildNpc = 2;
        rec.GuildNpcLevel = 3;
        c.BuyProduct(1, 0);
        Assert.That((sent.Count, boxes.Count, notices.Count), Is.EqualTo((0, 0, 1)));
        c.GuildNpcLevel = npc => npc == 2 ? 3 : 0;

        // All gates satisfied -> sends mode 4
        notices.Clear();
        c.BuyProduct(1, 0);
        Assert.That(sent, Is.EqualTo(new[] { new ShopBuyRequest(1, 1, 106) }));
    }

    [Test]
    public void BindLiveStateWiresGuildFundTagCountSummonedPetPlayingScoreAndGates()
    {
        Packs();
        var model = NewModel();
        var (h, c) = Load(model);
        var sent = new List<IOutPacket>();
        var boxes = new List<MessageBoxData>();
        var notices = new List<string>();
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.SystemNotice = notices.Add;

        // Build live GuildBook with Fund = 5000 and local player "Hero" as master
        var guildInfo = new GuildInfo { Id = 42, Name = "TestGuild", MasterCharId = 100, Fund = 5_000 };
        guildInfo.Members.Add(new GuildMember { CharId = 100, Name = "Hero", Fc8 = 1000, Triple = (10, 5, 5), Fe0 = 250 });
        var guilds = new GuildBook();
        guilds.Apply(new GuildMessage(0) { GuildId = 42, Info = guildInfo });

        var me = new MyCharacter();
        me.Apply(new FieldAddUser
        {
            ObjectId = 1,
            User = new UserBlock { CharacterId = 100, Name = "Hero" },
            HasPet = true,
            Pet = new EquipItem { Id = 60000001, Uid = 555_000L, Amount = 1 },
        });

        // Pick a real item id with a non-empty ItemTags entry for tag-paid currency testing
        var taggedData = model.Tables.Items.Get(20000028);
        taggedData.StringTag = "DungeonRewardTicketA";
        int ticketTag = ItemTags.ByName("DungeonRewardTicketA");
        Assert.That(ticketTag, Is.GreaterThan(0));
        var invItems = new List<EquipItem> { new() { Id = 20000028, Uid = 11L, Amount = 12, Rarity = 1 } };
        long playingScore = 777_000L;

        c.BindLiveState(() => guilds, () => null, () => null, () => me, () => invItems, () => playingScore, model.Tables.Items);

        // 1. GuildFund & IsGuildMaster wired -> currency 7 product passes
        var guildProd = c.Products(0)[1];
        guildProd.Record.Currency = 7;
        guildProd.Record.Price = 2_000;
        Assert.That(c.CanPayProduct(guildProd, 1), Is.True);
        Assert.That(c.CheckGuildMaster(guildProd, true), Is.True);
        Assert.That(c.GuildTotalTrophy(), Is.EqualTo(20));
        Assert.That(c.GuildMemberJoinTime(), Is.EqualTo(1000L));
        Assert.That(c.GuildMemberChampionshipScore(), Is.EqualTo(250));

        // 2. ItemTagCount wired -> tag-paid stack gets MaxAffordableCount = 6 (12 tickets / price 2)
        var stackData = model.Tables.Items.Get(20000028);
        stackData.SlotMax = 99;
        var tagProd = new ShopController.Product
        {
            Record = new ShopProduct { Serial = 50, ItemId = 20000028, Currency = 1, CurrencyItemTag = ticketTag, Price = 2, Bundle = 1 },
            Item = Item.Create(model.Tables.Items, 20000028, 1),
        };
        Assert.That(c.ItemTagCount(ticketTag), Is.EqualTo(12));
        Assert.That(c.MaxAffordableCount(tagProd), Is.EqualTo(6));

        // 3. IsSummonedPet wired -> selling summoned pet uid refuses and sends nothing
        var summonedPet = Item.Create(model.Tables.Items, 20000028, 1);
        summonedPet.Uid = 555_000L;
        Assert.That(c.RequestSellItem(summonedPet, 1), Is.False);
        Assert.That(sent, Is.Empty);
        Assert.That(notices, Has.Count.EqualTo(1));

        // 4. PlayingScoreUid wired -> CanSellItem refuses currently playing score with box
        var scoreItem = Item.Create(model.Tables.Items, 20000028, 1);
        scoreItem.Uid = 777_000L;
        scoreItem.Data.Type = 0xc; // IsScoreItem
        Assert.That(c.CanSellItem(scoreItem), Is.False);
        Assert.That(boxes, Has.Count.EqualTo(1));
    }

    [Test]
    public void CanPayProductTruncatesTotalToUnsigned32LikeClient()
    {
        Packs();
        var model = NewModel();
        model.Tables.ShopBuyConfirmPrice = uint.MaxValue;
        model.Player.Mesos = 0;
        var (h, c) = Load(model);
        var sent = new List<IOutPacket>();
        var notices = new List<string>();
        c.Send = sent.Add;
        c.SystemNotice = notices.Add;

        var prod = c.Products(0)[1];
        // Within positive signed 32-bit int: 2,000,000,000 with 0 mesos refuses
        prod.Record.Price = 2_000_000_000L;
        Assert.That(c.CanPayProduct(prod, 1), Is.False);
        Assert.That(notices, Has.Count.EqualTo(1));

        // FUN_140db8580 truncates the total to 32 bits and CanPayCurrency takes it unsigned: 3,000,000,000 is refused
        notices.Clear();
        prod.Record.Price = 3_000_000_000L;
        Assert.That(c.CanPayProduct(prod, 1), Is.False);
        Assert.That(notices, Has.Count.EqualTo(1));
        c.BuyProduct(1, 0);
        Assert.That(sent, Is.Empty);
        // 2^32 wraps to 0 in the client's 32-bit total
        prod.Record.Price = 4_294_967_296L;
        Assert.That(c.CanPayProduct(prod, 1), Is.True);
    }

    [Test]
    public void HudSceneDispatchesShopProcessOnlyOncePerFrame()
    {
        // Walk up to repo root and check HudScene._Process calls ShopController.Process / ShopTick only once
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MS2Godot.sln"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null);
        string srcText = File.ReadAllText(Path.Combine(dir.FullName, "game", "ui", "HudScene.cs"));
        int start = srcText.IndexOf("public override void _Process(", StringComparison.Ordinal);
        int end = srcText.IndexOf("public override void _Input(", start, StringComparison.Ordinal);
        if (end < 0) end = Math.Min(srcText.Length, start + 4000);
        string body = srcText[start..end];
        int calls = System.Text.RegularExpressions.Regex.Matches(body, @"\b(shop\?\.Process|shop\.Process|ShopTick)\s*\(").Count;
        Assert.That(calls, Is.EqualTo(1), "UIShopDialog::vfunc_72 must run once per frame in HudScene._Process");
    }

    [Test]
    public void BindLiveNetworkWiresSendSystemShopOnCloseWithSortKind()
    {
        Packs();
        var (_, c) = Load(NewModel());
        var sent = new List<IOutPacket>();
        int talkEnd = 0;
        c.BindLiveNetwork(sent.Add, () => talkEnd++);

        c.SortKind = 4;
        c.Close();
        Assert.That(talkEnd, Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new SystemShopRequest(4, 0) }));
        Assert.That(c.SortKind, Is.EqualTo(0));

        // Closing again with SortKind == 0 sends no SystemShopRequest
        sent.Clear();
        c.Close();
        Assert.That(talkEnd, Is.EqualTo(2));
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void SystemShopPacketOpensShopAndSetsSortKindLikeClient()
    {
        Packs();
        var model = NewModel();
        var (h, c) = Load(model);
        int opened = 0;
        var boxes = new List<MessageBoxData>();
        string Key(string k) => model.Tables.Common(k);
        // type 0 is ignored
        Assert.That(SystemShopFlow.OnSystemShop(new SystemShopPacket(0, true), c, Key, () => opened++, boxes.Add), Is.False);
        // not ok: a box only for type 8 (s_shop_no_star_point_event), no dialog
        Assert.That(SystemShopFlow.OnSystemShop(new SystemShopPacket(3, false), c, Key, () => opened++, boxes.Add), Is.False);
        Assert.That((opened, boxes.Count), Is.EqualTo((0, 0)));
        Assert.That(SystemShopFlow.OnSystemShop(new SystemShopPacket(8, false), c, Key, () => opened++, boxes.Add), Is.False);
        Assert.That((opened, boxes.Count), Is.EqualTo((0, 1)));
        // the key is a row of the string data (a missing key would give "" or the key, and equal itself)
        Assert.That(Key("s_shop_no_star_point_event"), Is.Not.Empty.And.Not.EqualTo("s_shop_no_star_point_event"));
        Assert.That(boxes[0].Msg, Is.EqualTo(Key("s_shop_no_star_point_event")));
        // ok: the shop opens and SortKind is the type
        Assert.That(SystemShopFlow.OnSystemShop(new SystemShopPacket(5, true), c, Key, () => opened++, boxes.Add), Is.True);
        Assert.That((opened, c.SortKind), Is.EqualTo((1, 5)));
    }

    [Test]
    public void ToggleShopTabSendsOpenOrClosesLikeClient()
    {
        Packs();
        var (h, c) = Load(NewModel());
        var sent = new List<IOutPacket>();
        int closedShop = 0, closedInv = 0;
        var sounds = new List<string>();
        // shop closed: SystemShop(tab, 1) and the open sound
        Assert.That(SystemShopFlow.ToggleShopTab(4, c, false, () => closedShop++, () => closedInv++, sent.Add, sounds.Add), Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new SystemShopRequest(4, 1) }));
        Assert.That(sounds, Is.EqualTo(new[] { SystemShopFlow.OpenSound }));
        // open on the same tab: closes, nothing sent
        c.SortKind = 4; sent.Clear();
        Assert.That(SystemShopFlow.ToggleShopTab(4, c, true, () => closedShop++, () => closedInv++, sent.Add), Is.EqualTo(0));
        Assert.That((sent.Count, closedShop, closedInv), Is.EqualTo((0, 1, 1)));
        // open on another tab: closes both, then opens the new tab
        Assert.That(SystemShopFlow.ToggleShopTab(5, c, true, () => closedShop++, () => closedInv++, sent.Add), Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new SystemShopRequest(5, 1) }));
        Assert.That((closedShop, closedInv), Is.EqualTo((2, 2)));
        // the first guard (FUN_141adc7f0 / vfunc_73) refuses: 0, nothing sent, nothing closed
        sent.Clear(); sounds.Clear();
        Assert.That(SystemShopFlow.ToggleShopTab(6, c, true, () => closedShop++, () => closedInv++, sent.Add, sounds.Add, () => false), Is.EqualTo(0));
        Assert.That((sent.Count, sounds.Count, closedShop, closedInv), Is.EqualTo((0, 0, 2, 2)));
    }
}
