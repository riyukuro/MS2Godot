using Maple2.PacketLib.Tools;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CGuildManager (GuildBook): the Guild types it applies — outside a guild the requests and the search, in a
/// guild the requests for the guild, the members, the leave and the disband — and its checked sends.</summary>
public class GuildBookTests {
    static GuildBook Book(List<IOutPacket> sent, List<string> notices) {
        var b = new GuildBook { Common = id => $"c{id:x}", CommonByKey = k => k, Send = sent.Add, Level = () => 10, CreateMinLevel = 5 };
        b.Notice += (t, _, _) => notices.Add(t);
        return b;
    }

    [Test]
    public void CreateChecksTheNameAndLevelBeforeTheSend() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = Book(sent, notices);
        Assert.That(b.Create("G"), Is.False);
        Assert.That(notices, Is.EqualTo(new[] { "s_guild_err_invalide_create_name" }));
        b.CheckName = _ => "bad";
        Assert.That(b.Create("Gd"), Is.False);
        b.CheckName = null;
        b.Level = () => 5;
        Assert.That(b.Create("Gd"), Is.False);          // not above GuildCreateMinLevel: error 0x13 (common 0x413)
        Assert.That(notices.Last(), Is.EqualTo("c413"));
        b.Level = () => 6;
        Assert.That(b.Create("Gd"), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildCreateRequest("Gd")));
    }

    [Test]
    public void SearchWithoutAPropensityAsksForOneUnlessAnyFocus() {
        var sent = new List<IOutPacket>(); var boxes = new List<string>();
        var b = Book(sent, new List<string>());
        b.Box += boxes.Add;
        b.Search(false);
        Assert.That((sent.Count, boxes.Single()), Is.EqualTo((0, "c3c9")));
        b.Search(true);
        Assert.That(sent.Single(), Is.EqualTo(new GuildSearchRequest(0x7fffffff, 1)));
        b.SearchPropensity = 4;
        b.Search(false);
        Assert.That(sent.Last(), Is.EqualTo(new GuildSearchRequest(4, 1)));
    }

    [Test]
    public void TheRequestListAndResultsFollowThePackets() {
        var sent = new List<IOutPacket>();
        var b = Book(sent, new List<string>());
        int searchPage = -1, mine = 0;
        b.SearchRefresh += p => searchPage = p;
        b.MyRequestsRefresh += () => mine++;
        b.Apply(new GuildMessage(0x56) { Requests = new[] { new GuildJoinRequest { Id = 4, GuildId = 9 } } });
        Assert.That((b.FindRequestByGuild(9)?.Id, searchPage, mine), Is.EqualTo(((long?)4, 0, 1)));
        b.Apply(new GuildMessage(0x57) { Results = new[] { new GuildSearchEntry { GuildId = 9 } }, SearchCount = 1 });
        Assert.That((b.SearchResults.Count, searchPage), Is.EqualTo((1, 1)));
        b.Apply(new GuildMessage(0x30) { RequestId = 4 });
        Assert.That((b.MyRequests.Count, mine), Is.EqualTo((0, 2)));
        b.Apply(new GuildMessage(0x52) { GuildId = 9, Name = "Gd" });
        Assert.That(sent.Last(), Is.EqualTo(new GuildMyRequestsRequest()));
        b.Apply(new GuildMessage((byte)0) { GuildId = 9 });
        Assert.That(b.InGuild, Is.True);
        b.Search(true);
        Assert.That(sent.Last(), Is.EqualTo(new GuildMyRequestsRequest()));   // no search in a guild
    }

    [Test]
    public void JoinStopsAtTenRequests() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = Book(sent, notices);
        b.Apply(new GuildMessage(0x56) { Requests = Enumerable.Range(0, 10).Select(i => new GuildJoinRequest { Id = i }).ToList() });
        b.Join(9);
        Assert.That((sent.Count, notices.Single()), Is.EqualTo((0, "c3a3")));   // error 0x25: common 0x3a0 + 3
    }

    [Test]
    public void ErrorTextFollowsTheCodeTable() {
        var b = Book(new List<IOutPacket>(), new List<string>());
        Assert.That(b.ErrorText(1, 0), Is.EqualTo("c403"));
        Assert.That(b.ErrorText(3, 0), Is.EqualTo("c404"));
        Assert.That(b.ErrorText(0xa, 0), Is.EqualTo("c40a"));
        Assert.That(b.ErrorText(0x1e, 0), Is.EqualTo("c41d"));
        Assert.That(b.ErrorText(0x29, 0), Is.EqualTo("c424"));
        Assert.That(b.ErrorText(0x2d, 0), Is.EqualTo("s_guild_err_too_fast_to_search_by_name"));
        Assert.That(b.ErrorText(0x99, 0), Is.EqualTo("c420"));
    }

    static GuildMember Member(string name, long charId, int grade, int level, int item, params int[] trophy) => new() {
        Mode = 3, Grade = grade, Name = name, CharId = charId, Level = level, ItemLevel = item,
        Triple = (trophy.ElementAtOrDefault(0), trophy.ElementAtOrDefault(1), trophy.ElementAtOrDefault(2)) };

    [Test]
    public void TheGuildInfoGivesTheMembersGradesAndRights() {
        var b = Book(new List<IOutPacket>(), new List<string>());
        var info = new GuildInfo { Id = 7, Name = "Gd", MaxMembers = 50, MasterCharId = 12 };
        info.Members.Add(Member("bo", 13, 1, 20, 200, 1, 2, 3));
        info.Members.Add(Member("Ma", 12, 0, 40, 100, 4));
        info.Grades.Add(new GuildGrade(0, "Master", 4095));
        info.Grades.Add(new GuildGrade(1, "Member", 8));
        b.Apply(new GuildMessage((byte)0) { GuildId = 7, Info = info });
        Assert.That(b.InGuild, Is.True);
        Assert.That(b.Members.Select(m => m.Name), Is.Not.Empty);
        Assert.That(b.Members.Select(m => m.Name), Is.EqualTo(new[] { "bo", "Ma" }));      // case-insensitive name order: b before m
        Assert.That((b.GuildName, b.MemberCount, b.Capacity, b.TotalTrophy, b.TotalItemLevel, b.TotalCharLevel), Is.EqualTo(("Gd", 2, 50, 10, 300, 60)));
        Assert.That(b.MasterMember.Name, Is.EqualTo("Ma"));
        Assert.That((b.IsMaster("MA"), b.IsMaster("bo"), b.IsMaster("zz")), Is.EqualTo((true, false, false)));
        Assert.That((b.HasMemberPermission("bo", 8), b.HasMemberPermission("bo", 0x40), b.HasMemberPermission("Ma", 0x40), b.HasMemberPermission("zz", 8)),
            Is.EqualTo((true, false, true, false)));
        Assert.That(b.GetGrade(1).Name, Is.EqualTo("Member"));
        Assert.That(b.GetGrade(2), Is.Null);
    }

    /// <summary>A book in a guild: the master Ma (grade 0, all rights) and the member bo (grade 1, right 8).</summary>
    static GuildBook InGuild(List<IOutPacket> sent, List<string> lines) {
        var b = Book(sent, new List<string>());
        b.ChatLine += (t, _) => lines.Add(t);
        b.PlayerName = () => "Ma";
        var info = new GuildInfo { Id = 7, Name = "Gd", MaxMembers = 50, MasterCharId = 12 };
        info.Members.Add(Member("bo", 13, 1, 20, 200, 1, 2, 3));
        info.Members.Add(Member("Ma", 12, 0, 40, 100, 4));
        info.Grades.Add(new GuildGrade(0, "Master", 4095));
        info.Grades.Add(new GuildGrade(1, "Member", 8));
        b.Apply(new GuildMessage((byte)0) { GuildId = 7, Info = info });
        return b;
    }

    [Test]
    public void TheRequestsForTheGuildFollowThePacketsAndAskOnlyWithTheRight() {
        var sent = new List<IOutPacket>();
        var b = InGuild(sent, new List<string>());
        int refresh = 0;
        b.RequestsForGuildRefresh += () => refresh++;
        var request = new GuildJoinRequestForGuild { Id = 4, CharId = 13, Name = "bo", Job = 200, Level = 20 };
        b.Apply(new GuildMessage(0x55) { GuildRequests = new[] { request } });
        Assert.That((b.RequestsForGuild.Single().Name, refresh), Is.EqualTo(("bo", 1)));
        b.Apply(new GuildMessage(0x2d) { NewRequest = new GuildJoinRequestForGuild { Id = 5, CharId = 13, Name = "bo" } });
        Assert.That((b.RequestsForGuild.Count, refresh), Is.EqualTo((2, 2)));
        b.Apply(new GuildMessage(0x2e) { RequestId = 4 });
        b.Apply(new GuildMessage(0x2e) { RequestId = 4 });      // a second withdraw of the same id finds nothing
        Assert.That((b.RequestsForGuild.Count, refresh), Is.EqualTo((1, 4)));
        b.ListRequests();                                       // SendGuildMode85 asks whenever this is a guild
        Assert.That(sent.Last(), Is.EqualTo(new GuildListRequestsRequest()));
        // FUN_1413bd5c0 asks only from a member with the invite right (2): bo (grade 1, rights 8) does not have it
        Assert.That((b.HasMemberPermissionOf(12, 2), b.HasMemberPermissionOf(13, 2)), Is.EqualTo((true, false)));
    }

    [Test]
    public void TheAnswersAndTheMemberPacketsDrawTheLinesAndTheLists() {
        var sent = new List<IOutPacket>(); var lines = new List<string>();
        var b = InGuild(sent, lines);
        b.Apply(new GuildMessage(0x54) { GuildId = 13, Applicant = "bo", Accepted = true });
        b.Apply(new GuildMessage(0x54) { GuildId = 0, Applicant = "bo", Accepted = false });   // id 0: no line
        Assert.That(lines, Is.EqualTo(new[] { "c3d0" }));
        b.Apply(new GuildMessage(0x2f) { Name = "Ma", Applicant = "bo", Accepted = true, RequestId = 4 });
        Assert.That(lines.Last(), Is.EqualTo("c3bd"));
        string refreshed = null;
        b.MemberRefresh += n => refreshed = n;
        b.Apply(new GuildMessage(0x12) { Name = "Ma", Applicant = "bo", Member = Member("bo", 13, 1, 21, 300, 1, 2, 3), Notify = true });
        Assert.That((lines.Last(), refreshed, b.MemberCount, b.Members.Single(m => m.Name == "bo").Level), Is.EqualTo(("c3a8", "bo", 2, 21)));
        b.Apply(new GuildMessage(0x12) { Name = "Ma", Applicant = "bo", Member = Member("bo", 13, 1, 22, 300), Notify = true });
        Assert.That(b.MemberCount, Is.EqualTo(2));            // bo replaced, not added twice
        string removed = null;
        b.MemberRemoved += n => removed = n;
        b.Apply(new GuildMessage(0x13) { Name = "bo" });
        Assert.That((lines.Last(), removed, b.MemberCount), Is.EqualTo(("c3a6", "bo", 1)));
        int gone = 0;
        b.MemberRemoved += _ => gone++;
        b.Apply(new GuildMessage(0x13) { Name = "zz" });      // no such member: no line, the refresh still runs
        Assert.That((lines.Count, gone, b.MemberCount), Is.EqualTo((5, 1, 1)));   // OnPacketMemberLeft :1962-1972
    }

    [Test]
    public void LeaveIsOnlyForAMemberWhoIsNotTheMaster() {
        var sent = new List<IOutPacket>(); var notices = new List<string>(); var boxes = new List<string>(); var lines = new List<string>();
        var b = InGuild(sent, lines);
        b.Notice += (t, _, _) => notices.Add(t);
        b.Box += boxes.Add;
        int closed = 0;
        b.DialogClose += () => closed++;
        b.Leave();
        Assert.That((sent.Count, notices.Single(), closed), Is.EqualTo((0, "c396", 0)));     // the master cannot leave
        b.PlayerName = () => "bo";
        b.Leave();
        Assert.That(sent.Last(), Is.EqualTo(new GuildLeaveRequest()));
        b.Apply(new GuildMessage(7));
        Assert.That((b.InGuild, lines.Last(), closed, boxes.Count), Is.EqualTo((false, "c395", 1, 0)));   // no box of its own
    }

    [Test]
    public void BreakIsOnlyForTheMasterOfAOneMemberGuild() {
        var sent = new List<IOutPacket>(); var notices = new List<string>(); var boxes = new List<string>(); var lines = new List<string>();
        var b = InGuild(sent, lines);
        b.Notice += (t, _, _) => notices.Add(t);
        b.Box += boxes.Add;
        int closed = 0;
        b.DialogClose += () => closed++;
        Assert.That(b.Break(), Is.False);                     // the member count comes first, master or not
        // a guild with members: the box of common 0x40e (FUN_140d73bc0 is ShowMessageBox(GetStringCommon(id)))
        Assert.That((sent.Count, boxes.Single(), notices.Count), Is.EqualTo((0, "c40e", 0)));
        b.Apply(new GuildMessage(0x13) { Name = "bo" });      // bo left: the master is alone
        b.PlayerName = () => "zz";
        Assert.That(b.Break(), Is.False);                     // not the master: nothing sent, no second box
        Assert.That((sent.Count, boxes.Count), Is.EqualTo((0, 1)));
        b.PlayerName = () => "Ma";
        Assert.That(b.Break(), Is.True);
        Assert.That(sent.Last(), Is.EqualTo(new GuildBreakRequest()));
        b.Apply(new GuildMessage(2) { Result = 0 });
        Assert.That((b.InGuild, boxes.Last(), closed), Is.EqualTo((false, "c38a", 1)));
    }

    [Test]
    public void TheDisbandPacketClearsTheGuildAndBoxesTheLeaveJustClosesIt() {
        var sent = new List<IOutPacket>(); var boxes = new List<string>(); var lines = new List<string>();
        var b = InGuild(sent, lines);
        b.Box += boxes.Add;
        int closed = 0;
        b.DialogClose += () => closed++;
        b.Apply(new GuildMessage(2) { Result = 0 });
        Assert.That((b.InGuild, boxes.Single(), closed), Is.EqualTo((false, "c38a", 1)));
        b.Apply(new GuildMessage(7));
        Assert.That(b.InGuild, Is.False);                     // FUN_1413be350 is only the answer to a send
        var failed = InGuild(new List<IOutPacket>(), lines);
        var errorNotices = new List<string>();
        failed.Notice += (t, _, _) => errorNotices.Add(t);
        failed.Apply(new GuildMessage(2) { Result = 1 });
        Assert.That((failed.InGuild, errorNotices.Single()), Is.EqualTo((true, "c403")));   // ShowGuildError(2, 1, 0)
    }

    [Test]
    public void TheInviteChecksTheNameTheRightAndTheRoom() {
        // FUN_1413b71c0: the own name is common 0x38d, a banned one 0x38e, no invite right (2) 0x415, a full guild the box 0x40f
        var sent = new List<IOutPacket>(); var notices = new List<string>(); var boxes = new List<string>(); var sounds = new List<string>();
        var b = InGuild(sent, new List<string>());
        b.Notice += (t, _, _) => notices.Add(t);
        b.Box += boxes.Add;
        b.Sound += sounds.Add;
        Assert.That(b.Invite("ma"), Is.False);
        b.BannedName = n => n == "Bad";
        Assert.That(b.Invite("Bad"), Is.False);
        b.PlayerName = () => "bo";                            // grade 1: no invite right
        Assert.That(b.Invite("Cy"), Is.False);
        Assert.That(notices, Is.EqualTo(new[] { "c38d", "c38e", "c415" }));
        b.PlayerName = () => "Ma";
        b.Info.MaxMembers = 2;
        Assert.That(b.Invite("Cy"), Is.False);
        Assert.That((boxes.Single(), sent.Count), Is.EqualTo(("c40f", 0)));
        b.Info.MaxMembers = 50;
        Assert.That(b.Invite("Cy"), Is.True);
        Assert.That((sent.Single(), sounds.Single()), Is.EqualTo(((IOutPacket)new GuildInviteRequest("Cy"), "PC_InviteSuccess")));
    }

    [Test]
    public void TheExpelGradeAndMasterSendsNeedTheirRights() {
        var sent = new List<IOutPacket>();
        var b = InGuild(sent, new List<string>());
        b.PlayerName = () => "bo";                            // grade 1 holds only the notice right (8)
        b.Expel("Ma"); b.SetMemberGrade("Ma", 1); b.ChangeMaster("Ma");
        Assert.That(sent, Is.Empty);
        b.PlayerName = () => "Ma";
        b.Expel("ma");                                        // the own name
        b.SetMemberGrade("bo", 0);                            // grade 0 is the master's
        b.SetMemberGrade("bo", 2);                            // past GetMaxGradeIndex (2 grades: 1)
        b.SetMemberGrade("b", 1);                             // a name shorter than 2
        b.ChangeMaster("MA");
        Assert.That(sent, Is.Empty);
        b.Expel("bo"); b.SetMemberGrade("bo", 1); b.ChangeMaster("bo");
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GuildExpelRequest("bo"), new GuildMemberGradeRequest("bo", 1), new GuildChangeMasterRequest("bo") }));
    }

    [Test]
    public void TheNoticeAndMemoCheckLengthRightAndContent() {
        // FUN_1413b7c70 kind 1: at most 300 with the notice right (8) and no banned word; SendGuildMode13: at most 0x14
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = InGuild(sent, new List<string>());
        b.Notice += (t, _, _) => notices.Add(t);
        b.PlayerName = () => "bo";
        Assert.That(b.SendText(1, new string('a', 301)), Is.False);
        b.BannedWord = t => t == "no";
        Assert.That(b.SendText(1, "no"), Is.False);
        Assert.That(b.SendText(0, "hi"), Is.False);           // kind 0 is the master's
        b.CheckContent = t => t == "rude" ? "refused" : null;
        Assert.That(b.SendText(1, "rude"), Is.False);
        Assert.That(b.SetMemo(new string('a', 0x15)), Is.False);
        Assert.That(b.SetMemo("rude"), Is.False);
        Assert.That((sent.Count, notices), Is.EqualTo((0, new[] { "refused", "refused" })));
        Assert.That(b.SendText(1, new string('a', 300)), Is.True);
        Assert.That(b.SetMemo(new string('a', 0x14)), Is.True);
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GuildTextRequest(1, new string('a', 300)), new GuildMemoRequest(new string('a', 0x14)) }));
    }

    [Test]
    public void ThePropensityFollowsTypeX44AndSkipsTheSameValue() {
        var sent = new List<IOutPacket>();
        var b = InGuild(sent, new List<string>());
        b.Apply(new GuildMessage(0x44) { Focus = 1, Value = 0x204 });
        Assert.That((b.PropensityFocus, b.Propensity), Is.EqualTo((1, 0x204)));
        b.SetPropensity(1, 0x204);                            // the one it has: no send
        b.PlayerName = () => "bo";
        b.SetPropensity(0, 0x204);                            // no invite right (2)
        Assert.That(sent, Is.Empty);
        b.PlayerName = () => "Ma";
        b.SetPropensity(0, 0x204);
        Assert.That(sent.Single(), Is.EqualTo(new GuildPropensityRequest(0, 0x204)));
    }

    [Test]
    public void TheInviteReceivedShowsItsBoxOrIsRefusedAndItsResultClearsIt() {
        var sent = new List<IOutPacket>(); var lines = new List<string>(); var boxes = new List<string>(); var sounds = new List<string>();
        var b = Book(sent, new List<string>());
        b.PlayerName = () => "Me";
        b.ChatLine += (t, _) => lines.Add(t);
        b.Box += boxes.Add;
        b.Sound += sounds.Add;
        var shown = new List<GuildInviteInfo>();
        b.InviteBox += shown.Add;
        int closed = 0;
        b.DialogClose += () => closed++;
        var other = new GuildInviteInfo { GuildId = 9, GuildName = "Gd", Inviter = "Ma", Invitee = "You" };
        b.Apply(new GuildMessage(4) { Invite = other });          // another name: dropped
        Assert.That((b.HasInvite, shown.Count), Is.EqualTo((false, 0)));
        b.AnswerInvite(true);                                     // no invite: no send
        Assert.That(sent, Is.Empty);
        var mine = new GuildInviteInfo { GuildId = 9, GuildName = "Gd", Inviter = "Ma", Invitee = "ME" };
        b.Apply(new GuildMessage(4) { Invite = mine });           // IsEqualNoCase
        Assert.That((b.HasInvite, shown.Single(), sounds.Single()), Is.EqualTo((true, mine, "System_GuildInvitation")));
        b.AnswerInvite(true);
        Assert.That(sent.Single(), Is.EqualTo(new GuildInviteAnswerRequest(mine, true)));
        Assert.That(b.HasInvite, Is.True, "kept until the result");
        b.Apply(new GuildMessage(5) { Invite = mine, Result = 1 });
        Assert.That((b.HasInvite, lines.Last(), closed), Is.EqualTo((false, "c38f", 1)));
        b.Apply(new GuildMessage(5) { Invite = mine, Result = 0 });
        Assert.That(lines.Last(), Is.EqualTo("c390"));

        sent.Clear();
        b.RefuseInvite = () => true;                              // the refuse option answers no at once
        b.Apply(new GuildMessage(4) { Invite = mine });
        Assert.That((sent.Single(), shown.Count), Is.EqualTo((new GuildInviteAnswerRequest(mine, false), 1)));
        b.RefuseInvite = () => false;
        b.PvpPoint = () => true;
        b.Apply(new GuildMessage(4) { Invite = mine });
        Assert.That((sent.Count, shown.Count), Is.EqualTo((2, 1)));

        b.Apply(new GuildMessage(3) { Name = "Gd" });
        Assert.That(boxes.Single(), Is.EqualTo("c38b"));
        b.Apply(new GuildMessage(6) { Name = "Me", Accepted = false, Reason = 3 });
        Assert.That(lines.Last(), Is.EqualTo("c394"));
        int n = lines.Count;
        b.Apply(new GuildMessage(6) { Name = "Me", Accepted = true, Reason = 3 });
        Assert.That(lines.Count, Is.EqualTo(n), "an accept has no line");
    }

    const long Now = 1_795_262_400;      // a noon (UTC); a minute before is the same day, a day before is not

    [Test]
    public void AttendChecksTheJoinAndAttendDaysAndTheCompleteShowsTheBoxAndLine() {
        var sent = new List<IOutPacket>(); var lines = new List<string>(); var notices = new List<string>(); var boxes = new List<string>();
        var b = InGuild(sent, lines);
        b.Notice += (t, _, _) => notices.Add(t);
        b.Box += boxes.Add;
        b.ServerTime = () => Now;
        var me = b.FindMemberByName("Ma");
        me.Fc8 = Now - 60;                              // joined today
        Assert.That(b.Attend(), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_can_next_day"));
        me.Fc8 = Now - 3 * 86400;
        me.Fd8 = Now - 60;                              // attended today
        Assert.That((b.CanAttend, b.Attend()), Is.EqualTo((false, false)));
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_attend_already"));
        me.Fd8 = Now - 86400;
        Assert.That((b.CanAttend, b.Attend()), Is.EqualTo((true, true)));
        Assert.That(sent.Single(), Is.InstanceOf<GuildAttendRequest>());
        b.Apply(new GuildMessage(0xf));
        Assert.That((boxes.Single(), lines.Last()), Is.EqualTo(("s_guild_attend_complete", "s_guild_attend_complete")));
        Assert.That(b.CanAttend, Is.True, "the complete does not set the attend time (type 0x24 does)");
    }

    [Test]
    public void DonateChecksTheDailyMaxTheFundMaxAndTheMeso() {
        var sent = new List<IOutPacket>(); var lines = new List<string>(); var notices = new List<string>();
        var b = InGuild(sent, lines);
        b.Notice += (t, _, _) => notices.Add(t);
        b.ServerTime = () => Now;
        b.LevelByExp = _ => 1;
        b.Property = level => level == 1 ? new GuildProperty(100_000, 3) : null;
        b.Mesos = () => 50_000;
        var me = b.FindMemberByName("Ma");
        me.Fc8 = Now - 3 * 86400;
        me.Ff0 = 2; me.Ff8 = Now - 60;                  // two donations today
        Assert.That(b.Donate(0), Is.False);
        Assert.That(b.Donate(2), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_donateMax"));
        Assert.That(b.Donate(1), Is.True);
        Assert.That(((GuildDonateRequest)sent.Last()).Amount, Is.EqualTo(1));
        me.Ff8 = Now - 86400;                           // yesterday's count is 0 today
        Assert.That(b.DonationToday(me), Is.EqualTo(0));
        Assert.That(b.Donate(3), Is.True);
        b.Info.Fund = 80_000;                           // 80000 + 30000 > 100000
        Assert.That(b.Donate(3), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_donate_guildFundMax"));
        b.Info.Fund = 86_000;
        Assert.That(b.Donate(1), Is.True);              // 96000
        b.FundEventRate = () => 5000;                   // +50%: 86000 + 15000 > 100000
        Assert.That(b.Donate(1), Is.False);
        b.FundEventRate = () => 0;
        b.Info.Fund = 0;
        b.Mesos = () => 9_999;
        Assert.That(b.Donate(1), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_no_money"));
        Assert.That(sent.Count, Is.EqualTo(3));

        b.Apply(new GuildMessage(0x71) { DonationCount = 3, DonationTime = Now - 5 });
        Assert.That((me.Ff0, me.Ff8, b.DonationToday(me), lines.Last()), Is.EqualTo((3, Now - 5, 3, "s_guild_donate_complete")));
        b.Mesos = () => 50_000;
        Assert.That(b.Donate(1), Is.False, "three today is the max");
    }

    [Test]
    public void AnInviteReceivedInAGuildIsIgnored() {
        var sent = new List<IOutPacket>();
        var b = InGuild(sent, new List<string>());
        var shown = 0;
        b.InviteBox += _ => shown++;
        b.Apply(new GuildMessage(4) { Invite = new GuildInviteInfo { Invitee = "Ma" } });
        Assert.That((b.ReceivedInvite, shown, sent.Count), Is.EqualTo(((GuildInviteInfo)null, 0, 0)));
    }

    const string SkillXml = "<ms2>" +
        "<guildBuff id=\"1\" level=\"1\" requireLevel=\"2\" upgradeCost=\"0\" cost=\"100\" duration=\"1\" cooltime=\"2\"/>" +
        "<guildBuff id=\"1\" level=\"2\" requireLevel=\"5\" upgradeCost=\"300\" cost=\"100\" duration=\"1\" cooltime=\"2\"/>" +
        "<guildBuff id=\"2000\" level=\"1\" requireLevel=\"1\" upgradeCost=\"50\" cost=\"80\" duration=\"0\" cooltime=\"0\"/>" +
        "<guildBuff id=\"2000\" level=\"2\" requireLevel=\"1\" upgradeCost=\"90\" cost=\"80\" duration=\"0\" cooltime=\"0\"/>" +
        "<guildBuff id=\"20000\" level=\"1\" requireLevel=\"2\" upgradeCost=\"0\" cost=\"1000\" duration=\"0\" cooltime=\"0\"/>" +
        "</ms2>";

    /// <summary>A guild with the skill table, the guild level (exp 0 gives level <paramref name="guildLevel"/>), and fund / event fund.</summary>
    static GuildBook SkillBook(List<IOutPacket> sent, List<string> notices, int guildLevel, int fund, int eventFund, long now = 1000) {
        var b = InGuild(sent, new List<string>());
        b.Notice += (t, _, _) => notices.Add(t);
        b.SkillTable = new Ms2.Formats.World.GuildSkillTable(System.Xml.Linq.XDocument.Parse(SkillXml));
        b.LevelByExp = _ => guildLevel;
        b.ServerTime = () => now;
        b.Info.Fund = fund; b.Info.EventFund = eventFund;
        b.Info.Skills.Add(new GuildSkillState(1, 1, 0));
        b.Info.Skills.Add(new GuildSkillState(2000, 1, 0));
        return b;
    }

    [Test]
    public void SkillLevelAndCostsFollowTheRecordAndTheTable() {
        var b = SkillBook(new List<IOutPacket>(), new List<string>(), 3, 0, 0);
        Assert.That((b.SkillLevel(1), b.SkillLevel(9)), Is.EqualTo((1, 0)));
        Assert.That((b.SkillCost(1, 1), b.SkillCost(1, 9)), Is.EqualTo((100, 0x7fffffff)));
        b.GuildSkillSaleRate = () => 2500;
        Assert.That((b.SkillCostWithEventDiscount(1, 1), b.SkillCostWithEventDiscount(2000, 1), b.SkillCostWithEventDiscount(1, 9)), Is.EqualTo((75, 80, 0)));
        b.GuildPersonalSkillSaleRate = () => 1000;
        Assert.That((b.PersonalSkillDiscountedCost(20000, 1), b.PersonalSkillDiscountedCost(1, 1), b.PersonalSkillDiscountedCost(20000, 5)), Is.EqualTo((900, 0x7fffffff, 0x7fffffff)));
    }

    [Test]
    public void UpgradeChecksInTheClientsOrderAndSendsOnlyWhenAllPass() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = SkillBook(sent, notices, 3, 500, 0);
        Assert.That(b.UpgradeSkill(1), Is.False);                       // next row needs guild level 5
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_need_guild_level"));
        b.LevelByExp = _ => 5; b.Info.Fund = 299;
        Assert.That(b.UpgradeSkill(1), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_not_enough_guild_fund"));
        b.Info.Fund = 300;
        Assert.That((b.UpgradeSkill(1), sent.Single()), Is.EqualTo((true, (IOutPacket)new GuildSkillUpgradeRequest(1))));
        b.Info.Skills[0] = new GuildSkillState(1, 2, 0);
        Assert.That(b.UpgradeSkill(1), Is.False);                       // no row above level 2
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_max_level"));
        Assert.That(sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void UpgradeOfAnEventSkillReadsTheEventFundAndNeedsTheMaster() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = SkillBook(sent, notices, 3, 9999, 89);
        Assert.That(b.UpgradeSkill(2000), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_not_enough_guild_event_fund"));
        b.PlayerName = () => "bo";
        Assert.That(b.UpgradeSkill(2000), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("c416"));
        b.PlayerName = () => "Ma"; b.Info.EventFund = 90;
        Assert.That(b.UpgradeSkill(2000), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildSkillUpgradeRequest(2000)));
    }

    [Test]
    public void UseChecksLevelFundAndTheUsableStateBeforeTheSend() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = SkillBook(sent, notices, 1, 99, 0);
        Assert.That(b.UseSkill(1), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_not_enough_guild_level"));
        b.LevelByExp = _ => 2;
        Assert.That(b.UseSkill(1), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_not_enough_guild_fund"));
        b.Info.Fund = 100;
        Assert.That(b.UseSkill(1), Is.True);                            // never used: Time 0, past its cooldown
        Assert.That(sent.Single(), Is.EqualTo(new GuildSkillUseRequest(1)));
        b.Info.Skills[0] = new GuildSkillState(1, 1, 999);              // used a second ago: the 1 minute duration still runs
        Assert.That(b.UseSkill(1), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_cannot_use_skill"));
        Assert.That((b.RemainingDuration(b.Info.Skills[0]), b.RemainingCooldown(b.Info.Skills[0])), Is.EqualTo((59, 119)));
        Assert.That(sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void RemainingDurationAndCooldownCountDownFromTheUseTime() {
        var b = SkillBook(new List<IOutPacket>(), new List<string>(), 3, 0, 0, now: 1000);
        var used = new GuildSkillState(1, 1, 900);                       // duration 1 min, cooltime 2 min (the xml's minutes x 60)
        Assert.That((b.RemainingDuration(used), b.RemainingCooldown(used), b.SkillUsable(used)), Is.EqualTo((0, 20, false)));
        b.ServerTime = () => 950;
        Assert.That((b.RemainingDuration(used), b.RemainingCooldown(used)), Is.EqualTo((10, 70)));
        b.ServerTime = () => 1021;
        Assert.That(b.SkillUsable(used), Is.True);
        Assert.That(b.RemainingCooldown(new GuildSkillState(1, 9, 0)), Is.EqualTo(0x7fffffff));
    }

    [Test]
    public void PersonalUseNeedsLevelMesoAndAPlayerAndTheRecallSkillIsRefused() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = SkillBook(sent, notices, 1, 0, 0);
        b.Info.Skills.Add(new GuildSkillState(20000, 1, 0));
        Assert.That(b.UsePersonalSkill(20000), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_not_enough_guild_level"));
        b.LevelByExp = _ => 2; b.Mesos = () => 999;
        Assert.That(b.UsePersonalSkill(20000), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_err_lack_meso"));
        b.Mesos = () => 1000;
        Assert.That(b.UsePersonalSkill(1), Is.False);                    // a guild skill is not a personal one
        Assert.That(b.UsePersonalSkill(20000), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildPersonalSkillUseRequest(20000)));
    }

    /// <summary>guildmanager.cpp:807-812: the skill's effect already on the player refuses with s_guild_err_cannot_use_skill, and the
    /// meso skill's packet does not go out.</summary>
    [Test]
    public void PersonalUseIsRefusedWhileTheEffectIsOnThePlayer() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = SkillBook(sent, notices, 2, 0, 0);
        b.Info.Skills.Add(new GuildSkillState(20000, 1, 0));
        b.Mesos = () => 5000; b.Level = () => 50;
        var asked = new List<(uint, ushort)>();
        bool up = true;
        b.HasOwnEffect = (id, lv) => { asked.Add((id, lv)); return up; };
        Assert.That(b.UsePersonalSkill(20000), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_cannot_use_skill"));
        Assert.That(sent, Is.Empty);
        Assert.That(asked, Has.Count.EqualTo(1));
        up = false;
        Assert.That(b.UsePersonalSkill(20000), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildPersonalSkillUseRequest(20000)));
    }

    [Test]
    public void UseOfAnEventSkillReadsTheEventFundAndNeedsTheMaster() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = SkillBook(sent, notices, 2, 1000, 0);
        Assert.That(b.UseSkill(2000), Is.False);
        Assert.That(notices.Last(), Is.EqualTo("s_guild_err_not_enough_guild_event_fund"), "the event fund, not the guild fund");
        b.Info.EventFund = 1000;
        Assert.That(b.UseSkill(2000), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildSkillUseRequest(2000)));
    }

    [Test]
    public void UseOfASkillFromANonMasterIsRefusedWithTheMasterText() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = SkillBook(sent, notices, 2, 1000, 1000);
        b.PlayerName = () => "Other";
        Assert.That(b.UseSkill(1), Is.False);
        Assert.That(sent, Is.Empty);
        Assert.That(notices, Has.Count.EqualTo(1));
    }

    [Test]
    public void VsGameRegisterAsksForTheRightAndTheStateBeforeTheSend() {
        var sent = new List<IOutPacket>(); var notices = new List<string>(); var boxes = new List<string>();
        var b = InGuild(sent, new List<string>());
        b.Notice += (t, _, _) => notices.Add(t);
        b.Box += boxes.Add;
        // the master holds the right: mode 0x73
        Assert.That(b.SendGuildMode115(), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildVsGameRegisterRequest()));
        sent.Clear();
        // a member without 0x400: notice 0x415, no send
        b.PlayerName = () => "bo";
        Assert.That(b.SendGuildMode115(), Is.False);
        Assert.That((sent, notices), Is.EqualTo((new List<IOutPacket>(), new List<string> { "c415" })));
        // another game exists: the box of the key, ahead of the right check
        b.Info.F350 = 1;
        Assert.That(b.SendGuildMode115(), Is.False);
        Assert.That(boxes, Is.EqualTo(new[] { "s_guild_vsGame_another_game_exist" }));
        Assert.That(notices, Has.Count.EqualTo(1));
        // already registered: nothing at all
        b.Info.F354 = true;
        Assert.That(b.SendGuildMode115(), Is.False);
        Assert.That((sent, boxes, notices.Count), Is.EqualTo((new List<IOutPacket>(), boxes, 1)));
        Assert.That(boxes, Has.Count.EqualTo(1));
    }

    [Test]
    public void VsGameUnregisterNeedsTheRegistrationAndTheRight() {
        var sent = new List<IOutPacket>(); var notices = new List<string>();
        var b = InGuild(sent, new List<string>());
        b.Notice += (t, _, _) => notices.Add(t);
        Assert.That(b.SendGuildMode116(), Is.False);   // not registered
        Assert.That((sent, notices), Is.EqualTo((new List<IOutPacket>(), new List<string>())));
        b.Info.F354 = true;
        b.PlayerName = () => "bo";
        Assert.That(b.SendGuildMode116(), Is.False);
        Assert.That(notices, Is.EqualTo(new[] { "c415" }));
        Assert.That(sent, Is.Empty);
        b.PlayerName = () => "Ma";
        Assert.That(b.SendGuildMode116(), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildVsGameUnregisterRequest()));
    }

    [Test]
    public void TheVsGameModesWriteTheRegistrationAndTheOtherGame() {
        var sent = new List<IOutPacket>(); var lines = new List<string>(); var notices = new List<string>();
        var b = InGuild(sent, lines);
        b.Notice += (t, _, _) => notices.Add(t);
        int refreshed = 0;
        b.VsGameRefresh += () => refreshed++;
        // registered, with the notice: the ticket line, and the register button's send does nothing
        b.Apply(new GuildMessage((byte)0x76) { Accepted = true, Notify = true });
        Assert.That(b.Info.F354, Is.True);
        Assert.That(lines, Is.EqualTo(new[] { "s_guild_notify_vsGame_register_ticket" }));
        Assert.That(b.SendGuildMode115(), Is.False);
        Assert.That(sent, Is.Empty, "register confirmed: no second send");
        // unregistered without the notice: no line
        b.Apply(new GuildMessage((byte)0x76) { Accepted = false, Notify = false });
        Assert.That(lines, Has.Count.EqualTo(1));
        b.Apply(new GuildMessage((byte)0x76) { Accepted = true });
        // the match was canceled: the flag drops, the notice shows, the register sends again
        b.Apply(new GuildMessage((byte)0x75));
        Assert.That(b.Info.F354, Is.False);
        Assert.That(notices, Is.EqualTo(new[] { "s_guild_notify_vsGame_match_canceled" }));
        Assert.That(b.SendGuildMode115(), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(new GuildVsGameRegisterRequest()));
        // the vs game opened: the other game's id, the flag dropped
        b.Info.F354 = true;
        b.Apply(new GuildMessage((byte)0x77) { Value = 9 });
        Assert.That((b.Info.F350, b.Info.F354), Is.EqualTo((9, false)));
        Assert.That(refreshed, Is.EqualTo(4), "0x76 three times and 0x77 once; 0x75 is not routed to the dialog");
    }

    [Test]
    public void AnOpenedEventOverwritesTheCustomEventAndNamesTheUser() {
        var sent = new List<IOutPacket>(); var lines = new List<string>();
        var b = InGuild(sent, lines);
        b.CommonByKey = k => k == "s_guild_notify_open_event_user" ? "{0}|{1}" : k;
        b.StringTableText = (t, id) => $"{t}:{id}";
        b.Events = new Ms2.Formats.World.GuildEventTable(System.Xml.Linq.XDocument.Parse("<ms2><guildEvent id=\"1\" mapCode=\"77\" imagePath=\"a\"/></ms2>"));
        b.Info.CustomEvents.Add((1, 5));
        int rebuilt = 0;
        b.EventsRefresh += () => rebuilt++;
        b.Apply(new GuildMessage((byte)0x38) { Name = "bo", EventId = 2, EventTime = 9 });   // no custom event of the id: nothing
        Assert.That((rebuilt, lines), Is.EqualTo((0, new List<string>())));
        b.Apply(new GuildMessage((byte)0x38) { Name = "bo", EventId = 1, EventTime = 100 });
        Assert.That(b.CustomEvent(1), Is.EqualTo((1, 100)));
        Assert.That(rebuilt, Is.EqualTo(1));
        Assert.That(lines, Is.EqualTo(new[] { "bo|3:77" }));
    }

    const string HouseXml = "<ms2><guildHouse level='2' theme='3' stringKey='9'/></ms2>";
    const string NpcXml = "<ms2><npc type='goods' level='2' stringKey='6'/></ms2>";

    [Test]
    public void TheHouseUpgradeSetsTheLevelAndThemeRaisesTheRefreshesAndSaysTheLine() {
        var b = GuildDialogTests.InGuild();
        b.CommonByKey = k => k + " {0} {1} {2}";
        b.Houses = new Ms2.Formats.World.GuildHouseTable(System.Xml.Linq.XDocument.Parse(HouseXml));
        b.StringTableText = (t, id) => $"{t}:{id}";
        var lines = new List<(string, int)>(); int house = 0, npc = 0;
        b.ChatLine += (t, ty) => lines.Add((t, ty));
        b.HouseRefresh += () => house++; b.NpcRefresh += () => npc++;
        Assert.That((b.HouseLevel, b.HouseTheme), Is.EqualTo((1, 1)));
        b.Apply(new GuildMessage((byte)0x39) { Name = "bo", HouseLevel = 2, HouseTheme = 3 });
        Assert.That((b.HouseLevel, b.HouseTheme, house, npc), Is.EqualTo((2, 3, 1, 1)));
        Assert.That(lines.Count, Is.EqualTo(1));
        Assert.That(lines[0].Item2, Is.EqualTo(0xd));
        Assert.That(lines[0].Item1, Does.Contain("bo").And.Contain("54:9").And.Contain("2"));
        b.Apply(new GuildMessage((byte)0x39) { Name = "bo", HouseLevel = 7, HouseTheme = 7 });
        Assert.That(lines.Count, Is.EqualTo(1), "no row of that level and theme: no line");
        Assert.That(house, Is.EqualTo(2));
    }

    [Test]
    public void TheNpcUpgradeReplacesTheNpcOfThatKindAndSaysTheLine() {
        var b = GuildDialogTests.InGuild();
        b.CommonByKey = k => k + " {0} {1} {2}";
        b.Npcs = new Ms2.Formats.World.GuildNpcTable(System.Xml.Linq.XDocument.Parse(NpcXml));
        b.StringTableText = (t, id) => $"{t}:{id}";
        var lines = new List<string>(); int npc = 0;
        b.ChatLine += (t, _) => lines.Add(t); b.NpcRefresh += () => npc++;
        Assert.That(b.NpcLevel(2), Is.EqualTo(0));
        b.Apply(new GuildMessage((byte)0x3b) { Name = "bo", NpcKind = 2, NpcLevel = 1 });
        b.Apply(new GuildMessage((byte)0x3b) { Name = "bo", NpcKind = 2, NpcLevel = 2 });
        Assert.That(b.NpcLevel(2), Is.EqualTo(2));
        Assert.That(b.Info.Npcs.Count(n => n.Kind == 2), Is.EqualTo(1), "replaced, not added");
        Assert.That(npc, Is.EqualTo(2));
        Assert.That(lines.Count, Is.EqualTo(1), "only level 2 has a row");
        Assert.That(lines[0], Does.Contain("63:6"));
    }

    static GuildItem Item(int id, int rank, int count) => new(id, rank, count);
    record GuildItem(int Id, int Rank, int Count) { public static implicit operator GuildStorageItem(GuildItem i) => new(i.Id, i.Rank, i.Count, false, false, false, false); }

    [Test]
    public void AStorageEntryReplacesItsKeyAndACountOfZeroRemovesIt() {
        var b = GuildDialogTests.InGuild(); int refresh = 0; b.StorageRefresh += () => refresh++;
        b.Apply(new GuildMessage((byte)0x6e) { StorageItem = Item(1, 0, 5) });
        b.Apply(new GuildMessage((byte)0x6e) { StorageItem = Item(1, 1, 2) });
        b.Apply(new GuildMessage((byte)0x6e) { StorageItem = Item(1, 0, 3) });
        Assert.That((b.StorageItemCount(1, 0), b.StorageItemCount(1, 1), b.Info.StorageItems.Count, refresh), Is.EqualTo((3, 2, 2, 3)));
        b.Apply(new GuildMessage((byte)0x6e) { StorageItem = Item(1, 0, 0) });
        Assert.That((b.StorageItemCount(1, 0), b.Info.StorageItems.Count), Is.EqualTo((0, 1)));
    }

    [Test]
    public void TheHistoryIsKeptCappedAndAskedForOnce() {
        var sent = new List<IOutPacket>(); var b = GuildDialogTests.InGuild(); b.Send = sent.Add; int refresh = 0; b.StorageHistoryRefresh += () => refresh++;
        b.SendGuildMode112();
        Assert.That(sent.Single(), Is.EqualTo(new GuildStorageHistoryRequest()));
        var lines = Enumerable.Range(0, 0x40).Select(i => new GuildStorageHistory(new(0, 0, "a"), new(0, 0, "b"), Item(i, 0, 1), i)).ToList();
        b.Apply(new GuildMessage((byte)0x70) { StorageHistory = lines });
        Assert.That((b.StorageHistory.Count, b.StorageHistory[0].Time, b.StorageHistoryReceived, refresh), Is.EqualTo((0x32, 0x40 - 0x32, true, 1)));
        b.SendGuildMode112();
        Assert.That((sent.Count, refresh), Is.EqualTo((1, 2)), "received: no second send, the dialog refreshes");
        b.Apply(new GuildMessage((byte)0x6e) { StorageItem = Item(1, 0, 1) });
        b.SendGuildMode112();
        Assert.That(sent.Count, Is.EqualTo(2), "an entry change asks for the history again");
    }

    [Test]
    public void TheGiftResultSaysTheReceiverOrTheNeededPeriod() {
        var b = GuildDialogTests.InGuild(); b.CommonByKey = k => k + " {0}";
        var lines = new List<(string, int)>(); var notices = new List<string>();
        b.ChatLine += (t, ty) => lines.Add((t, ty)); b.Notice += (t, _, _) => notices.Add(t);
        b.Apply(new GuildMessage((byte)0x6d) { Result = 0, Name = "bo" });
        Assert.That(lines.Single(), Is.EqualTo(("s_guild_storage_gift bo", 0xd)));
        b.Apply(new GuildMessage((byte)0x6d) { Result = 0x36 });
        Assert.That(notices.Single(), Is.EqualTo("s_guild_storage_not_enough_gift_need_period 7"));
        b.Apply(new GuildMessage((byte)0x6d) { Result = 5 });
        Assert.That((lines.Count, notices.Count), Is.EqualTo((1, 1)));
    }

    [Test]
    public void TheStorageGiftChecksRunInTheClientsOrder() {
        var sent = new List<IOutPacket>(); var b = GuildDialogTests.InGuild(); b.Send = sent.Add;
        var notices = new List<string>(); b.Notice += (t, _, _) => notices.Add(t);
        b.Info.Members.Add(new GuildMember { Mode = 3, Grade = 1, Name = "Bo", CharId = 13, Level = 5, Triple = (0, 0, 0) });
        b.Apply(new GuildMessage((byte)0x6e) { StorageItem = Item(1, 0, 5) });
        b.ItemSlotMax = _ => 4; b.ItemHasStringTag0xd7 = id => id == 2;
        Assert.That(b.SendStorageGift(Item(1, 0, 1), ""), Is.False);
        Assert.That(b.SendStorageGift(Item(1, 0, 1), "Zz"), Is.False);
        Assert.That(b.SendStorageGift(Item(1, 0, 0), "Bo"), Is.False);
        Assert.That(b.SendStorageGift(Item(1, 0, 5), "Bo"), Is.False, "over the slot max");
        b.ItemSlotMax = _ => 9;
        Assert.That(b.SendStorageGift(Item(1, 0, 6), "Bo"), Is.False, "more than stored");
        b.Apply(new GuildMessage((byte)0x6e) { StorageItem = Item(2, 0, 1) });
        Assert.That(b.SendStorageGift(Item(2, 0, 1), "Bo"), Is.False, "tagged: only to oneself");
        Assert.That(notices, Is.EqualTo(new[] { "s_guild_err_empty_member", "c40d", "s_guild_err_empty_count", "s_guild_err_storage_over_slot_max_count", "s_guild_err_storage_not_enough_count", "s_guild_err_storage_send_only_to_me" }));
        Assert.That(sent, Is.Empty);
        Assert.That(b.SendStorageGift(Item(1, 0, 5), "Bo"), Is.True);
        Assert.That(b.SendStorageGift(Item(2, 0, 1), "Ma"), Is.True);
        Assert.That(sent.Count, Is.EqualTo(2));
        b.ItemSlotMax = null;
        Assert.That(b.SendStorageGift(Item(1, 0, 1), "Bo"), Is.False, "no item data: nothing");
        Assert.That(sent.Count, Is.EqualTo(2));
        b.PlayerName = () => "Bo";
        Assert.That(b.SendStorageGift(Item(1, 0, 1), "Bo"), Is.False, "not the master");
        Assert.That(notices.Last(), Is.EqualTo("c416"));
    }

    [Test]
    public void ARaidCountSetsTheMembersCountAndTheResetClearsEveryMember() {
        var b = GuildDialogTests.InGuild(); int refresh = 0; b.RaidRefresh += () => refresh++;
        b.Info.Members.Add(new GuildMember { Mode = 3, Grade = 1, Name = "Bo", CharId = 13, Level = 5, Triple = (0, 0, 0) });
        b.Apply(new GuildMessage((byte)0x25) { Name = "Ma", RaidId = 3, Value = 2 });
        b.Apply(new GuildMessage((byte)0x25) { Name = "Ma", RaidId = 3, Value = 4 });
        b.Apply(new GuildMessage((byte)0x25) { Name = "Bo", RaidId = 5, Value = 1 });
        b.Apply(new GuildMessage((byte)0x25) { Name = "Nobody", RaidId = 5, Value = 7 });
        Assert.That((b.FindMemberByName("Ma").Raid[3], b.Me.RaidTotal, b.TotalRaidCount, refresh), Is.EqualTo((4, 4, 5, 0)), "the count is set, not added; the dialog is not told");
        b.Apply(new GuildMessage((byte)0x3e));
        Assert.That((b.Me.RaidTotal, b.TotalRaidCount, refresh), Is.EqualTo((0, 0, 1)));
    }

    [Test]
    public void TheWeeklyResetClearsEveryContributionAndBothResetsApplyTheTicket() {
        var b = GuildDialogTests.InGuild(); var rows = new List<string>(); int vs = 0;
        b.MemberRefresh += rows.Add; b.VsGameRefresh += () => vs++;
        b.Info.Members.Add(new GuildMember { Mode = 3, Grade = 1, Name = "Bo", CharId = 13, Level = 5, Triple = (0, 0, 0), Contribution = 40 });
        b.Me.Contribution = 25;
        b.Apply(new GuildMessage((byte)0x3d) { Ticket = true, Accepted = true, Notify = false });
        Assert.That(b.Info.Members.Select(x => x.Contribution), Is.All.EqualTo(0));
        Assert.That(rows, Is.EquivalentTo(b.Info.Members.Select(x => x.Name)), "one row redraw per member");
        Assert.That((b.Info.F354, vs), Is.EqualTo((true, 1)), "then the ticket's flag and event buttons");
        b.Apply(new GuildMessage((byte)0x3e) { Ticket = true, Accepted = false, Notify = false });
        Assert.That((b.Info.F354, vs), Is.EqualTo((false, 2)));
        // An empty body (the retail GMS2 server's weekly reset): the reset alone, the ticket flag stays.
        b.Info.F354 = true; b.Me.Contribution = 7; rows.Clear();
        b.Apply(new GuildMessage((byte)0x3d));
        Assert.That((b.Me.Contribution, rows.Count > 0, b.Info.F354, vs), Is.EqualTo((0, true, true, 2)));
    }

    [Test]
    public void ARaidCountOutsideAGuildDoesNothingButTheResetStillRefreshes() {
        var b = new GuildBook { Common = id => $"c{id:x}", CommonByKey = k => k, PlayerName = () => "Ma" }; int refresh = 0; b.RaidRefresh += () => refresh++;
        b.Apply(new GuildMessage((byte)0x25) { Name = "Ma", RaidId = 3, Value = 2 });
        b.Apply(new GuildMessage((byte)0x3e));
        Assert.That((b.TotalRaidCount, refresh), Is.EqualTo((0, 1)));
    }

    static SeasonDataTable PvpSeasons() => new(System.Xml.Linq.XDocument.Parse(
        "<ms2><Season seasonID='4' eventStart='2022-1-1-0-0-0' eventEnd='2030-1-1-0-0-0' grade1='0' grade2='100' grade3='200' grade4='300' grade5='400' grade6='500' grade7='600'/></ms2>"), "GuildPvP",
        System.Xml.Linq.XDocument.Parse("<ms2><season type='GuildPvP' id='4' name='S4'/></ms2>").Root.Elements("season"));

    [Test]
    public void TheSeasonIsTheCurrentOneOfTheTableAndAGradeIsTheOneBelowTheFirstPointsAboveTheScore() {
        var b = GuildDialogTests.InGuild(); b.ZoneOffset = () => 0; b.ServerTime = () => SeasonDataTable.Time("2025-1-1-0-0-0");
        Assert.That(b.GetSeason().GetRankForScore(0), Is.EqualTo(7), "no table: every grade starts at 0, so the top grade");
        b.Seasons = PvpSeasons();
        var s = b.GetSeason();
        Assert.That((s.Id, s.Name, s.GetRankPoints(3), s.GetRankPoints(0), s.GetRankPoints(8)), Is.EqualTo((4, "S4", 200, 0, 0)));
        Assert.That(new[] { -1, 0, 99, 100, 599, 600, 9999 }.Select(s.GetRankForScore), Is.EqualTo(new[] { 0, 1, 1, 2, 6, 7, 7 }));
        b.ServerTime = () => SeasonDataTable.Time("2031-1-1-0-0-0");
        Assert.That(b.GetSeason().Id, Is.EqualTo(0), "no current season resets it");
    }

    [Test]
    public void ThePvpStateAndScoreComeFromTheInfoAndTheTypes0x27And0x29SetThemAndAGradeChangeIsALine() {
        var info = GuildDialogTests.InGuild().Info; info.PvpState = 2; info.Counters = new[] { 150, 7, 3, 0, 0, 0, 0, 0 };
        var b = new GuildBook { Common = id => id == 0x3b8 ? "grade {0}" : $"c{id:x}", CommonByKey = k => k, PlayerName = () => "Ma", ZoneOffset = () => 0, Seasons = PvpSeasons(),
            ServerTime = () => SeasonDataTable.Time("2025-1-1-0-0-0"), StringTableText = (t, id) => $"t{t:x}.{id}" };
        var lines = new List<(string, int)>(); int rating = 0;
        b.ChatLine += (t, c) => lines.Add((t, c)); b.ChampionshipRatingRefresh += () => rating++;
        b.Apply(new GuildMessage((byte)0) { GuildId = info.Id, Info = info });
        Assert.That((b.PvpState, b.Score, b.WinCount, b.LoseCount), Is.EqualTo((2, 150, 7, 3)));
        b.Apply(new GuildMessage((byte)0x27) { PvpState = 1 });
        Assert.That((b.PvpState, rating), Is.EqualTo((1, 1)));
        b.Apply(new GuildMessage((byte)0x29) { Score = 160, Wins = 8, Losses = 3 });
        Assert.That((b.Score, b.WinCount, b.LoseCount, rating, lines.Count), Is.EqualTo((160, 8, 3, 2, 0)), "same grade: no line");
        b.Apply(new GuildMessage((byte)0x29) { Score = 250, Wins = 9, Losses = 3 });
        Assert.That((rating, lines.Count, lines[0].Item2), Is.EqualTo((3, 1, 0xd)));
        Assert.That(lines[0].Item1, Does.Contain("t2d.3"), "the line names the new grade from string table 0x2d");
    }

    [Test]
    public void ThePvpHistoryKeepsTheNewestTenAndTheLastIdIsTheNewestsTimeAndTheSeasonsAreReplaced() {
        var sent = new List<IOutPacket>();
        var b = GuildDialogTests.InGuild(); b.Send = sent.Add; int rows = 0, seasons = 0;
        b.PvpHistoryRefresh += () => rows++; b.PvpSeasonHistoryRefresh += () => seasons++;
        var list = Enumerable.Range(1, 12).Select(i => new GuildPvpHistory { Time = i * 10 }).ToList();
        b.Apply(new GuildMessage((byte)0x4a) { PvpHistory = list });
        Assert.That((b.PvpHistoryNewestFirst.Select(h => h.Time).ToArray(), b.LastPvpHistoryId, rows),
            Is.EqualTo((new long[] { 120, 110, 100, 90, 80, 70, 60, 50, 40, 30 }, 120L, 1)));
        b.Apply(new GuildMessage((byte)0x4b) { PvpSeasonHistory = new[] { new GuildPvpSeasonHistory { SeasonName = "a" }, new GuildPvpSeasonHistory { SeasonName = "b" } } });
        b.Apply(new GuildMessage((byte)0x4b) { PvpSeasonHistory = new[] { new GuildPvpSeasonHistory { SeasonName = "c" } } });
        Assert.That((b.PvpSeasonHistoryNewestFirst.Select(h => h.SeasonName).ToArray(), seasons), Is.EqualTo((new[] { "c" }, 2)));
        b.Apply(new GuildMessage((byte)0x4a) { PvpHistory = Array.Empty<GuildPvpHistory>() });
        Assert.That((b.LastPvpHistoryId, rows), Is.EqualTo((120L, 2)), "no entry: the id stays, the dialog is still told");
    }

    [Test]
    public void TheHistoryRequestSendsTheLastIdAndAsksForTheSeasonsOnlyTheFirstTimeAndTheGuildChangeForgetsIt() {
        var sent = new List<IOutPacket>(); int seasons = 0;
        var b = GuildDialogTests.InGuild(); b.Send = sent.Add; b.PvpSeasonHistoryRefresh += () => seasons++;
        b.Apply(new GuildMessage((byte)0x4a) { PvpHistory = new[] { new GuildPvpHistory { Time = 55 } } });
        b.RequestPvpHistoryAndSeason();
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GuildPvpHistoryRequest(55), new GuildPvpSeasonHistoryRequest() }));
        sent.Clear(); b.RequestPvpHistoryAndSeason();
        Assert.That((sent.Single(), seasons), Is.EqualTo((new GuildPvpHistoryRequest(55), 1)), "second time: only the battles, the season rows are drawn again");
        var w = new ByteWriter(); new GuildPvpHistoryRequest(0x0102030405060708).Write(w);
        Assert.That(w.ToArray(), Is.EqualTo(new byte[] { 0x4a, 8, 7, 6, 5, 4, 3, 2, 1 }));
        b.Apply(new GuildMessage((byte)0) { GuildId = b.Info.Id, Info = b.Info });   // a new guild info: the reset forgets the history
        sent.Clear(); b.RequestPvpHistoryAndSeason();
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GuildPvpHistoryRequest(0), new GuildPvpSeasonHistoryRequest() }));
    }

    /// <summary>OnPacketGuildExp (:2723) keeps the exp and tells the open dialog whether the guild level changed; FUN_1413c4b60 /
    /// FUN_1413c4bd0 (:2739, :2748) keep the fund and the event fund and redraw the fund texts. With no guild info nothing changes.</summary>
    [Test]
    public void TheExpAndFundPacketsKeepTheirValueAndRedrawTheDialog() {
        var b = InGuild(new List<IOutPacket>(), new List<string>());
        b.LevelByExp = e => 1 + e / 100;
        var exp = new List<bool>(); int funds = 0;
        b.ExpRefresh += exp.Add;
        b.FundRefresh += () => funds++;
        b.Apply(new GuildMessage((byte) 0x32) { Value = 50 });
        b.Apply(new GuildMessage((byte) 0x32) { Value = 250 });
        Assert.That(b.Info.Exp, Is.EqualTo(250));
        Assert.That(exp, Is.EqualTo(new[] { false, true }), "level 1 to 1, then 1 to 3");
        b.Apply(new GuildMessage((byte) 0x33) { Value = 900 });
        b.Apply(new GuildMessage((byte) 0x34) { Value = 40 });
        Assert.That((b.Info.Fund, b.Info.EventFund, funds), Is.EqualTo((900, 40, 2)));

        var none = Book(new List<IOutPacket>(), new List<string>());
        none.ExpRefresh += exp.Add;
        none.FundRefresh += () => funds++;
        none.Apply(new GuildMessage((byte) 0x32) { Value = 5 });
        none.Apply(new GuildMessage((byte) 0x33) { Value = 5 });
        Assert.That((exp.Count, funds), Is.EqualTo((2, 2)), "no guild info: no event");
    }

    /// <summary>A book in a guild (Ma the master and the player, grade 0; bo grade 1) with every UI call logged by name.</summary>
    sealed class Rig {
        public readonly GuildBook B;
        public readonly List<(string Text, int Kind)> Lines = new();
        public readonly List<string> Calls = new();
        public bool Open;
        public Rig() {
            B = new GuildBook { Common = id => $"c{id:x}({{0}},{{1}})", CommonByKey = k => k + "({0},{1},{2})", Send = Sent.Add, Level = () => 10 };
            B.PlayerName = () => "Ma";
            B.DialogOpen = () => Open;
            B.GroupText = n => $"#{n}";
            B.EffectName = (id, level) => $"e{id}.{level}";
            B.IdText = t => t.Text;
            B.ChatLine += (t, k) => Lines.Add((t, k));
            B.Box += t => Calls.Add("box " + t);
            B.Notice += (t, type, log) => Calls.Add($"notice {t} {type} {log}");
            B.Sound += s => Calls.Add("sound " + s);
            B.DialogClose += () => Calls.Add("close");
            B.GuildInfoRefresh += () => Calls.Add("info");
            B.FundRefresh += () => Calls.Add("fund");
            B.MemberRefresh += n => Calls.Add("row " + n);
            B.MemberRemoved += n => Calls.Add("remove " + n);
            B.RequestsForGuildRefresh += () => Calls.Add("requests");
            B.SkillsRefresh += () => Calls.Add("skills");
            B.PersonalSkillsRefresh += () => Calls.Add("personal");
            B.AttendRefresh += () => Calls.Add("attend");
            B.GameMenuGuildRefresh += () => Calls.Add("menu");
            B.GuildNameRefresh += n => Calls.Add("name " + n);
            B.GradeRefresh += g => Calls.Add("grade " + g);
            B.MarkRefresh += c => Calls.Add("mark " + c);
            B.HeadGuildName += (who, g) => Calls.Add($"head {who} {g}");
            var info = new GuildInfo { Id = 7, Name = "Gd", MaxMembers = 50, MasterCharId = 12, MasterName = "Ma" };
            info.Members.Add(Member("bo", 13, 1, 20, 200, 1, 2, 3));
            info.Members.Add(Member("Ma", 12, 0, 40, 100, 4));
            info.Grades.Add(new GuildGrade(0, "Master", 4095));
            info.Grades.Add(new GuildGrade(1, "Member", 8));
            info.Skills.Add(new GuildSkillState(5, 2, 0));
            info.Skills.Add(new GuildSkillState(10001, 1, 0));
            B.Apply(new GuildMessage((byte) 0) { GuildId = 7, Info = info });
            B.SkillTable = new GuildSkillTable(System.Xml.Linq.XDocument.Parse(
                "<ms2><guildBuff id='5' level='2' additionalEffectId='70' additionalEffectLevel='3'/>"
                + "<guildBuff id='5' level='3' additionalEffectId='70' additionalEffectLevel='4'/>"
                + "<guildBuff id='10001' level='2' additionalEffectId='80' additionalEffectLevel='2'/></ms2>"));
            B.Events = new GuildEventTable(System.Xml.Linq.XDocument.Parse("<ms2><guildEvent id='3' mapCode='2000001'/></ms2>"));
            B.StringTableText = (table, id) => $"t{table}.{id}";
            Calls.Clear();
        }
        public readonly List<IOutPacket> Sent = new();
        public void Apply(byte type, Func<GuildMessage, GuildMessage> fill = null) => B.Apply(fill == null ? new GuildMessage(type) : fill(new GuildMessage(type)));
        public GuildMember M(string name) => B.FindMemberByName(name);
    }

    /// <summary>The modes the client only reads in a guild (FUN_1413be400 0x8, FUN_1413be7e0 0xa, FUN_1413bee70 0x3f,
    /// FUN_1413beff0 0x41) change nothing; FUN_1413be460 (0x9) ends the guild with 0x3aa (no name) or 0x3ab, then
    /// CloseDialog; FUN_1413be670 (0xb) drops the member with no line.</summary>
    [Test]
    public void TheLeaveAndReadOnlyModesFollowTheClient() {
        var r = new Rig();
        foreach (byte t in new byte[] { 0x8, 0xa, 0x3f, 0x41 }) r.Apply(t, m => m with { Name = "bo" });
        Assert.That((r.Lines.Count, r.Calls.Count, r.B.Info.Members.Count), Is.EqualTo((0, 0, 2)));
        r.Apply(0xb, m => m with { Name = "bo" });
        Assert.That((r.M("bo"), r.Lines.Count, r.Calls.Count), Is.EqualTo(((GuildMember) null, 0, 0)));
        r.Apply(0x9, m => m with { Name = "bo" });
        Assert.That((r.B.InGuild, r.Lines.Single(), r.Calls.Single()), Is.EqualTo((false, ("c3ab(bo,{1})", 0xd), "close")));
        var s = new Rig();
        s.Apply(0x9);
        Assert.That(s.Lines.Single(), Is.EqualTo(("c3aa({0},{1})", 0xd)));
    }

    /// <summary>OnPacketMemberRenamed (0xc): a member of the old name is renamed (old row removed, new row drawn), else the
    /// first request for the guild of the old name is.</summary>
    [Test]
    public void ARenameRenamesTheMemberElseTheRequest() {
        var r = new Rig();
        r.Apply(0xc, m => m with { Name = "bo", NewName = "bi" });
        Assert.That((r.M("bi")?.CharId, r.Calls), Is.EqualTo(((long?) 13, new[] { "remove bo", "row bi" })));
        r.Calls.Clear();
        r.Apply(0x55, m => m with { GuildRequests = new[] { new GuildJoinRequestForGuild { Id = 4, Name = "zz" } } });
        r.Calls.Clear();
        r.Apply(0xc, m => m with { Name = "zz", NewName = "zy" });
        Assert.That((r.B.RequestsForGuild.Single().Name, r.Calls.Single()), Is.EqualTo(("zy", "requests")));
    }

    /// <summary>The boxes: 0x40 result 1 (0x397), OnPacketExpandCapacity 0x42 (keeps the capacity, 0x39f), OnPacketGradeChanged
    /// 0x43 (0x398 with the grade's name), FUN_1413c4dd0 0x47 (0x228).</summary>
    [Test]
    public void TheBoxModesShowTheirCommonText() {
        var r = new Rig();
        r.Apply(0x40, m => m with { Result = 0 });
        r.Apply(0x40, m => m with { Result = 1 });
        r.Apply(0x42, m => m with { Value = 60 });
        r.Apply(0x43, m => m with { Grade = 1 });
        r.Apply(0x47);
        Assert.That(r.Calls, Is.EqualTo(new[] { "box c397({0},{1})", "box c39f(60,{1})", "box c398(Member,{1})", "box c228({0},{1})" }));
        Assert.That(r.B.Info.MaxMembers, Is.EqualTo(60));
    }

    /// <summary>The member list: OnPacketMemberExpelled 0x14 (0x3a9 expeller, name) and FUN_1413c0250 0x21 (0x3a6) remove the
    /// member; the dialog's info, row removal and skill page run either way. Online 0x17 / offline 0x18 (0x3ac / 0x3ad unless a
    /// buddy, the login time kept), the grade 0x15 (0x3ae another, 0x3af own), the memo 0x16, the item level 0xe (no row), the map
    /// 0x1f and the data 0x20 (a changed trophy triple redraws the info).</summary>
    [Test]
    public void TheMemberModesChangeTheMemberAndTellTheDialog() {
        var r = new Rig();
        r.Apply(0x18, m => m with { Name = "bo", Time = 99 });
        Assert.That((r.M("bo").Offline, r.M("bo").LastSeen, r.Lines.Last(), r.Calls.Last()), Is.EqualTo((1, 99L, ("c3ad(bo,{1})", 0xd), "row bo")));
        r.Apply(0x17, m => m with { Name = "bo" });
        Assert.That((r.M("bo").Offline, r.Lines.Last()), Is.EqualTo((0, ("c3ac(bo,{1})", 0xd))));
        r.B.IsBuddy = n => n == "bo";
        r.Lines.Clear();
        r.Apply(0x18, m => m with { Name = "bo", Time = 5 });
        Assert.That(r.Lines, Is.Empty, "a buddy gets the buddy list's notice instead");
        r.Apply(0x15, m => m with { Name = "bo", Grade = 0 });
        Assert.That((r.M("bo").Grade, r.Lines.Last()), Is.EqualTo((0, ("c3ae(bo,Master)", 0xd))));
        r.Apply(0x15, m => m with { Name = "Ma", Grade = 1 });
        Assert.That(r.Lines.Last(), Is.EqualTo(("c3af(Member,{1})", 0xd)));
        r.Apply(0x16, m => m with { Name = "bo", Text = "hi" });
        r.Apply(0xe, m => m with { Name = "bo", Value = 777 });
        r.Apply(0x1f, m => m with { Name = "bo", Value = 2000001 });
        Assert.That((r.M("bo").Memo, r.M("bo").ItemLevel, r.M("bo").MapId), Is.EqualTo(("hi", 777, 2000001)));
        r.Calls.Clear();
        var data = Member("bo", 13, 9, 21, 300, 1, 2, 3);
        r.Apply(0x20, m => m with { Name = "bo", Member = data });
        Assert.That((r.M("bo").Level, r.M("bo").Grade, r.Calls), Is.EqualTo((21, 0, new[] { "row bo" })), "the grade is not member data");
        r.Calls.Clear();
        r.Apply(0x20, m => m with { Name = "bo", Member = Member("bo", 13, 9, 21, 300, 5, 5, 5) });
        Assert.That(r.Calls, Is.EqualTo(new[] { "info", "row bo" }));

        r.Calls.Clear(); r.Lines.Clear();
        r.Apply(0x14, m => m with { Name = "bo", By = "Ma" });
        Assert.That((r.M("bo"), r.Lines.Single(), r.Calls), Is.EqualTo(((GuildMember) null, ("c3a9(Ma,bo)", 0xd), new[] { "info", "remove bo", "skills" })));
        r.Lines.Clear();
        r.Apply(0x21, m => m with { Name = "nobody" });
        Assert.That(r.Lines, Is.Empty, "no member: no line, the dialog still redraws");
    }

    /// <summary>OnPacketMemberContribution 0x24 keeps the check-in time (fd8); the player's also redraws the game menu and the
    /// attend button. FUN_1413c23d0 0x26 and FUN_1413c2590 0x35 only with the dialog open; a gain of the player's is the line
    /// s_guild_gain_contribution.</summary>
    [Test]
    public void TheContributionModesFollowTheDialog() {
        var r = new Rig();
        r.Apply(0x24, m => m with { Name = "bo", Contribution = 50 });
        r.Apply(0x24, m => m with { Name = "Ma", Contribution = 60 });
        Assert.That((r.M("bo").Fd8, r.M("Ma").Fd8, r.Calls), Is.EqualTo((50L, 60L, new[] { "menu", "attend" })));
        r.Calls.Clear();
        r.Apply(0x26, m => m with { MemberValues = new[] { ("bo", 1, 2) } });
        r.Apply(0x35, m => m with { Name = "Ma", Weekly = 3, Total = 4, Value = 1 });
        Assert.That((r.M("bo").Fe0, r.M("Ma").Contribution, r.Calls.Count, r.Lines.Count), Is.EqualTo((0, 0, 0, 0)), "dialog closed");
        r.Open = true;
        r.Apply(0x26, m => m with { MemberValues = new[] { ("bo", 1, 2) } });
        r.Apply(0x35, m => m with { Name = "Ma", Weekly = 3, Total = 4, Value = 1 });
        r.Apply(0x35, m => m with { Name = "bo", Weekly = 5, Total = 6, Value = 1 });
        Assert.That((r.M("bo").Fe0, r.M("bo").Fe4, r.M("Ma").Contribution, r.M("Ma").TotalContribution), Is.EqualTo((1, 2, 3, 4)));
        Assert.That(r.Calls, Is.EqualTo(new[] { "row bo", "row Ma", "row bo" }));
        Assert.That(r.Lines.Single(), Is.EqualTo(("s_guild_gain_contribution(1,{1},{2})", 0xd)));
    }

    /// <summary>The guild's own fields: the master 0x19, the tag / notice 0x1a, the name 0x22, the capacity 0x1c, the grade
    /// 0x1d, the propensity 0x1e, the mark 0x1b and the guild trophies 0x6b.</summary>
    [Test]
    public void TheGuildHeaderModesChangeTheGuild() {
        var r = new Rig();
        r.Apply(0x19, m => m with { Name = "Ma", NewName = "bo" });
        Assert.That((r.M("Ma").Grade, r.M("bo").Grade, r.B.Info.MasterCharId, r.B.Info.MasterName), Is.EqualTo((1, 0, 13L, "bo")));
        Assert.That((r.Lines.Last(), r.Calls), Is.EqualTo((("c3b0(bo,{1})", 0xd), new[] { "info", "row bo", "row Ma" })));
        r.Apply(0x19, m => m with { Name = "bo", NewName = "Ma" });
        Assert.That(r.Lines.Last(), Is.EqualTo(("c3b1({0},{1})", 0xd)));
        r.Calls.Clear(); r.Lines.Clear();
        r.Apply(0x1a, m => m with { Kind = 0, Text = "tag" });
        Assert.That((r.B.Info.Tag, r.Calls.Count, r.Lines.Count), Is.EqualTo(("tag", 0, 0)));
        r.Apply(0x1a, m => m with { Kind = 1, Text = "note", Name = "bo" });
        Assert.That((r.B.Info.Notice, r.Calls.Single(), r.Lines.Single()), Is.EqualTo(("note", "info", ("c3b2(bo,{1})", 0xd))));
        r.Calls.Clear(); r.Lines.Clear();
        r.Apply(0x22, m => m with { Name = "Gn" });
        Assert.That((r.B.Info.Name, r.Lines.Single(), r.Calls), Is.EqualTo(("Gn", ("c3b4(Gn,{1})", 0xd), new[] { "name Gn", "head Ma Gn" })));
        r.Calls.Clear(); r.Lines.Clear();
        r.Apply(0x1c, m => m with { Value = 70 });
        Assert.That((r.B.Info.MaxMembers, r.Calls, r.Lines.Single()), Is.EqualTo((70, new[] { "info", "fund" }, ("c3b5(70,{1})", 0xd))));
        r.Calls.Clear(); r.Lines.Clear();
        r.Apply(0x1d, m => m with { Grade = 0, GradeEntry = new GuildGrade(0, "Boss", 4095) });
        Assert.That((r.B.GetGrade(0).Name, r.Calls.Single(), r.Lines.Single()), Is.EqualTo(("Boss", "grade 0", ("c3a7(Ma,{1})", 0xd))));
        r.Apply(0x1d, m => m with { Grade = 4, GradeEntry = new GuildGrade(4, "X", 0) });
        Assert.That((r.B.GetGrade(4), r.Calls.Count), Is.EqualTo(((GuildGrade) null, 1)), "a grade not kept is not added");
        r.Apply(0x1e, m => m with { Focus = 3, Value = 9 });
        Assert.That((r.B.Info.B3, r.B.Info.V14), Is.EqualTo((3, 9)));
        r.Calls.Clear(); r.Lines.Clear();
        r.Apply(0x1b, m => m with { Code = "mk", Changer = new StringIdText(false, 0, 0, Array.Empty<string>(), "bo") });
        Assert.That((r.B.Info.Code, r.Lines.Single(), r.Calls.Single()), Is.EqualTo(("mk", ("c3b3(bo,{1})", 0xd), "mark mk")));
        r.Apply(0x6b, m => m with { Value = 1234 });
        Assert.That(r.B.Info.F358, Is.EqualTo(1234));
    }

    /// <summary>The guild skill notices: FUN_1413bf340 0x5a (s_guild_skill_use_skill with the effect of the guild's level),
    /// FUN_1413bf540 0x5b (kept until the effect comes, only with the dialog open; FUN_1413bce40 then shows the line once), the
    /// used 0x36 and upgraded 0x37 skills (the kept skill replaced; guild or personal page by the row's id).</summary>
    [Test]
    public void TheSkillModesKeepTheSkillAndShowTheirLines() {
        var r = new Rig();
        r.Apply(0x5a, m => m with { Value = 5 });
        Assert.That(r.Lines.Single(), Is.EqualTo(("s_guild_skill_use_skill(e70.3,{1},{2})", 0xd)));
        r.Lines.Clear();
        r.Apply(0x5b, m => m with { Value = 5 });
        Assert.That(r.B.PendingSkillEffects, Is.Empty, "dialog closed");
        r.Open = true;
        r.Apply(0x5b, m => m with { Value = 5 });
        Assert.That(r.B.PendingSkillEffects, Is.EqualTo(new[] { (70, 3) }));
        Assert.That(r.B.OwnEffectAdded(70, 2), Is.False);
        Assert.That(r.B.OwnEffectAdded(70, 3), Is.True);
        Assert.That(r.B.OwnEffectAdded(70, 3), Is.False, "once");
        Assert.That((r.Lines.Single(), r.Calls.Single()), Is.EqualTo((("s_guild_skill_use_skill(e70.3,{1},{2})", 0xd), "personal")));
        r.Lines.Clear(); r.Calls.Clear();
        r.Apply(0x36, m => m with { Name = "bo", Skill = new GuildSkillState(5, 2, 77) });
        Assert.That((r.B.Info.Skills[0].Time, r.Calls.Single(), r.Lines.Single()),
            Is.EqualTo((77L, "skills", ("s_guild_notify_use_skill_user(bo,e70.3,{2})", 0xd))));
        r.Lines.Clear(); r.Calls.Clear();
        r.Apply(0x37, m => m with { Name = "bo", Skill = new GuildSkillState(5, 3, 0) });
        r.Apply(0x37, m => m with { Name = "bo", Skill = new GuildSkillState(10001, 2, 0) });
        r.Apply(0x37, m => m with { Name = "bo", Skill = new GuildSkillState(6, 1, 0) });
        Assert.That((r.B.SkillLevel(5), r.B.SkillLevel(10001), r.B.SkillLevel(6)), Is.EqualTo((3, 2, 0)), "an id not kept is not added");
        Assert.That(r.Calls, Is.EqualTo(new[] { "skills", "personal" }));
        Assert.That(r.Lines, Is.EqualTo(new[] { ("s_guild_notify_upgrade_skill(bo,e70.4,3)", 0xd), ("s_guild_notify_upgrade_skill(bo,e80.2,2)", 0xd) }));
    }

    /// <summary>The gains and events: OnPacketGainExpFund 0x61 (both, the exp or the fund, grouped), OnPacketOpenEvent 0x63 (the
    /// event's map name), OnPacketVsGameReward 0x7b (kind 1 exp, 2 fund, chat kind 3), the offline expel list 0x4c (only the
    /// "expeled" reason), the poster 0x3a (named user only).</summary>
    [Test]
    public void TheGainAndEventModesShowTheirLines() {
        var r = new Rig();
        r.Apply(0x61, m => m with { Value = 80, Value2 = 10000 });
        r.Apply(0x61, m => m with { Value = 80 });
        r.Apply(0x61, m => m with { Value2 = 5 });
        r.Apply(0x61);
        r.Apply(0x63, m => m with { EventId = 3 });
        r.Apply(0x63, m => m with { EventId = 4 });
        r.Apply(0x7b, m => m with { Kind = 1, Value = 9 });
        r.Apply(0x7b, m => m with { Kind = 2, Value = 8 });
        r.Apply(0x7b, m => m with { Kind = 3, Value = 7 });
        r.Apply(0x4c, m => m with { Expelled = new[] { ("bo", "expeled", 1L), ("zz", "left", 1L) } });
        r.Apply(0x3a, m => m with { Name = "" });
        r.Apply(0x3a, m => m with { Name = "bo" });
        Assert.That(r.Lines, Is.EqualTo(new[] {
            ("s_guild_gain_exp_fund(#80,#10000,{2})", 0xd), ("s_guild_gain_exp(#80,{1},{2})", 0xd), ("s_guild_gain_fund(#5,{1},{2})", 0xd),
            ("s_guild_open_event(t3.2000001,{1},{2})", 0xd),
            ("s_guild_notify_vsGame_reward_exp(#9,{1},{2})", 3), ("s_guild_notify_vsGame_reward_fund(#8,{1},{2})", 3),
            ("c3ab(bo,{1})", 0xd), ("s_guild_notify_change_poster(bo,{1},{2})", 0xd) }));
    }

    /// <summary>The guild battle (no in-guild check): FUN_1413c28e0 0x28 (the sounds, then the notice 0x3bf / 0x3c0),
    /// OnPacketBattleResult 0x2a (0x3bb won / 0x3bc lost, an empty name "Unknown"), 0x2b (0x3b9 and the apply sound), 0x2c
    /// (0x3ba); the remain-time notice 0x50; the head names 0x4d / 0x4e.</summary>
    [Test]
    public void TheBattleAndHeadModesFollowTheClient() {
        var r = new Rig();
        r.Apply(0x9);       // out of the guild: these still run
        r.Lines.Clear(); r.Calls.Clear();
        r.Apply(0x28, m => m with { Accepted = true });
        r.Apply(0x28, m => m with { Accepted = false });
        r.Apply(0x2a, m => m with { Accepted = true, Name = "Ga" });
        r.Apply(0x2a, m => m with { Accepted = false });
        r.Apply(0x2b, m => m with { Name = "Ga" });
        r.Apply(0x2c, m => m with { Name = "Ga" });
        r.Apply(0x50);
        r.Apply(0x4d, m => m with { Name = "bo", Text = "Gd" });
        r.Apply(0x4e, m => m with { Name = "bo" });
        Assert.That(r.Calls, Is.EqualTo(new[] {
            "sound guildBattle_MatchingSuccess", "sound system_notice_GuildBattleMatching01", "notice c3bf({0},{1}) 1 True",
            "sound guildBattle_MatchingFail", "notice c3c0({0},{1}) 1 True", "sound guildBattle_Apply", "notice c421({0},{1}) 1 True",
            "head bo Gd", "head bo " }));
        Assert.That(r.Lines, Is.EqualTo(new[] { ("c3bb(Ga,{1})", 0xd), ("c3bc(Unknown,{1})", 0xd), ("c3b9(Ga,{1})", 0xd), ("c3ba(Ga,{1})", 0xd) }));
    }

    /// <summary>Out of a guild the in-guild modes change nothing.</summary>
    [Test]
    public void OutOfAGuildTheInGuildModesDoNothing() {
        var r = new Rig();
        r.Apply(0x9);
        r.Lines.Clear(); r.Calls.Clear();
        foreach (byte t in new byte[] { 0xb, 0xc, 0xe, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21,
                     0x22, 0x24, 0x40, 0x42, 0x43, 0x5a, 0x5b, 0x61, 0x63 })
            r.Apply(t, m => m with { Name = "bo", NewName = "Ma", Value = 1, Value2 = 1, Result = 1, Kind = 1, EventId = 3 });
        Assert.That((r.Lines.Count, r.Calls.Count), Is.EqualTo((0, 0)));
    }

    /// <summary>The game menu's guild count (UIGameMenuDialog::FUN_1419381c0) is drawn again after the request list's own redraw
    /// by OnPacketJoinRequestsForGuild 0x55 (guildmanager.cpp:2909), FUN_1413c6180 0x2f (:3057, before its line),
    /// OnPacketJoinRequestReceived 0x2d (:3079) and OnPacketJoinRequestWithdrawn 0x2e (:3102).</summary>
    [Test]
    public void TheRequestModesRedrawTheGameMenuCount() {
        var r = new Rig();
        r.Apply(0x55, m => m with { GuildRequests = new[] { new GuildJoinRequestForGuild { Id = 4, Name = "zz" } } });
        r.Apply(0x2d, m => m with { NewRequest = new GuildJoinRequestForGuild { Id = 5, Name = "zy" } });
        r.Apply(0x2e, m => m with { RequestId = 5 });
        Assert.That(r.Calls, Is.EqualTo(new[] { "requests", "menu", "requests", "menu", "requests", "menu" }));
        r.Calls.Clear();
        int lineAtMenu = -1;
        r.B.GameMenuGuildRefresh += () => lineAtMenu = r.Lines.Count;
        r.Apply(0x2f, m => m with { Name = "Gd", Applicant = "zz", Accepted = true, RequestId = 4 });
        Assert.That((r.Calls, lineAtMenu, r.Lines.Count), Is.EqualTo((new[] { "requests", "menu" }, 0, 1)), "the count before the line");
        Assert.That(r.B.RequestsForGuild, Is.Empty);
    }

    /// <summary>OnPacketMemberJoined 0x12 with a full record (mode 3) and OnPacketMemberLeft 0x13 end with the guild skill page
    /// FUN_14196e590 (guildmanager.cpp:1945 and the 0x13 handler), the latter also for a name not found; FUN_1413c3110 0x6b
    /// (:2476) only sets f358.</summary>
    [Test]
    public void TheMemberModesRedrawTheSkillPageAndTheTrophyModeNothing() {
        var r = new Rig();
        var full = Member("zz", 14, 1, 30, 100); full.Mode = 3;
        r.Apply(0x12, m => m with { Name = "Ma", Applicant = "zz", Member = full });
        Assert.That(r.Calls, Is.EqualTo(new[] { "info", "row zz", "skills" }));
        r.Calls.Clear();
        r.Apply(0x13, m => m with { Name = "zz" });
        r.Apply(0x13, m => m with { Name = "nobody" });
        Assert.That(r.Calls, Is.EqualTo(new[] { "info", "remove zz", "skills", "info", "remove nobody", "skills" }));
        r.Calls.Clear();
        r.Apply(0x6b, m => m with { Value = 99 });
        Assert.That((r.B.Info.F358, r.Calls.Count), Is.EqualTo((99, 0)));
    }

    /// <summary>FUN_1413b5e80 (guildmanager.cpp:218), each tick: in a guild, once IsNotRecentDayChange(m_nextRefreshTime) the game
    /// menu count and UpdateAttendBackground are drawn again and m_nextRefreshTime is the server time. The guild info sets it
    /// (:1546), so the same day draws nothing; LeaveGame (FUN_1413b5f90) sets it to 0; outside a guild nothing runs.</summary>
    [Test]
    public void TheDayChangeRedrawsTheMenuCountAndTheAttendButtonOnce() {
        var r = new Rig();
        long t = Now;
        r.B.ServerTime = () => t;
        var info = r.B.Info;
        r.Apply(0, m => m with { GuildId = 7, Info = info });
        r.Calls.Clear();
        r.B.Update();
        t += 60; r.B.Update();
        Assert.That(r.Calls, Is.Empty, "the guild info's day");
        t += 86400; r.B.Update(); r.B.Update();
        t += 1; r.B.Update();
        Assert.That(r.Calls, Is.EqualTo(new[] { "menu", "attend" }), "once on the next day");
        r.Calls.Clear();
        t += 86400; r.B.Update();
        Assert.That(r.Calls, Is.EqualTo(new[] { "menu", "attend" }), "and again a day later");
        r.Calls.Clear();
        r.B.LeaveGame();
        t += 86400; r.B.Update();
        Assert.That((r.B.InGuild, r.Calls.Count), Is.EqualTo((false, 0)), "outside a guild");
    }
    /// <summary>The HUD calls Update each frame once the guild is watched: in a guild with a new server second each call (the
    /// day check and its two calendar days every time, the worst case; all within the guild info's day, so no redraw) and
    /// outside a guild, it allocates nothing.</summary>
    [Test]
    public void TheFrameUpdateAllocatesNothingInOrOutsideAGuild() {
        var r = new Rig();
        long t = Now;
        r.B.ServerTime = () => t;
        var info = r.B.Info;
        r.Apply(0, m => m with { GuildId = 7, Info = info });
        r.Calls.Clear();
        const int N = 100_000;
        long Measure() {
            for (int i = 0; i < 1000; i++) { t = Now + 1 + i % 50; r.B.Update(); }
            long a = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < N; i++) { t = Now + 1 + i % 50; r.B.Update(); }
            return GC.GetAllocatedBytesForCurrentThread() - a;
        }
        long inGuild = Measure();
        TestContext.Out.WriteLine($"in a guild: {inGuild} B over {N} calls, {r.Calls.Count} redraws");
        r.Calls.Clear();
        r.B.LeaveGame();
        long outside = Measure();
        TestContext.Out.WriteLine($"outside a guild: {outside} B over {N} calls");
        Assert.That((inGuild, outside, r.Calls.Count), Is.EqualTo((0L, 0L, 0)));
    }
}
