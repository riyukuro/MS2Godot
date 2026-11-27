using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The dungeon room window's room panel (UIDungeonRoomInfo, uidungeonroomdialog.cpp:535-1827) on its movie (needs MS2_DATA): the
/// refreshed texts and positions, the limits, the party status, the enter buttons and what each click sends. Compared by ids, counts,
/// colours and positions, never by language text. GMS2's room table has no groups, so a window that cannot open the room ignores there.</summary>
public class DungeonRoomPanelTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static GameTables Tables()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= new PackSet(d);
        src ??= new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        return tables ??= new GameTables(packs, "en");
    }

    sealed class Env
    {
        public readonly List<string> Notices = new(), NoticeTexts = new();
        public readonly List<IOutPacket> Sent = new();
        public readonly List<MessageBoxData> Boxes = new();
        public PartyBook Book = new();
        public DungeonRoomDialogController C;
        public UiModel Model;
        public UiHost Host;
        public Player P;
        public DungeonRoomInfoPanel Panel => C.Panel;
    }

    static Env Make(DungeonRoomData room, ushort level = 60, uint gear = 0, PartyBook book = null, Action<Env> before = null, int portal = 0)
    {
        var t = Tables();
        var e = new Env();
        if (book != null) e.Book = book;
        e.C = new DungeonRoomDialogController(e.Book)
        {
            Notice = e.Notices.Add, NoticeText = e.NoticeTexts.Add, LocalPlayer = () => (level, gear),
            Send = e.Sent.Add, ShowBox = e.Boxes.Add,
        };
        e.C.Records.ServerTime = () => 1_000_000;
        e.C.Records.CharacterId = () => 1;
        e.C.SetPortalId(portal);
        e.C.SetRoom(room);
        e.Model = new UiModel(t);
        before?.Invoke(e);
        var (rt, p) = src.Open(e.C.Movie, r => r.Budget = 20_000_000, e.C.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        e.P = p;
        e.Host = new UiHost(p, e.C, e.Model);
        Assert.That(e.Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        if (e.C.Room == null || e.Panel == null) Assert.Ignore("the room table cannot open this room (no groups)");
        return e;
    }

    static DungeonRoomTable Rooms() => Tables().DungeonRooms;

    static DungeonRoomData Room(Func<DungeonRoomData, bool> pred, string what)
    {
        var r = Rooms().RoomList.FirstOrDefault(pred);
        if (r == null) Assert.Ignore("no room with " + what);
        return r;
    }

    static DungeonRoomData Any() => Room(r => r.GroupType > 0 && r.MaxUserCount > 0, "a group");

    static PartyUser U(long id, short level = 50) => new() { AccountId = id + 100, CharacterId = id, Name = "n" + id, MapId = 2000001, Level = level, Channel = 1, GearScore = 100 };

    static PartyBook PartyOf(long leader, int dungeon = 0, bool matchmaking = false, int bits = 0, params long[] ids)
    {
        var book = new PartyBook();
        if (ids.Length == 0) ids = new long[] { 1, 2, 3 };
        book.Apply(new PartyInfo(false, 9, leader, ids.Select(i => new PartyInfoMember(false, U(i))).ToArray(), matchmaking, dungeon) { SearchBits = bits }, 1, false);
        return book;
    }

    static void AddRecord(Env e, DungeonRoomRecord rec) =>
        e.C.Records.Apply(new RoomDungeonReply(RoomDungeonKind.AddRecord, 6, Array.Empty<DungeonRoomRecord>(), rec, 0, 0, 0,
            Array.Empty<ClosingPhotoEntry>(), 0, 0, Array.Empty<DungeonRankRewardEntry>(), null, 0, 0));

    static DungeonRoomRecord Rec(uint room, byte unionClears = 0, byte subClears = 0, long cooldown = 0) =>
        new(room, 0, unionClears, subClears, 0, 0, 0, 0, 0, -1, cooldown, -1, 0) { Key = room };

    static object Child(Env e, string name, object parent = null) => e.Host.Child(name, parent ?? e.Host.Child("m_dungeonInfo"));
    static object Sub(Env e, string name) => e.Host.Child(name, e.Host.Child("m_contents", e.Host.Child("m_dungeonScrollInfo", e.Host.Child("m_dungeonInfo"))));
    static string Text(Env e, object clip)
    {
        var f = e.Host.Has(clip, "htmlText") ? clip : e.Host.Child("textField", clip);
        return Rt.ToString(e.Host.Get(f, "text"));
    }
    static string Html(Env e, object clip)
    {
        var f = e.Host.Has(clip, "htmlText") ? clip : e.Host.Child("textField", clip);
        return Rt.ToString(e.Host.Get(f, "htmlText"));
    }
    static bool Visible(Env e, object o) => Rt.ToBoolean(e.Host.Get(o, "visible"));
    static double X(Env e, object o) => Rt.ToNumber(e.Host.Get(o, "x"));
    static double Y(Env e, object o) => Rt.ToNumber(e.Host.Get(o, "y"));

    static void Click(Env e, object target)
    {
        var ev = e.Host.RT.Construct(e.Host.RT.GetClass("flash.events.Event"), new object[] { "buttonClick" });
        e.Host.Invoke(target, "dispatchEvent", ev);
    }

    // ---- construction and refresh ----

    [Test]
    public void ThePanelIsBuiltOnTheMovieWithEveryChildItNames()
    {
        var e = Make(Any());
        Assert.That(e.Host.RT.Missing.Keys.Where(k => k.Contains("dungeon room panel")), Is.Empty);
        Assert.That(e.Panel.Room, Is.SameAs(e.C.Room));
        for (int i = 0; i < DungeonRoomInfoPanel.SubCount; i++)
        {
            Assert.That(Sub(e, "m_subTabButton" + i), Is.Not.Null, "tab " + i);
            Assert.That(Sub(e, "m_subPanel" + i), Is.Not.Null, "panel " + i);
        }
    }

    [Test]
    public void TheTitleIsTheRoomsListText()
    {
        var e = Make(Any());
        Assert.That(Text(e, Child(e, "m_title")), Is.EqualTo(e.C.GetRoomListText(e.C.Room, false)));
    }

    [Test]
    public void SetRoomReachesThePanelAndRefreshesItsTitle()
    {
        var a = Any();
        var b = Rooms().RoomList.FirstOrDefault(r => r.GroupType > 0 && r.Id != a.Id && e0Title(r) != e0Title(a));
        if (b == null) Assert.Ignore("one room only");
        var e = Make(a);
        e.C.SetRoom(b);
        e.C.Refresh();
        Assert.That(e.Panel.Room, Is.SameAs(b));
        Assert.That(Text(e, Child(e, "m_title")), Is.EqualTo(e.C.GetRoomListText(b, false)));
    }

    static string e0Title(DungeonRoomData r) => Tables().FieldEnteranceName((int) r.Id) ?? "";

    [Test]
    public void ClosingTheDialogDropsThePanel()
    {
        var e = Make(Any());
        e.C.Close();
        Assert.That(e.C.Panel, Is.Null);
    }

    // ---- limits ----

    static string Colors(string html) => string.Concat(System.Text.RegularExpressions.Regex.Matches(html, "color=['\"]#([0-9a-fA-F]+)['\"]", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value.ToLowerInvariant() + ";"));

    [Test]
    public void ALevelLimitIsRedBelowAndWhiteFromTheLevel()
    {
        var room = Room(r => r.GroupType > 0 && r.LimitPlayerLevel > 1 && r.GearScore == 0 && r.MinUserCount == 0 && r.LimitClearDungeon.Count == 0, "a level limit and no other counted limit");
        var below = Make(room, (ushort) (room.LimitPlayerLevel - 1));
        var at = Make(room, room.LimitPlayerLevel);
        var limit = Sub(below, "m_subPanel1");
        Assert.That(Colors(Html(below, Child(below, "m_limitText", limit))), Does.Contain("ff0000"));
        var met = Colors(Html(at, Child(at, "m_limitText", Sub(at, "m_subPanel1"))));
        Assert.That(met, Does.Contain("ffffff").And.Not.Contain("ff0000"));
    }

    [Test]
    public void ARoomWithNoCountedLimitHasNoRedLine()
    {
        var plain = Room(r => r.GroupType > 0 && r.LimitPlayerLevel == 0 && r.GearScore == 0 && r.MinUserCount == 0 && r.LimitClearDungeon.Count == 0, "no counted limit");
        var e = Make(plain);
        Assert.That(Colors(Html(e, Child(e, "m_limitText", Sub(e, "m_subPanel1")))), Does.Not.Contain("ff0000"));
    }

    [Test]
    public void AClearDungeonLimitIsWhiteOnceTheRecordHasProgress()
    {
        var room = Room(r => r.GroupType > 0 && r.LimitClearDungeon.Count == 1, "one clear dungeon limit");
        var e = Make(room);
        Assert.That(Colors(Html(e, Child(e, "m_limitText", Sub(e, "m_subPanel1")))), Does.Contain("ff0000"));
        AddRecord(e, Rec(room.LimitClearDungeon[0], unionClears: 1) with { TotalClears = 1 });
        e.C.Refresh();
        if (e.C.Records.HasProgress(room.LimitClearDungeon[0]))
            Assert.That(Colors(Html(e, Child(e, "m_limitText", Sub(e, "m_subPanel1")))), Does.Not.Contain("ff0000"));
    }

    [Test]
    public void TheRequireConditionIsBelowTheLimitTextForAnAchievementOrVip()
    {
        var room = Room(r => r.GroupType > 0 && (r.LimitAchieveId != 0 || r.LimitVip), "an achievement or VIP limit");
        var e = Make(room);
        var req = Child(e, "m_requireCondition", Sub(e, "m_subPanel1"));
        Assert.That(Visible(e, req), Is.True);
        Assert.That(Y(e, req), Is.GreaterThan(0));
    }

    [Test]
    public void ARoomWithNoAchievementOrVipHidesTheRequireCondition()
    {
        var room = Room(r => r.GroupType > 0 && r.LimitAchieveId == 0 && !r.LimitVip, "no achievement or VIP limit");
        var e = Make(room);
        Assert.That(Visible(e, Child(e, "m_requireCondition", Sub(e, "m_subPanel1"))), Is.False);
    }

    // ---- party status ----

    [Test]
    public void TheSoloStatusShowsOnlyTheFirstMemberWithItsStateColour()
    {
        var e = Make(Any(), before: x => x.C.Hooks.ClearState = _ => 1);
        var members = Child(e, "m_partyMembers");
        Assert.That(Visible(e, Child(e, "m_partyMember0", members)), Is.True);
        for (int i = 1; i < 10; i++) Assert.That(Visible(e, Child(e, "m_partyMember" + i, members)), Is.False, "member " + i);
        Assert.That(Rt.ToInt32(e.Host.Get(Child(e, "m_partyMember0", members), "currentFrame")), Is.GreaterThan(0));
    }

    [Test]
    public void WithoutAClearStateTheMemberIconsStayHiddenAndTheBannerEmpty()
    {
        var e = Make(Any());
        Assert.That(e.Panel.Banner, Is.EqualTo(""));
        Assert.That(Visible(e, Child(e, "m_partyMember0", Child(e, "m_partyMembers"))), Is.False);
    }

    [Test]
    public void ThePartyStatusShowsAMemberIconPerMemberAndABannerLinePerMember()
    {
        var book = PartyOf(1);
        var e = Make(Any(), book: book, before: x => x.C.Hooks.MemberClearState = (_, _) => 0);
        var members = Child(e, "m_partyMembers");
        for (int i = 0; i < 10; i++) Assert.That(Visible(e, Child(e, "m_partyMember" + i, members)), Is.EqualTo(i < 3), "member " + i);
        Assert.That(e.Panel.Banner, Does.Contain("n1").And.Contain("n2").And.Contain("n3"));
    }

    [Test]
    public void TheCountColourNamesFollowTheState()
    {
        Assert.That(DungeonRoomInfoPanel.CountColor(0), Is.EqualTo("green"));
        Assert.That(DungeonRoomInfoPanel.CountColor(1), Is.EqualTo("blue"));
        Assert.That(DungeonRoomInfoPanel.CountColor(2), Is.EqualTo("red"));
        Assert.That(DungeonRoomInfoPanel.CountColor(11), Is.EqualTo("red"));
    }

    [Test]
    public void ThePartyMembersTooltipIsTheBannerAndNothingWhenItIsEmpty()
    {
        var e = Make(Any(), before: x => x.C.Hooks.ClearState = _ => 11);
        var t = e.C.GetTooltip(new object[] { "m_partyMembers" });
        Assert.That(t, Is.Not.Null);
        Assert.That(t["text"], Is.EqualTo(e.Panel.Banner));
        var none = Make(Any());
        Assert.That(none.C.GetTooltip(new object[] { "m_partyMembers" }), Is.Null);
    }

    // ---- enter buttons ----

    [Test]
    public void ASoloEnterSendsTheEntryOfTheRoomWithThePortal()
    {
        var room = Room(r => r.GroupType > 0 && r.MinUserCount == 0 && !r.IsPartyOnly && !r.HasExtraRewardTicket, "a solo room");
        var e = Make(room, portal: 77);
        Click(e, Child(e, "m_enterSoloButton"));
        Assert.That(e.Sent, Is.EqualTo(new IOutPacket[] { new RoomDungeonEntryRequest(room.Id, false, 77, false) }));
    }

    [Test]
    public void APartyEnterByAMemberSendsNothingAndByTheLeaderSendsTheEntry()
    {
        var room = Room(r => r.GroupType > 0 && r.MaxUserCount > 1 && !r.HasExtraRewardTicket, "a party room");
        var member = Make(room, book: PartyOf(2));
        member.Panel.OnEnterParty();
        Assert.That(member.Sent, Is.Empty);
        var leader = Make(room, book: PartyOf(1), portal: 5);
        leader.Panel.OnEnterParty();
        Assert.That(leader.Sent, Is.EqualTo(new IOutPacket[] { new RoomDungeonEntryRequest(room.Id, true, 5, false) }));
    }

    [Test]
    public void APartyEnterInAMatchmadeRunOfTheRoomSendsTheDungeonId()
    {
        var room = Room(r => r.GroupType > 0 && r.MaxUserCount > 1, "a party room");
        var e = Make(room, book: PartyOf(2, dungeon: (int) room.Id, matchmaking: true));
        e.Panel.OnEnterParty();
        Assert.That(e.Sent, Is.EqualTo(new IOutPacket[] { new RoomDungeonEnterRequest((int) room.DungeonId) }));
    }

    [Test]
    public void TheVisibleEnterButtonsSitOnTheLeftCentreAndRightPlacesByCount()
    {
        var seen = new HashSet<int>();
        foreach (var room in Rooms().RoomList.Where(r => r.GroupType > 0))
        {
            bool solo = !room.IsPartyOnly && room.MinUserCount == 0;
            int count = (solo ? 1 : 0) + (room.IsUseRandomMatch ? 1 : 0) + 1;
            if (!seen.Add(count)) continue;
            var e = Make(room);
            var shown = new[] { "m_enterSoloButton", "m_partySearchButton", "m_enterPartyButton" }.Select(n => Child(e, n)).Where(c => Visible(e, c)).ToList();
            Assert.That(shown, Has.Count.EqualTo(count));
            var want = count switch { 1 => new[] { 126.0 }, 2 => new[] { 8.0, 252.0 }, _ => new[] { 8.0, 126.0, 252.0 } };
            Assert.That(shown.Select(c => X(e, c)), Is.EqualTo(want), "count " + count);
            Assert.That(shown.Select(c => Y(e, c)).Distinct().Count(), Is.EqualTo(1));
        }
        if (seen.Count == 0) Assert.Ignore("this room table has no groups (GMS2)");
    }

    [Test]
    public void ThePartySearchSendsStartThenCancelWithTheGroupsClubBuffType()
    {
        var room = Room(r => r.GroupType > 0 && r.IsUseRandomMatch, "a random match room");
        var e = Make(room);
        Click(e, Child(e, "m_partySearchButton"));
        Assert.That(e.Sent.Last(), Is.EqualTo(new PartyDungeonSearchStartRequest(room.Id)));
        var on = Make(room, book: PartyOf(1, bits: 0xff & ~1));
        on.Panel.RefreshPartySearchButton();
        Assert.That(Rt.ToBoolean(on.Host.Get(Child(on, "m_partySearchButton"), "selected")), Is.EqualTo(on.Book.IsMatchSearching(Ms2.Quest.QuestCommon.ClubBuffTypeOf(room.GroupType))));
        if (on.Book.IsMatchSearching(Ms2.Quest.QuestCommon.ClubBuffTypeOf(room.GroupType)))
        {
            on.Panel.OnPartySearch();
            Assert.That(on.Sent.Last(), Is.EqualTo(new PartyDungeonSearchCancelRequest((byte) Ms2.Quest.QuestCommon.ClubBuffTypeOf(room.GroupType))));
        }
    }

    [Test]
    public void TheSearchingIndicatorFollowsTheGroupsSearchBit()
    {
        var room = Room(r => r.GroupType > 0 && r.IsUseRandomMatch && Ms2.Quest.QuestCommon.ClubBuffTypeOf(r.GroupType) > 0, "a random match room of a searchable group");
        int bit = Ms2.Quest.QuestCommon.ClubBuffTypeOf(room.GroupType);
        var on = Make(room, book: PartyOf(1, bits: 1 << bit));
        Assert.That(Visible(on, Child(on, "m_dungeonSearching")), Is.True);
        var off = Make(room, book: PartyOf(1));
        Assert.That(Visible(off, Child(off, "m_dungeonSearching")), Is.False);
        off.C.OnRoomPacket53(2);
        Assert.That(Visible(off, Child(off, "m_dungeonSearching")), Is.False);
    }

    [Test]
    public void TheFavouriteMarkSendsItsNewState()
    {
        var room = Any();
        var e = Make(room);
        var mark = Child(e, "m_favoriteMark");
        e.Host.Set(mark, "selected", true);
        Click(e, mark);
        Assert.That(e.Sent.Last(), Is.EqualTo(new RoomDungeonFavoriteRequest(room.Id, true)));
    }

    // ---- rewards ----

    [Test]
    public void TheRewardTextCarriesTheRecordsCountOverItsMax()
    {
        var room = Room(r => r.GroupType > 0 && r.RewardCount > 0 && r.SubRewardCount == 0 && r.UnionRewardId == 0, "a weekly reward room");
        var e = Make(room);
        AddRecord(e, Rec(room.Id));
        e.C.Refresh();
        var r = e.C.Records.Peek(room.Id);
        Assert.That(Text(e, Child(e, "m_rewardText1")), Does.Contain($"{e.C.Records.BonusRewardCountNow(r)} / {e.C.Records.MaxCount(r)}"));
        Assert.That(Text(e, Child(e, "m_rewardText2")), Is.EqualTo(""));
        Assert.That(Visible(e, Child(e, "m_rewardIcon2")), Is.False);
    }

    /// <summary>GameEventDungeonExtraRewardCli::OnStart and vfunc_6 (gameeventcli.cpp:737-752) call the open window's RefreshReward: the
    /// reward text takes the extra reward event's count (GetWeeklyRewardCountText @0x140e14d70) when it starts and drops it when it ends.</summary>
    [Test]
    public void AnExtraRewardEventStartingAndEndingRefreshesTheRewardText()
    {
        var room = Room(r => r.GroupType > 0 && r.RewardCount > 0 && r.SubRewardCount == 0 && r.UnionRewardId == 0, "a weekly reward room");
        var e = Make(room);
        AddRecord(e, Rec(room.Id));
        e.C.Refresh();
        var events = new DungeonExtraRewards();
        e.C.Records.ExtraRewardEventCount = events.CountOf;
        string plain = Text(e, Child(e, "m_rewardText1"));
        var start = new DungeonExtraRewardEvent(5, new[] { new DungeonExtraRewardCount(room.RewardRoomId, 5, 3) });
        events.Apply(new GameEventMessage(1, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { DungeonExtraRewardEvents = new[] { start } });
        Assert.That(Text(e, Child(e, "m_rewardText1")), Is.EqualTo(plain), "nothing refreshed yet");
        e.C.RefreshReward();
        string withEvent = Text(e, Child(e, "m_rewardText1"));
        Assert.That(withEvent, Does.StartWith(plain));
        Assert.That(withEvent.Length, Is.GreaterThan(plain.Length), "the event's count follows the record's");
        events.Apply(new GameEventMessage(2, Array.Empty<ShutdownInteractObjectEvent>(), new[] { 5 }, false));
        e.C.RefreshReward();
        Assert.That(Text(e, Child(e, "m_rewardText1")), Is.EqualTo(plain));
    }

    [Test]
    public void ARoomWithASubRewardShowsTheSecondRewardRow()
    {
        var room = Room(r => r.GroupType > 0 && r.SubRewardCount > 0 && r.UnionRewardId == 0, "a daily and weekly reward room");
        var e = Make(room);
        AddRecord(e, Rec(room.Id, subClears: 1));
        e.C.Refresh();
        Assert.That(Visible(e, Child(e, "m_rewardIcon2")), Is.True);
        Assert.That(Text(e, Child(e, "m_rewardText2")), Does.Contain("/"));
    }

    [Test]
    public void TheExtraRewardButtonIsShownOnlyWithAnExtraRewardTicket()
    {
        var with = Room(r => r.GroupType > 0 && r.HasExtraRewardTicket && r.UnionRewardId == 0, "an extra reward ticket");
        var without = Room(r => r.GroupType > 0 && !r.HasExtraRewardTicket && r.UnionRewardId == 0 && !r.HasRoundGroup, "no extra reward ticket");
        Assert.That(Visible(Make(with), Child(Make(with), "m_addExtraRewardCount")), Is.True);
        var e = Make(without);
        Assert.That(Visible(e, Child(e, "m_addExtraRewardCount")), Is.False);
    }

    [Test]
    public void TheExtraRewardButtonAsksBeforeSendingTheUse()
    {
        var room = Room(r => r.GroupType > 0 && r.HasExtraRewardTicket && r.UnionRewardId == 0 && Rooms().ExtraRewardTicketCost(r, 0) > 0, "an extra reward ticket with a cost");
        var e = Make(room);
        e.Panel.OnAddExtraRewardCount();
        Assert.That(e.Boxes, Has.Count.EqualTo(1));
        Assert.That(e.Sent, Is.Empty, "nothing is sent before the answer");
        e.Boxes[0].Callback(0, e.Boxes[0]);
        Assert.That(e.Sent, Is.EqualTo(new IOutPacket[] { new RoomDungeonUseExtraRewardRequest(room.Id) }));
    }

    [Test]
    public void ARoomWithoutAnExtraRewardTicketGivesThePlainBoxAndSendsNothing()
    {
        var room = Room(r => r.GroupType > 0 && !r.HasExtraRewardTicket && r.UnionRewardId == 0, "no extra reward ticket");
        var e = Make(room);
        e.Panel.OnAddExtraRewardCount();
        Assert.That(e.Boxes, Has.Count.EqualTo(1));
        Assert.That(e.Sent, Is.Empty);
    }

    // ---- helper ----

    [Test]
    public void TheHelperWidgetsStayHiddenWithoutFeature0x93()
    {
        var e = Make(Any());
        foreach (var n in new[] { "m_findHelperButton", "m_findMemberButton", "m_helpCoinInfo", "m_changeExpertButton", "m_dungeonHelpEvent" })
            Assert.That(Visible(e, Child(e, n)), Is.False, n);
    }

    [Test]
    public void FindHelperNeedsTheLeaderAndRoomInThePartyAndSendsItsRoom()
    {
        var room = Room(r => r.GroupType > 0 && r.MaxUserCount > 1, "a party room");
        // The button is hidden without feature 0x93; a click on it still runs the handler, as the client's does.
        var member = Make(room, book: PartyOf(2), before: x => x.C.Hooks.ClearState = _ => 0);
        Click(member, Child(member, "m_findHelperButton"));
        Assert.That(member.Sent, Is.Empty);
        Assert.That(member.Notices, Does.Contain("s_party_err_not_chief"));
    }

    [Test]
    public void ChangeExpertAsksFirstThenSendsTheRoom()
    {
        var room = Any();
        var e = Make(room);
        e.Panel.OnChangeExpert();
        Assert.That(e.Sent, Is.Empty);
        Assert.That(e.Boxes, Has.Count.EqualTo(1));
        e.Boxes[0].Callback(1, e.Boxes[0]);
        Assert.That(e.Sent, Is.Empty, "no sends the other button");
        e.Boxes[0].Callback(0, e.Boxes[0]);
        Assert.That(e.Sent, Is.EqualTo(new IOutPacket[] { new RoomDungeonChangeExpertRequest(room.Id) }));
    }

    // ---- sub tabs and layout ----

    [Test]
    public void TheSubPanelsFollowTheirTabsTopToBottom()
    {
        var e = Make(Any());
        double y = 0;
        for (int i = 0; i < DungeonRoomInfoPanel.SubCount; i++)
        {
            var tab = Sub(e, "m_subTabButton" + i);
            Assert.That(Y(e, tab), Is.EqualTo(y), "tab " + i);
            y += 0x22;
            if (!Visible(e, tab)) continue;
            var panel = Sub(e, "m_subPanel" + i);
            if (Visible(e, panel)) { Assert.That(Y(e, panel), Is.EqualTo(y), "panel " + i); y += Math.Ceiling(Rt.ToNumber(e.Host.Get(panel, "height"))); }
        }
    }

    [Test]
    public void TheMissionAndSeasonTabsAreVisibleOnlyForTheirRooms()
    {
        var e = Make(Any());
        e.C.Model.Features.Add(0x71);
        e.C.Refresh();
        var room = e.C.Room;
        Assert.That(Visible(e, Sub(e, "m_subTabButton3")), Is.EqualTo(room.HasMissions));
        Assert.That(Visible(e, Sub(e, "m_subTabButton4")), Is.EqualTo(room.SeasonRankRewardId != 0));
    }

    [Test]
    public void TheMissionRankPanelHasALabelPerMissionAndTheSummaryBelowThem()
    {
        var room = Room(r => r.GroupType > 0 && r.HasMissions, "missions");
        var e = Make(room, before: x => x.Model.Features.Add(0x71));
        var panel = Sub(e, "m_subPanel3");
        var summary = Child(e, "m_summaryScore", panel);
        Assert.That(Y(e, summary), Is.EqualTo(room.Missions().Count * 0x18 + 5));
        Assert.That(X(e, summary), Is.EqualTo(0x88));
    }

    [Test]
    public void TheSeasonRewardClipsAreSpreadByTheirCountAndTheRestHidden()
    {
        var room = Room(r => r.GroupType > 0 && r.SeasonRankRewardId != 0 && Rooms().GetRankReward(r.SeasonRankRewardId) is { MaxRank: > 0 }, "a season rank reward");
        var e = Make(room, before: x => x.Model.Features.Add(0x71));
        int max = Rooms().GetRankReward(room.SeasonRankRewardId).MaxRank;
        int left = max < 5 ? 0x21 : 0x13, step = max < 5 ? 0x72 : 0x5f;
        var panel = Sub(e, "m_subPanel4");
        for (int i = 0; i < 5; i++)
        {
            var clip = Child(e, "m_seasonRankReward" + i, panel);
            Assert.That(Visible(e, clip), Is.EqualTo(i < max), "reward " + i);
            if (i < max) Assert.That(X(e, clip), Is.EqualTo(left + step * i), "x " + i);
        }
    }

    // ---- the enter limit message ----

    [Test]
    public void TheEnterLimitMessageIsTheNoticeOfItsReason()
    {
        var room = Room(r => r.GroupType > 0 && r.LimitPlayerLevel > 0, "a level limit");
        var e = Make(room);
        e.C.ShowEnterLimitMessage(room.Id, 12);
        e.C.ShowEnterLimitMessage(room.Id, 14);
        e.C.ShowEnterLimitMessage(room.Id, 99);
        e.C.ShowEnterLimitMessage(0xfffffff, 12);
        Assert.That(e.NoticeTexts, Has.Count.EqualTo(3), "an unknown reason shows nothing; no room shows the 0x99a text");
        Assert.That(e.NoticeTexts[0], Is.Not.EqualTo(e.NoticeTexts[1]));
    }

    [Test]
    public void ARoomPacket53RefreshesOnlyForItsCommands()
    {
        var room = Room(r => r.GroupType > 0 && r.IsUseRandomMatch && Ms2.Quest.QuestCommon.ClubBuffTypeOf(r.GroupType) > 0, "a random match room of a searchable group");
        var book = PartyOf(1);
        var e = Make(room, book: book);
        var search = Child(e, "m_partySearchButton");
        Assert.That(Rt.ToBoolean(e.Host.Get(search, "selected")), Is.False);
        int bit = Ms2.Quest.QuestCommon.ClubBuffTypeOf(room.GroupType);
        book.Apply(new PartyDungeonMatchRegister(bit, true, false, 1 << bit), 1, false);
        e.C.OnRoomPacket53(5);
        Assert.That(Rt.ToBoolean(e.Host.Get(search, "selected")), Is.False, "command 5 does not refresh");
        e.C.OnRoomPacket53(9);
        Assert.That(Rt.ToBoolean(e.Host.Get(search, "selected")), Is.True);
    }

    // ---- reward preview and description ----

    static List<Ms2.Ui.Items.ItemInfoSlot> RewardSlots(Env e) =>
        e.Model.ItemSlots.Where(k => k.Key.StartsWith("DungeonRoomDialog_", StringComparison.Ordinal))
            .OrderBy(k => int.Parse(k.Key.Substring("DungeonRoomDialog_".Length))).Select(k => k.Value).ToList();

    [Test]
    public void TheRewardGridListsTheRoomsItemsInTheClientsOrder()
    {
        var room = Room(r => r.GroupType > 0 && r.MaxUserCount > 0 && r.RewardDropBoxIds.Count > 0 &&
                             DungeonRoomInfoPanel.PreviewItems(Tables(), r, 0, 60).Count > 1, "reward items");
        var e = Make(room);
        var expected = DungeonRoomInfoPanel.PreviewItems(Tables(), room, 0, 60).Take(DungeonRoomInfoPanel.RewardItemCount).ToList();
        var slots = RewardSlots(e);
        Assert.That(slots.Select(s => s.Item.ItemId), Is.EqualTo(expected.Select(x => x.ItemId)));
        Assert.That(slots.All(s => s.IconType == 0x403), Is.True, "the binder's icon type (ctor :537)");
        var panel2 = Sub(e, "m_subPanel2");
        for (int i = 0; i < expected.Count; i++) Assert.That(Visible(e, Child(e, "m_rewardItem" + i, panel2)), Is.True, "slot " + i);
        // a second refresh unbinds the first one's slots before binding its own
        e.Panel.Refresh();
        Assert.That(RewardSlots(e).Select(s => s.Item.ItemId), Is.EqualTo(expected.Select(x => x.ItemId)));
    }

    [Test]
    public void TheRewardGridFollowsTheRoomItIsRefreshedFor()
    {
        var rooms = Rooms().RoomList.Where(r => r.GroupType > 0 && r.MaxUserCount > 0 && r.RewardDropBoxIds.Count > 0).Take(40).ToList();
        if (rooms.Count < 2) Assert.Ignore("fewer than two rooms with reward boxes");
        var e = Make(rooms[0]);
        foreach (var r in rooms)
        {
            e.Panel.SetRoom(r);
            e.Panel.Refresh();
            var expected = DungeonRoomInfoPanel.PreviewItems(Tables(), r, 0, 60).Take(DungeonRoomInfoPanel.RewardItemCount).Select(x => x.ItemId);
            Assert.That(RewardSlots(e).Select(s => s.Item.ItemId), Is.EqualTo(expected), $"room {r.Id}");
        }
    }

    [Test]
    public void TheDescriptionFieldHoldsTheFieldEntranceText()
    {
        var room = Room(r => r.GroupType > 0 && r.MaxUserCount > 0 && Tables().StringTableText(0x15, r.Id).Length > 0, "a description");
        var e = Make(room);
        var desc = Sub(e, "m_subPanel0") is { } sub ? Child(e, "m_dungeonDesc", sub) : null;
        Assert.That(desc, Is.Not.Null);
        // the field holds the table's text, not the movie's list index text: its first characters (tags and blanks dropped) lead the field
        static string Bare(string t) => new string(System.Text.RegularExpressions.Regex.Replace(t, "<[^>]*>", "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        string table = Bare(Tables().StringTableText(0x15, room.Id)), shown = Bare(Text(e, desc));
        Assert.That(shown, Does.StartWith(table.Substring(0, Math.Min(table.Length, 6))), "not the list index text of the movie");
        Assert.That(Rt.ToNumber(e.Host.Get(desc, "height")), Is.GreaterThan(0x14));
    }
}
