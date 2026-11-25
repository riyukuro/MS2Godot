using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The side popup talk pane (UISidePopupTalk, uisidepopuptalk_1.cpp) run by its movie: a popup shown before the
/// load waits for it, addPopup's members, kind 1's minimap and quest guide, the voice and sound calls (needs MS2_DATA).</summary>
public class SidePopupTalkTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    sealed class Rig
    {
        public SidePopupTalkController C;
        public UiHost H;
        public Runtime RT;
        public Player P;
        public int Loads;
        public readonly List<bool> Visible = new();
        public readonly List<string> Voices = new(), Sounds = new();
        public readonly List<int> Stopped = new();
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
        int next = 40;
        g.C = new SidePopupTalkController
        {
            LoadRequested = () => g.Loads++,
            ReplaceObjectAndValueWords = s => s.Replace("$x$", "X"),
            SoundState = () => (false, 0.5f),
            MinimapAndQuestGuideVisible = g.Visible.Add,
            PlayVoice = v => { g.Voices.Add(v); return ++next; },
            StopEffect = g.Stopped.Add,
            SystemSound = g.Sounds.Add,
            ServerTime = () => 1000,
        };
        return g;
    }

    static void Load(Rig g)
    {
        var (rt, p) = src.Open(g.C.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        g.RT = rt; g.P = p;
        g.H = new UiHost(p, g.C, new UiModel(tables));
        Assert.That(g.H.Load(), Is.True, g.C.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
    }

    static SidePopupTalkParam Param(int kind) => new(kind, "", "npc_illust", 4000, "voice_a", "sound_a", "hello $x$");

    [Test]
    public void APopupBeforeTheLoadWaitsAndTheLoadAddsIt()
    {
        var g = New();
        g.C.ShowPopup(Param(0));
        Assert.That((g.C.Pending, g.Loads), Is.EqualTo((1, 1)), "waits and asks for the load");
        Load(g);
        Assert.That(g.C.Pending, Is.Zero);
        var m = g.C.LastPopup;
        Assert.That(m["type"], Is.EqualTo(0));
        Assert.That(m["msg"], Is.EqualTo("hello X"), "through ReplaceObjectAndValueWords");
        Assert.That((m["illust"], m["duration"], m["voice"], m["sound"]), Is.EqualTo(("npc_illust", 4000, "voice_a", "sound_a")));
        Assert.That(m["volume"], Is.EqualTo(0.5));
        Assert.That(g.Visible, Is.Empty, "kind 0 leaves the minimap");
        for (int i = 0; i < 10; i++) g.P.Tick();
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void KindOneHidesTheMinimapUntilItsHideComplete()
    {
        var g = New();
        Load(g);
        g.C.ShowPopup(Param(1));
        Assert.That(g.Loads, Is.Zero, "loaded: added at once");
        Assert.That(g.Visible, Is.EqualTo(new[] { false }));
        Assert.That(g.C.OnCallback("onHideComplete", new object[] { 0 }), Is.True);
        Assert.That(g.C.OnCallback("onHideComplete", new object[] { 1 }), Is.True);
        Assert.That(g.Visible, Is.EqualTo(new[] { false, true }));
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void AVoiceKeyStopsItsLastVoiceAndTheSoundPlays()
    {
        var g = New();
        Load(g);
        g.C.OnCallback("onPlayVoice", new object[] { 2, "v1" });
        g.C.OnCallback("onPlayVoice", new object[] { 2, "v2" });
        g.C.OnCallback("onPlayVoice", new object[] { 2, "" });
        Assert.That(g.Voices, Is.EqualTo(new[] { "v1", "v2" }));
        Assert.That(g.Stopped, Is.EqualTo(new[] { 41, 42 }));
        g.C.OnCallback("onPlaySound", new object[] { 0, "snd" });
        Assert.That(g.Sounds, Is.EqualTo(new[] { "snd" }));
        Assert.That(g.C.GetUserValue("getExpireDuration", new object[] { "1300" }), Is.EqualTo(300));
    }
}
