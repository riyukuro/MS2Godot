using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The inventory (uiinventorydialog) run by its own ActionScript with UIInventoryDialog's C++ side (needs MS2_DATA).</summary>
[NonParallelizable]   // sets process-wide hooks the product reads
public class InventoryTests
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

    /// <summary>A bag in the parsed item packet's shape (ItemInventory mode 8, LoadTab): tab = id / 10000000 here, 48 base slots.</summary>
    static ItemBook Book()
    {
        var b = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        b.Apply(new ItemMessage.LoadTab(0, new[] { new EquipItem { Id = 1, Uid = 1, InventorySlot = 0, Amount = 1 }, new EquipItem { Id = 2, Uid = 2, InventorySlot = 5, Amount = 1 } }));
        b.Apply(new ItemMessage.LoadTab(3, new[] { new EquipItem { Id = 30000001, Uid = 3, InventorySlot = 0, Amount = 7 } }));
        b.Apply(new ItemMessage.ExpandCount(3, 6));
        return b;
    }

    static (UiHost, InventoryController, ItemBook) Load(UiModel model = null, ItemBook book = null)
    {
        model ??= NewModel();
        model.Player.Mesos = 1234567;
        book ??= Book();
        var c = new InventoryController { View = new InventoryView(book) };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) { p.Tick(); c.Process(); }
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, book);
    }

    [Test]
    public void SlotsAreBuiltOnTheItemSlotPath()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var (h, c, book) = Load(model);
        int material = c.Tabs.Select((t, i) => (t, i)).First(x => x.t.Name == "material").i;
        h.Set(h.Child("m_tab"), "selectedIndex", material);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var data = model.Tables.Items.Get(30000001);
        Assert.That(data, Is.Not.Null, "item 30000001 in itemdata");
        var slot = c.GetBinding("InventoryDialog.0");
        Assert.That(slot, Is.Not.Null);
        Assert.That(Rt.ToNumber(h.Get(slot, "itemID")), Is.EqualTo(30000001));
        Assert.That(Rt.ToNumber(h.Get(slot, "index")), Is.EqualTo(0));
        Assert.That(Rt.ToNumber(h.Get(slot, "level")), Is.EqualTo(data.LevelLimit));
        Assert.That(Rt.ToString(h.Get(slot, "itemCount")), Is.EqualTo(data.IsStackable ? "7" : ""));
        Assert.That(Rt.ToBoolean(h.Get(slot, "updateIconSearchOn")), Is.False);
        Assert.That(Rt.IsNullish(h.Get(slot, "itemNew")), Is.True);
        Assert.That(c.GetBinding("InventoryDialog.1"), Is.Null, "an empty cell");
        // the new mark (ItemInventory mode 7 sets IsNew)
        book.Get(3).IsNew = true;
        Assert.That(Rt.ToNumber(h.Get(c.GetBinding("InventoryDialog.0"), "itemNew")), Is.EqualTo(1));
        model.HasLocalPlayer = false;
        Assert.That(c.GetBinding("InventoryDialog.0"), Is.Null, "no local player: vfunc_8 returns 0");
    }

    static List<object> Items(UiHost h, object dp) =>
        Enumerable.Range(0, (int)Rt.ToNumber(h.Get(dp, "length"))).Select(i => h.Invoke(dp, "requestItemAt", i)).ToList();

    [Test]
    public void TabsAreTheClientsTabList()
    {
        Packs();
        var (h, c, _) = Load();
        var tabs = Items(h, h.Get(h.Child("m_tab"), "dataProvider"));
        var labels = tabs.Select(t => Rt.ToString(h.Get(t, "label"))).ToList();
        TestContext.WriteLine(string.Join(", ", labels));
        Assert.That(labels.First(), Is.EqualTo("$s_inventory_tab_equip"));
        Assert.That(labels.Last(), Is.EqualTo("$s_inventory_tab_misc"));
        Assert.That(tabs.Select(t => (int)Rt.ToNumber(h.Get(t, "tabIndex"))), Is.EqualTo(c.Tabs.Select(t => t.Tab)));
        Assert.That(Rt.ToString(h.Get(h.Get(tabs[0], "tooltipData"), "id")), Is.EqualTo("$s_tooltip_inven_equip"));
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_tab"), "selectedIndex")), Is.EqualTo(0), "initData selects tab 0's button");
    }

    [Test]
    public void ShownTabHasItsSlotsLabelAndMesos()
    {
        Packs();
        var (h, c, _) = Load();
        var names = Items(h, h.Get(h.Child("m_itemBagEx"), "dataProvider")).Select(Rt.ToString).ToList();
        Assert.That(names.Count, Is.EqualTo(48));
        Assert.That(names[0], Is.EqualTo("InventoryDialog.0"));
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("2/48"));
        Assert.That(Rt.ToString(h.Get(h.Child("m_meso"), "text")), Is.EqualTo("1,234,567"));
    }

    [Test]
    public void ClickingATabShowsThatTabAndSendsNothing()
    {
        Packs();
        var (h, c, _) = Load();
        int sent = 0; c.View.RequestSort = (_, _) => sent++;
        int material = c.Tabs.Select((t, i) => (t, i)).First(x => x.t.Name == "material").i;
        h.Set(h.Child("m_tab"), "selectedIndex", material);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        Assert.That(c.View.Tab, Is.EqualTo(3));
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("1/54"));
        Assert.That((int)Rt.ToNumber(h.Get(h.Get(h.Child("m_itemBagEx"), "dataProvider"), "length")), Is.EqualTo(54));
        Assert.That(sent, Is.Zero);
    }

    [Test]
    public void OpenLockOrBreakDialogSwitchesTheOtherButtonOff()
    {
        // UpdateButtonState (@0x141999f60): lock open: m_1a4 3, break off; break open: 1, lock off; donation open: 2, lock off
        Packs();
        var (h, c, _) = Load();
        bool Enabled(string n) => Rt.ToBoolean(h.Get(h.Child(n), "enabled"));
        c.LockOrBreakDialogExists = () => true;
        c.UpdateButtonState();
        Assert.That((c.ButtonMode, Enabled("m_itemBreak"), Enabled("m_itemLock")), Is.EqualTo((0, true, true)));
        bool lockOpen = true;
        c.LockDialogOpen = () => lockOpen;
        c.UpdateButtonState();
        Assert.That((c.ButtonMode, Enabled("m_itemBreak"), Enabled("m_itemLock")), Is.EqualTo((3, false, true)));
        lockOpen = false;
        c.BreakDialogOpen = () => true;
        c.UpdateButtonState();
        Assert.That((c.ButtonMode, Enabled("m_itemBreak"), Enabled("m_itemLock")), Is.EqualTo((1, true, false)));
        c.BreakDialogOpen = () => false;
        c.DonationDialogOpen = () => true;
        c.UpdateButtonState();
        Assert.That((c.ButtonMode, Enabled("m_itemBreak"), Enabled("m_itemLock")), Is.EqualTo((2, true, false)));
        c.DonationDialogOpen = () => false;
        c.UpdateButtonState();
        Assert.That((c.ButtonMode, Enabled("m_itemBreak"), Enabled("m_itemLock")), Is.EqualTo((0, true, true)));
    }

    [Test]
    public void ButtonStateIsNotPushedWhileNoLockOrBreakDialogExists()
    {
        // UpdateButtonState (uiinventorydialog.cpp:1152): lock, break and both extraction dialogs null = return before the push
        Packs();
        var (h, c, _) = Load();
        bool Enabled(string n) => Rt.ToBoolean(h.Get(h.Child(n), "enabled"));
        h.Set(h.Child("m_itemLock"), "enabled", true);
        c.LockDialogOpen = () => true;
        c.UpdateButtonState();
        Assert.That(Enabled("m_itemBreak"), Is.True, "nothing pushed: the open test is not even reached");
        Assert.That(c.ButtonMode, Is.Zero);
        c.LockOrBreakDialogExists = () => true;
        c.UpdateButtonState();
        Assert.That((c.ButtonMode, Enabled("m_itemBreak")), Is.EqualTo((3, false)));
    }

    [Test]
    public void TheDonationDialogAloneDoesNotCountAsAnExistingLockOrBreakDialog()
    {
        // UpdateButtonState tests 0x4f08, 0x4e8d and the extraction dialogs, not the donation dialog 0x4e8e (uiinventorydialog.cpp:1152)
        Assert.That(InventoryController.LockOrBreakExists(null, null), Is.False);
        Assert.That(InventoryController.LockOrBreakExists(null, new ItemBreakDialogController(true)), Is.False, "donation only");
        Assert.That(InventoryController.LockOrBreakExists(null, new ItemBreakDialogController(false)), Is.True);
        Assert.That(InventoryController.LockOrBreakExists(new ItemLockDialogController(), null), Is.True);
    }

    [Test]
    public void ItemDialogChangedPushesTheButtonsAndFlagsOnlyWhileOpen()
    {
        // the lock / break dialogs' load and unload (uiinventorydialog.cpp:2340-2362): UpdateButtonState then FUN_141999e00
        Packs();
        var (h, c, _) = Load();
        bool Enabled(string n) => Rt.ToBoolean(h.Get(h.Child(n), "enabled"));
        c.LockOrBreakDialogExists = () => true;
        c.LockDialogOpen = () => true;
        c.ItemDialogChanged();
        Assert.That((c.ButtonMode, Enabled("m_itemBreak")), Is.EqualTo((3, false)));
        c.LockDialogOpen = () => false;
        c.Close();
        for (int i = 0; i < 300 && !c.IsClosed; i++) { h.Player.Tick(); c.Process(); }
        Assert.That(c.IsClosed, Is.True);
        c.ItemDialogChanged();
        Assert.That(c.ButtonMode, Is.EqualTo(3), "closed: neither the state nor the mode is touched");
    }

    [Test]
    public void AddFlagsMakesTheNewMarksExpandLabelAndTabRebuildPendingAgain()
    {
        Packs();
        var (_, c, _) = Load();
        Assert.That(c.View.Pending, Is.Zero);
        c.AddFlags();
        Assert.That(c.View.Pending, Is.EqualTo(7));
    }

    [Test]
    public void ItemMergeButtonRefusesThenOpensTheOptionDialog()
    {
        Packs();
        var (h, c, _) = Load();
        var notices = new List<string>(); var toggled = new List<int>();
        c.NoticeText += notices.Add; c.ToggleDialog += toggled.Add;
        bool shut = true, refused = true;
        c.MergeShutdown = () => shut; c.TalkRefused = () => refused;
        Assert.That(c.OnCallback("onClickItemMerge", Array.Empty<object>()), Is.True);
        Assert.That((notices.Count, toggled.Count), Is.EqualTo((1, 0)), "shutdown wins");
        shut = false;
        c.OnCallback("onClickItemMerge", Array.Empty<object>());
        Assert.That((notices.Count, toggled.Count), Is.EqualTo((2, 0)), "battle state refuses");
        Assert.That(notices[1], Is.Not.EqualTo(notices[0]));
        refused = false;
        c.OnCallback("onClickItemMerge", Array.Empty<object>());
        Assert.That(toggled, Is.EqualTo(new[] { 0x4e68 }));
        Assert.That(notices.Count, Is.EqualTo(2));
    }

    [Test]
    public void RevertDialogHeadAnswers17ForTheClickAndAcceptedHintsElseFallsThrough()
    {
        Packs();
        var model = NewModel();
        var (_, c, _) = Load(model);
        var data = new[] { 11300001, 12000001, 11100001, 11200001 }.Select(i => model.Tables.Items.Get(i)).FirstOrDefault(d => d != null && d.Type == 1);
        if (data == null) Assert.Ignore("no known equipment item");
        var item = Item.Create(model.Tables.Items, data.Id, 3);
        c.DialogHead = () => ItemMergeRevertDialogController.UiId;
        int check = ItemMergeRevertDialogController.CheckRank;
        c.RevertCheck = _ => check;
        Assert.That(c.UseMode(item, false), Is.EqualTo(17), "the click");
        Assert.That(c.UseMode(item, true), Is.EqualTo(6), "refused hint: the normal chain (equipment)");
        check = ItemMergeRevertDialogController.CheckOk;
        Assert.That(c.UseMode(item, true), Is.EqualTo(17), "accepted hint");
    }

    [Test]
    public void SortIsTheClientsAndTheOtherRequestsAreOnlyRaised()
    {
        Packs();
        var (h, c, book) = Load();
        var raised = new List<string>(); var sent = new List<(int, bool)>(); int toggled = 0;
        c.Button += raised.Add; c.ToggleDialog += id => toggled = id; c.View.RequestSort = (t, s) => sent.Add((t, s));
        Assert.That(c.OnCallback("OnClickSort", Array.Empty<object>()), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { (0, false) }), "no expired item: sent at once");
        Assert.That(c.OnCallback("onClickSlotExpand", new object[] { "Equip" }), Is.True);
        Assert.That(c.OnCallback("onClickItemBreak", Array.Empty<object>()), Is.True);
        Assert.That(c.OnCallback("onClickItemRemakeOption", Array.Empty<object>()), Is.False, "the client returns 0");
        Assert.That(c.OnCallback("onClickOpenBank", Array.Empty<object>()), Is.False, "the client returns 0");
        Assert.That(c.OnCallback("onClickMesoDrop", Array.Empty<object>()), Is.False, "no branch in the client: 0");
        Assert.That(c.OnCallback("OnSelected", new object[] { "Elixir" }), Is.False, "no branch in the client: 0");
        Assert.That(raised, Is.EqualTo(new[] { "onClickSlotExpand", "onClickItemRemakeOption", "onClickOpenBank" }), "the two unhandled ones raise nothing");
        Assert.That(toggled, Is.EqualTo(0x4e8d));

        // an expired item: box 0x2a1, the answer sends (0 = stack)
        MessageBoxData shown = null;
        c.ShowBox = b => shown = b;
        book.Apply(new ItemMessage.Add(new EquipItem { Id = 3, Uid = 9, InventorySlot = 7, Amount = 1, ExpiryTime = 1 }));
        c.View.ServerTime = () => 100;
        sent.Clear();
        var before = MessageBoxData.CommonByKey;
        MessageBoxData.CommonByKey = k => k == InventoryController.ExpiredItemsKey ? "remove expired?" : "";
        try
        {
            Assert.That(c.View.ClickSort(), Is.EqualTo(InventoryView.SortClick.Confirm));
            Assert.That(c.OnCallback("OnClickSort", Array.Empty<object>()), Is.True);
        }
        finally { MessageBoxData.CommonByKey = before; }
        Assert.That(sent, Is.Empty);
        Assert.That(shown?.Msg, Is.EqualTo("remove expired?"));
        Assert.That(shown.Type, Is.EqualTo(1)); Assert.That(shown.Modaless, Is.True); Assert.That(shown.EnterAllowed, Is.False);
        shown.Callback(0, shown);
        Assert.That(sent, Is.EqualTo(new[] { (0, true) }));
    }

    [Test]
    public void AnOwnedItemKeepsItsRecordsLockAndEnchant()
    {
        var model = NewModel();
        var e = new EquipItem { Id = 15000001, Uid = 9, Rarity = 3, Amount = 1, LockState = 1, UnlockTime = 77 };
        e.Enchant.Level = 5; e.LimitBreak.Level = 2;
        var item = InventoryController.ToItem(e, model.Tables.Items);
        Assert.That((item.LockState, item.UnlockTime, item.EnchantLevel, item.UnlimitedEnchantLevel), Is.EqualTo((1, 77L, 5, 2)));
        Assert.That(item.Record, Is.SameAs(e));
    }

    [Test]
    public void TheWornItemComparedIsTheClients()
    {
        var model = NewModel(); var t = model.Tables;
        EquipItem E(int id, long uid, EquipSlot slot) => new() { Id = id, Uid = uid, Slot = slot, Rarity = 1 };
        Item Hover(int id) => InventoryController.ToItem(E(id, 999, EquipSlot.Unknown), t.Items);
        var top = new PcInfo { Equips = new[] { E(11400001, 1, EquipSlot.CL), E(13000001, 2, EquipSlot.RH), E(14000001, 3, EquipSlot.LH) } };
        var worn = WornEquipment.Of(top, t.Items);
        Assert.That(worn.ForCompare(Hover(11400001), t.ItemModels, false)?.Uid, Is.EqualTo(1L), "the model's first group, CL");
        Assert.That(worn.ForCompare(Hover(11400021), t.ItemModels, false), Is.Null, "a skin item looks in layer 1");
        Assert.That(worn.ForCompare(Hover(15300001), t.ItemModels, false)?.Uid, Is.EqualTo(2L), "two-handed: RH first");
        Assert.That((worn.ForCompare(Hover(13100001), t.ItemModels, false)?.Uid, worn.ForCompare(Hover(13100001), t.ItemModels, true)?.Uid),
            Is.EqualTo((2L, 3L)), "subtype 0x1f: RH, LH with Shift");
        var leftOnly = WornEquipment.Of(new PcInfo { Equips = new[] { E(14000001, 3, EquipSlot.LH) } }, t.Items);
        Assert.That(leftOnly.ForCompare(Hover(15300001), t.ItemModels, false)?.Uid, Is.EqualTo(3L), "two-handed: then LH");
        Assert.That((worn.Wears(3), worn.Wears(999)), Is.EqualTo((true, false)));
        Assert.That(WornEquipment.Of(new PcInfo(), t.Items), Is.Null, "no equipment lists");
    }

    [Test]
    public void TheWornAbilitiesSumTheOptionsAndTheLimitBreak()
    {
        var model = NewModel(); var t = model.Tables;
        EquipItem E(int id, EquipSlot slot, int key, int value)
        {
            var e = new EquipItem { Id = id, Uid = id, Slot = slot, Rarity = 1 };
            e.Options.Special[0][key] = value;
            return e;
        }
        var robe = E(11400001, EquipSlot.CL, 0x5b, 3);
        robe.Options.Special[2][0x5b] = 2;
        robe.Options.Special[1][0xcf] = 50;                                  // not below 0xcf
        robe.LimitBreak.Special[0xca] = 7;
        var record = new PcInfo { Level = 100, Equips = new[] { robe, E(14000001, EquipSlot.LH, 0x10, 1), E(15300001, EquipSlot.LH, 0x20, 9) } };
        Assert.That(WornEquipment.Abilities(record, t.Items, t.ItemModels, _ => true),
            Is.EqualTo(new Dictionary<int, int> { [0x5b] = 5, [0xca] = 7, [0x10] = 1 }), "five groups plus the limit break; a two-handed LH item adds nothing");
        Assert.That(WornEquipment.Abilities(record, t.Items, t.ItemModels, it => it.Uid != 11400001),
            Is.EqualTo(new Dictionary<int, int> { [0x10] = 1 }), "an item the wearer cannot use adds nothing");
        record.Level = 0;
        Assert.That(WornEquipment.Abilities(record, t.Items, t.ItemModels, _ => true), Is.Empty, "under the level limit");
        Assert.That(WornEquipment.Abilities(new PcInfo(), t.Items, t.ItemModels, _ => true), Is.Null);
    }

    [Test]
    public void TheRightClickEquipSlotIsTheClients()
    {
        var model = NewModel(); var t = model.Tables;
        EquipItem E(int id, long uid, EquipSlot slot) => new() { Id = id, Uid = uid, Slot = slot, Rarity = 1 };
        Item Bag(int id) => InventoryController.ToItem(E(id, 999, EquipSlot.Unknown), t.Items);
        WornEquipment Worn(params EquipItem[] e) => WornEquipment.Of(new PcInfo { Equips = e }, t.Items);
        int Slot(WornEquipment w, int id, bool usable = true) => w.EquipSlotFor(Bag(id), t.ItemModels, _ => false, _ => usable);
        var both = Worn(E(13000001, 2, EquipSlot.RH), E(14000001, 3, EquipSlot.LH));
        var rightOnly = Worn(E(13000001, 2, EquipSlot.RH));
        var none = Worn();
        Assert.That(Slot(none, 15300001), Is.EqualTo(5), "two-handed: RH");
        Assert.That((Slot(both, 13100001), Slot(rightOnly, 13100001), Slot(none, 13100001)), Is.EqualTo((5, 4, 5)),
            "subtype 0x1f: both hands RH, only RH then LH, no RH then RH");
        Assert.That(Slot(rightOnly, 13100001, usable: false), Is.EqualTo(5), "an unusable worn item is no item");
        Assert.That(Slot(Worn(E(15300001, 2, EquipSlot.RH)), 13100001), Is.EqualTo(5), "a two-handed RH item");
        Assert.That(Slot(none, 13000001), Is.EqualTo(t.ItemModels.Get(t.Items.Get(13000001).ItemPreset).FirstSlotType), "another weapon: the model's first group");
        Assert.That(Slot(none, 11400001), Is.EqualTo(t.ItemModels.Get(t.Items.Get(11400001).ItemPreset).FirstSlotType), "no weapon: the model's first group");
        Assert.That(both.EquipSlotFor(Bag(13100001), t.ItemModels, f => f == 0xda, _ => true, it => it.Uid == 2 ? 10 : 5), Is.EqualTo(4),
            "feature 0xda: the higher-level RH item is kept, LH");
    }

    [Test]
    public void TheHoverTooltipIsTheClients()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        model.Keys = new KeyTable(Packs(), "en"); model.Keys.LoadDefaultKeys();
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(1, new[] {
            new EquipItem { Id = 15300001, Uid = 11, InventorySlot = 0, Amount = 1, Rarity = 1 },
            new EquipItem { Id = 11400001, Uid = 12, InventorySlot = 1, Amount = 1, Rarity = 1 } }));
        book.Apply(new ItemMessage.LoadTab(2, new[] { new EquipItem { Id = 20000028, Uid = 21, InventorySlot = 0, Amount = 10, Rarity = 1 } }));
        var (h, c, _) = Load(model, book);
        var me = new PcInfo { CharacterId = 5, Name = "Solo", Equips = new[] { new EquipItem { Id = 13000001, Uid = 31, Slot = EquipSlot.RH, Rarity = 1 } } };
        c.Wearer = () => me;
        void Show(int tab) { h.Set(h.Child("m_tab"), "selectedIndex", c.Tabs.Select((t, i) => (t, i)).First(x => x.t.Tab == tab).i); for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); } }
        string Common(string key) => model.Tables.Common(key);

        // lv10_tooltip_consumable_red_herb.png: nothing worn to compare; the quick slot hint and the double-click line
        Show(2);
        var herb = c.GetSlotTooltip("InventoryDialog.0");
        Assert.That(herb["tooltip"], Is.EqualTo("export_common_tooltip_kindsItem"));
        Assert.That((herb["enablePuton"], herb["enableUseDbClick"]), Is.EqualTo((Common("s_msg_rclick_puton_qslot"), Common("s_msg_dclick_usable"))));
        var shown = TooltipTests.Draw(herb);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("quick slots"));
        Assert.That(shown, Has.Some.Contains("double-clicking"));

        // a two-handed weapon against the worn RH item: the compare tooltip, the worn one on the left
        Show(1);
        var pair = c.GetSlotTooltip("InventoryDialog.0");
        Assert.That(pair["tooltip"], Is.EqualTo("export_common_tooltip_tooltipCompare"));
        var lhs = (IDictionary<string, object>)pair["lhsTooltip"];
        var rhs = (IDictionary<string, object>)pair["rhsTooltip"];
        Assert.That((lhs["itemName"], rhs["itemName"]), Is.EqualTo((model.Tables.ItemName(13000001), model.Tables.ItemName(15300001))));
        Assert.That((lhs["itemEquip"], lhs["enablePuton"]), Is.EqualTo((Common("s_word_equiped"), "")));
        Assert.That(rhs["enablePuton"], Is.EqualTo(Common("s_msg_rclick_puton_equip")));
        Assert.That((lhs.ContainsKey("enablePreview"), rhs.ContainsKey("enablePreview")), Is.EqualTo((false, true)));
        Assert.That(pair.ContainsKey("enablePuton"), Is.False);
        shown = TooltipTests.Draw(pair);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Equipped"));

        // a CL item with nothing worn on CL: its own tooltip
        Assert.That(c.GetSlotTooltip("InventoryDialog.1")["tooltip"], Is.EqualTo("export_common_tooltip_kindsItem"));
        // the worn item itself: no tooltip (an empty object); not a bag slot: none
        me.Equips = new[] { new EquipItem { Id = 15300001, Uid = 11, Slot = EquipSlot.RH, Rarity = 1 } };
        Assert.That(c.GetSlotTooltip("InventoryDialog.0"), Is.Empty);
        Assert.That(c.GetSlotTooltip("InventoryDialog.999"), Is.Null);
        Assert.That(c.GetSlotTooltip("Other.0"), Is.Null);
    }

    [Test]
    public void ADropOnAnotherSlotSendsTheMoveOnly()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var (h, c, book) = Load(model);
        int material = c.Tabs.Select((t, i) => (t, i)).First(x => x.t.Tab == 3).i;
        h.Set(h.Child("m_tab"), "selectedIndex", material);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var sent = new List<(long, short)>(); c.View.RequestMove = (u, s) => sent.Add((u, s));
        var raised = new List<string>(); c.Button += raised.Add;
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "InventoryDialog.0", "InventoryDialog.4" });
        Assert.That(sent, Is.EqualTo(new[] { (3L, (short)4) }), "mode 3: the dragged item's uid and the target index");
        Assert.That(c.View.ItemAt(c.View.Tab, 0)?.Uid, Is.EqualTo(3L), "nothing moves until the server says so");
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "InventoryDialog.0", "InventoryDialog.0" });
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "InventoryDialog.0" });
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "QuickSlot.1", "InventoryDialog.4" });
        Assert.That(sent, Has.Count.EqualTo(1), "the same slot, one argument or a source that is no drag slot send nothing");
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "InventoryDialog.0", null });
        Assert.That((sent.Count, raised.Last()), Is.EqualTo((1, "notifySlotDragEnd")), "a drop off the bag is not ported: raised");
    }

    [Test]
    public void AShiftClickLinksTheSlotItem()
    {
        var model = NewModel();
        model.HasLocalPlayer = true;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(1, new[] { new EquipItem { Id = 11400001, Uid = 11, InventorySlot = 0, Amount = 1, Rarity = 3 } }));
        var (h, c, _) = Load(model, book);
        int gear = c.Tabs.Select((t, i) => (t, i)).First(x => x.t.Tab == 1).i;
        h.Set(h.Child("m_tab"), "selectedIndex", gear);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var linked = new List<Item>(); c.LinkItem = linked.Add;
        var raised = new List<string>(); c.Button += raised.Add;
        // InvenSlot::vfunc_15: the slot's item (FindItemByUid) is linked; an empty cell links nothing
        Assert.That(c.OnCallback("notifySlotShiftClick", new object[] { "InventoryDialog.0" }), Is.True);
        Assert.That(linked.Select(i => (i.Uid, i.Data.Id)), Is.EqualTo(new[] { (11L, 11400001) }));
        c.OnCallback("notifySlotShiftClick", new object[] { "InventoryDialog.1" });
        Assert.That(linked.Count, Is.EqualTo(1));
        // no name: nothing; another slot's name is raised
        c.OnCallback("notifySlotShiftClick", new object[] { Undefined.Value });
        c.OnCallback("notifySlotShiftClick", new object[] { "EquipSlot.3" });
        Assert.That((linked.Count, raised), Is.EqualTo((1, new List<string> { "notifySlotShiftClick" })));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ARightClickSendsTheEquipOnly()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(1, new[]
        {
            new EquipItem { Id = 13100001, Uid = 10, InventorySlot = 0, Amount = 1, Rarity = 1 },                    // subtype 0x1f
            new EquipItem { Id = 11400001, Uid = 11, InventorySlot = 1, Amount = 1, Rarity = 1 },
            new EquipItem { Id = 11400001, Uid = 12, InventorySlot = 2, Amount = 1, Rarity = 1, ExpiryTime = 500 },
            new EquipItem { Id = 11400021, Uid = 13, InventorySlot = 3, Amount = 1, Rarity = 1 },                    // a skin item
            new EquipItem { Id = 11400001, Uid = 14, InventorySlot = 4, Amount = 1, Rarity = 1, Transfer = { Rule = 8 } }, // binds on equip
            new EquipItem { Id = 11400001, Uid = 15, InventorySlot = 5, Amount = 1, Rarity = 1, ExpiryTime = 1000 },  // expires now
        }));
        var (h, c, _) = Load(model, book);
        c.View.ServerTime = () => 1000;
        var record = new PcInfo { Level = 50, JobGroup = 10, Equips = new[] { new EquipItem { Id = 13000001, Uid = 2, Slot = EquipSlot.RH, Rarity = 1 } } };
        c.Wearer = () => record;
        int gear = c.Tabs.Select((t, i) => (t, i)).First(x => x.t.Tab == 1).i;
        h.Set(h.Child("m_tab"), "selectedIndex", gear);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var sent = new List<(long, byte, byte)>(); c.View.RequestEquip = (u, s, m) => sent.Add((u, s, m));
        var raised = new List<string>(); c.Button += raised.Add;
        var boxes = new List<string>(); var shownBoxes = new List<MessageBoxData>();
        c.ShowBox = b => { boxes.Add(b.Msg); shownBoxes.Add(b); };
        var t = model.Tables;
        Assert.That(t.Common(InventoryController.ExpiredKey), Is.Not.Empty);
        Assert.That(t.Common(InventoryController.LowLevelKey), Is.Not.Empty);

        var notices = new List<string>(); c.SystemNotice += notices.Add;
        var guide = new Ms2.Ui.Guide.GuideManager(new Ms2.Ui.Guide.GuideWorld());
        guide.AddLimit(Ms2.Ui.Guide.GuideLimit.ItemPutOn);
        Ms2.Ui.Guide.GuideManager.Current = guide;
        try { c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0", 1 }); }
        finally { Ms2.Ui.Guide.GuideManager.Current = null; }
        Assert.That((sent.Count, notices.Single()), Is.EqualTo((0, Ms2.Ui.Guide.GuideManager.ItemPutOnOffNotice)),
            "the guide holds putting on: notice 2185, no send (inventory.cpp:1237)");
        notices.Clear();

        Assert.That(c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0", 1 }), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { (10L, (byte)4, (byte)0) }), "subtype 0x1f with only RH worn: LH, layer 0");
        Assert.That(c.View.ItemAt(c.View.Tab, 0)?.Uid, Is.EqualTo(10L), "nothing is put on until the server says so");
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        Assert.That(sent.Last(), Is.EqualTo((11L, (byte)t.ItemModels.Get(t.Items.Get(11400001).ItemPreset).FirstSlotType, (byte)0)));
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.3", 1 });
        byte layer = model.IsFeatureEnabled(0x8e) ? (byte)2 : (byte)1;
        Assert.That(sent.Last().Item3, Is.EqualTo(layer == 2 && model.IsFeatureEnabled(142) ? 2 : 1), "a skin item's layer, mapped as FUN_1401fb870");
        int count = sent.Count;

        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.2", 1 });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((count, t.Common(InventoryController.ExpiredKey))), "expired: box 225, no send");
        record.Level = 0;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((count, t.Common(InventoryController.LowLevelKey))), "level: box 224, no send");
        record.Level = 50;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.5", 1 });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((count, t.Common(InventoryController.ExpiredKey))), "IsExpired is expiry <= now");
        model.Player.Name = "Tester";
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.4", 1 });
        Assert.That(sent.Count, Is.EqualTo(count), "binds on equip: the box first, no send");
        var bind = shownBoxes.Last();
        Assert.That((bind.Type, bind.Extra, bind.ItemUid), Is.EqualTo((0x11, InventoryController.BindOnEquipBox, 14L)));
        Assert.That(bind.Args, Is.EqualTo(new[] { "0", "0" }), "the slot asked for (0) and the layer, Append<unsigned>");
        Assert.That(bind.Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(t.Common(InventoryController.BindOnEquipKey), "Tester")));
        Assert.That(t.Common(InventoryController.BindOnEquipKey), Does.Contain("{0}"), "the key is formatted with the name");
        Assert.That(bind.ItemEntry, Is.Not.Null, "SetItemEntry: flag 1");
        Assert.That((bind.Flags & 1, bind.ItemEntry.Count, bind.ItemEntry.Max, bind.ItemEntry.Price, bind.ItemEntry.UnitCount), Is.EqualTo((1, 0, -1, -1, 1)));
        Assert.That(bind.ItemEntry.Name, Does.StartWith($"<FONT color='#{t.Items.TextColor(1):x}'>"), "the name in the rarity's colour");
        var obj = bind.FillObj(false, false, model);
        string bag = "MsgBoxData_" + bind.Id;
        Assert.That((obj["boxType"], obj["bagId"], obj["itemName"], obj["itemCount"], obj["itemMax"], obj["itemUnitCount"]),
            Is.EqualTo(("itemNotify", bag, bind.ItemEntry.Name, 0, -1, 1)));
        Assert.That(obj.ContainsKey("itemPrice"), Is.False, "price -1: no price members");
        Assert.That(model.ItemSlots[bag].Item.Uid, Is.EqualTo(14L), "the bag is bound to the item (CreateItemInfoSlot)");
        bind.ReleaseItems(model);
        Assert.That(model.ItemSlots.ContainsKey(bag), Is.False, "the record's item binding goes with it");
        Assert.That(c.ConfirmBindEquip(14, bind.ArgUInt(0, 0), bind.ArgUInt(1, 0)), Is.True);
        Assert.That(sent.Last(), Is.EqualTo((14L, (byte)t.ItemModels.Get(t.Items.Get(11400001).ItemPreset).FirstSlotType, (byte)0)),
            "the yes asks again with the binding skip: the slot chosen then, layer 0");
        Assert.That(shownBoxes.Last(), Is.SameAs(bind), "no second box");
        Assert.That(c.ConfirmBindEquip(99, 0, 0), Is.False, "an item the inventory no longer has: the handler's 0");
        count = sent.Count;
        record.JobGroup = 0;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((count, t.Common(InventoryController.EquipJobKey))), "job 0 is never allowed: box 232");
        record.JobGroup = 10;
        c.LocalPlayerDead = () => true;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.2", 1 });
        Assert.That((sent.Count, boxes.Count(b => b == t.Common(MyInfoController.DeadKey)), boxes.Last()),
            Is.EqualTo((count, 2, t.Common(MyInfoController.DeadKey))), "dead: box 66 before the expiry check, no send (inventory.cpp:1241)");
        c.LocalPlayerDead = () => false;
        c.ActionKeyDown = a => a == 0x49406;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        c.ActionKeyDown = _ => false;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1" });
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.9", 1 });
        Assert.That(sent.Count, Is.EqualTo(count), "the preview key, a double click or an empty cell send nothing");
        Assert.That(raised.Count(r => r == "notifySlotUse"), Is.EqualTo(2), "the preview and the double click (no function) are raised");
        model.HasLocalPlayer = false;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        Assert.That(sent.Count, Is.EqualTo(count), "no local player");
    }

    [Test]
    public void ADoubleClickSendsTheUseOnly()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var t = model.Tables;
        bool Plain(ItemData d) => d is { HasFunction: true, HasCubeTextureMesh: false, IsScoreItem: false, LevelLimit: <= 1, GenderLimit: 2, JobLimit.Count: 0 }
            && !InventoryController.OwnUseFunctions.Contains(d.FunctionName) && !d.IsPetItem && !d.HasMaidId;
        int plain = Enumerable.Range(20000000, 2000).Concat(Enumerable.Range(20300000, 2000)).FirstOrDefault(id => t.Items.Get(id) is { } d && Plain(d));
        Assert.That(plain, Is.Not.Zero, "an item with a plain use");
        TestContext.WriteLine($"plain use item {plain}: {t.Items.Get(plain).FunctionName}");
        int tab = plain / 10000000;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(tab, new[]
        {
            new EquipItem { Id = plain, Uid = 20, InventorySlot = 0, Amount = 3, Rarity = 1 },
            new EquipItem { Id = plain, Uid = 21, InventorySlot = 1, Amount = 1, Rarity = 1, ExpiryTime = 500 },
            new EquipItem { Id = 20300327, Uid = 22, InventorySlot = 2, Amount = 1, Rarity = 1 },                    // OpenItemBox
            new EquipItem { Id = plain, Uid = 23, InventorySlot = 3, Amount = 1, Rarity = 1, Transfer = { Rule = 8 } }, // binds on use
            new EquipItem { Id = plain, Uid = 24, InventorySlot = 4, Amount = 1, Rarity = 1, ExpiryTime = 1000 },     // expires now
        }));
        var (h, c, _) = Load(model, book);
        c.View.ServerTime = () => 1000;
        var record = new PcInfo { Level = 50, JobGroup = 10, Equips = Array.Empty<EquipItem>() };
        c.Wearer = () => record;
        int shown = c.Tabs.Select((x, i) => (x, i)).First(x => x.x.Tab == tab).i;
        h.Set(h.Child("m_tab"), "selectedIndex", shown);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var sent = new List<(long, string)>(); c.View.RequestUse = (u, a) => sent.Add((u, a));
        var raised = new List<string>(); c.Button += raised.Add;
        var boxes = new List<string>(); var shownBoxes = new List<MessageBoxData>();
        c.ShowBox = b => { boxes.Add(b.Msg); shownBoxes.Add(b); };

        Assert.That(c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0" }), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { (20L, "") }), "the uid and an empty argument");
        Assert.That(c.View.ItemAt(c.View.Tab, 0)?.Amount, Is.EqualTo(3), "nothing is used up until the server says so");
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0", 0 });
        Assert.That(sent, Has.Count.EqualTo(2), "a repeat goes through, as in the client");
        c.ShiftHeld = () => true;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0" });
        c.ShiftHeld = () => false;
        Assert.That(sent, Has.Count.EqualTo(2), "Shift held: nothing");
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1" });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((2, t.Common(InventoryController.ExpiredKey))), "expired: box 225");
        record.JobGroup = 0;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0" });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((2, t.Common(InventoryController.DisabledJobKey))), "job 0: s_item_err_disable_job");
        record.JobGroup = 10;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.4" });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((2, t.Common(InventoryController.ExpiredKey))), "IsExpired is expiry <= now");
        model.Player.Name = "Tester";
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.3" });
        Assert.That(sent.Count, Is.EqualTo(2), "binds on use: the box first, no send");
        var bind = shownBoxes.Last();
        Assert.That((bind.Type, bind.Extra, bind.ItemUid, bind.Args.Count), Is.EqualTo((0x11, InventoryController.BindOnUseBox, 23L, 0)));
        Assert.That(bind.Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(t.Common(InventoryController.BindOnUseKey), "Tester")));
        Assert.That(bind.ItemEntry?.Item?.Uid, Is.EqualTo(23L));
        Assert.That(c.ConfirmBindUse(23), Is.True);
        Assert.That(sent.Last(), Is.EqualTo((23L, "")), "the yes uses it with bConfirmed: sent");
        Assert.That(shownBoxes.Last(), Is.SameAs(bind), "no second box");
        Assert.That(c.ConfirmBindUse(99), Is.False, "an item the inventory no longer has: the handler's 0");
        var notices = new List<string>(); c.SystemNotice += notices.Add;
        record.Level = 0;
        Assert.That(c.ConfirmBindUse(23), Is.True);
        record.Level = 50;
        Assert.That((sent.Count, notices.LastOrDefault()), Is.EqualTo((3, InventoryController.UseLevelKey)),
            "CanUseItem again on the yes: its level check is a system notice (unit_140d1ae30:461)");
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.2" });
        Assert.That((sent.Count, raised.Last()), Is.EqualTo((3, "notifySlotUse")), "a function with its own handler: raised");
        c.LocalPlayerDead = () => true;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0" });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((3, t.Common(MyInfoController.DeadKey))), "dead: box 66, no send (inventory.cpp:1211)");
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1" });
        Assert.That((sent.Count, boxes.Last()), Is.EqualTo((3, t.Common(InventoryController.ExpiredKey))),
            "CanUseItem's expiry (:430) comes before RequestUseItem's dead check");
        int raisedCount = raised.Count;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.2" });
        Assert.That(raised.Count, Is.EqualTo(raisedCount + 1), "dead: an OpenItemBox item still reaches its own handler");
        var box = c.FindItem(c.View.ItemAt(c.View.Tab, 2).Uid);
        Assert.That((c.RequestUseItem(box, "x"), sent.Last()), Is.EqualTo((true, (box.Uid, "x"))),
            "dead: RequestUseItem skips the dead check for an OpenItemBox function (checkState 0)");
        Assert.That(InventoryController.NoStateCheckFunctions, Is.EquivalentTo(new[] { "OpenItemBox", "OpenItemBoxLullu", "OpenItemBoxLulluSimple" }),
            "RequestUseItem's checkState 0 functions (inventory.cpp:1205-1207)");
        c.LocalPlayerDead = () => false;
        // the window closed while the box is up: its yes still uses the item (the client's goes through the global
        // inventory, uimessagebox.cpp:1868; the HUD keeps this controller for it)
        h.Unload();
        Assert.That(c.ConfirmBindUse(23), Is.True);
        Assert.That((sent.Count, sent.Last()), Is.EqualTo((5, (23L, ""))), "the yes after the window closed: sent");
    }

    [Test]
    public void ThePreviewActionIsOnTheShiftKeys()
    {
        var keys = new KeyTable(Packs(), "en");
        keys.LoadDefaultKeys();
        Assert.That(keys.KeysOfAction(0x49406).OrderBy(k => k), Is.EqualTo(new[] { 42, 54 }), "defaultkey.xml: DIK_LSHIFT, DIK_RSHIFT");
        Assert.That(keys.KeysOfAction(0), Is.Empty);
    }

    /// <summary>
    /// The inventory closed while an item tooltip shows: the dialog's movie leaves the stage on unload (UISwfEntity::Unload,
    /// uiswfentity.cpp:241, unloads the Loader that holds it), and the removedFromStage down its display tree makes the
    /// movie's TooltipManager destroy every tooltip of the removed elements (appendMouseEvent's REMOVED_FROM_STAGE
    /// listener → handleRemovedFromStage → destroyTooltipByOwner), so the run's tooltip movie holds none of them.
    /// </summary>
    [Test]
    public void ClosingTheWindowDestroysTheItemTooltip()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(1, new[] { new EquipItem { Id = 15300001, Uid = 11, InventorySlot = 0, Amount = 1, Rarity = 1 } }));
        // the run's tooltip movie (GFxManager::FindMovie(5))
        var tc = new TooltipController();
        var (trt, tp) = src.Open(tc.Movie, r => r.Budget = 20_000_000);
        tp.StageW = 1280; tp.StageH = 720;
        var th = new UiHost(tp, tc, NewModel());
        Assert.That(th.Load(), Is.True, "tooltip movie: " + string.Join("; ", trt.Errors));
        for (int i = 0; i < 5; i++) tp.Tick();
        UiHost.Tooltips = tc;
        try
        {
            var (h, c, _) = Load(model, book);
            h.Set(h.Child("m_tab"), "selectedIndex", c.Tabs.Select((t, i) => (t, i)).First(x => x.t.Tab == 1).i);
            for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
            Assert.That(c.GetSlotTooltip("InventoryDialog.0")["tooltip"], Is.EqualTo("export_common_tooltip_kindsItem"), "the slot has an item tooltip");
            // hover the slot as the HUD's shot does (HudScene.DragStep: SlotPoint finds the display object by its binding)
            AsDisplayObject Slot()
            {
                var queue = new Queue<AsDisplayObject>();
                foreach (var k in (h.Player.Stage as AsContainer).Children) queue.Enqueue(k);
                while (queue.Count > 0)
                {
                    var d = queue.Dequeue();
                    if (d != h.Player.Stage && Rt.ToString(h.Get(d, "binding")) == "InventoryDialog.0") return d;
                    if (d is AsContainer cc) foreach (var k in cc.Children) queue.Enqueue(k);
                }
                return null;
            }
            var slot = Slot();
            Assert.That(slot, Is.Not.Null, "the slot bound InventoryDialog.0");
            var b = slot.GetBounds(null);
            (double, double)? pt = null;
            for (double y = b.Y; y < b.Y + b.H && pt == null; y += 2)
                for (double x = b.X; x < b.X + b.W && pt == null; x += 2)
                    for (var t = (AsDisplayObject)h.Player.MouseTarget(x, y); t != null; t = t.Parent)
                        if (t == slot) { pt = (x, y); break; }
            Assert.That(pt, Is.Not.Null, "a point on the slot");
            h.Player.MouseMove(pt.Value.Item1, pt.Value.Item2);
            for (int i = 0; i < 3; i++) { h.Player.Tick(); tp.Tick(); }
            Assert.That(tc.Open, Is.Not.Empty, "the hovered item's tooltip is shown");
            h.Unload();      // the HUD's close: Host.Unload once the dialog is unloaded
            Assert.That(tc.Open, Is.Empty, "the movie leaving the stage destroyed its tooltips");
        }
        finally { UiHost.Tooltips = null; }
    }

    [Test]
    public void AWornItemDroppedOnTheBagSwapsOrTakesItOff()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var t = model.Tables;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(1, new[]
        {
            new EquipItem { Id = 13000001, Uid = 30, InventorySlot = 0, Amount = 1, Rarity = 1 },
            new EquipItem { Id = 11400001, Uid = 31, InventorySlot = 1, Amount = 1, Rarity = 1 },
        }));
        var (h, c, _) = Load(model, book);
        c.View.ServerTime = () => 1000;
        c.Wearer = () => new PcInfo { Level = 50, JobGroup = 10, Equips = Array.Empty<EquipItem>() };
        var wornRecord = new EquipItem { Id = 13000001, Uid = 2, Slot = EquipSlot.RH, Rarity = 1 };
        var worn = InventoryController.ToItem(wornRecord, t.Items);
        int group = t.ItemModels.Get(worn.Data.ItemPreset).FirstSlotType;
        var equips = new EquipSlots { Worn = n => n == "MyPcInfoDialog.Equip.5" ? (worn, (int)wornRecord.Slot, 0) : null };
        var unequipped = equips.Unequipped;
        h.DragSlots.Add(equips);
        h.DragSlots.Add(new OtherSlots());
        var equipped = new List<(long, byte, byte)>(); c.View.RequestEquip = (u, s, m) => equipped.Add((u, s, m));
        var raised = new List<string>(); c.Button += raised.Add;
        int gear = c.Tabs.Select((x, i) => (x, i)).First(x => x.x.Tab == 1).i;
        h.Set(h.Child("m_tab"), "selectedIndex", gear);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }

        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "MyPcInfoDialog.Equip.5", "InventoryDialog.0" });
        Assert.That(equipped, Is.EqualTo(new[] { (30L, (byte)group, (byte)0) }), "the cell's item has the worn item's group: put on at that slot, the bag's kind");
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "MyPcInfoDialog.Equip.5", "InventoryDialog.1" });
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "MyPcInfoDialog.Equip.5", "InventoryDialog.7" });
        Assert.That((equipped.Count, unequipped), Is.EqualTo((1, new List<long> { 2, 2 })), "another item or an empty cell: taken off");
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "MyPcInfoDialog.Equip.4", "InventoryDialog.0" });
        Assert.That((equipped.Count, unequipped.Count, raised.Count), Is.EqualTo((1, 2, 0)), "an empty equip slot: nothing");
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "QuickSlot.1", "InventoryDialog.7" });
        Assert.That((unequipped.Count, raised.Count(r => r == "notifySlotDragEnd")), Is.EqualTo((2, 1)), "another kind of slot: not ported, raised");
        int other = c.Tabs.Select((x, i) => (x, i)).First(x => x.x.Tab == 3).i;
        h.Set(h.Child("m_tab"), "selectedIndex", other);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "MyPcInfoDialog.Equip.5", "InventoryDialog.0" });
        Assert.That(unequipped.Count, Is.EqualTo(2), "another tab shown: nothing");
    }

    /// <summary>A character window's equip slots, "MyPcInfoDialog.Equip.N", with the worn items given.</summary>
    sealed class EquipSlots : IEquipSlotOwner
    {
        public Func<string, (Item Item, int Slot, int Kind)?> Worn = _ => null;
        public readonly List<long> Unequipped = new();
        public bool IsDragSlot(string name) => IsEquipSlot(name);
        public bool IsEquipSlot(string name) => name.StartsWith("MyPcInfoDialog.Equip.", StringComparison.Ordinal);
        public (Item Item, int Slot, int Kind)? WornSlot(string name) => Worn(name);
        public void RequestUnequip(Item item) => Unequipped.Add(item.Uid);
        public void RefreshSlot(string name) { }
        public void SlotDropUnresolved(string name) { }
    }

    /// <summary>A drag slot of another kind ("QuickSlot.1").</summary>
    sealed class OtherSlots : IDragSlotOwner
    {
        public bool IsDragSlot(string name) => name == "QuickSlot.1";
        public void RefreshSlot(string name) { }
        public void SlotDropUnresolved(string name) { }
    }

    [Test]
    public void AnOpenItemBoxAsksThenOpensOnlyOnTheServerAnswer()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var d = model.Tables.Items.Get(20300327);
        var saved = d.FunctionArgs.ToList();
        try
        {
            d.FunctionArgs.Clear(); d.FunctionArgs.AddRange(new[] { "0", "0", "0", "0", "1" });
            var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
            book.Apply(new ItemMessage.LoadTab(2, new[]
            {
                new EquipItem { Id = 20300327, Uid = 30, InventorySlot = 0, Amount = 5, Rarity = 1 },
            }));
            var (h, c, _) = Load(model, book);
            var record = new PcInfo { Level = 50, JobGroup = 10, Equips = Array.Empty<EquipItem>() };
            c.Wearer = () => record;
            var sent = new List<(long, string)>(); c.View.RequestUse = (u, a) => sent.Add((u, a));
            var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
            var multi = new List<(int, int, string, int)>(); c.OpenMultiBox = (id, r, a, n) => multi.Add((id, r, a, n));
            var item = c.FindItem(30);

            Assert.That(c.UseItem(item, true), Is.True);
            Assert.That((boxes.Count, boxes[0].Type, sent.Count), Is.EqualTo((1, 0x1a, 0)), "the confirm box, nothing sent yet");
            Assert.That(boxes[0].ItemUid, Is.EqualTo(30L));
            boxes[0].Callback(1, boxes[0]);
            Assert.That((boxes.Count, sent.Count), Is.EqualTo((1, 0)), "the other button does nothing");
            boxes[0].Callback(0, boxes[0]);
            Assert.That((boxes.Count, boxes[1].Type, sent.Count), Is.EqualTo((2, 0x29, 0)), "five in the bag: the count box, still nothing sent");
            boxes[1].ClickedTag = "x,1";
            boxes[1].Callback(0, boxes[1]);
            Assert.That(sent, Is.EqualTo(new[] { (30L, "") }), "a count of 1 is the plain use");
            boxes[1].ClickedTag = "x,3";
            boxes[1].Callback(0, boxes[1]);
            Assert.That((multi, sent.Count), Is.EqualTo((new[] { (20300327, 1, "", 3) }, 1)), "more than one: the multi open window, no send");
            boxes[1].ClickedTag = "x,5000";
            boxes[1].Callback(0, boxes[1]);
            Assert.That(multi.Last().Item4, Is.EqualTo(model.Tables.ItemBoxMultiOpenLimitCount), "the count is clamped to the limit");
            boxes[1].ClickedTag = "3";
            boxes[1].Callback(0, boxes[1]);
            Assert.That(multi.Count, Is.EqualTo(2), "a tag of one field is ignored");

            d.FunctionArgs.Clear(); d.FunctionArgs.AddRange(new[] { "0", "0", "0", "0", "9" });
            int before = boxes.Count;
            Assert.That(c.UseItem(item, true), Is.False);
            Assert.That((boxes.Count, boxes.Last().Type, sent.Count), Is.EqualTo((before + 1, 0, 1)), "more than the bag holds: a notice box, nothing sent");
        }
        finally { d.FunctionArgs.Clear(); d.FunctionArgs.AddRange(saved); }
    }

    [Test]
    public void ASelectItemBoxOpensItsDialogOnce()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        int id = Enumerable.Range(20300000, 3000).FirstOrDefault(i => model.Tables.Items.Get(i)?.FunctionName == "SelectItemBox");
        if (id == 0) Assert.Ignore("no SelectItemBox item in this data");
        book.Apply(new ItemMessage.LoadTab(2, new[] { new EquipItem { Id = id, Uid = 31, InventorySlot = 0, Amount = 1, Rarity = 1 } }));
        var (h, c, _) = Load(model, book);
        var opened = new List<long>(); bool open = false;
        c.OpenSelectBox = i => { opened.Add(i.Uid); open = true; };
        c.SelectBoxOpen = () => open;
        var sent = new List<(long, string)>(); c.View.RequestUse = (u, a) => sent.Add((u, a));
        c.UseItem(c.FindItem(31), true);
        c.UseItem(c.FindItem(31), true);
        Assert.That((opened, sent.Count), Is.EqualTo((new[] { 31L }, 0)), "opened once, nothing sent by the open");
    }
}
