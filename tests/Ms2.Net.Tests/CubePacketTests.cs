using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class CubePacketTests
{
    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void OpcodesMatchClientTable()
    {
        // commonfiles/opcodes.h:113 RecvOp::ResponseCube = 0x0069; SendOp::RequestCube = 0x0037
        Assert.That((ushort)Inbound.ResponseCube, Is.EqualTo(0x0069));
        Assert.That((ushort)Outbound.RequestCube, Is.EqualTo(0x0037));
    }

    [Test]
    public void HeldItemReleaseIsRequestCubeWithOnlyMode12()
    {
        // PC::SendRequestCube (pc.cpp:1929-1937): WriteUInt8(0x12) and nothing else
        var p = new HeldItemReleaseRequest();
        var w = new ByteWriter();
        p.Write(w);
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestCube));
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 0x12 }));
    }

    [Test]
    public void ResponseCubeMode12ReadsErrorThenId()
    {
        // OnResponseCube (scenestategame.cpp:9876) u8 mode; case 0x12 (:10176-10192): u8 error, i32 id
        var ok = ResponseCubeHeldItem.Read(Reader(w => { w.WriteByte(0x12); w.WriteByte(0); w.WriteInt(1234); }));
        Assert.That(ok, Is.EqualTo(new ResponseCubeHeldItem(0, 1234)));
        var err = ResponseCubeHeldItem.Read(Reader(w => { w.WriteByte(0x12); w.WriteByte(3); w.WriteInt(77); }));
        Assert.That(err, Is.EqualTo(new ResponseCubeHeldItem(3, 77)));
        Assert.That(ResponseCubeHeldItem.Read(Reader(w => { w.WriteByte(0x11); w.WriteByte(0); w.WriteInt(1); })), Is.Null);
    }
}
