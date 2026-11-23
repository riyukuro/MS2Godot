using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>HomeCommand: mode 1 (architect score) passes; GMS2 mode 0 is i64 account id and i64 last nominated time, which
/// the reader drops after the mode byte. Layouts: the GMS2 packet layout notes §B13.</summary>
public class RcHomeCommandTests {
    // kms2 recording: packet 1105, 21 bytes
    static readonly byte[] Mode1 = RcPkt.Hex(
        "0139FB6000F991EB60000000000100000001000000");
    // gms2 recording: packet 2235, 17 bytes
    static readonly byte[] Mode0 = RcPkt.Hex(
        "001111111111111111A618895E00000000");

    [Test] public void Mode1ReadsTheArchitectScore() {
        var r = new ByteReader(Mode1);
        var m = HomeCommend.Read(r);
        Assert.That(m, Is.Not.Null);
        Assert.That((m.ObjectId, m.MonthBase, m.MonthlyCount, m.Point), Is.EqualTo((6355769, 1626051065L, 1, 1)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 home command mode 0 not read yet")]
    [Test] public void Gms2Mode0ReadsTheAccountAndTime() {
        var r = new ByteReader(Mode0);
        var m = RcPkt.Read("HomeCommend", r);
        Assert.That(m, Is.Not.Null);
        Assert.That(RcPkt.MemberAny<byte>(m, "Mode"), Is.EqualTo((byte) 0));
        Assert.That(RcPkt.MemberAny<long>(m, "AccountId"), Is.EqualTo(0x1111111111111111L), "the recorded account id is masked");
        Assert.That(RcPkt.MemberAny<long>(m, "LastNominatedTime", "Time", "NominatedTime"), Is.EqualTo(1586043046L));
        Assert.That(r.Available, Is.EqualTo(0), "the two i64 values must be read");
    }
}
