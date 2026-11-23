using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>UserState (0x37 GMS2, 0x36 KMS2): the object id and one state byte.</summary>
public class UserStateTests {
    // gms2 recording: packet 2300, 5 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x7F, 0xC0, 0x06, 0x00, 0x05,
    };
    // kms2 recording: packet 579, 5 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0xEC, 0x6A, 0xAD, 0x00, 0x05,
    };

    [Test] public void Gms2UserStateReadsTheActor() {
        var r = new ByteReader(Gms2);
        Assert.That(UserState.Read(r), Is.EqualTo(new UserState(442495, (byte) 5)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsUserStateReadsTheActor() {
        var r = new ByteReader(Kms);
        Assert.That(UserState.Read(r), Is.EqualTo(new UserState(11365100, (byte) 5)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
