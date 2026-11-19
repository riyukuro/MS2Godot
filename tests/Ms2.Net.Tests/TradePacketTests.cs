using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Trade packet (0x82, CTradeManagerEx::OnTrade) and the SendTradeMode* sends (Trade 0x49).</summary>
public class TradePacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Cat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    static readonly byte[] Id5 = { 5, 0, 0, 0, 0, 0, 0, 0 };

    [Test]
    public void TheSendsWriteTheirModeAndFields() {
        Assert.That(TradeRequest.Ask(5).Op, Is.EqualTo(Outbound.Trade));
        Assert.That(Bytes(TradeRequest.Ask(5)), Is.EqualTo(Cat(new byte[] { 0 }, Id5)));
        Assert.That(Bytes(TradeRequest.Shown(5)), Is.EqualTo(Cat(new byte[] { 2 }, Id5)));
        Assert.That(Bytes(TradeRequest.Answer(5, true)), Is.EqualTo(Cat(new byte[] { 3 }, Id5)));
        Assert.That(Bytes(TradeRequest.Answer(5, false)), Is.EqualTo(Cat(new byte[] { 4 }, Id5)));
        Assert.That(Bytes(TradeRequest.Close()), Is.EqualTo(new byte[] { 7 }));
        Assert.That(Bytes(TradeRequest.AddItem(5, 3, 2)), Is.EqualTo(Cat(new byte[] { 8 }, Id5, new byte[] { 3, 0, 0, 0, 2, 0, 0, 0 })));
        Assert.That(Bytes(TradeRequest.RemoveItem(5, 2)), Is.EqualTo(Cat(new byte[] { 9 }, Id5, new byte[] { 2, 0, 0, 0 })));
        Assert.That(Bytes(TradeRequest.SetMeso(5)), Is.EqualTo(Cat(new byte[] { 10 }, Id5)));
        Assert.That(Bytes(TradeRequest.Lock()), Is.EqualTo(new byte[] { 11 }));
        Assert.That(Bytes(TradeRequest.Confirm()), Is.EqualTo(new byte[] { 13 }));
        Assert.That(Bytes(TradeRequest.Busy(5)), Is.EqualTo(Cat(new byte[] { 14 }, Id5)));
    }

    [Test]
    public void EachCodeReadsItsFields() {
        var r = R(w => { w.WriteByte(0); w.WriteUnicodeString("Ann"); w.WriteLong(7); });
        var m = TradeMessage.Read(r);
        Assert.That((m.Code, m.Name, m.Id, r.Available), Is.EqualTo(((byte)0, "Ann", 7L, 0)));
        r = R(w => { w.WriteByte(1); w.WriteByte(0x14); w.WriteUnicodeString("Bo"); w.WriteInt(20000001); w.WriteInt(0x10028); });
        m = TradeMessage.Read(r);
        Assert.That((m.Flag, m.Name, m.ItemId, m.Level, r.Available), Is.EqualTo(((byte)0x14, "Bo", 20000001, (ushort)0x28, 0)),
            "the level is the i32 cast to unsigned short");
        r = R(w => { w.WriteByte(4); w.WriteUnicodeString("Cy"); });
        Assert.That(TradeMessage.Read(r).Name, Is.EqualTo("Cy"));
        r = R(w => { w.WriteByte(5); w.WriteLong(9); });
        Assert.That(TradeMessage.Read(r).Id, Is.EqualTo(9L));
        r = R(w => { w.WriteByte(6); w.WriteByte(1); });
        Assert.That(TradeMessage.Read(r).Flag, Is.EqualTo((byte)1));
        r = R(w => { w.WriteByte(9); w.WriteByte(1); w.WriteInt(3); w.WriteLong(11); });
        m = TradeMessage.Read(r);
        Assert.That((m.Flag, m.TradeSlot, m.Id, r.Available), Is.EqualTo(((byte)1, 3, 11L, 0)));
        r = R(w => { w.WriteByte(10); w.WriteByte(0); w.WriteLong(1234); });
        m = TradeMessage.Read(r);
        Assert.That((m.Flag, m.Meso, r.Available), Is.EqualTo(((byte)0, 1234L, 0)));
        foreach (byte c in new byte[] { 11, 12 }) {
            r = R(w => { w.WriteByte(c); w.WriteByte(1); });
            Assert.That((TradeMessage.Read(r).Flag, r.Available), Is.EqualTo(((byte)1, 0)));
        }
        foreach (byte c in new byte[] { 2, 13, 14 }) {
            r = R(w => w.WriteByte(c));
            Assert.That((TradeMessage.Read(r).Code, r.Available), Is.EqualTo((c, 0)));
        }
    }

    [Test]
    public void AnUnknownItemIdLeavesTheBodyUnread() {
        var keep = ItemReader.Known;
        try {
            ItemReader.Known = _ => false;
            var r = R(w => { w.WriteByte(8); w.WriteByte(1); w.WriteInt(1); w.WriteLong(11); w.WriteInt(1); w.WriteInt(4); w.WriteInt(2); w.WriteInt(3); });
            var m = TradeMessage.Read(r);
            Assert.That((m.Flag, m.Id, m.TradeSlot, m.Item, r.Available), Is.EqualTo(((byte)1, 11L, 3, (EquipItem)null, 0)));
        } finally { ItemReader.Known = keep; }
    }
}
