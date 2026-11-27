using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The DungeonWaiting packet (UIDungeonLobby::OnRoomPacket @0x140e17930, uidungeonlobby.cpp:68): the room id and the second value.</summary>
public class DungeonWaitingPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void TheOpIsTheClientsRecvOp() => Assert.That((ushort)Inbound.DungeonWaiting, Is.EqualTo(0xF0));

    [Test]
    public void TheGms2OpIsTheServersSendOp() => Assert.That(RcLane.Gms2Inbound(Inbound.DungeonWaiting), Is.EqualTo((ushort)0xF2));

    [Test]
    public void ItReadsTheRoomIdAndTheSecondValue() =>
        Assert.That(DungeonWaiting.Read(R(w => { w.WriteInt(21001001); w.WriteInt(3); })), Is.EqualTo(new DungeonWaiting(21001001, 3)));

    [Test]
    public void TheClientLeavesAnyRestUnread() =>
        Assert.That(DungeonWaiting.Read(R(w => { w.WriteInt(1); w.WriteInt(2); w.WriteByte(7); })), Is.EqualTo(new DungeonWaiting(1, 2)));
}
