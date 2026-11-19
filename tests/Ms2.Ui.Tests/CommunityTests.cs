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
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
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
