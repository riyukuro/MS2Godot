using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The housing cube inventory (0x24) has no Inbound member or reader on main. A1: u8 mode 0/1/2/3/4; mode 1 is
/// i32 item id, i64 uid, i64 expire-or-count, bool hasLook and, when set, the UgcItemLook. Layouts:
/// the GMS2 packet layout notes §A1.</summary>
public class RcFurnishingInventoryTests {
    // gms2 recording: packet 953, 1 bytes
    static readonly byte[] StartList = RcPkt.Hex(
        "00");
    // gms2 recording: packet 954, 22 bytes
    static readonly byte[] Add = RcPkt.Hex(
        "012077FC02557343505D96492C000000000000000000");
    // gms2 recording: packet 3770, 370 bytes
    static readonly byte[] AddLook = RcPkt.Hex(
        "01B400FE021765C93D7F15C229000000000000000001EB00C83D7F15C22924002A002A002A002A002A002A002A002A00",
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00",
        "2A002A002A002A0019002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00",
        "2A002A002A002A002A002A000102000000111111111111111111111111111111110A002A002A002A002A002A002A002A",
        "002A002A002A005446F95C0000000050002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A0000");

    static object Read(ByteReader r) => RcPkt.CallAny("FurnishingInventory", new[] { "Read" }, new[] {
        new object[] { r, ProtocolProfile.Gms2 },
        new object[] { r },
    });

    [Ignore("GMS2 furnishing inventory mode not read yet")]
    [Test] public void Gms2StartListNeedsNoBody() {
        var r = new ByteReader(StartList);
        var m = RcPkt.CallAny("FurnishingInventory", new[] { "Read" }, new[] {
            new object[] { r, ProtocolProfile.Gms2 },
            new object[] { r },
        });
        Assert.That(RcPkt.MemberAny<byte>(m, "Mode"), Is.EqualTo((byte) 0));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 furnishing inventory mode not read yet")]
    [Test] public void Gms2AddReadsTheItemAndUid() {
        var r = new ByteReader(Add);
        var m = Read(r);
        Assert.That(RcPkt.MemberAny<byte>(m, "Mode"), Is.EqualTo((byte) 1));
        Assert.That(RcPkt.MemberAny<int>(m, "ItemId"), Is.EqualTo(50100000));
        Assert.That(RcPkt.MemberAny<long>(m, "Uid"), Is.EqualTo(3191247138491429717L));
        Assert.That(RcPkt.MemberAny<long>(m, "ExpireOrCount", "Count", "ExpireTime"), Is.EqualTo(0L));
        Assert.That(RcPkt.MemberAny<bool>(m, "HasLook"), Is.False);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 furnishing inventory mode not read yet")]
    [Test] public void Gms2AddReadsTheUgcLook() {
        var r = new ByteReader(AddLook);
        var m = Read(r);
        Assert.That(RcPkt.MemberAny<byte>(m, "Mode"), Is.EqualTo((byte) 1));
        Assert.That(RcPkt.MemberAny<int>(m, "ItemId"), Is.EqualTo(50200756));
        Assert.That(RcPkt.MemberAny<bool>(m, "HasLook"), Is.True);
        var look = RcPkt.MemberAny(m, "Look", "UgcLook");
        Assert.That(RcPkt.MemberAny<long>(look, "Id", "Uid"), Is.EqualTo(3008991137278460139L));
        Assert.That(RcPkt.MemberAny<string>(look, "FileName"), Has.Length.EqualTo(36));
        Assert.That(RcPkt.MemberAny<string>(look, "Name", "ItemName"), Has.Length.EqualTo(25));
        Assert.That(RcPkt.MemberAny<byte>(look, "Flag30", "Flag"), Is.EqualTo((byte) 1));
        Assert.That(RcPkt.MemberAny<int>(look, "Value34", "Value"), Is.EqualTo(2));
        Assert.That(RcPkt.MemberAny<long>(look, "AccountId"), Is.EqualTo(0x1111111111111111L), "the recorded account id is masked");
        Assert.That(RcPkt.MemberAny<long>(look, "CharacterId"), Is.EqualTo(0x1111111111111111L), "the recorded character id is masked");
        Assert.That(RcPkt.MemberAny<string>(look, "Author"), Has.Length.EqualTo(10));
        Assert.That(RcPkt.MemberAny<long>(look, "CreationTime"), Is.EqualTo(1559840340L));
        Assert.That(RcPkt.MemberAny<string>(look, "Url"), Has.Length.EqualTo(80));
        Assert.That(RcPkt.MemberAny<byte>(look, "State"), Is.EqualTo((byte) 0));
        Assert.That(r.Available, Is.EqualTo(0), "the whole UgcItemLook must be read");
    }
}
