using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillCancel (0x40 GMS2): the cast uid and the actor id.</summary>
public class SkillCancelTests {
    // gms2 recording: packet 2535, 12 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x74, 0x0E, 0x3D, 0x8D, 0x73, 0x35, 0x00, 0x00, 0xD5, 0x8C, 0x11, 0x00,
    };

    [Test] public void Gms2SkillCancelReadsTheCast() {
        var r = new ByteReader(Gms2);
        Assert.That(SkillCancel.Read(r), Is.EqualTo(new SkillCancel(58770407100020L, 1150165)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
