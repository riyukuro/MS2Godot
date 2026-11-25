using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The field timer (WorldService FUN_141bc6f60 / FUN_141bc6fb0) and the trigger timer dialog (UITriggerTimerDialog,
/// uitriggertimerdialog.cpp) run by its movie (the movie tests need MS2_DATA).
/// </summary>
public class TriggerTimerDialogTests
{
    [Test]
    public void TheTimerReadsTheRemainingTimeAndClearsItsFlagWhileTimeRemains()
    {
        var t = new WorldTimer();
        Assert.That(t.Get(5000, out _, out _, out _, out _), Is.False, "no duration");
        t.Set(10_000, 125_340, 1);
        Assert.That(t.Get(0, out _, out _, out _, out int f0), Is.False, "no server tick");
        Assert.That(f0, Is.EqualTo(1));
        // before the start: the whole duration, the flag as set
        Assert.That(t.Get(9_000, out int m, out int s, out int h, out int f), Is.True);
        Assert.That((m, s, h, f), Is.EqualTo((2, 5, 34, 1)));
        // running: 125,340 - 20,000 = 105,340 ms left, the flag cleared
        t.Get(30_000, out m, out s, out h, out f);
        Assert.That((m, s, h, f), Is.EqualTo((1, 45, 34, 0)));
        // at the end and past it: zeros, the flag as set
        t.Get(135_340, out m, out s, out h, out f);
        Assert.That((m, s, h, f), Is.EqualTo((0, 0, 0, 1)));
        t.Get(200_000, out m, out s, out h, out f);
        Assert.That((m, s, h, f), Is.EqualTo((0, 0, 0, 1)));
    }

    [Test]
    public void TheComparisonsAreUnsigned()
    {
        var t = new WorldTimer();
        t.Set(-10, 1000, 0);          // a start past int.MaxValue as the client's unsigned tick
        t.Get(5, out int m, out int s, out _, out _);
        Assert.That((m, s), Is.EqualTo((0, 1)), "a start above the tick reads as not started: the whole duration");
    }

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    sealed class Rig
    {
        public TriggerTimerDialogController C;
        public UiHost H;
        public Runtime RT;
        public int Now = 1_000_000;
        public readonly List<string> Effects = new();
        public readonly List<int> Stopped = new();
    }

    static Rig Load(int start, int duration, int flag)
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
        g.C = new TriggerTimerDialogController
        {
            ServerTick = () => g.Now,
            PlayEffect = name => { g.Effects.Add(name); return 7; },
            StopEffect = g.Stopped.Add,
        };
        g.C.Timer.Set(start, duration, flag);
        var (rt, p) = src.Open(g.C.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        g.RT = rt;
        g.H = new UiHost(p, g.C, new UiModel(tables));
        Assert.That(g.H.Load(), Is.True, g.C.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return g;
    }

    [Test]
    public void TheDialogShowsTheMinutesAndSecondsAndStartsTheLowTimeEffectOnce()
    {
        var g = Load(1_000_000, 90_500, 1);
        Assert.That(g.C.IsClosed, Is.False);
        g.C.Update();
        Assert.That(g.C.ShownTime, Is.EqualTo("0130"));
        Assert.That(g.Effects, Is.Empty);
        g.Now += 80_000; g.C.Update();      // 10.5 s left
        Assert.That(g.C.ShownTime, Is.EqualTo("0010"));
        Assert.That(g.Effects, Is.EqualTo(new[] { tables.CommonResource(TriggerTimerDialogController.TimeLimitEffectKey) }));
        Assert.That(g.Effects[0], Is.Not.Empty, "the timeLimit effect is in the common resource table");
        TestContext.WriteLine("timeLimit: " + g.Effects[0]);
        g.Now += 5_000; g.C.Update();
        Assert.That(g.Effects, Has.Count.EqualTo(1), "once per load");
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void TheDialogClosesAtTheEndOnlyWithTheAutoRemoveFlag()
    {
        var g = Load(1_000_000, 5_000, 1);
        g.C.Update();
        g.Now += 6_000; g.C.Update();
        for (int i = 0; i < 120 && !g.C.IsClosed; i++) g.H.Player.Tick();     // the close tween ends with OnEndClose
        Assert.That(g.C.IsClosed, Is.True, "run out with the auto-remove flag");
        Assert.That(g.Stopped, Is.EqualTo(new[] { 7 }), "the close removes the effect");

        var k = Load(1_000_000, 5_000, 0);
        k.Now += 6_000; k.C.Update();
        Assert.That(k.C.ShownTime, Is.EqualTo("0000"), "without the flag it stays at 0000");
        Assert.That(k.Stopped, Is.Empty);
    }

    [Test]
    public void TheAlignAndOffsetLayTheEntityOutAgain()
    {
        var g = Load(1_000_000, 5_000, 0);
        double y0 = Rt.ToNumber(g.H.Get(g.H.Entity, "y"));
        g.C.SetOffsetY(40);
        double d40 = Rt.ToNumber(g.H.Get(g.H.Entity, "y")) - y0;
        g.C.SetOffsetY(80);
        double d80 = Rt.ToNumber(g.H.Get(g.H.Entity, "y")) - y0;
        Assert.That(d80 - d40, Is.EqualTo(40).Within(0.5), "the offset goes in below the top (offset + 0x82)");
        double x0 = Rt.ToNumber(g.H.Get(g.H.Entity, "x"));
        g.C.SetAlign(TriggerTimerDialogController.AlignTopRight);
        Assert.That(Rt.ToNumber(g.H.Get(g.H.Entity, "x")), Is.GreaterThan(x0), "TR goes to the right");
        Assert.That(g.RT.Errors, Is.Empty);
    }
}
