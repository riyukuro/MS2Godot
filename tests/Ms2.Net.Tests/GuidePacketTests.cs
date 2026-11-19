using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class GuidePacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void TheStepsSentAtATeardownAreCountThenPairs() {
        // GuideRecordOut (0x57, CGuideManager::SendStates @0x14138b170): the packet a quit's SendFunctionCube sends
        // over the guide's changed steps (mypc.cpp:2835-2837) — i32 count, then (i32 save id, i32 step) pairs, no
        // trailing byte (the bits byte is the inbound 0x89's).
        Assert.That(Bytes(new GuideRecordOut(new[] { (3, 1), (9, 4) })),
            Is.EqualTo(new byte[] { 2, 0, 0, 0, 3, 0, 0, 0, 1, 0, 0, 0, 9, 0, 0, 0, 4, 0, 0, 0 }));
    }

    [Test]
    public void TheLoadedStepsSkipASaveIdOfZero() {
        // GuideRecordIn (0x89, CGuideManager::ReceiveStates @0x14138b280): a pair of save id 0 is skipped, a u8 bits
        // byte closes it
        var r = GuideRecordIn.Read(R(w => { w.WriteInt(2); w.WriteInt(0); w.WriteInt(7); w.WriteInt(5); w.WriteInt(1); w.WriteByte(0x05); }));
        Assert.That(r.Steps, Is.EqualTo(new[] { (5, 1) }));
        Assert.That(r.Bits, Is.EqualTo(0x05));
    }
}
