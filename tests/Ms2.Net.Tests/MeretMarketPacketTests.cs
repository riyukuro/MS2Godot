using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The merat market buy (SendOp 0x39 mode 0x1e) and its two answers (RecvOp 0x6C modes 0x1e, 0x1f) from
/// CMeratMarketPacket::BuyPacket (meratmarketbasketinfo.cpp:2745, :2769) and CMeratMarketMan::OnBuySucceeded / OnBuyFailed.</summary>
public class MeretMarketPacketTests {
    static ByteReader Reader(byte[] b) => new(b);
    static byte[] WriteOut(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    static MeretMarketBuy Sample() => new() {
        Kind = 4, PremiumId = 7001, UgcId = 0, Quantity = 1, Option = 3, BookmarkId = 0, Index = 1, Total = 2, ShopId = 106,
        Gift = 0, Receiver = "", Message = "", Price = 800, StarPoint = true, PayType = 3, HomeA = 5, HomeB = 6, HomeCount = 7, RandomRate = 20,
    };

    [Test] public void BuyRequestWritesTheModeThenTheBuyPacketFieldByField() {
        var got = WriteOut(new MeretMarketBuyRequest(Sample()));
        var w = new ByteWriter();
        w.WriteByte(0x1e);                                  // SendMeretMarketMode30
        w.WriteByte(4); w.WriteInt(7001); w.WriteLong(0);   // kind, key
        w.WriteInt(1); w.WriteInt(3); w.WriteLong(0);       // m_n34, m_n30, m_n38
        w.WriteInt(1); w.WriteInt(2); w.WriteInt(106);      // m_n40, m_n44, m_n48
        w.WriteByte(0); w.WriteUnicodeString(""); w.WriteUnicodeString("");
        w.WriteLong(800); w.WriteBool(true); w.WriteByte(3);
        w.WriteInt(5); w.WriteInt(6); w.WriteInt(7); w.WriteInt(20);
        Assert.That(got, Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(new MeretMarketBuyRequest(Sample()).Op, Is.EqualTo(Outbound.MeretMarket));
        Assert.That((int)Outbound.MeretMarket, Is.EqualTo(0x39));
        Assert.That((int)Inbound.MeretMarket, Is.EqualTo(0x6C));
    }

    [Test] public void BoughtAnswerReadsTheEchoThenFourCounts() {
        var r = Reader(MeretMarketPacket.WriteSucceeded(Sample(), 2, 9, 0, 0));
        var p = MeretMarketPacket.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(p.Succeeded, Is.True);
        Assert.That(p.Buy.PremiumId, Is.EqualTo(7001));
        Assert.That(p.Buy.Index, Is.EqualTo(1));
        Assert.That(p.BuyCount, Is.EqualTo(2));
        Assert.That(p.SoldCount, Is.EqualTo(9));
    }

    [Test] public void FailedAnswerReadsTheEchoThenTheError() {
        var r = Reader(MeretMarketPacket.WriteFailed(Sample(), 0x88));
        var p = MeretMarketPacket.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(p.Succeeded, Is.False);
        Assert.That(p.Error, Is.EqualTo(0x88));
        Assert.That(p.Buy.Index, Is.EqualTo(1));
    }

    [Test] public void OtherModesAreNotRead() {
        var w = new ByteWriter(); w.WriteByte(0x65);
        Assert.That(MeretMarketPacket.Read(Reader(w.Buffer.AsSpan(0, w.Length).ToArray())), Is.Null);
    }

    [Test] public void BuyPacketRoundTripsWithGiftStrings() {
        var b = Sample(); b.Gift = 1; b.Receiver = "friend"; b.Message = "hi";
        var w = new ByteWriter(); b.Write(w);
        var r = Reader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var c = MeretMarketBuy.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(c.Receiver, Is.EqualTo("friend"));
        Assert.That(c.Message, Is.EqualTo("hi"));
        Assert.That(c.Price, Is.EqualTo(800));
        Assert.That(c.StarPoint, Is.True);
    }
}
