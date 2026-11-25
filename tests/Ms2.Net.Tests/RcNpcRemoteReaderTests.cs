using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The remote-npc readers: the FieldAddNpc single-stat form (CNpc::vfunc_90), FollowNpc (KMS 0x70, GMS2 0x72), NpcNotice
/// (KMS 0xE8, GMS2 0xEA, the client's OnNpcNotice scenestategame.cpp:9145-9215), the live name tag (Insignia, KMS 0xB1,
/// GMS2 0xB3) and the 0x130 skill-spread body (scenestategame.cpp:7677-7692).
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
    // gms2 recording (Ignicore dungeon): mode 0, the unicode text $BullArmorRed_DashAttack$, 10000 ms
    static readonly byte[] Gms2NpcNoticeAnnounce = RcLane.Bytes(
        "00" + "1900" + "2400420075006C006C00410072006D006F0072005200650064005F004400610073006800410074007400610063006B002400" + "10270000");

    /// <summary>A FieldAddNpc of a non-boss row with the single-stat form (type 1) of <paramref name="stat"/>: no recording
    /// has one (both servers write type 0x23, the three-row form), so the body is built by the client's reader.</summary>
    static byte[] SingleStatNpc(byte stat)
    {
        var w = new ByteWriter();
        w.WriteInt(77);                    // object id
        w.WriteInt(9000001);               // npc id
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3);     // position
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);     // rotation
        w.WriteByte(1);                    // the single-stat form
        w.WriteByte(stat);
        // IsPercentStat (@0x14082f1a0) holds for health (4) only: three i64; any other stat three u32
        if (stat == 4) { w.WriteLong(1000); w.WriteLong(900); w.WriteLong(800); }
        else { w.WriteInt(1000); w.WriteInt(900); w.WriteInt(800); }
        w.WriteByte(0);                    // not hidden
        w.WriteShort(0);                   // no buffs
        w.WriteLong(0);                    // maid uid
        w.WriteBool(false);                // talkable
        w.WriteInt(40);                    // level
        w.WriteInt(0);                     // unknown
        w.WriteByte(0);                    // flag
        return w.ToArray();
    }

    [Test]
    public void Gms2FieldAddNpcType1ReadsTheSingleStat()
    {
        var n = FieldAddNpc.Read(new ByteReader(SingleStatNpc(4)), _ => default);
        // health: the three i64 values are its total, base and current (SetStat(i, 4, value)); no spirit
        Assert.That(n.OneStat, Is.EqualTo((byte) 4));
        Assert.That(n.Stats.Select(s => s.Health), Is.EqualTo(new long[] { 1000, 900, 800 }));
        Assert.That(n.Stats.Select(s => s.Spirit), Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That((n.OneStat, n.Level), Is.EqualTo(((byte) 4, 40)));
    }

    /// <summary>CNpc::vfunc_90 reads any stat but health as three u32, and only health and spirit land in the rows.</summary>
    [Test]
    public void TheSingleStatOfAnotherStatIsThreeInts()
    {
        foreach (var (stat, health, spirit) in new[] { ((byte) 0xd, 0L, 900), ((byte) 35, 0L, 0) }) {
            var w = new ByteWriter();
            w.WriteInt(77); w.WriteInt(9000001); for (int i = 0; i < 6; i++) w.WriteFloat(0);
            w.WriteByte(1); w.WriteByte(stat); w.WriteInt(1000); w.WriteInt(900); w.WriteInt(800);
            w.WriteByte(0); w.WriteShort(0); w.WriteLong(0); w.WriteBool(false); w.WriteInt(40); w.WriteInt(0); w.WriteByte(0);
            var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
            var n = FieldAddNpc.Read(r, _ => default);
            Assert.That((n.OneStat, n.OneStatValues, n.Stats[1].Health, n.Stats[1].Spirit, n.Level, r.Available),
                Is.EqualTo((stat, new long[] { 1000, 900, 800 }, health, spirit, 40, 0)), $"stat {stat}");
        }
    }

    [Test]
    public void FieldAddNpcSingleStatOfAnotherStatReadsThreeU32()
    {
        // stat 35 is not a percent stat: three u32 values, set on that stat; the health rows stay unset
        var n = FieldAddNpc.Read(new ByteReader(SingleStatNpc(35)), _ => default);
        Assert.That(n.OneStat, Is.EqualTo((byte) 35));
        Assert.That(n.OneStatValues, Is.EqualTo(new long[] { 1000, 900, 800 }));
        Assert.That(n.Stats.Select(s => s.Health), Is.EqualTo(new long[] { 0, 0, 0 }));
        Assert.That(n.Level, Is.EqualTo(40));
    }

    [Test]
    public void Gms2FollowNpcReadsTheNpcId()
    {
        var p = RcLane.Read("FollowNpc", Gms2Follow);
        Assert.That(RcLane.Long(p, "NpcId", "Id"), Is.EqualTo(378_468L));
    }

    [Test]
    public void KmsFollowNpcReadsTheNpcId()
    {
        var p = RcLane.Read("FollowNpc", KmsFollow);
        Assert.That(RcLane.Long(p, "NpcId", "Id"), Is.EqualTo(5_789_407L));
    }

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

    [Test]
    public void Gms2NpcNoticeReadsTheAnnounceMode()
    {
        var p = RcLane.Read("NpcNotice", Gms2NpcNoticeAnnounce);
        Assert.That(RcLane.Int(p, "Mode"), Is.EqualTo(0));
        Assert.That(RcLane.MemberAny<string>(p, "Text"), Is.EqualTo("$BullArmorRed_DashAttack$"));
        Assert.That(RcLane.Int(p, "Duration"), Is.EqualTo(10_000));
    }

    [Test]
    public void NpcNoticeOfAnotherModeReadsNoMore()
    {
        // the client's else branch is empty: the rest stays unread
        var n = NpcNotice.Read(new ByteReader(RcLane.Bytes("07AABBCC")));
        Assert.That(n.Mode, Is.EqualTo(7));
    }

    /// <summary>Every recorded FollowNpc, NpcNotice and Insignia packet of both corpora (KMS2 and GMS2 numbers) reads to its
    /// end; a NpcNotice of an unknown mode is not in them.</summary>
    [Test]
    public void EveryRecordedFollowNoticeAndNametagReadsToTheEnd()
    {
        string root = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("MS2_SNIFFS needed");
        var seen = new Dictionary<string, int>();
        foreach (var (dir, profile) in new[] { ("kms2-sniffs", ProtocolProfile.Kms2525), ("gms2-sniffs", ProtocolProfile.Gms2) })
        {
            var ops = new Dictionary<ushort, Func<ByteReader, object>>
            {
                [profile.Number(Inbound.FollowNpc)] = r => FollowNpc.Read(r),
                [profile.Number(Inbound.NpcNotice)] = r => NpcNotice.Read(r),
                [profile.Number(Inbound.Insignia)] = r => Nametag.Read(r),
            };
            foreach (var path in Directory.GetFiles(Path.Combine(root, dir), "*.msb", SearchOption.AllDirectories))
            {
                Ms2.Net.Replay.MsbFile f;
                try { f = Ms2.Net.Replay.MsbFile.Read(path); } catch (Exception) { continue; }
                // the KMS2 builds before 2520 number their ops apart (their 0xE8 is another packet), as in NpcSniffTests
                if (dir == "kms2-sniffs" && f.Build < 2520) continue;
                foreach (var pkt in f.Packets)
                {
                    if (!pkt.ServerToClient || !ops.TryGetValue(pkt.Opcode, out var read) || pkt.Data.Length == 0) continue;
                    var r = new ByteReader(pkt.Data);
                    var v = read(r);
                    Assert.That(r.Available, Is.EqualTo(0), $"{Path.GetFileName(path)} op 0x{pkt.Opcode:X}");
                    if (v is NpcNotice n) Assert.That(n.Mode, Is.LessThanOrEqualTo(NpcNotice.SidePopup), Path.GetFileName(path));
                    string key = $"{dir} {v.GetType().Name}";
                    seen[key] = seen.GetValueOrDefault(key) + 1;
                }
            }
        }
        TestContext.WriteLine(string.Join(", ", seen.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.That(seen.Keys, Is.SupersetOf(new[] { "gms2-sniffs FollowNpc", "gms2-sniffs NpcNotice", "kms2-sniffs FollowNpc", "kms2-sniffs Nametag" }));
    }

    [Test]
    public void TheOwnNametagSetsTheSymbolAndMarksTheRecordChanged()
    {
        // OnNametag (scenestategame.cpp:7314-7331): FindPCById, FUN_141661dc0(symbol, shown); the MyPC's dialog refresh
        var me = new MyCharacter { ObjectId = 7 };
        int changed = 0;
        me.Changed += () => changed++;
        Assert.That(me.Apply(new Nametag(8, 47, true)), Is.False, "another player's packet");
        Assert.That(me.Insignia, Is.EqualTo(0));
        Assert.That(changed, Is.EqualTo(0));
        Assert.That(me.Apply(Nametag.Read(new ByteReader(RcLane.Bytes("07000000" + "3200" + "01")))), Is.True);
        Assert.That(me.Insignia, Is.EqualTo(50));
        Assert.That(me.InsigniaShown, Is.True);
        Assert.That(changed, Is.EqualTo(1));
        me.Apply(new Nametag(7, 50, false));
        Assert.That(me.InsigniaShown, Is.False);
    }

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

    [Test]
    public void Gms2OpcodeRowsAreAdded()
    {
        Assert.That(RcLane.Gms2Inbound(RcLane.EnumMember<Inbound>("FollowNpc")), Is.EqualTo(0x72));
        Assert.That(RcLane.Gms2Inbound(RcLane.EnumMember<Inbound>("NpcNotice")), Is.EqualTo(0xEA));
        // the live name tag: the implementation may call the member Insignia or Nametag. GMS2 0xB3 is the upstream server's
        // SendOp.Insignia, next to WorldShareInfo 0xB2 (KMS 0xB0 and 0xB1); no GMS2 recording has the packet
        Inbound tag;
        if (!Enum.TryParse("Nametag", out tag) && !Enum.TryParse("Insignia", out tag))
            Assert.Fail("Ms2.Net.Inbound.Nametag / Insignia is not defined at origin/main");
        Assert.That(RcLane.Gms2Inbound(tag), Is.EqualTo(0xB3));
        Assert.That((ushort)RcLane.EnumMember<Inbound>("FollowNpc"), Is.EqualTo(0x70));
        Assert.That((ushort)RcLane.EnumMember<Inbound>("NpcNotice"), Is.EqualTo(0xE8));
        Assert.That((ushort)tag, Is.EqualTo(0xB1));
    }
}
