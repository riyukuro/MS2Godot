using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The inventory tab of every item of the GMS2 data (ItemKinds.TabOf, the GMS2 client's rule @0x140344be0 on the per-item files).
/// The upstream Maple2 server places an item by its own InventoryType rule (Maple2.Model/ModelExtensions.cs:50) and the client
/// files what it gets by its own tab, so both have to agree for an item to show where the server put it. Needs MS2_DATA at a
/// GMS2 Data folder (the per-item files); ignored on the KMS data, whose type rule is CItemData::FUN_14053e530.
/// </summary>
public class Gms2InventoryTabDataTests {
    /// <summary>InventoryType of the upstream server (ModelExtensions.cs:50-77) in its wire numbers; -1 where it throws.</summary>
    static int Upstream(int type, int subtype, bool skin, bool piece) {
        if (piece) return 16;
        return type switch {
            0 or 2 or 4 => subtype == 2 ? 11 : 7,
            1 => skin ? 1 : 0,
            3 => 5, 5 or 9 => 2, 6 or 12 => 4, 7 => 13, 10 => subtype != 20 ? 7 : 4, 11 => 10, 13 or 14 => 6, 15 or 19 => 3,
            16 => 9, 17 => 8, 18 => 11, 20 => 12, 21 => 15, 22 => 7,
            _ => -1,
        };
    }

    [Test]
    public void EveryGms2ItemSitsInTheTabTheServerSendsItIn() {
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        if (!packs.Exists("Xml", "item/1/13/11320388.xml")) Assert.Ignore("MS2_DATA has no per-item GMS2 files");
        Assert.That(Ms2.Ui.LobbyFeatures.IsEnabled(packs, Ms2.Ui.Items.ItemData.Gms2PieceTabFeature), Is.True,
            "the GMS2 data turns InventoryPieceTab on");

        var kinds = new Ms2Godot.Net.ItemKinds(packs);
        int items = 0, emotion = 0, survival = 0;
        var off = new List<string>();
        foreach (var (id, entry) in Ms2.Formats.World.PerItemFiles.Index(packs)) {
            var root = Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", entry))?.Root;
            if (root == null || Ms2.Ui.FeatureElements.SelectEnvironment(packs, root)?.Element("property") is not { } p) continue;
            int Int(string a) => int.TryParse((string) p.Attribute(a), out int v) ? v : 0;
            int type = Int("type"), subtype = Int("subtype");
            bool piece = (string) p.Attribute("functionTags") == "piece";   // upstream ItemMapper.cs:196
            int tab = kinds.TabOf(id);
            items++;
            if (Int("survival") != 0) {
                survival++;
                Assert.That(tab, Is.EqualTo(ItemBook.SurvivalTab), $"{id}: the survival tab (wire 14)");
                continue;
            }
            if (type == 2 && subtype == 14) { emotion++; Assert.That(tab, Is.EqualTo(7), $"emotion item {id}"); }
            int want = Gms2InventoryTab.FromWire(Upstream(type, subtype, Int("skin") != 0, piece));
            if (tab != want && off.Count < 20) off.Add($"{id} {type}/{subtype}: {tab}, the server {want}");
        }
        TestContext.Out.WriteLine($"items {items}, emotion items {emotion}, survival {survival}");
        Assert.That(off, Is.Empty);
        Assert.That(emotion, Is.EqualTo(251));
        Assert.That(survival, Is.EqualTo(44));
        Assert.That(items, Is.GreaterThan(35000));
    }

    /// <summary>An item whose tab this inventory has none of (TabOf -1) is kept by uid with no cell check; the bag's slot count of a
    /// tab outside 0-14 asserts (inventory.cpp:2333).</summary>
    [Test]
    public void AnItemOfATabThisInventoryHasNoneOfIsKept() {
        var book = new ItemBook { TabOf = id => id == 20000733 ? -1 : 7, BaseSlots = tab => BagSlots.Base(tab, true, _ => null) };
        Assert.That(book.TryAdd(new EquipItem { Id = 20000733, Uid = 1, InventorySlot = 3 }), Is.EqualTo(0));
        Assert.That(book.TryAdd(new EquipItem { Id = 20210002, Uid = 2, InventorySlot = 3 }), Is.EqualTo(0));
        Assert.That(book.Get(1), Is.Not.Null);
    }
}
