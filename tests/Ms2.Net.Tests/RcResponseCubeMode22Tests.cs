using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>ResponseCube: mode 0x12 (release) passes; GMS2 0x22 is a u8 mode and i32 map id (ReturnMap), which the reader
/// drops after the mode byte. Layouts: the GMS2 packet layout notes §B14.</summary>
public class RcResponseCubeMode22Tests {
    // gms2 recording: packet 5942, 6 bytes
    static readonly byte[] Mode12 = RcPkt.Hex(
        "120099C10600");
    // gms2 recording: packet 2307, 5 bytes
    static readonly byte[] Mode22 = RcPkt.Hex(
        "22D4841E00");

    [Test] public void Mode12ReadsTheRelease() {
        var r = new ByteReader(Mode12);
        var m = ResponseCubeHeldItem.Read(r);
        Assert.That(m, Is.Not.Null);
        Assert.That((m.Mode, m.Error, m.Id), Is.EqualTo((ResponseCubeHeldItem.ModeRelease, (byte) 0, 442777)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 return map not read yet")]
    [Test] public void Gms2Mode22ReadsTheReturnMap() {
        var r = new ByteReader(Mode22);
        var m = ResponseCubeHeldItem.Read(r);
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Mode, Is.EqualTo((byte) 0x22));
        Assert.That(RcPkt.MemberAny<int>(m, "MapId", "Id"), Is.EqualTo(2000084));
        Assert.That(r.Available, Is.EqualTo(0), "the i32 map id must be read");
    }
}
