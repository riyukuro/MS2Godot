using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CBuddyManager's packet handling (BuddyBook), UICommunityQueryDialog's checks and sends (CommunityQueryController)
/// and UICommunityDialog's rows (CommunityController); the last tests run the movies uicommunitydialog and
/// uicommunityquerydialog with their ActionScript (needs MS2_DATA).</summary>
[NonParallelizable]
public class CommunityTests
{
    static string Text(int id) => $"#{id:x}";

    static BuddyInfo B(long id, string name, bool received = false, bool waiting = false, bool blocked = false, bool online = false) => new()
    {
        Id = id, CharacterId = id + 100, Name = name, Received = received, Waiting = waiting, Blocked = blocked, Online = online,
        Level = 40, JobGroup = 2, Memo = $"memo{id}",
    };

    static BuddyBook Book(params BuddyInfo[] entries)
    {
        var b = new BuddyBook { Common = Text };
        b.Apply(new BuddyMessage(1) { Entries = entries });
        return b;
    }

    [Test]
    public void TheListFillsTheBookAndEachPacketCountsTheMenu()
    {
        int counts = 0;
        var b = new BuddyBook { Common = Text };
        b.MenuCount += () => counts++;
        b.Apply(new BuddyMessage(1) { Entries = new[] { B(1, "Ann", received: true), B(2, "Bo", online: true), B(3, "Cy", blocked: true) } });
        Assert.That((b.Count, b.ReceivedCount, b.OnlineCount, counts), Is.EqualTo((3, 1, 1, 1)));
        Assert.That(b.FindByName("bo")?.Id, Is.EqualTo(2), "names are found case-insensitively");
        Assert.That((b.IsFriend(102), b.IsBlocked(103), b.IsBlockedName("Cy")), Is.EqualTo((true, true, true)));
        var chat = new List<string>();
        b.ChatLine += (t, _) => chat.Add(t);
        b.Apply(new BuddyMessage(3) { Id = 1 });
        Assert.That((b.FindById(1).Received, b.FindById(1).IsNew, b.ReceivedCount), Is.EqualTo((false, true, 0)), "protocol 3 accepts");
        b.Apply(new BuddyMessage(7) { Id = 2 });
        Assert.That(b.FindById(2), Is.Null, "protocol 7 removes");
        Assert.That(chat, Is.EqualTo(new[] { "#5fd", "#605" }));
        b.Apply(new BuddyMessage(15));
        Assert.That((b.Count, counts), Is.EqualTo((0, 4)));
    }

