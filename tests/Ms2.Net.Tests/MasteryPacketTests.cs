using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Mastery 0xCD read, the 0x92 requests (CSceneStateGame::OnMastery, scenestategame.cpp:3920) and UserEnv modes 8
/// and 9 (scenestategame.cpp:4469).</summary>
public class MasteryPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Bytes(Ms2.Net.Wire.IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test] public void Mode0ReadsTheTypeTheExpAndTheSubType() {
        var m = MasteryMessage.Read(R(w => { w.WriteByte(0); w.WriteByte(2); w.WriteInt(1234); w.WriteInt(0xc80); }));
        Assert.That(m, Is.EqualTo(new MasteryMessage.ExpChanged(2, 1234, 0xc80)));
    }

    [Test] public void Modes1And2ReadTheirItemLists() {
        var a = (MasteryMessage.RewardReceived) MasteryMessage.Read(R(w => {
            w.WriteByte(1); w.WriteInt(2003); w.WriteInt(2);
            w.WriteInt(100); w.WriteShort(3); w.WriteInt(200); w.WriteShort(unchecked((short) 0xffff));
        }));
        Assert.That(a.RewardId, Is.EqualTo(2003));
        Assert.That(a.Items, Is.EqualTo(new[] { (100, 3), (200, 0xffff) }));
        var b = (MasteryMessage.Crafted) MasteryMessage.Read(R(w => { w.WriteByte(2); w.WriteShort(8); w.WriteInt(0); }));
        Assert.That(b.Type, Is.EqualTo(8));
        Assert.That(b.Items, Is.Empty);
        Assert.That(MasteryMessage.Read(R(w => { w.WriteByte(3); w.WriteShort(5); })), Is.EqualTo(new MasteryMessage.Result(5)));
    }

    [Test] public void TheRequestsWriteTheirModeAndId() {
        Assert.That(Bytes(new MasteryRewardRequest(2003)), Is.EqualTo(new byte[] { 1, 0xd3, 0x07, 0, 0 }));
        Assert.That(Bytes(new MasteryCraftRequest(35000001)), Is.EqualTo(new byte[] { 2, 0xc1, 0x0e, 0x16, 0x02 }));
    }

    [Test] public void UserEnvModes8And9ReadTheGatheringCountsAndTheRewards() {
        var g = (UserEnvMessage.GatheringCounts) UserEnvMessage.Read(R(w => { w.WriteByte(8); w.WriteInt(1); w.WriteInt(10000001); w.WriteInt(7); }));
        Assert.That(g.Counts[10000001], Is.EqualTo(7));
        var m = (UserEnvMessage.MasteryRewards) UserEnvMessage.Read(R(w => { w.WriteByte(9); w.WriteInt(1); w.WriteInt(1002); w.WriteByte(1); }));
        Assert.That(m.Received[1002u], Is.EqualTo((byte) 1));
    }
}
