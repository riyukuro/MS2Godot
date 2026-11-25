using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The massive event entrance notice (UIGlobalPortalDialog, uiglobalportaldialog.cpp) run by its movie: the GlobalPortal
/// packet's modes, the delayed open and its gates, the texts and buttons, and the enter send (needs MS2_DATA).</summary>
public class GlobalPortalDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    sealed class Rig
    {
        public GlobalPortalController C;
        public UiHost H;
        public Runtime RT;
        public Player P;
        public uint Now = 50_000;
        public int? Level = 30;
        public int MinLevel;
        public int Scene = GlobalPortalController.GameSceneId;
        public bool Open;
        public int Opens;
        public readonly List<IOutPacket> Sent = new();
        public readonly List<(string Text, int Type, bool Log)> Notices = new();
        public readonly List<string> Sounds = new();
    }

    static Rig New()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        if (packs == null)
        {
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        var g = new Rig();
        g.C = new GlobalPortalController
        {
            Tables = tables,
            NowMs = () => g.Now,
            RetryDelay = () => 4000,
            PlayerLevel = () => g.Level,
            MinLevel = () => g.MinLevel,
            SceneId = () => g.Scene,
            IsOpen = () => g.Open,
            OpenRequested = () => g.Opens++,
            Send = g.Sent.Add,
            Notice = (t, ty, l) => g.Notices.Add((t, ty, l)),
            PlaySound = g.Sounds.Add,
        };
        return g;
    }

    static void Load(Rig g)
    {
        var (rt, p) = src.Open(g.C.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        g.RT = rt; g.P = p;
        g.H = new UiHost(p, g.C, new UiModel(tables));
        g.Open = true;
        Assert.That(g.H.Load(), Is.True, g.C.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
    }

    static GlobalPortalPacket.Notice Notice(int id, int param, string c1, string c2, string c3) =>
        new(id, param, "s_massive_event_message", "System_Quiz_Global_Portal", c1, c2, c3);

    [Test]
    public void ANoticeOpensTheDialogAfterItsDelayOnlyInTheGameScene()
    {
        var g = New();
        g.C.OnPacket(Notice(0x13885, 0x2c, "s_massive_event_name_blood_mine", "", ""));
        Assert.That(g.C.RetryTime, Is.EqualTo(54_000u), "now + the random delay");
        Assert.That(g.C.Text, Is.EqualTo(tables.Common("s_massive_event_message")), "the key's common string");
        g.Now += 3999;
        g.C.Update();
        Assert.That(g.Opens, Is.Zero, "before the time");
        g.Now += 1;
        g.Level = null;
        g.C.Update();
        Assert.That((g.Opens, g.C.RetryTime), Is.EqualTo((0, 54_000u)), "no local player: it waits");
        g.Level = 30;
        g.C.Update();
        Assert.That((g.Opens, g.C.RetryTime), Is.EqualTo((1, 0u)), "opened once, disarmed");
        g.C.Update();
        Assert.That(g.Opens, Is.EqualTo(1));

        g.C.OnPacket(Notice(0x13886, 1, "s_massive_event_name_blood_mine", "", ""));
        g.Scene = 0x6200001;
        g.Now += 10_000;
        g.C.Update();
        Assert.That((g.Opens, g.C.RetryTime), Is.EqualTo((1, 0u)), "another scene: disarmed, not opened");
    }

    [Test]
    public void ALevelUnderTheMinimumDisarmsTheOpen()
    {
        var g = New();
        g.MinLevel = 40;
        g.C.OnPacket(Notice(5, 6, "s_massive_event_name_blood_mine", "", ""));
        g.Now += 5000;
        g.C.Update();
        Assert.That((g.Opens, g.C.RetryTime), Is.EqualTo((0, 0u)));
        g.MinLevel = 0;
        g.C.OnPacket(Notice(5, 7, "s_massive_event_name_blood_mine", "", ""));
        g.Now += 5000;
        g.C.Update();
        Assert.That(g.Opens, Is.EqualTo(1), "0 is no limit");
    }

    [Test]
    public void TheLoadShowsTheTextAndOneButtonPerCaptionAndAnEnterSendsTheEventAndButton()
    {
        var g = New();
        g.C.OnPacket(Notice(0x13885, 0x2c, "s_massive_event_name_blood_mine", "", "s_massive_event_name_crazy_runner"));
        Load(g);
        var text = g.H.Child("textField", g.H.Child("m_text"));
        Assert.That(g.H.Get(text, "text"), Is.EqualTo(tables.Common("s_massive_event_message")));
        string Label(string caption) => ClientFormat.Format(tables.Common("s_massive_event_enter_button") ?? "", tables.Common(caption) ?? "");
        Assert.That(g.C.ButtonLabels, Is.EqualTo(new[] { Label("s_massive_event_name_blood_mine"), "", Label("s_massive_event_name_crazy_runner") }));
        Assert.That(g.H.Get(g.H.Child("m_enterButton1"), "visible"), Is.EqualTo(true));
        Assert.That(g.H.Get(g.H.Child("m_enterButton2"), "visible"), Is.EqualTo(false));
        Assert.That(g.H.Get(g.H.Child("m_enterButton3"), "visible"), Is.EqualTo(true));
        Assert.That(g.H.Get(g.H.Child("m_enterButton3"), "label"), Is.EqualTo(g.C.ButtonLabels[2]));
        Assert.That(g.Sounds, Is.EqualTo(new[] { "System_Quiz_Global_Portal" }));
        g.C.ClickEnter(2);
        Assert.That(g.Sent, Is.EqualTo(new IOutPacket[] { new GlobalPortalEnter(0x13885, 2) }));
        for (int i = 0; i < 10; i++) g.P.Tick();
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void NoCaptionClosesTheDialogAndMode1ClosesAnOpenOne()
    {
        var g = New();
        g.C.OnPacket(Notice(9, 9, "", "", ""));
        Load(g);
        Assert.That(g.Sounds, Is.Empty, "the close comes before the sound");
        for (int i = 0; i < 120 && !g.C.IsClosed; i++) g.P.Tick();
        Assert.That(g.C.IsClosed, Is.True);

        var h = New();
        h.C.OnPacket(Notice(9, 9, "s_massive_event_name_blood_mine", "", ""));
        Load(h);
        h.C.OnPacket(new GlobalPortalPacket.End());
        Assert.That(h.C.EventId, Is.Zero);
        for (int i = 0; i < 120 && !h.C.IsClosed; i++) h.P.Tick();
        Assert.That(h.C.IsClosed, Is.True);
    }

    [Test]
    public void Modes3And4AreTheDeadAndLevelLimitNotices()
    {
        var g = New();
        g.C.OnPacket(new GlobalPortalPacket.Other(3));
        g.C.OnPacket(new GlobalPortalPacket.Other(4));
        g.C.OnPacket(new GlobalPortalPacket.Other(2));
        Assert.That(g.Notices, Is.EqualTo(new[]
        {
            (tables.Common("s_massive_event_error_cant_enter_dead"), 1, true),
            (tables.Common("s_massive_event_error_cant_enter_level_limit"), 1, true),
        }));
        Assert.That(g.Opens, Is.Zero);
    }
}
