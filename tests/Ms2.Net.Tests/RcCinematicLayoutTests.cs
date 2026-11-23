using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Cinematic: types 1-3 pass today; GMS2 adds 4-12 and types 4+ are returned as Other. Type 4 is SetSkip
/// (u8 kind, strn value). Layouts: the GMS2 packet layout notes §B7.</summary>
public class RcCinematicLayoutTests {
    // gms2 recording: packet 2333, 2 bytes
    static readonly byte[] Type1 = RcPkt.Hex(
        "0101");
    // gms2 recording: packet 2360, 1 bytes
    static readonly byte[] Type2 = RcPkt.Hex(
        "02");
    // gms2 recording: packet 2334, 9 bytes
    static readonly byte[] Type3 = RcPkt.Hex(
        "030300000000000000");
    // gms2 recording: packet 2485, 13 bytes
    static readonly byte[] Type4Scene = RcPkt.Hex(
        "040109006E6578745374617465");
    // gms2 recording: packet 2588, 4 bytes
    static readonly byte[] Type4State = RcPkt.Hex(
        "04020000");

    [Test] public void Types1To3ReadToday() {
        var key = CinematicPacket.Read(new ByteReader(Type1)) as CinematicPacket.KeyMode;
        Assert.That(key, Is.Not.Null);
        Assert.That(key.On, Is.True);
        Assert.That(CinematicPacket.Read(new ByteReader(Type2)), Is.InstanceOf<CinematicPacket.Close>());
        var start = CinematicPacket.Read(new ByteReader(Type3)) as CinematicPacket.Start;
        Assert.That(start, Is.Not.Null);
        Assert.That(start.Id, Is.EqualTo(3));
        Assert.That(start.Text, Is.Empty);
    }

    [Test] public void Gms2Type4ReadsTheSkipState() {
        var scene = ReadSkip(Type4Scene, kind: 1, valueLength: 9);
        Assert.That(scene);
        var state = ReadSkip(Type4State, kind: 2, valueLength: 0);
        Assert.That(state);
    }

    static bool ReadSkip(byte[] bytes, int kind, int valueLength) {
        var r = new ByteReader(bytes);
        var p = RcPkt.Read("CinematicPacket", r);
        RcPkt.NotType(p, "Other", "SetSkip");
        Assert.That(RcPkt.MemberAny<byte>(p, "Kind"), Is.EqualTo(kind));
        Assert.That(RcPkt.MemberAny<string>(p, "Message"), Has.Length.EqualTo(valueLength));
        Assert.That(r.Available, Is.EqualTo(0), "the type-4 body must be read, not left after the type byte");
        return true;
    }
}
