using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Revival send (SendOp 0x1F) of the revive dialog's buttons.</summary>
public class RevivalPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void TheModesWriteTheirByteAndTheCouponForPaidOnes() {
        Assert.That(new RevivalRequest(0).Op, Is.EqualTo(Outbound.Revival));
        Assert.That((ushort)Outbound.Revival, Is.EqualTo(0x1F));
        Assert.That(Bytes(new RevivalRequest(0)), Is.EqualTo(new byte[] { 0 }));
        Assert.That(Bytes(new RevivalRequest(1, false)), Is.EqualTo(new byte[] { 1, 0 }));
        Assert.That(Bytes(new RevivalRequest(1, true)), Is.EqualTo(new byte[] { 1, 1 }));
        Assert.That(Bytes(new RevivalRequest(2, true)), Is.EqualTo(new byte[] { 2, 1 }));
    }

    [Test]
    public void LeavingTheDungeonIsRequestMoveFieldMode1() {
        Assert.That(new RequestMoveFieldLeave().Op, Is.EqualTo(Outbound.RequestMoveField));
        Assert.That(Bytes(new RequestMoveFieldLeave()), Is.EqualTo(new byte[] { 1 }));
    }
}
