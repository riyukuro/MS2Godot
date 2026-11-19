using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Guild packet types the guild dialog reads (0x8A, CGuildManager FUN_1413b6370) and its sends (Guild 0x4B).</summary>
public class GuildPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Str(string s) { var w = new ByteWriter(); w.WriteUnicodeString(s); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Cat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    static readonly byte[] Id5 = { 5, 0, 0, 0, 0, 0, 0, 0 };

    [Test]
    public void TheRequestsWriteTheirModeAndFields() {
        Assert.That(new GuildCreateRequest("G").Op, Is.EqualTo(Outbound.Guild));
        Assert.That(Bytes(new GuildCreateRequest("Gd")), Is.EqualTo(Cat(new byte[] { 1 }, Str("Gd"))));
        Assert.That(Bytes(new GuildJoinRequestSend(5)), Is.EqualTo(Cat(new byte[] { 0x52 }, Id5)));
        Assert.That(Bytes(new GuildJoinCancelRequest(5)), Is.EqualTo(Cat(new byte[] { 0x53 }, Id5)));
        Assert.That(Bytes(new GuildMyRequestsRequest()), Is.EqualTo(new byte[] { 0x56 }));
        Assert.That(Bytes(new GuildSearchRequest(0x7fffffff)), Is.EqualTo(new byte[] { 0x57, 0xff, 0xff, 0xff, 0x7f, 1, 0, 0, 0 }));
        Assert.That(Bytes(new GuildSearchByNameRequest("Gd")), Is.EqualTo(Cat(new byte[] { 0x58 }, Str("Gd"))));
    }

    [Test]
    public void TheTypesReadTheirFields() {
        Assert.That(GuildMessage.Read(R(w => { w.WriteByte(0); w.WriteLong(7); })).GuildId, Is.EqualTo(7));
        var fail = GuildMessage.Read(R(w => { w.WriteByte(1); w.WriteByte(0x13); }));
        Assert.That((fail.Result, fail.Name), Is.EqualTo((0x13, "")));
        var ok = GuildMessage.Read(R(w => { w.WriteByte(1); w.WriteByte(0); w.WriteUnicodeString("Gd"); }));
        Assert.That((ok.Result, ok.Name), Is.EqualTo((0, "Gd")));
        var sent = GuildMessage.Read(R(w => { w.WriteByte(0x52); w.WriteLong(9); w.WriteUnicodeString("Gd"); }));
        Assert.That((sent.Type, sent.GuildId, sent.Name), Is.EqualTo(((byte)0x52, 9L, "Gd")));
        var removed = GuildMessage.Read(R(w => { w.WriteByte(0x30); w.WriteUnicodeString("Gd"); w.WriteLong(4); w.WriteByte(1); }));
        Assert.That((removed.Name, removed.RequestId, removed.Joined), Is.EqualTo(("Gd", 4L, true)));
    }

    [Test]
    public void TheListsSkipEmptySlotsAndReadEachEntry() {
        var search = GuildMessage.Read(R(w => {
            w.WriteByte(0x57); w.WriteInt(2); w.WriteByte(0); w.WriteByte(1);
            w.WriteLong(9); w.WriteUnicodeString("Gd"); w.WriteUnicodeString("mark");
            w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteInt(4); w.WriteInt(50); w.WriteInt(600);
            w.WriteLong(11); w.WriteLong(12); w.WriteUnicodeString("Ma");
        }));
        Assert.That(search.SearchCount, Is.EqualTo(2));
        Assert.That(search.Results, Has.Count.EqualTo(1));
        var g = search.Results[0];
        Assert.That((g.GuildId, g.Name, g.TrophyTotal, g.MemberCount, g.Capacity, g.Exp, g.MasterId, g.MasterAccountId, g.MasterName),
            Is.EqualTo((9L, "Gd", 6, 4, 50, 600, 11L, 12L, "Ma")));
        var mine = GuildMessage.Read(R(w => {
            w.WriteByte(0x56); w.WriteInt(1); w.WriteByte(1);
            w.WriteLong(4); w.WriteLong(9); w.WriteUnicodeString("Gd"); w.WriteInt(600); w.WriteUnicodeString("mark");
            w.WriteInt(1); w.WriteInt(1); w.WriteInt(1); w.WriteInt(3); w.WriteInt(50); w.WriteUnicodeString("Ma");
            w.WriteLong(0); w.WriteLong(0); w.WriteLong(0);
        }));
        var r = mine.Requests.Single();
        Assert.That((r.Id, r.GuildId, r.GuildName, r.Exp, r.TrophyTotal, r.MemberCount, r.Capacity, r.MasterName),
            Is.EqualTo((4L, 9L, "Gd", 600, 3, 3, 50, "Ma")));
    }

