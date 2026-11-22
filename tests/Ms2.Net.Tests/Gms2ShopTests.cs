using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 Shop: Outbound 0x28 modes 3 (repurchase), 4 (buy), 5 (sell) and Inbound 0x52, checked against the recordings.</summary>
public class Gms2ShopTests {
    static byte[] Bytes(ShopBuyRequest p, ProtocolProfile profile) { var w = new ByteWriter(); p.Write(w, profile); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    static byte[] Raw(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void OpcodesAreTheRecordedOnes() {
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.Shop), Is.EqualTo(0x28));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.Shop), Is.EqualTo(0x52));
    }

    [Test]
    public void BuyRequestOmitsTheShopIdOnGms2Only() {
        var gms = Bytes(new ShopBuyRequest(7, 3, 99), ProtocolProfile.Gms2);
        Assert.That(gms, Is.EqualTo(new byte[] { 4, 7, 0, 0, 0, 3, 0, 0, 0 }));
        var kms = Bytes(new ShopBuyRequest(7, 3, 99), ProtocolProfile.Kms2525);
        Assert.That(kms.Length, Is.EqualTo(13));
        Assert.That(kms.Take(9), Is.EqualTo(gms));
        Assert.That(BitConverter.ToInt32(kms, 9), Is.EqualTo(99), "bytes 9..12 of the KMS2 form are the shop id");
    }

    [Test]
    public void EveryRecordedGms2RequestIsOneOfThreeModes() {
        var all = Gms2Sniffs.Outbound("gms2", 0x28);
        Assert.That(all, Is.Not.Empty);
        int repurchase = 0, sell = 0;
        foreach (var (file, d) in all) {
            switch (d[0]) {
                case 3:
                    Assert.That(d.Length, Is.EqualTo(5), file);
                    Assert.That(Raw(new ShopRepurchaseRequest(BitConverter.ToInt32(d, 1))), Is.EqualTo(d), file);
                    repurchase++; break;
                case 4:
                    Assert.That(d.Length, Is.EqualTo(9), file);
                    var r = new ByteReader(d); r.ReadByte();
                    int serial = r.ReadInt(), qty = r.ReadInt();
                    Assert.That(Bytes(new ShopBuyRequest(serial, qty, 0), ProtocolProfile.Gms2), Is.EqualTo(d), file);
                    break;
                case 5:
                    Assert.That(d.Length, Is.EqualTo(13), file);
                    Assert.That(Raw(new ShopSellRequest(BitConverter.ToInt64(d, 1), BitConverter.ToInt32(d, 9))), Is.EqualTo(d), file);
                    sell++; break;
                default: Assert.Fail($"{file}: unrecorded mode {d[0]}"); break;
            }
        }
        Assert.That(repurchase, Is.GreaterThanOrEqualTo(1));
        Assert.That(sell, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void EveryRecordedGms2AnswerReadsWhole() {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set: item bodies need the data hooks");
        var all = Gms2Sniffs.Inbound("gms2", 0x52);
        Assert.That(all, Is.Not.Empty);
        var packs = new Ms2.Formats.Pack.PackSet(data);
        var kinds = new Ms2Godot.Net.ItemKinds(packs); var slots = new Ms2Godot.Net.ItemModelSlots(packs);
        var (s, e, k) = (ItemReader.SlotOf, ItemReader.ExtraOf, ItemReader.Known);
        try {
            ItemReader.SlotOf = slots.SlotOf; ItemReader.ExtraOf = kinds.ExtraOf; ItemReader.Known = null;
            foreach (var (file, d) in all) {
                var r = new ByteReader(d);
                ShopPacket.Read(r, ProtocolProfile.Gms2);
                Assert.That(r.Available, Is.EqualTo(0), $"{file} mode {d[0]}: bytes left");
            }
        } finally { ItemReader.SlotOf = s; ItemReader.ExtraOf = e; ItemReader.Known = k; }
    }
}
