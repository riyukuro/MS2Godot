using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The character window (uimyinfodialog) run by its own ActionScript with UIMyInfoDialog's C++ side (needs MS2_DATA).</summary>
public class MyInfoTests
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

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };

    static (UiHost, MyInfoController, MyCharacter, List<string>) Load(Action<MyInfoController> setup = null)
    {
        var model = NewModel();
        model.HasLocalPlayer = true;
        var me = UiFixture.Character();
        var c = new MyInfoController { Record = () => me.ToPcInfo(0), StatPoints = me.StatPoints };
        setup?.Invoke(c);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) { p.Tick(); c.Process(i * 16); }
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, me, rt.Errors);
    }

    static List<object> Items(UiHost h, object dp) =>
        Enumerable.Range(0, (int)Rt.ToNumber(h.Get(dp, "length"))).Select(i => h.Invoke(dp, "requestItemAt", i)).ToList();

    [Test]
    public void TabsAreTheClientsTabList()
    {
        Packs();
        var (h, c, _, errors) = Load();
        Assert.That(errors, Is.Empty);
        var tabs = Items(h, h.Get(h.Child("m_tab"), "dataProvider"));
        var labels = tabs.Select(t => Rt.ToString(h.Get(t, "label"))).ToList();
        TestContext.WriteLine(string.Join(", ", labels));
        bool codi = c.Model.IsFeatureEnabled(0x8e);
        var expect = new List<string> { "$s_myInfo_game", "$s_myInfo_skin" };
        if (codi) expect.Add("$s_myInfo_codi");
        expect.Add("$s_myInfo_badge");
        Assert.That(labels, Is.EqualTo(expect));
        Assert.That(tabs.Select(t => (int)Rt.ToNumber(h.Get(t, "tabIndex"))), Is.EqualTo(codi ? new[] { 0, 1, 3, 2 } : new[] { 0, 1, 2 }));
        Assert.That(c.ShownBag, Is.EqualTo(MyInfoController.EquipBag));
        c.OnCallback("onChangeTab", new object[] { 2 });
        Assert.That(c.ShownBag, Is.EqualTo(MyInfoController.GemBag));
    }

    [Test]
    public void ProfileAndStatPointsComeFromTheRecord()
    {
        Packs();
        var (h, c, _, _) = Load();
        Assert.That(Rt.ToString(h.Get(h.Child("m_level"), "text")), Is.EqualTo("21"));
        Assert.That(Rt.ToString(h.Get(h.Child("m_job"), "text")), Is.EqualTo(c.Model.Tables.JobName(100)));
        // the six cost stats: name, Stats2 (health: Stats0), orange where points were put
        var row1 = (AsDisplayObject)h.Child("m_statPoint1");
        Assert.That(Rt.ToString(h.Get(h.FindNamed("m_Label", row1), "text")), Is.EqualTo(c.Model.Tables.StatName(0)));
        Assert.That(Rt.ToString(h.Get(h.FindNamed("m_point", row1), "text")), Is.EqualTo("48"));
        Assert.That(Rt.ToNumber(h.Get(h.FindNamed("m_point", row1), "textColor")), Is.EqualTo(0xffa200));
        var row5 = (AsDisplayObject)h.Child("m_statPoint5");
        Assert.That(Rt.ToString(h.Get(h.FindNamed("m_point", row5), "text")), Is.EqualTo("2100"), "health shows Stats0");
        Assert.That(Rt.ToNumber(h.Get(h.FindNamed("m_point", row5), "textColor")), Is.EqualTo(0xffffff));
        var remain = h.FindNamed("m_remainStatPoint", (AsDisplayObject)h.Child("m_remainPoint"));
        Assert.That(Rt.ToString(h.Get(remain, "text")), Is.EqualTo("3"));
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_resetStatPoint"), "enabled")), Is.True);
        // defense without feature 0xdf: Stats2[0x14]
        if (!c.Model.IsFeatureEnabled(0xdf))
            Assert.That(Rt.ToString(h.Get(h.FindNamed("value", (AsDisplayObject)h.Child("m_defenseInfo")), "text")), Is.EqualTo("41"));
    }

    [Test]
    public void CodiTabWithoutAPeriodShowsTheBoxAndOkGoesBack()
    {
        Packs();
        MessageBoxData shown = null;
        var (h, c, me, _) = Load(c => c.ShowBox = b => shown = b);
        if (!c.Model.IsFeatureEnabled(0x8e)) Assert.Ignore("feature 0x8e is off: no codi tab");
        var before = MessageBoxData.CommonByKey;
        MessageBoxData.CommonByKey = k => k == MyInfoController.CodiTabNotActiveKey ? "not active" : "";
        try { Assert.That(c.OnCallback("onChangeTab", new object[] { 3 }), Is.False); }
        finally { MessageBoxData.CommonByKey = before; }
        Assert.That(c.Tab, Is.EqualTo(3));
        Assert.That(shown?.Msg, Is.EqualTo("not active"));
        Assert.That(shown.Type, Is.EqualTo(0));
        shown.Callback(0, shown);
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_tab"), "selectedIndex")), Is.EqualTo(0));
    }

    [Test]
    public void RequestsAreOnlyRaised()
    {
        Packs();
        var (_, c, _, _) = Load();
        var raised = new List<string>(); var toggled = new List<int>();
        c.Button += raised.Add; c.ToggleDialog += toggled.Add;
        Assert.That(c.OnCallback("OnAddStatPoint", new object[] { 1 }), Is.True);
        Assert.That(c.OnCallback("OnResetStatPoint", Array.Empty<object>()), Is.True);
        Assert.That(c.OnCallback("OnTitleChange", new object[] { 0 }), Is.False);
        Assert.That(c.OnCallback("onSymbolClick", Array.Empty<object>()), Is.False);
        Assert.That(raised, Is.EqualTo(new[] { "OnAddStatPoint", "OnResetStatPoint", "OnTitleChange" }));
        Assert.That(toggled, Is.EqualTo(new[] { 0x4ed5 }));
    }

    [Test]
    public void EquipSlotsAreBoundOnTheItemSlotPath()
    {
        Packs();
        var (h, c, _, errors) = Load();
        Assert.That(errors, Is.Empty);
        // set_equipBagID gave each m_equip child its name by slotIndex: slot0 = CP (6), slot10 = RH (5), slot11 = LH (4)
        var equip = (AsDisplayObject)h.Child("m_equip");
        Assert.That(Rt.ToString(h.Get(h.FindNamed("slot0", equip), "binding")), Is.EqualTo(MyInfoController.EquipBag + ".6"));
        Assert.That(Rt.ToString(h.Get(h.FindNamed("slot10", equip), "binding")), Is.EqualTo(MyInfoController.EquipBag + ".5"));
        var rh = c.GetBinding(MyInfoController.EquipBag + ".5");
        Assert.That(rh, Is.Not.Null);
        Assert.That(Rt.ToNumber(h.Get(rh, "itemID")), Is.EqualTo(15000001));
        Assert.That(Rt.IsNullish(h.Get(rh, "grayColor")), Is.True);
        var lh = c.GetBinding(MyInfoController.EquipBag + ".4");
        Assert.That(Rt.ToNumber(h.Get(lh, "itemID")), Is.EqualTo(15000001), "an empty LH shows the two-handed RH item");
        Assert.That(Rt.ToNumber(h.Get(lh, "grayColor")), Is.EqualTo(1));
        Assert.That(c.GetBinding(MyInfoController.EquipBag + ".6"), Is.Null, "an empty slot");
        bool skin = c.Model.Tables.Items.Get(11400021).HasSkinType;
        Assert.That(c.GetBinding(MyInfoController.EquipBag + ".8") is null, Is.EqualTo(skin), "the top is on the outfit tab when it has a skin type");
        // the outfit tab (1)
        c.OnCallback("onChangeTab", new object[] { 1 });
        var top = c.GetBinding(MyInfoController.EquipBag + ".8");
        Assert.That(top is null, Is.EqualTo(!skin));
        if (top != null) Assert.That(Rt.ToNumber(h.Get(top, "itemBothEquip")), Is.EqualTo(0));
        // the gem tab (2): the chat balloon is index 2 (gem type 3)
        c.OnCallback("onChangeTab", new object[] { 2 });
        var gem = c.GetBinding(MyInfoController.GemBag + ".2");
        Assert.That(Rt.ToNumber(h.Get(gem, "itemID")), Is.EqualTo(70200001));
        Assert.That(Rt.ToNumber(h.Get(gem, "gemSystem")), Is.EqualTo(3));
        Assert.That(Rt.ToBoolean(h.Get(gem, "lock")), Is.False);
        Assert.That(Rt.ToBoolean(h.Get(gem, "config")), Is.False, "subtype 3");
        Assert.That(c.GetBinding(MyInfoController.GemBag + ".0"), Is.Null, "transparency: always valid, no item");
    }

    [Test]
    public void GearScoreIsZeroOnlyWithoutLayerZeroItems()
    {
        Packs();
        var (h, _, _, _) = Load();
        var il = (AsDisplayObject)h.Child("m_itemLevelInfo");
        string worn = Rt.ToString(h.Get(h.FindNamed("detail", il), "text"));
        TestContext.WriteLine("movie default detail: " + worn);
        Assert.That(worn, Is.Not.EqualTo("0 + (0)"), "a worn weapon: the attack getters are not here, the label is left");
        var (h2, _, _, _) = Load(c =>
        {
            var me = UiFixture.Character();
            me.Apply(new FieldAddUser { ObjectId = 1, User = me.User });
            c.Record = () => me.ToPcInfo(0);
        });
        var il2 = (AsDisplayObject)h2.Child("m_itemLevelInfo");
        Assert.That(Rt.ToString(h2.Get(h2.FindNamed("value", il2), "text")), Is.EqualTo("0"));
        Assert.That(Rt.ToString(h2.Get(h2.FindNamed("detail", il2), "text")), Is.EqualTo("0 + (0)"));
    }

    [Test]
    public void StatPointRowsHaveTheClientsTooltips()
    {
        Packs();
        var (h, c, _, _) = Load();
        var row = h.Child("m_statPoint1");
        var fn = h.Get(row, "externalTooltipFunction") as AsFunction;
        Assert.That(fn, Is.Not.Null);
        var tip = fn.Call(row, Array.Empty<object>());
        Assert.That(Rt.ToString(h.Get(tip, "tooltip")), Is.EqualTo("export_common_tooltip_tooltipText"));
        Assert.That(Rt.ToNumber(h.Get(tip, "tooltipAlign")), Is.EqualTo(4));
        Assert.That(Rt.ToString(h.Get(tip, "text")), Is.EqualTo(c.Model.Tables.Common("s_char_info_stat_point_tooltip_str") ?? ""));
        var remain = h.Child("m_remainPoint");
        var rf = h.Get(remain, "externalTooltipFunction") as AsFunction;
        Assert.That(rf, Is.Not.Null);
        TestContext.WriteLine(Rt.ToString(h.Get(rf.Call(remain, Array.Empty<object>()), "text")));
    }

    [Test]
    public void AdvancedShowsTheStatLabels()
    {
        Packs();
        var (h, c, _, errors) = Load();
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("hide"));
        h.Invoke(h.Entity, "onDetailInfo", new object[] { null });
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(1000 + i * 16); }
        Assert.That(errors, Is.Empty);
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("show"));
        var manager = h.Child("m_statLabelManager");
        // a container's child 0 is its m_background; StatLabelContainer.getStatLabel puts labelIndex n on the next one
        string Cell(int container, int label, string field) =>
            Rt.ToString(h.Get(h.Get(h.Invoke(h.Invoke(manager, "getChildAt", container), "getChildAt", label + 1), field), "text"));
        Assert.That((Cell(0, 0, "m_statName"), Cell(0, 0, "m_statValue")), Is.EqualTo((c.Model.Tables.StatName(4), "2100")));
        Assert.That((Cell(0, 3, "m_statName"), Cell(0, 3, "m_statValue")), Is.EqualTo((c.Model.Tables.StatName(0), "48")));
        Assert.That(Cell(0, 7, "m_statValue"), Is.EqualTo(c.Model.Tables.Common("s_common_percent").Replace("{0}", "120.0")), "a rate stat");
        Assert.That(Cell(0, 8, "m_statName"), Is.EqualTo(c.Model.Tables.Common("s_pc_info_name_shield")));
        Assert.That(Cell(2, 0, "m_statValue"), Is.EqualTo("41"), "defense");
    }

    [Test]
    public void SlotTooltipsAreTheItemTooltip()
    {
        Packs();
        var (h, c, _, errors) = Load();
        var t = c.Model.Tables;
        // gear tab: RH has the two-handed weapon; the own record is 0x200, so the putoff hint is set
        var rh = c.GetSlotTooltip(MyInfoController.EquipBag + ".5");
        Assert.That(rh["tooltip"], Is.EqualTo("export_common_tooltip_kindsItem"));
        Assert.That(rh["itemName"], Is.EqualTo(t.ItemName(15000001)));
        Assert.That(rh["itemGrade"], Is.EqualTo(2), "the record's rarity");
        Assert.That(rh["itemKinds"], Is.EqualTo(ItemTooltip.Kinds(t.Items.Get(15000001).IconCode)));
        Assert.That(rh["enablePuton"], Is.EqualTo(t.Common("s_msg_rclick_putoff_equip")));
        Assert.That(rh["tooltipAlign"], Is.EqualTo(5));
        Assert.That(c.GetSlotTooltip(MyInfoController.EquipBag + ".6"), Is.Empty, "an empty slot: the object without members");
        Assert.That(c.GetSlotTooltip(MyInfoController.EquipBag + ".99"), Is.Null, "no such slot");
        // outfit tab: the top
        c.OnCallback("onChangeTab", new object[] { 1 });
        var top = c.GetSlotTooltip(MyInfoController.EquipBag + ".8");
        Assert.That(top["itemName"], Is.EqualTo(t.ItemName(11400021)));
        Assert.That(top["itemSkin"], Is.EqualTo(true));
        // badge tab: an empty valid type is its text tooltip; the chat balloon badge is an item tooltip with an empty hint
        c.OnCallback("onChangeTab", new object[] { 2 });
        var empty = c.GetSlotTooltip(MyInfoController.GemBag + ".0");
        Assert.That((empty["tooltip"], empty["text"], empty["tooltipAlign"]), Is.EqualTo(("export_common_tooltip_tooltipText", "$s_tooltip_myinfo_gem_trans", 3)));
        var emptyShown = TooltipTests.Draw(empty);
        TestContext.WriteLine("empty badge: " + string.Join(" | ", emptyShown));
        Assert.That(emptyShown, Has.Some.Contains("Transparency Badge"), "lv10_badges_hover_empty_slot.png");
        var badge = c.GetSlotTooltip(MyInfoController.GemBag + ".2");
        Assert.That(badge["enablePuton"], Is.EqualTo(""));
        Assert.That(badge.ContainsKey("itemName"), Is.False, "a gem's name (GetGemName) is not ported");
        Assert.That(errors, Is.Empty);

        // the tooltip movie draws the top's name and hint
        c.OnCallback("onChangeTab", new object[] { 1 });
        var shown = TooltipTests.Draw(c.GetSlotTooltip(MyInfoController.EquipBag + ".8"));
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Does.Contain(t.ItemName(11400021)));
        Assert.That(shown, Has.Some.Contains("right-click"));
        // korItemDescription.xml 11400021: the description and the outfit guide line (as the Green Hood capture's)
        Assert.That(shown, Has.Some.Contains("A simple yet elegant striped shirt."));
        Assert.That(shown, Has.Some.Contains("Outfit items replace the look of your gear when equipped."));
    }

    [Test]
    public void TradeLinesAreTheClientsForABoundOutfit()
    {
        // the Green Hood Uniform of outfits_hover_equipped_top.png: bound to Solo, transfer type 2, no shopSell, no
        // enableBreak, socketDataId 9006 (grade 0 row: 4 max, 0 open) with the server's socket transfer flag
        var m = NewModel();
        var r = new EquipItem { Id = 12200398, Uid = 9, Rarity = 1, Amount = 1 };
        r.Transfer.Rule = 8; r.Transfer.HasBinding = true; r.Transfer.BindingId = 2; r.Transfer.BindingName = "Solo"; r.Transfer.SocketTransferEnabled = true;
        var item = Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items);
        var tip = new ItemTooltip(m, 0x200) { Owner = (2, "Solo") };
        Assert.That(tip.Fill(item), Is.True);
        Assert.That(tip.Data["notExchange"], Is.EqualTo("Untradable, Unsellable\nBound to Solo\nThis outfit cannot be dismantled"));
        var shown = TooltipTests.Draw(tip.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Bound to Solo"));

        // without the socket transfer flag the client adds its line; a binding to someone else keeps the binding's name
        r.Transfer.SocketTransferEnabled = false; r.Transfer.BindingId = 5; r.Transfer.BindingName = "Other";
        tip = new ItemTooltip(m, 0x200) { Owner = (2, "Solo") };
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items));
        Assert.That(tip.Data["notExchange"], Is.EqualTo("Untradable, Unsellable\nBound to Other\nThis outfit cannot be dismantled\nSocket cannot be transferred."));

        // unbound, tradable 3 times, a weapon (not skin): the count line, then "Binds to character" for type 1
        var w = new EquipItem { Id = 15000001, Uid = 10, Rarity = 2, Amount = 1 };
        w.Transfer.Rule = 8 | 0x10 | 4; w.Transfer.Count = 3;
        tip = new ItemTooltip(m, 0x200);
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(w, m.Tables.Items));
        var wd = m.Tables.Items.Get(15000001);
        string bind = wd.TransferType switch { 2 => "Binds on Loot", 3 or 4 => "Binds on Equip", _ => "Binds to character" };
        string first = wd.ShopSell ? "<FONT color='#FFD200'>Tradable 3 time(s)</FONT>" : "Tradable 3 time(s), Unsellable";
        TestContext.WriteLine($"15000001: transferType {wd.TransferType} shopSell {wd.ShopSell} -> {tip.Data["notExchange"]}");
        Assert.That(tip.Data["notExchange"], Is.EqualTo(first + "\n" + bind));

        // a gem in a socket: the gem checks are not ported, so the member is left out
        w.Socket.MaxSlot = 1; w.Socket.Gems.Add(new SocketGem { GemId = 40100001 });
        tip = new ItemTooltip(m, 0x200);
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(w, m.Tables.Items));
        Assert.That(tip.Data.ContainsKey("notExchange"), Is.False);
    }

    [Test]
    public void DyeLineIsTheClientsForACustomizableOutfit()
    {
        // the Green Hood Uniform's model 12200398: <customize color="1" colorPalette="10" defaultColorIndex="13">
        var m = NewModel();
        m.HasLocalPlayer = true;
        var gm = m.Tables.ItemModels.Get(m.Tables.Items.Get(12200398).ItemPreset);
        Assert.That(gm, Is.Not.Null);
        Assert.That((gm.ColorEnabled, gm.Palette, gm.DefaultColor(0), gm.MultiColor), Is.EqualTo((true, 10, 13, false)));
        var r = new EquipItem { Id = 12200398, Uid = 9, Rarity = 1, Amount = 1 };
        var tip = new ItemTooltip(m, 0x200);
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items));
        // outfits_hover_equipped_top.png's line
        Assert.That(tip.Data["customizeColor"], Is.EqualTo("Can be dyed at the Dye Workshop on Beauty Street."));
        var shown = TooltipTests.Draw(tip.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Can be dyed at the Dye Workshop"));

        // no local player: the client returns before the line
        m.HasLocalPlayer = false;
        tip = new ItemTooltip(m, 0x200);
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items));
        Assert.That(tip.Data.ContainsKey("customizeColor"), Is.False);

        // a weapon has no dyeable model
        m.HasLocalPlayer = true;
        tip = new ItemTooltip(m, 0x200);
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(new EquipItem { Id = 15000001, Uid = 10, Rarity = 2, Amount = 1 }, m.Tables.Items));
        Assert.That(tip.Data.ContainsKey("customizeColor"), Is.False);
    }

    [Test]
    public void LockLinesAreTheClients()
    {
        var flash = GameStrings.Parse(Packs().Read("Xml", "string/en/korflash.xml")).Root.Elements("key")
            .Where(e => e.Attribute("id").Value.StartsWith("$s_itemlock_tooltip_msg"))
            .ToDictionary(e => (string)e.Attribute("id"), e => (string)e.Attribute("value"));
        var now = new DateTime(2026, 11, 18, 12, 0, 0);
        var m = NewModel();
        m.ServerTime = () => now;
        long t0 = new DateTimeOffset(now).ToUnixTimeSeconds();
        var r = new EquipItem { Id = 12200398, Uid = 9, Rarity = 1, Amount = 1 };
        ItemTooltip Tip(int state, long unlock)
        {
            var item = Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items);
            item.LockState = state; item.UnlockTime = unlock;
            var tip = new ItemTooltip(m, 0x200) { Localize = k => flash[k] };
            tip.Fill(item);
            return tip;
        }
        var locked = Tip(1, 0);
        Assert.That((locked.Data["itemIconLock"], locked.Data["itemIconUnLock"]), Is.EqualTo((true, false)));
        Assert.That(locked.Data["itemLockInfo"], Is.EqualTo("<FONT color='#FF4040'>This item is locked.</FONT>"));
        var shown = TooltipTests.Draw(locked.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("This item is locked."));

        // unlocking in 2 h 5 min 30 s: hours and whole minutes
        var unlocking = Tip(0, t0 + 2 * 3600 + 5 * 60 + 30);
        Assert.That((unlocking.Data["itemIconLock"], unlocking.Data["itemIconUnLock"]), Is.EqualTo((false, true)));
        Assert.That(unlocking.Data["itemLockInfo"], Is.EqualTo("<FONT color='#FF4040'>This item will be unlocked in 2 hour(s) and 5 minute(s).</FONT>"));
        // under a minute left: at least 1 minute, the no-hours text
        Assert.That(Tip(0, t0 + 20).Data["itemLockInfo"], Is.EqualTo("<FONT color='#FF4040'>This item will be unlocked in 1 minute(s).</FONT>"));
        // unlocked: neither icon, no line
        var open = Tip(0, t0 - 5);
        Assert.That((open.Data["itemIconLock"], open.Data["itemIconUnLock"]), Is.EqualTo((false, false)));
        Assert.That(open.Data.ContainsKey("itemLockInfo"), Is.False);
    }

    [Test]
    public void UsePeriodLinesAreTheClients()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0);
        var m = NewModel();
        m.ServerTime = () => now;
        long t0 = new DateTimeOffset(now).ToUnixTimeSeconds();
        string Period(int id, long expiry = 0)
        {
            var item = Item.Create(m.Tables.Items, id, 1);
            item.ExpiryTime = expiry;
            var tip = new ItemTooltip(m, 0x200);
            tip.Fill(item);
            return tip.Data.TryGetValue("itemUsePeriod", out var v) ? (string)v : null;
        }
        // itemdata 203: <life usePeriod="1440">, expirationType 2 with 2 months, expirationType 1 without a count,
        // expirationPeriod 2016-02-25-07-00-00
        Assert.That(Period(20300008), Is.EqualTo("This is a temporary item. (1 d)"));
        Assert.That(Period(20300043), Is.EqualTo("This item expires 2 month(s) after you obtain it."));
        Assert.That(Period(20301428), Is.EqualTo("This item expires 1 week(s) after you obtain it."));
        // the fields come back as written (the zone offset is added and taken off again)
        Assert.That(Period(20300081), Is.EqualTo("This is a temporary item.\n(Expires 2/25/2016 07:00)"));
        // an owned item's expiry time comes first: its end date, then the expired form
        var at = Ms2.Net.MsTime.LocalTm(t0 + 3 * 86400);
        Assert.That(Period(20300008, t0 + 3 * 86400), Is.EqualTo($"Expires {at.Month}/{at.Day}/{at.Year} {at.Hour:00}:{at.Minute:00}"));
        Assert.That(Period(20300008, t0 - 60), Does.StartWith("<FONT color='#FF5F5F'>(Expired "));
        // no life: no member
        Assert.That(Period(12200398), Is.Null);
    }

    [Test]
    public void ItemDescriptionsAreTheStringTablesRows()
    {
        var t = new GameTables(Packs(), "en");
        Assert.That(t.ItemDescription(11400021, "tooltipDescription"), Is.EqualTo("A simple yet elegant striped shirt."));
        Assert.That(t.ItemDescription(11400021, "nope"), Is.EqualTo(""));
        Assert.That(t.ItemDescription(1, "tooltipDescription"), Is.EqualTo(""));
        // the object words are replaced: $item:13100028$ is that item's name
        Assert.That(t.ItemDescription(13100153, "tooltipDescription"), Does.StartWith("When " + t.ItemName(13100028) + " was thrown"));
        // 11301252 has a WorldChampionMushroom row and an Event / NA row: the last available one is kept
        var rows = GameStrings.Parse(Packs().Read("Xml", "string/en/koritemdescription.xml")).Root.Elements("key")
            .Where(e => (string)e.Attribute("id") == "11301252" && FeatureElements.IsAvailable(Packs(), e)).ToList();
        string want = rows.Count == 0 ? "" : ((string)rows[^1].Attribute("tooltipDescription")).Replace("\\n", "\n");
        TestContext.WriteLine($"11301252: {rows.Count} available rows -> {want}");
        Assert.That(t.ItemDescription(11301252, "tooltipDescription"), Is.EqualTo(want));
    }

    [Test]
    public void ScoreTooltipsAreDrawnAsTheClients()
    {
        Packs();
        var (h, c, _, errors) = Load();
        if (!c.Model.IsFeatureEnabled(0xdf)) Assert.Ignore("feature 0xdf off in this data");
        foreach (var (label, title) in new[] { ("m_attackInfo", "Attack Score"), ("m_defenseInfo", "Defense Score") })
        {
            var data = UiHost.FromAs(h.Get(h.Child(label), "_tooltipData")) as IDictionary<string, object>;
            Assert.That(data, Is.Not.Null, label);
            var shown = TooltipTests.Draw(data);
            TestContext.WriteLine(label + ": " + string.Join(" | ", shown));
            Assert.That(shown, Has.Some.StartsWith(title), "lv10_hover_attack_score.png / lv10_hover_defense_score.png");
        }
        Assert.That(errors, Is.Empty);
    }
}
