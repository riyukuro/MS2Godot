using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 MeretMarket buy (Outbound 0x3A / Inbound 0x6E mode 0x1e): the buy packet stops after the pay type, checked against the recordings.</summary>
public class Gms2MeretMarketTests {
    static byte[] Bytes(IProfileOutPacket p, ProtocolProfile pr) { var w = new ByteWriter(); p.Write(w, pr); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static MeretMarketBuy Sample() => new() { Kind = 1, UgcId = 0x29C0191929 , Quantity = 1, Index = 1, Total = 1, Price = 50, PayType = 3, HomeA = 5, RandomRate = 7 };

    [Test]
    public void OpcodesAreTheRecordedOnes() {
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.MeretMarket), Is.EqualTo(0x3A));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.MeretMarket), Is.EqualTo(0x6E));
    }

    [Test]
    public void BuyRequestIs16BytesShorterOnGms2() {
        var req = new MeretMarketBuyRequest(Sample());
        Assert.That(Bytes(req, ProtocolProfile.Gms2).Length, Is.EqualTo(57));
        Assert.That(Bytes(req, ProtocolProfile.Kms2525).Length, Is.EqualTo(73));
    }

    [Test]
    public void BoughtAnswerRoundTripsOnGms2() {
        var buy = Sample();
        var w = new ByteWriter(); w.WriteByte(0x1e); buy.Write(w, ProtocolProfile.Gms2);
        w.WriteInt(1); w.WriteInt(0); w.WriteInt(0); w.WriteInt(1);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var p = MeretMarketPacket.Read(r, ProtocolProfile.Gms2);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That((p.Buy.Price, p.Buy.PayType, p.BuyCount, p.Ugc40), Is.EqualTo((50L, 3, 1, 1)));
    }

    [Test]
    public void EveryRecordedMode30RequestRewritesToTheSameBytes() {
        var all = Gms2Sniffs.Outbound("gms2", 0x3A).Where(x => x.Data[0] == 0x1e).ToList();
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, d) in all) {
            var r = new ByteReader(d); r.ReadByte();
            var buy = MeretMarketBuy.Read(r, ProtocolProfile.Gms2);
            Assert.That(r.Available, Is.EqualTo(0), file);
            Assert.That(Bytes(new MeretMarketBuyRequest(buy), ProtocolProfile.Gms2), Is.EqualTo(d), file);
        }
    }

    [Test]
    public void EveryRecordedMode30AnswerReadsWhole() {
        var all = Gms2Sniffs.Inbound("gms2", 0x6E).Where(x => x.Data[0] == 0x1e || x.Data[0] == 0x1f).ToList();
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, d) in all) {
            var r = new ByteReader(d);
            var p = MeretMarketPacket.Read(r, ProtocolProfile.Gms2);
            Assert.That(r.Available, Is.EqualTo(0), file);
            Assert.That(p.Succeeded, Is.True, file);
        }
    }
}
