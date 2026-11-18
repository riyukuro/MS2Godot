using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
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

    static (UiHost, InventoryController, ItemBook) Load(UiModel model = null)
    {
        model ??= NewModel();
        model.Player.Mesos = 1234567;
        var book = Book();
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
}
