using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SendOp::CheckCharName (uimessagebox.cpp:2109) and RecvOp::CheckCharNameResult (ReadCharNameResult,
/// unit_1407a4f20_packetserializers.cpp:308); the GMS2 numbers from upstream's RecvOp / SendOp tables.</summary>
public class CheckCharNamePacketTests
{
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Expect(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void TheOpcodeNumbers()
    {
        var k = ProtocolProfile.Kms2525; var g = ProtocolProfile.Gms2;
        Assert.That((k.Number(Outbound.CheckCharName), k.Number(Inbound.CheckCharNameResult)), Is.EqualTo(((ushort)0x7D, (ushort)0xD3)));
        Assert.That((g.Number(Outbound.CheckCharName), g.Number(Inbound.CheckCharNameResult)), Is.EqualTo(((ushort)0x7E, (ushort)0xD5)));
    }

    [Test]
    public void TheRequestIsTheNameThenTheItemUid()
    {
        Assert.That(Bytes(new CheckCharNameRequest("Abc", 0x1122334455L)),
            Is.EqualTo(Expect(w => { w.WriteUnicodeString("Abc"); w.WriteLong(0x1122334455L); })));
    }

    [Test]
    public void TheResultIsTheUsedFlagTheUidAndTheName()
    {
        foreach (byte flag in new byte[] { 0, 1, 2 })
        {
            var w = new ByteWriter(); w.WriteByte(flag); w.WriteLong(77); w.WriteUnicodeString("Abc");
            var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
            Assert.That(CheckCharNameResult.Read(r), Is.EqualTo(new CheckCharNameResult(flag == 1, 77, "Abc")), $"flag {flag}: used only when 1");
            Assert.That(r.Available, Is.EqualTo(0));
        }
    }
}
