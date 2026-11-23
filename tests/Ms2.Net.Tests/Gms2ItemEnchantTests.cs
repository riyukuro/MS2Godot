using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The recorded GMS2 ItemEnchant (0x99) packets: the info (mode 5) options carry an f32 each, modes 10, 11 and 15 an item body in the GMS2 layout.</summary>
public class Gms2ItemEnchantTests {
    [Test]
    public void EveryRecordedGms2EnchantPacketReadsToTheEnd() {
        var list = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.ItemEnchant));
        Assert.That(list, Is.Not.Empty);
        int checkedModes = 0;
        Gms2Sniffs.WithGms2ItemData(() => { foreach (var (file, data) in list) {
            int mode = data[0];
            if (mode != 5 && mode != 10 && mode != 11) continue;
            var r = new ByteReader(data);
            Assert.DoesNotThrow(() => ItemEnchantMessage.Read(r, _ => new EquipItem { Id = 20000001 }, ProtocolProfile.Gms2), file);
            Assert.That(r.Available, Is.EqualTo(0), $"{file} mode {mode}");
            checkedModes++;
        } });
        Assert.That(checkedModes, Is.GreaterThan(0));
    }

    [Test]
    public void AGms2OptionCarriesATrailingFloat() {
        var w = new ByteWriter(); w.WriteInt(2);
        for (int i = 0; i < 2; i++) { w.WriteShort((short) (10 + i)); w.WriteInt(100 + i); w.WriteFloat(0.5f); }
        var r = new ByteReader(w.ToArray());
        var d = ItemEnchantMessage.ReadOptions(r, ProtocolProfile.Gms2);
        Assert.That(d[11], Is.EqualTo(101)); Assert.That(r.Available, Is.EqualTo(0));
        var w2 = new ByteWriter(); w2.WriteInt(1); w2.WriteShort(10); w2.WriteInt(5);
        var r2 = new ByteReader(w2.ToArray());
        ItemEnchantMessage.ReadOptions(r2, ProtocolProfile.Kms2525);
        Assert.That(r2.Available, Is.EqualTo(0));
    }
}
