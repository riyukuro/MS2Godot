using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Mail packet (0x54, CSceneStateGame::OnMail and CMailInfo::Read) and the mail list's sends (Mail 0x2D).</summary>
public class MailPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void TheRequestsWriteTheirModeAndIds() {
        Assert.That(new MailListRequest().Op, Is.EqualTo(Outbound.Mail));
        Assert.That(Bytes(new MailListRequest()), Is.EqualTo(new byte[] { 0 }));
        Assert.That(Bytes(new MailReadRequest(0x0102)), Is.EqualTo(new byte[] { 2, 2, 1, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new MailCollectRequest(3)), Is.EqualTo(new byte[] { 0xb, 3, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new MailDeleteRequest(new long[] { 4, 5 })),
            Is.EqualTo(new byte[] { 0xd, 2, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new MailReadListRequest(new long[] { 6 })), Is.EqualTo(new byte[] { 0x12, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new MailCollectListRequest(Array.Empty<long>())), Is.EqualTo(new byte[] { 0x13, 0, 0, 0, 0 }));
    }

    static void WriteMail(ByteWriter w, byte kind, long id, string sender, string title) {
        w.WriteByte(kind); w.WriteLong(id); w.WriteLong(77);
        w.WriteUnicodeString(sender); w.WriteUnicodeString(title); w.WriteUnicodeString("body");
        w.WriteUnicodeString(""); w.WriteUnicodeString("");
        w.WriteByte(0);                                                 // no attachment
        w.WriteLong(1000); w.WriteLong(0);                              // meso, not claimed
        w.WriteLong(0); w.WriteLong(0);
        w.WriteLong(0); w.WriteLong(0);
        w.WriteByte(1); w.WriteByte(2); w.WriteByte(1); w.WriteLong(30); w.WriteLong(0);   // a merat benefit
        w.WriteLong(0); w.WriteLong(5000); w.WriteLong(4000);           // unread, expiry, sent
        w.WriteUnicodeString("");
    }

    [Test]
    public void Mode0ReadsEachMail() {
        var r = R(w => { w.WriteByte(0); w.WriteInt(2); WriteMail(w, 1, 10, "Ann", "hi"); WriteMail(w, 101, 11, "sys", "t"); });
        var m = (MailMessage.Listed)MailMessage.Read(r);
        Assert.That(r.Available, Is.Zero, "the whole body is read");
        Assert.That(m.Mails.Select(x => (x.Kind, x.Id, x.Sender, x.Title)), Is.EqualTo(new[] { ((byte)1, 10L, "Ann", "hi"), ((byte)101, 11L, "sys", "t") }));
        var a = m.Mails[0];
        Assert.That((a.Meso.Begin, a.Meso.IsUnclaimed, a.ExpireTime, a.SentTime, a.IsRead), Is.EqualTo((1000L, true, 5000L, 4000L, false)));
        Assert.That(a.Benefits.Single().Amount, Is.EqualTo(30));
        Assert.That(a.HasAttachment(), Is.True);
    }

    [Test]
    public void TheOtherModesReadTheirFields() {
        var read = (MailMessage.MarkedRead)MailMessage.Read(R(w => { w.WriteByte(2); w.WriteLong(10); w.WriteLong(99); }));
        Assert.That((read.Id, read.ReadTime, read.Mode), Is.EqualTo((10L, 99L, (byte)2)));
        var c = (MailMessage.Collected)MailMessage.Read(R(w => { w.WriteByte(0xa); w.WriteLong(10); w.WriteByte(1); w.WriteByte(3); w.WriteLong(5); }));
        Assert.That((c.Id, c.Attachment, c.Slot, c.Time), Is.EqualTo((10L, true, 3, 5L)));
        var n = (MailMessage.New)MailMessage.Read(R(w => { w.WriteByte(0xe); w.WriteInt(4); w.WriteBool(true); w.WriteInt(1); }));
        Assert.That((n.Unread, n.Alarm, n.UnreadAlarm), Is.EqualTo((4, true, 1)));
        Assert.That(MailMessage.Read(R(w => { w.WriteByte(0xd); w.WriteLong(10); })), Is.EqualTo(new MailMessage.Deleted(10) { Mode = 0xd }));
        Assert.That(MailMessage.Read(R(w => w.WriteByte(0x99))), Is.InstanceOf<MailMessage.Other>());
    }
}
