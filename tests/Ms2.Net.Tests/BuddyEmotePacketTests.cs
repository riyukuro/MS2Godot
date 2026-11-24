using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class BuddyEmotePacketTests
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

    static byte[] Bytes(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void OpcodesMatchTheClientAndUpstreamTables()
    {
        // commonfiles/opcodes.h:269 RecvOp::BuddyEmote = 0x010D, :466 SendOp::BuddyEmote = 0x00A3;
        // upstream SendOp.BuddyEmote = 0x010F, RecvOp.BuddyEmote = 0x00A4
        Assert.That((ushort)Inbound.BuddyEmote, Is.EqualTo(0x010D));
        Assert.That((ushort)Outbound.BuddyEmote, Is.EqualTo(0x00A3));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.BuddyEmote), Is.EqualTo((ushort)0x010F));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.BuddyEmote), Is.EqualTo((ushort)0x00A4));
    }

    [Test]
    public void EachModeReadsItsFieldsToTheEnd()
    {
        // OnBuddyEmote (networkservice.cpp:1096-1184): the fields of each mode, nothing after them
        var r = Reader(w => { w.WriteByte(0); w.WriteInt(90220001); w.WriteLong(4001); w.WriteUnicodeString("Asker"); });
        Assert.That(BuddyEmoteMessage.Read(r), Is.EqualTo(new BuddyEmoteMessage.Invite(90220001, 4001, "Asker")));
        Assert.That(r.Available, Is.EqualTo(0));

        r = Reader(w => { w.WriteByte(1); w.WriteLong(4002); });
        Assert.That(BuddyEmoteMessage.Read(r), Is.EqualTo(new BuddyEmoteMessage.InviteSent(4002)));
        Assert.That(r.Available, Is.EqualTo(0));

        r = Reader(w => { w.WriteByte(2); w.WriteByte(7); });
        Assert.That(BuddyEmoteMessage.Read(r), Is.EqualTo(new BuddyEmoteMessage.Failed(7)));
        Assert.That(r.Available, Is.EqualTo(0));

        r = Reader(w => { w.WriteByte(3); w.WriteInt(90220002); w.WriteLong(4002); });
        Assert.That(BuddyEmoteMessage.Read(r), Is.EqualTo(new BuddyEmoteMessage.Accepted(90220002, 4002)));
        Assert.That(r.Available, Is.EqualTo(0));

        r = Reader(w => { w.WriteByte(4); w.WriteInt(90220002); w.WriteLong(4002); });
        Assert.That(BuddyEmoteMessage.Read(r), Is.EqualTo(new BuddyEmoteMessage.Declined(90220002, 4002)));
        Assert.That(r.Available, Is.EqualTo(0));

        r = Reader(w =>
        {
            w.WriteByte(5); w.WriteInt(90220003); w.WriteLong(4001); w.WriteLong(4002);
            w.WriteFloat(-900f); w.WriteFloat(-875.5f); w.WriteFloat(3001f);
            w.WriteFloat(0f); w.WriteFloat(0f); w.WriteFloat(135f);
            w.WriteInt(123456);
        });
        Assert.That(BuddyEmoteMessage.Read(r), Is.EqualTo(new BuddyEmoteMessage.Start(90220003, 4001, 4002,
            new Vector3(-900f, -875.5f, 3001f), new Vector3(0f, 0f, 135f), 123456)));
        Assert.That(r.Available, Is.EqualTo(0));

        r = Reader(w => { w.WriteByte(6); w.WriteInt(90220003); w.WriteLong(4001); });
        Assert.That(BuddyEmoteMessage.Read(r), Is.EqualTo(new BuddyEmoteMessage.Left(90220003, 4001)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void AnotherModeIsNothing()
    {
        // the switch has no default: mode 7 reads nothing more
        var r = Reader(w => { w.WriteByte(7); w.WriteInt(1); });
        Assert.That(BuddyEmoteMessage.Read(r), Is.Null);
        Assert.That(r.Available, Is.EqualTo(4));
    }

    [Test]
    public void TheRequestsWriteTheClientsBytes()
    {
        Assert.That(BytesOf(new BuddyEmoteInviteRequest(90220001, 4002)),
            Is.EqualTo(Bytes(w => { w.WriteByte(0); w.WriteInt(90220001); w.WriteLong(4002); })));
        Assert.That(BytesOf(new BuddyEmoteInviteReceived(4001)), Is.EqualTo(Bytes(w => { w.WriteByte(1); w.WriteLong(4001); })));
        Assert.That(BytesOf(new BuddyEmoteRefuse(4001, 8)),
            Is.EqualTo(Bytes(w => { w.WriteByte(2); w.WriteLong(4001); w.WriteByte(8); })));
        Assert.That(BytesOf(new BuddyEmoteAccept(90220001, 4001, new Vector3(1, 2, 3), new Vector3(4, 5, 6), 90f)),
            Is.EqualTo(Bytes(w =>
            {
                w.WriteByte(3); w.WriteInt(90220001); w.WriteLong(4001);
                w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(4); w.WriteFloat(5); w.WriteFloat(6);
                w.WriteFloat(90f);
            })));
        Assert.That(BytesOf(new BuddyEmoteDecline(90220001, 4001)),
            Is.EqualTo(Bytes(w => { w.WriteByte(4); w.WriteInt(90220001); w.WriteLong(4001); })));
        Assert.That(BytesOf(new BuddyEmoteCancel(90220001, 4002)),
            Is.EqualTo(Bytes(w => { w.WriteByte(6); w.WriteInt(90220001); w.WriteLong(4002); })));
    }
}
