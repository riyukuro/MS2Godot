using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The BlackMarket packet (0x98, CBlackMarketMan::OnBlackMarket and CBlackMarketProduct::Read) and its sends (0x5F).</summary>
public class BlackMarketPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Bytes(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader R(Action<ByteWriter> f) => new(Bytes(f));

    static BlackMarketMessage ReadAll(Action<ByteWriter> f) {
        var r = R(f);
        var m = BlackMarketMessage.Read(r);
        r.ExpectEnd("black market");
        return m;
    }

    const int ItemId = 11300001;

    static void WriteProduct(ByteWriter w, long id, int count, int bought, long unit, long seller, int itemId = ItemId, bool body = true) {
        w.WriteLong(id); w.WriteLong(1000); w.WriteLong(1100); w.WriteLong(5000);
        w.WriteInt(count); w.WriteInt(bought); w.WriteLong(unit); w.WriteByte(0);
        w.WriteLong(id * 10); w.WriteInt(itemId); w.WriteByte(3); w.WriteLong(seller);
        if (body) ItemReader.WriteEmptyBody(w, count - bought);
    }

    [Test]
    public void TheOpcodesAreTheClients() {
        Assert.That((ushort)Inbound.BlackMarket, Is.EqualTo(0x98));
        Assert.That((ushort)Outbound.BlackMarket, Is.EqualTo(0x5F));
    }

    [Test]
    public void TheRequestsWriteTheirModeAndFields() {
        Assert.That(new BlackMarketBuyRequest(1, 1).Op, Is.EqualTo(Outbound.BlackMarket));
        Assert.That(Bytes(new BlackMarketOwnListRequest()), Is.EqualTo(new byte[] { 1 }));
        Assert.That(Bytes(new BlackMarketMode6Request()), Is.EqualTo(new byte[] { 6 }));
        Assert.That(Bytes(new BlackMarketRegisterRequest(0x0102, 0x0304, 5)),
            Is.EqualTo(new byte[] { 2, 2, 1, 0, 0, 0, 0, 0, 0, 4, 3, 0, 0, 0, 0, 0, 0, 5, 0, 0, 0 }));
        Assert.That(Bytes(new BlackMarketCancelRequest(7)), Is.EqualTo(new byte[] { 3, 7, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new BlackMarketBuyRequest(8, 2)), Is.EqualTo(new byte[] { 5, 8, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0 }));
        Assert.That(Bytes(new BlackMarketReferencePriceRequest(0x0a0b, 4)), Is.EqualTo(new byte[] { 8, 0x0b, 0x0a, 0, 0, 4, 0, 0, 0 }));
    }

    [Test]
    public void TheSearchWritesTheConditionInWriteStreamOrder() {
        var c = new BlackMarketSearch { MajorCategory = 1, MinorCategory = 2, MinLevel = 3, MaxLevel = 4, JobGroup = 5, Rank = 6, Name = "ab", Page = 2, Sort = 7 };
        var req = BlackMarketSearchRequest.TryCreate(c);
        var expected = Bytes(w => {
            w.WriteByte(4);
            foreach (var v in new[] { 1, 2, 3, 4, 5, 6, 0, 15 }) w.WriteInt(v);      // the enchant range defaults to 0..15
            w.WriteByte(0); w.WriteByte(3);                                           // the socket range 0..3
            w.WriteUnicodeString("ab");
            w.WriteInt(2); w.WriteByte(7); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
            w.WriteByte(0); w.WriteByte(1);                                           // no option search, limit 1
            for (int i = 0; i < 6; i++) w.WriteInt(0);
        });
        Assert.That(Bytes(req), Is.EqualTo(expected));
    }

    [Test]
    public void AnOptionSearchCompactsItsStatRangesOrSendsNothing() {
        var none = new BlackMarketSearch { OptionSearch = 1 };
        Assert.That(BlackMarketSearchRequest.TryCreate(none), Is.Null, "an option search with no stat range is not sent");
        var c = new BlackMarketSearch { OptionSearch = 1 };
        c.StatMin[1] = 10; c.StatMax[2] = 20;
        Assert.That(BlackMarketSearchRequest.TryCreate(c), Is.Not.Null);
        Assert.That(c.StatMin, Is.EqualTo(new[] { 10, 0, 0 }));
        Assert.That(c.StatMax, Is.EqualTo(new[] { 0, 20, 0 }));
        var plain = new BlackMarketSearch();
        plain.StatMin[2] = 5;
        Assert.That(BlackMarketSearchRequest.TryCreate(plain), Is.Not.Null);
        Assert.That(plain.StatMin, Is.EqualTo(new[] { 0, 0, 5 }), "without the option search nothing moves");
    }

    [Test]
    public void TheListsReadEachProduct() {
        var own = (BlackMarketMessage.OwnList)ReadAll(w => { w.WriteByte(1); w.WriteInt(2); WriteProduct(w, 1, 5, 2, 100, 9); WriteProduct(w, 2, 1, 0, 50, 9); });
        Assert.That(own.Products.Select(p => (p.Id, p.Count, p.Bought, p.UnitPrice, p.SellerAccountId)),
            Is.EqualTo(new[] { (1L, 5, 2, 100L, 9L), (2L, 1, 0, 50L, 9L) }));
        var p = own.Products[0];
        Assert.That((p.ListedTime, p.Value28, p.ExpireTime, p.ItemUid, p.ItemId, p.Rarity), Is.EqualTo((1000L, 1100L, 5000L, 10L, ItemId, (byte)3)));
        Assert.That((p.Remain, p.TotalPrice, p.PriceFor(2)), Is.EqualTo((3, 500L, 200L)));
        Assert.That((p.Item.Id, p.Item.Uid, p.Item.Rarity), Is.EqualTo((ItemId, 10L, 3)));

        var found = (BlackMarketMessage.SearchResult)ReadAll(w => { w.WriteByte(4); w.WriteInt(1); WriteProduct(w, 3, 2, 0, 70, 8); });
        Assert.That(found.Products.Single().Id, Is.EqualTo(3));
        var added = (BlackMarketMessage.OwnAdded)ReadAll(w => { w.WriteByte(2); WriteProduct(w, 4, 1, 0, 1, 9); });
        Assert.That(added.Product.Id, Is.EqualTo(4));
    }

    [Test]
    public void AnUnknownItemReadsNoBody() {
        var was = ItemReader.Known;
        try {
            ItemReader.Known = id => id != 1;
            var m = (BlackMarketMessage.OwnAdded)ReadAll(w => { w.WriteByte(2); WriteProduct(w, 4, 1, 0, 1, 9, itemId: 1, body: false); });
            Assert.That((m.Product.Id, m.Product.Item), Is.EqualTo((4L, (EquipItem)null)));
        } finally { ItemReader.Known = was; }
    }

    [Test]
    public void TheOtherModesReadTheirFields() {
        Assert.That(ReadAll(w => { w.WriteByte(3); w.WriteLong(5); w.WriteByte(1); }), Is.EqualTo(new BlackMarketMessage.OwnRemoved(5, 1) { Mode = 3 }));
        Assert.That(ReadAll(w => { w.WriteByte(5); w.WriteLong(5); w.WriteInt(2); }), Is.EqualTo(new BlackMarketMessage.Bought(5, 2) { Mode = 5 }));
        Assert.That(ReadAll(w => { w.WriteByte(6); w.WriteByte(1); w.WriteLong(10); w.WriteLong(20); }), Is.EqualTo(new BlackMarketMessage.Status(1, 10, 20) { Mode = 6 }));
        Assert.That(ReadAll(w => w.WriteByte(7)), Is.InstanceOf<BlackMarketMessage.Sold>());
        Assert.That(ReadAll(w => { w.WriteByte(8); w.WriteInt(ItemId); w.WriteInt(3); w.WriteLong(900); }),
            Is.EqualTo(new BlackMarketMessage.ReferencePrice(ItemId, 3, 900) { Mode = 8 }));
        Assert.That(ReadAll(w => w.WriteByte(9)), Is.InstanceOf<BlackMarketMessage.Open>());
        Assert.That(ReadAll(w => w.WriteByte(0x20)), Is.EqualTo(new BlackMarketMessage.Other { Mode = 0x20 }));
    }

    [Test]
    public void TheErrorKeepsTheNumberAsAShort() {
        var e = (BlackMarketMessage.Error)ReadAll(w => { w.WriteByte(0); w.WriteByte(2); w.WriteInt(43); w.WriteLong(6); w.WriteInt(12); w.WriteInt(0x10005); });
        Assert.That((e.Type, e.Result, e.Value, e.TableId, e.Number), Is.EqualTo(((byte)2, 43, 6L, 12, (ushort)5)));
    }
}
