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
        Assert.That(c.ShopEnabled, Is.True);
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
        c.RequireAchieveText = (_, _) => "Test Trophy";
        c.Wearer = () => new PcInfo { Equips = new List<EquipItem> { new() { Id = 11300001, Uid = 999, Rarity = 1 } } };

        var row = h.Obj(("serialID", 1), ("productType", 0));
        var tip = c.GetTooltip(new object[] { "", row });
        Assert.That(tip, Is.Not.Null);
        Assert.That(tip.ContainsKey("requireAchieve"), Is.True);
        Assert.That(tip["requireAchieve"]?.ToString(), Does.Contain("Test Trophy"));
        Assert.That(c.GetTooltip(new object[] { "m_coinInfo" }), Is.Null, "non-gacha shop has no coin tooltip");
    }
}
