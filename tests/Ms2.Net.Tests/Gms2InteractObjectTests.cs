using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 InteractObject requests (Outbound 0x31 modes 0x0b and 0x0c), checked against the recordings.</summary>
public class Gms2InteractObjectTests {
    static byte[] Bytes(IProfileOutPacket p, ProtocolProfile profile) { var w = new ByteWriter(); p.Write(w, profile); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void EveryRecordedGms2RequestIsTheModeAndOneName() {
        var all = Gms2Sniffs.Outbound("gms2", ProtocolProfile.Gms2.Number(Outbound.InteractObject));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, d) in all) {
            Assert.That(d[0], Is.AnyOf((byte) 0x0b, (byte) 0x0c), file);
            var r = new ByteReader(d);
            r.ReadByte();
            string name = r.ReadString();
            Assert.That(r.Available, Is.Zero, $"{file}: bytes after the name");
            Assert.That(name, Is.Not.Empty, file);
            IProfileOutPacket p = d[0] == 0x0b ? new InteractObjectStartRequest(name) : new InteractObjectCompleteRequest(name);
            Assert.That(Bytes(p, ProtocolProfile.Gms2), Is.EqualTo(d), file);
        }
    }

    [Test]
    public void TheKmsRequestCountsItsNames() {
        var w = new ByteWriter();
        w.WriteByte(0x0b); w.WriteInt(2); w.WriteString("ab"); w.WriteString("cd");
        Assert.That(Bytes(new InteractObjectStartRequest(new[] { "ab", "cd" }), ProtocolProfile.Kms2525), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
        w = new ByteWriter();
        w.WriteByte(0x0c); w.WriteInt(1); w.WriteString("ab");
        Assert.That(Bytes(new InteractObjectCompleteRequest("ab"), ProtocolProfile.Kms2525), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    [Test]
    public void TheGms2RequestSendsTheFirstNameOnly() {
        var w = new ByteWriter();
        w.WriteByte(0x0c); w.WriteString("ab");
        Assert.That(Bytes(new InteractObjectCompleteRequest("ab"), ProtocolProfile.Gms2), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }
}
