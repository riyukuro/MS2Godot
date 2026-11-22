using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The item body kind of the GMS2 profile from the GMS2 item data (one file per item, item/&lt;a&gt;/&lt;bb&gt;/&lt;id&gt;.xml, the model slot named in the
/// item itself): CItemData::CreateExtraData (itemdata.cpp:1430) picks the hair (first model group type 1, HR) or decal (type 3, FD) appearance by the first model slot, and a cap only by GetEquipSubtype() == 0xd. CP stands in for subtype 0xd: in the KMS data CP <-> 13 one to one (2847 = 2847), the GMS2 subtypes seen are 0 and 16, so the GMS2 item file's slot name tells the cap (the recordings prove HR and CP). The face slot FA (type 2) has no case in that switch, so it stays unmapped as in the client.
/// Needs MS2_DATA at a GMS2 Data folder and MS2_SNIFFS; skipped otherwise.
/// STILL-OPEN: the item files of locale NA are not selected (the client's locale is a constant KR here), so those items have no kind; music items without a score file.</summary>
public class Gms2ItemDataTests {
    static PackSet Packs() {
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || Environment.GetEnvironmentVariable("MS2_SNIFFS") == null) Assert.Ignore("no MS2_DATA / MS2_SNIFFS");
        var p = new PackSet(data);
        if (!p.Exists("Xml", "item/1/13/11320388.xml")) { p.Dispose(); Assert.Ignore("MS2_DATA has no per-item GMS2 files"); }
        return p;
    }

    [Test]
    public void TheFirstModelSlotOfTheItemFilePicksTheAppearanceLayout() {
        using var packs = Packs();
        var slots = new Ms2Godot.Net.ItemModelSlots(packs);
        Assert.That(slots.SlotOf(10200001), Is.EqualTo(Ms2.Net.Packets.EquipSlot.HR));
        Assert.That(slots.SlotOf(11320388), Is.EqualTo(Ms2.Net.Packets.EquipSlot.CP), "a cap of subtype 0 names its slot CP");
        Assert.That(slots.SlotOf(35100034), Is.EqualTo(Ms2.Net.Packets.EquipSlot.Unknown), "an empty slot name with a decal element is no decal item");
    }

    /// <summary>The 61 inventory loads that read short with the KMS data now read; the rest of the over-reads belong to ids the data lacks or locale NA files.</summary>
    [Test]
    public void RecordedInventoryPacketsReadWithTheItemData() {
        using var packs = Packs();
        var (slotOf, extraOf, known) = (ItemReader.SlotOf, ItemReader.ExtraOf, ItemReader.Known);
        try {
            ItemReader.SlotOf = new Ms2Godot.Net.ItemModelSlots(packs).SlotOf; ItemReader.ExtraOf = new Ms2Godot.Net.ItemKinds(packs).ExtraOf; ItemReader.Known = null;
            int ok = 0, under = 0, over = 0;
            foreach (var (_, d) in Gms2Sniffs.Inbound("gms2", 0x21)) {
                var r = new ByteReader(d);
                try { ItemMessage.Read(r, _ => null, ProtocolProfile.Gms2); if (r.Available == 0) ok++; else under++; } catch { over++; }
            }
            TestContext.Out.WriteLine($"ok {ok} under {under} over {over}");
            Assert.That(under, Is.LessThanOrEqualTo(1));
            Assert.That(over, Is.LessThan(ok / 50), "over-reads stay under 2 percent");
        } finally { (ItemReader.SlotOf, ItemReader.ExtraOf, ItemReader.Known) = (slotOf, extraOf, known); }
    }
}
