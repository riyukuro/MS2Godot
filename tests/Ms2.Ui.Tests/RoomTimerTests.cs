using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>RoomTimerManager (roomtimermanager.cpp) over a recording window, and UITimerDialog (uitimerdialog.cpp) run by its
/// movie (the movie tests need MS2_DATA).</summary>
public class RoomTimerTests
{
    sealed class Window : IRoomTimerWindow
    {
        public readonly List<string> Calls = new();
        public void SetTime(uint start, uint duration, int offset) => Calls.Add($"time {start} {duration} {offset}");
        public void ShowIncreaseEffect() => Calls.Add("increase");
        public void ShowDecreaseEffect() => Calls.Add("decrease");
        public void SetLap(int id, int time) => Calls.Add($"lap {id} {time}");
        public void Open() => Calls.Add("open");
        public void CloseNow() => Calls.Add("close");
    }

    static (RoomTimerManager M, Window W, List<(string, int)> Notices, List<RoomTimerMessage> Unapplied) Manager()
    {
        var w = new Window();
        var notices = new List<(string, int)>();
        var unapplied = new List<RoomTimerMessage>();
        var m = new RoomTimerManager
        {
            GetTimer = t => t == RoomTimerMessage.TimerDialog ? w : null,
            Common = id => "c" + id + " {0}",
            Notice = (t, k) => notices.Add((t, k)),
            Unapplied = unapplied.Add,
        };
        return (m, w, notices, unapplied);
    }

    static RoomTimerMessage Start(byte type, int start, int duration, int offset = 0, byte command = RoomTimerMessage.Start) =>
        new(command) { Type = type, StartTick = start, Duration = duration, Offset = offset };

    [Test]
    public void StartOpensTheTypesWindowAndStopClosesIt()
    {
        var (m, w, _, _) = Manager();
        m.Dispatch(Start(1, 100, 5000, 2));
        Assert.That(w.Calls, Is.EqualTo(new[] { "time 100 5000 2", "open" }));
        Assert.That(m.IsActive, Is.True);
        m.Dispatch(new RoomTimerMessage(RoomTimerMessage.Stop));
        Assert.That(w.Calls[^1], Is.EqualTo("close"));
        Assert.That((m.IsActive, m.Timer), Is.EqualTo((false, (IRoomTimerWindow)null)));
    }

    [Test]
    public void StartWithoutADurationOrTypeOpensNothingButRestartTakesAnyType()
    {
        var (m, w, _, un) = Manager();
        m.Dispatch(Start(1, 100, 0));
        m.Dispatch(Start(0, 100, 5000));
        Assert.That(w.Calls, Is.Empty);
        m.Dispatch(Start(2, 100, 5000, command: RoomTimerMessage.Restart));      // the gauge: no window here
        Assert.That(un, Has.Count.EqualTo(1));
        m.Dispatch(Start(1, 100, 5000, command: RoomTimerMessage.Restart));
        Assert.That(w.Calls, Is.EqualTo(new[] { "time 100 5000 0", "open" }));
    }

    [Test]
    public void UpdateMovesTheTimeAndShowsItsNotice()
    {
        var (m, w, notices, _) = Manager();
        m.Dispatch(new RoomTimerMessage(RoomTimerMessage.Update) { StartTick = 1, Duration = 10 });
        Assert.That(w.Calls, Is.Empty, "no window: nothing");
        m.Dispatch(Start(1, 100, 60_000, 3));
        w.Calls.Clear();
        m.Dispatch(new RoomTimerMessage(RoomTimerMessage.Update) { StartTick = 100, Duration = 90_000, NoticeType = 1, Delta = 30_000 });
        m.Dispatch(new RoomTimerMessage(RoomTimerMessage.Update) { StartTick = 100, Duration = 80_000, NoticeType = 1, Delta = -10_500 });
        m.Dispatch(new RoomTimerMessage(RoomTimerMessage.Update) { StartTick = 100, Duration = 70_000, NoticeType = 2 });
        m.Dispatch(new RoomTimerMessage(RoomTimerMessage.Update) { StartTick = 100, Duration = 70_000, NoticeType = 0, Delta = 5 });
        Assert.That(w.Calls, Is.EqualTo(new[] { "time 100 90000 3", "increase", "time 100 80000 3", "decrease", "time 100 70000 3", "increase", "time 100 70000 3" }),
            "the offset of the start is kept");
        Assert.That(notices, Is.EqualTo(new[] { ("c2463 30", 4), ("c2465 10", 2), ("c2464 {0}", 4) }));
        m.Dispatch(new RoomTimerMessage(RoomTimerMessage.Update) { StartTick = 100, Duration = 0 });
        Assert.That(w.Calls[^1], Is.EqualTo("close"));
        Assert.That(m.Timer, Is.Null);
    }

