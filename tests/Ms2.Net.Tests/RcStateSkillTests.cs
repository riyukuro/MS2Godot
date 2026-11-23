using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>StateSkill GMS2 type 6: i32 actor, i32 item id, i64 cast uid, i32 skill, i32 state and a trailing i32 tick.
/// Main reads the KMS layout (i32 actor, i64 cast uid, i32 skill, i32 level) and leaves the last 8 bytes in Rest.
/// Layouts: the GMS2 packet layout notes §B15.</summary>
public class RcStateSkillTests {
    // gms2 recording: packet 2530, 29 bytes
    static readonly byte[] First = RcPkt.Hex(
        "06D58C110000000000740E3D8D7335000067DBC4041E000000D0327A07");
    // gms2 recording: packet 2595, 29 bytes
    static readonly byte[] Second = RcPkt.Hex(
        "06A0E82B000000000008000000A0E82B000B2D31011D000000D653643E");
    // gms2 recording: packet 2538, 29 bytes
    static readonly byte[] Third = RcPkt.Hex(
        "06D58C110000000000750E3D8D7335000043DBC4041E00000067337A07");

    [Test] public void Gms2Type6ReadsTheGms2Order() {
        Assert.That(Read(First, actor: 1150165, castUid: 58770407100020L, skill: 80010087, state: 30, tick: 125448912));
        Assert.That(Read(Second, actor: 2877600, castUid: 12359197890969608L, skill: 20000011, state: 29, tick: 1046762454));
        Assert.That(Read(Third, actor: 1150165, castUid: 58770407100021L, skill: 80010051, state: 30, tick: 125449063));
    }

    [Test] public void KmsOrderIsUnchangedAndOtherTypesReadNothing() {
        var w = new ByteWriter();
        w.WriteByte(6); w.WriteInt(77); w.WriteLong(9); w.WriteInt(10200001); w.WriteInt(4);
        var m = StateSkill.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()), ProtocolProfile.Kms2525);
        Assert.That((m.ActorId, m.CastUid, m.SkillId, m.Level, m.ItemId, m.Tick), Is.EqualTo((77, 9L, 10200001, 4, 0, 0)));
        Assert.That(StateSkill.Read(new ByteReader(new byte[] { 2, 1, 2, 3 }), ProtocolProfile.Gms2).Type, Is.EqualTo((byte) 2));
    }

    static bool Read(byte[] bytes, int actor, long castUid, int skill, int state, int tick) {
        var r = new ByteReader(bytes);
        var m = StateSkill.Read(r, ProtocolProfile.Gms2);
        Assert.That(m.Type, Is.EqualTo((byte) 6));
        Assert.That(m.ActorId, Is.EqualTo(actor));
        Assert.That(m.CastUid, Is.EqualTo(castUid), "GMS2 puts the item id before the cast uid");
        Assert.That(RcPkt.MemberAny<int>(m, "ItemId"), Is.EqualTo(0));
        Assert.That(m.SkillId, Is.EqualTo(skill));
        Assert.That(RcPkt.MemberAny<int>(m, "State", "Level"), Is.EqualTo(state), "the state sits in the level's place");
        Assert.That(RcPkt.MemberAny<int>(m, "Tick", "TrailingTick", "ServerTick"), Is.EqualTo(tick));
        Assert.That(r.Available, Is.EqualTo(0));
        return true;
    }
}
