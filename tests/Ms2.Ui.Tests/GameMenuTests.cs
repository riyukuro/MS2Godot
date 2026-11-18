using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The game menu (uigamemenudialog) run by its own ActionScript with UIGameMenuDialog's C++ side (needs MS2_DATA).</summary>
public class GameMenuTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };
    static bool FeatureOn(int id) => NewModel().IsFeatureEnabled(id);

    static (UiHost, GameMenuController) Load()
    {
        var c = new GameMenuController { Table = GameMenuTable.Load(Packs(), FeatureOn) };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var strs = new Dictionary<string, string>();
        using (var xr = System.Xml.XmlReader.Create(new MemoryStream(Packs().Read("Xml", "string/en/korflash.xml"))))
            while (xr.Read())
                if (xr.NodeType == System.Xml.XmlNodeType.Element && xr.Name == "key" && xr.GetAttribute("id") is { } id && xr.GetAttribute("value") is { } v)
                    strs[id] = v.Replace("\\n", "\n");
        p.Translate = k => strs.TryGetValue(k, out var t) ? t : k;
        var h = new UiHost(p, c, NewModel());
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    [Test]
    public void TableHasTheKrGameSet()
    {
        var t = GameMenuTable.Load(Packs(), FeatureOn);
        var top = t.TopItems().Select(i => (t.Find(i.Id)?.Name, i.Col, i.Row)).ToList();
        TestContext.WriteLine(string.Join(", ", top));
        Assert.That(top.Take(6), Is.EqualTo(new[]
        {
            ("charactergroup", 0, 0), ("contentsgroup", 1, 0), ("gamegroup", 2, 0), ("communitygroup", 3, 0), ("home", 4, 0), ("option", 5, 0),
        }));
        Assert.That(t.FindTopId(9), Is.EqualTo(1001), "the inventory is in the character group");
        Assert.That(t.IsChild(9), Is.True);
        Assert.That(t.IsChild(22), Is.False);
    }

    [Test]
    public void EntriesAreBuiltByTheMovie()
    {
        var (h, c) = Load();
        var menus = h.Child("m_menus");
        var names = Enumerable.Range(0, (int)Rt.ToNumber(h.Get(menus, "numChildren")))
            .Select(i => Rt.ToString(h.Get(h.Invoke(menus, "getChildAt", i), "name"))).ToList();
        TestContext.WriteLine(string.Join(", ", names));
        Assert.That(names, Does.Contain("charactergroup").And.Contain("home").And.Contain("option"));
        // addChildAt(entry, 0): the first entry ends up last
        Assert.That(names.Last(), Is.EqualTo("charactergroup"));
        var group = h.Invoke(menus, "getChildByName", "charactergroup");
        Assert.That(Rt.ToNumber(h.Get(group, "x")), Is.EqualTo(0));
        Assert.That(Rt.ToNumber(h.Get(group, "y")), Is.EqualTo(120));
        var button = h.Invoke(group, "getChildByName", "m_button");
        var dp = h.Get(button, "dataProvider");
        int n = (int)Rt.ToNumber(h.Get(dp, "length"));
        var kids = Enumerable.Range(0, n).Select(i => Rt.ToString(h.Get(h.Invoke(dp, "requestItemAt", i), "name"))).ToList();
        TestContext.WriteLine("character group: " + string.Join(", ", kids));
        Assert.That(kids, Does.Contain("inven").And.Contain("skillbook").And.Contain("characterinfo"));
        var home = h.Invoke(menus, "getChildByName", "home");
        Assert.That(Rt.ToNumber(h.Get(home, "x")), Is.EqualTo(4 * 56));
        Assert.That(Rt.ToString(h.Get(h.Invoke(home, "getChildByName", "m_button"), "m_layerName")), Is.EqualTo("home"));
        // setMenuTag: the caption in the tag, bottom-aligned (TextFieldEx.setVerticalAlign)
        var tag = (Ms2.Avm2.Flash.AsTextField)h.Get(group, "tag");
        Assert.That(tag.Text, Is.EqualTo("Character"));
        Assert.That(tag.VerticalAlign, Is.EqualTo("bottom"));
    }

    [Test]
    public void ClickingAnEntryTogglesItsDialog()
    {
        var (h, c) = Load();
        int toggled = 0; GameMenuDef action = null;
        c.ToggleDialog += id => toggled = id;
        c.MenuAction += d => action = d;
        Assert.That(c.OnCallback("inven", Array.Empty<object>()), Is.True);
        Assert.That(toggled, Is.EqualTo(0x4e2d));
        Assert.That(c.OnCallback("home", Array.Empty<object>()), Is.True);
        Assert.That(action?.Action, Is.EqualTo("FUN_14193b750"));
        Assert.That(c.OnCallback("ugcshop_event", Array.Empty<object>()), Is.True);
        Assert.That(action?.Id, Is.EqualTo(15));
        Assert.That(c.OnCallback("nosuchentry", Array.Empty<object>()), Is.False);
    }
}
