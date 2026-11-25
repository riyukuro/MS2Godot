using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The remote-npc readers main does not have: the FieldAddNpc single-stat form (Npcs.cs:31-33 throws today), FollowNpc
/// (KMS 0x70, GMS2 0x72), NpcNotice (KMS 0xE8, GMS2 0xEA, the client's OnNpcNotice scenestategame.cpp:9145-9215), the
/// live name tag (KMS 0xB1, GMS2 crosswalk-only) and the 0x130 skill-spread body (scenestategame.cpp:7677-7692).
/// Recorded bytes are extracted with work/rc-samples/scripts/extract.py (mask_names).
/// </summary>
public class RcNpcRemoteReaderTests
{
    // gms2 recording: 10-Cave Depths.msb packet 2340, 4 bytes (0x72)
    static readonly byte[] Gms2Follow = RcLane.Bytes("64C60500");
    // kms2 recording: Knight part 1.msb packet 723, 4 bytes (0x70)
    static readonly byte[] KmsFollow = RcLane.Bytes("DF565800");
    // kms2 recording: kms2-BeginnerPackage.msb packet 3477, 7 bytes (0xB1, shown=false); no GMS2 0xB1/0xB3 recording exists in the corpus
    static readonly byte[] KmsNametagHidden = RcLane.Bytes("3EA908002F0000");
    // kms2 recording: kms2-BeginnerPackage.msb packet 3777, 7 bytes (0xB1, shown=true)
    static readonly byte[] KmsNametagShown = RcLane.Bytes("3EA90800320001");
    // gms2 recording: Clone Failure 1.msb packet 5051, 30 bytes (0xEA): mode 3, kind 2, npc 2000, empty name, Cutin_Mason_01
    // (no KMS2 mode 3 recording exists in the corpus; KMS2 0xE8 only records mode 0)
    static readonly byte[] Gms2NpcNoticeMode3Small = RcLane.Bytes(
        "0302D0070000" + "0000" + "0E00" + "437574696E5F4D61736F6E5F3031" + "0000" + "0000" + "0000");
    // gms2 recording: 0xEA mode 3, packet 10259, 134 bytes (text masked with '*' by extract.py):
    // mode 3, kind 0, npc 3000, empty name, Turned_Yuperia_normal, sound ko/Npc/00002215, empty script, 41-char text
    static readonly byte[] Gms2NpcNoticeMode3WithSoundAndText = RcLane.Hex(
        "0300B80B0000000015005475726E65645F597570657269615F6E6F726D616C0F006B6F2F4E70632F3030303032323135",
        "000029002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00",
        "2A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00");
    // gms2 recording: Ishura.msb packet 7973, 71 bytes (0xEA): mode 1, objectId 477386, UTF-16 asset path "Etc/Eff_Battle_Target_Balrog.xml"
    // (asset path contains no player text; extract.py's heuristic false-matches the 0x0007 upper bytes of objectId + " Etc/Ef")
    static readonly byte[] Gms2NpcNoticeMode1 = RcLane.Hex(
        "01CA48070020004500740063002F004500660066005F0042006100740074006C0065005F005400610072006700650074",
        "005F00420061006C0072006F0067002E0078006D006C00");