    [Test]
    public void AnAutoRejectedRequestIsDeclinedBeforeTheMenuCount()
    {
        var sent = new List<IOutPacket>();
        int counts = 0;
        var b = new BuddyBook { Common = Text, Send = sent.Add, AutoReject = () => true };
        b.MenuCount += () => counts++;
        b.Apply(new BuddyMessage(9) { Entries = new[] { B(5, "Dee", received: true) } });
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { BuddyRequest.Decline(5) }));
        Assert.That((b.Count, counts), Is.EqualTo((0, 0)));
    }

    /// <summary>One packet into a fresh book (Ann a friend, Bo a received request, Cy our waiting request, Dee blocked, Eve a
    /// friend at home field 100 plot 2): every UI call OnBuddy makes, in order.</summary>
    static (BuddyBook, List<string>) Run(BuddyMessage m)
    {
        var calls = new List<string>();
        var b = new BuddyBook { Common = id => $"#{id:x} {{0}}", Send = p => calls.Add($"sent:{p}") };
        b.Apply(new BuddyMessage(1) { Entries = new[] { B(1, "Ann"), B(2, "Bo", received: true), B(3, "Cy", waiting: true), B(4, "Dee", blocked: true), Home(B(5, "Eve"), 100, 2) } });
        b.Notice += (t, _, _) => calls.Add($"notice:{t}");
        b.ChatLine += (t, kind) => calls.Add($"chat{kind}:{t}");
        b.Sound += s => calls.Add($"sound:{s}");
        b.Box += t => calls.Add($"box:{t}");
        b.BuddyEvent += (k, id, arg) => calls.Add($"{k} {id} {arg}");
        b.ListRebuild += () => calls.Add("rebuild");
        b.MenuCount += () => calls.Add("count");
        b.Apply(m);
        return (b, calls);
    }

    static BuddyInfo Home(BuddyInfo b, int field, int plot) { b.HomeMapId = field; b.HomePlot = plot; return b; }

    static IEnumerable<TestCaseData> EveryProtocol()
    {
        TestCaseData C(string name, BuddyMessage m, params string[] calls) => new TestCaseData(m, calls).SetName($"Protocol {name}");
        yield return C("2 sent", new BuddyMessage(2) { Name = "Zed" }, "notice:#600 Zed", "count");
        yield return C("2 failed", new BuddyMessage(2) { Result = 4, Name = "Zed" }, "count");
        yield return C("3 accepted", new BuddyMessage(3) { Id = 2 }, "sound:buddy_agree", "chat12:#5fd Bo", "Accepted 2 0", "count");
        yield return C("3 failed", new BuddyMessage(3) { Result = 1, Id = 2 }, "count");
        yield return C("4 declined", new BuddyMessage(4) { Id = 3 }, "sound:buddy_deny", "chat12:#601 Cy", "Declined 3 0", "count");
        yield return C("4 failed", new BuddyMessage(4) { Result = 1, Id = 3 }, "count");
        yield return C("5 blocked", new BuddyMessage(5) { Id = 1 }, "chat12:#5fe Ann", "Blocked 1 1", "count");
        yield return C("5 failed", new BuddyMessage(5) { Result = 1, Id = 1 }, "count");
        yield return C("6 unblocked", new BuddyMessage(6) { Id = 4 }, "chat12:#606 Dee", "Unblocked 4 0", "UnblockedRemoved 104 0", "count");
        yield return C("6 failed", new BuddyMessage(6) { Result = 1, Id = 4 }, "count");
        yield return C("7 our request", new BuddyMessage(7) { Id = 3 }, "notice:#602 Cy", "Removed 3 0", "count");
        yield return C("7 their request", new BuddyMessage(7) { Id = 2 }, "chat12:#603 Bo", "Removed 2 0", "count");
        yield return C("7 a friend", new BuddyMessage(7) { Id = 1 }, "chat12:#605 Ann", "Removed 1 0", "count");
        yield return C("7 unknown id", new BuddyMessage(7) { Id = 99 }, "count");
        yield return C("7 failed", new BuddyMessage(7) { Result = 1, Id = 1 }, "count");
        yield return C("8 moved", new BuddyMessage(8) { Entries = new[] { Home(B(5, "Eve"), 200, 2) } }, "Updated 5 0", "Moved 5 100", "count");
        yield return C("8 other plot", new BuddyMessage(8) { Entries = new[] { Home(B(5, "Eve"), 100, 3) } }, "Updated 5 0", "Moved 5 100", "count");
        yield return C("8 same home", new BuddyMessage(8) { Entries = new[] { Home(B(5, "Eve"), 100, 2) } }, "Updated 5 0", "count");
        yield return C("8 not a friend", new BuddyMessage(8) { Entries = new[] { Home(B(3, "Cy", waiting: true), 200, 2) } }, "Updated 3 0", "count");
        yield return C("8 new entry", new BuddyMessage(8) { Entries = new[] { B(6, "Fay") } }, "Updated 6 0", "count");
        yield return C("9 request in", new BuddyMessage(9) { Entries = new[] { B(7, "Gus", received: true) } }, "sound:buddy_request", "chat12:#607 {0}", "RequestReceived 7 0", "count");
        yield return C("9 our request", new BuddyMessage(9) { Entries = new[] { B(3, "Cy", waiting: true) } }, "Accepted 3 0", "count");
        yield return C("9 blocked", new BuddyMessage(9) { Entries = new[] { B(8, "Hal", blocked: true) } }, "Blocked 8 0", "count");
        yield return C("9 a friend", new BuddyMessage(9) { Entries = new[] { B(9, "Ivy") } }, "count");
        yield return C("9 failed", new BuddyMessage(9) { Result = 1, Entries = new[] { B(7, "Gus", received: true) } }, "count");
        yield return C("10 memo", new BuddyMessage(10) { Id = 4, Name = "Dee", Memo = "new" }, "notice:#5ff Dee", "MemoChanged 4 0", "count");
        yield return C("10 unknown id", new BuddyMessage(10) { Id = 99, Name = "Zed", Memo = "new" }, "notice:#5ff Zed", "count");
        yield return C("10 failed", new BuddyMessage(10) { Result = 1, Id = 4, Name = "Dee" }, "count");
        yield return C("11 accepted", new BuddyMessage(11) { Id = 3 }, "chat12:#5fd Cy", "Accepted 3 0", "count");
        yield return C("11 unknown id", new BuddyMessage(11) { Id = 99 }, "count");
        yield return C("12 blocked", new BuddyMessage(12) { Name = "Zed" }, "chat12:#5fe Zed", "count");
        yield return C("12 result 10", new BuddyMessage(12) { Result = 10, Name = "Zed" }, "notice:#61b Zed", "count");
        yield return C("12 other result", new BuddyMessage(12) { Result = 3, Name = "Zed" }, "count");
        yield return C("13 answers", new BuddyMessage(13) { Answers = new[] { ("Ann", "approved", 1L), ("Bo", "declined", 2L), ("Cy", "removed", 3L), ("Dee", "Approved", 4L) } },
            "chat12:#5fd Ann", "chat12:#602 Bo", "chat12:#605 Cy", "chat12:", "count");
        yield return C("14 online", new BuddyMessage(14) { Flag = 0, Name = "Ann" }, "chat12:#608 Ann", "count");
        yield return C("14 offline", new BuddyMessage(14) { Flag = 1, Name = "Ann" }, "chat12:#609 Ann", "count");
        yield return C("14 other flag", new BuddyMessage(14) { Flag = 2, Name = "Ann" }, "chat12:", "count");
        yield return C("15 clear", new BuddyMessage(15), "count");
        yield return C("17 cancelled", new BuddyMessage(17) { Id = 3 }, "chat12:#604 Cy", "Cancelled 3 0", "count");
        yield return C("17 failed", new BuddyMessage(17) { Result = 1, Id = 3 }, "count");
        yield return C("18 rebuild", new BuddyMessage(18), "rebuild", "box:#8ba {0}", "count");
        yield return C("20 auto reject", new BuddyMessage(20), $"sent:{new BuddyAutoRejectRequest(false)}", "count");
        yield return C("unknown", new BuddyMessage(0x99), "count");
    }

    /// <summary>CBuddyManager::OnBuddy (buddymanager.cpp:95-188): each protocol's messages, sounds, dispatcher calls and sends, and
    /// the game menu's count after every packet; a failed result or an unknown id does nothing but the count.</summary>
    [TestCaseSource(nameof(EveryProtocol))]
    public void EveryProtocolMakesTheClientsUiCalls(BuddyMessage m, string[] calls) => Assert.That(Run(m).Item2, Is.EqualTo(calls));

    [Test]
    public void EachProtocolChangesTheBookAsTheClientDoes()
    {
        static BuddyBook After(BuddyMessage m) => Run(m).Item1;
        var b3 = After(new BuddyMessage(3) { Id = 2 }).FindById(2);
        Assert.That((b3.Received, b3.IsFriend, b3.IsNew), Is.EqualTo((false, true, true)), "3: the received request became a friend");
        Assert.That(After(new BuddyMessage(4) { Id = 3 }).FindById(3), Is.Null, "4: our request is gone");
        var b5 = After(new BuddyMessage(5) { Id = 1 }).FindById(1);
        Assert.That((b5.Blocked, b5.IsNew), Is.EqualTo((true, true)), "5");
        var b6 = After(new BuddyMessage(6) { Id = 4 });
        Assert.That((b6.FindById(4), b6.FindByAccount(104)), Is.EqualTo(((BuddyInfo)null, (BuddyInfo)null)), "6: both maps drop it");
        Assert.That(After(new BuddyMessage(7) { Id = 1 }).Count, Is.EqualTo(4), "7");
        var b8 = After(new BuddyMessage(8) { Entries = new[] { Home(B(5, "Eve2", online: true), 200, 2) } });
        var eve = b8.FindById(5);
        Assert.That((b8.Count, eve.Name, eve.Online, eve.HomeMapId, eve.IsNew), Is.EqualTo((5, "Eve2", true, 200, false)), "8 reads into the known entry and keeps IsNew");
        var b8n = After(new BuddyMessage(8) { Entries = new[] { B(6, "Fay") } });
        Assert.That((b8n.Count, b8n.FindById(6).IsNew, b8n.Buddies[^1].Name), Is.EqualTo((6, true, "Fay")), "8 adds an unknown entry at the end, new");
        var b9 = After(new BuddyMessage(9) { Entries = new[] { B(7, "Gus", received: true) } });
        Assert.That((b9.ReceivedCount, b9.FindById(7).IsNew), Is.EqualTo((2, true)), "9");
        Assert.That(After(new BuddyMessage(10) { Id = 4, Name = "Dee", Memo = "new" }).FindById(4).Memo, Is.EqualTo("new"), "10");
        Assert.That(After(new BuddyMessage(10) { Result = 1, Id = 4, Name = "Dee", Memo = "new" }).FindById(4).Memo, Is.EqualTo("memo4"), "10 failed");
        var b11 = After(new BuddyMessage(11) { Id = 3 }).FindById(3);
        Assert.That((b11.Waiting, b11.IsFriend, b11.IsNew), Is.EqualTo((false, true, true)), "11: our request was accepted");
        var b13 = After(new BuddyMessage(13) { Answers = new[] { ("ann", "approved", 1L), ("Bo", "Approved", 2L) } });
        Assert.That((b13.FindById(1).IsNew, b13.FindById(2).IsNew), Is.EqualTo((true, false)), "13: the name is found without case, the action is compared with it");
        Assert.That(After(new BuddyMessage(15)).Count, Is.Zero, "15");
        Assert.That(After(new BuddyMessage(17) { Id = 3 }).FindById(3), Is.Null, "17");
        var b1 = After(new BuddyMessage(1) { Entries = new[] { B(1, "Dup"), B(10, "Jo") } });
        Assert.That((b1.Count, b1.FindById(1).Name), Is.EqualTo((6, "Ann")), "1: AddBuddy skips a known id");
    }

    static readonly Ms2.Formats.Text.BanTextChecker NoBans = new(_ => null);

    static (CommunityQueryController, List<IOutPacket>, List<string>, List<MessageBoxData>) Query(BuddyBook book, string self = "Me")
    {
        var sent = new List<IOutPacket>();
        var notices = new List<string>();
        var boxes = new List<MessageBoxData>();
        var q = new CommunityQueryController(book) { Common = Text, Checker = NoBans, SelfName = () => self, Send = sent.Add, ShowBox = boxes.Add, NextBox = () => { } };
        q.SystemNotice += notices.Add;
        return (q, sent, notices, boxes);
    }

    [Test]
    public void TheRequestChecksTheNameAndTheBookBeforeItIsSent()
    {
        var book = Book(B(1, "Ann"), B(2, "Bo", blocked: true), B(3, "Cy", waiting: true), B(4, "Dee", received: true));
        var (q, sent, notices, _) = Query(book);
        var sounds = new List<string>();
        q.Sound += sounds.Add;
        foreach (var name in new[] { "", "me", "Ann", "Bo", "Cy", "Dee" }) q.OnCallback("OnOk", new object[] { 1, name, "hi" });
        Assert.That(notices.Select(n => n.Split(' ')[0]), Is.EqualTo(new[] { "#615", "#616", "#621", "#619", "#622", "#61a" }));
        Assert.That(sent, Is.Empty);
        q.OnCallback("OnOk", new object[] { 1, "Eve", "  hi  " });
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { BuddyRequest.Invite("Eve", "hi") }), "the message is trimmed");
        Assert.That(sounds, Is.EqualTo(new[] { "PC_InviteSuccess" }));
    }

    [Test]
    public void ABlockAsksFirstAndAnAlreadyBlockedNameIsRejected()
    {
        var book = Book(B(2, "Bo", blocked: true));
        var (q, sent, notices, boxes) = Query(book);
        q.OnCallback("OnOk", new object[] { 2, "Bo", "x" });
        Assert.That(notices.Single(), Does.StartWith("#620"));
        q.OnCallback("OnOk", new object[] { 2, "Eve", " rude " });
        var box = boxes.Single();
        Assert.That((box.Type, box.Extra, box.Listener), Is.EqualTo((1, "banBuddy", (IMessageBoxListener)q)));
        Assert.That(sent, Is.Empty, "nothing goes before yes");
        q.OnMessageBox(null, 0, "banBuddy");
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { BuddyRequest.Block(0, "Eve", "rude") }));
    }

    [Test]
    public void WithNoCheckerTheInviteAndTheBlockSendNothing()
    {
        // uibuddyeventlistener.cpp:1925 and :1756: the check and the send are inside if (pChecker)
        var (q, sent, _, boxes) = Query(Book());
        q.Checker = null;
        q.OnCallback("OnOk", new object[] { 1, "Eve", "hi" });
        q.OnCallback("OnOk", new object[] { 2, "Fay", "x" });
        Assert.That(boxes.Single().Extra, Is.EqualTo("banBuddy"), "the block memo check passes without a checker");
        q.OnMessageBox(null, 0, "banBuddy");
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void RemoveAndUnblockAskInABoxWithTheEntryId()
    {
        var (q, sent, _, boxes) = Query(Book(B(5, "Ann"), B(6, "Bo", blocked: true)));
        Assert.That(q.OnQuery(1, 6, "Ann", "5"), Is.False);
        Assert.That(boxes.Single().Extra, Is.EqualTo("delBuddy"));
        q.OnMessageBox(null, 0, "delBuddy");
        q.OnQuery(1, 7, "Bo", "6");
        q.OnMessageBox(null, 1, "delBanBuddy");       // no
        q.OnQuery(1, 7, "Bo", "6");
        q.OnMessageBox(null, 0, "delBanBuddy");
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { BuddyRequest.Remove(5), BuddyRequest.Unblock(6) }));
    }

    [Test]
    public void AQueryOpensOnlyForItsModesAndTheAnswerShowsItsNotice()
    {
        var (q, _, notices, _) = Query(Book());
        int opens = 0;
        q.OpenRequested += () => opens++;
        Assert.That(q.OnQuery(0, 0), Is.True);
        Assert.That(q.OnQuery(0, 9), Is.True);
        Assert.That(opens, Is.Zero);
        Assert.That(q.OnQuery(0, 1, "Ann"), Is.True);
        Assert.That((opens, q.Mode), Is.EqualTo((1, 1)));
        q.OnPacket(new BuddyMessage(2) { Result = 4, Name = "Ann" });
        q.OnPacket(new BuddyMessage(5) { Result = 1 });
        q.OnPacket(new BuddyMessage(3) { Result = 4 });            // not an answer of the query
        q.OnPacket(new BuddyMessage(12) { Result = 0x30 });
        Assert.That(notices.Select(n => n.Split(' ')[0]), Is.EqualTo(new[] { "#617", "#614", "#613" }));
        Assert.That(q.Mode, Is.Zero, "the answer resets the query");
    }

    [Test]
    public void TheRowsCarryTheTabsFields()
    {
        var c = new CommunityController(Book()) { Common = Text, MapName = id => $"map{id}", JobName = j => $"job{j}", SelfCharacterId = () => 101 };
        var b = B(1, "Ann", online: true);
        b.Channel = 3; b.MapId = 7; b.IsNew = true; b.TodayMemo = "today";
        var friend = c.BuddyRow(b, 1);
        Assert.That(friend["charname"], Is.EqualTo("Ann"));
        Assert.That(friend["buddysn"], Is.EqualTo("1"));
        Assert.That((friend["offline"], friend["isNew"], friend["homeVisitEnable"], friend["recv"]), Is.EqualTo((false, 1, 0, 0)), "the own character has no home visit");
        Assert.That(friend["todaymemo"], Is.EqualTo("today"));
        Assert.That(friend.ContainsKey("memo"), Is.False);
        Assert.That(c.BuddyRow(b, 1)["isNew"], Is.EqualTo(0), "a row clears the new mark");
        var request = c.BuddyRow(B(2, "Bo", received: true), 2);
        Assert.That((request["recv"], request["state"], request["homeVisitEnable"]), Is.EqualTo((1, "#60d", 1)));
        Assert.That(request.ContainsKey("msg"), Is.True);
        Assert.That(c.BuddyRow(B(3, "Cy", blocked: true), 3)["memo"], Is.EqualTo("memo3"));
        Assert.That(c.BuddyRow(B(4, "Dee", waiting: true, online: true), 1)["state"], Is.EqualTo("#60b"), "a waiting request reads as waiting");
    }

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static (UiHost, T) Load<T>(T c) where T : UiController
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    [Test]
    public void TheListMovieLoadsAndTakesTheRows()
    {
        var book = Book(B(1, "Ann", online: true), B(2, "Bo"), B(3, "Cy", received: true), B(4, "Dee", blocked: true));
        var (h, c) = Load(new CommunityController(book) { Common = Text, ShowBox = _ => { } });
        foreach (var fn in new[] { "buildBuddyList", "addBuddy", "removeBuddy", "updateBuddy", "setNew", "setOnline" })
            Assert.That(h.Get(h.Entity, fn), Is.Not.Null, $"the movie's {fn}");
        c.SetTab(1);
        c.BuildBuddyList(1);
        c.Process();
        Assert.That(c.Online, Is.EqualTo(1));
    }

    static IEnumerable<AsDisplayObject> Named(AsDisplayObject o, string name)
    {
        if (o.name == name) yield return o;
        if (o is AsContainer c) foreach (var k in c.Children.ToList()) foreach (var f in Named(k, name)) yield return f;
    }

    [Test]
    public void TheOwnRowHidesItsVisitButtonAndIcons()
    {
        var self = new NearbyPlayer(7, "Me", "", 21, 100, "", "", (0, 0, 0));
        var (h, c) = Load(new CommunityController(Book())
        {
            Common = Text, ShowBox = _ => { }, SelfCharacterId = () => 7, NearbyPlayers = () => new[] { self },
        });
        c.BuildBuddyList(0);
        for (int i = 0; i < 5; i++) h.Player.Tick();
        // CommunityRenderer.setData: gotoAndStop("player") runs the renderer's frame setup (__setProp_*, visible = true) at
        // once, so the own row's homeVisitEnable/isBuddy/isParty lines after it hide the button and icons
        var root = (AsDisplayObject)h.Entity;
        foreach (var n in new[] { "m_homeVisit", "m_iconBuddy", "m_iconParty" })
            Assert.That(Named(root, n).Where(o => o.EffectivelyVisible), Is.Empty, n);
    }

    [Test]
    public void TheQueryMovieLoadsAndTakesItsKeys()
    {
        var q = new CommunityQueryController(Book()) { Common = Text, Checker = NoBans, ShowBox = _ => { }, NextBox = () => { } };
        q.OnQuery(0, 1, "Ann");
        var (h, _) = Load(q);
        foreach (var fn in new[] { "initData", "onEnterKey", "onTabKey", "errorName" })
            Assert.That(h.Get(h.Entity, fn), Is.Not.Null, $"the movie's {fn}");
        Assert.That(q.OnKey(false, true), Is.True);
        Assert.That(q.OnKey(false, false), Is.False);
    }
}
