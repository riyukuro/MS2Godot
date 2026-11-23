using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The housing storage (0x23) has no Inbound member or reader on main. A2: mode 1 StartList, 3 Add
/// (i32 itemId, i64 uid, u8 rarity, i32 slot, the item body), 4 Remove, 5/7 amount, 8 EndList. Layouts:
/// the GMS2 packet layout notes §A2.</summary>
public class RcFurnishingStorageTests {
    // gms2 recording: packet 80, 1 bytes
    static readonly byte[] StartList = RcPkt.Hex(
        "01");
    // gms2 recording: packet 81, 250 bytes
    static readonly byte[] Add = RcPkt.Hex(
        "032077FC02DCB81CB63597A72D01000000000B00000000000000FFFFFFFF7C82D45D0000000000000000000000000000",
        "000000000000000000000000000000000000000000000000000000000000000000000000000000000000FFFFFFFF0000",
        "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
        "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
        "000000000100000000000000000000000000000000000000000000000000000000000000010000000000000000000000",
        "00000000000000000000");
    // gms2 recording: packet 237, 642 bytes
    static readonly byte[] AddTemplate = RcPkt.Hex(
        "03DD77FC0298E72C26E396BF2901490000000100000000000000FFFFFFFF843AAB5D0000000000000000000000000000",
        "000000000000000000000000000000000000000000000000000000000000000000000000000000000000FFFFFFFF0000",
        "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
        "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
        "00000000010000000000000000000000000000000000340D2C26E396BF2924002A002A002A002A002A002A002A002A00",
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00",
        "2A002A002A002A0019002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00",
        "2A002A002A002A002A002A0001020000003771D6AAAF922F281A4226330A133C2807002A002A002A002A002A002A002A",
        "00863AAB5D0000000050002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A00000000000000000000000000000000000000000000",
        "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000",
        "000000000000000000000000000000000000");

    static object Read(ByteReader r) => RcPkt.CallAny("FurnishingStorage", new[] { "Read" }, new[] {
        new object[] { r, ProtocolProfile.Gms2 },
        new object[] { r },
    });

    [Ignore("GMS2 furnishing storage mode not read yet")]
    [Test] public void Gms2StartListNeedsNoBody() {
        var r = new ByteReader(StartList);
        var m = Read(r);
        Assert.That(RcPkt.MemberAny<byte>(m, "Mode"), Is.EqualTo((byte) 1));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 furnishing storage mode not read yet")]
    [Test] public void Gms2AddReadsTheItemHeader() {
        var r = new ByteReader(Add);
        var m = Read(r);
        Assert.That(RcPkt.MemberAny<byte>(m, "Mode"), Is.EqualTo((byte) 3));
        Assert.That(RcPkt.MemberAny<int>(m, "ItemId"), Is.EqualTo(50100000));
        Assert.That(RcPkt.MemberAny<long>(m, "Uid"), Is.EqualTo(3289764309761833180L));
        Assert.That(RcPkt.MemberAny<byte>(m, "Rarity"), Is.EqualTo((byte) 1));
        Assert.That(RcPkt.MemberAny<int>(m, "Slot"), Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0), "the 232-byte item body must be read");
    }

    [Ignore("GMS2 furnishing storage mode not read yet")]
    [Test] public void Gms2AddReadsTheTemplateVariant() {
        var r = new ByteReader(AddTemplate);
        var m = Read(r);
        Assert.That(RcPkt.MemberAny<byte>(m, "Mode"), Is.EqualTo((byte) 3));
        Assert.That(RcPkt.MemberAny<int>(m, "ItemId"), Is.EqualTo(50100189));
        Assert.That(RcPkt.MemberAny<long>(m, "Uid"), Is.EqualTo(3008288978449000344L));
        Assert.That(RcPkt.MemberAny<byte>(m, "Rarity"), Is.EqualTo((byte) 1));
        Assert.That(RcPkt.MemberAny<int>(m, "Slot"), Is.EqualTo(73));
        Assert.That(r.Available, Is.EqualTo(0), "the 624-byte template/socket item body must be read by length");
    }
}
