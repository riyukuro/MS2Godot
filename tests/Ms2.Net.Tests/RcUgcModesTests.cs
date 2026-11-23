using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Ugc: 0x12 is the three banner lists and 0x15 the cost table, both discarded as UgcOther; mode 0x16 is
/// covered by UgcRecordedTests. Layouts: the GMS2 packet layout notes §B12.</summary>
public class RcUgcModesTests {
    // gms2 recording: packet 2256, 13 bytes
    static readonly byte[] Mode12 = RcPkt.Hex(
        "12000000000000000000000000");
    // gms2 recording: packet 2234, 17 bytes
    static readonly byte[] Mode15 = RcPkt.Hex(
        "150100000000000000000000004B000000");

    [Ignore("GMS2 Ugc mode not read yet")]
    [Test] public void Gms2Mode12ReadsTheThreeBannerLists() {
        var r = new ByteReader(Mode12);
        var p = UgcMessage.Read(r);
        RcPkt.NotType(p, "UgcOther", "UgcBannerList");
        Assert.That(RcPkt.MemberAny<byte>(p, "Mode"), Is.EqualTo((byte) 0x12));
        Assert.That(RcPkt.MemberAny<int>(p, "RollingImageCount", "RollingImages", "ImageCount"), Is.EqualTo(0));
        Assert.That(RcPkt.MemberAny<int>(p, "BannerCount", "Banners"), Is.EqualTo(0));
        Assert.That(RcPkt.MemberAny<int>(p, "ReserveCount", "Reserves"), Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0), "the three i32 counts must be read, not discarded");
    }

    [Ignore("GMS2 Ugc mode not read yet")]
    [Test] public void Gms2Mode15ReadsTheCostTable() {
        var r = new ByteReader(Mode15);
        var p = UgcMessage.Read(r);
        RcPkt.NotType(p, "UgcOther", "UgcCostTable");
        Assert.That(RcPkt.MemberAny<byte>(p, "Mode"), Is.EqualTo((byte) 0x15));
        Assert.That(RcPkt.MemberAny<int>(p, "Count"), Is.EqualTo(1));
        var rows = RcPkt.MemberAny(p, "Entries", "Costs", "Rows");
        var first = RcPkt.Member(rows, "[0]");
        Assert.That(RcPkt.MemberAny<long>(first, "Key", "Band"), Is.EqualTo(0L));
        Assert.That(RcPkt.MemberAny<int>(first, "Percent", "Cost"), Is.EqualTo(75));
        Assert.That(r.Available, Is.EqualTo(0), "the i32 count and the i64/i32 row must be read");
    }
}
