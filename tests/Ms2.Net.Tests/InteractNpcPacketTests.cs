using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class InteractNpcPacketTests
{
    static byte[] BytesOf(Ms2.Net.Wire.IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void OpcodesMatchClientTable()
    {
        // commonfiles/opcodes.h:97 RecvOp::InteractNpc = 0x0059, :355 SendOp::InteractNpc = 0x0032
        Assert.That((ushort)Inbound.InteractNpc, Is.EqualTo(0x0059));
        Assert.That((ushort)Outbound.InteractNpc, Is.EqualTo(0x0032));
    }

    [Test]
    public void RequestWritesObjectIdOnly()
    {
        // CNpc::SendInteractNpc (@0x141094730, unit_14108e1e0_npcactorandvibrate.cpp:1439): i32 GetObjectId()
        var req = new InteractNpcRequest(0x0a0b0c0d);
        Assert.That(req.Op, Is.EqualTo(Outbound.InteractNpc));
        Assert.That(BytesOf(req), Is.EqualTo(new byte[] { 0x0d, 0x0c, 0x0b, 0x0a }));
    }

    [Test]
    public void MessageReadsObjectIdAndTalkableByte()
    {
        // CSceneStateGame::OnInteractNpc (@0x141719990, scenestategame.cpp:3634): i32 object id, then u8 (FUN_14174a560)
        var m = InteractNpcMessage.Read(new ByteReader(new byte[] { 0x39, 0x30, 0, 0, 1 }));
        Assert.That(m.ObjectId, Is.EqualTo(12345));
        Assert.That(m.HasTalkable, Is.True);
        Assert.That(m.Talkable, Is.True);

        var off = InteractNpcMessage.Read(new ByteReader(new byte[] { 0x39, 0x30, 0, 0, 0 }));
        Assert.That(off.Talkable, Is.False);
        Assert.That(off.HasTalkable, Is.True);
    }

    [Test]
    public void MessageWithoutTalkableByteIsAccepted()
    {
        // the byte is read only after FindNpcById succeeds, so an id alone is a complete message
        var m = InteractNpcMessage.Read(new ByteReader(new byte[] { 7, 0, 0, 0 }));
        Assert.That(m.ObjectId, Is.EqualTo(7));
        Assert.That(m.HasTalkable, Is.False);
    }
}
