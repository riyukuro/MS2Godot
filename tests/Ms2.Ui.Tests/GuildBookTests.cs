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
}
