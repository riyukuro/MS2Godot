using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The RequestPet writers (StateSummon::vfunc_8 @0x1417f7b60, MyPC::SendRequestPet @0x14152b800,
/// CPetManager::SendRequestPet @0x140c8b340) and the pet ride-off actions (rideonaction.cpp:514-560).</summary>
public class PetRequestTests
{
    static string Hex(IOutPacket p, Outbound op)
    {
        var w = new ByteWriter();
        p.Write(w);
        Assert.That(p.Op, Is.EqualTo(op));
        return Convert.ToHexString(w.Buffer.AsSpan(0, w.Length));
    }

    [Test]
    public void RequestPetOpcodeMatchesBothTables()
    {
        // commonfiles/opcodes.h:425 SendOp RequestPet = 0x0078; upstream RecvOp RequestPet = 0x79
        Assert.That((ushort)Outbound.RequestPet, Is.EqualTo(0x0078));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.RequestPet), Is.EqualTo((ushort)0x79));
    }

    [Test]
    public void SummonUnsummonTransferAndAttackBytes()
    {
        const long uid = 0x0102030405060708;
        Assert.That(Hex(new PetSummonRequest(uid), Outbound.RequestPet), Is.EqualTo("00" + "0807060504030201"));
        Assert.That(Hex(new PetUnsummonRequest(uid), Outbound.RequestPet), Is.EqualTo("01" + "0807060504030201"));
        Assert.That(Hex(new PetTransferRequest(uid), Outbound.RequestPet), Is.EqualTo("03" + "0807060504030201"));
        Assert.That(Hex(new PetAttackRequest(true), Outbound.RequestPet), Is.EqualTo("0F01"));
        Assert.That(Hex(new PetAttackRequest(false), Outbound.RequestPet), Is.EqualTo("0F00"));
    }

    [Test]
    public void RideOffPetActionsReadBackAsTheAnswersTypes()
    {
        const long uid = 0x0102030405060708;
        string on = Hex(new RideOffSummonPetOnRequest(uid), Outbound.RequestRide);
        string transfer = Hex(new RideOffSummonPetTransferRequest(uid), Outbound.RequestRide);
        Assert.That(on, Is.EqualTo("010900" + "0807060504030201" + "00"));
        Assert.That(transfer, Is.EqualTo("010A00" + "0807060504030201"));
        // the action after the mode byte is what ReadRideOffAction (rideonaction.cpp:159) gives back in the answer
        var a = RideOffAction.Read(new ByteReader(Convert.FromHexString(on[2..])));
        Assert.That(a, Is.EqualTo(new RideOffSummonPetOn(0, uid, 0)));
        var b = RideOffAction.Read(new ByteReader(Convert.FromHexString(transfer[2..])));
        Assert.That(b, Is.EqualTo(new RideOffSummonPetTransfer(0, uid)));
    }
}
