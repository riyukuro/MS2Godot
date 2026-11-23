using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The remote-npc readers main does not have: the FieldAddNpc single-stat form (Npcs.cs:31-33 throws today), FollowNpc
/// (0x72), NpcNotice (0xEA, the client's OnNpcNotice scenestategame.cpp:9145-9215), the live name tag (0xB1) and the
/// 0x130 skill-spread body (scenestategame.cpp:7677-7692). Samples are GMS2 recordings (FollowNpc) or the KMS2 sample
/// the layout came from (Nametag; the GMS2 number is crosswalk-only), masked as the corpus is.
/// </summary>
public class RcNpcRemoteReaderTests
{
    // gms2 recording: packet 2340, 4 bytes
    static readonly byte[] Follow = RcLane.Bytes("64C60500");
    // gms2 recording: packet 3477, 7 bytes (KMS 0xB1)
    static readonly byte[] NametagSample = RcLane.Bytes("3EA908002F0000");
    // gms2 recording: packet 5051, 30 bytes: mode 3, kind 2, npc 2000, empty name, Cutin_Mason_01
    static readonly byte[] NpcNoticeSample = RcLane.Bytes(
        "0302D0070000" + "0000" + "0E00" + "437574696E5F4D61736F6E5F3031" + "0000" + "0000" + "0000");

    static byte[] SingleStatNpc()
    {
        var w = new ByteWriter();
        w.WriteInt(77);                    // object id
        w.WriteInt(9000001);               // npc id
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3);     // position
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);     // rotation
        w.WriteByte(1);                    // the single-stat form
        w.WriteByte(35);                   // the stat id seen in the recordings (a percent stat: three i64)
        w.WriteLong(1000); w.WriteLong(900); w.WriteLong(800);
        w.WriteByte(0);                    // not hidden
        w.WriteShort(0);                   // no buffs
        w.WriteLong(0);                    // maid uid
        w.WriteBool(false);                // talkable
        w.WriteInt(40);                    // level
        w.WriteInt(0);                     // unknown
        w.WriteByte(0);                    // flag
        return w.ToArray();
    }

    [Ignore("the FieldAddNpc single-stat form throws")]
    [Test]
    public void Gms2FieldAddNpcType1ReadsTheSingleStat()
    {
        var n = FieldAddNpc.Read(new ByteReader(SingleStatNpc()), _ => default);
        // the three values are the stat's total, base and current: each row keeps its health value, no spirit
        Assert.That(n.Stats.Select(s => s.Health), Is.EqualTo(new long[] { 1000, 900, 800 }));
        Assert.That(n.Stats.Select(s => s.Spirit), Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That(n.Level, Is.EqualTo(40));
    }

    [Ignore("FollowNpc is not read yet")]
    [Test]
    public void Gms2FollowNpcReadsTheNpcId()
    {
        var p = RcLane.Read("FollowNpc", Follow);
        Assert.That(RcLane.Long(p, "NpcId", "Id"), Is.EqualTo(378_468L));
    }

    [Ignore("NpcNotice is not read yet")]
    [Test]
    public void Gms2NpcNoticeReadsModeThree()
    {
        var p = RcLane.Read("NpcNotice", NpcNoticeSample);
        Assert.That(RcLane.Int(p, "Mode"), Is.EqualTo(3));
        Assert.That(RcLane.Int(p, "Kind"), Is.EqualTo(2));
        Assert.That(RcLane.Long(p, "NpcId"), Is.EqualTo(2000L));
        Assert.That(RcLane.MemberAny<string>(p, "Name"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(p, "Icon"), Is.EqualTo("Cutin_Mason_01"));
        Assert.That(RcLane.MemberAny<string>(p, "Sound"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(p, "Script"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(p, "Text"), Is.Empty);
    }

    [Ignore("NpcNotice is not read yet")]
    [Test]
    public void Gms2NpcNoticeReadsTheModelAndScriptModes()
    {
        var model = RcLane.Read("NpcNotice", RcLane.Bytes("01050000000300616263"));
        Assert.That(RcLane.Int(model, "Mode"), Is.EqualTo(1));
        Assert.That(RcLane.Long(model, "ObjectId"), Is.EqualTo(5L));
        Assert.That(RcLane.MemberAny<string>(model, "Model"), Is.EqualTo("abc"));
        var script = RcLane.Read("NpcNotice", RcLane.Bytes("0206000000040078797A77"));
        Assert.That(RcLane.Int(script, "Mode"), Is.EqualTo(2));
        Assert.That(RcLane.MemberAny<string>(script, "Script"), Is.EqualTo("xyzw"));
    }

    [Ignore("the live name tag is not read yet")]
    [Test]
    public void Gms2NametagReadsTheSymbol()
    {
        var p = RcLane.Read("Nametag", NametagSample);
        Assert.That(RcLane.Long(p, "ObjectId"), Is.EqualTo(566_590L));
        Assert.That(RcLane.Int(p, "Symbol"), Is.EqualTo(47));
        Assert.That(RcLane.Bool(p, "Shown", "Show"), Is.False);
    }

    [Ignore("the 0x130 skill-spread body is not read yet")]
    [Test]
    public void Gms2SkillSpreadReadsActorSkillLevelAndPose()
    {
        var w = new ByteWriter();
        w.WriteInt(5); w.WriteInt(80010051); w.WriteShort(3);
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(1);
        var p = RcLane.Read("NpcSkillSpread", w.ToArray());
        Assert.That(RcLane.Long(p, "ActorId", "ObjectId"), Is.EqualTo(5L));
        Assert.That(RcLane.Long(p, "SkillId"), Is.EqualTo(80_010_051L));
        Assert.That(RcLane.Int(p, "Level"), Is.EqualTo(3));
        Assert.That(RcLane.Member(p, "Position"), Is.Not.Null);
        Assert.That(RcLane.Member(p, "Direction"), Is.Not.Null);
    }

    [Ignore("the new GMS2 opcode rows are not mapped")]
    [Test]
    public void Gms2OpcodeRowsAreAdded()
    {
        Assert.That(RcLane.Gms2Inbound(RcLane.EnumMember<Inbound>("FollowNpc")), Is.EqualTo(0x72));
        Assert.That(RcLane.Gms2Inbound(RcLane.EnumMember<Inbound>("NpcNotice")), Is.EqualTo(0xEA));
        // the live name tag: the implementation may call the member Insignia or Nametag
        Inbound tag;
        if (!Enum.TryParse("Nametag", out tag) && !Enum.TryParse("Insignia", out tag))
            Assert.Fail("Ms2.Net.Inbound.Nametag / Insignia is not defined at origin/main");
        Assert.That(RcLane.Gms2Inbound(tag), Is.EqualTo(0xB1));
    }
}