    /// <summary>A guild info packet (type 0) of one full member, one short member, two grades and a skill.</summary>
    internal static ByteReader InfoPacket() => R(w => {
        w.WriteByte(0); w.WriteLong(7); w.WriteUnicodeString("Gd"); w.WriteUnicodeString("mark"); w.WriteByte(50);
        w.WriteUnicodeString("tag"); w.WriteUnicodeString("hello"); w.WriteLong(11); w.WriteLong(12); w.WriteUnicodeString("Ma"); w.WriteLong(1000);
        w.WriteByte(1); for (int i = 0; i < 8; i++) w.WriteInt(i); w.WriteByte(3); w.WriteInt(4); w.WriteInt(600); w.WriteInt(70); w.WriteInt(8);
        w.WriteBool(true); w.WriteInt(9);
        w.WriteByte(2);
        w.WriteByte(3); w.WriteByte(0); w.WriteLong(1);                       // the full member: mode 3, grade 0, id
        w.WriteLong(10); w.WriteLong(12); w.WriteUnicodeString("Ma"); w.WriteByte(0); w.WriteInt(1); w.WriteInt(300); w.WriteShort(40);
        w.WriteInt(100); w.WriteInt(2000); w.WriteShort(2); w.WriteUnicodeString("img"); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteLong(0);
        w.WriteInt(1); w.WriteInt(2); w.WriteInt(3);
        w.WriteUnicodeString("memo"); w.WriteLong(0); w.WriteLong(55); w.WriteLong(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteInt(5); w.WriteInt(6); w.WriteInt(0); w.WriteLong(0);
        w.WriteInt(1); w.WriteInt(9); w.WriteInt(4); w.WriteByte(0);
        w.WriteByte(1); w.WriteByte(1); w.WriteLong(2);                       // the short member: mode 1, grade 1
        w.WriteUnicodeString("Bo"); w.WriteInt(0); w.WriteInt(200); w.WriteShort(20); w.WriteUnicodeString("img2");
        w.WriteByte(2); w.WriteByte(0); w.WriteUnicodeString("Master"); w.WriteInt(4095); w.WriteByte(1); w.WriteUnicodeString("Member"); w.WriteInt(8);
        w.WriteByte(1); w.WriteInt(3); w.WriteInt(2); w.WriteLong(0);
    });

    [Test]
    public void TheInfoPacketReadsTheHeaderMembersGradesAndSkills() {
        var m = GuildMessage.Read(InfoPacket());
        var g = m.Info;
        Assert.That((m.GuildId, g.Name, g.Code, g.MaxMembers, g.Notice, g.MasterCharId, g.MasterName, g.CreateTime, g.Exp, g.Fund, g.EventFund),
            Is.EqualTo((7L, "Gd", "mark", 50, "hello", 12L, "Ma", 1000L, 600, 70, 8)));
        Assert.That(g.Members, Has.Count.EqualTo(2));
        var a = g.Members[0];
        Assert.That((a.Mode, a.Name, a.CharId, a.JobCode, a.Level, a.ItemLevel, a.MapId, a.Channel, a.TrophyTotal, a.Memo, a.LastSeen, a.Contribution,
            a.TotalContribution, a.RaidTotal, a.Offline), Is.EqualTo((3, "Ma", 12L, 300, 40, 100, 2000, 2, 6, "memo", 55L, 5, 6, 4, 0)));
        var b = g.Members[1];
        Assert.That((b.Mode, b.Grade, b.Name, b.JobCode, b.Level, b.Code, b.Offline), Is.EqualTo((1, 1, "Bo", 200, 20, "img2", 1)));
        Assert.That(g.Grades.Select(x => (x.Grade, x.Name, x.Permissions)), Is.EqualTo(new[] { (0, "Master", 4095), (1, "Member", 8) }));
        Assert.That(g.Skills.Single(), Is.EqualTo(new GuildSkillState(3, 2, 0)));
    }
}
