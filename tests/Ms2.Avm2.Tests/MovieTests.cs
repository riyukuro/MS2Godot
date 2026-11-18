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

    static object Get(Player p, object o, string n) => p.RT.GetProperty(o, Name.Public(n));

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
