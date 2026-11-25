using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 client's item tab (@0x140344be0, <see cref="ItemData.Gms2InventoryTabOf"/>), in the GMS2 wire numbers.</summary>
public class Gms2InventoryTabRuleTests
{
    static int Tab(int type, int subtype = 0, int skin = 0, int survival = 0, bool piece = false, bool pieceOn = true) =>
        ItemData.Gms2InventoryTabOf(type, subtype, skin, survival, piece, pieceOn);

    [Test]
    public void TheJumpTableGivesEachTypeItsTab()
    {
        var byType = new Dictionary<int, int> {
            [3] = 5, [5] = 2, [6] = 4, [7] = 13, [9] = 2, [11] = 10, [12] = 4, [13] = 6, [14] = 6, [15] = 3, [16] = 9, [17] = 8,
            [18] = 11, [19] = 3, [20] = 12, [21] = 15,
        };
        foreach (var (type, tab) in byType) Assert.That(Tab(type), Is.EqualTo(tab), $"type {type}");
        Assert.That(Tab(1), Is.EqualTo(0));
        Assert.That(Tab(1, skin: 1), Is.EqualTo(1), "an outfit");
    }

    [Test]
    public void TheOtherTypesGoByTheSubtypeAndAnEmotionItemIsMisc()
    {
        foreach (int type in new[] { 0, 2, 4, 8, 10, 22 })
        {
            Assert.That(Tab(type, 20), Is.EqualTo(4), $"type {type} subtype 20");
            Assert.That(Tab(type, 2), Is.EqualTo(11), $"type {type} subtype 2");
            Assert.That(Tab(type, 0), Is.EqualTo(7), $"type {type} subtype 0");
        }
        Assert.That(Tab(2, ItemData.Gms2EmotionSubtype), Is.EqualTo(7), "an emotion item (type 2, subtype 14) is in Misc");
    }

    [Test]
    public void SurvivalComesFirstThenThePieceTabWhenItsFeatureIsOn()
    {
        Assert.That(Tab(2, 2, survival: 1, piece: true), Is.EqualTo(14));
        Assert.That(Tab(10, piece: true), Is.EqualTo(16));
        Assert.That(Tab(10, piece: true, pieceOn: false), Is.EqualTo(7), "without InventoryPieceTab the type rule");
        Assert.That(Tab(15, piece: true, pieceOn: false), Is.EqualTo(3));
    }

    [Test]
    public void TheKmsDataHasNoGms2Tab()
    {
        var d = new ItemData { Id = 20200001, Type = ItemData.EmotionItemType };
        Assert.That(d.Gms2WireTab(true), Is.Null);
        var g = new ItemData { Id = 20210002, Type = 2, Subtype = ItemData.Gms2EmotionSubtype };
        g.FillGms2File(); g.FillEquipSubtype(); g.FillBoxType();
        Assert.That(g.Gms2WireTab(true), Is.EqualTo(7));
        var box = new ItemData { Id = 20300559, Type = 10, Subtype = 17 };
        box.FillGms2File(); box.FillEquipSubtype(); box.FillBoxType();
        Assert.That(box.Type, Is.EqualTo(0x19), "FillBoxType still gives the KMS type");
        Assert.That(box.Gms2WireTab(false), Is.EqualTo(7), "the tab reads the file's own type 10");
    }
}