    [Test]
    public void ValuesGoToTheWindowInIdOrderKeepingTheFirstOfAnId()
    {
        var (m, w, _, _) = Manager();
        m.Dispatch(Start(1, 100, 5000));
        w.Calls.Clear();
        m.Dispatch(new RoomTimerMessage(RoomTimerMessage.Values) { Laps = new[] { (5, 50), (2, 20), (5, 99) } });
        Assert.That(w.Calls, Is.EqualTo(new[] { "lap 2 20", "lap 5 50" }));
    }

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    sealed class Rig
    {
        public TimerDialogController C;
        public UiHost H;
        public Runtime RT;
        public int Server = 1_000_000;
        public uint Ms = 50_000;
        public readonly List<string> Effects = new();
        public readonly List<int> Stopped = new();
        public void Step(int ms) { Server += ms; Ms += (uint)ms; C.Update(); }
    }

    static Rig Load(uint start, uint duration, int offset = 0)
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
        g.C = new TimerDialogController
        {
            ServerTick = () => g.Server,
            NowMs = () => g.Ms,
            PlayEffect = n => { g.Effects.Add(n); return 9; },
            StopEffectOf = g.Stopped.Add,
        };
        g.C.SetTime(start, duration, offset);
        var (rt, p) = src.Open(g.C.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        g.RT = rt;
        g.H = new UiHost(p, g.C, new UiModel(tables));
        Assert.That(g.H.Load(), Is.True, g.C.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return g;
    }

    [Test]
    public void TheDialogResetsThenSetsTheTimeEvery100Ms()
    {
        var g = Load(1_000_000, 75_000);
        g.Step(0);
        Assert.That((g.C.LastTimeCall, g.C.ShownTime), Is.EqualTo(("resetTime", "0115")));
        g.Step(50);                                 // within 100 ms: not checked
        Assert.That(g.C.ShownTime, Is.EqualTo("0115"));
        g.Step(1_000);
        Assert.That((g.C.LastTimeCall, g.C.ShownTime), Is.EqualTo(("setTime", "0113")));
        g.C.SetTime(1_000_000, 75_000, 0);         // a new time: the next change is a reset
        g.Step(1_000);
        Assert.That(g.C.LastTimeCall, Is.EqualTo("resetTime"));
        g.C.ShowIncreaseEffect();
        g.C.ShowDecreaseEffect();
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void TheEffectRunsAtTenSecondsAndStopsWhenTimeIsAddedBack()
    {
        var g = Load(1_000_000, 12_000);
        g.Step(0);
        Assert.That(g.Effects, Is.Empty);
        g.Step(1_500);                              // 10.5 s left
        Assert.That(g.Effects, Has.Count.EqualTo(1));
        Assert.That(g.C.EffectRunning, Is.True);
        g.C.SetTime(1_000_000, 60_000, 0);         // time added
        g.Step(200);
        Assert.That(g.Stopped, Is.EqualTo(new[] { 9 }));
        Assert.That(g.C.EffectRunning, Is.False);
    }

    [Test]
    public void ThePositiveOffsetAddsItsSecondsToTheTick()
    {
        var g = Load(1_000_000, 75_000, 5);
        g.Step(0);
        Assert.That(g.C.ShownTime, Is.EqualTo("0110"));
    }

    [Test]
    public void CloseNowClosesWithoutTheTween()
    {
        var g = Load(1_000_000, 75_000);
        g.C.CloseNow();
        Assert.That(g.C.IsClosed, Is.True, "closed at once");
    }
}
