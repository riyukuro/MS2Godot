using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Ping (0x06): one i32 sequence. The GMS2 and KMS2 bodies are the same 4 bytes.</summary>
public class PingRecordedTests {
    // gms2 recording: packet 2248, 4 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x08, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 639, 4 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x19, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2PingReadsTheSequence() {
        var r = new ByteReader(Gms2);
        Assert.That(Ping.Read(r).Sequence, Is.EqualTo(8));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsPingReadsTheSequence() {
        var r = new ByteReader(Kms);
        Assert.That(Ping.Read(r).Sequence, Is.EqualTo(25));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
