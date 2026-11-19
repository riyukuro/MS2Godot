using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>UI movies' own ActionScript driven the way the client's C++ drives them (needs MS2_DATA).</summary>
public class MovieTests
{
    static MovieSource src;

    static (Runtime Rt, Player P) Open(string key)
    {
        if (src == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            var packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        var (rt, p) = src.Open(key, r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        Assert.That(p.Start(), Is.True, key + " document class");
        for (int i = 0; i < 5; i++) p.Tick();
        return (rt, p);
    }

    /// <summary>A namespace constant (<c>namespace motion_internal = "..."</c>, a script trait with a namespace value) is the
    /// namespace itself: fl.motion's MotionBase keeps spanStart in it, and the timeline code reaches it through an RTQName whose
    /// namespace comes from that constant. It used to read back as null (public), so the write found no property.</summary>
    [Test]
    public void NamespaceConstantQualifiesRuntimeNames()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var source = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var (rt, p) = source.Open("bg_login_2020_spring", r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        p.Start();
        Assert.That(rt.Errors.Where(e => e.Contains("spanStart")), Is.Empty);
        Assert.That(rt.Missing.Keys.Where(k => k.Contains("spanStart")), Is.Empty);
    }

    static object Get(Player p, object o, string n) => p.RT.GetProperty(o, Name.Public(n));

    static MovieSource Fresh()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        return new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    /// <summary>The display list after the document class and 5 frames: type, name, depth, matrix, colour, visibility,
    /// frame and text of every object, in child order.</summary>
    static string DisplayList(MovieSource source, string key, out Player player)
    {
        var (_, p) = source.Open(key, r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        Assert.That(p.Start(), Is.True, key + " document class");
        for (int i = 0; i < 5; i++) p.Tick();
        var sb = new System.Text.StringBuilder();
        void Walk(AsDisplayObject d, int depth)
        {
            var m = d.Matrix;
            sb.Append(' ', depth).Append(d.GetType().Name).Append(' ').Append(d.InstanceName).Append(' ').Append(d.Depth)
              .Append($" [{m.A:R} {m.B:R} {m.C:R} {m.D:R} {m.Tx:R} {m.Ty:R}] {d.Color} {d.IsVisible}");
            if (d is AsMovieClip mc) sb.Append(" f").Append(mc.currentFrame);
            if (d is AsTextField t) sb.Append(" \"").Append(t.Text).Append('"');
            sb.Append('\n');
            if (d is AsContainer c) foreach (var ch in c.Children) Walk(ch, depth + 1);
        }
        Walk(p.Stage, 0);
        player = p;
        return sb.ToString();
    }

    /// <summary>
    /// A movie source shared by every open (MovieDialog's, one per pack set) gives each open the same display list as a
    /// source of its own, the second open too: the cached layouts and code are only read.
    /// </summary>
    [TestCase("uiinventorydialog")]
    [TestCase("uiworldmapdialog")]
    [TestCase("uishopdialog")]
    [TestCase("uimyinfodialog")]
    public void SharedSourceOpensMatchAFreshSource(string key)
    {
        var shared = Fresh();
        var first = DisplayList(shared, key, out var p1);
        var second = DisplayList(shared, key, out var p2);
        var fresh = DisplayList(Fresh(), key, out var pf);
        Assert.That(fresh.Split('\n').Length, Is.GreaterThan(40), "a whole window's display list");
        Assert.That(p2.Layout, Is.SameAs(p1.Layout), "the layout is built once");
        Assert.That(first, Is.EqualTo(fresh));
        Assert.That(second, Is.EqualTo(fresh));
        Assert.That(p1.Layout.ToJsonString(), Is.EqualTo(pf.Layout.ToJsonString()), "the opens leave the shared layout as built");
    }

    [Test]
    public void LoginTypingTabAndConnect()
    {
        var (rt, p) = Open("uilogindialog");
        var dlg = p.FindOwner("onEnterKey");
        string connected = null;
        p.External = (n, a) =>
        {
            if (a.Length > 0 && Rt.ToString(a[0]) == "OnConnect")
                connected = Rt.ToString(Get(p, Get(p, dlg, "m_inputID"), "text")) + "/" + Rt.ToString(Get(p, Get(p, dlg, "m_inputPW"), "text"));
            return null;
        };
        var id = (AsDisplayObject)Get(p, Get(p, dlg, "m_inputID"), "textField");
        var b = id.GetBounds(null);
        p.Click(b.X + b.W / 2, b.Y + b.H / 2); p.Tick();
        Assert.That(p.Focus, Is.SameAs(id));
        ((AsTextField)p.Focus).setSelection(0, ((AsTextField)p.Focus).Text.Length);
        foreach (var ch in "tester") p.Key(char.ToUpperInvariant(ch), ch, true);
        p.Tick();
        p.Call(dlg, "onTabKey"); p.Tick();
        Assert.That(p.Focus, Is.SameAs(Get(p, Get(p, dlg, "m_inputPW"), "textField")));
        foreach (var ch in "secret") p.Key(char.ToUpperInvariant(ch), ch, true);
        p.Tick();
        p.Call(dlg, "onEnterKey"); p.Tick();
        Assert.That(connected, Is.EqualTo("tester/secret"));
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void MinimapEntityHidesItsHitArea()
    {
        var (rt, p) = Open("uiminimapdialog");
        var dlg = p.FindOwner("createEntity");
        var e = p.Call(dlg, "createEntity", p.NewObject(("mcname", "portal"), ("frame", "portal"), ("layer", "portal"), ("x", 120), ("y", 80),
            ("visible", true), ("showtext", false), ("showguide", false), ("name", "portal"))) as AsSprite;
        p.Tick();
        Assert.That(e, Is.Not.Null);
        var hit = Get(p, e, "m_hitarea") as AsDisplayObject;
        Assert.That(hit, Is.Not.Null);
        Assert.That(e.hitArea, Is.SameAs(hit));
        Assert.That(hit.IsVisible, Is.False);
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void ExpBarSetExp()
    {
        var (rt, p) = Open("uiexpbar");
        if (p.FindOwner("alignment") is { } al) p.Call(al, "alignment");
        var dlg = p.FindOwner("setExp");
        Assert.That(dlg, Is.Not.Null);
        p.Call(dlg, "setExp", 42.5, 10.0, "42.50%", 3);
        for (int i = 0; i < 30; i++) p.Tick();
        Assert.That(rt.Errors, Is.Empty);
    }
}
