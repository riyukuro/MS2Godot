using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The slot logic of another player's put-on / put-off: the lists (CEquipment::EquipItem, FUN_1411e3960), which slots
/// rebuild and what a replace asset takes the place of.</summary>
public class WornEquipsTests {
    static EquipItem It(int id, long uid, EquipSlot slot) => new() { Id = id, Uid = uid, Slot = slot };
    // item ids under 20000000 are gear (layer 0), 20000000 and up are outfits (layer 1), 0 = no model preset
    static int? Layer(int id) => id == 0 ? null : id >= 20000000 ? 1 : 0;

    static (IReadOnlyList<EquipItem> Gear, IReadOnlyList<EquipItem> Outfit) Lists(params EquipItem[] gear) => (gear, new List<EquipItem>());

    [Test] public void APutOnReplacesTheItemOfItsSlotAndLayer() {
        var (g, o) = Lists(It(11, 1, EquipSlot.CL), It(12, 2, EquipSlot.SH));
        Assert.That(WornEquips.PutOn(ref g, ref o, new ItemPutOn(5, 0, It(13, 3, EquipSlot.CL)), Layer), Is.True);
        Assert.That(g.Select(e => e.Uid), Is.EquivalentTo(new long[] { 2, 3 }));
    }

    [Test] public void AnOutfitLayerItemSitsAfterTheGearSoItIsTheOneShown() {
        var (g, o) = Lists(It(11, 1, EquipSlot.CL));
        WornEquips.PutOn(ref g, ref o, new ItemPutOn(5, 2, It(14, 4, EquipSlot.CL)), Layer);
        Assert.That(g.Count, Is.EqualTo(1));
        Assert.That(o.Select(e => e.Uid), Is.EqualTo(new long[] { 4 }));
        Assert.That(WornEquips.Look(g, o).Last().Uid, Is.EqualTo(4));
        Assert.That(WornEquips.Shown(WornEquips.Look(g, o))[EquipSlot.CL].Uid, Is.EqualTo(4));
    }

    [Test] public void APutOffOfTheOutfitShowsTheGearAgain() {
        var (g, o) = Lists(It(11, 1, EquipSlot.CL));
        WornEquips.PutOn(ref g, ref o, new ItemPutOn(5, 2, It(14, 4, EquipSlot.CL)), Layer);
        Assert.That(WornEquips.PutOff(ref g, ref o, 4, Layer), Is.EqualTo(EquipSlot.CL));
        Assert.That(WornEquips.Shown(WornEquips.Look(g, o))[EquipSlot.CL].Uid, Is.EqualTo(1));
    }

    [Test] public void AnItemWithoutAPresetOrAKindAboveTwoIsNotWorn() {
        var (g, o) = Lists(It(11, 1, EquipSlot.CL));
        Assert.That(WornEquips.PutOn(ref g, ref o, new ItemPutOn(5, 0, It(0, 9, EquipSlot.SH)), Layer), Is.False);
        Assert.That(WornEquips.PutOn(ref g, ref o, new ItemPutOn(5, 3, It(12, 9, EquipSlot.SH)), Layer), Is.False);
        Assert.That(WornEquips.PutOff(ref g, ref o, 77, Layer), Is.Null);
        Assert.That(g.Count, Is.EqualTo(1));
    }

    [Test] public void OnlyTheChangedSlotsRebuild() {
        var before = WornEquips.Shown(new[] { It(11, 1, EquipSlot.CL), It(12, 2, EquipSlot.SH), It(13, 3, EquipSlot.HR) });
        var swapped = WornEquips.Shown(new[] { It(11, 1, EquipSlot.CL), It(15, 5, EquipSlot.SH), It(13, 3, EquipSlot.HR) });
        Assert.That(WornEquips.Changed(before, swapped), Is.EqualTo(new[] { EquipSlot.SH }));
        Assert.That(WornEquips.Changed(before, before), Is.Empty);
        var off = WornEquips.Shown(new[] { It(11, 1, EquipSlot.CL), It(13, 3, EquipSlot.HR) });
        Assert.That(WornEquips.Changed(before, off), Is.EqualTo(new[] { EquipSlot.SH }));
        var on = WornEquips.Shown(new[] { It(11, 1, EquipSlot.CL), It(12, 2, EquipSlot.SH), It(13, 3, EquipSlot.HR), It(16, 6, EquipSlot.CP) });
        Assert.That(WornEquips.Changed(before, on), Is.EqualTo(new[] { EquipSlot.CP }));
    }

    [Test] public void ADyeChangeRebuildsTheSlot() {
        var a = It(11, 1, EquipSlot.CL);
        var b = It(11, 1, EquipSlot.CL);
        b.Color = new EquipColor(new Rgba(1, 2, 3, 255), default, default, 0, 0);
        Assert.That(WornEquips.Changed(WornEquips.Shown(new[] { a }), WornEquips.Shown(new[] { b })), Is.EqualTo(new[] { EquipSlot.CL }));
    }

    [Test] public void SkinAndFaceDecalsDrawNoModel() {
        var shown = WornEquips.Shown(new[] { It(1, 1, EquipSlot.SK), It(2, 2, EquipSlot.FD), It(3, 3, EquipSlot.CL) });
        Assert.That(shown.Keys, Is.EqualTo(new[] { EquipSlot.CL }));
    }

    [Test] public void AReplaceAssetTakesTheBodyMeshesNamedLikeItsSlot() {
        Assert.That(WornEquips.Replaces("CL", "CL_Skin"), Is.True);
        Assert.That(WornEquips.Replaces("CL", "CL"), Is.True);
        Assert.That(WornEquips.Replaces("FA", "FA_EA"), Is.True);
        Assert.That(WornEquips.Replaces("FA", "FACE"), Is.False);       // a longer name is not the slot's
        Assert.That(WornEquips.Replaces("CL", "PA_Skin"), Is.False);
        Assert.That(WornEquips.Replaces("cl", "CL_Bra"), Is.True);
    }
}
