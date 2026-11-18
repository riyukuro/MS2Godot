using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The character window (uimyinfodialog) run by its own ActionScript with UIMyInfoDialog's C++ side (needs MS2_DATA).</summary>
public class MyInfoTests
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

    static (UiHost, MyInfoController, MyCharacter, List<string>) Load(Action<MyInfoController> setup = null)
    {
        var model = NewModel();
        model.HasLocalPlayer = true;
        var me = UiFixture.Character();
        var c = new MyInfoController { Record = () => me.ToPcInfo(0), StatPoints = me.StatPoints };
        setup?.Invoke(c);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) { p.Tick(); c.Process(i * 16); }
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, me, rt.Errors);
    }

    static List<object> Items(UiHost h, object dp) =>
        Enumerable.Range(0, (int)Rt.ToNumber(h.Get(dp, "length"))).Select(i => h.Invoke(dp, "requestItemAt", i)).ToList();

    [Test]
    public void TabsAreTheClientsTabList()
    {
        Packs();
        var (h, c, _, errors) = Load();
        Assert.That(errors, Is.Empty);
        var tabs = Items(h, h.Get(h.Child("m_tab"), "dataProvider"));
        var labels = tabs.Select(t => Rt.ToString(h.Get(t, "label"))).ToList();
        TestContext.WriteLine(string.Join(", ", labels));
        bool codi = c.Model.IsFeatureEnabled(0x8e);
        var expect = new List<string> { "$s_myInfo_game", "$s_myInfo_skin" };
        if (codi) expect.Add("$s_myInfo_codi");
        expect.Add("$s_myInfo_badge");
        Assert.That(labels, Is.EqualTo(expect));
        Assert.That(tabs.Select(t => (int)Rt.ToNumber(h.Get(t, "tabIndex"))), Is.EqualTo(codi ? new[] { 0, 1, 3, 2 } : new[] { 0, 1, 2 }));
        Assert.That(c.ShownBag, Is.EqualTo(MyInfoController.EquipBag));
        c.OnCallback("onChangeTab", new object[] { 2 });
        Assert.That(c.ShownBag, Is.EqualTo(MyInfoController.GemBag));
    }

    [Test]
    public void ProfileAndStatPointsComeFromTheRecord()
    {
        Packs();
        var (h, c, _, _) = Load();
        Assert.That(Rt.ToString(h.Get(h.Child("m_level"), "text")), Is.EqualTo("21"));
        Assert.That(Rt.ToString(h.Get(h.Child("m_job"), "text")), Is.EqualTo(c.Model.Tables.JobName(100)));
        // the six cost stats: name, Stats2 (health: Stats0), orange where points were put
        var row1 = (AsDisplayObject)h.Child("m_statPoint1");
        Assert.That(Rt.ToString(h.Get(h.FindNamed("m_Label", row1), "text")), Is.EqualTo(c.Model.Tables.StatName(0)));
        Assert.That(Rt.ToString(h.Get(h.FindNamed("m_point", row1), "text")), Is.EqualTo("48"));
        Assert.That(Rt.ToNumber(h.Get(h.FindNamed("m_point", row1), "textColor")), Is.EqualTo(0xffa200));
        var row5 = (AsDisplayObject)h.Child("m_statPoint5");
        Assert.That(Rt.ToString(h.Get(h.FindNamed("m_point", row5), "text")), Is.EqualTo("2100"), "health shows Stats0");
        Assert.That(Rt.ToNumber(h.Get(h.FindNamed("m_point", row5), "textColor")), Is.EqualTo(0xffffff));
        var remain = h.FindNamed("m_remainStatPoint", (AsDisplayObject)h.Child("m_remainPoint"));
        Assert.That(Rt.ToString(h.Get(remain, "text")), Is.EqualTo("3"));
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_resetStatPoint"), "enabled")), Is.True);
        // defense without feature 0xdf: Stats2[0x14]
        if (!c.Model.IsFeatureEnabled(0xdf))
            Assert.That(Rt.ToString(h.Get(h.FindNamed("value", (AsDisplayObject)h.Child("m_defenseInfo")), "text")), Is.EqualTo("41"));
    }

    [Test]
    public void CodiTabWithoutAPeriodShowsTheBoxAndOkGoesBack()
    {
        Packs();
        MessageBoxData shown = null;
        var (h, c, me, _) = Load(c => c.ShowBox = b => shown = b);
        if (!c.Model.IsFeatureEnabled(0x8e)) Assert.Ignore("feature 0x8e is off: no codi tab");
        var before = MessageBoxData.CommonByKey;
        MessageBoxData.CommonByKey = k => k == MyInfoController.CodiTabNotActiveKey ? "not active" : "";
        try { Assert.That(c.OnCallback("onChangeTab", new object[] { 3 }), Is.False); }
        finally { MessageBoxData.CommonByKey = before; }
        Assert.That(c.Tab, Is.EqualTo(3));
        Assert.That(shown?.Msg, Is.EqualTo("not active"));
        Assert.That(shown.Type, Is.EqualTo(0));
        shown.Callback(0, shown);
        Assert.That(Rt.ToNumber(h.Get(h.Child("m_tab"), "selectedIndex")), Is.EqualTo(0));
    }

    [Test]
    public void RequestsAreOnlyRaised()
    {
        Packs();
        var (_, c, _, _) = Load();
        var raised = new List<string>(); var toggled = new List<int>();
        c.Button += raised.Add; c.ToggleDialog += toggled.Add;
        Assert.That(c.OnCallback("OnAddStatPoint", new object[] { 1 }), Is.True);
        Assert.That(c.OnCallback("OnResetStatPoint", Array.Empty<object>()), Is.True);
        Assert.That(c.OnCallback("OnTitleChange", new object[] { 0 }), Is.False);
        Assert.That(c.OnCallback("onSymbolClick", Array.Empty<object>()), Is.False);
        Assert.That(raised, Is.EqualTo(new[] { "OnAddStatPoint", "OnResetStatPoint", "OnTitleChange" }));
        Assert.That(toggled, Is.EqualTo(new[] { 0x4ed5 }));
    }
}
