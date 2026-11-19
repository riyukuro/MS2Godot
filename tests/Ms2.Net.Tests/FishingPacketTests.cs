using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Fishing 0x73 modes 7 and 8, the part the fishing book reads (CFishingManager::FUN_140cd9840, fishingmanager.cpp:546).</summary>
public class FishingPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test] public void ListReadsTheCountAndFourIntsPerFish() {
        var r = R(w => { w.WriteByte(7); w.WriteInt(2);
            w.WriteInt(20000001); w.WriteInt(5); w.WriteInt(2); w.WriteInt(130);
            w.WriteInt(20000002); w.WriteInt(1); w.WriteInt(0); w.WriteInt(40); });
        var m = (FishingMessage.List) FishingMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(m.Fishes, Is.EqualTo(new[] { new FishingMessage.Record(20000001, 5, 2, 130), new FishingMessage.Record(20000002, 1, 0, 40) }));
    }

    [Test] public void ResultReadsTheRecordOnlyOnSuccess() {
        var ok = R(w => { w.WriteByte(8); w.WriteInt(7); w.WriteInt(99); w.WriteByte(1); w.WriteByte(0);
            w.WriteInt(7); w.WriteInt(3); w.WriteInt(1); w.WriteInt(99); });
        var m = (FishingMessage.Result) FishingMessage.Read(ok);
        Assert.That(ok.Available, Is.EqualTo(0));
        Assert.That((m.FishId, m.Size, m.Success, m.Auto, m.Fish), Is.EqualTo((7u, 99u, true, false, (FishingMessage.Record?) new FishingMessage.Record(7, 3, 1, 99))));
        var miss = R(w => { w.WriteByte(8); w.WriteInt(7); w.WriteInt(99); w.WriteByte(0); w.WriteByte(1); });
        var f = (FishingMessage.Result) FishingMessage.Read(miss);
        Assert.That((f.Success, f.Auto, f.Fish), Is.EqualTo((false, true, (FishingMessage.Record?) null)));
    }

    [Test] public void TrailingBytesAreAnError() {
        Assert.That(() => FishingMessage.Read(R(w => { w.WriteByte(7); w.WriteInt(0); w.WriteByte(1); })), Throws.Exception);
    }

    [Test] public void OtherModesAreNotTheBooks() {
        Assert.That(FishingMessage.Read(R(w => w.WriteByte(3))), Is.Null);
        Assert.That((ushort) Inbound.Fishing, Is.EqualTo(0x73));
    }
}
