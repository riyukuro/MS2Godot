using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The system menu movie (uisystemmenudialog) run by its own ActionScript with UISystemMenuDialog's C++ side
/// (needs MS2_DATA).</summary>
public class SystemMenuTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static (UiHost, SystemMenuController) Load(Action<SystemMenuController> setup = null)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var c = new SystemMenuController
        {
            MapType = 0, ChannelWord = "Ch.", CurrentChannel = 2,
            Channels = new[] { new ChannelRow(1), new ChannelRow(2), new ChannelRow(3) },
        };
        setup?.Invoke(c);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        TestContext.WriteLine($"calls [{string.Join(", ", h.Calls)}] unhandled [{string.Join(", ", h.Unhandled)}]");
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    static double Num(UiHost h, object o, string m) => Rt.ToNumber(h.Get(o, m));

    static void Click(UiHost h, string name)
    {
        var b = ((AsDisplayObject)h.Child(name)).GetBounds(null);
        h.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
        for (int i = 0; i < 5; i++) h.Player.Tick();
    }

    [Test]
    public void LeaveEntryShowsOnlyForItsMapTypes()
    {
        var (h, c) = Load();
        Assert.That(c.IsOpen, Is.True);
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_exitRoom"), "visible")), Is.False, "map type 0 has no leave entry");
        double bgNone = Num(h, h.Child("m_bg"), "height");
        (h, c) = Load(x => { x.MapType = 5; x.LeaveText = "Leave Dungeon"; });
        var exit = h.Child("m_exitRoom");
        Assert.That(Rt.ToBoolean(h.Get(exit, "visible")), Is.True);
        Assert.That(Rt.ToString(h.Get(exit, "label")), Is.EqualTo("Leave Dungeon"));
        Assert.That(Num(h, h.Child("m_bg"), "height"), Is.GreaterThan(bgNone), "arrangeConrols docks the shown entry");
        int left = -1;
        c.Leave += t => left = t;
        Click(h, "m_exitRoom");
        Assert.That(left, Is.EqualTo(5));
    }

    [Test]
    public void ChannelListHasTheRowsAndTheCurrentOne()
    {
        var (h, c) = Load(x => x.ChannelGroupName = "Grp");
        var list = h.Child("m_channel");
        var dp = h.Get(list, "dataProvider");
        Assert.That(Rt.ToInt32(h.Get(dp, "length")), Is.EqualTo(3));
        Assert.That(Rt.ToInt32(h.Get(list, "selectedIndex")), Is.EqualTo(1));
        TestContext.WriteLine("row 0 label: " + Rt.ToString(h.Get(h.Invoke(dp, "requestItemAt", 0), "label")));
        int asked = 0;
        c.ChangeChannel = ch => { asked = ch; return false; };
        Assert.That(c.OnCallback("OnChangeChannel", new object[] { 3 }), Is.True);
        Assert.That(asked, Is.EqualTo(3));
    }

    [Test]
    public void EntriesCallTheBridgeAndCloseEnds()
    {
        var (h, c) = Load();
        int page = -1;
        c.OpenGameOption += p => page = p;
        bool unloaded = false;
        c.Unloaded += () => unloaded = true;
        Click(h, "m_graphic");
        Assert.That(page, Is.EqualTo(6));
        Assert.That(c.IsOpen, Is.False, "OpenGameOptionDialog closes the menu");
        for (int i = 0; i < 60 && !unloaded; i++) h.Player.Tick();
        Assert.That(h.Calls, Does.Contain("OnEndClose"));
        Assert.That((unloaded, c.IsClosed), Is.EqualTo((true, true)));

        (h, c) = Load();
        bool character = false, exit = false;
        c.SelectCharacter += () => character = true;
        c.GameExit += () => exit = true;
        Click(h, "m_selectCharacter");
        Assert.That((character, c.IsOpen), Is.EqualTo((true, true)), "the menu stays for character select");
        Click(h, "m_exitGame");
        Assert.That((exit, c.IsOpen), Is.EqualTo((true, false)), "exit asks, then the movie closes the menu");
        Assert.That(h.Unhandled, Is.Empty);
    }
}
