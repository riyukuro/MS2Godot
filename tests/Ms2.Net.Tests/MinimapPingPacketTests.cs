using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class MinimapPingPacketTests
{
    [Test]
    public void OpcodesMatchClientTable()
    {
        // commonfiles/opcodes.h:288 RecvOp::MinimapPing = 0x0121, :480 SendOp::MinimapPing = 0x00B1
        Assert.That((ushort)Inbound.MinimapPing, Is.EqualTo(0x0121));
        Assert.That((ushort)Outbound.MinimapPing, Is.EqualTo(0x00B1));
    }

    [Test]
    public void ReadsUidTypeAndPosition()
    {
        // OnMinimapPing (scenestategame.cpp:6806-6815): i64 uid, u8 type, ReadVector3 (three floats)
        var w = new ByteWriter();
        w.WriteLong(0x1122334455667788); w.WriteByte(1); w.WriteFloat(1.5f); w.WriteFloat(-2f); w.WriteFloat(3f);
        var m = MinimapPingMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(m.Uid, Is.EqualTo(0x1122334455667788));
        Assert.That(m.Type, Is.EqualTo(1));
        Assert.That(m.Position, Is.EqualTo(new Vector3(1.5f, -2f, 3f)));
    }

    [Test]
    public void RequestWritesCharacterIdKindAndPoint()
    {
        // SendMinimapPing (uiminimapentity.cpp:2557-2561): Encode8 character id, Encode1 kind, EncodeVector3
        var w = new ByteWriter();
        new MinimapPingRequest(42, 1, new Vector3(10f, 20f, 0f)).Write(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That(w.Length, Is.EqualTo(8 + 1 + 12));
        Assert.That(r.ReadLong(), Is.EqualTo(42));
        Assert.That(r.ReadByte(), Is.EqualTo(1));
        Assert.That((r.ReadFloat(), r.ReadFloat(), r.ReadFloat()), Is.EqualTo((10f, 20f, 0f)));
    }
}
