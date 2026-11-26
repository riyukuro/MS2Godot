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
        Assert.That(Bytes(new GuildRespondRequest(5, true)), Is.EqualTo(Cat(new byte[] { 0x54 }, Id5, new byte[] { 1 })));
        Assert.That(Bytes(new GuildRespondRequest(5, false)), Is.EqualTo(Cat(new byte[] { 0x54 }, Id5, new byte[] { 0 })));
        Assert.That(Bytes(new GuildListRequestsRequest()), Is.EqualTo(new byte[] { 0x55 }));
        Assert.That(Bytes(new GuildLeaveRequest()), Is.EqualTo(new byte[] { 7 }));
        Assert.That(Bytes(new GuildBreakRequest()), Is.EqualTo(new byte[] { 2 }));
    }

    [Test]
    public void TheTypesReadTheirFields() {
        Assert.That(GuildMessage.Read(InfoPacket()).GuildId, Is.EqualTo(7));
        var fail = GuildMessage.Read(R(w => { w.WriteByte(1); w.WriteByte(0x13); }));
        Assert.That((fail.Result, fail.Name), Is.EqualTo((0x13, "")));
        var ok = GuildMessage.Read(R(w => { w.WriteByte(1); w.WriteByte(0); w.WriteUnicodeString("Gd"); }));
        Assert.That((ok.Result, ok.Name), Is.EqualTo((0, "Gd")));
        var sent = GuildMessage.Read(R(w => { w.WriteByte(0x52); w.WriteLong(9); w.WriteUnicodeString("Gd"); }));
        Assert.That((sent.Type, sent.GuildId, sent.Name), Is.EqualTo(((byte)0x52, 9L, "Gd")));
        var removed = GuildMessage.Read(R(w => { w.WriteByte(0x30); w.WriteUnicodeString("Gd"); w.WriteLong(4); w.WriteByte(1); }));
        Assert.That((removed.Name, removed.RequestId, removed.Joined), Is.EqualTo(("Gd", 4L, true)));
        var disbanded = GuildMessage.Read(R(w => { w.WriteByte(2); w.WriteByte(0); }));
        Assert.That(disbanded.Result, Is.EqualTo(0));
        var leave = GuildMessage.Read(R(w => w.WriteByte(7)));
        Assert.That(leave.Type, Is.EqualTo((byte)7));
        var answer = GuildMessage.Read(R(w => { w.WriteByte(0x54); w.WriteLong(13); w.WriteUnicodeString("bo"); w.WriteByte(1); }));
        Assert.That((answer.GuildId, answer.Applicant, answer.Accepted), Is.EqualTo((13L, "bo", true)));
        var withdrawn = GuildMessage.Read(R(w => { w.WriteByte(0x2e); w.WriteLong(4); }));
        Assert.That(withdrawn.RequestId, Is.EqualTo(4L));
        var answered = GuildMessage.Read(R(w => { w.WriteByte(0x2f); w.WriteUnicodeString("Ma"); w.WriteUnicodeString("bo"); w.WriteByte(1); w.WriteLong(4); }));
        Assert.That((answered.Name, answered.Applicant, answered.Accepted, answered.RequestId), Is.EqualTo(("Ma", "bo", true, 4L)));
        var left = GuildMessage.Read(R(w => { w.WriteByte(0x13); w.WriteUnicodeString("bo"); }));
        Assert.That(left.Name, Is.EqualTo("bo"));
    }

    [Test]
    public void TheRequestsForTheGuildReadTheirEntries() {
        var arrived = GuildMessage.Read(R(w => { w.WriteByte(0x2d);
            w.WriteLong(4); w.WriteLong(9); w.WriteLong(11); w.WriteLong(13); w.WriteUnicodeString("bo"); w.WriteUnicodeString("img");
            w.WriteInt(100); w.WriteInt(200); w.WriteInt(20); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteLong(77);
        }));
        var r = arrived.NewRequest;
        Assert.That((r.Id, r.GuildId, r.AccountId, r.CharId, r.Name, r.Picture, r.JobCode, r.Job, r.Level, r.TrophyTotal, r.Time),
            Is.EqualTo((4L, 9L, 11L, 13L, "bo", "img", 100, 200, 20, 6, 77L)));
        var list = GuildMessage.Read(R(w => {
            w.WriteByte(0x55); w.WriteInt(1); w.WriteByte(1);
            w.WriteLong(4); w.WriteLong(9); w.WriteLong(11); w.WriteLong(13); w.WriteUnicodeString("bo"); w.WriteUnicodeString("img");
            w.WriteInt(100); w.WriteInt(200); w.WriteInt(20); w.WriteInt(1); w.WriteInt(2); w.WriteInt(3); w.WriteLong(77);
        }));
        Assert.That(list.GuildRequests.Single().Name, Is.EqualTo("bo"));
    }

    [Test]
    public void TheMemberJoinedReadsTheNamesTheNoticeAndTheMember() {
        var m = GuildMessage.Read(R(w => {
            w.WriteByte(0x12); w.WriteUnicodeString("Ma"); w.WriteUnicodeString("bo"); w.WriteByte(1);
            w.WriteByte(3); w.WriteByte(1); w.WriteLong(1);
            w.WriteLong(10); w.WriteLong(13); w.WriteUnicodeString("bo"); w.WriteByte(0); w.WriteInt(0); w.WriteInt(200); w.WriteShort(20);
            w.WriteInt(100); w.WriteInt(2000); w.WriteShort(2); w.WriteUnicodeString("img2"); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteLong(0);
            w.WriteInt(1); w.WriteInt(2); w.WriteInt(3);
            w.WriteUnicodeString(""); w.WriteLong(0); w.WriteLong(66); w.WriteLong(0);
            w.WriteInt(0); w.WriteInt(0); w.WriteInt(5); w.WriteInt(6); w.WriteInt(0); w.WriteLong(0);
            w.WriteInt(0); w.WriteByte(1);
        }));
        Assert.That((m.Name, m.Applicant, m.Notify), Is.EqualTo(("Ma", "bo", true)));
        Assert.That((m.Member.Mode, m.Member.Grade, m.Member.Name, m.Member.CharId, m.Member.Level), Is.EqualTo((3, 1, "bo", 13L, 20)));
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
        w.WriteByte(0); w.WriteInt(1); w.WriteInt(1); w.WriteInt(0);          // no custom events; the two i32 and no string records
        w.WriteByte(0); w.WriteBool(false); w.WriteInt(0);                    // no npcs, no npc shop, empty storage
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

    [Test]
    public void TheMemberSendsWriteTheirModeAndFields() {
        // guildmanager.cpp FUN_1413b71c0 (u8 3), SendGuildMode8 (8), SendGuildMode10 (10, u8 grade), SendGuildMode63 (0x3f),
        // FUN_1413b7c70 (0x40, u8 kind), SendGuildMode13 (0xd), SendGuildMode68 (0x44, u8 focus, i32 value)
        Assert.That(new GuildInviteRequest("Bo").Op, Is.EqualTo(Outbound.Guild));
        Assert.That(Bytes(new GuildInviteRequest("Bo")), Is.EqualTo(Cat(new byte[] { 3 }, Str("Bo"))));
        Assert.That(Bytes(new GuildExpelRequest("Bo")), Is.EqualTo(Cat(new byte[] { 8 }, Str("Bo"))));
        Assert.That(Bytes(new GuildMemberGradeRequest("Bo", 2)), Is.EqualTo(Cat(new byte[] { 10 }, Str("Bo"), new byte[] { 2 })));
        Assert.That(Bytes(new GuildChangeMasterRequest("Bo")), Is.EqualTo(Cat(new byte[] { 0x3f }, Str("Bo"))));
        Assert.That(Bytes(new GuildTextRequest(1, "hi")), Is.EqualTo(Cat(new byte[] { 0x40, 1 }, Str("hi"))));
        Assert.That(Bytes(new GuildMemoRequest("hi")), Is.EqualTo(Cat(new byte[] { 0xd }, Str("hi"))));
        Assert.That(Bytes(new GuildPropensityRequest(1, 0x204)), Is.EqualTo(new byte[] { 0x44, 1, 4, 2, 0, 0 }));
        Assert.That(Bytes(new GuildGradeRequest(1, new GuildGrade(1, "Ab", 8))),
            Is.EqualTo(Cat(new byte[] { 0x43, 1, 1 }, Str("Ab"), new byte[] { 8, 0, 0, 0 })));
    }

    [Test]
    public void ThePropensityTypeReadsTheFocusAndValue() {
        // FUN_1413bf2a0 (guildmanager.cpp:1855): u8 focus, i32 value
        var m = GuildMessage.Read(R(w => { w.WriteByte(0x44); w.WriteByte(1); w.WriteInt(0x204); }));
        Assert.That((m.Type, m.Focus, m.Value), Is.EqualTo(((byte)0x44, 1, 0x204)));
    }

    [Test]
    public void TheInviteMarkIsAUtf16StringOnTheWire() {
        // ReadUnicodeStringA (stream.cpp:166) is DecodeStr, u16 count + UTF-16, then narrowed in memory; WriteUnicodeStringA
        // (:112) widens and stores a unicode string. So the mark (f28) has the same wire as the names around it.
        byte[] wire = Cat(BitConverter.GetBytes(5L), Str("Gd"), new byte[] { 4, 0, (byte)'1', 0, (byte)'a', 0, (byte)'2', 0, (byte)'b', 0 },
            Str("Ma"), Str("Me"));
        var invite = GuildInviteInfo.Read(new ByteReader(wire));
        Assert.That((invite.Mark, invite.Inviter, invite.Invitee), Is.EqualTo(("1a2b", "Ma", "Me")));
        var w = new ByteWriter();
        invite.Write(w);
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(wire));
    }

    [Test]
    public void TheInviteTypesReadAndTheAnswerWritesTheInviteBack() {
        // CGuildInviteInfo::ReadStream (@0x1404c9230): i64, the guild name, the mark (A), the inviter, the invitee
        static void Invite(ByteWriter w) { w.WriteLong(5); w.WriteUnicodeString("Gd"); w.WriteUnicodeString("mk"); w.WriteUnicodeString("Ma"); w.WriteUnicodeString("Me"); }
        var got = GuildMessage.Read(R(w => { w.WriteByte(4); Invite(w); }));
        Assert.That((got.Type, got.Invite.GuildId, got.Invite.GuildName, got.Invite.Mark, got.Invite.Inviter, got.Invite.Invitee),
            Is.EqualTo(((byte)4, 5L, "Gd", "mk", "Ma", "Me")));
        var result = GuildMessage.Read(R(w => { w.WriteByte(5); Invite(w); w.WriteByte(1); }));
        Assert.That((result.Invite.GuildName, result.Result), Is.EqualTo(("Gd", 1)));
        var sent = GuildMessage.Read(R(w => { w.WriteByte(3); w.WriteUnicodeString("Gd"); }));
        Assert.That((sent.Type, sent.Name), Is.EqualTo(((byte)3, "Gd")));
        var refused = GuildMessage.Read(R(w => { w.WriteByte(6); w.WriteUnicodeString("Me"); w.WriteByte(0); w.WriteByte(2); }));
        Assert.That((refused.Name, refused.Accepted, refused.Reason), Is.EqualTo(("Me", false, 2)));
        // SendGuildMode5: u8 5, the invite as read, u8 accept
        Assert.That(Bytes(new GuildInviteAnswerRequest(got.Invite, true)),
            Is.EqualTo(Cat(new byte[] { 5 }, Id5, Str("Gd"), Str("mk"), Str("Ma"), Str("Me"), new byte[] { 1 })));
    }

    [Test]
    public void AttendAndDonateWriteTheirModesAndTheDonationReads() {
        Assert.That(Bytes(new GuildAttendRequest()), Is.EqualTo(new byte[] { 0xf }));
        Assert.That(Bytes(new GuildDonateRequest(3)), Is.EqualTo(new byte[] { 0x71, 3, 0, 0, 0 }));
        Assert.That(GuildMessage.Read(R(w => w.WriteByte(0xf))).Type, Is.EqualTo((byte)0xf));
        var d = GuildMessage.Read(R(w => { w.WriteByte(0x71); w.WriteInt(2); w.WriteLong(99); }));
        Assert.That((d.DonationCount, d.DonationTime), Is.EqualTo((2, 99L)));
    }

    [Test]
    public void TheVsGameAndEventTypesReadTheirFields() {
        var t = GuildMessage.Read(R(w => { w.WriteByte(0x76); w.WriteByte(1); w.WriteByte(0); }));
        Assert.That((t.Accepted, t.Notify), Is.EqualTo((true, false)));
        Assert.That(GuildMessage.Read(R(w => w.WriteByte(0x75))).Type, Is.EqualTo((byte)0x75));
        Assert.That(GuildMessage.Read(R(w => { w.WriteByte(0x77); w.WriteInt(-3); })).Value, Is.EqualTo(-3));
        var e = GuildMessage.Read(R(w => { w.WriteByte(0x38); w.WriteUnicodeString("bo"); w.WriteInt(4); w.WriteInt(1234); }));
        Assert.That((e.Name, e.EventId, e.EventTime), Is.EqualTo(("bo", 4, 1234)));
    }

    [Test]
    public void TheHouseAndNpcUpgradeTypesReadTheirFields() {
        var h = GuildMessage.Read(R(w => { w.WriteByte(0x39); w.WriteUnicodeString("bo"); w.WriteInt(3); w.WriteInt(2); }));
        Assert.That((h.Name, h.HouseLevel, h.HouseTheme), Is.EqualTo(("bo", 3, 2)));
        var n = GuildMessage.Read(R(w => { w.WriteByte(0x3b); w.WriteUnicodeString("bo"); w.WriteInt(4); w.WriteInt(5); }));
        Assert.That((n.Name, n.NpcKind, n.NpcLevel), Is.EqualTo(("bo", 4, 5)));
        Assert.Throws<PacketLayoutException>(() => GuildMessage.Read(R(w => { w.WriteByte(0x39); w.WriteUnicodeString("bo"); w.WriteInt(3); w.WriteInt(2); w.WriteByte(0); })), "ExpectEnd");
    }

    [Test]
    public void TheStorageTypesReadTheirFields() {
        void Item(ByteWriter w, int id, int rank, int count) { w.WriteInt(id); w.WriteShort((short) rank); w.WriteInt(count); w.WriteByte(1); w.WriteByte(0); w.WriteByte(1); w.WriteByte(0); }
        var e = GuildMessage.Read(R(w => { w.WriteByte(0x6e); Item(w, 1001, -1, 5); }));
        Assert.That(e.StorageItem, Is.EqualTo(new GuildStorageItem(1001, 65535, 5, true, false, true, false)), "the rank is unsigned");
        var g = GuildMessage.Read(R(w => { w.WriteByte(0x6d); w.WriteByte(0); w.WriteUnicodeString("bo"); }));
        Assert.That((g.Result, g.Name), Is.EqualTo((0, "bo")));
        var f = GuildMessage.Read(R(w => { w.WriteByte(0x6d); w.WriteByte(0x36); }));
        Assert.That((f.Result, f.Name), Is.EqualTo((0x36, "")));
        var h = GuildMessage.Read(R(w => {
            w.WriteByte(0x70); w.WriteInt(1);
            w.WriteLong(1); w.WriteLong(2); w.WriteUnicodeString("a"); w.WriteLong(3); w.WriteLong(4); w.WriteUnicodeString("b");
            Item(w, 7, 1, 2); w.WriteLong(99);
        }));
        Assert.That(h.StorageHistory.Single(), Is.EqualTo(new GuildStorageHistory(new(1, 2, "a"), new(3, 4, "b"), new GuildStorageItem(7, 1, 2, true, false, true, false), 99)));
        Assert.Throws<PacketLayoutException>(() => GuildMessage.Read(R(w => { w.WriteByte(0x6d); w.WriteByte(0x36); w.WriteByte(0); })), "ExpectEnd");
    }

    [Test]
    public void TheGuildInfoKeepsTheStorageItems() {
        var info = new GuildInfo();
        info.StorageItems.Add(new GuildStorageItem(5, 2, 9, false, true, false, true));
        var w = new ByteWriter(); new GuildStorageItem(5, 2, 9, false, true, false, true).Write(w);
        Assert.That(GuildStorageItem.Read(new ByteReader(w.ToArray())), Is.EqualTo(info.StorageItems[0]), "Write and Read agree");
    }

    [Test]
    public void TheRaidTypesReadTheirFieldsAndEndWithThem() {
        var m = GuildMessage.Read(R(w => { w.WriteByte(0x25); w.WriteUnicodeString("bo"); w.WriteInt(3); w.WriteInt(9); }));
        Assert.That((m.Name, m.RaidId, m.Value), Is.EqualTo(("bo", 3, 9)));
        Assert.Throws<PacketLayoutException>(() => GuildMessage.Read(R(w => { w.WriteByte(0x25); w.WriteUnicodeString("bo"); w.WriteInt(3); w.WriteInt(9); w.WriteByte(0); })), "ExpectEnd");
    }

    [Test]
    public void TheWeeklyAndRaidResetsFallIntoTheTicketsTwoBools() {
        // guildmanager.cpp:367-369: 0x3d and 0x3e read nothing and fall into OnPacketVsGameTicket (GMS2 59 and 60 into 115).
        foreach (var (wire, kms, profile) in new[] { ((byte) 0x3d, (byte) 0x3d, ProtocolProfile.Kms2525), ((byte) 0x3e, (byte) 0x3e, ProtocolProfile.Kms2525),
                     ((byte) 59, (byte) 0x3d, ProtocolProfile.Gms2), ((byte) 60, (byte) 0x3e, ProtocolProfile.Gms2) }) {
            var m = GuildMessage.Read(R(w => { w.WriteByte(wire); w.WriteByte(1); w.WriteByte(1); }), profile);
            Assert.That((m.Type, m.Ticket, m.Accepted, m.Notify), Is.EqualTo((kms, true, true, true)), $"{profile.Name} {wire}");
            // The retail GMS2 server's weekly reset has no body: the reset alone.
            var empty = GuildMessage.Read(R(w => w.WriteByte(wire)), profile);
            Assert.That((empty.Type, empty.Ticket), Is.EqualTo((kms, false)), $"{profile.Name} {wire} empty");
            Assert.Throws<PacketLayoutException>(() => GuildMessage.Read(R(w => { w.WriteByte(wire); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); }), profile), "ExpectEnd");
        }
    }

    [Test]
    public void AGms2BodyOfAnUncheckedLayoutIsSkippedWithAWarning() {
        var warnings = new List<string>(); var old = GuildMessage.Warning; GuildMessage.Warning = warnings.Add;
        try {
            foreach (byte kms in GuildMessage.Gms2UncheckedLayouts) {
                byte wire = GuildModes.Gms2Receive.Single(kv => kv.Value == kms).Key;
                // One entry present, then a byte the KMS entry reader cannot place.
                var m = GuildMessage.Read(R(w => { w.WriteByte(wire); w.WriteInt(1); w.WriteByte(1); w.WriteByte(9); }), ProtocolProfile.Gms2);
                Assert.That(m.Type, Is.EqualTo(GuildModes.NoKmsType), $"GMS2 {wire}");
                Assert.That(warnings.Last(), Does.Contain($"GMS2 type {wire}"));
                // An empty list still reads.
                Assert.That(GuildMessage.Read(R(w => { w.WriteByte(wire); w.WriteInt(0); }), ProtocolProfile.Gms2).Type, Is.EqualTo(kms));
                // KMS keeps the strict read.
                Assert.That(() => GuildMessage.Read(R(w => { w.WriteByte(kms); w.WriteInt(1); w.WriteByte(1); w.WriteByte(9); }), ProtocolProfile.Kms2525), Throws.Exception);
            }
            Assert.That(warnings, Has.Count.EqualTo(GuildMessage.Gms2UncheckedLayouts.Length));
        } finally { GuildMessage.Warning = old; }
    }

    static void WriteBattle(ByteWriter w, long time) {
        w.WriteInt(1); w.WriteInt(2); w.WriteByte(1); w.WriteLong(11); w.WriteLong(12); w.WriteUnicodeString("A"); w.WriteUnicodeString("B");
        for (int i = 0; i < 4; i++) w.WriteInt(20 + i);
        for (int i = 0; i < 5; i++) { w.WriteUnicodeString("1,m:2,n"); w.WriteUnicodeString("3,o"); w.WriteByte(i % 2 == 0 ? (byte)'W' : (byte)'L'); }
        w.WriteLong(77); w.WriteLong(time);
    }

    [Test]
    public void ThePvpStateAndScoreTypesReadTheirFieldsAndEndWithThem() {
        Assert.That(GuildMessage.Read(R(w => { w.WriteByte(0x27); w.WriteByte(3); })).PvpState, Is.EqualTo(3));
        var m = GuildMessage.Read(R(w => { w.WriteByte(0x29); w.WriteInt(500); w.WriteInt(4); w.WriteInt(2); }));
        Assert.That((m.Score, m.Wins, m.Losses), Is.EqualTo((500, 4, 2)));
        Assert.Throws<PacketLayoutException>(() => GuildMessage.Read(R(w => { w.WriteByte(0x27); w.WriteByte(3); w.WriteByte(0); })));
        Assert.Throws<PacketLayoutException>(() => GuildMessage.Read(R(w => { w.WriteByte(0x29); w.WriteInt(1); w.WriteInt(1); w.WriteInt(1); w.WriteByte(0); })));
    }

    [Test]
    public void ThePvpHistoryKeepsTheEntriesFlaggedPresentInTheClientsFieldOrder() {
        var m = GuildMessage.Read(R(w => {
            w.WriteByte(0x4a); w.WriteInt(3);
            w.WriteByte(1); WriteBattle(w, 1000);
            w.WriteByte(0);
            w.WriteByte(1); WriteBattle(w, 2000);
        }));
        Assert.That(m.PvpHistory.Select(h => h.Time), Is.EqualTo(new[] { 1000L, 2000L }));
        var h0 = m.PvpHistory[0];
        Assert.That((h0.Result, h0.FirstGuildId, h0.SecondGuildId, h0.FirstGuildName, h0.SecondGuildName, h0.Fb0), Is.EqualTo((1, 11L, 12L, "A", "B", 77L)));
        Assert.That((h0.F48[3], h0.FirstMembers[4], h0.SecondMembers[0], (char)h0.RoundResults[1]), Is.EqualTo((23, "1,m:2,n", "3,o", 'L')));
    }

    [Test]
    public void ThePvpSeasonHistoryReadsTheSeasonRows() {
        var m = GuildMessage.Read(R(w => {
            w.WriteByte(0x4b); w.WriteInt(2);
            w.WriteByte(0);
            w.WriteByte(1); w.WriteInt(1); w.WriteLong(2); w.WriteLong(3); w.WriteUnicodeString("S1"); w.WriteInt(4); w.WriteInt(5); w.WriteInt(6); w.WriteUnicodeString("x");
            for (int i = 0; i < 7; i++) w.WriteInt(100 + i);
        }));
        var s = m.PvpSeasonHistory.Single();
        Assert.That((s.SeasonName, s.Rank, s.Grade, s.Wins, s.Losses, s.F68), Is.EqualTo(("S1", 5, 6, 101, 102, 106)));
        Assert.Throws<PacketLayoutException>(() => GuildMessage.Read(R(w => { w.WriteByte(0x4b); w.WriteInt(0); w.WriteByte(0); })));
    }

    /// <summary>OnPacketGuildExp, FUN_1413c4b60 and FUN_1413c4bd0 (guildmanager.cpp:2723, :2739, :2748): one i32 each, the exp, the
    /// fund and the event fund. GMS2 case 49 (0x140a9416a) is the exp and 50 (0x140a941da) the fund; KMS 0x34 has no GMS2 case.</summary>
    [Test]
    public void TheExpAndFundPacketsReadTheirOneValueOnBothRegions() {
        foreach (var (wire, kms, profile) in new[] { ((byte) 0x32, (byte) 0x32, ProtocolProfile.Kms2525), ((byte) 0x33, (byte) 0x33, ProtocolProfile.Kms2525),
                     ((byte) 0x34, (byte) 0x34, ProtocolProfile.Kms2525), ((byte) 49, (byte) 0x32, ProtocolProfile.Gms2), ((byte) 50, (byte) 0x33, ProtocolProfile.Gms2) }) {
            var m = GuildMessage.Read(R(w => { w.WriteByte(wire); w.WriteInt(123456); }), profile);
            Assert.That((m.Type, m.Value), Is.EqualTo((kms, 123456)), $"wire {wire} on {profile.Name}");
            Assert.That(() => GuildMessage.Read(R(w => { w.WriteByte(wire); w.WriteInt(1); w.WriteByte(0); }), profile),
                Throws.InstanceOf<PacketLayoutException>(), $"wire {wire}: one i32 and nothing after");
        }
    }
}
