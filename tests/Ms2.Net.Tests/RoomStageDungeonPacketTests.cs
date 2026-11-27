using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Inbound RoomStageDungeon (CNetworkService::OnRoomStageDungeon @0x14153e180): the player's dungeon stage is created or cleared.</summary>
public class RoomStageDungeonPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void TheOpcodesAreTheClientsRecvOpsOfBothProfiles() {
        Assert.That(((ushort)Inbound.RoomStageDungeon, (ushort)Inbound.DungeonMission), Is.EqualTo(((ushort)0xFF, (ushort)0x100)));
        Assert.That((ProtocolProfile.Gms2.Number(Inbound.RoomStageDungeon), ProtocolProfile.Gms2.Number(Inbound.DungeonMission)), Is.EqualTo((0x101, 0x102)));
    }

    [Test]
    public void KindZeroCreatesTheStageWithRoomStageAndFlag() {
        var p = RoomStageDungeonPacket.Read(R(w => { w.WriteByte(0); w.WriteInt(21001001); w.WriteByte(3); w.WriteBool(true); w.WriteByte(9); }));
        Assert.That(p, Is.EqualTo(new RoomStageDungeonPacket(true, 21001001, 3, true)), "bytes left in the body are not looked at");
    }

    [Test]
    public void KindOneClearsTheStageWithoutReadingMore() {
        var p = RoomStageDungeonPacket.Read(R(w => w.WriteByte(1)));
        Assert.That(p, Is.EqualTo(new RoomStageDungeonPacket(false, 0, 0, false)));
    }

    [Test]
    public void AnotherKindDoesNothing() {
        Assert.That(RoomStageDungeonPacket.Read(R(w => { w.WriteByte(2); w.WriteInt(5); })), Is.Null);
    }
}
