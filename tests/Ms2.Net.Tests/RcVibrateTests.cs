using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Vibrate (0x77) has no Inbound member or reader on main. A3: u8 mode 0/1 = entity (narrow), cast uid, skill,
/// level, motion, attack point, vec3s position, serial, bone (narrow), flag. Layouts: the GMS2 packet layout notes §A3.</summary>
public class RcVibrateTests {
    // gms2 recording: packet 3279, 64 bytes
    static readonly byte[] First = RcPkt.Hex(
        "012000616463326535663735376331343930383934663938613964343634633862303202000000793300009994620201",
        "000100970B93FFA50E87B28C10000000");
    // gms2 recording: packet 3735, 64 bytes
    static readonly byte[] Second = RcPkt.Hex(
        "0120006537343432343936613434303437383238386534306438633565366463393630E43D7B187735000041420F0001",
        "000000800290098C0AEFCA8C10000000");
    // gms2 recording: packet 5112, 64 bytes
    static readonly byte[] Third = RcPkt.Hex(
        "012000383961383934313063646632346262346133346138666537393731663036376206000000A82D37006BF4930301",
        "000100690FC3F40D0B2FC09D11000000");

    [Ignore("GMS2 vibrate mode not read yet")]
    [Test] public void Gms2Mode1ReadsTheAttack() {
        Assert.That(Read(First, castUid: 56594784059394L, skill: 40015001, motion: 1, serial: 277656199,
            position: (2967, -109, 3749)));
        Assert.That(Read(Second, castUid: 58785628110308L, skill: 1000001, motion: 0, serial: 277662447,
            position: (640, 2448, 2700)));
        Assert.That(Read(Third, castUid: 15531323296841734L, skill: 60028011, motion: 1, serial: 295551023,
            position: (3945, -2877, 2829)));
    }

    static bool Read(byte[] bytes, long castUid, int skill, byte motion, int serial, (short X, short Y, short Z) position) {
        var r = new ByteReader(bytes);
        var m = RcPkt.CallAny("Vibrate", new[] { "Read" }, new[] {
            new object[] { r, ProtocolProfile.Gms2 },
            new object[] { r },
        });
        Assert.That(RcPkt.MemberAny<byte>(m, "Mode"), Is.EqualTo((byte) 1));
        Assert.That(RcPkt.MemberAny<string>(m, "Entity"), Has.Length.EqualTo(32));
        Assert.That(RcPkt.MemberAny<long>(m, "CastUid"), Is.EqualTo(castUid));
        Assert.That(RcPkt.MemberAny<int>(m, "SkillId"), Is.EqualTo(skill));
        Assert.That(RcPkt.MemberAny<short>(m, "Level"), Is.EqualTo((short) 1));
        Assert.That(RcPkt.MemberAny<byte>(m, "Motion"), Is.EqualTo(motion));
        Assert.That(RcPkt.MemberAny<byte>(m, "AttackPoint"), Is.EqualTo((byte) 0));
        var p = RcPkt.MemberAny<Vector3S>(m, "Position");
        Assert.That((p.X, p.Y, p.Z), Is.EqualTo(position));
        Assert.That(RcPkt.MemberAny<int>(m, "Serial", "ServerTick"), Is.EqualTo(serial));
        Assert.That(RcPkt.MemberAny<string>(m, "Bone"), Has.Length.EqualTo(0));
        Assert.That(RcPkt.MemberAny<byte>(m, "Flag"), Is.EqualTo((byte) 0));
        Assert.That(r.Available, Is.EqualTo(0), "the trailing narrow bone and flag must be read");
        return true;
    }
}
