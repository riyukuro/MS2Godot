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
}