    // synthetic: no confirmed FieldAddNpc type=1 single-stat frame is isolated in samples.tsv
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
        var p = RcLane.Read("FollowNpc", Gms2Follow);
        Assert.That(RcLane.Long(p, "NpcId", "Id"), Is.EqualTo(378_468L));
    }

    [Ignore("FollowNpc is not read yet")]
    [Test]
    public void KmsFollowNpcReadsTheNpcId()
    {
        var p = RcLane.Read("FollowNpc", KmsFollow);
        Assert.That(RcLane.Long(p, "NpcId", "Id"), Is.EqualTo(5_789_407L));
    }

    [Ignore("NpcNotice is not read yet")]
    [Test]
    public void Gms2NpcNoticeReadsModeThree()
    {
        var p = RcLane.Read("NpcNotice", Gms2NpcNoticeMode3Small);
        Assert.That(RcLane.Int(p, "Mode"), Is.EqualTo(3));
        Assert.That(RcLane.Int(p, "Kind"), Is.EqualTo(2));
        Assert.That(RcLane.Long(p, "NpcId"), Is.EqualTo(2000L));
        Assert.That(RcLane.MemberAny<string>(p, "Name"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(p, "Icon"), Is.EqualTo("Cutin_Mason_01"));
        Assert.That(RcLane.MemberAny<string>(p, "Sound"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(p, "Script"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(p, "Text"), Is.Empty);

        var full = RcLane.Read("NpcNotice", Gms2NpcNoticeMode3WithSoundAndText);
        Assert.That(RcLane.Int(full, "Mode"), Is.EqualTo(3));
        Assert.That(RcLane.Int(full, "Kind"), Is.EqualTo(0));
        Assert.That(RcLane.Long(full, "NpcId"), Is.EqualTo(3000L));
        Assert.That(RcLane.MemberAny<string>(full, "Name"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(full, "Icon"), Is.EqualTo("Turned_Yuperia_normal"));
        Assert.That(RcLane.MemberAny<string>(full, "Sound"), Is.EqualTo("ko/Npc/00002215"));
        Assert.That(RcLane.MemberAny<string>(full, "Script"), Is.Empty);
        Assert.That(RcLane.MemberAny<string>(full, "Text"), Has.Length.EqualTo(41));
    }

    [Ignore("NpcNotice is not read yet")]
    [Test]
    public void Gms2NpcNoticeReadsTheModelAndScriptModes()
    {
        // mode 1: recorded GMS2 frame (Ishura.msb packet 7973); ReadUnicodeStringA is UTF-16LE on the wire
        var model = RcLane.Read("NpcNotice", Gms2NpcNoticeMode1);
        Assert.That(RcLane.Int(model, "Mode"), Is.EqualTo(1));
        Assert.That(RcLane.Long(model, "ObjectId"), Is.EqualTo(477_386L));
        Assert.That(RcLane.MemberAny<string>(model, "Model"), Is.EqualTo("Etc/Eff_Battle_Target_Balrog.xml"));
        // mode 2: synthetic (no mode 2 recording exists in either corpus; ReadUnicodeStringA is UTF-16LE on the wire)
        var script = RcLane.Read("NpcNotice", RcLane.Bytes("02060000000400780079007A007700"));
        Assert.That(RcLane.Int(script, "Mode"), Is.EqualTo(2));
        Assert.That(RcLane.Long(script, "ObjectId"), Is.EqualTo(6L));
        Assert.That(RcLane.MemberAny<string>(script, "Script"), Is.EqualTo("xyzw"));
    }

    [Ignore("the live name tag is not read yet")]
    [Test]
    public void Gms2NametagReadsTheSymbol()
    {
        // KMS2 recordings (no GMS2 0xB1/0xB3 recording exists in the corpus)
        var hidden = RcLane.Read("Nametag", KmsNametagHidden);
        Assert.That(RcLane.Long(hidden, "ObjectId"), Is.EqualTo(567_614L));
        Assert.That(RcLane.Int(hidden, "Symbol"), Is.EqualTo(47));
        Assert.That(RcLane.Bool(hidden, "Shown", "Show"), Is.False);

        var shown = RcLane.Read("Nametag", KmsNametagShown);
        Assert.That(RcLane.Long(shown, "ObjectId"), Is.EqualTo(567_614L));
        Assert.That(RcLane.Int(shown, "Symbol"), Is.EqualTo(50));
        Assert.That(RcLane.Bool(shown, "Shown", "Show"), Is.True);
    }

    [Ignore("the 0x130 skill-spread body is not read yet")]
    [Test]
    public void Gms2SkillSpreadReadsActorSkillLevelAndPose()
    {
        // synthetic: no 34-byte 0x130 skill-spread recording exists in either corpus (0x130/0x132 samples are 5-byte packets)
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
