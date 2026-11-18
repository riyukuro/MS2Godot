using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The inventory (uiinventorydialog) run by its own ActionScript with UIInventoryDialog's C++ side (needs MS2_DATA).</summary>
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
        Assert.That(raised, Is.EqualTo(new[] { "onClickSlotExpand", "onClickItemRemakeOption", "onClickOpenBank" }));
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
        Assert.That(c.OnCallback("notifySlotDragEnd", new object[] { "InventoryDialog.0", "InventoryDialog.4" }), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { (3L, (short)4) }), "mode 3: the dragged item's uid and the target index");
        Assert.That(c.View.ItemAt(c.View.Tab, 0)?.Uid, Is.EqualTo(3L), "nothing moves until the server says so");
        c.OnCallback("notifySlotDragEnd", new object[] { "InventoryDialog.0", "InventoryDialog.0" });
        c.OnCallback("notifySlotDragEnd", new object[] { "InventoryDialog.0" });
        c.OnCallback("notifySlotDragEnd", new object[] { "QuickSlot.1", "InventoryDialog.4" });
        Assert.That(sent, Has.Count.EqualTo(1), "the same slot, one argument or another bag's source send nothing");
        c.OnCallback("notifySlotDragEnd", new object[] { "InventoryDialog.0", null });
        Assert.That((sent.Count, raised.Last()), Is.EqualTo((1, "notifySlotDragEnd")), "a drop off the bag is not ported: raised");
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
        }));
        var (h, c, _) = Load(model, book);
        c.View.ServerTime = () => 1000;
        var record = new PcInfo { Level = 50, Equips = new[] { new EquipItem { Id = 13000001, Uid = 2, Slot = EquipSlot.RH, Rarity = 1 } } };
        c.Wearer = () => record;
        int gear = c.Tabs.Select((t, i) => (t, i)).First(x => x.t.Tab == 1).i;
        h.Set(h.Child("m_tab"), "selectedIndex", gear);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); c.Process(); }
        var sent = new List<(long, byte, byte)>(); c.View.RequestEquip = (u, s, m) => sent.Add((u, s, m));
        var raised = new List<string>(); c.Button += raised.Add;
        var boxes = new List<string>(); c.ShowBox = b => boxes.Add(b.Msg);
        var t = model.Tables;
        Assert.That(t.Common(InventoryController.ExpiredKey), Is.Not.Empty);
        Assert.That(t.Common(InventoryController.LowLevelKey), Is.Not.Empty);

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
        c.ActionKeyDown = a => a == 0x49406;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        c.ActionKeyDown = _ => false;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1" });
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.9", 1 });
        Assert.That(sent.Count, Is.EqualTo(count), "the preview key, a double click or an empty cell send nothing");
        Assert.That(raised.Count(r => r == "notifySlotUse"), Is.EqualTo(2), "the preview and the double click are raised");
        model.HasLocalPlayer = false;
        c.OnCallback("notifySlotUse", new object[] { "InventoryDialog.1", 1 });
        Assert.That(sent.Count, Is.EqualTo(count), "no local player");
    }
}
