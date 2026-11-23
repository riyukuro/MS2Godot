using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Fishing (0xC6 GMS2) mode 8 (a catch result, with the fish record) and mode 7 (the caught-fish list). The recorded
/// mode 7 body is empty (count 0).</summary>
public class FishingRecordedTests {
    // gms2 recording: packet 4787, 27 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Result = new byte[] {
        0x08, 0x97, 0x96, 0x98, 0x00, 0x18, 0x00, 0x00, 0x00, 0x01, 0x00, 0x97, 0x96, 0x98, 0x00, 0x79,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x75, 0x00, 0x00, 0x00,
    };
    // gms2 recording: packet 2216, 5 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2List = new byte[] {
        0x07, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2ResultReadsTheCatch() {
        var r = new ByteReader(Gms2Result);
        var f = FishingMessage.Read(r) as FishingMessage.Result;
        Assert.That(f, Is.Not.Null);
        Assert.That((f.FishId, f.Size, f.Success, f.Auto), Is.EqualTo((10000023u, 24u, true, false)));
        Assert.That(f.Fish, Is.Not.Null);
        Assert.That(f.Fish.Value, Is.EqualTo(new FishingMessage.Record(10000023u, 121, 1, 117u)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2EmptyListIsConsumed() {
        var r = new ByteReader(Gms2List);
        var f = FishingMessage.Read(r) as FishingMessage.List;
        Assert.That(f, Is.Not.Null);
        Assert.That(f.Fishes, Is.Empty);
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
