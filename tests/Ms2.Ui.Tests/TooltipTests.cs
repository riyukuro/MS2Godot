using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The tooltip movie (CommonTooltip) run by its own ActionScript with GFxMovieTooltip's C++ side, fed by the game
/// menu's TooltipManager through getUserData "getTooltip" (needs MS2_DATA).</summary>
[NonParallelizable]
public class TooltipTests
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

    static (UiHost, T) Open<T>(T c, int ticks) where T : UiController
    {
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, NewModel());
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < ticks; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine($"  [{c.Movie}] error: " + e);
        return (h, c);
    }

    [TearDown]
    public void Drop() => UiHost.Tooltips = null;

    static Dictionary<string, string> flash;

    /// <summary>The tooltip movie draws <paramref name="data"/> (createTooltip as a dialog's TooltipManager sends it, with
    /// the movie's "$" strings from korflash): the texts it shows.</summary>
    internal static List<string> Draw(IDictionary<string, object> data)
    {
        Packs();
        if (flash == null)
        {
            flash = new();
            using var xr = System.Xml.XmlReader.Create(new MemoryStream(Packs().Read("Xml", "string/en/korflash.xml")));
            while (xr.Read())
                if (xr.NodeType == System.Xml.XmlNodeType.Element && xr.Name == "key" && xr.GetAttribute("id") is { } id && xr.GetAttribute("value") is { } v)
                    flash[id] = v.Replace("\\n", "\n");
        }
        var tc = new TooltipController();
        var (rt, p) = src.Open(tc.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => flash.TryGetValue(k, out var t) ? t : k;
        var th = new UiHost(p, tc, NewModel());
        Assert.That(th.Load(), Is.True);
        for (int i = 0; i < 5; i++) p.Tick();
        tc.Call("createTooltip", new object[] { 1, data, new Dictionary<string, object> { ["x"] = 600, ["y"] = 300, ["width"] = 40, ["height"] = 20 } });
        for (int i = 0; i < 30; i++) p.Tick();
        Assert.That(rt.Errors, Is.Empty, string.Join("; ", rt.Errors));
        return Texts(p.Stage).Select(t => t.Text).Where(t => t.Length > 0).ToList();
    }

    [Test]
    public void TextTooltipIsTheClientsObject()
    {
        Assert.That(AsTooltip.Text(""), Is.Null);
        var t = AsTooltip.Text("x");
        Assert.That(t["tooltip"], Is.EqualTo("export_common_tooltip_tooltipText"));
        Assert.That(t["onDelay"], Is.EqualTo(300));
        Assert.That(t["offDelay"], Is.EqualTo(60000));
        Assert.That(t.ContainsKey("tooltipAlign"), Is.False);
    }

    static IEnumerable<AsTextField> Texts(AsDisplayObject o)
    {
        if (o is AsTextField t) yield return t;
        if (o is AsContainer c)
            foreach (var k in c.Children.ToList())
                foreach (var x in Texts(k)) yield return x;
    }

    [Test]
    public void GameMenuEntryTooltipIsDrawnByTheTooltipMovie()
    {
        Packs();
        var (th, tc) = Open(new TooltipController(), 5);
        UiHost.Tooltips = tc;
        bool FeatureOn(int id) => NewModel().IsFeatureEnabled(id);
        var (h, c) = Open(new GameMenuController { Table = GameMenuTable.Load(Packs(), FeatureOn) }, 30);

        // the option entry's SystemDropdown (an ITooltip): getTooltipData = getUserData(id, "getTooltip", m_layerName)
        var data = c.GetUserData("getTooltip", new object[] { "option" });
        Assert.That(data?["text"], Is.EqualTo(Ms2.Quest.ClientFormat.Format(NewModel().Tables.Common("s_tooltip_gamemenu_gameoption"), "")));
        Assert.That(data.ContainsKey("tooltipAlign"), Is.False, "a top entry keeps the default align");
        // a child entry (the inventory, in the character group): its key command in brackets, align 3
        var inven = c.GetUserData("getTooltip", new object[] { "inven" });
        TestContext.WriteLine("inven: " + inven?["text"]);
        Assert.That(inven?["tooltipAlign"], Is.EqualTo(3));
        string key = h.Model.Keys.GetKeyName(h.Model.Keys.FindKeyByActionAndPriority(c.Table.FindByName("inven").KeyBind, 0));
        Assert.That(inven["text"], Is.EqualTo(Ms2.Quest.ClientFormat.Format(NewModel().Tables.Common(c.Table.FindByName("inven").Tooltip), key.Length > 0 ? "[" + key + "]" : "")));
        Assert.That(c.GetUserData("getTooltip", new object[] { "nosuchentry" }), Is.Null);

        var menus = h.Child("m_menus");
        var button = (AsDisplayObject)h.Invoke(h.Invoke(menus, "getChildByName", "option"), "getChildByName", "m_button");
        // the button's own art (its bounds also hold the closed drop-down)
        var hits = new List<(double X, double Y)>();
        for (double y = 400; y < 720; y += 2) for (double x = 900; x < 1280; x += 2)
            if (h.Player.MouseTarget(x, y) == button) hits.Add((x, y));
        Assert.That(hits, Is.Not.Empty);
        double hx = (hits.Min(p => p.X) + hits.Max(p => p.X)) / 2, hy = (hits.Min(p => p.Y) + hits.Max(p => p.Y)) / 2;
        h.Player.MouseMove(hx, hy);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); th.Player.Tick(); }
        Assert.That(h.Calls, Does.Contain("getUserData:getTooltip"));
        Assert.That(tc.Open, Has.Count.EqualTo(1), "createTooltip reached the tooltip movie");
        for (int i = 0; i < 30; i++) th.Player.Tick();
        var shown = Texts(th.Player.Stage).Select(t => t.Text).Where(t => t.Length > 0).ToList();
        TestContext.WriteLine("tooltip texts: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Select channels"));

        h.Player.MouseMove(1, 1);
        for (int i = 0; i < 3; i++) { h.Player.Tick(); th.Player.Tick(); }
        Assert.That(tc.Open, Is.Empty, "roll out destroys it");
    }
}
