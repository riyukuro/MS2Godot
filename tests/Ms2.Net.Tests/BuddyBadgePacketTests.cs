using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class BuddyBadgePacketTests
{
    static byte[] BytesOf(Ms2.Net.Wire.IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void OpcodesMatchTheClientAndUpstreamTables()
    {
        // commonfiles/opcodes.h:270 RecvOp::BuddyBadge = 0x010E, :467 SendOp::BuddyBadge = 0x00A4;
        // upstream SendOp.BuddyBadge = 0x0110, RecvOp.BuddyBadge = 0x00A5
        Assert.That((ushort)Inbound.BuddyBadge, Is.EqualTo(0x010E));
        Assert.That((ushort)Outbound.BuddyBadge, Is.EqualTo(0x00A4));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.BuddyBadge), Is.EqualTo((ushort)0x0110));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.BuddyBadge), Is.EqualTo((ushort)0x00A5));
    }

    [Test]
    public void EachKindReadsItsFieldsToTheEnd()
    {
        // OnBuddyBadge (networkservice.cpp:1187-1202): u8 kind, i64 the character; kind 3 then i64 the item uid and the name
        foreach (byte kind in new byte[] { 0, 1, 2, 4, 0xFF })
        {
            var r = Reader(w => { w.WriteByte(kind); w.WriteLong(4001); });
            Assert.That(BuddyBadgeMessage.Read(r), Is.EqualTo(new BuddyBadgeMessage(kind, 4001, 0, null)), $"kind {kind}");
            Assert.That(r.Available, Is.EqualTo(0), $"kind {kind}");
        }
        var n = Reader(w => { w.WriteByte(3); w.WriteLong(4001); w.WriteLong(0x0123456789ABCDEFL); w.WriteUnicodeString("Partner"); });
        Assert.That(BuddyBadgeMessage.Read(n), Is.EqualTo(new BuddyBadgeMessage(3, 4001, 0x0123456789ABCDEFL, "Partner")));
        Assert.That(n.Available, Is.EqualTo(0));
        Assert.That((BuddyBadgeMessage.Show, BuddyBadgeMessage.Hide, BuddyBadgeMessage.ShowToNewcomer, BuddyBadgeMessage.PartnerName),
            Is.EqualTo(((byte)0, (byte)1, (byte)2, (byte)3)));
    }

    [Test]
    public void TheRequestsWriteTheClientsBytes()
    {
        // CoupleEffectSystem::SetEffectMode: u8 mode, i64 GetCharacterId
        Assert.That(BytesOf(new BuddyBadgeModeRequest(0, 0x0102030405060708L)),
            Is.EqualTo(new byte[] { 0, 8, 7, 6, 5, 4, 3, 2, 1 }));
        Assert.That(BytesOf(new BuddyBadgeModeRequest(1, 7)), Is.EqualTo(new byte[] { 1, 7, 0, 0, 0, 0, 0, 0, 0 }));
        // FUN_140b0ead0: u8 2, i64 GetCharacterId, i64 the newcomer
        Assert.That(BytesOf(new BuddyBadgeNewcomerRequest(7, 9)),
            Is.EqualTo(new byte[] { 2, 7, 0, 0, 0, 0, 0, 0, 0, 9, 0, 0, 0, 0, 0, 0, 0 }));
        // SendStartRequest: u8 3, i64 the partner
        Assert.That(BytesOf(new BuddyBadgeNameRequest(0x1122L)), Is.EqualTo(new byte[] { 3, 0x22, 0x11, 0, 0, 0, 0, 0, 0 }));
        Assert.That(new Ms2.Net.Wire.IOutPacket[] { new BuddyBadgeModeRequest(0, 1), new BuddyBadgeNewcomerRequest(1, 2), new BuddyBadgeNameRequest(1) }
            .Select(p => p.Op), Is.All.EqualTo(Outbound.BuddyBadge));
    }
}
