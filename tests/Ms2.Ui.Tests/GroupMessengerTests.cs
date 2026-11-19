using System.Text.RegularExpressions;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// UIGroupMessengerBrowser / UIGroupMessenger (GroupMessengerBrowserController, GroupMessengerController): the window
/// slots and their cap, the talk logs' counts, the leave box, and with the movies (needs MS2_DATA) the fixture's windows,
/// the keys of the current window only, the typed line's send and an item link into the current window.
/// Texts are common ids ("c&lt;id&gt; {0}") or fixture names, so no language's strings.
/// </summary>
public class GroupMessengerTests
{
    static PackSet packs;
    static KeyTable keys;
    static MovieSource src;
    static GameTables tables;

    static UiModel Model()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
            keys = new KeyTable(packs, "en");
        }
        var m = new UiModel(tables) { Keys = keys };
        UiFixture.Apply(m);
        m.HasLocalPlayer = true;
        m.Player.Name = "Me";
        m.ServerTime = () => new DateTime(2026, 1, 1, 9, 5, 0);
        return m;
    }

    static readonly List<string> loadErrors = new();

    static UiHost Load(UiController c, UiModel m)
    {
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        loadErrors.AddRange(rt.Errors);
        return h;
    }

    readonly List<IOutPacket> sent = new();

    [SetUp]
    public void Reset() { sent.Clear(); loadErrors.Clear(); }

    GroupMessengerBrowserController Browser(UiModel m, Func<GroupMessengerController, bool> open = null)
    {
        var b = new GroupMessengerBrowserController
        {
            Common = id => $"c{id:x} {{0}}",
            LocalName = () => "Me",
            LocalCharacterId = () => PartyFixture.SelfCharacterId,
        };
        b.Names.Model = m;
        b.Names.ServerTime = () => 0;
        b.Book.Common = b.Common;
        b.Book.Send = sent.Add;
        b.Book.LocalName = b.LocalName;
        b.Book.Messenger = () => b;
        b.Book.ShownBox = () => null;
        b.Book.ShowBox = _ => { };
        b.OpenWindow = open ?? (_ => true);
        return b;
    }

    static GroupChat Chat(int oid) => new GroupChat { Oid = oid }.With(PartyFixture.SelfCharacterId);

    [Test]
    public void WindowsKeepToThreeSlotsAndReuseAFreedOne()
    {
        var b = Browser(new UiModel(null) { HasLocalPlayer = true });
        for (int oid = 1; oid <= 3; oid++) b.Book.AddChat(Chat(oid));
        b.OpenJoinedChatWindows();
        Assert.That(b.Windows.Select(w => (w.GroupId, w.Index)), Is.EqualTo(new[] { (1, 0), (2, 1), (3, 2) }));
        Assert.That(b.Windows.Select(w => w.Name), Is.EqualTo(new[] { "c5d0 {0} 1", "c5d0 {0} 2", "c5d0 {0} 3" }));
        Assert.That(b.OpenChatWindow(1, false, false), Is.False, "three windows: none more, the old one kept");
        Assert.That(b.FindWindowByGroupId(1), Is.Not.Null);
        b.CloseGroupWindow(2);
        Assert.That(b.FindWindowByGroupId(2), Is.Null);
        Assert.That(b.OpenChatWindow(9, false, false), Is.False, "no such chat");
        Assert.That(b.OpenChatWindow(2, false, false), Is.True);
        Assert.That(b.FindWindowByGroupId(2).Index, Is.EqualTo(1));
        // a window that did not open is not kept
        var refused = Browser(new UiModel(null) { HasLocalPlayer = true }, _ => false);
        refused.Book.AddChat(Chat(1));
        Assert.That(refused.OpenChatWindow(1, false, false), Is.False);
        Assert.That(refused.Windows, Is.Empty);
        // no local player: OpenJoinedChatWindows opens nothing
        var none = Browser(new UiModel(null));
        none.Book.AddChat(Chat(1));
        none.OpenJoinedChatWindows();
        Assert.That(none.Windows, Is.Empty);
    }

    [Test]
    public void TalkLogsCountTheMinimizedLinesAndKeepFiftyOneReportLines()
    {
        var b = Browser(new UiModel(null) { HasLocalPlayer = true });
        b.Book.AddChat(Chat(1));
        b.OpenJoinedChatWindows();
        b.AddTalkLog(9, "a", "x", false, true);                       // no log: nothing
        Assert.That(b.LogsOf(9), Is.Empty);
        b.AddTalkLog(1, "a", "x", false, true);
        b.AddTalkLog(1, "a", "y", false, true);
        b.AddTalkLog(1, "a", "z", true, false);
        Assert.That(b.GetReadLineCount(1), Is.EqualTo(2));
        Assert.That(b.GetUnreadLineCount(1), Is.EqualTo(1));
        b.MarkChatRead(1);
        Assert.That(b.GetReadLineCount(1), Is.EqualTo(0));
        Assert.That(b.GetUnreadLineCount(1), Is.EqualTo(3));
        for (int i = 0; i < 60; i++) b.AddTalkLog(1, "a", "m" + i, false, false);
        Assert.That(b.LogsOf(1).Count, Is.EqualTo(63));
        Assert.That(b.LinesOf(1).Count, Is.EqualTo(51));
        Assert.That(b.LinesOf(1)[0], Does.EndWith("][Group][a] m9\n"));
        Assert.That(b.LinesOf(1).Last(), Does.Match(@"^\[[ \d]{2}:[ \d]{2}:[ \d]{2}\]\[Group\]\[a\] m59\n$"));
    }

    [Test]
    public void TheLeaveBoxSendsTheLeaveOnlyOnItsFirstButton()
    {
        var b = Browser(new UiModel(null) { HasLocalPlayer = true });
        b.Book.AddChat(Chat(7));
        b.OpenJoinedChatWindows();
        var w = b.FindWindowByGroupId(7);
        Assert.That(w.OnMessageBox(null, 1, GroupMessengerController.ExitTag), Is.True);
        Assert.That(sent, Is.Empty);
        Assert.That(w.OnMessageBox(null, 0, GroupMessengerController.ExitTag), Is.True);
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GroupChatLeaveRequest(7, 0) }));
        Assert.That(w.OnMessageBox(null, 0, "other"), Is.False);
    }

    [Test]
    public void AChatWindowTakesKeysOnlyAsTheCurrentName()
    {
        var m = Model();
        var log = new MessengerBrowser { Model = m };
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true, log);
        Load(c, m);
        log.MakeCurrent("another window");                             // a group window's focus (FUN_141482c80)
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.False);
        log.MakeCurrent(c.Name);
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(loadErrors, Is.Empty);
    }

    [Test]
    public void TheFixtureWindowsLoadShowTheirLinesAndSendTheTypedLine()
    {
        var m = Model();
        var hosts = new Dictionary<GroupMessengerController, UiHost>();
        var b = Browser(m, w => { hosts[w] = Load(w, m); return true; });
        GroupChatFixture.Apply(b.Book, m.Player);
        var bh = Load(b, m);
        Assert.That(b.Windows.Select(w => w.GroupId), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(hosts.Count, Is.EqualTo(2));
        b.Process();                                                   // the queued lines to their windows
        Assert.That(b.LogsOf(1).Select(l => l.Text), Is.EqualTo(new[] { "1", "2", "3" }));
        Assert.That(b.LogsOf(2).Select(l => l.Text), Is.EqualTo(new[] { "4" }));
        // the window list: one row per window
        Assert.That(b.Windows.Select(w => w.Name), Is.EqualTo(new[] { "c5d0 {0} 1", "c5d0 {0} 2" }));

        var w1 = b.FindWindowByGroupId(1);
        var w2 = b.FindWindowByGroupId(2);
        var h1 = hosts[w1];
        var input = (AsObject)h1.Child("m_textInput");
        Assert.That(input, Is.Not.Null);
        b.Names.MakeCurrent(w2.Name);
        Assert.That(w1.OnKey(MessengerController.DikReturn, false), Is.False, "not the current window");
        b.Names.MakeCurrent(w1.Name);
        Assert.That(w1.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(w1.InputActive, Is.True);
        h1.Set(h1.Get(input, "textField"), "text", "  hello <b> ");
        Assert.That(w1.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GroupChatTalkRequest("hello &lt;b&gt;", 1) }));
        Assert.That(w1.InputActive, Is.False);

        // an item link from the chat log (FUN_140d2d060) reaches the current group window only (FUN_141953660)
        b.Names.GroupLink = b.LinkToCurrent;
        b.Names.MakeCurrent(w2.Name);
        b.Names.LinkItem(15000001, 1, 7);
        Assert.That(w2.InputHistory.Count, Is.EqualTo(1));
        Assert.That(w1.InputHistory, Is.Empty);
        Assert.That(Rt.ToString(hosts[w2].Get(hosts[w2].Child("m_textInput"), "text")), Does.EndWith("] "));

        // the leave answer: the chat's removal closes its window
        b.Book.Apply(new GroupChatRemove(2));
        Assert.That(b.FindWindowByGroupId(2), Is.Null);
        Assert.That(bh.RT.Errors, Is.Empty);
        Assert.That(loadErrors, Is.Empty);
    }
}

static class GroupChatTestExtensions
{
    public static GroupChat With(this GroupChat c, long characterId)
    {
        c.AddMember(new GroupChatMember { State = 1, User = new PartyUser { Name = "Me", CharacterId = characterId } });
        return c;
    }
}
