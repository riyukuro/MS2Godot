using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>FieldProperty (0xCC GMS2, 0xCA KMS2) mode 2 (remove): a u8 kind and nothing else. The 2-byte recorded bodies are
/// the same on both regions. Modes 0 and 1 of GMS2 lights differ in width and are left as an open difference.</summary>
public class FieldPropertyRemoveTests {
    // gms2 recording: packet 2955, 2 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x02, 0x03,
    };
    // kms2 recording: packet 10966, 2 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x02, 0x03,
    };

    [Test] public void Gms2RemoveReadsTheKind() {
        var r = new ByteReader(Gms2);
        var m = FieldPropertyMessage.Read(r) as FieldPropertyMessage.Remove;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Kind, Is.EqualTo((byte) 3));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsRemoveReadsTheKind() {
        var r = new ByteReader(Kms);
        var m = FieldPropertyMessage.Read(r) as FieldPropertyMessage.Remove;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Kind, Is.EqualTo((byte) 3));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
