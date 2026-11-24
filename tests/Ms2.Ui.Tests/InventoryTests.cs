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
    public void ShowTabSelectsTheTabsButtonAsAClickDoes()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var (h, c, _) = Load(model);
        var (name, tab) = c.Tabs.First(t => t.Name == "material");
        Assert.That(c.ShowTab(tab), Is.True, "the view shows the material tab");
        Assert.That(c.View.Tab, Is.EqualTo(tab));
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_tab"), "selectedIndex")), Is.EqualTo(c.Tabs.ToList().IndexOf((name, tab))), "the bar's button");
        Assert.That(c.ShowTab(999), Is.False, "no such tab");
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

    /// <summary>The slot list: the KMS movie's m_itemBagEx (a TileList), the GMS2 movie's m_itemBag (an ItemBag).</summary>
    static object SlotList(UiHost h) => h.Child("m_itemBagEx") ?? h.Child("m_itemBag");

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
        var names = Items(h, h.Get(SlotList(h), "dataProvider")).Select(Rt.ToString).ToList();
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
        Assert.That((int)Rt.ToNumber(h.Get(h.Get(SlotList(h), "dataProvider"), "length")), Is.EqualTo(54));
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
        Assert.That(raised, Is.EqualTo(new[] { "onClickItemRemakeOption", "onClickOpenBank" }), "the expand is the dialog's own; the two unhandled ones raise nothing");
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
    public void TheWornWeaponMaterialIsTheUsableRightHandItemsAttackMaterial()
    {
        var t = NewModel().Tables;
        EquipItem E(int id, long uid, EquipSlot slot) => new() { Id = id, Uid = uid, Slot = slot, Rarity = 1 };
        Assert.That(t.Items.Get(13000001).AttackMaterial, Is.EqualTo(6), "<material ui=\"6\" attack=\"6\"/>");
        Assert.That(t.Items.Get(14000001).AttackMaterial, Is.Zero, "no attack attribute");
        var worn = WornEquipment.Of(new PcInfo { Equips = new[] { E(13000001, 2, EquipSlot.RH), E(14000001, 3, EquipSlot.LH) } }, t.Items);
        Assert.That(worn.WeaponMaterial(_ => true), Is.EqualTo(6));
        Assert.That(worn.WeaponMaterial(_ => false), Is.Zero, "an unusable weapon");
        var leftOnly = WornEquipment.Of(new PcInfo { Equips = new[] { E(13000001, 3, EquipSlot.LH) } }, t.Items);
        Assert.That(leftOnly.WeaponMaterial(_ => true), Is.Zero, "only the RH slot");
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
        Assert.That((sent.Count, raised), Is.EqualTo((1, new List<string>())), "a drop off the bag over the UI: the item returns (the drop on the ground is SlotDropUnresolved's)");
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
    public void AnEmotionItemAsksFirstAndItsOkSendsTheLearn()
    {
        // InvenSlot::vfunc_13 (invenslot.cpp:873-884): an emotion item (type 24) passes FUN_14140dea0, then box 1 with
        // FormatStringId(0x85a, the skill's name), tag "learn_emotion" and the item uid; nothing is sent until its OK
        // (HandleLearnEmotionButton, uimessagebox.cpp:1451-1466), which runs Unk_Inventory::SendEmotion (inventory.cpp:1615)
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var t = model.Tables;
        var data = t.Items.Get(20200001);
        if (data?.Type != Ms2.Ui.EmotionList.EmotionItemType) Assert.Inconclusive("this data has no type 24 item 20200001 (GMS2 data: type 2)");
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(2, new[]
        {
            new EquipItem { Id = 20200001, Uid = 30, InventorySlot = 0, Amount = 1, Rarity = 1 },
            new EquipItem { Id = 20200001, Uid = 31, InventorySlot = 1, Amount = 1, Rarity = 1, ExpiryTime = 500 },
        }));
        var (h, c, _) = Load(model, book);
        c.View.ServerTime = () => 1000;
        int tab = c.Tabs.Select((x, i) => (x, i)).First(x => x.x.Tab == 2).i;
        h.Set(h.Child("m_tab"), "selectedIndex", tab);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var raised = new List<string>(); c.Button += raised.Add;
        var shown = new List<MessageBoxData>(); c.ShowBox = shown.Add;
        var sent = new List<Ms2.Net.Packets.EmoteLearnRequest>(); c.SendEmoteLearn = sent.Add;
        var equips = new List<long>(); c.View.RequestEquip = (u, _, _) => equips.Add(u);

        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0" });
        var box = shown.Single();
        Assert.That((box.Type, box.Extra, box.ItemUid), Is.EqualTo((1, InventoryController.LearnEmotionTag, 30L)));
        Assert.That(box.Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(t.Common(0x85a), t.SkillName((uint)data.SkillId))));
        Assert.That(t.Common(0x85a), Is.Not.Empty);
        Assert.That((sent.Count, raised.Count, equips.Count), Is.EqualTo((0, 0, 0)), "the box first: nothing sent or raised");
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1" });
        Assert.That(shown.Last().Msg, Is.EqualTo(t.Common(InventoryController.ExpiredKey)), "FUN_14140dea0: the expired box, no learn box");

        // the box's OK through the message box layer's tag handler
        var boxes = new MessageBoxController();
        boxes.LearnEmotion = c.ConfirmLearnEmotion;
        boxes.data = box;
        Assert.That(boxes.HandlePlainTag(1, InventoryController.LearnEmotionTag), Is.True);
        Assert.That(sent, Is.Empty, "cancel sends nothing");
        Assert.That(boxes.HandlePlainTag(0, InventoryController.LearnEmotionTag), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { new Ms2.Net.Packets.EmoteLearnRequest(30) }));

        // learned already: box 2136, nothing sent; an item the bag no longer has, or no local player: nothing
        c.Emotions = new Ms2.Ui.EmotionList();
        c.Emotions.Apply(new EmoteMessage.List(new[] { new EmoteEntry(data.SkillId, (ushort)data.SkillLevel, true, 0, 0, false) }), 0);
        c.ConfirmLearnEmotion(30);
        Assert.That(shown.Last().Msg, Is.EqualTo(t.Common(Ms2.Ui.EmotionList.AlreadyLearnedString)));
        int boxCount = shown.Count;
        c.ConfirmLearnEmotion(99);
        model.HasLocalPlayer = false;
        c.ConfirmLearnEmotion(30);
        Assert.That((sent.Count, shown.Count), Is.EqualTo((1, boxCount)));
    }

    [Test]
    public void AStickerPackItemAsksWithItsPeriodAndItsYesSendsTheUse()
    {
        // ChatEmoticonAdd @0x140d1fba0 (unit_140d1ae30_itemfunctiondispatch.cpp:1027-1064); the yes is UIMessageBox::FUN_1419fc810
        // (uimessagebox.cpp:1527-1567): FindItemByUid, FUN_140a63880(item, "")
        var packs = Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var t = model.Tables;
        int id = EmotionCastTests.ItemIds(packs).FirstOrDefault(i => t.Items.Get(i)?.FunctionName == "ChatEmoticonAdd");
        if (id == 0) Assert.Inconclusive("no ChatEmoticonAdd item in this data");
        var book = new ItemBook { TabOf = i => i / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(id / 10000000, new[] { new EquipItem { Id = id, Uid = 51, InventorySlot = 0, Amount = 1, Rarity = 1 } }));
        var (h, c, _) = Load(model, book);
        var shown = new List<MessageBoxData>(); c.ShowBox = shown.Add;
        var sent = new List<(long, string)>(); c.View.RequestUse = (u, a) => sent.Add((u, a));
        var raised = new List<string>(); c.Button += raised.Add;

        var item = c.FindItem(51);
        Assert.That(c.UseItem(item, true), Is.True);
        Assert.That((sent.Count, raised.Count), Is.EqualTo((0, 0)), "the box asks first; not raised as unported");
        var box = shown.Single();
        Assert.That((box.Type, box.ItemUid, box.Extra), Is.EqualTo((0x11, 51L, InventoryController.UseFunctionItemTag)));
        Assert.That(box.ItemEntry?.Item?.ItemId, Is.EqualTo(id), "SetItemById: the item's id and rarity");
        Assert.That(box.ItemEntry.Count, Is.Zero);
        string name = new ItemTooltip(model, 0).Name(item);
        Assert.That(box.Msg, Does.StartWith(Ms2.Quest.ClientFormat.Format(t.Common(InventoryController.ChatEmoticonUseKey), name)));
        Assert.That(box.Msg, Does.EndWith(t.Common(InventoryController.ChatEmoticonWarningKey)));
        Assert.That(t.Common(InventoryController.ChatEmoticonUseKey), Is.Not.Empty);

        var boxes = new MessageBoxController { UseFunctionItem = c.ConfirmUseFunctionItem, data = box };
        Assert.That(boxes.HandlePlainTag(1, InventoryController.UseFunctionItemTag), Is.True);
        Assert.That(sent, Is.Empty, "no sends nothing");
        Assert.That(boxes.HandlePlainTag(0, InventoryController.UseFunctionItemTag), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { (51L, "") }));
        box.Append(1);
        Assert.That(boxes.HandlePlainTag(0, InventoryController.UseFunctionItemTag), Is.True);
        c.ConfirmUseFunctionItem(99);
        Assert.That(sent.Count, Is.EqualTo(1), "the ask argument's box (not ported) and an item no longer in the bag send nothing");
    }

    [Test]
    public void TheStickerEndDateIsReadAsTheClientsScan()
    {
        // FUN_14028e280 (commonutil.cpp:922-938) with "%04d-%02d-%02d-%02d-%02d-%02d"
        Assert.That(InventoryController.ParseDate("2021-12-31-23-59-00"), Is.EqualTo(new DateTime(2021, 12, 31, 23, 59, 0)));
        Assert.That(InventoryController.ParseDate(" 2021-1-2-3-4-5"), Is.EqualTo(new DateTime(2021, 1, 2, 3, 4, 5)));
        Assert.That(InventoryController.ParseDate("2021-02-31-00-00-00"), Is.EqualTo(new DateTime(2021, 3, 3)), "mktime carries the day");
        Assert.That(InventoryController.ParseDate("2021-12-31"), Is.EqualTo(new DateTime(2021, 12, 31)), "fields not read stay 0");
        Assert.That(InventoryController.ParseDate("2021-12"), Is.Null, "day 0");
        Assert.That(InventoryController.ParseDate("1899-12-31-00-00-00"), Is.Null);
        Assert.That(InventoryController.ParseDate("2021-13-01-00-00-00"), Is.Null);
        Assert.That(InventoryController.ParseDate("2021-12-31-24-00-00"), Is.Null);
        Assert.That(InventoryController.ParseDate("2021/12/31"), Is.Null, "the dash must match: month 0");
        Assert.That(InventoryController.ParseDate("20211-12-31-00-00-00"), Is.Null, "%04d stops at 4 digits; the next char is no dash");
        Assert.That(InventoryController.ParseDate(""), Is.Null);
    }

    /// <summary>Every ChatEmoticonAdd item's function values: an endDate the client reads, a durationSec, or neither; an
    /// endDate it cannot read ends the use with no box, so those are listed. Its id names a sticker pack (group) of the
    /// emoticon table with stickers in it.</summary>
    static void EveryStickerItemHasItsPeriod(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var t = new GameTables(packs, "en");
        int end = 0, duration = 0, neither = 0;
        var bad = new List<int>();
        var noGroup = new List<int>();
        string sample = null;
        var table = t.ChatEmoticonData;
        foreach (int id in EmotionCastTests.ItemIds(packs))
        {
            var d = t.Items.Get(id);
            if (d?.FunctionName != "ChatEmoticonAdd") continue;
            sample ??= d.FunctionParameter;
            if (table.GetEmoticonsByGroup((uint)d.FunctionXmlInt("id")).Count == 0) noGroup.Add(id);
            if (d.HasFunctionXmlKey("endDate")) { if (InventoryController.ParseDate(d.FunctionXmlString("endDate")) == null) bad.Add(id); else end++; }
            else if (d.HasFunctionXmlKey("durationSec")) duration++;
            else neither++;
        }
        TestContext.Out.WriteLine($"{variable}: ChatEmoticonAdd items: {end} endDate, {duration} durationSec, {neither} neither, " +
            $"{bad.Count} unreadable endDate [{string.Join(",", bad)}]; {noGroup.Count} without stickers [{string.Join(",", noGroup)}]; sample {sample}");
        Assert.That(end + duration + neither + bad.Count, Is.GreaterThan(0));
        Assert.That(noGroup, Is.Empty, "each pack item's group has stickers");
    }

    [Test]
    public void EveryKmsStickerItemHasItsPeriod() => EveryStickerItemHasItsPeriod("MS2_DATA");

    [Test]
    public void EveryGms2StickerItemHasItsPeriod() => EveryStickerItemHasItsPeriod("MS2_GMS2_DATA");

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
        Assert.That(raised.Count(r => r == "notifySlotUse"), Is.EqualTo(1), "the preview is raised; the double click of an equipment item with no shop open does nothing (invenslot.cpp:886)");
        model.HasLocalPlayer = false;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        Assert.That(sent.Count, Is.EqualTo(count), "no local player");
    }

    [Test]
    public void AnEquipmentItemsDoubleClickPutsItOnOnlyWhileTheShopIsOpen()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var t = model.Tables;
        // an equipment item (type 1) without a function, a shop id or a pet
        Assert.That(t.Items.Get(11400001)?.Type, Is.EqualTo(1));
        Assert.That(t.Items.Get(11400001).HasFunction, Is.False);
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(1, new[] { new EquipItem { Id = 11400001, Uid = 11, InventorySlot = 0, Amount = 1, Rarity = 1 } }));
        var (h, c, _) = Load(model, book);
        c.View.ServerTime = () => 1000;
        c.Wearer = () => new PcInfo { Level = 50, JobGroup = 10, Equips = Array.Empty<EquipItem>() };
        h.Set(h.Child("m_tab"), "selectedIndex", c.Tabs.Select((x, i) => (x, i)).First(x => x.x.Tab == 1).i);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var sent = new List<(long, byte, byte)>(); c.View.RequestEquip = (u, s, m) => sent.Add((u, s, m));
        var raised = new List<string>(); c.Button += raised.Add;

        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0", 0 });
        Assert.That((sent.Count, raised.Count), Is.EqualTo((0, 0)), "no shop dialog: nothing, not raised (invenslot.cpp:886-893)");
        c.Shop = () => new Ms2.Ui.Shop.ShopController();
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0", 0 });
        Assert.That(sent.Select(x => x.Item1), Is.EqualTo(new[] { 11L }), "the shop dialog is open: put on as by the right click");
        Assert.That(raised.Count, Is.EqualTo(0));
    }

    [Test]
    public void ARideItemDoubleClickGoesToItsItemSkill()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var t = model.Tables;
        int ride = new[] { 50600000, 50200000, 50000000, 50100000, 50300000, 50400000, 50500000 }
            .SelectMany(b => Enumerable.Range(b, 1500))
            .FirstOrDefault(id => t.Items.Get(id) is { IsRideItem: true, HasFunction: false, LevelLimit: <= 1, GenderLimit: 2, JobLimit.Count: 0 });
        Assert.That(ride, Is.Not.Zero, "a ride item without a function");
        TestContext.WriteLine($"ride item {ride}: ridee {t.Items.Get(ride).RideeId}");
        int tab = ride / 10000000;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(tab, new[]
        {
            new EquipItem { Id = ride, Uid = 30, InventorySlot = 0, Amount = 1, Rarity = 1 },
            new EquipItem { Id = ride, Uid = 31, InventorySlot = 1, Amount = 1, Rarity = 1, ExpiryTime = 500 },
        }));
        var (h, c, _) = Load(model, book);
        c.View.ServerTime = () => 1000;
        c.Wearer = () => new PcInfo { Level = 50, JobGroup = 10, Equips = Array.Empty<EquipItem>() };
        int shown = c.Tabs.Select((x, i) => (x, i)).First(x => x.x.Tab == tab).i;
        h.Set(h.Child("m_tab"), "selectedIndex", shown);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var used = new List<long>(); var sent = new List<long>(); var raised = new List<string>(); var boxes = new List<string>();
        c.View.RequestUse = (u, _) => sent.Add(u);
        c.Button += raised.Add;
        c.ShowBox = b => boxes.Add(b.Msg);

        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0" });
        Assert.That((raised.Count, sent.Count), Is.EqualTo((1, 0)), "unset: raised, no use request");
        c.UseItemSkill = i => used.Add(i.Uid);
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.0" });
        Assert.That((used, raised.Count, sent.Count), Is.EqualTo((new List<long> { 30 }, 1, 0)),
            "FUN_140a635f0 with the item, not the use request (invenslot.cpp:894-897)");
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1" });
        Assert.That((used.Count, boxes.Last()), Is.EqualTo((1, t.Common(InventoryController.ExpiredKey))), "FUN_14140dea0 first: box 225");
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
    public void CallAirTaxiOpensTheWorldMapAndRecallPartyDoesNothingNeitherSends()
    {
        // CallAirTaxi (@0x140d24e10): OpenDialog(0x4e42) when closed, 1; RecallParty (@0x140d1df70): 1 and nothing
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var d = model.Tables.Items.Get(20300327);
        var (savedName, savedArgs) = (d.FunctionName, d.FunctionArgs.ToList());
        try
        {
            var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
            book.Apply(new ItemMessage.LoadTab(2, new[] { new EquipItem { Id = 20300327, Uid = 30, InventorySlot = 0, Amount = 1, Rarity = 1 } }));
            var (h, c, _) = Load(model, book);
            c.Wearer = () => new PcInfo { Level = 50, JobGroup = 10, Equips = Array.Empty<EquipItem>() };
            var sent = new List<(long, string)>(); c.View.RequestUse = (u, a) => sent.Add((u, a));
            var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
            int maps = 0; c.CallAirTaxi = () => maps++;
            var raised = new List<string>(); c.Button += raised.Add;                // an unported own-use function is raised
            d.FunctionName = "CallAirTaxi";
            Assert.That(c.UseItem(c.FindItem(30), true), Is.True);
            Assert.That((maps, sent.Count, boxes.Count, raised.Count), Is.EqualTo((1, 0, 0, 0)), "the world map opens, nothing sent");
            d.FunctionName = "RecallParty";
            Assert.That(c.UseItem(c.FindItem(30), true), Is.True);
            Assert.That((maps, sent.Count, boxes.Count, raised.Count), Is.EqualTo((1, 0, 0, 0)), "nothing at all, not raised as unported");
        }
        finally { d.FunctionName = savedName; d.FunctionArgs.Clear(); d.FunctionArgs.AddRange(savedArgs); }
    }

    [Test]
    public void RecallGuildGoesToItsOwnHandlerNotTheUnportedRaise()
    {
        // RecallGuild (@0x140d226b0): its checks and StateRecall; UseItem returns its result. Unset: raised as unported.
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var d = model.Tables.Items.Get(20300327);
        var (savedName, savedArgs) = (d.FunctionName, d.FunctionArgs.ToList());
        try
        {
            var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
            book.Apply(new ItemMessage.LoadTab(2, new[] { new EquipItem { Id = 20300327, Uid = 30, InventorySlot = 0, Amount = 1, Rarity = 1 } }));
            var (h, c, _) = Load(model, book);
            c.Wearer = () => new PcInfo { Level = 50, JobGroup = 10, Equips = Array.Empty<EquipItem>() };
            var sent = new List<(long, string)>(); c.View.RequestUse = (u, a) => sent.Add((u, a));
            var raised = new List<string>(); c.Button += raised.Add;
            var recalls = new List<long>(); bool answer = false;
            c.RecallGuild = item => { recalls.Add(item.Uid); return answer; };
            d.FunctionName = "RecallGuild";
            Assert.That(c.UseItem(c.FindItem(30), true), Is.False, "the handler's refusal is UseItem's result");
            answer = true;
            Assert.That(c.UseItem(c.FindItem(30), true), Is.True);
            Assert.That((recalls.Count, recalls[0], sent.Count, raised.Count), Is.EqualTo((2, 30L, 0, 0)), "no plain use, not raised");
            d.FunctionName = "RecallWedding";
            Assert.That(c.UseItem(c.FindItem(30), true), Is.True);
            Assert.That((recalls.Count, sent.Count, raised.Count), Is.EqualTo((2, 0, 1)), "the wedding recall stays raised");
        }
        finally { d.FunctionName = savedName; d.FunctionArgs.Clear(); d.FunctionArgs.AddRange(savedArgs); }
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

    [Test]
    public void ABlueprintWithAUgcUidAsksBeforeItIsOpened()
    {
        // BlueprintImport @0x140d1f6d0 (unit_140d1ae30_itemfunctiondispatch.cpp:982-1022): a ugc uid != 0 shows box 0x11 answered by FUN_140ee8000; uid 0 is not ported
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        int id = Enumerable.Range(20000000, 40000000).FirstOrDefault(i => model.Tables.Items.Get(i)?.FunctionName == "BlueprintImport");
        if (id == 0) Assert.Ignore("no BlueprintImport item in this data");
        book.Apply(new ItemMessage.LoadTab(id / 10000000, new[] {
            new EquipItem { Id = id, Uid = 41, InventorySlot = 0, Amount = 1, Rarity = 1, HasUgc = true, Ugc = new UgcLook { Uid = 5 } },
            new EquipItem { Id = id, Uid = 42, InventorySlot = 1, Amount = 1, Rarity = 1, HasUgc = true, Ugc = new UgcLook { Uid = 0 } } }));
        var (h, c, _) = Load(model, book);
        var shown = new List<MessageBoxData>(); c.ShowBox = shown.Add;
        var sent = new List<(long, string)>(); c.View.RequestUse = (u, a) => sent.Add((u, a));
        var raised = new List<string>(); c.Button += raised.Add;
        c.UseItem(c.FindItem(42), true);
        Assert.That((shown.Count, raised.Count), Is.EqualTo((0, 1)), "uid 0 stays with the own-use list while the bill is not wired");
        var answers = new List<(int, long)>();
        c.BlueprintOpenAnswer = (a, u) => { answers.Add((a, u)); return true; };
        c.UseItem(c.FindItem(42), true);
        Assert.That((shown.Count, raised.Count), Is.EqualTo((0, 2)), "uid 0: not ported, nothing opens");
        Assert.That(c.UseItem(c.FindItem(41), true), Is.True);
        Assert.That(sent, Is.Empty, "the box asks first");
        var box = shown.Single();
        Assert.That((box.Type, box.ItemUid, box.Extra), Is.EqualTo((0x11, 41L, InventoryController.UseFunctionItemTag)));
        Assert.That(box.EnterAllowed, Is.False);   // FUN_141a024a0(0) (:1015)
        box.InvokeCallback(0, box);
        Assert.That(answers, Is.EqualTo(new[] { (0, 41L) }));
    }

    // ---- the bag expand (vfunc_28 "onClickSlotExpand" :959, RequestExpandSlots :1874) and the answers (OnItemInventory :2973) ----

    [Test]
    public void TheExpandButtonAsksWithThePriceAndSendsOnlyWithTheMerets()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var (h, c, book) = Load(model);
        c.BaseSlots = _ => 48; c.MaxSlots = _ => 36;
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        var sent = new List<int>(); c.View.RequestExpand = sent.Add;
        int purchase = 0; c.MeretPurchase = () => purchase++;
        var raised = new List<string>(); c.Button += raised.Add;

        Assert.That(c.OnCallback("onClickSlotExpand", new object[] { "Equip" }), Is.True);
        Assert.That(raised, Is.Empty, "the dialog answers it itself");
        var box = boxes.Single();
        Assert.That((box.Type, box.Extra), Is.EqualTo((1, InventoryController.ExpandSlotBox)));
        Assert.That(box.Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(model.Tables.Common(0x266), model.Tables.InventoryExpandPrice1Row, "Equip", 6)));
        Assert.That(box.Msg, Does.Contain(model.Tables.InventoryExpandPrice1Row.ToString()), "the price is in the text");

        box.Callback(1, box);
        Assert.That((sent.Count, purchase), Is.EqualTo((0, 0)), "no: nothing");
        c.MeratBalance = () => model.Tables.InventoryExpandPrice1Row - 1;
        box.Callback(0, box);
        Assert.That((sent.Count, purchase), Is.EqualTo((0, 1)), "too few merets: the purchase dialog, nothing sent");
        c.MeratBalance = () => model.Tables.InventoryExpandPrice1Row;
        box.Callback(0, box);
        Assert.That(sent, Is.EqualTo(new[] { 0 }), "the shown tab");

        // the tab at its most extra slots: box BlockFormat(0x267), no ask
        book.Apply(new ItemMessage.ExpandCount(0, 36));
        boxes.Clear();
        c.OnCallback("onClickSlotExpand", new object[] { "Equip" });
        Assert.That((boxes.Single().Type, boxes.Single().Extra, boxes.Single().Msg), Is.EqualTo((0, "", model.Tables.Common(0x267))));
    }

    [Test]
    public void TheExpandPriceIsTheConstantsRow()
    {
        Packs();
        var t = NewModel().Tables;
        Assert.That(t.InventoryExpandPrice1Row, Is.GreaterThan(0));
        TestContext.WriteLine($"InventoryExpandPrice1Row {t.InventoryExpandPrice1Row}");
    }

    [Test]
    public void EveryAnswerHasTheClientsText()
    {
        Packs();
        var t = NewModel().Tables;
        int item = Enumerable.Range(20000000, 100000).First(i => !string.IsNullOrEmpty(t.ItemName(i)));
        foreach (int code in new[] { 0xc, 0xd, 0x1f, 0x23, 0x24, 0x25, 0x26, 0x28, 0x29, 0x2a, 17 })
        {
            var a = ItemInventoryAnswer.Error(code, item, 3);
            string text = InventoryController.AnswerText(a, t);
            TestContext.WriteLine($"0x{code:x}: {a.Kind} {text}");
            if (a.Kind == ItemAnswerKind.BoxKey)
            {
                // GetStringCommonByKey: GMS2's common strings have no s_item_err_Invalid_slot_codi, so its box is empty there
                Assert.That(text, Is.EqualTo(t.Common(a.Key) ?? ""), $"code 0x{code:x}");
                if (text.Length == 0) TestContext.WriteLine($"  this data has no {a.Key}");
                continue;
            }
            Assert.That(text, Is.Not.Empty, $"code 0x{code:x}");
            Assert.That(text, Does.Not.Contain("{"), $"code 0x{code:x} is formatted");
        }
        Assert.That(InventoryController.AnswerText(ItemInventoryAnswer.Error(0x28, item, 3), t), Does.Contain(t.ItemName(item)));
        Assert.That(InventoryController.AnswerText(ItemInventoryAnswer.Error(17, item, 3), t), Does.Contain("17"));
        Assert.That(InventoryController.AnswerText(ItemInventoryAnswer.ExpandDone, t), Is.EqualTo(t.Common(0x2a2)));
        Assert.That(InventoryController.AnswerText(ItemInventoryAnswer.Error(0x22, item, 3), t), Is.Empty, "the purchase dialog has no text");
        Assert.That(InventoryController.AnswerIsBox(ItemInventoryAnswer.Error(0xd, 0, 0)), Is.False);
        Assert.That(InventoryController.AnswerIsBox(ItemInventoryAnswer.Error(0x29, 0, 0)), Is.False);
        Assert.That(InventoryController.AnswerIsBox(ItemInventoryAnswer.Error(0x28, 0, 0)), Is.True);
    }

    // ---- an item dragged off the bag (SlotDragEnd's FUN_1422aeed0(2), InvenSlot::vfunc_17 / vfunc_26, FUN_14140c4d0) ----

    /// <summary>A stackable item of the data that no rule of vfunc_26 other than the transfer refuses, in tab 2.</summary>
    static int Droppable(UiModel model) => Enumerable.Range(20000000, 1000000).FirstOrDefault(i => model.Tables.Items.Get(i) is { } d
        && d.SlotMax > 1 && !d.IsPetItem && !d.IsScoreItem && !string.Equals(d.StringTag, InventoryController.PetExpTag, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(model.Tables.ItemName(i)));

    static (UiHost, InventoryController) DragBag(UiModel model, params EquipItem[] items) => DragBag(model, 48, items);

    static (UiHost, InventoryController) DragBag(UiModel model, int slots, params EquipItem[] items)
    {
        var book = new ItemBook { TabOf = i => i / 10000000, BaseSlots = _ => slots };
        book.Apply(new ItemMessage.LoadTab(2, items));
        var (h, c, _) = Load(model, book);
        Assert.That(c.ShowTab(2), Is.True);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        return (h, c);
    }

    static EquipItem Bag(int id, long uid, short slot, int amount, int rule = 4) =>
        new() { Id = id, Uid = uid, InventorySlot = slot, Amount = amount, Rarity = 1, Transfer = { Rule = rule } };

    [Test]
    public void ADragThatEndsOverTheUiReturnsTheItem()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        int id = Droppable(model);
        if (id == 0) Assert.Ignore("no stackable item in this data");
        var (h, c) = DragBag(model, Bag(id, 50, 0, 1));
        var drops = new List<(long, int)>(); c.View.RequestDrop = (u, n) => drops.Add((u, n));
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        c.CursorOverUi = () => true;
        c.SlotDropUnresolved("InventoryDialog.0");
        Assert.That((drops.Count, boxes.Count), Is.EqualTo((0, 0)));
        c.CursorOverUi = () => false;
        c.SlotDropUnresolved("InventoryDialog.0");
        Assert.That(drops, Is.EqualTo(new[] { (50L, 1) }), "over the world: one item goes at once");
        Assert.That(boxes, Is.Empty);
    }

    [Test]
    public void AStackDroppedOnTheGroundAsksTheCountAndTheBoxSendsIt()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        int id = Droppable(model);
        if (id == 0) Assert.Ignore("no stackable item in this data");
        var (h, c) = DragBag(model, Bag(id, 51, 0, 5));
        var drops = new List<(long, int)>(); c.View.RequestDrop = (u, n) => drops.Add((u, n));
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        var notices = new List<string>(); c.NoticeText += notices.Add;
        c.CursorOverUi = () => false;
        c.SlotDropUnresolved("InventoryDialog.0");
        Assert.That(drops, Is.Empty, "the box asks first");
        var box = boxes.Single();
        Assert.That((box.Type, box.Extra, box.ItemUid, box.InputType, box.InputDefault), Is.EqualTo((0xd, InventoryController.DropBox, 51L, 1, "5")));
        Assert.That(box.Msg, Is.EqualTo(model.Tables.Common("s_msg_item_drop")));
        Assert.That(box.InputMaxChars, Is.EqualTo(model.Tables.Items.Get(id).SlotMax.ToString().Length), "as many digits as the slot max");

        var mb = new MessageBoxController { DropItem = c.ConfirmDrop, data = box };
        Assert.That(mb.HandleArgTag(0, "drop,3"), Is.True);
        Assert.That(drops, Is.EqualTo(new[] { (51L, 3) }));
        mb.HandleArgTag(0, "drop,9");
        Assert.That(drops.Count, Is.EqualTo(1), "more than the stack: the count notice, nothing sent");
        Assert.That(notices, Has.Count.EqualTo(1));
        mb.HandleArgTag(1, "drop,2");
        Assert.That(drops.Count, Is.EqualTo(1), "the other button: nothing");
        mb.HandlePlainTag(0, "drop");
        Assert.That(drops.Last(), Is.EqualTo((51L, 1)), "the plain tag drops one");
    }

    [Test]
    public void AnItemThatCannotGoOnTheGroundAsksToDestroyIt()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        int id = Droppable(model);
        if (id == 0) Assert.Ignore("no stackable item in this data");
        var bound = Bag(id, 53, 1, 1);
        bound.Transfer.HasBinding = true; bound.Transfer.BindingId = 99; bound.Transfer.BindingName = "Owner";
        var (h, c) = DragBag(model, Bag(id, 52, 0, 2, rule: 0), bound);
        var drops = new List<(long, int)>(); c.View.RequestDrop = (u, n) => drops.Add((u, n));
        var destroyed = new List<long>(); c.View.RequestDestroy = destroyed.Add;
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        c.CursorOverUi = () => false;

        c.SlotDropUnresolved("InventoryDialog.0");
        var box = boxes.Single();
        Assert.That((box.Type, box.Extra, box.ItemUid, box.EnterAllowed), Is.EqualTo((0x11, InventoryController.DestroyBox, 52L, false)));
        Assert.That(box.Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(model.Tables.Common(0xd8), model.Tables.ItemName(id))), "no other reason: 0xd8 with the name");
        c.SlotDropUnresolved("InventoryDialog.1");
        Assert.That(boxes[1].Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(model.Tables.Common(0xd9), "Owner")), "bound: 0xd9 with the owner");
        Assert.That(drops, Is.Empty);

        var mb = new MessageBoxController { DestroyItem = c.ConfirmDestroy, data = boxes[1] };
        mb.HandlePlainTag(1, "destroy_item");
        Assert.That(destroyed, Is.Empty, "the other button: nothing");
        Assert.That(mb.HandlePlainTag(0, "destroy_item"), Is.True);
        Assert.That(destroyed, Is.EqualTo(new[] { 53L }));
    }

    [Test]
    public void TheDragOutRefusalsComeFirstInTheClientsOrder()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        int id = Droppable(model);
        if (id == 0) Assert.Ignore("no stackable item in this data");
        var (h, c) = DragBag(model, Bag(id, 54, 0, 1, rule: 0));
        var drops = new List<(long, int)>(); c.View.RequestDrop = (u, n) => drops.Add((u, n));
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        c.CursorOverUi = () => false;
        c.LocalPlayerDead = () => true;
        c.IsSummoned = _ => true;
        c.SlotDropUnresolved("InventoryDialog.0");
        Assert.That(boxes.Single().Msg, Is.EqualTo(model.Tables.Common(0x42)), "dead first");
        c.LocalPlayerDead = () => false;
        c.SlotDropUnresolved("InventoryDialog.0");
        Assert.That(boxes[1].Msg, Is.EqualTo(model.Tables.Common(0x71)), "a summoned item before the destroy box");
        c.IsSummoned = _ => false;
        c.RideMaidUid = () => 54;
        c.SlotDropUnresolved("InventoryDialog.0");
        Assert.That(boxes[2].Msg, Is.EqualTo(model.Tables.Common(0x71)), "the ride's maid");
        c.RideMaidUid = () => 0;
        c.SlotDropUnresolved("InventoryDialog.0");
        Assert.That(boxes[3].Extra, Is.EqualTo(InventoryController.DestroyBox));
        Assert.That(drops, Is.Empty);
    }

    // ---- the item search (OnItemSearch / Enter -> FUN_14199b800 -> FUN_14199c2b0, OnSearchTextChanged) ----

    /// <summary>Items of tab 2 (ids 20000000+) with names of their own, as many as asked; empty when the data has too few.</summary>
    static int[] Named(UiModel model, int n)
    {
        var t = new ItemTooltip(model, 0);
        var seen = new HashSet<string>(); var ids = new List<int>();
        for (int i = 20000000; i < 21000000 && ids.Count < n; i++)
        {
            if (model.Tables.Items.Get(i) is not { } d || d.IsPetItem || d.IsScoreItem) continue;
            string name = model.Tables.ItemName(i);
            if (string.IsNullOrEmpty(name) || name.Contains('?') || !seen.Add(name)) continue;
            ids.Add(i);
        }
        return ids.Count == n ? ids.ToArray() : Array.Empty<int>();
    }

    static string Name(UiModel model, InventoryController c, long uid) => new ItemTooltip(model, 0).Name(c.FindItem(uid));

    /// <summary>A-Z of a text upper-cased (what MakeLower undoes).</summary>
    static string AsciiUpper(string s) => new(s.Select(ch => ch >= 'a' && ch <= 'z' ? (char)(ch - 32) : ch).ToArray());

    static bool SearchOn(UiHost h, InventoryController c, int slot) => Rt.ToBoolean(h.Get(c.GetBinding(InventoryController.SlotName(slot)), "updateIconSearchOn"));

    [Test]
    public void TheSearchButtonScrollsToTheFirstSlotWithTheNameAndMarksEachOne()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var ids = Named(model, 2);
        if (ids.Length == 0) Assert.Ignore("too few named items in this data");
        var (h, c) = DragBag(model, 96, Bag(ids[1], 60, 0, 1), Bag(ids[0], 61, 43, 1), Bag(ids[0], 62, 31, 1));
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        string name = Name(model, c, 61);
        Assert.That(c.OnCallback("OnItemSearch", new object[] { AsciiUpper(name) }), Is.True);
        Assert.That(boxes, Is.Empty, "found: no box");
        Assert.That(c.SearchText, Is.EqualTo(Ms2.Formats.Text.JamoSearchText.Of(AsciiUpper(name))), "m_1d8 is the text form");
        Assert.That(Rt.ToNumber(h.Get(SlotList(h), "scrollPosition")), Is.EqualTo(5), "slot 31 is on row 31 / 6");
        Assert.That((SearchOn(h, c, 31), SearchOn(h, c, 43), SearchOn(h, c, 0)), Is.EqualTo((true, true, false)));
        c.ShowTab(0);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        Assert.That(c.SearchText, Is.Empty, "a bag rebuild clears the search");
    }

    [Test]
    public void ASearchWithNoSlotEmptiesTheInputAndSaysNoItemThenGivesTheFocusBack()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var ids = Named(model, 1);
        if (ids.Length == 0) Assert.Ignore("no named item in this data");
        var (h, c) = DragBag(model, Bag(ids[0], 63, 0, 1));
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        var input = h.Child("m_inputSearch");
        Assert.That(input, Is.Not.Null);
        h.Set(input, "text", "zzzz");
        c.OnCallback("OnItemSearch", new object[] { "zzzz" });
        var box = boxes.Single();
        Assert.That((box.Type, box.Msg), Is.EqualTo((0, model.Tables.Common(0x937))));
        Assert.That((Rt.ToString(h.Get(input, "text")), Rt.ToNumber(h.Get(input, "focused"))), Is.EqualTo(("", 0.0)));
        Assert.That(SearchOn(h, c, 0), Is.False);
        model.TopLayer = "other";
        box.Callback(0, box);
        Assert.That((model.TopLayer, Rt.ToNumber(h.Get(input, "focused"))), Is.EqualTo((h.Id, 1.0)), "OK: this dialog on top, the input focused");
        c.OnCallback("OnItemSearch", new object[] { "" });
        Assert.That(boxes.Count, Is.EqualTo(2), "an empty search finds no slot either");
    }

    [Test]
    public void TypingListsTheTabsMatchingNamesOnceAsCandidates()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var ids = Named(model, 2);
        if (ids.Length == 0) Assert.Ignore("too few named items in this data");
        var (h, c) = DragBag(model, Bag(ids[0], 64, 0, 1), Bag(ids[0], 65, 1, 1), Bag(ids[1], 66, 2, 1));
        string name = Name(model, c, 64);
        var input = h.Child("m_inputSearch");
        h.Set(input, "text", name + "  ");
        h.Set(input, "focused", 0);
        c.OnSearchTextChanged();
        Assert.That(Rt.IsNullish(h.Get(input, "m_candidate")), Is.True, "not focused: nothing");
        h.Set(input, "focused", 1);
        var change = h.RT.Construct(h.RT.GetClass("flash.events.Event"), new object[] { "change" });
        h.Invoke(input, "dispatchEvent", change);
        var dp = h.Get(input, "m_candidate");
        Assert.That(Rt.ToNumber(h.Get(dp, "length")), Is.EqualTo(1), "the trailing blanks trimmed; the name once");
        Assert.That(Rt.ToString(h.Get(h.Get(dp, "0"), "label")), Is.EqualTo(name));
        Assert.That((c.CandidateRows, Rt.ToNumber(h.Get(input, "m_rowCount"))), Is.EqualTo((1, 1.0)));
        Assert.That(c.FindMatchingItemNames(Ms2.Formats.Text.JamoSearchText.Of(name.Substring(0, 1))), Does.Contain(name), "a part of the name");
    }

    [Test]
    public void EnterWithTheInputFocusedSearchesAndTakesTheKey()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var ids = Named(model, 1);
        if (ids.Length == 0) Assert.Ignore("no named item in this data");
        var (h, c) = DragBag(model, Bag(ids[0], 67, 0, 1));
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        var input = h.Child("m_inputSearch");
        string name = Name(model, c, 67);
        h.Set(input, "text", name);
        h.Set(input, "focused", 0);
        Assert.That(c.EnterKey(), Is.False, "the input not focused: not taken");
        Assert.That(c.SearchText, Is.Empty);
        h.Set(input, "focused", 1);
        Assert.That(c.EnterKey(), Is.True);
        Assert.That((c.SearchText, boxes.Count, Rt.ToNumber(h.Get(input, "focused"))), Is.EqualTo((Ms2.Formats.Text.JamoSearchText.Of(name), 0, 0.0)));
        Assert.That(SearchOn(h, c, 0), Is.True);
    }

    // ---- the item sounds (PlayItemSound: the material sound of GetUiMaterialSound; the destroy's default sound) ----

    [Test]
    public void TheMaterialSoundIsTheTablesRowElseTheNoMaterialSound()
    {
        var items = NewModel().Tables.Items;
        Assert.That((items.UiMaterialSound(0), items.UiMaterialSound(-7)), Is.EqualTo(("09300001", "09300001")));
        var rows = items.UiMaterialRows;
        TestContext.WriteLine($"uiMaterialSound rows {rows.Count}: {string.Join(" ", rows.Select(r => $"{r.Key}={r.Value}"))}");
        Assert.That(rows, Is.Not.Empty, "Table/uiMaterialSound.xml has rows");
        foreach (var (id, sound) in rows) Assert.That(items.UiMaterialSound(id), Is.EqualTo(sound));
        int withMaterial = Enumerable.Range(20000000, 200000).Count(i => items.Get(i) is { UiMaterial: not 0 });
        TestContext.WriteLine($"items 20000000-20199999 with <material ui>: {withMaterial}");
        Assert.That(withMaterial, Is.GreaterThan(0), "the item files carry <material ui>");
        var d = Enumerable.Range(20000000, 200000).Select(items.Get).First(x => x is { UiMaterial: not 0 });
        Assert.That(items.MaterialSoundEvent(d), Is.EqualTo("ms2sound/sound/item/" + items.UiMaterialSound(d.UiMaterial)));
    }

    [Test]
    public void ABagMoveAndAUsePlayTheItemsSoundAndADestroyTheDefaultOne()
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        int id = Droppable(model);
        if (id == 0) Assert.Ignore("no stackable item in this data");
        var (h, c) = DragBag(model, Bag(id, 70, 0, 3));
        var sounds = new List<long>(); c.ItemSound += i => sounds.Add(i.Uid);
        int destroySounds = 0; c.DestroySound += () => destroySounds++;
        c.View.RequestMove = (_, _) => { };
        NotifyDragSlot.SlotDragEnd(h.DragSlots, new object[] { "InventoryDialog.0", "InventoryDialog.4" });
        Assert.That(sounds, Is.EqualTo(new[] { 70L }), "the move: PlayItemSoundByUid");
        var used = new List<long>(); c.View.RequestUse = (u, _) => used.Add(u);
        Assert.That(c.RequestUseItem(c.FindItem(70), ""), Is.True);
        Assert.That((used.Single(), sounds.Count), Is.EqualTo((70L, 2)), "the use: the material sound after the request");
        var destroyed = new List<long>(); c.View.RequestDestroy = destroyed.Add;
        c.ConfirmDestroy(70);
        Assert.That((destroyed.Single(), destroySounds, sounds.Count), Is.EqualTo((70L, 1, 2)), "the destroy: the default sound, not the item's");
    }

    // ---- the use handlers of RegisterItemUseFuncs (InventoryController.UseFunctions) ------------------------------------------

    /// <summary>The bag with the data's item of a function (the item function sweep's first id) at uid 50, slot 0, and the
    /// other items given; ignored when the data's item has another function.</summary>
    static (InventoryController C, UiModel Model, Item Item, List<(long, string)> Sent, List<MessageBoxData> Boxes, List<string> Raised)
        UseSetup(string function, int id, params EquipItem[] more)
    {
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        if (model.Tables.Items.Get(id)?.FunctionName != function) Assert.Ignore($"item {id} is not {function} in this data");
        var book = new ItemBook { TabOf = i => i / 10000000, BaseSlots = _ => 48 };
        var all = new List<EquipItem> { new() { Id = id, Uid = 50, InventorySlot = 0, Amount = 1, Rarity = 1 } };
        all.AddRange(more);
        foreach (var g in all.GroupBy(e => e.Id / 10000000)) book.Apply(new ItemMessage.LoadTab(g.Key, g.ToArray()));
        var (_, c, _) = Load(model, book);
        var sent = new List<(long, string)>(); c.View.RequestUse = (u, a) => sent.Add((u, a));
        var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
        var raised = new List<string>(); c.Button += raised.Add;
        return (c, model, c.FindItem(50), sent, boxes, raised);
    }

    [TestCase("EnchantScroll", 31000001, InventoryController.EnchantScrollDialogId, null)]
    [TestCase("ItemRemakeScroll", 30000914, InventoryController.RemakeScrollDialogId, InventoryController.RemakeScrollOpenKey)]
    [TestCase("ItemRePackingScroll", 20300464, InventoryController.RePackingScrollDialogId, InventoryController.RePackingScrollOpenKey)]
    [TestCase("ItemSocketScroll", 31000193, InventoryController.SocketScrollDialogId, InventoryController.SocketScrollOpenKey)]
    public void AScrollIsUsedUnlessItsWindowIsOpen(string function, int id, int dialog, string openKey)
    {
        // EnchantScroll @0x140d225d0 (box 0xb68), ItemRemakeScroll @0x140d221c0, ItemRePackingScroll @0x140d22090, ItemSocketScroll @0x140d21c30
        var (c, model, item, sent, boxes, raised) = UseSetup(function, id);
        bool open = false;
        c.IsDialogOpen = d => open && d == dialog;
        c.UseItem(item, true);
        Assert.That((sent, raised.Count, boxes.Count), Is.EqualTo((new[] { (50L, "") }, 0, 0)), "closed: the plain use, the server opens the window");
        open = true;
        Assert.That(c.UseItem(item, true), Is.False);
        string text = openKey == null ? model.Tables.Common(0xb68) : model.Tables.Common(openKey);
        Assert.That((sent.Count, boxes.Single().Type, boxes.Single().Msg), Is.EqualTo((1, 0, text)), "open: its box, nothing sent");
    }

    [Test]
    public void AnInventoryExpandItemAsksNamingTheTabsAtTheirMaximumAndItsYesUsesIt()
    {
        // ExpandInven @0x140d23870, FUN_140a5ce30 (inventory.cpp:469), the box's yes FUN_1419fc810 (uimessagebox.cpp:1527)
        var (c, model, item, sent, boxes, raised) = UseSetup("ExpandInven", 20300247);
        c.MaxSlots = _ => 36;
        Assert.That(c.UseItem(item, true), Is.True);
        var box = boxes.Single();
        Assert.That((box.Type, box.Extra, box.ItemUid, box.EnterAllowed), Is.EqualTo((0x11, InventoryController.UseFunctionItemTag, 50L, false)));
        Assert.That(box.Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(model.Tables.Common(0x29c))));
        Assert.That((sent.Count, raised.Count), Is.EqualTo((0, 0)), "it asks first");
        var mb = new MessageBoxController { UseFunctionItem = c.ConfirmUseFunctionItem, data = box };
        Assert.That(mb.HandlePlainTag(1, InventoryController.UseFunctionItemTag), Is.True);
        Assert.That(sent, Is.Empty, "the other button: nothing");
        Assert.That(mb.HandlePlainTag(0, InventoryController.UseFunctionItemTag), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { (50L, "") }), "the yes is the plain use");
        c.ConfirmUseFunctionItem(99);     // an item no longer in the bag: nothing
        Assert.That(sent.Count, Is.EqualTo(1));

        c.MaxSlots = t => t == 2 ? 0 : 36;
        c.UseItem(item, true);
        Assert.That(boxes.Last().Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(model.Tables.Common(0x29c))), "two arguments: the plain text even with a tab at its maximum");
        var d = item.Data;
        var saved = d.FunctionArgs.ToList();
        try
        {
            d.FunctionArgs.Clear(); d.FunctionArgs.Add("6");
            c.UseItem(item, true);
            Assert.That(boxes.Last().Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(model.Tables.Common(0x2a6), model.Tables.Common(0x25c))), "one argument: the tab at its maximum is named");
        }
        finally { d.FunctionArgs.Clear(); d.FunctionArgs.AddRange(saved); }

        c.MaxSlots = _ => 0;
        int before = boxes.Count;
        Assert.That(c.UseItem(item, true), Is.False);
        Assert.That((boxes.Count, boxes.Last().Type, boxes.Last().Msg), Is.EqualTo((before + 1, 0, model.Tables.Common(0x2a3))), "every tab at its maximum: box 0x2a3");
        Assert.That(InventoryController.InventoryTabName(model.Tables, 13), Is.EqualTo(model.Tables.Common("s_inventory_tab_lapenshard") ?? ""));
    }

    [Test]
    public void AKeyBoxNeedsItsKeysThenAsks()
    {
        // OpenItemBoxWithKey @0x140d25430: arguments 0 (the key) and 1 (the count)
        var (c, model, item, sent, boxes, raised) = UseSetup("OpenItemBoxWithKey", 20300133);
        int key = item.Data.FunctionArgInt(0, 0), need = item.Data.FunctionArgInt(1, 0);
        string keyName = model.Tables.ItemName(key) ?? "";
        c.UseItem(item, true);
        Assert.That((boxes.Single().Type, boxes.Single().Msg, sent.Count),
            Is.EqualTo((0, Ms2.Quest.ClientFormat.Format(model.Tables.Common(InventoryController.WithKeyNoKeyKey), keyName, 0, need), 0)), "no key: the count box");

        (c, model, item, sent, boxes, raised) = UseSetup("OpenItemBoxWithKey", 20300133,
            new EquipItem { Id = key, Uid = 51, InventorySlot = (short)(key / 10000000 == 2 ? 1 : 0), Amount = need, Rarity = 1 });
        c.UseItem(item, true);
        var box = boxes.Single();
        Assert.That((box.Type, box.Extra, box.Msg, box.EnterAllowed), Is.EqualTo((0x11, InventoryController.UseFunctionItemTag,
            Ms2.Quest.ClientFormat.Format(model.Tables.Common(InventoryController.WithKeyKey), keyName), true)));
        Assert.That((sent.Count, raised.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public void ALevelPotionChecksTheFieldTheLevelAndDeathBeforeTheUse()
    {
        // LevelPotion @0x140d24900: <v targetLevel="..."/>
        var (c, model, item, sent, boxes, raised) = UseSetup("LevelPotion", 20000476);
        int target = item.Data.FunctionXmlInt("targetLevel");
        Assert.That(target, Is.GreaterThan(1));
        var notices = new List<string>(); c.SystemNotice += notices.Add;
        var record = new PcInfo { Level = (ushort)target, JobGroup = 10, Equips = Array.Empty<EquipItem>() };
        c.Wearer = () => record;
        c.FieldIsTutorial = () => true;
        c.UseItem(item, true);
        Assert.That((notices, boxes.Count, sent.Count), Is.EqualTo((new[] { InventoryController.TutorialLevelUpKey }, 0, 0)), "a tutorial field: the notice");
        c.FieldIsTutorial = () => false;
        c.UseItem(item, true);
        Assert.That(boxes.Last().Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(model.Tables.Common(InventoryController.TargetLevelKey), target)), "at the level already");
        record.Level = (ushort)(target - 1);
        bool dead = true;
        c.LocalPlayerDead = () => dead;
        c.UseItem(item, true);
        Assert.That((boxes.Last().Msg, sent.Count), Is.EqualTo((model.Tables.Common(0x54), 0)), "dead: box 0x54");
        dead = false;
        c.UseItem(item, true);
        Assert.That((sent, raised.Count), Is.EqualTo((new[] { (50L, "") }, 0)));
    }

    [Test]
    public void ALimitedQuantityBoxWaitsItsCooldownUnlessTheGmLevelIsHigh()
    {
        // OpenItemBoxLimitedQuantity @0x140d25c50: <xmlData cooldownTime="3"/>, s_itemUseMap by item id, DAT_143ab2914 < 0x32
        var (c, model, item, sent, boxes, raised) = UseSetup("OpenItemBoxLimitedQuantity", 20302629);
        int cooldown = item.Data.FunctionXmlInt("cooldownTime");
        Assert.That(cooldown, Is.GreaterThan(0));
        uint now = 7_000_000;
        c.TickCount = () => now;
        c.UseItem(item, true);
        Assert.That(sent.Count, Is.EqualTo(1));
        now += (uint)cooldown * 1000 - 1;
        c.UseItem(item, true);
        Assert.That(sent.Count, Is.EqualTo(1), "sooner than the cooldown: nothing");
        c.GmLevel = () => 0x32;
        c.UseItem(item, true);
        Assert.That(sent.Count, Is.EqualTo(2), "a GM level of 0x32: no cooldown");
        c.GmLevel = () => 0;
        now += 1;
        c.UseItem(item, true);
        Assert.That((sent.Count, raised.Count), Is.EqualTo((3, 0)), "the cooldown passed");
    }

    [Test]
    public void ATreasureMapDoesNothingAsAPartyRecall()
    {
        // TreasureMap is registered with RecallParty's handler @0x140d1df70: returns 1, nothing done
        var (c, model, item, sent, boxes, raised) = UseSetup("TreasureMap", 30001025);
        Assert.That(c.UseItem(item, true), Is.True);
        Assert.That((sent.Count, raised.Count, boxes.Count), Is.EqualTo((0, 0, 0)), "returns 1, nothing done, not raised");
    }

    [Test]
    public void AnItemBoxOfTheOtherGenderShowsBox0x2d()
    {
        // OpenItemBox @0x140d259d0: PlaySystemSound(s_warningGender), FUN_140d73bc0(0x2d): a message box, not a notice
        Packs();
        var model = NewModel();
        model.HasLocalPlayer = true;
        var d = model.Tables.Items.Get(20300327);
        int saved = d.GenderLimit;
        try
        {
            d.GenderLimit = 1;
            var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
            book.Apply(new ItemMessage.LoadTab(2, new[] { new EquipItem { Id = 20300327, Uid = 30, InventorySlot = 0, Amount = 1, Rarity = 1 } }));
            var (_, c, _) = Load(model, book);
            c.Wearer = () => new PcInfo { Level = 50, JobGroup = 10, Gender = 0, Equips = Array.Empty<EquipItem>() };
            var boxes = new List<MessageBoxData>(); c.ShowBox = boxes.Add;
            var notices = new List<string>(); c.SystemNotice += notices.Add;
            var sounds = new List<string>(); c.WarningSound += sounds.Add;
            if (d.FunctionName != "OpenItemBox") Assert.Ignore("item 20300327 is not OpenItemBox in this data");
            // CanUseItem's gender check comes first with the same box: go past it to the handler's own
            Assert.That(c.OpenItemBox(c.FindItem(30)), Is.False);
            Assert.That((boxes.Single().Type, boxes.Single().Msg, notices.Count, sounds.Single()),
                Is.EqualTo((0, model.Tables.Common(0x2d), 0, InventoryController.GenderWarningSound)));
        }
        finally { d.GenderLimit = saved; }
    }
}
