using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CGuildManager's not-in-guild side (GuildBook): the Guild types it applies and its checked sends.</summary>
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
}
