using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIHomeInsideOptionDialog (uihomeinsideoptiondialog.cpp) on its movie (needs MS2_DATA).</summary>
public class HomeInsideOptionDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static void Setup()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= TestData.Packs(d);
        src ??= TestData.Movies(packs);
        tables ??= new GameTables(packs, "en");
    }

    sealed class Env : IHomeInsideOptionHost
    {
        public UgcHomeOptionData Data { get; set; }
        public (byte Background, byte Light, byte Camera) Setting { get; set; }
        public readonly List<(byte, byte)> Sent = new();
        public readonly List<(int Type, string Text, string Tag)> Boxes = new();
        public int Nexts;
        public void Send(byte mode, byte index) => Sent.Add((mode, index));
    }

    sealed class Dlg(IHomeInsideOptionHost h) : HomeInsideOptionDialogController(h)
    {
        public int Closed;
        public override void Close() => Closed++;
    }

    static (Dlg c, Env e) Open(byte bg = 0, byte light = 0, byte camera = 0)
    {
        Setup();
        var e = new Env { Data = new UgcHomeOptionData(packs, id => "n" + id), Setting = (bg, light, camera) };
        var c = new Dlg(e) { Str = k => "<" + k + ">" };
        c.ShowBox = b => e.Boxes.Add((b.Type, b.Msg, b.Extra));
        c.NextBox = () => e.Nexts++;
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        return (c, e);
    }

    static int Selected(HomeInsideOptionDialogController c, string name) => Rt.ToInt32(c.Host.Get(c.Host.Child(name), "selectedIndex"));
    static int Rows(HomeInsideOptionDialogController c, string name)
    {
        var dp = c.Host.Get(c.Host.Child(name), "dataProvider");
        return Rt.ToInt32(c.Host.Get(dp, "length"));
    }

    [Test]
    public void TheCombosListTheTablesAndSelectTheSetting()
    {
        var (c, e) = Open(2, 1, 3);
        Assert.That(Rows(c, "m_background"), Is.EqualTo(e.Data.Backgrounds.Count));
        Assert.That(Rows(c, "m_light"), Is.EqualTo(e.Data.Lights.Count));
        Assert.That(Rows(c, "m_camera"), Is.EqualTo(e.Data.Cameras.Count));
        Assert.That((Selected(c, "m_background"), Selected(c, "m_light"), Selected(c, "m_camera")), Is.EqualTo((2, 1, 3)));
    }

    [Test]
    public void OnApplyAsksAndYesSendsTheThreeSelections()
    {
        var (c, e) = Open(1, 2, 3);
        c.OnCallback("onapply", Array.Empty<object>());
        Assert.That(e.Boxes, Is.EqualTo(new[] { (1, "<s_home_option_msg_confirm>", "OnApply") }));
        Assert.That(e.Sent, Is.Empty);
        c.OnMessageBox(null, 1, "OnApply");
        Assert.That(e.Sent, Is.Empty);
        c.OnMessageBox(null, 0, "OnApply");
        Assert.That(e.Sent, Is.EqualTo(new[] { ((byte) 0x33, (byte) 1), ((byte) 0x34, (byte) 2), ((byte) 0x36, (byte) 3) }));
        Assert.That(Rt.ToBoolean(c.Host.Get(c.Host.Child("m_apply"), "enabled")), Is.False);
        Assert.That(e.Nexts, Is.EqualTo(2));
    }

    [Test]
    public void OnConfirmClosesWithoutAskingWhileApplyIsDisabled()
    {
        var (c, e) = Open();
        c.Host.Set(c.Host.Child("m_apply"), "enabled", false);
        c.OnCallback("OnConfirm", Array.Empty<object>());
        Assert.That(e.Boxes, Is.Empty);
        Assert.That(c.Closed, Is.EqualTo(1));
        Assert.That(e.Sent, Is.Empty);
    }

    [Test]
    public void OnConfirmAsksWhileApplyIsEnabledAndYesSendsThenCloses()
    {
        var (c, e) = Open(0, 0, 1);
        c.Host.Set(c.Host.Child("m_apply"), "enabled", true);
        c.OnCallback("OnConfirm", Array.Empty<object>());
        Assert.That(e.Boxes.Select(b => b.Tag), Is.EqualTo(new[] { "OnConfirm" }));
        c.OnMessageBox(null, 0, "OnConfirm");
        Assert.That(e.Sent.Count, Is.EqualTo(3));
        Assert.That(c.Closed, Is.EqualTo(1));
    }
}
