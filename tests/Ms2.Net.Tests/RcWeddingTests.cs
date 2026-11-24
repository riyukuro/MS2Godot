using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Wedding (KMS 0x128, GMS2 0x12A): CWeddingManager::OnPacket (weddingmanager.cpp:198). Recorded on GMS2: modes 0 (84 bytes), 1 (94 bytes) and 0x17 (1 byte); the other modes follow the client's handlers.</summary>
public class RcWeddingTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static ByteReader ZeroBody(byte mode, int len) { var b = new byte[len]; b[0] = mode; return new ByteReader(b); }

    static void Ticket(ByteWriter w) {
        w.WriteLong(11); w.WriteInt(2); w.WriteInt(3); w.WriteLong(1700000000); w.WriteBool(true); w.WriteLong(5); w.WriteLong(6);
        w.WriteUnicodeString("Gro"); w.WriteUnicodeString("Bri");
        for (int i = 0; i < 5; i++) w.WriteLong(100 + i);
        w.WriteInt(77);
    }

    // recorded: 1180 packets, 84 bytes (all zero)
    [Test] public void EmptyProxyIs83ZeroBytes() {
        var m = WeddingPacket.Read(ZeroBody(0, 84));
        Assert.That(m.Mode, Is.EqualTo(0));
        Assert.That(m.Proxy.State, Is.EqualTo(0));
        Assert.That(m.Proxy.Groom.Name, Is.EqualTo(""));
        Assert.That(m.Proxy.Log, Is.Empty);
    }

    [Test] public void ProxyFieldsKeepTheirOrder() {
        var m = WeddingPacket.Read(R(w => {
            w.WriteByte(0); w.WriteLong(1); w.WriteByte(3); w.WriteLong(2); w.WriteLong(3);
            w.WriteLong(10); w.WriteLong(11); w.WriteUnicodeString("A"); w.WriteBool(true);
            w.WriteLong(20); w.WriteLong(21); w.WriteUnicodeString("B"); w.WriteBool(false);
            w.WriteLong(999); w.WriteUnicodeString("w0"); w.WriteUnicodeString("w1"); w.WriteString("a0"); w.WriteString("a1");
            w.WriteInt(2); w.WriteShort(4); w.WriteLong(50); w.WriteLong(60); w.WriteShort(5); w.WriteLong(-7); w.WriteLong(61);
        }));
        Assert.That((m.Proxy.N18, m.Proxy.State, m.Proxy.N28, m.Proxy.N30), Is.EqualTo((1L, (byte) 3, 2L, 3L)));
        Assert.That(m.Proxy.Groom, Is.EqualTo(new WeddingPartner(10, 11, "A", true)));
        Assert.That(m.Proxy.Bride, Is.EqualTo(new WeddingPartner(20, 21, "B", false)));
        Assert.That((m.Proxy.Meso, m.Proxy.MessageW0, m.Proxy.MessageW1, m.Proxy.MessageA0, m.Proxy.MessageA1), Is.EqualTo((999L, "w0", "w1", "a0", "a1")));
        Assert.That(m.Proxy.Log, Is.EqualTo(new[] { new WeddingLogEntry(4, 50, 60), new WeddingLogEntry(5, -7, 61) }));
    }

    // recorded: 1180 packets, 94 bytes (all zero)
    [Test] public void EmptyTicketListIs93ZeroBytes() {
        var m = WeddingPacket.Read(ZeroBody(1, 94));
        Assert.That(m.Ticket.HallSn, Is.EqualTo(0));
        Assert.That(m.Invited, Is.Empty);
    }

    [Test] public void TicketListHoldsTicketAndInvitedPairs() {
        var m = WeddingPacket.Read(R(w => { w.WriteByte(1); Ticket(w); w.WriteInt(2); w.WriteLong(1); w.WriteLong(2); w.WriteLong(3); w.WriteLong(4); }));
        Assert.That((m.Ticket.HallSn, m.Ticket.PackageId, m.Ticket.HallId, m.Ticket.Time, m.Ticket.Public), Is.EqualTo((11L, 2, 3, 1700000000L, true)));
        Assert.That((m.Ticket.BookingAccSn, m.Ticket.BookingCharSn, m.Ticket.Groom, m.Ticket.Bride, m.Ticket.ReservationCouponId), Is.EqualTo((5L, 6L, "Gro", "Bri", 77)));
        Assert.That(m.Ticket.Fee, Is.EqualTo(new WeddingHallFee(100, 101, 102, 103, 104)));
        Assert.That(m.Invited, Is.EqualTo(new[] { new KeyValuePair<long, long>(1, 2), new KeyValuePair<long, long>(3, 4) }));
    }

    // recorded: 722 packets, 1 byte
    [Test] public void InvitationClearHasNoBody() {
        Assert.That(WeddingPacket.Read(new ByteReader(new byte[] { 0x17 })).Mode, Is.EqualTo(0x17));
        Assert.Throws<PacketLayoutException>(() => WeddingPacket.Read(new ByteReader(new byte[] { 0x17, 0 })));
    }

    [Test] public void ResultAndDeclineAreOneByte() {
        Assert.That(WeddingPacket.Read(new ByteReader(new byte[] { 2, 0x12 })).Result, Is.EqualTo(0x12));
        Assert.That(WeddingPacket.Read(new ByteReader(new byte[] { 0x1F, 3 })).Result, Is.EqualTo(3));
    }

    [Test] public void ProposeIsIdAndName() {
        var m = WeddingPacket.Read(R(w => { w.WriteByte(3); w.WriteLong(0x1234); w.WriteUnicodeString("Lover"); }));
        Assert.That((m.Id, m.Name), Is.EqualTo((0x1234L, "Lover")));
    }

    [Test] public void ProposeAcceptedIsAProxy() {
        var m = WeddingPacket.Read(ZeroBody(6, 84));
        Assert.That(m.Proxy, Is.Not.Null);
    }

    [Test] public void ReservationTicketsReadOneTicket() {
        foreach (byte mode in new byte[] { 0xD, 0xF }) {
            var m = WeddingPacket.Read(R(w => { w.WriteByte(mode); Ticket(w); }));
            Assert.That(m.Ticket.HallId, Is.EqualTo(3));
        }
    }

    [Test] public void ChangeRequestAndChangeOkAddCost() {
        foreach (byte mode in new byte[] { 0x14, 0x16 }) {
            var m = WeddingPacket.Read(R(w => { w.WriteByte(mode); Ticket(w); w.WriteLong(-5000); }));
            Assert.That((m.Ticket.PackageId, m.Cost), Is.EqualTo((2, -5000L)));
        }
    }

    [Test] public void InvitationTicketStartsWithItsId() {
        var m = WeddingPacket.Read(R(w => { w.WriteByte(0x18); w.WriteLong(9); Ticket(w); }));
        Assert.That((m.Id, m.Ticket.HallSn), Is.EqualTo((9L, 11L)));
        Assert.That(WeddingPacket.Read(R(w => { w.WriteByte(0x19); w.WriteLong(9); })).Id, Is.EqualTo(9));
    }

    [Test] public void ReservationChangeRequestIsAShortReason() {
        Assert.That(WeddingPacket.Read(R(w => { w.WriteByte(0x11); w.WriteShort(0x22); })).Reason, Is.EqualTo(0x22));
    }

    [Test] public void StepResultNamesThePartnerOnlyOnResult3() {
        Assert.That(WeddingPacket.Read(R(w => { w.WriteByte(0x1E); w.WriteByte(3); w.WriteUnicodeString("Mate"); })).Name, Is.EqualTo("Mate"));
        Assert.That(WeddingPacket.Read(R(w => { w.WriteByte(0x1E); w.WriteByte(1); })).Name, Is.EqualTo(""));
    }

    [Test] public void EmotionResultAcceptedReadsIdAndFlags() {
        var m = WeddingPacket.Read(R(w => { w.WriteByte(0x27); w.WriteByte(2); w.WriteInt(5); w.WriteBool(true); w.WriteBool(true); w.WriteInt(8); }));
        Assert.That((m.EmotionId, m.EmotionFlagB2D, m.EmotionFlagB2C, m.EmotionValue), Is.EqualTo((5, true, true, 8)));
        var n = WeddingPacket.Read(R(w => { w.WriteByte(0x27); w.WriteByte(2); w.WriteInt(5); w.WriteBool(false); w.WriteBool(false); }));
        Assert.That(n.EmotionValue, Is.EqualTo(0));
        Assert.That(WeddingPacket.Read(new ByteReader(new byte[] { 0x27, 1 })).Result, Is.EqualTo(1));
    }

    [Test] public void BodylessModesAreRefusedWithTrailingBytes() {
        foreach (byte mode in new byte[] { 8, 0x10, 0x13, 0x1D, 0x23 }) {
            Assert.That(WeddingPacket.Read(new ByteReader(new byte[] { mode })).Mode, Is.EqualTo(mode));
            Assert.Throws<PacketLayoutException>(() => WeddingPacket.Read(new ByteReader(new byte[] { mode, 1 })));
        }
    }

    [Test] public void GuestFinderAnswerIsKeptRaw() {
        Assert.That(WeddingPacket.Read(new ByteReader(new byte[] { 0x1B, 1, 2, 3 })).RawBody, Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test] public void UnknownModeReadsNothingMore() => Assert.That(WeddingPacket.Read(new ByteReader(new byte[] { 0x7E, 1 })).Mode, Is.EqualTo(0x7E));

    [Test] public void ImpossibleCountsAreRefused() {
        Assert.Throws<InvalidDataException>(() => WeddingPacket.Read(R(w => { w.WriteByte(1); Ticket(w); w.WriteInt(1000); w.WriteLong(1); })));
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.Wedding));
        Assert.That(all, Is.Not.Empty);
        var modes = new HashSet<byte>();
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = WeddingPacket.Read(r);
            modes.Add(m.Mode);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
        Assert.That(modes, Is.EquivalentTo(new byte[] { 0, 1, 0x17 }));
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.Wedding), Is.EqualTo((ushort) 0x128));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.Wedding), Is.EqualTo((ushort) 0x12A));
    }
}
