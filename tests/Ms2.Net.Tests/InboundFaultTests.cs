using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The warning of a packet whose handling threw (<see cref="InboundFault"/>): opcode, name, size, reader, exception.</summary>
public class InboundFaultTests {
    static byte[] Body(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    static string Fault(RawPacket p, ProtocolProfile profile, Action<RawPacket> handle) {
        try { handle(p); }
        catch (Exception ex) { return InboundFault.Describe(p, profile, ex); }
        Assert.Fail("the packet did not throw");
        return null;
    }

    [TestCase("KMS")]
    [TestCase("GMS2")]
    public void APlaySystemSoundWithBytesLeftNamesItsOpcodeAndReader(string set) {
        var profile = set == "KMS" ? ProtocolProfile.Kms2525 : ProtocolProfile.Gms2;
        var p = new RawPacket(profile.Number(Inbound.PlaySystemSound), Body(w => { w.WriteUnicodeString("abc"); w.WriteShort(7); }));
        var text = Fault(p, profile, q => SystemSound.Read(q.Reader()));
        Assert.That(text, Is.EqualTo($"packet 0x{p.Opcode:X4} PlaySystemSound ({p.Body.Length} bytes) read by SystemSound.Read: "
            + "PacketLayoutException: PlaySystemSound: 2 bytes left over"));
    }

    [Test]
    public void ATruncatedPacketNamesItsReader() {
        var profile = ProtocolProfile.Kms2525;
        var p = new RawPacket(profile.Number(Inbound.FieldAddUser), new byte[] { 1, 0, 0, 0, 2 });
        var text = Fault(p, profile, q => FieldAddUser.Read(q.Reader(), profile));
        Assert.That(text, Does.StartWith($"packet 0x{p.Opcode:X4} FieldAddUser (5 bytes) read by "));
        Assert.That(text, Does.Contain("FieldAddUser.Read"));
    }

    [Test]
    public void AHandlerThatThrowsOutsideAReaderIsNamedByItsMethod() {
        var p = new RawPacket(0xFFFF, new byte[3]);
        var text = Fault(p, ProtocolProfile.Kms2525, _ => throw new InvalidOperationException("no field"));
        Assert.That(text, Does.StartWith("packet 0xFFFF (3 bytes) not in a reader, at InboundFaultTests."));
        Assert.That(text, Does.EndWith(": InvalidOperationException: no field"));
    }

    [Test]
    public void WithoutAProfileTheOpcodeAlone() {
        var p = new RawPacket(0x00C3, Body(w => { w.WriteUnicodeString("abc"); w.WriteByte(1); }));
        Assert.That(Fault(p, null, q => SystemSound.Read(q.Reader())), Does.StartWith("packet 0x00C3 (9 bytes) read by SystemSound.Read: "));
    }
}
