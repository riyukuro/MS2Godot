using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>MeretMarket: mode 0x16 (Mode22) reads today (null); GMS2 0xC9 is u8 mode and i32 value, which the reader drops
/// after the mode byte. Layouts: the GMS2 packet layout notes §B11.</summary>
public class RcMeretMarketTests {
    // gms2 recording: packet 663, 73 bytes
    static readonly byte[] Mode1E = RcPkt.Hex(
        "1E014DCF5C08000000000000000001000000000000000000000000000000000000000100000000000000000000000005",
        "00000000000000000300000000000000000000000000000000");
    // gms2 recording: packet 2669, 2 bytes
    static readonly byte[] Mode22 = RcPkt.Hex(
        "1600");
    // gms2 recording: 1-Audience chamber.msb packet 2215, 5 bytes (0x6E)
    static readonly byte[] ModeC9 = RcPkt.Hex(
        "C900000000");
    // kms2 recording: ApplyingLayoutWithPortals.msb packet 514, 5 bytes (0x6C, extracted with extract.py)
    static readonly byte[] KmsModeC9 = RcPkt.Hex(
        "C900000000");

    [Test] public void Mode1EReadsTheBuyAnswer() {
        var r = new ByteReader(Mode1E);
        var p = MeretMarketPacket.Read(r, ProtocolProfile.Gms2);
        Assert.That(p, Is.Not.Null);
        Assert.That((p.Mode, p.Succeeded), Is.EqualTo(((byte) 0x1E, true)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 meret market value not read yet")]
    [Test] public void Gms2ModeC9ReadsTheValue() {
        var r = new ByteReader(ModeC9);
        var p = MeretMarketPacket.Read(r, ProtocolProfile.Gms2);
        Assert.That(p, Is.Not.Null);
        Assert.That(p.Mode, Is.EqualTo((byte) 0xC9));
        Assert.That(RcPkt.MemberAny<int>(p, "Value", "Count"), Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0), "the i32 after the mode byte must be read");
    }

    [Ignore("GMS2 meret market value not read yet")]
    [Test] public void KmsModeC9ReadsTheValue() {
        var r = new ByteReader(KmsModeC9);
        var p = MeretMarketPacket.Read(r, ProtocolProfile.Kms2525);
        Assert.That(p, Is.Not.Null);
        Assert.That(p.Mode, Is.EqualTo((byte) 0xC9));
        Assert.That(RcPkt.MemberAny<int>(p, "Value", "Count"), Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0), "the i32 after the mode byte must be read");
    }
}
