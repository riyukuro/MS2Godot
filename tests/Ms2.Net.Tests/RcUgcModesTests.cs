using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Ugc: 0x12 is the three banner lists and 0x15 the cost table, both discarded as UgcOther; mode 0x16 is
/// covered by UgcRecordedTests. Layouts: the GMS2 packet layout notes §B12.</summary>
public class RcUgcModesTests {
    // gms2 recording: 1-Audience chamber.msb packet 2256, 13 bytes (0x6D, all three lists empty)
    static readonly byte[] Mode12 = RcPkt.Hex(
        "12000000000000000000000000");
    // gms2 recording: 1-Flora Avenue, ... Tria.msb packet 8434, 34 bytes (0x6D, extracted with extract.py:
    // 0 rolling images, 1 unregistered banner id 10013, 1 reserve id 10013 with 0 hours)
    static readonly byte[] Mode12NonEmpty = RcPkt.Hex(
        "1200000000010000001D2700000000000000010000001D2700000000000000000000");
    // kms2 recording: ApplyingLayoutWithPortals.msb packet 567, 13 bytes (0x6B, extracted with extract.py: empty lists)
    static readonly byte[] KmsMode12Empty = RcPkt.Hex(
        "12000000000000000000000000");
    // kms2 recording: AssassinTutorialPart1.msb packet 414, 34 bytes (0x6B, extracted with extract.py:
    // 0 rolling images, 1 unregistered banner id 10010, 1 reserve id 10010 with 0 hours)
    static readonly byte[] KmsMode12NonEmpty = RcPkt.Hex(
        "1200000000010000001A2700000000000000010000001A2700000000000000000000");
    // gms2 recording: 1-Audience chamber.msb packet 2234, 17 bytes (0x6D)
    static readonly byte[] Mode15 = RcPkt.Hex(
        "150100000000000000000000004B000000");
    // kms2 recording: ApplyingLayoutWithPortals.msb packet 535, 65 bytes (0x6B, extracted with extract.py:
    // 5 rows: (0, 70), (5000, 60), (10000, 50), (100000, 40), (200000, 20))
    static readonly byte[] KmsMode15 = RcPkt.Hex(
        "150500000000000000000000004600000088130000000000003C000000102700000000000032000000A0860100000000",
        "0028000000400D03000000000014000000");

    static int ListCount(object p, params string[] names) {
        var v = RcPkt.MemberAny(p, names);
        return v is int n ? n : RcPkt.Count(v);
    }

    [Ignore("GMS2 Ugc mode not read yet")]
    [Test] public void Gms2Mode12ReadsTheThreeBannerLists() {
        var r = new ByteReader(Mode12);
        var p = UgcMessage.Read(r);
        RcPkt.NotType(p, "UgcOther", "UgcBannerList");
        Assert.That(RcPkt.MemberAny<byte>(p, "Mode"), Is.EqualTo((byte) 0x12));
        Assert.That(ListCount(p, "RollingImageCount", "RollingImages", "ImageCount"), Is.EqualTo(0));
        Assert.That(ListCount(p, "BannerCount", "Banners"), Is.EqualTo(0));
        Assert.That(ListCount(p, "ReserveCount", "Reserves"), Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0), "the three i32 counts must be read, not discarded");

        var r2 = new ByteReader(Mode12NonEmpty);
        var p2 = UgcMessage.Read(r2);
        RcPkt.NotType(p2, "UgcOther", "UgcBannerList");
        Assert.That(ListCount(p2, "RollingImageCount", "RollingImages", "ImageCount"), Is.EqualTo(0));
        Assert.That(ListCount(p2, "BannerCount", "Banners"), Is.EqualTo(1));
        Assert.That(ListCount(p2, "ReserveCount", "Reserves"), Is.EqualTo(1));
        Assert.That(r2.Available, Is.EqualTo(0), "the 34-byte banner and reserve entries must be read");
    }

    [Ignore("GMS2 Ugc mode not read yet")]
    [Test] public void KmsMode12ReadsTheThreeBannerLists() {
        var r = new ByteReader(KmsMode12Empty);
        var p = UgcMessage.Read(r);
        RcPkt.NotType(p, "UgcOther", "UgcBannerList");
        Assert.That(RcPkt.MemberAny<byte>(p, "Mode"), Is.EqualTo((byte) 0x12));
        Assert.That(ListCount(p, "RollingImageCount", "RollingImages", "ImageCount"), Is.EqualTo(0));
        Assert.That(ListCount(p, "BannerCount", "Banners"), Is.EqualTo(0));
        Assert.That(ListCount(p, "ReserveCount", "Reserves"), Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0), "the three i32 counts must be read");

        var r2 = new ByteReader(KmsMode12NonEmpty);
        var p2 = UgcMessage.Read(r2);
        RcPkt.NotType(p2, "UgcOther", "UgcBannerList");
        Assert.That(ListCount(p2, "RollingImageCount", "RollingImages", "ImageCount"), Is.EqualTo(0));
        Assert.That(ListCount(p2, "BannerCount", "Banners"), Is.EqualTo(1));
        Assert.That(ListCount(p2, "ReserveCount", "Reserves"), Is.EqualTo(1));
        Assert.That(r2.Available, Is.EqualTo(0), "the 34-byte banner and reserve entries must be read");
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

    [Ignore("GMS2 Ugc mode not read yet")]
    [Test] public void KmsMode15ReadsTheCostTable() {
        var r = new ByteReader(KmsMode15);
        var p = UgcMessage.Read(r);
        RcPkt.NotType(p, "UgcOther", "UgcCostTable");
        Assert.That(RcPkt.MemberAny<byte>(p, "Mode"), Is.EqualTo((byte) 0x15));
        Assert.That(RcPkt.MemberAny<int>(p, "Count"), Is.EqualTo(5));
        var rows = RcPkt.MemberAny(p, "Entries", "Costs", "Rows");
        var first = RcPkt.Member(rows, "[0]");
        Assert.That(RcPkt.MemberAny<long>(first, "Key", "Band"), Is.EqualTo(0L));
        Assert.That(RcPkt.MemberAny<int>(first, "Percent", "Cost"), Is.EqualTo(70));
        var last = RcPkt.Member(rows, "[4]");
        Assert.That(RcPkt.MemberAny<long>(last, "Key", "Band"), Is.EqualTo(200_000L));
        Assert.That(RcPkt.MemberAny<int>(last, "Percent", "Cost"), Is.EqualTo(20));
        Assert.That(r.Available, Is.EqualTo(0), "the i32 count and all five i64/i32 rows must be read");
    }
}
