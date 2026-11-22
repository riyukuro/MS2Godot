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
[NonParallelizable]   // sets process-wide hooks the product reads
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

    static (UiHost, MyInfoController, MyCharacter, List<string>) Load(Action<MyInfoController> setup = null,
                                                                       Func<MyCharacter, MyInfoController> make = null)
    {
        var model = NewModel();
        model.HasLocalPlayer = true;
        var me = UiFixture.Character();
        var c = make?.Invoke(me) ?? new MyInfoController { Record = () => me.ToPcInfo(0), StatPoints = me.StatPoints };
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
    public void TrophyCountIsTheAchieveTally()
    {
        Packs();
        uint sum = 0;
        var (h, c, _, errors) = Load(x => x.AchieveTally = () => sum);
        Assert.That(errors, Is.Empty);
        var point = h.Child("m_trophyPoint");
        Assert.That(Rt.ToString(h.Get(point, "text")), Is.EqualTo("0"));
        sum = 1234;                                                    // OnPacket mode 2: FUN_141a5a560, then vfunc_15
        c.MarkChanged(0);
        c.Process(0);
        Assert.That(Rt.ToString(h.Get(point, "text")), Is.EqualTo("1234"));
    }

    [Test]
    public void OtherCharactersWindowShowsTheAnswersRecord()
    {
        // UIPCInfoDialog (0x4ec8): the same movie over the answer's record; its own bag names, the record's trophy count
        // (vfunc_84: setTrophy of m_3d4), stat buttons that do nothing, and the record cleared at the close (vfunc_67)
        Packs();
        PcInfo remote = null;
        var req = new PcInfoRequests();
        int buttons = 0;
        var (h, c, _, errors) = Load(x => x.Button += _ => buttons++, me =>
        {
            remote = me.ToPcInfo(0);
            remote.IsLocal = false;
            remote.Trophy = 77;
            remote.Level = 33;
            req.Info = remote;
            return new MyInfoController { PcInfoDialog = true, Requests = req, Record = () => req.Info };
        });
        Assert.That(errors, Is.Empty);
        Assert.That(Rt.ToString(h.Get(h.Child("m_trophyPoint"), "text")), Is.EqualTo("77"));
        Assert.That(Rt.ToString(h.Get(h.Child("m_level"), "text")), Is.EqualTo("33"));
        Assert.That(c.ShownBag, Is.EqualTo(MyInfoController.PcEquipBag));
        c.OnCallback("onChangeTab", new object[] { 2 });
        Assert.That(c.ShownBag, Is.EqualTo(MyInfoController.PcGemBag));
        Assert.That(c.OnCallback("onAddStatPoint", new object[] { 0 }), Is.True);
        Assert.That(c.OnCallback("onResetStatPoint", new object[0]), Is.True);
        Assert.That(buttons, Is.Zero);
        remote.Level = 34;                                              // vfunc_79's loaded branch
        c.ShowRecord();
        Assert.That(Rt.ToString(h.Get(h.Child("m_level"), "text")), Is.EqualTo("34"));
        c.Close();
        Assert.That(req.Info, Is.Null);
    }

    [Test]
    public void PutOffBuildsTheEquipBagAgain()
    {
        var t = new GameTables(Packs(), "en");
        var (h, c, me, errors) = Load();
        Assert.That(errors, Is.Empty);
        string rh = MyInfoController.EquipBag + ".5";
        Assert.That(c.GetBinding(rh), Is.Not.Null);
        int equipChanges = 0;
        me.EquipChanged += () => equipChanges++;
        long uid = me.Equips.First(e => e.Slot == EquipSlot.RH).Uid;
        Assert.That(me.Apply(new ItemPutOff(me.ObjectId, uid), t.Items.WornLayer), Is.True);
        Assert.That(equipChanges, Is.EqualTo(1));
        // FUN_141a5a560 alone (a stat or trophy change) keeps the bag's record: RebuildPCInfo(1, 0, 0)
        c.MarkChanged(0);
        c.Process(0);
        Assert.That(c.GetBinding(rh), Is.Not.Null);
        // the put-off (vfunc_22) also sets m_20c: RebuildPCInfo(1, 0, 1) builds the bag over the new record
        c.MarkDirty(16);
        c.Process(16);
        Assert.That(c.GetBinding(rh), Is.Null);
        Assert.That(c.ShownBag, Is.EqualTo(MyInfoController.EquipBag));
    }

    [Test]
    public void SymbolButtonIsTheOwnSymbol()
    {
        Packs();
        // symbol 0 (no condition): its name from string table 39, drawn disabled
        var (h, c, _, errors) = Load();
        Assert.That(errors, Is.Empty);
        var btn = (AsDisplayObject)h.Child("m_symbolBtn");
        Assert.That(Rt.ToBoolean(h.Get(btn, "visible")), Is.True);
        Assert.That(Rt.ToString(h.Get(btn, "label")), Is.EqualTo(c.Model.Tables.NametagSymbolTitle(0)).And.Not.Empty);
        var icon = h.Get(btn, "m_icon");
        Assert.That(Rt.ToNumber(h.Get(icon, "numChildren")), Is.EqualTo(1));
        // a level symbol is met at level 50 (the fixture is level 21) and an adventure level one is ranked by the level
        var t = c.Model.Tables.NametagSymbols;
        var p = new NametagSymbols.Player(21, 0, 0, Array.Empty<uint>(), 0);
        foreach (ushort id in Enumerable.Range(0, 2000).Select(i => (ushort)i))
        {
            if (t.Get(id) is not { } s) continue;
            if (s.Condition == NametagSymbols.Condition.Level)
            {
                Assert.That(t.CheckCondition(id, p), Is.EqualTo(0u));
                Assert.That(t.CheckCondition(id, p with { Level = 50 }), Is.EqualTo(1u));
            }
            if (s.Condition == NametagSymbols.Condition.AdventureLevel)
                Assert.That(t.IconRanked(id, p with { AdventureLevel = 650 }), Is.EqualTo(s.Icon + "_500"));
        }
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
    public void EmptyEquipSlotsShowTheirSlotTypesSilhouette()
    {
        Packs();
        var (h, c, _, errors) = Load();
        Assert.That(errors, Is.Empty);
        var equip = (AsDisplayObject)h.Child("m_equip");
        var cp = h.FindNamed("slot0", equip);
        Assert.That(Rt.ToString(h.Get(cp, "m_strSlotType")), Is.EqualTo("CP"));
        var bg = h.Get(h.Get(cp, "m_slotType"), "m_bg");                   // setSlotBg's m_slotBg
        Assert.That(Rt.ToNumber(h.Get(bg, "numChildren")), Is.EqualTo(1));
        Assert.That(Rt.ToString(h.Get(h.Invoke(bg, "getChildAt", 0), "type")), Is.EqualTo("CP"), "UIMyInfoDialog_slotbg_CP");
        Assert.That(Rt.ToString(h.Get(h.FindNamed("slot10", equip), "m_strSlotType")), Is.EqualTo("RH"));
        c.OnCallback("onChangeTab", new object[] { 2 });
        Assert.That(Rt.ToString(h.Get(h.FindNamed("slot2", (AsDisplayObject)h.Child("m_equip")), "m_strSlotType")), Is.EqualTo("chatBalloon"));
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
    public void GearScoreIsTheWornLayerZeroItemsLevel()
    {
        Packs();
        var (h, _, _, _) = Load();
        var il = (AsDisplayObject)h.Child("m_itemLevelInfo");
        // RH 15000001: gearScore 10, subtype 0x32 (factor 1), rarity 2: 10 * 10 + 1 * 5 = 105, no enchant level under gear 70
        // without rank 4; the CL item 11400021 has a skin type (layer 1), which adds nothing
        Assert.That(Rt.ToString(h.Get(h.FindNamed("value", il), "text")), Is.EqualTo("105"));
        Assert.That(Rt.ToString(h.Get(h.FindNamed("detail", il), "text")), Is.EqualTo("105 + (0)"));
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
        Assert.That(badge["itemName"], Is.EqualTo(t.ItemName(70200001)), "GetGemName: a chat balloon gem (not a pet skin) is its table name");
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
    public void UnlimitedEnchantItemLevelIsTheLuaChunks()
    {
        // calcUnlimitedEnchantItemLevel (luautil.cpp:1181 and its Lua chunk), by plus level
        var want = new Dictionary<int, int> { [0] = 84295, [1] = 85138, [5] = 88510, [9] = 91882, [10] = 92725, [15] = 96940, [19] = 100312,
            [20] = 101576, [35] = 120543, [50] = 144567, [77] = 212425, [99] = 294192, [100] = 0 };
        foreach (var (plus, level) in want) Assert.That(ItemLevels.UnlimitedEnchantLevel(plus, 0, 0), Is.EqualTo(level), "plus " + plus);
    }

    static List<string> RowTexts(AsTooltip data) =>
        ((IEnumerable<object>)data["extraDescription"]).Cast<Dictionary<string, object>>().Select(r => r["text"] + " #" + r["color"]).ToList();

    [Test]
    public void ItemStatusIsTheClientsOptionRows()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        var t = m.Tables;
        ItemTooltip Tip(EquipItem r, int flags = 0x200) { var tip = new ItemTooltip(m, flags); tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, t.Items)); return tip; }
        string Status(ItemTooltip tip) => tip.Data.TryGetValue("itemStatus", out var v) ? (string)v : null;
        const string space = "<font size='3'>\n</font>";
        string S(int stat) => t.StatName(stat);
        string V(int stat, int v) => Ms2.Quest.ClientFormat.Format(t.ItemOptions.StatValueText(stat), v);   // FUN_140631600
        string Sp(int tenths) => Ms2.Quest.ClientFormat.Format(t.ItemOptions.SpecialValueText(7), tenths / 10.0f);   // a rate special

        // 15000001 (level 10, random option 15000001; its data has stats 0x1b / 0x1c, so kind 0 skips them)
        var weapon = new EquipItem { Id = 15000001, Uid = 10, Rarity = 2, Amount = 1 };
        weapon.Options.Stat[0][0x1b] = 10; weapon.Options.Stat[0][0x1c] = 20; weapon.Options.Stat[0][0x17] = 5;
        // CAtlMap order: bin key % 17, the newest first in a bin: cap (0x11) and str (0) share bin 0
        weapon.Options.Stat[2][1] = 12; weapon.Options.Stat[2][0] = 18; weapon.Options.Stat[2][0x11] = 8;
        weapon.Options.Special[2][7] = 26;
        // enchant rates (value / 1000) of the kind 0 values: 0.1 of 5 rounds to 0, 1.0 of 10 is 10
        weapon.Enchant.Options[0x1b] = 1000; weapon.Enchant.Options[0x17] = 100;
        var full = Status(Tip(weapon));
        TestContext.WriteLine("full: " + full.Replace("\n", "\\n"));
        Assert.That(full, Is.EqualTo(
            "<font color='#ffffff'>Basic Attributes\n  • " + V(0x17, 5) + "\n</font>" + space +
            // str 18 and finalAdditionalDamage 26 are the last value of their variation rows (1010153, 1010008)
            "<font color='#91a8ff'>Bonus Attributes\n  • " + V(0x11, 8) + "\n  <font color='#d659ff'>• " + V(0, 18) + " (Max)</font>\n" +
            "  • " + V(1, 12) + "\n  <font color='#d659ff'>• " + Sp(26) + " (Max)</font>\n</font>" + space +
            "<font color='#ffd200'>Enchantment Effects\n  • " + Ms2.Quest.ClientFormat.Format(t.Common("s_word_stat_increase_near_zero"), S(0x17)) + "\n  • " + V(0x1b, 10) + "\n</font>"));
        // level 10 is below its enchant limit: no gauge
        Assert.That(Tip(weapon).Data.ContainsKey("itemEnchantExpString"), Is.False);
        // the compact block: 15000001 has no socketDataId (the Lua max count, STILL-OPEN), so no member
        Assert.That(Status(Tip(weapon, 0x800)), Is.Null);

        // a skin with socketDataId 1002 (max 2): 2 options less its own stat 0x14, no random option; flag 0x4000 hides the sockets
        var skin = new EquipItem { Id = 11300379, Uid = 13, Rarity = 1, Amount = 1 };
        skin.Options.Stat[0][0x14] = 5; skin.Options.Special[0][7] = 10;
        Assert.That(Status(Tip(skin, 0x800)), Is.EqualTo("<font color='#ffffff'>Random Attributes: 1</font>\n<font color='#ffffff'>+2 Sockets</font>"));
        Assert.That(Status(Tip(skin, 0x4800)), Is.EqualTo("<font color='#ffffff'>Random Attributes: 1</font>"));
        Assert.That(Status(Tip(skin, 0x10800)), Is.EqualTo("<font color='#ffffff'>Basic Attributes\n  • " + Sp(10) + "\n</font>"),
            "0x10000 takes the option block");

        // the unlimited block: this data's English s_tooltip_itemoption_unlimited is a placeholder without fields
        var limit = new EquipItem { Id = 15000001, Uid = 11, Rarity = 2, Amount = 1 };
        limit.Options.Stat[0][0x1c] = 20;
        limit.LimitBreak.Level = 2; limit.LimitBreak.Stat[0x1c] = 500; limit.LimitBreak.Stat[0x17] = 30; limit.LimitBreak.Special[5] = 15;
        Assert.That(Status(Tip(limit)), Is.EqualTo(t.Common("s_tooltip_itemoption_unlimited")));

        // 15000307 (rank 4) can be enchanted: +3 with 5000 charge, step 5000 (the version 1 table): 1 of 2, 50%
        var gauge = new EquipItem { Id = 15000307, Uid = 12, Rarity = 4, Amount = 1 };
        gauge.Enchant.Level = 3; gauge.Enchant.Charge = 5000; gauge.Enchant.Version = 1;
        var g = Tip(gauge);
        Assert.That(Status(g), Is.EqualTo("<font color='#ffd200'>Enchantment Experience\n</font>gauge:\n\n"));
        Assert.That(g.Data["itemEnchantExpString"], Is.EqualTo("1 / 2"));
        Assert.That(g.Data["itemEnchantExpPercent"], Is.EqualTo(50));
        gauge.Enchant.Version = 0;                          // NeedsChargeVersionUpdate
        Assert.That(Status(Tip(gauge)), Is.EqualTo("<font color='#ffd200'>Enchantment Experience Reset & Material Refund Available\n</font>gauge:\n\n"));
    }

    [Test]
    public void StatDescriptionIsTheConstantStat()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        var t = m.Tables;
        string Desc(EquipItem r, int flags = 0x80)
        {
            var tip = new ItemTooltip(m, flags);
            tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, t.Items));
            return tip.Data.TryGetValue("statDescription", out var v)
                ? string.Join(",", ((Dictionary<string, object>)v).Select(p => p.Key + ":" + p.Value)) : null;
        }
        EquipItem Rec(int id) => new() { Id = id, Uid = 1, Rarity = 1, Amount = 1 };

        // a weapon (dagger, equip subtype 0x1f): Min Attack's text, min 3 and max 5 plus the ratio enchant ScaleValue(5, 0.5)
        var dagger = Rec(13100001);
        dagger.Options.Stat[0][0x1b] = 3; dagger.Options.Stat[0][0x1c] = 5; dagger.Enchant.Options[0x1c] = 500;
        dagger.Options.Stat[1][0x1b] = 40;                  // group 1 is not in GetStatValue(stat, 3, 0)
        string want = "type:2,title:" + t.StatName(0x1b) + ",extraup:0,min:3,max:7";
        Assert.That(Desc(dagger), Is.EqualTo(want));
        Assert.That(Desc(dagger, 0x800), Is.EqualTo(want));

        // a suit (equip subtype 0x16): defense, min and max the same; no value, no member
        var robe = Rec(12200036);
        Assert.That(Desc(robe), Is.Null);
        robe.Options.Stat[0][0x14] = 5;
        Assert.That(Desc(robe), Is.EqualTo("type:2,title:" + t.StatName(0x14) + ",extraup:0,min:5,max:5"));
        // a top without a constantID: nothing
        var top = Rec(11400021);
        top.Options.Stat[0][0x14] = 5;
        Assert.That(Desc(top), Is.Null);

        // a gemstone: the first special (a rate), then the first stat replaces it; stat 0x11 and 0 share bin 0, the newest first
        var gem = Rec(40200001);
        gem.Options.Special[0][7] = 26;
        Assert.That(Desc(gem), Is.EqualTo("type:2,title:" + t.SpecialAbilityName(7) + ",extraup:0,min:2.6%,max:2.6%"));
        gem.Options.Stat[0][0x11] = 8; gem.Options.Stat[0][0] = 18;
        Assert.That(Desc(gem), Is.EqualTo("type:2,title:" + t.StatName(0) + ",extraup:0,min:18,max:18"));
    }

    [Test]
    public void ItemCompareIsTheStatChange()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        var t = m.Tables;
        string S(int stat) => t.StatName(stat);
        EquipItem Rec(int id, long uid, EquipSlot slot, int rarity = 1) => new() { Id = id, Uid = uid, Slot = slot, Amount = 1, Rarity = rarity };
        // worn: dagger 13100001 RH (equip subtype 0x1f), 13400001 LH (0x22), top 11400021 CL
        var rh = Rec(13100001, 1, EquipSlot.RH);
        rh.Options.Stat[0][0x17] = 40; rh.Options.Stat[0][0x1b] = 4; rh.Options.Stat[0][0x1c] = 6;
        var lh = Rec(13400001, 2, EquipSlot.LH);
        lh.Options.Stat[0][0x17] = 5;
        var top = Rec(11400021, 3, EquipSlot.CL);
        var equipment = WornEquipment.Of(new PcInfo { Equips = new[] { rh, lh, top } }, t.Items);
        bool shift = false;
        ItemTooltip Tip(EquipItem r, int flags = 0x80, WornEquipment eq = null)
        {
            var tip = new ItemTooltip(m, flags) { OwnerEquipment = eq ?? equipment, ShiftHeld = () => shift };
            tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, t.Items));
            return tip;
        }
        List<string> Rows(ItemTooltip tip) => tip.Data.TryGetValue("itemCompare", out var v)
            ? ((IEnumerable<object>)v).Cast<Dictionary<string, object>>().Select(r =>
                r["statName"] + " " + (r.TryGetValue("statRate", out var x) ? "rate " + x : "value " + r["statValue"]) + ((bool)r["up"] ? " up" : " down")).ToList()
            : null;

        // the bag dagger: rarity 2, max attack 20 with a ratio enchant of 500 (ScaleValue(20, 0.5) = 10), min attack 10,
        // physical attack 30, perfect guard (a rate stat) 25 in group 2, special 7 (a rate) 26, and gem 40200001 in its
        // socket (constant rank: int 2)
        var bag = Rec(13100001, 10, EquipSlot.RH, 2);
        bag.Options.Stat[0][0x1b] = 10; bag.Options.Stat[0][0x1c] = 20; bag.Enchant.Options[0x1c] = 500;
        bag.Options.Stat[0][0x17] = 30; bag.Options.Stat[2][0x15] = 25; bag.Options.Special[0][7] = 26;
        bag.Socket.MaxSlot = 1; bag.Socket.Gems.Add(new SocketGem { GemId = 40200001 });
        var d = t.Items.Get(13100001);
        int score = ItemLevels.Base(d, 2) - ItemLevels.Base(d, 1);
        Assert.That(score, Is.Not.Zero);
        var rows = Rows(Tip(bag));
        TestContext.WriteLine(string.Join(" | ", rows));
        // max attack: (30 - 6 + 10 - 4) / 2 = 15, min attack left out; the stat order, then the specials, then the level
        Assert.That(rows, Is.EqualTo(new[]
        {
            S(0x1c) + " value 15 up", S(2) + " value 2 up", S(0x17) + " value 10 down", S(0x15) + " rate 2.5% up",
            t.SpecialAbilityName(7) + " rate 2.6% up", t.Common("s_common_item_level") + " rate " + score + " up",
        }));

        // Shift held: against the LH item (physical attack 5)
        shift = true;
        Assert.That(Rows(Tip(bag))[2], Is.EqualTo(S(0x17) + " value 25 up"));
        shift = false;

        // a two-handed weapon replaces both hands: physical attack 0 - (40 + 5)
        Assert.That(Rows(Tip(Rec(15000001, 11, EquipSlot.RH))), Does.Contain(S(0x17) + " value 45 down"));

        // the worn item itself: the array is there but empty
        Assert.That(Rows(Tip(rh)), Is.Empty);
        // nothing worn in the hands: the item's own stats, max attack (30 + 10) / 2
        var empty = WornEquipment.Of(new PcInfo { Equips = Array.Empty<EquipItem>() }, t.Items);
        Assert.That(Rows(Tip(bag, eq: empty))[0], Is.EqualTo(S(0x1c) + " value 20 up"));

        // no owner player, flag 0x800 or a non-equip item: no member
        var none = new ItemTooltip(m, 0x80);
        none.Fill(Ms2.Ui.Hud.InventoryController.ToItem(bag, t.Items));
        Assert.That(Rows(none), Is.Null);
        Assert.That(Rows(Tip(bag, 0x800)), Is.Null);
        Assert.That(Rows(Tip(Rec(40200001, 12, EquipSlot.Unknown))), Is.Null);
    }

    [Test]
    public void ExtraDescriptionIsTheClientsRows()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        m.Player.Level = 1; m.Player.JobGroup = 10;
        var t = m.Tables;
        ItemTooltip Tip(EquipItem r) { var tip = new ItemTooltip(m, 0x200); tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, t.Items)); return tip; }

        var weapon = new EquipItem { Id = 15000001, Uid = 10, Rarity = 2, Amount = 1 };
        var wd = t.Items.Get(15000001);
        var rows = RowTexts(Tip(weapon).Data);
        TestContext.WriteLine($"15000001 (levelLimit {wd.LevelLimit}, jobs {string.Join(",", wd.JobLimit)}): " + string.Join(" | ", rows));
        Assert.That(rows, Is.EqualTo(new[] { "<FONT color='#FFFFFF'>Gear Score 105</FONT> #aaaaaa", "Requires Level 10 #ff4040",
            "Rare Two-handed Greatsword #aaaaaa" }), "the rank's base level; level 1 is below the limit");
        m.Player.Level = 10;
        Assert.That(RowTexts(Tip(weapon).Data)[1], Is.EqualTo("Requires Level 10 #aaaaaa"));

        // +10 adds nothing below gear score 70 and data rank 4 (FUN_14053f390); 15000307 (gear score 70, rank 4) adds it
        var low = Ms2.Ui.Hud.InventoryController.ToItem(weapon, t.Items);
        low.EnchantLevel = 10;
        Assert.That(ItemLevels.Of(low), Is.EqualTo(((int, int)?)(105, 0)));
        var item = Ms2.Ui.Hud.InventoryController.ToItem(new EquipItem { Id = 15000307, Uid = 13, Rarity = 4, Amount = 1 }, t.Items);
        item.EnchantLevel = 10;
        var (lvl, add) = ItemLevels.Of(item).Value;
        TestContext.WriteLine($"+10: level {lvl} additional {add}");
        Assert.That(add, Is.GreaterThan(0));
        var enchanted = new ItemTooltip(m, 0x200);
        enchanted.Fill(item);
        Assert.That(RowTexts(enchanted.Data)[0], Does.StartWith($"<FONT color='#FFFFFF'>Gear Score {lvl - add}").And.Contain(add.ToString()));

        // a gemstone: its rank and kind, then the part it fits (EquipPart 3: accessory)
        var gem = RowTexts(Tip(new EquipItem { Id = 40200001, Uid = 11, Rarity = 3, Amount = 1 }).Data);
        TestContext.WriteLine("40200001: " + string.Join(" | ", gem));
        Assert.That(gem, Is.EqualTo(new[] { "Exceptional Gemstone #aaaaaa", "Accessory #aaaaaa" }));

        // a lapenshard is named by its color (EquipPart 1: red)
        var lapen = RowTexts(Tip(new EquipItem { Id = 41000011, Uid = 12, Rarity = 4, Amount = 1 }).Data);
        TestContext.WriteLine("41000011: " + string.Join(" | ", lapen));
        Assert.That(lapen, Is.EqualTo(new[] { "Requires Level 60 #ff4040", "Epic Red Lapenshard #aaaaaa" }));

        // jobLimit 10,20,90: the names this server offers, gray for a listed job (a beginner counts as a Knight), else red
        var top = new EquipItem { Id = 11400003, Uid = 14, Rarity = 1, Amount = 1 };
        var jobs = RowTexts(Tip(top).Data);
        TestContext.WriteLine("11400003: " + string.Join(" | ", jobs));
        string names = string.Join(", ", new[] { 10, 20, 90 }.Where(j => j != 90 || m.IsFeatureEnabled(8)).Select(t.JobGroupName));
        Assert.That(jobs[^1], Is.EqualTo(Ms2.Quest.ClientFormat.Format(t.Common("s_word_enable_jobs"), names) + " #aaaaaa"));
        Assert.That(jobs, Is.EqualTo(new[] { "<FONT color='#FFFFFF'>Gear Score 32</FONT> #aaaaaa", "Requires Level 10 #aaaaaa", "Normal Top #aaaaaa",
            "Job: Knight, Berserker, Runeblade #aaaaaa" }));
        var drawn = TooltipTests.Draw(Tip(top).Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", drawn));
        Assert.That(drawn, Has.Some.Contains("Gear Score 32"));
        Assert.That(drawn, Has.Some.Contains("Job: Knight, Berserker, Runeblade"));
        m.Player.JobGroup = 1;
        Assert.That(RowTexts(Tip(top).Data)[^1], Does.EndWith("#aaaaaa"));
        m.Player.JobGroup = 30;
        Assert.That(RowTexts(Tip(top).Data)[^1], Does.EndWith("#ff4040"));

        // a VIP item's row color needs PC::IsVip: not ported, so the member is left out
        Assert.That(t.Items.Get(15000001).Vip, Is.False);

        // no local player: the client returns first
        m.HasLocalPlayer = false;
        Assert.That(Tip(weapon).Data.ContainsKey("extraDescription"), Is.False);
    }

    [Test]
    public void ExtraDescriptionOfAnInstallItemHasItsPlaceAndHousingCategory()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        var t = m.Tables;
        // FUN_1409de630 by install.space (common 0x58e indoor, 0x58f outdoor, 0x590 both), then FUN_1409dea00:
        // FormatStringId(0x6f8, string table 50 by categoryIndex); the first item by id of each place kind
        var seen = new HashSet<int>();
        foreach (var row in t.Housing.Rows.OrderBy(r => r.ItemId))
        {
            var d = t.Items.Get(row.ItemId);
            if (d == null || !d.IsInstallItem || d.Vip || d.Wedding) continue;
            int place = (d.InstallSpace & 1) == 0 ? 0x58e : (d.InstallSpace & 6) != 0 ? 0x590 : 0x58f;
            if (!seen.Add(place)) continue;
            var tip = new ItemTooltip(m, 0);
            Assert.That(tip.FillById(d.Id, 1), Is.True);
            var rows = RowTexts(tip.Data);
            TestContext.WriteLine($"{d.Id} (space {d.InstallSpace}, category {row.CategoryIndex}): " + string.Join(" | ", rows));
            string category = t.UgcCubeCategoryName(row.CategoryIndex);
            Assert.That(category, Is.Not.Empty, "category " + row.CategoryIndex);
            Assert.That(rows[^2], Is.EqualTo(t.Common(place) + " #aaaaaa"));
            Assert.That(rows[^1], Is.EqualTo(Ms2.Quest.ClientFormat.Format(t.Common(0x6f8), category) + " #aaaaaa"));
            Assert.That(rows, Has.Count.GreaterThanOrEqualTo(3), "the name row (rank and kind) comes first");
        }
        TestContext.WriteLine("place kinds: " + string.Join(", ", seen.Select(p => "0x" + p.ToString("x"))));
        Assert.That(seen, Is.Not.Empty);
        Assert.That(t.Common(0x58e), Is.Not.Empty);
        Assert.That(t.UgcCubeCategoryName(0), Is.Empty);
    }

    [Test]
    public void GuideDescriptionByIdHasAnInstallItemsRightsHousingLevelAndTrophy()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        m.FeatureEnabledByName = null;                  // the features come from m.Features below
        var t = m.Tables;
        string Guide(int id)
        {
            var tip = new ItemTooltip(m, 0);
            Assert.That(tip.FillById(id, 1), Is.True);
            return tip.Data.TryGetValue("itemGuideDescription", out var v) ? (string)v : null;
        }
        string F(int common, object arg) => Ms2.Quest.ClientFormat.Format(t.Common(common), arg);
        string Rights(ItemData d, string text)
        {
            var (state, trigger) = t.ObjectCubeRights(d.InstallObjCode);
            if (state) text = F(0xafa, text);
            if (trigger) text = F(0xaf9, text);
            return text;
        }

        // the rights (FormatStringId 0xafa, then 0xaf9) with every trophy cleared and feature 0xdb off: nothing appended
        m.IsAchieveGradeCleared = (_, _) => true;
        m.RequireAchieveText = (id, grade) => $"trophy {id}/{grade}";
        int both = 0, rights = 0;
        foreach (var row in t.Housing.Rows.OrderBy(r => r.ItemId))
        {
            var d = t.Items.Get(row.ItemId);
            if (d == null || d.InstallObjCode == 0) continue;
            var r = t.ObjectCubeRights(d.InstallObjCode);
            if (!r.StateChange && !r.TriggerControl) continue;
            if (rights++ < 3 || (r.StateChange && r.TriggerControl && both++ == 0))
            {
                string guide = Guide(d.Id);
                TestContext.WriteLine($"{d.Id} (object {d.InstallObjCode}, {r}): {guide}");
                Assert.That(guide, Is.EqualTo(Rights(d, t.ItemDescription(d.Id, "guideDescription"))));
            }
        }
        TestContext.WriteLine($"items with rights: {rights}, with both: {both}");
        Assert.That(rights, Is.GreaterThan(0));
        Assert.That(t.ObjectCubeRights(0), Is.EqualTo((false, false)));

        // feature 0xdb, an interiorLevel above 0 and priceCount 1: BlockFormat(0x6fa, the level raised to the category's
        // interior requireLevel) above the own housing grade (0 with no housing state)
        var leveled = t.Housing.Rows.OrderBy(r => r.ItemId).First(r => r.InteriorLevel > 1 && r.MoneyType == 1
            && t.Items.Get(r.ItemId) is { IsInstallItem: true } d && t.ObjectCubeRights(d.InstallObjCode) == (false, false));
        var ld = t.Items.Get(leveled.ItemId);
        string plain = t.ItemDescription(ld.Id, "guideDescription");
        Assert.That(Guide(ld.Id), Is.EqualTo(plain));
        m.Features.Add(0xdb);
        int level = Math.Max(leveled.InteriorLevel, t.CubeInteriors.RequireLevel(leveled.CategoryIndex) ?? 0);
        string line = F(0x6fa, level);
        TestContext.WriteLine($"{ld.Id} (interiorLevel {leveled.InteriorLevel}, category {leveled.CategoryIndex}): {Guide(ld.Id)}");
        Assert.That(Guide(ld.Id), Is.EqualTo(string.IsNullOrEmpty(plain) ? line : plain + "\n" + line));
        m.HousingGrade = () => level;
        Assert.That(Guide(ld.Id), Is.EqualTo(plain), "a level at the own grade adds nothing");
        m.HousingGrade = () => level - 1;
        Assert.That(Guide(ld.Id), Does.EndWith(line));
        m.Features.Remove(0xdb);

        // a trophy grade not cleared: s_construct_require_trophy with GetRequireAchieveText, when that is not empty
        var trophied = t.Housing.Rows.OrderBy(r => r.ItemId).First(r => r.TrophyId != 0
            && t.Items.Get(r.ItemId) is { IsInstallItem: true } d && t.ObjectCubeRights(d.InstallObjCode) == (false, false));
        var td = t.Items.Get(trophied.ItemId);
        string tplain = t.ItemDescription(td.Id, "guideDescription");
        Assert.That(Guide(td.Id), Is.EqualTo(tplain));
        m.IsAchieveGradeCleared = (id, grade) => id != (int)trophied.TrophyId || grade != (int)trophied.TrophyLevel;
        string tline = Ms2.Quest.ClientFormat.Format(t.Common("s_construct_require_trophy"), $"trophy {trophied.TrophyId}/{trophied.TrophyLevel}");
        TestContext.WriteLine($"{td.Id} (trophy {trophied.TrophyId}/{trophied.TrophyLevel}): {Guide(td.Id)}");
        Assert.That(Guide(td.Id), Is.EqualTo(string.IsNullOrEmpty(tplain) ? tline : tplain + "\n" + tline));
        m.RequireAchieveText = (_, _) => "";
        Assert.That(Guide(td.Id), Is.EqualTo(tplain), "an empty requirement text adds nothing");
        m.RequireAchieveText = null;
        m.IsAchieveGradeCleared = null;
        Assert.That(Guide(td.Id), Is.EqualTo(tplain));
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
    public void CreateTimeLinesAreTheClients()
    {
        var m = NewModel();
        AsTooltip Tip(int id, UgcLook look)
        {
            var r = new EquipItem { Id = id, Uid = 9, Rarity = 1, Amount = 1, HasUgc = look != null, Ugc = look };
            var tip = new ItemTooltip(m, 0x200);
            tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items));
            return tip.Data;
        }
        // 50100189 has <ucc mesh="ugc_cube_A01">: the author and creation time, and the item's name
        var data = Tip(50100189, new UgcLook { Uid = 77, Author = "Maker", CreationTime = 1700000000 });
        var t = Ms2.Net.MsTime.LocalTm(1700000000);
        Assert.That(data["itemCreateTime"], Is.EqualTo($"Created by Maker ({t.Month}/{t.Day}/{t.Year} - {t.Hour:00}:{t.Minute:00}:{t.Second:00})"));
        Assert.That(data["designOrgItemName"], Is.EqualTo(m.Tables.ItemName(50100189)));
        // no UGC id yet: the re-register hint
        Assert.That(Tip(50100189, new UgcLook())["itemCreateTime"], Is.EqualTo(m.Tables.Common("s_ugc_upload_must_profile")));
        // no look, or an item that is neither: no member
        Assert.That(Tip(50100189, null).ContainsKey("itemCreateTime"), Is.False);
        Assert.That(Tip(12200398, new UgcLook { Uid = 77 }).ContainsKey("itemCreateTime"), Is.False);
    }

    [Test]
    public void GemstoneUpgradeCommentIsTheClients()
    {
        var m = NewModel();
        string Comment(int id)
        {
            var tip = new ItemTooltip(m, 0x80);
            tip.Fill(Item.Create(m.Tables.Items, id, 4));
            return tip.Data.TryGetValue("extraComment", out var v) ? (string)v : null;
        }
        // lv10_tooltip_gemstone.png: Tier 3 Wisdom Gemstone (40200003, next tier 40200004)
        Assert.That(Comment(40200003), Is.EqualTo("<FONT color='#FFD200'>Upgrade Available</FONT>\n" +
            "<FONT color='#909090'>Upgrade Materials: Blue Gem Dust, Cyan Gem Dust, Orange Gem Dust, Crystal Fragment</FONT>"));
        // the last tier (no NextItemID) and a non-gemstone have none
        Assert.That(Comment(40200010), Is.Null);
        Assert.That(Comment(12200398), Is.Null);
        var drawnTip = new ItemTooltip(m, 0x80);
        drawnTip.Fill(Item.Create(m.Tables.Items, 40200003, 4));
        var shown = TooltipTests.Draw(drawnTip.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Upgrade Materials: Blue Gem Dust"));
    }

    [Test]
    public void MasteryRecipeCommentIsTheClients()
    {
        var m = NewModel();
        string Comment(int id, int have)
        {
            var tip = new ItemTooltip(m, 0x80) { OwnedCount = i => i == id ? have : 0 };
            tip.Fill(Item.Create(m.Tables.Items, id, 1));
            return tip.Data.TryGetValue("extraComment", out var v) ? (string)v : null;
        }
        // 33000014 (type 0x10) is an ingredient of eight recipes, in file order; the count turns red below each need
        Assert.That(m.Tables.Items.Get(33000014).Type, Is.EqualTo(0x10));
        var lines = Comment(33000014, 30).Split('\n');
        Assert.That(lines, Has.Length.EqualTo(8));
        Assert.That(lines[0], Is.EqualTo("<FONT color='#FFD200'>[Smithing]</FONT> Used to craft Opal Smithing Slate (30/2)"));
        Assert.That(lines[1], Is.EqualTo("<FONT color='#FFD200'>[Smithing]</FONT> Used to craft Tin Whetstone (30/30)"));
        Assert.That(lines[3], Is.EqualTo("<FONT color='#FFD200'>[Smithing]</FONT> Used to craft Tier 1 Gemstone Box (<FONT color='#FF5F5F' >30</FONT>/160)"));
        Assert.That(lines[4], Does.StartWith("<FONT color='#FFD200'>[Handicrafts]</FONT> Used to craft Snail Shell Handicraft Slate ("));
        Assert.That(Comment(33000014, 0).Split('\n')[0], Does.EndWith("(<FONT color='#FF5F5F' >0</FONT>/2)"));
        // a type 0x10 item no recipe uses, and a non-ingredient, have none
        Assert.That(Comment(12200398, 0), Is.Null);
        var drawnTip = new ItemTooltip(m, 0x80);
        drawnTip.Fill(Item.Create(m.Tables.Items, 33000014, 1));
        var shown = TooltipTests.Draw(drawnTip.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Used to craft Opal Smithing Slate"));
    }

    [Test]
    public void MasteryRecipeTableIsTheClients()
    {
        // FUN_14026b9d0: three ',' parts, the id and the tag around ':', the third part the count; id 0 or another part
        // count adds none
        Assert.That(ItemTags.ByName("GemstoneA10"), Is.Not.Zero);
        Assert.That(MasteryRecipeTable.ParseIngredient("40201610:GemstoneA10,4,1"),
            Is.EqualTo(new MasteryRecipeTable.Ingredient(40201610, 1, ItemTags.ByName("GemstoneA10"))));
        Assert.That(MasteryRecipeTable.ParseIngredient("33000014,,2,5"), Is.EqualTo(new MasteryRecipeTable.Ingredient(33000014, 5)), "Tokenize skips empty tokens");
        Assert.That(MasteryRecipeTable.ParseIngredient("33000014,0"), Is.Null);
        Assert.That(MasteryRecipeTable.ParseIngredient("0,0,3"), Is.Null);
        Assert.That(MasteryRecipeTable.ParseIngredient(""), Is.Null);
        // a later row of an id replaces the recipe; the ingredient lists keep every loaded row
        var t = new MasteryRecipeTable(System.Xml.Linq.XDocument.Parse(
            "<ms2><receipe id='1' masteryType='3' requireItem1='5,0,2' requireItem2='6,0,1' />" +
            "<receipe id='2' masteryType='7' requireItem3='5,0,4' />" +
            "<receipe id='1' masteryType='9' requireItem1='5,0,8' /></ms2>"));
        Assert.That(t.Find(1).Type, Is.EqualTo(9));
        Assert.That(t.IngredientRecipes(5), Is.EqualTo(new[] { (1, 2), (2, 4), (1, 8) }));
        Assert.That(t.IngredientRecipes(6), Is.EqualTo(new[] { (1, 1) }));
        Assert.That(t.IngredientRecipes(7), Is.Null);
        Assert.That((MasteryRecipeTable.TypeKey(3), MasteryRecipeTable.TypeKey(11), MasteryRecipeTable.TypeKey(2)),
            Is.EqualTo(("s_mastery_mining", "s_mastery_pettaming", (string)null)));
    }

    [Test]
    public void MasteryGradesAndOddsAreTheClients()
    {
        // Grade (:355): one below the first grade above the value, else the last grade
        var t = new MasteryRecipeTable(System.Xml.Linq.XDocument.Parse("<ms2 />"), System.Xml.Linq.XDocument.Parse(
            "<ms2><mastery type='3'><v grade='1' value='0' /><v grade='2' value='100' /><v grade='3' value='300' /></mastery></ms2>"));
        Assert.That((t.Grade(3, 0), t.Grade(3, 99), t.Grade(3, 100), t.Grade(3, 5000)), Is.EqualTo((1, 1, 2, 3)));
        Assert.That((t.MaxGrade(3), t.MaxGrade(4), t.Grade(12, 0)), Is.EqualTo((3, 0, 0)));
        Assert.That((MasteryRecipeTable.MakeID(3, 2), MasteryRecipeTable.TypeFromID(3002), MasteryRecipeTable.GradeFromID(3002)),
            Is.EqualTo((3002, 3, 2)));
        // the Lua success odds (high 10, normal 100: the last count is 60)
        Assert.That(MasteryRecipeTable.LuaGatheringSuccessProp(5, 10, 100), Is.EqualTo(100));
        Assert.That(MasteryRecipeTable.LuaGatheringSuccessProp(10, 10, 100), Is.EqualTo(100));
        Assert.That(MasteryRecipeTable.LuaGatheringSuccessProp(20, 10, 100), Is.EqualTo(96.98179412008142).Within(1e-9));
        Assert.That(MasteryRecipeTable.LuaGatheringSuccessProp(59, 10, 100), Is.EqualTo(27.53287682315494).Within(1e-9));
        Assert.That(MasteryRecipeTable.LuaGatheringSuccessProp(60, 10, 100), Is.EqualTo(0));
    }

    [Test]
    public void OwnedCountIsTheClients()
    {
        var m = NewModel();
        EquipItem E(int id, int amount, long expiry = 0) => new() { Id = id, Uid = id + amount, Amount = amount, ExpiryTime = expiry };
        var w = new Ms2.Ui.Skills.SlotWorld
        {
            ItemData = m.Tables.Items.Get, TabOf = id => id == 33000014 ? 3 : 4,
            Inventory = new[] { E(33000014, 7), E(33000014, 5), E(33000014, 9, 1), E(33000117, 4) },
        };
        // FUN_140a5f180(id, 0): the usable items of the id in its tab (an expired one is not usable)
        Assert.That(w.CountById(33000014), Is.EqualTo(12));
        Assert.That(w.CountById(0), Is.EqualTo(0));
        w.TabOf = _ => 15;
        Assert.That(w.CountById(33000014), Is.EqualTo(0), "no tab");
        w.TabOf = _ => 3;
        w.HasPc = false;
        Assert.That(w.CountById(33000014), Is.EqualTo(0), "no local player");
    }

    [Test]
    public void ModelSlotTypesAreTheClients()
    {
        var models = new ItemModelTable(Packs());
        Assert.That(models.Get(11400001).SlotTypes, Is.EqualTo(new[] { 8 }), "CL");
        var wings = models.Get(15300000);
        Assert.That(wings.SlotTypes, Is.EqualTo(new[] { 5, 4 }), "RH, then LH");
        Assert.That((wings.FirstSlotType, wings.HasSlotType(4), wings.HasSlotType(8)), Is.EqualTo((5, true, false)));
        Assert.That((ItemModelTable.SlotType("OH"), ItemModelTable.SlotType("oh"), ItemModelTable.SlotType("")), Is.EqualTo((19, 0, 0)));
        var oh = new ItemModelTable.Model(false, 0, new List<int>(), new[] { 19, 8 });
        Assert.That((oh.HasSlotType(5), oh.HasSlotType(4), oh.HasSlotType(8)), Is.EqualTo((true, true, false)), "OH answers first");
    }

    [Test]
    public void PreviewLineIsTheClients()
    {
        var m = NewModel();
        m.Keys = new KeyTable(Packs(), "en");
        m.Keys.LoadDefaultKeys();
        var hood = Item.Create(m.Tables.Items, 11400021, 1);
        var tip = new ItemTooltip(m, 0x200);
        tip.Fill(hood);
        tip.SetEnablePreview(hood);
        // outfits_hover_equipped_top.png: "Hold the [Shift] key and right-click the item to preview it."
        var shown = TooltipTests.Draw(tip.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Hold the [Shift] key and right-click the item to preview it."));
        // an expired item gets none
        var old = Item.Create(m.Tables.Items, 11400021, 1);
        old.ExpiryTime = 1;
        var none = new ItemTooltip(m, 0x200);
        none.Fill(old);
        none.SetEnablePreview(old);
        Assert.That(none.Data.ContainsKey("enablePreview"), Is.False);
    }

    [Test]
    public void UseHintsAreTheClients()
    {
        var m = NewModel();
        ItemTooltip Tip(int id, int flags, bool worn = false)
        {
            var tip = new ItemTooltip(m, flags) { OwnerWearsUid = _ => worn };
            tip.Fill(Item.Create(m.Tables.Items, id, 1));
            return tip;
        }
        // lv10_tooltip_consumable_red_herb.png: "Use by double-clicking your mouse."
        var herb = Tip(20000028, 0x80);
        Assert.That(herb.Data["enableUseDbClick"], Is.EqualTo(m.Tables.Common("s_msg_dclick_usable")));
        Assert.That(herb.Data.ContainsKey("enablePuton"), Is.False);
        var shown = TooltipTests.Draw(herb.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("double-clicking"));
        // lv10_tooltip_lifeskill_slot2.png: Iron Ore has neither line
        var ore = Tip(33000005, 0x80);
        Assert.That(ore.Data.ContainsKey("enableUseDbClick") || ore.Data.ContainsKey("enablePuton"), Is.False);
        // an equipment item: the default put-on line; worn by the owner: itemEquip only
        var hood = Tip(11400021, 0x80);
        Assert.That(hood.Data["enablePuton"], Is.EqualTo(m.Tables.Common("s_msg_rclick_puton_equip")));
        var worn = Tip(11400021, 0x80, worn: true);
        Assert.That(worn.Data["itemEquip"], Is.EqualTo(m.Tables.Common("s_word_equiped")));
        Assert.That(worn.Data.ContainsKey("enablePuton"), Is.False);
        // without flag 0x80 / 4, or without an owner player, none of it
        Assert.That(Tip(20000028, 0x200).Data.ContainsKey("enableUseDbClick"), Is.False);
        var noOwner = new ItemTooltip(m, 0x80);
        noOwner.Fill(Item.Create(m.Tables.Items, 20000028, 1));
        Assert.That(noOwner.Data.ContainsKey("enableUseDbClick"), Is.False);
    }

    [Test]
    public void StatDescriptionIsTheMainDescription()
    {
        var m = NewModel();
        var tip = new ItemTooltip(m, 0x80);
        tip.Fill(Item.Create(m.Tables.Items, 20000028, 1));
        // lv10_tooltip_consumable_red_herb.png: "28 Sec Duration" over "Restores 224 Health"
        var row = (Dictionary<string, object>) tip.Data["statDescription"];
        Assert.That(row["type"], Is.EqualTo(0));
        Assert.That(row["text"], Is.EqualTo("<font size='14'>28 Sec Duration</font>\n<font size='24'>Restores 224 Health</font>"));
        var shown = TooltipTests.Draw(tip.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Restores 224 Health"));
        // Iron Ore has no mainDescription: no row
        var ore = new ItemTooltip(m, 0x80);
        ore.Fill(Item.Create(m.Tables.Items, 33000005, 1));
        Assert.That(ore.Data.ContainsKey("statDescription"), Is.False);
    }

    [Test]
    public void SellPriceLineIsTheClients()
    {
        var m = NewModel();
        ItemTooltip Tip(int id, int rarity, int lockState = 0)
        {
            var item = Item.Create(m.Tables.Items, id, rarity);
            item.LockState = lockState;
            var tip = new ItemTooltip(m, 0x80);
            tip.Fill(item);
            return tip;
        }
        // lv10_tooltip_gemstone.png: "Shop Sales Price : 1" with the meso icon
        var gem = Tip(40200003, 4);
        Assert.That(gem.Data["itemSellPrice"], Is.EqualTo("Shop Sales Price : 1 <Img Src='export_common_icon_meso_h21' vspace='-6'/> "));
        var shown = TooltipTests.Draw(gem.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Shop Sales Price : 1"));
        // locked: not sellable; the Green Hood has no shopSell
        Assert.That(Tip(40200003, 4, lockState: 1).Data.ContainsKey("itemSellPrice"), Is.False);
        Assert.That(Tip(12200398, 1).Data.ContainsKey("itemSellPrice"), Is.False);
        // the amount is grouped; 12200001 has six prices, one per rarity (472 .. 1392)
        var top = Item.Create(m.Tables.Items, 12200001, 6);
        Assert.That(top.Rarity, Is.EqualTo(6));
        var topTip = new ItemTooltip(m, 0x80);
        topTip.Fill(top);
        Assert.That(topTip.Data["itemSellPrice"], Is.EqualTo("Shop Sales Price : 1,392 <Img Src='export_common_icon_meso_h21' vspace='-6'/> "));
    }

    [Test]
    public void PutOnAndOffChangeTheOwnEquipment()
    {
        var t = new GameTables(Packs(), "en");
        Func<int, int?> layer = t.Items.WornLayer;
        Assert.That((layer(15000001), layer(11400021), layer(12200398), layer(1)), Is.EqualTo(((int?)0, (int?)1, (int?)1, (int?)null)));
        var me = UiFixture.Character();
        int changes = 0;
        me.Changed += () => changes++;
        long[] Uids(IEnumerable<EquipItem> l) => l.Select(e => e.Uid).OrderBy(u => u).ToArray();

        // the Green Hood top (an outfit, layer 1) in CL replaces the outfit top 11400021; the sword stays
        var hood = new EquipItem { Id = 12200398, Uid = 3001, Slot = EquipSlot.CL, Rarity = 1, Amount = 1 };
        Assert.That(me.Apply(new ItemPutOn(1, 0, hood), layer), Is.True);
        Assert.That(Uids(me.Equips), Is.EqualTo(new long[] { 2001, 3001 }));
        // put-on kind 2 goes to the second outfit layer and leaves the first one
        var hood2 = new EquipItem { Id = 12200398, Uid = 3002, Slot = EquipSlot.CL, Rarity = 1, Amount = 1 };
        Assert.That(me.Apply(new ItemPutOn(1, 2, hood2), layer), Is.True);
        Assert.That((Uids(me.Equips), Uids(me.Outfit2)), Is.EqualTo((new long[] { 2001, 3001 }, new long[] { 3002 })));
        // another object, a kind above 2 and an unknown item are not applied
        Assert.That(me.Apply(new ItemPutOn(7, 0, hood), layer), Is.False);
        Assert.That(me.Apply(new ItemPutOn(1, 3, hood), layer), Is.False);
        Assert.That(me.Apply(new ItemPutOn(1, 0, new EquipItem { Id = 1, Uid = 3003, Slot = EquipSlot.CL }), layer), Is.False);
        // put-off by uid, from either list
        Assert.That(me.Apply(new ItemPutOff(1, 3002), layer), Is.True);
        Assert.That(me.Apply(new ItemPutOff(1, 2001), layer), Is.True);
        Assert.That((Uids(me.Equips), Uids(me.Outfit2)), Is.EqualTo((new long[] { 3001 }, Array.Empty<long>())));
        Assert.That(me.Apply(new ItemPutOff(1, 9999), layer), Is.False);
        Assert.That(changes, Is.EqualTo(4));
        // the character window's record has the new list
        Assert.That(Uids(me.ToPcInfo(0).Equips), Is.EqualTo(new long[] { 3001 }));
        // a gear item (layer 0) goes before the outfits, so the look built in list order shows the outfit on top
        Assert.That(me.Apply(new ItemPutOn(1, 0, new EquipItem { Id = 15000001, Uid = 2001, Slot = EquipSlot.RH, Rarity = 1, Amount = 1 }), layer), Is.True);
        Assert.That(me.Equips.Select(e => e.Uid), Is.EqualTo(new long[] { 2001, 3001 }));
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

    [Test]
    public void ARightClickOnAWornItemSendsTheUnequipOnly()
    {
        Packs();
        var book = new Ms2.Net.Packets.ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 2 };
        var view = new Ms2.Net.Packets.InventoryView(book);
        var sent = new List<long>(); view.RequestUnequip = sent.Add;
        var notices = new List<string>(); var raised = new List<string>();
        var shown = new List<MessageBoxData>();
        var (h, c, _, errors) = Load(x => { x.Inventory = view; x.SystemNotice += notices.Add; x.Button += raised.Add; x.ShowBox = shown.Add; });
        Assert.That(errors, Is.Empty);
        long rh = c.Record().Equips.First(e => e.Id == 15000001).Uid;
        string Slot(int s) => MyInfoController.EquipBag + "." + s;

        var guide = new Ms2.Ui.Guide.GuideManager(new Ms2.Ui.Guide.GuideWorld());
        guide.AddLimit(Ms2.Ui.Guide.GuideLimit.ItemPutOff);
        Ms2.Ui.Guide.GuideManager.Current = guide;
        try { c.OnCallback("notifySlotUse", new object[] { Slot(5), 1 }); }
        finally { Ms2.Ui.Guide.GuideManager.Current = null; }
        Assert.That((sent.Count, notices.Single()), Is.EqualTo((0, Ms2.Ui.Guide.GuideManager.ItemPutOnOffNotice)),
            "the guide holds taking off: notice 2185, no send (inventory.cpp:1279)");
        notices.Clear();

        Assert.That(c.OnCallback("notifySlotUse", new object[] { Slot(5), 1 }), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { rh }), "the worn item's uid");
        Assert.That(c.Record().Equips.Any(e => e.Uid == rh), Is.True, "nothing is taken off until the server says so");
        c.OnCallback("notifySlotUse", new object[] { Slot(4), 1 });
        Assert.That(sent.Last(), Is.EqualTo(rh), "an empty LH holds the two-handed RH item (vfunc_25)");
        sent.Clear();
        c.OnCallback("notifySlotUse", new object[] { Slot(6), 1 });
        c.OnCallback("notifySlotUse", new object[] { Slot(5) });
        Assert.That((sent.Count, raised.Count), Is.EqualTo((0, 0)), "an empty slot or a double click (vfunc_13) does nothing");
        c.ActionKeyDown = a => a == 0x49406;
        c.OnCallback("notifySlotUse", new object[] { Slot(5), 1 });
        c.ActionKeyDown = _ => false;
        Assert.That((sent.Count, raised.Single()), Is.EqualTo((0, "notifySlotUse")), "the preview key: raised");
        c.LocalPlayerDead = () => true;
        c.OnCallback("notifySlotUse", new object[] { Slot(5), 1 });
        c.OnCallback("notifySlotUse", new object[] { Slot(6), 1 });
        Assert.That((sent.Count, shown.Count, shown.Last().Msg), Is.EqualTo((0, 1, c.Model.Tables.Common(MyInfoController.DeadKey))),
            "dead: box 66, no send (inventory.cpp:1282); an empty slot never reaches RequestUnequipItem");
        c.LocalPlayerDead = () => false;

        book.Apply(new Ms2.Net.Packets.ItemMessage.Add(new Ms2.Net.Packets.EquipItem { Id = 15000002, Uid = 900, InventorySlot = 0, Amount = 1 }));
        book.Apply(new Ms2.Net.Packets.ItemMessage.Add(new Ms2.Net.Packets.EquipItem { Id = 15000003, Uid = 901, InventorySlot = 1, Amount = 1 }));
        c.OnCallback("notifySlotUse", new object[] { Slot(5), 1 });
        Assert.That((sent.Count, notices.Single()), Is.EqualTo((0, MyInfoController.InventoryFullKey)), "no empty cell in the item's tab: notice 67");
        var w = c.WornSlot(Slot(4));
        Assert.That((w?.Item.Uid, w?.Slot, w?.Kind), Is.EqualTo(((long?)rh, (int?)5, (int?)0)), "LH shows the RH item: its own slot");
        Assert.That(c.WornSlot(Slot(6)).HasValue || c.WornSlot("InventoryDialog.0").HasValue, Is.False, "an empty slot or another name");
    }

    /// <summary>An inventory's bag slots, "InventoryDialog.N", over the records given (InvenSlot / Unk_Inventory).</summary>
    sealed class BagSlots : IInvenSlotOwner
    {
        public readonly Dictionary<string, Ms2.Net.Packets.EquipItem> Cells = new();
        public ItemDataTable Table;
        public readonly List<(long Uid, int Slot, int Layer)> Checked = new(), Equipped = new();
        public readonly List<string> Refreshed = new();
        public bool IsDragSlot(string name) => name.StartsWith("InventoryDialog.", StringComparison.Ordinal);
        public long? InvenSlotUid(string name) => IsDragSlot(name) ? (Cells.TryGetValue(name, out var r) ? r.Uid : 0) : null;
        public Item FindItem(long uid) => Cells.Values.FirstOrDefault(r => r.Uid == uid) is { } r ? InventoryController.ToItem(r, Table) : null;
        public bool CanEquipItem(Item item, int slot, int layer) { Checked.Add((item.Uid, slot, layer)); return true; }
        public void RequestEquipItem(Item item, int slot, int layer) => Equipped.Add((item.Uid, slot, layer));
        public void RefreshSlot(string name) => Refreshed.Add(name);
        public void SlotDropUnresolved(string name) { }
    }

    /// <summary>Another character window's equip slots, "MyPcInfoDialog.Equip.N", with the dragged items given.</summary>
    sealed class WornSlots : IEquipSlotOwner
    {
        public readonly Dictionary<string, Item> Worn = new();
        public readonly List<string> Refreshed = new();
        public bool IsDragSlot(string name) => IsEquipSlot(name);
        public bool IsEquipSlot(string name) => name.StartsWith(MyInfoController.EquipBag + ".", StringComparison.Ordinal);
        public (Item Item, int Slot, int Kind)? WornSlot(string name) => Worn.TryGetValue(name, out var i) ? (i, 5, 0) : null;
        public void RequestUnequip(Item item) { }
        public void RefreshSlot(string name) => Refreshed.Add(name);
        public void SlotDropUnresolved(string name) { }
    }

    [Test]
    public void ABagItemDroppedOnAnEquipSlotIsPutOnThere()
    {
        Packs();
        var shown = new List<MessageBoxData>();
        var (h, c, _, errors) = Load(x => x.ShowBox = shown.Add);
        Assert.That(errors, Is.Empty);
        var bag = new BagSlots { Table = c.Model.Tables.Items };
        bag.Cells["InventoryDialog.0"] = new Ms2.Net.Packets.EquipItem { Id = 11400021, Uid = 901, Amount = 1, Rarity = 1 };  // an outfit top
        bag.Cells["InventoryDialog.1"] = new Ms2.Net.Packets.EquipItem { Id = 13000001, Uid = 902, Amount = 1, Rarity = 1 };  // a one-handed RH weapon (slot 5)
        string Slot(int s) => MyInfoController.EquipBag + "." + s;

        c.SlotDroppedOn(Slot(5), "InventoryDialog.0", bag);
        Assert.That((bag.Equipped.Count, bag.Checked.Count, shown.Single().Msg), Is.EqualTo((0, 0, c.Model.Tables.Common(InventoryController.InvalidSlotKey))),
            "the item's model has no RH group type: box 0xd3, CanEquipItem not asked");
        Assert.That(bag.Refreshed, Is.EqualTo(new[] { "InventoryDialog.0" }), "the source is pushed again (vfunc_10)");
        shown.Clear();
        c.SlotDroppedOn(Slot(5), "InventoryDialog.1", bag);
        Assert.That((bag.Checked.Single(), bag.Equipped.Single(), shown.Count), Is.EqualTo(((902L, 5, 0), (902L, 5, 0), 0)),
            "CanEquipItem then RequestEquipItem(item, m_nSlot, m_nKind)");
        c.SlotDroppedOn(Slot(5), "InventoryDialog.7", bag);
        c.SlotDroppedOn(Slot(5), "QuickSlot.1", bag);
        Assert.That((bag.Equipped.Count, shown.Count), Is.EqualTo((1, 0)), "an empty cell or a name that is no bag slot: nothing");
    }

    [Test]
    public void AnEquippedItemDroppedOnAnotherHandIsSwapped()
    {
        Packs();
        var model = new GameTables(Packs(), "en").Items;
        Ms2.Net.Packets.EquipItem Rec(int id, long uid, Ms2.Net.Packets.EquipSlot s) => new() { Id = id, Uid = uid, Slot = s, Amount = 1, Rarity = 1 };
        var rh = Rec(13100001, 5001, Ms2.Net.Packets.EquipSlot.RH);   // equip subtype 0x1f
        var lh = Rec(13400001, 5002, Ms2.Net.Packets.EquipSlot.LH);   // equip subtype 0x22
        var two = Rec(15000001, 5003, Ms2.Net.Packets.EquipSlot.RH);  // equip subtype 0x32, two-handed
        var top = Rec(11400021, 5004, Ms2.Net.Packets.EquipSlot.CL);
        var skinRh = Rec(13100026, 5005, Ms2.Net.Packets.EquipSlot.RH);   // skin type 2: the kind-1 layer
        var skinLh = Rec(13400077, 5006, Ms2.Net.Packets.EquipSlot.LH);
        string Slot(int s) => MyInfoController.EquipBag + "." + s;
        Assert.That((model.Get(13100026).HasSkinType, model.Get(13100026).EquipSubtype, model.Get(13400077).HasSkinType,
            model.Get(13400077).EquipSubtype), Is.EqualTo((true, 0x1f, true, 0x22)));
        Assert.That((model.Get(13100001).HasSkinType, model.Get(13400001).HasSkinType), Is.EqualTo((false, false)));

        (MyInfoController, List<MessageBoxData>, List<(long, byte)>) Window(params Ms2.Net.Packets.EquipItem[] worn)
        {
            var shown = new List<MessageBoxData>();
            var sent = new List<(long, byte)>();
            var view = new Ms2.Net.Packets.InventoryView(new Ms2.Net.Packets.ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 2 });
            view.RequestSwapEquip = (u, s) => sent.Add((u, s));
            var me = UiFixture.Character();
            var (_, c, _, errors) = Load(x =>
            {
                x.Record = () => { var p = me.ToPcInfo(0); p.Equips = worn; return p; };
                x.ShowBox = shown.Add;
                x.Inventory = view;
            });
            Assert.That(errors, Is.Empty);
            return (c, shown, sent);
        }
        var src = new WornSlots();
        void Drag(Ms2.Net.Packets.EquipItem r) => src.Worn[Slot(5)] = InventoryController.ToItem(r, model);

        // both hands hold swappable items: the RH item onto LH is sent (ItemEquip mode 2, its uid, slot 4)
        var (c, shown, sent) = Window(rh, lh, top);
        Drag(rh);
        c.SlotDroppedOn(Slot(4), Slot(5), src);
        Assert.That((sent.Single(), shown.Count), Is.EqualTo(((5001L, (byte)4), 0)));
        Assert.That(src.Refreshed, Is.EqualTo(new[] { Slot(5) }), "the source is pushed again: the server's packets swap them");
        // the dragged item is not of subtype 0x1f / 0x22: false, no box
        Drag(two);
        c.SlotDroppedOn(Slot(4), Slot(5), src);
        Assert.That((sent.Count, shown.Count), Is.EqualTo((1, 0)));
        // a slot that is no equip slot of the source: nothing
        c.SlotDroppedOn(Slot(4), "QuickSlot.1", src);
        Assert.That(sent.Count, Is.EqualTo(1));

        // the hands checked are the kind-1 (skin) layer's (GetEquipItemSet1, unit_1414210f0.cpp:55): one skin hand only: box 0xd5
        (c, shown, sent) = Window(rh, lh, skinRh, top);
        Drag(rh);
        c.SlotDroppedOn(Slot(4), Slot(5), src);
        Assert.That((sent.Count, shown.Single().Msg), Is.EqualTo((0, c.Model.Tables.Common(MyInfoController.SwapHandsKey))));
        // one layer-0 hand only, no skin hand: sent
        (c, shown, sent) = Window(rh, top);
        Drag(rh);
        c.SlotDroppedOn(Slot(4), Slot(5), src);
        Assert.That((sent.Single(), shown.Count), Is.EqualTo(((5001L, (byte)4), 0)));
        // both skin hands, swappable: sent
        (c, shown, sent) = Window(rh, skinRh, skinLh, top);
        Drag(rh);
        c.SlotDroppedOn(Slot(4), Slot(5), src);
        Assert.That((sent.Single(), shown.Count), Is.EqualTo(((5001L, (byte)4), 0)));

        // neither hand: sent while this slot's item, when there is one, is swappable
        (c, shown, sent) = Window(top);
        Drag(rh);
        c.SlotDroppedOn(Slot(4), Slot(5), src);
        Assert.That((sent.Single(), shown.Count), Is.EqualTo(((5001L, (byte)4), 0)));
    }

    [Test]
    public void ABagGemDroppedOnItsGemSlotIsEquipped()
    {
        Packs();
        var book = new Ms2.Net.Packets.ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 4 };
        var view = new Ms2.Net.Packets.InventoryView(book);
        var sent = new List<(long, byte)>(); view.RequestEquipGem = (u, s) => sent.Add((u, s));
        var shown = new List<MessageBoxData>();
        var (h, c, _, errors) = Load(x => { x.Inventory = view; x.ShowBox = shown.Add; });
        Assert.That(errors, Is.Empty);
        c.OnCallback("onChangeTab", new object[] { 2 });
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(200 + i * 16); }
        Ms2.Net.Packets.EquipItem Gem(long uid, byte type) => new() { Id = 70200001, Uid = uid, GemType = type, Amount = 1, Rarity = 1 };
        var bag = new BagSlots { Table = c.Model.Tables.Items };
        bag.Cells["InventoryDialog.0"] = Gem(951, 3);
        bag.Cells["InventoryDialog.1"] = Gem(952, 5);
        string Slot(int s) => MyInfoController.GemBag + "." + s;

        // slot 2 is gem type 3: the worn type-3 gem's slot (BadgeSlot 3)
        c.SlotDroppedOn(Slot(2), "InventoryDialog.0", bag);
        Assert.That((sent.Single(), shown.Count), Is.EqualTo(((951L, (byte)3), 0)));
        Assert.That(bag.Refreshed, Is.EqualTo(new[] { "InventoryDialog.0" }));
        // slot 1 is gem type 5, none worn: GetSlot -1 as an unsigned char
        c.SlotDroppedOn(Slot(1), "InventoryDialog.1", bag);
        Assert.That(sent.Last(), Is.EqualTo((952L, (byte)0xff)));
        // another gem type than the slot's: nothing
        c.SlotDroppedOn(Slot(1), "InventoryDialog.0", bag);
        Assert.That(sent.Count, Is.EqualTo(2));

        // binds on equip: box 17 with the slot as its argument; the yes equips it without asking again
        c.Model.Player.Name = "Tester";
        var bind = Gem(953, 3); bind.Transfer.Rule = 8; bind.InventorySlot = 0;
        bag.Cells["InventoryDialog.2"] = bind;
        book.Apply(new Ms2.Net.Packets.ItemMessage.Add(bind));
        c.SlotDroppedOn(Slot(2), "InventoryDialog.2", bag);
        var box = shown.Single();
        Assert.That((sent.Count, box.Type, box.Extra, box.ItemUid, box.ArgInt(0, -1)), Is.EqualTo((2, 17, MyInfoController.BindOnGemEquipBox, 953L, 3)));
        Assert.That(box.Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(c.Model.Tables.Common(InventoryController.BindOnEquipKey), "Tester")), "the local player's name (vfunc_77)");
        Assert.That(c.ConfirmBindGemEquip(953, box.ArgInt(0, 0)), Is.True);
        Assert.That((sent.Last(), shown.Count), Is.EqualTo(((953L, (byte)3), 1)));
        Assert.That(c.ConfirmBindGemEquip(99, 3), Is.False, "an item the inventory no longer has: the handler's 0");

        // dead: box 0x42, nothing sent
        c.LocalPlayerDead = () => true;
        c.SlotDroppedOn(Slot(2), "InventoryDialog.0", bag);
        Assert.That((sent.Count, shown.Last().Msg), Is.EqualTo((3, c.Model.Tables.Common(MyInfoController.DeadKey))));
        // no local player: false, the source is not pushed again
        c.Model.HasLocalPlayer = false;
        int refreshed = bag.Refreshed.Count;
        c.SlotDroppedOn(Slot(2), "InventoryDialog.0", bag);
        Assert.That((sent.Count, bag.Refreshed.Count), Is.EqualTo((3, refreshed)));
    }

    /// <summary>The gem slot takes the worn gem from the local actor's PCGemEquip (gemslot.cpp:134), which OnBadgeEquip
    /// (inbound 0x7f, scenestategame.cpp:11006) keeps: mode 0 puts a gem on by its type, mode 1 takes the type off.</summary>
    [Test]
    public void TheGemSlotReadsTheGemsTheBadgePacketsKeep()
    {
        Packs();
        var book = new Ms2.Net.Packets.ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 4 };
        var view = new Ms2.Net.Packets.InventoryView(book);
        var sent = new List<(long, byte)>(); view.RequestEquipGem = (u, s) => sent.Add((u, s));
        var me = UiFixture.Character();
        var (h, c, _, errors) = Load(x => { x.Inventory = view; x.LocalGems = () => me.Gems; });
        Assert.That(errors, Is.Empty);
        c.OnCallback("onChangeTab", new object[] { 2 });
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(200 + i * 16); }
        Ms2.Net.Packets.EquipItem Gem(long uid, byte type, byte slot = 0) => new() { Id = 70200001, Uid = uid, GemType = type, BadgeSlot = slot, IsBadge = true, Amount = 1, Rarity = 1 };
        var bag = new BagSlots { Table = c.Model.Tables.Items };
        bag.Cells["InventoryDialog.0"] = Gem(951, 3);
        bag.Cells["InventoryDialog.1"] = Gem(952, 5);
        string Slot(int s) => MyInfoController.GemBag + "." + s;
        int changes = 0;
        me.Changed += () => changes++;

        // nothing of type 5 worn: 0xff; then the server puts one on at slot 7: the next drop sends 7
        c.SlotDroppedOn(Slot(1), "InventoryDialog.1", bag);
        Assert.That(sent.Last(), Is.EqualTo((952L, (byte)0xff)));
        Assert.That(me.Apply(new BadgeEquipMessage(0, 1, Gem(952, 5, 7), 5, 0)), Is.True);
        c.SlotDroppedOn(Slot(1), "InventoryDialog.1", bag);
        Assert.That(sent.Last(), Is.EqualTo((952L, (byte)7)));
        // the same type again replaces it
        Assert.That(me.Apply(new BadgeEquipMessage(0, 1, Gem(953, 5, 8), 5, 0)), Is.True);
        Assert.That(me.Gems.Where(g => g.GemType == 5).Select(g => g.Uid), Is.EqualTo(new[] { 953L }));
        // the fixture's type-3 gem (slot 3) taken off: the drop sends 0xff
        c.SlotDroppedOn(Slot(2), "InventoryDialog.0", bag);
        Assert.That(sent.Last(), Is.EqualTo((951L, (byte)3)));
        Assert.That(me.Apply(new BadgeEquipMessage(1, 1, null, 3, 0)), Is.True);
        c.SlotDroppedOn(Slot(2), "InventoryDialog.0", bag);
        Assert.That(sent.Last(), Is.EqualTo((951L, (byte)0xff)));
        // another object, a type not worn, a gem with no type and the error mode change nothing
        Assert.That(me.Apply(new BadgeEquipMessage(0, 7, Gem(954, 4, 2), 4, 0)), Is.False);
        Assert.That(me.Apply(new BadgeEquipMessage(1, 1, null, 3, 0)), Is.False);
        Assert.That(me.Apply(new BadgeEquipMessage(0, 1, Gem(955, 0, 2), 0, 0)), Is.False);
        Assert.That(me.Apply(new BadgeEquipMessage(4, 0, null, 0, 2)), Is.False);
        Assert.That(changes, Is.EqualTo(3));
        Assert.That(me.ToPcInfo(0).Gems.Select(g => g.Uid), Is.EqualTo(new[] { 953L }), "the character window's record has them too");
    }

    [Test]
    public void TheGemErrorsAreTheirSystemNotices()
    {
        var m = NewModel();
        string[] keys = Enumerable.Range(0, 7).Select(e => MyInfoController.GemEquipErrorKey(e, false)).ToArray();
        Assert.That(keys, Is.EqualTo(new[] { "s_gem_error_inventory_full", "s_gem_error_InvalidSlot", "s_gem_error_InvalidItem",
            "s_gem_error_Expired", "s_gem_error_PCBang", "s_gem_error_unknown", "s_gem_error_unknown" }));
        Assert.That((MyInfoController.GemEquipErrorKey(5, true), MyInfoController.GemEquipErrorKey(-1, true)), Is.EqualTo(("s_gem_error_VIP", "s_gem_error_unknown")));
        foreach (var k in keys.Append("s_gem_error_VIP")) Assert.That(m.Tables.Common(k), Is.Not.Empty, k);
    }

    [Test]
    public void AvatarRendersIntoTheMouseAreaAndTurnsOnARightDrag()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var me = UiFixture.Character();
        var c = new MyInfoController { Record = () => me.ToPcInfo(0), StatPoints = me.StatPoints };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var urls = new List<string>();
        p.ImageSize = url => { urls.Add(url); return url.Contains(":avatar/") ? (10, 10) : null; };
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True);
        for (int i = 0; i < 10; i++) { p.Tick(); c.Process(i * 16); }
        Assert.That(rt.Errors, Is.Empty);
        // vfunc_17's LoadRenderTarget: the movie loads its mouse area's size from the avatar view
        var area = h.FindNamed("m_mouseArea");
        Assert.That(area, Is.Not.Null);
        var b = area.GetBounds(null);
        TestContext.WriteLine($"urls: {string.Join(", ", urls)}; area {b.X},{b.Y} {b.W}x{b.H}");
        Assert.That(urls.Count(u => System.Text.RegularExpressions.Regex.IsMatch(u, $@"^img://{h.Id}:avatar/[1-9]\d*/[1-9]\d*$")), Is.EqualTo(1));
        Assert.That(c.Avatar, Is.Not.Null);
        Assert.That(c.Avatar.Angle, Is.EqualTo((0f, 0f, 90f)));

        // a right press: main.gfx's stage listener stops the mouseDown (buttonIdx 1) and redispatches rightMouseDown, a plain
        // MouseEvent; the movie's onAvatarMouseDown takes RIGHT_BUTTON for it, OnAvatarMouseDown(1) -> mode 1; a 100 pixel drag
        // right turns z by -35
        double x = b.X + b.W / 2, y = b.Y + b.H / 2;
        p.MouseMove(x, y);
        p.MouseButton(true, Ms2.Avm2.Flash.Player.RightButton);
        Assert.That(c.Avatar.Mode, Is.EqualTo(1));
        p.MouseMove(x + 100, y);
        Assert.That(c.Avatar.Angle.Z, Is.EqualTo(55f).Within(1e-3));
        p.MouseButton(false, Ms2.Avm2.Flash.Player.RightButton);
        // a left press does not turn (mode 2 clears on an unlocked view)
        p.MouseMove(x, y);
        p.MouseButton(true);
        p.MouseMove(x - 50, y);
        p.MouseButton(false);
        Assert.That(c.Avatar.Mode, Is.EqualTo(0));
        Assert.That(c.Avatar.Angle.Z, Is.EqualTo(55f).Within(1e-3));
        c.Close();
        Assert.That(c.Avatar, Is.Null);
    }
}
