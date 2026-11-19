using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The trophy dialog's sends (uiachievedialog.cpp) and the UserEnv mode 3 reply its load asks for.</summary>
public class AchieveRequestTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void TheRequestsWriteTheirModeAndFields() {
        Assert.That(new AchieveRewardRequest(21100001).Op, Is.EqualTo(Outbound.Achieve));
        Assert.That(Bytes(new AchieveRewardRequest(21100001)), Is.EqualTo(new byte[] { 3, 0xe1, 0xf5, 0x41, 0x01 }));
        Assert.That(Bytes(new AchieveBookmarkRequest(1, true)), Is.EqualTo(new byte[] { 4, 1, 0, 0, 0, 1 }));
        Assert.That(Bytes(new AchieveBookmarkRequest(1, false)), Is.EqualTo(new byte[] { 4, 1, 0, 0, 0, 0 }));
        Assert.That(Bytes(new AchieveDebugRequest(2, 3)), Is.EqualTo(new byte[] { 5, 2, 0, 0, 0, 3 }));
        Assert.That(new UserEnvRequest().Op, Is.EqualTo(Outbound.RequestUserEnv));
        Assert.That(Bytes(new UserEnvRequest()), Is.EqualTo(new byte[] { 3 }));
    }

    [Test]
    public void UserEnvMode3ReadsTheCollectionGrades() {
        var w = new ByteWriter();
        w.WriteByte(3); w.WriteInt(2); w.WriteInt(30000001); w.WriteByte(1); w.WriteInt(30000002); w.WriteByte(4);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var m = (UserEnvMessage.Collection) UserEnvMessage.Read(r);
        Assert.That(r.Available, Is.Zero, "the whole body is read");
        Assert.That(m.Grades, Is.EquivalentTo(new Dictionary<uint, byte> { [30000001] = 1, [30000002] = 4 }));
    }
}
