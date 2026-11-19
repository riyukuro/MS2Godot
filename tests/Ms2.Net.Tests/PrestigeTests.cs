using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Prestige 0x11C read and the 0xAC claim requests (OnPrestige, scenestategame.cpp:8815).</summary>
public class PrestigeTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Bytes(Ms2.Net.Wire.IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test] public void ExpGainReadsTheExpAndTheGain() {
        var r = R(w => { w.WriteByte(1); w.WriteLong(1234); w.WriteLong(56); });
        Assert.That(PrestigeMessage.Read(r), Is.EqualTo(new PrestigeMessage.ExpGain(1234, 56)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void LevelUpReadsTheObjectAndTheLevel() {
        var r = R(w => { w.WriteByte(2); w.WriteInt(5001); w.WriteInt(12); });
        Assert.That(PrestigeMessage.Read(r), Is.EqualTo(new PrestigeMessage.LevelUp(5001, 12)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void LoadedReadsTheClaimedLevelsAsASet() {
        var r = R(w => { w.WriteByte(0); w.WriteLong(900); w.WriteInt(7); w.WriteLong(300); w.WriteInt(3); w.WriteInt(6); w.WriteInt(2); w.WriteInt(6); });
        var m = (PrestigeMessage.Loaded) PrestigeMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That((m.Exp, m.FilledLevel, m.NextExp), Is.EqualTo((900L, 7, 300L)));
        Assert.That(m.Claimed, Is.EqualTo(new uint[] { 2, 6 }));
    }

    [Test] public void ClaimResultReadsTheResultAndTheLevels() {
        var r = R(w => { w.WriteByte(4); w.WriteByte(1); w.WriteInt(2); w.WriteInt(4); w.WriteInt(2); });
        var m = (PrestigeMessage.ClaimResult) PrestigeMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(m.Result, Is.EqualTo(1));
        Assert.That(m.Levels, Is.EqualTo(new uint[] { 2, 4 }));
    }

    [TestCase(6)] [TestCase(7)]
    public void MissionListsKeepTheFirstOfAnId(int kind) {
        var r = R(w => {
            w.WriteByte((byte) kind);
            if (kind == 6) w.WriteByte(0);
            w.WriteInt(3);
            w.WriteLong(2); w.WriteLong(1); w.WriteByte(0);
            w.WriteLong(1); w.WriteLong(1); w.WriteByte(1);
            w.WriteLong(2); w.WriteLong(9); w.WriteByte(1);
        });
        var m = (PrestigeMessage.MissionList) PrestigeMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That((m.Kind, m.Result), Is.EqualTo(((byte) kind, (byte) (kind == 6 ? 0 : 1))));
        Assert.That(m.List.Keys, Is.EqualTo(new uint[] { 1, 2 }));
        Assert.That(m.List[1], Is.EqualTo((1u, true)));
        Assert.That(m.List[2], Is.EqualTo((1u, false)));
    }

    [TestCase(3)] [TestCase(5)] [TestCase(8)]
    public void OtherKindsReadAsNull(int kind) =>
        Assert.That(PrestigeMessage.Read(R(w => { w.WriteByte((byte) kind); w.WriteInt(1); })), Is.Null);

    [Test] public void ClaimRequestsWriteTheirMode() {
        Assert.That(new PrestigeLevelRewardRequest(4).Op, Is.EqualTo(Outbound.Prestige));
        Assert.That(Bytes(new PrestigeLevelRewardRequest(4)), Is.EqualTo(new byte[] { 3, 4, 0, 0, 0 }));
        Assert.That(Bytes(new PrestigeMissionRewardRequest(2)), Is.EqualTo(new byte[] { 5, 2, 0, 0, 0 }));
    }
}
