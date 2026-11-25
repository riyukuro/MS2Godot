using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 uiinventorydialog (m_itemBag, an ItemBag asked for its slot names by bagId) run by its own ActionScript
/// with the same C++ side as the KMS movie. Needs MS2_GMS2_DATA.</summary>
[NonParallelizable]
public class Gms2InventoryTests
{
    [Test]
    public void TheItemBagIsGivenItsBagIdAndAsksForTheSlotNames()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var packs = new PackSet(d);
        var src = TestData.Movies(packs);
        var model = new UiModel(new GameTables(packs, "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f), HasLocalPlayer = true };
        model.Player.Mesos = 1234567;
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.LoadTab(0, new[] { new EquipItem { Id = 1, Uid = 1, InventorySlot = 0, Amount = 1 } }));
        var c = new InventoryController { View = new InventoryView(book) };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) { p.Tick(); c.Process(); }
        Assert.That(h.Child("m_itemBagEx"), Is.Null);
        var bag = h.Child("m_itemBag");
        Assert.That(bag, Is.Not.Null);
        Assert.That(Rt.ToString(h.Get(bag, "bagId")), Is.EqualTo(InventoryController.BagName));
        Assert.That(c.GetBagSlotBindings(InventoryController.BagName).Count(), Is.EqualTo(48));
        Assert.That(Rt.ToNumber(h.Get(h.Get(bag, "dataProvider"), "length")), Is.EqualTo(48));
    }

    /// <summary>The GMS2 tab list (@0x140d87010): on a survival field (mapType 15/16) survival is the first button and the selected
    /// tab, with its item; on any other field it has no button and a selected survival tab goes back to equip. The KMS2 list
    /// (no hook) never has it. No list has the pet equipment tab (wire 8).</summary>
    [Test]
    public void TheSurvivalTabIsFirstAndSelectedOnlyOnASurvivalField()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var packs = new PackSet(d);
        var src = TestData.Movies(packs);
        var model = new UiModel(new GameTables(packs, "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f), HasLocalPlayer = true };
        Assert.That(model.Tables.Common("s_inventory_tab_survival"), Is.Not.Null.And.Not.Empty, "the GMS2 strings name the tab");
        // 30001102 has property@survival 1 in the GMS2 data (the tab rule @0x140344be0 files it in wire 14)
        var book = new ItemBook { TabOf = id => id == 30001102 ? ItemBook.SurvivalTab : 7, BaseSlots = _ => 48 };
        book.Apply(new ItemMessage.Add(new EquipItem { Id = 30001102, Uid = 1, InventorySlot = 2, Amount = 1 }));
        book.Apply(new ItemMessage.Add(new EquipItem { Id = 20210002, Uid = 2, InventorySlot = 0, Amount = 1 }));
        var view = new InventoryView(book);
        (UiHost, InventoryController) Open(Func<bool> survivalField)
        {
            var c = new InventoryController { View = view, Gms2SurvivalField = survivalField };
            var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            var h = new UiHost(p, c, model);
            Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) { p.Tick(); c.Process(); }
            return (h, c);
        }

        var (h1, c1) = Open(() => true);
        Assert.That(c1.Tabs[0], Is.EqualTo(("survival", ItemBook.SurvivalTab)));
        Assert.That(c1.Tabs.Select(t => t.Name), Does.Not.Contain("petequip"));
        Assert.That(view.Tab, Is.EqualTo(ItemBook.SurvivalTab), "m_194 = 14 on a survival field");
        Assert.That(Rt.ToNumber(h1.Get(h1.Child("m_tab"), "selectedIndex")), Is.EqualTo(0), "the bar's first button");
        Assert.That(view.Cells(ItemBook.SurvivalTab)[2].Uid, Is.EqualTo(1L), "the survival item in its cell");
        Assert.That(view.Used(ItemBook.SurvivalTab), Is.EqualTo(1));
        c1.Close(); h1.Unload();

        var (h2, c2) = Open(() => false);
        Assert.That(c2.Tabs.Select(t => t.Tab), Does.Not.Contain(ItemBook.SurvivalTab));
        Assert.That(view.Tab, Is.EqualTo(0), "a selected survival tab goes back to equip off a survival field");
        c2.Close(); h2.Unload();

        var (h3, c3) = Open(null);
        Assert.That(c3.Tabs.Select(t => t.Tab), Does.Not.Contain(ItemBook.SurvivalTab), "the KMS2 list has no survival tab");
        Assert.That(c3.Tabs.Select(t => t.Name), Is.EqualTo(c2.Tabs.Select(t => t.Name)));
        h3.Unload();
    }
}
