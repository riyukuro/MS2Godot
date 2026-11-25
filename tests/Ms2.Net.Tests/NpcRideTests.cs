using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// RecvOp NpcRide (KMS 0xE7, commonfiles/opcodes.h:235; GMS2 0xE9 inferred): CSceneStateGame::OnNpcRide (@0x141738ad0,
/// scenestategame.cpp:4871-4877) and CNpc::FUN_141092c50 (@0x141092c50, unit_14108e1e0_npcactorandvibrate.cpp:1036-1046).
/// Neither server sends it and no recording of either corpus carries it, so the bytes are the layout the client reads.
/// </summary>
public class NpcRideTests
{
    [Test]
    public void TheNumbersAreKms0xE7AndGms20xE9()
    {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.NpcRide), Is.EqualTo(0xE7));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.NpcRide), Is.EqualTo(0xE9));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.BypassKey), Is.EqualTo(0xE8), "its neighbours, KMS2 + 2");
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.NpcNotice), Is.EqualTo(0xEA));
    }

    [Test]
    public void ModeZeroReadsTheAccumulatedDamage()
    {
        var n = NpcRide.Read(new ByteReader(RcLane.Bytes("3930000000" + "0001000000020000")));
        Assert.That((n.ObjectId, n.Mode, n.AccumulatedDamage, n.Owner), Is.EqualTo((12345, NpcRide.Damage, 0x0000_0200_0000_0100L, 0)));
    }

    [Test]
    public void ModeOneReadsTheOwnerAndTwoReadsNothing()
    {
        var n = NpcRide.Read(new ByteReader(RcLane.Bytes("39300000" + "01" + "40E20100")));
        Assert.That((n.ObjectId, n.Mode, n.Owner), Is.EqualTo((12345, NpcRide.Ride, 123456)));
        n = NpcRide.Read(new ByteReader(RcLane.Bytes("39300000" + "02")));
        Assert.That((n.Mode, n.Owner, n.AccumulatedDamage), Is.EqualTo((NpcRide.RideEnd, 0, 0L)));
    }

    [Test]
    public void AnotherModeReadsNoMore()
    {
        // the client's mode chain has no else: the rest stays unread
        var n = NpcRide.Read(new ByteReader(RcLane.Bytes("39300000" + "05" + "AABBCC")));
        Assert.That((n.ObjectId, n.Mode, n.Owner), Is.EqualTo((12345, (byte)5, 0)));
    }
}
