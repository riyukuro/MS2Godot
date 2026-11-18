using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The HUD movies run by their own ActionScript and fed by the Ms2.Ui controllers (needs MS2_DATA).</summary>
public class HudTests
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
        m.ServerTime = () => new DateTime(2026, 1, 1, 9, 5, 0);
        return m;
    }

    static UiHost Load(UiController c, UiModel m)
    {
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        TestContext.WriteLine($"{c.Movie}: calls [{string.Join(", ", h.Calls)}] unhandled [{string.Join(", ", h.Unhandled)}]");
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        foreach (var (k, n) in rt.Missing) TestContext.WriteLine($"  missing: {k} x{n}");
        return h;
    }

    static string Text(UiHost h, string name, AsDisplayObject from = null) => (h.FindNamed(name, from) as AsTextField)?.Text;

    [Test]
    public void TablesReadNextExpAndJobNames()
    {
        var m = Model();
        Assert.That(m.Tables.NextExp(1), Is.GreaterThan(0));
        Assert.That(m.Tables.NextExp(21), Is.GreaterThan(m.Tables.NextExp(1)));
        Assert.That(m.Tables.JobName(100), Is.EqualTo("Knight"));
        Assert.That(m.Tables.Common("s_tooltip_rest_exp"), Does.Contain("{1:.2f}"));
    }

    [Test]
    public void ExpBarShowsPercentAndAnswersGetExp()
    {
        var m = Model();
        var c = new ExpBarController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(Text(h, "textField", h.Child("m_point") as AsDisplayObject), Is.EqualTo("38.00%"));
        // vfunc_43: the component's size is the stage width (its m_bg reflows to it) and alignment centres it
        var bg = h.Child("m_bg");
        Assert.That(Rt.ToNumber(h.Get(bg, "width")), Is.EqualTo(1920).Within(1));
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), w = Rt.ToNumber(h.Get(h.Entity, "width"));
        Assert.That(x + w / 2, Is.EqualTo(960).Within(1));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo(1080 - 21).Within(1));
        var ud = c.GetUserData("getExp", Array.Empty<object>());
        Assert.That((string)ud["text"], Does.StartWith($"{m.Player.Exp:N0} / {m.NextLevelExp:N0}".Replace(' ', ',')));
        Assert.That((string)ud["text"], Does.Contain("Level 21"));
        // status event 3 redraws
        m.Player.Exp = m.NextLevelExp / 2; m.Notify(StatusEvent.Exp); h.Player.Tick();
        Assert.That(Text(h, "textField", h.Child("m_point") as AsDisplayObject), Is.EqualTo("50.00%"));
    }

    [Test]
    public void MainPlayerInfoInitAndStatusEvents()
    {
        var m = Model();
        var h = Load(new MainPlayerInfoController(), m);
        Assert.That(h.RT.Errors, Is.Empty);
        // vfunc_43: bottom centre above the exp bar
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), w = Rt.ToNumber(h.Get(h.Entity, "width"));
        double y = Rt.ToNumber(h.Get(h.Entity, "y")), ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(x + w / 2, Is.EqualTo(960).Within(1));
        Assert.That(y + ht, Is.EqualTo(1080 - 19).Within(1));
        var pi = h.Child("m_playerInfo") as AsDisplayObject;
        Assert.That(pi, Is.Not.Null);
        var texts = AllTexts(pi);
        TestContext.WriteLine("texts: " + string.Join(" | ", texts));
        // the quick slots bind "SlotInfo.skill.0.N" and show the key bound to each slot's action (DefaultKey.xml)
        Assert.That(h.BindingNames, Does.Contain("SlotInfo.skill.0.0").And.Contain("SlotInfo.skill.0.24"));
        Assert.That(h.RT.Errors, Is.Empty);
        var slotTexts = AllTexts(h.Child("m_slot") as AsDisplayObject);
        Assert.That(slotTexts.Where(t => t.StartsWith("textField=")).Take(48).Where((_, n) => n % 2 == 0).Select(t => t[10..]),
            Is.EqualTo(new[] { "Q", "W", "E", "R", "A", "S", "D", "F", "1", "2", "3", "4", "5", "6", "7", "8", "Insert", "Home", "Page Up", "Delete", "End", "Page Dn", "Z", "X" }));
        Assert.That(texts, Has.Some.Contains("1630"));
        Assert.That(texts, Has.Some.Contains("2100"));
        m.Player.HpCur = 1000; m.Notify(StatusEvent.Hp);
        for (int i = 0; i < 60; i++) h.Player.Tick();
        Assert.That(AllTexts(pi), Has.Some.Contains("1000"));
    }

    [Test]
    public void UpperHudMoneyAndClock()
    {
        var m = Model();
        var h = Load(new UpperHudController(), m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(Text(h, "mesoText"), Is.EqualTo("1,284,530"));
        Assert.That(Text(h, "maratText"), Is.EqualTo("350"));
        Assert.That(Text(h, "timeText"), Is.EqualTo("09:05"));
        m.Player.Mesos = 7; m.Notify(StatusEvent.Meso);
        Assert.That(Text(h, "mesoText"), Is.EqualTo("7"));
    }

    [Test]
    public void MinimapInitDataAndEntities()
    {
        var m = Model();
        m.Field.Name = "Tria";
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        c.Marks.Add(new() { Entity = new Ms2.Formats.World.MinimapIcons.Entity("portal", "", "portal", "Exit", true, false), Position = () => default, ShowGuide = true });
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.FieldClip, Is.Not.Null);
        // the movie's onFieldLoadComplete: BuildEntities through createEntity
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        Assert.That(c.Marks[0].Clip, Is.Not.Null);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    static List<string> AllTexts(AsDisplayObject o)
    {
        var l = new List<string>();
        void Walk(AsDisplayObject d)
        {
            if (d is AsTextField t && !string.IsNullOrEmpty(t.Text)) l.Add($"{t.InstanceName}={t.Text}");
            if (d is AsContainer c) foreach (var k in c.Children) Walk(k);
        }
        Walk(o);
        return l;
    }
}
