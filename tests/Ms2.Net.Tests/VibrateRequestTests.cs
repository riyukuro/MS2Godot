using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Vibrate request (Outbound 0x26, WriteVibrateInfo), checked against every recorded one.</summary>
public class VibrateRequestTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    static VibrateRequest Read(byte[] d) {
        var r = new ByteReader(d);
        var p = new VibrateRequest(r.ReadString(), r.ReadLong(), r.ReadInt(), r.ReadShort(), r.ReadByte(), r.ReadByte(), r.ReadInt(),
            new System.Numerics.Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()));
        Assert.That(r.Available, Is.Zero, "bytes after the position");
        return p;
    }

    static void EveryRecordedRequestIsWrittenBack(string set, ushort op) {
        var all = Gms2Sniffs.Outbound(set, op);
        if (set == "gms2") Assert.That(all, Is.Not.Empty);
        foreach (var (file, d) in all) {
            var p = Read(d);
            Assert.That(p.Name, Is.Not.Empty, file);
            Assert.That(p.SkillId, Is.Positive, file);
            Assert.That(Bytes(p), Is.EqualTo(d), file);
        }
    }

    [Test]
    public void EveryRecordedGms2RequestIsWrittenBack() =>
        EveryRecordedRequestIsWrittenBack("gms2", ProtocolProfile.Gms2.Number(Outbound.Vibrate));

    [Test]
    public void EveryRecordedKmsRequestIsWrittenBack() =>
        EveryRecordedRequestIsWrittenBack("kms2", ProtocolProfile.Kms2525.Number(Outbound.Vibrate));
}
