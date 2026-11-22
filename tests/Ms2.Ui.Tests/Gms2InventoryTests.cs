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
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
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
}
