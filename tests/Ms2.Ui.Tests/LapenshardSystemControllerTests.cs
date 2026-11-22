using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The lapenshard window's tab shell (uilapenshardsystemdialog) run by its own ActionScript (needs MS2_DATA).</summary>
public class LapenshardSystemControllerTests
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

    sealed class Page : LapenshardTabPage
    {
        readonly string name, label;
        public readonly List<string> Log = new();
        public Page(string name, string label) { this.name = name; this.label = label; }
        public override string Name => name;
        public override string LabelKey => label;
        public override void Show(UiHost host, object root) => Log.Add("show " + (root != null));
        public override void Leave() => Log.Add("leave");
        public override void Notify(int ev) => Log.Add("event " + ev);
        public override bool OnCallback(string n, object[] a) { if (n == "PageOnly") { Log.Add("callback"); return true; } return false; }
    }

    static (UiHost, LapenshardSystemController, Page equip, Page upgrade) Load()
    {
        var model = new UiModel(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = true };
        var c = new LapenshardSystemController { Text = k => k };
        var equip = new Page("equip", "s_lapenshard_tabname_equip");
        var upgrade = new Page("upgrade", "s_lapenshard_tabname_upgrade");
        c.AddTab(equip); c.AddTab(upgrade);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 40_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return (h, c, equip, upgrade);
    }

    [Test]
    public void TheTabStripHoldsOneRowPerPageAndTheFirstPageIsShown()
    {
        var (h, c, equip, upgrade) = Load();
        var bar = h.Child("m_buttonBar");
        var dp = h.Get(bar, "dataProvider");
        Assert.That(Rt.ToInt32(h.Get(dp, "length")), Is.EqualTo(2));
        Assert.That(Rt.ToInt32(h.Get(bar, "selectedIndex")), Is.EqualTo(0));
        Assert.That(c.Page, Is.SameAs(equip), "OnChangeTab(0, root) made the equip page current");
        Assert.That(equip.Log, Is.EqualTo(new[] { "show True" }));
        Assert.That(upgrade.Log, Is.Empty);
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_webGuideButton"), "visible")), Is.False);
    }

    [Test]
    public void ChoosingTheSecondTabLeavesTheFirstPage()
    {
        var (h, c, equip, upgrade) = Load();
        h.Set(h.Child("m_buttonBar"), "selectedIndex", 1);
        for (int i = 0; i < 5; i++) h.Player.Tick();
        Assert.That(c.Page, Is.SameAs(upgrade));
        Assert.That(equip.Log, Is.EqualTo(new[] { "show True", "leave" }));
        Assert.That(upgrade.Log, Is.EqualTo(new[] { "show True" }));
    }

    [Test]
    public void ACallbackGoesToThePageFirstAndATabPastTheListIsIgnored()
    {
        var (h, c, equip, _) = Load();
        Assert.That(c.OnCallback("PageOnly", Array.Empty<object>()), Is.True);
        Assert.That(equip.Log, Does.Contain("callback"));
        c.ShowTabPage(5, null);
        Assert.That(c.Page, Is.SameAs(equip));
        c.NotifyPage(10);
        Assert.That(equip.Log[^1], Is.EqualTo("event 10"));
    }

    [Test]
    public void TheWindowIsGatedByFeature0xd3()
    {
        var (_, c, _, _) = Load();
        Assert.That(c.IsAvailable, Is.EqualTo(c.Model.IsFeatureEnabled(0xd3)));
        Assert.That(LapenshardSystemController.FeatureId, Is.EqualTo(0xd3));
    }
}
