using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The chat window's log scrolls: the wheel over the log, the scroll bar (thumb drag, arrows, track), and a new line
/// keeping the view where it is unless it was at the end (needs MS2_DATA).</summary>
public class ChatScrollTests
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

    static (MessengerController c, UiHost h) ChatWindow()
    {
        var m = Model();
        var log = new MessengerBrowser { Model = m };
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true, log);
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return (c, h);
    }

    static double N(UiHost h, object o, string name) => Rt.ToNumber(h.Get(o, name));

    static (double X, double Y) Center(UiHost h, object parent, string child)
    {
        var o = (AsDisplayObject)h.Get(parent, child);
        var b = o.GetBounds(o);
        return o.LocalToGlobal(b.X + b.W / 2, b.Y + b.H / 2);
    }

    /// <summary>A chat window with a long log in the shown tab: its output field, the field and the scroll bar.</summary>
    static (MessengerController c, UiHost h, AsObject output, object tf, AsObject bar) Filled(int lines = 80)
    {
        var (c, h) = ChatWindow();
        var output = (AsObject)h.Child("m_textOutput");
        for (int i = 0; i < lines; i++) c.AppendLine("line " + i + "<br>", 0);
        for (int i = 0; i < 5; i++) h.Player.Tick();
        return (c, h, output, h.Get(output, "textField"), (AsObject)h.Child("m_scrollBar"));
    }

    static double ScrollV(UiHost h, object tf) => N(h, tf, "scrollV");

    static void Press(UiHost h, double x, double y)
    {
        h.Player.MouseMove(x, y);
        h.Player.MouseButton(true);
        h.Player.Tick();
    }

    static void Release(UiHost h)
    {
        h.Player.MouseButton(false);
        h.Player.Tick();
    }

    [Test]
    public void LongLogStartsAtItsEndWithTheThumbDown()
    {
        var (_, h, output, tf, bar) = Filled();
        Assert.That(N(h, tf, "numLines"), Is.GreaterThan(N(h, tf, "bottomScrollV") - N(h, tf, "scrollV") + 1), "more lines than fit");
        Assert.That(ScrollV(h, tf), Is.EqualTo(N(h, tf, "maxScrollV")));
        Assert.That(N(h, output, "position"), Is.EqualTo(N(h, tf, "maxScrollV")));
        Assert.That(N(h, bar, "position"), Is.EqualTo(N(h, tf, "maxScrollV")), "the bar follows the field");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void WheelOverTheLogScrollsItAndTheBarFollows()
    {
        // TextField::OnMouseWheelEvent (sfr_gfx_03e.cpp:915): the view moves up by the wheel's delta, within 1 .. maxScrollV
        var (_, h, output, tf, bar) = Filled();
        double max = N(h, tf, "maxScrollV");
        var (gx, gy) = ((AsDisplayObject)output).LocalToGlobal(20, 20);
        h.Player.MouseMove(gx, gy);
        h.Player.Wheel(3);
        h.Player.Tick();
        Assert.That(ScrollV(h, tf), Is.EqualTo(max - 3));
        Assert.That(N(h, output, "position"), Is.EqualTo(max - 3), "the text area's position follows the field");
        Assert.That(N(h, bar, "position"), Is.EqualTo(max - 3), "so does the scroll bar");
        h.Player.Wheel(-1);
        Assert.That(ScrollV(h, tf), Is.EqualTo(max - 2));
        for (int i = 0; i < 40; i++) h.Player.Wheel(3);
        Assert.That(ScrollV(h, tf), Is.EqualTo(1), "stops at the first line");
        for (int i = 0; i < 40; i++) h.Player.Wheel(-3);
        Assert.That(ScrollV(h, tf), Is.EqualTo(max), "stops at the last position");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void WheelOverTheScrollBarScrollsIt()
    {
        // ScrollBar::handleMouseWheel (scaleform.clik.controls.ScrollBar): position - delta * pageScrollSize
        var (_, h, _, tf, bar) = Filled();
        double max = N(h, tf, "maxScrollV");
        var (gx, gy) = Center(h, bar, "track");
        h.Player.MouseMove(gx, gy);
        h.Player.Wheel(1);
        h.Player.Tick();
        Assert.That(ScrollV(h, tf), Is.LessThan(max), "the bar's wheel moved the field up");
        Assert.That(N(h, bar, "position"), Is.EqualTo(ScrollV(h, tf)));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ThumbDragMovesTheView()
    {
        var (_, h, _, tf, bar) = Filled();
        double max = N(h, tf, "maxScrollV");
        var (tx, ty) = Center(h, bar, "thumb");
        var (_, top) = Center(h, bar, "track");
        double trackTop = ((AsDisplayObject)h.Get(bar, "track")).LocalToGlobal(0, 0).Y;
        Press(h, tx, ty);
        h.Player.MouseMove(tx, trackTop + 2);                // to the top of the track
        h.Player.Tick();
        Release(h);
        Assert.That(ScrollV(h, tf), Is.LessThan(max / 2), "dragged to the top");
        double mid = ScrollV(h, tf);
        var (tx2, ty2) = Center(h, bar, "thumb");
        Press(h, tx2, ty2);
        h.Player.MouseMove(tx2, ty2 + 40);
        h.Player.Tick();
        Release(h);
        Assert.That(ScrollV(h, tf), Is.GreaterThan(mid), "dragged down again");
        Assert.That(N(h, bar, "position"), Is.EqualTo(ScrollV(h, tf)));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ArrowsAndTrackScrollTheView()
    {
        var (_, h, _, tf, bar) = Filled();
        double max = N(h, tf, "maxScrollV");
        var (ux, uy) = Center(h, bar, "upArrow");
        Press(h, ux, uy);
        Release(h);
        Assert.That(ScrollV(h, tf), Is.EqualTo(max - 1), "the up arrow: one line");
        var (dx, dy) = Center(h, bar, "downArrow");
        Press(h, dx, dy);
        Release(h);
        Assert.That(ScrollV(h, tf), Is.EqualTo(max), "the down arrow: back");
        // the track above the thumb: a page (trackScrollPageSize) up
        var track = (AsDisplayObject)h.Get(bar, "track");
        var (gx, gy) = track.LocalToGlobal(2, 5);
        Press(h, gx, gy);
        Release(h);
        Assert.That(ScrollV(h, tf), Is.LessThan(max), "the track above the thumb scrolls up");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void NewLineKeepsAScrolledUpViewAndFollowsAtTheEnd()
    {
        // FUN_141a139c0 (uimessenger.cpp:1739): a line appended while the view is at the end stays at the end
        // (ASTextArea::IsScrolledToEnd, astextarea.cpp:44); scrolled up, the view does not move
        var (c, h, output, tf, bar) = Filled();
        c.AppendLine("at end<br>", 0);
        Assert.That(ScrollV(h, tf), Is.EqualTo(N(h, tf, "maxScrollV")), "at the end it follows");
        var (gx, gy) = ((AsDisplayObject)output).LocalToGlobal(20, 20);
        h.Player.MouseMove(gx, gy);
        h.Player.Wheel(10);
        double at = ScrollV(h, tf);
        c.AppendLine("while up<br>", 0);
        c.AppendLine("while up 2<br>", 0);
        for (int i = 0; i < 3; i++) h.Player.Tick();
        Assert.That(ScrollV(h, tf), Is.EqualTo(at), "scrolled up it stays");
        Assert.That(N(h, bar, "position"), Is.EqualTo(at));
        Assert.That(h.RT.Errors, Is.Empty);
    }
}
