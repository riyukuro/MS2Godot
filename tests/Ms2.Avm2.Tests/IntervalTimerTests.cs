using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// The movie's interval timers (setTimeout, setInterval, flash.utils.Timer) as MovieImpl::Advance runs them
/// (sfr_gfx_01i.cpp:3986-4022): the elapsed time moves by what the host hands the advance, every timer that is due fires
/// once (IntervalTimer::Invoke, sfr_as3_18.cpp:7219), missed intervals are skipped, and all of it comes before the frame's
/// enterFrame. A Timer is one more entry on the same list (Timer::start / ExecuteEvent, sfr_as3_14.cpp:863-960).
/// </summary>
public class IntervalTimerTests
{
    static (Runtime, Player) Movie()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["fps"] = 30, ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        Assert.That(p.Start(), Is.True);
        return (rt, p);
    }

    static AsFunction Fn(Runtime rt, Action a) => new AsPlainFunction(rt, new NativeMethod("f", (s, args) => { a(); return Rt.Undef; }));

    static AsTimer NewTimer(Runtime rt, double delay, int repeat) => (AsTimer)rt.GetClass("flash.utils.Timer").Construct(delay, repeat);

    [Test]
    public void ATimerFiresOncePerAdvanceNotOncePerMissedDelay()
    {
        // a 10 ms Timer on a 30 fps movie: each tick is 33 ms, three delays, and it fires once (the next due time skips
        // the missed ones), not three times
        var (rt, p) = Movie();
        var t = NewTimer(rt, 10, 0);
        int fires = 0;
        t.addEventListener("timer", Fn(rt, () => fires++));
        t.start();
        for (int i = 0; i < 3; i++) p.Tick();
        Assert.That(fires, Is.EqualTo(3), "one fire per tick");
        Assert.That(t.currentCount, Is.EqualTo(3));
    }

    [Test]
    public void ASetIntervalSkipsTheIntervalsMissedInALongAdvance()
    {
        // setInterval(f, 100): one advance of 1 s fires it once; the next due time is on its 100 ms grid past now (1.1 s),
        // so the misses are not replayed on the next advances
        var (rt, p) = Movie();
        int fires = 0;
        rt.AddTimeout(Fn(rt, () => fires++), 100, Array.Empty<object>(), true);
        p.Advance(1000, 0);
        Assert.That(fires, Is.EqualTo(1));
        p.Advance(50, 0);
        Assert.That(fires, Is.EqualTo(1), "1.05 s: before the next grid time");
        p.Advance(50, 0);
        Assert.That(fires, Is.EqualTo(2), "1.1 s");
    }

    [Test]
    public void TimersFireBeforeTheFramesEnterFrame()
    {
        var (rt, p) = Movie();
        var order = new List<string>();
        p.Root.addEventListener("enterFrame", Fn(rt, () => order.Add("frame")));
        var t = NewTimer(rt, 30, 1);
        t.addEventListener("timer", Fn(rt, () => order.Add("timer")));
        t.start();
        rt.AddTimeout(Fn(rt, () => order.Add("timeout")), 30, Array.Empty<object>(), false);
        p.Tick();
        Assert.That(order, Is.EqualTo(new[] { "timer", "timeout", "frame" }), "the list order, then the frame");
    }

    [Test]
    public void AnAdvanceWithNoFrameStillRunsTheTimers()
    {
        // a game frame shorter than the movie's frame time runs no movie frame, but its time reaches the timers
        var (rt, p) = Movie();
        int frames = 0, fires = 0;
        p.Root.addEventListener("enterFrame", Fn(rt, () => frames++));
        rt.AddTimeout(Fn(rt, () => fires++), 10, Array.Empty<object>(), false);
        p.Advance(16, 0);
        Assert.That((fires, frames), Is.EqualTo((1, 0)));
    }

    [Test]
    public void AZeroIntervalIsFlooredAtATenthOfTheFrameTime()
    {
        // setInterval(f, 0) at 30 fps: the first fire is due at once, later ones every 3.333 ms (frameTime / 10), so ten
        // 1 ms advances fire it at 1, 4, 7 and 10 ms
        var (rt, p) = Movie();
        int fires = 0;
        rt.AddTimeout(Fn(rt, () => fires++), 0, Array.Empty<object>(), true);
        for (int i = 0; i < 10; i++) p.AdvanceTime(1);
        Assert.That(fires, Is.EqualTo(4));
    }

    [Test]
    public void ATimerAddedByACallbackFirstRunsAtTheNextAdvance()
    {
        var (rt, p) = Movie();
        int inner = 0;
        rt.AddTimeout(Fn(rt, () => rt.AddTimeout(Fn(rt, () => inner++), 0, Array.Empty<object>(), false)), 0, Array.Empty<object>(), false);
        p.AdvanceTime(1);
        Assert.That(inner, Is.Zero, "not in the advance that added it");
        p.AdvanceTime(1);
        Assert.That(inner, Is.EqualTo(1));
    }

    [Test]
    public void AClearedTimerDoesNotFireLaterInTheSameAdvance()
    {
        var (rt, p) = Movie();
        int second = 0, id = 0;
        rt.AddTimeout(Fn(rt, () => rt.ClearTimeout(id)), 10, Array.Empty<object>(), false);
        id = rt.AddTimeout(Fn(rt, () => second++), 10, Array.Empty<object>(), true);
        p.AdvanceTime(20);
        p.AdvanceTime(20);
        Assert.That(second, Is.Zero);
    }

    [Test]
    public void ACallbackThatThrowsDoesNotStopTheOthers()
    {
        // Invoke prints and drops the function's exception; the loop goes on to the next timer
        var (rt, p) = Movie();
        int after = 0;
        rt.AddTimeout(Fn(rt, () => throw new AsThrow("boom")), 10, Array.Empty<object>(), false);
        rt.AddTimeout(Fn(rt, () => after++), 10, Array.Empty<object>(), false);
        p.AdvanceTime(20);
        Assert.That(after, Is.EqualTo(1));
    }

    [Test]
    public void ARepeatForeverTimerSendsTimerCompleteAfterEachTimerEvent()
    {
        // ExecuteEvent tests CurrentCount >= RepeatCount with no check for 0, so new Timer(1000) sends timerComplete after
        // its first timer event, still running; the inventory's sort button (onClickRange) is enabled again on it
        var (rt, p) = Movie();
        var t = NewTimer(rt, 100, 0);
        var seen = new List<string>();
        t.addEventListener("timer", Fn(rt, () => seen.Add("timer")));
        t.addEventListener("timerComplete", Fn(rt, () => seen.Add("complete " + t.running + " " + t.currentCount)));
        t.start();
        p.Advance(100, 0);
        p.Advance(100, 0);
        Assert.That(seen, Is.EqualTo(new[] { "timer", "complete True 1", "timer", "complete True 2" }));
        Assert.That(t.running, Is.True, "a repeat count of 0 keeps it going");
    }

    [Test]
    public void ATimerWithARepeatCountStopsAtIt()
    {
        var (rt, p) = Movie();
        var t = NewTimer(rt, 10, 2);
        var seen = new List<string>();
        t.addEventListener("timer", Fn(rt, () => seen.Add("timer " + t.currentCount)));
        t.addEventListener("timerComplete", Fn(rt, () => seen.Add("complete")));
        t.start();
        for (int i = 0; i < 4; i++) p.Tick();
        Assert.That(seen, Is.EqualTo(new[] { "timer 1", "timer 2", "complete" }));
        Assert.That((t.running, t.currentCount), Is.EqualTo((false, 2)));
        t.start();
        p.Tick();
        Assert.That((t.running, t.currentCount), Is.EqualTo((false, 2)), "a start at the count sends nothing more and stops");
        Assert.That(seen, Has.Count.EqualTo(3));
        t.reset();
        t.start();
        p.Tick();
        Assert.That(t.currentCount, Is.EqualTo(1), "reset counts from 0 again");
    }

    [Test]
    public void ADelaySetWhileRunningTakesEffectAtTheNextStart()
    {
        // delaySet only stores the value; the running core keeps the delay it was started with
        var (rt, p) = Movie();
        var t = NewTimer(rt, 100, 0);
        int fires = 0;
        t.addEventListener("timer", Fn(rt, () => fires++));
        t.start();
        t.delay = 10;
        for (int i = 0; i < 4; i++) p.Tick();          // 133 ms
        Assert.That(fires, Is.EqualTo(1), "still on 100 ms");
        t.stop();
        t.start();
        for (int i = 0; i < 2; i++) p.Tick();
        Assert.That(fires, Is.EqualTo(3), "the new delay after the restart");
        Assert.That(t.currentCount, Is.EqualTo(3), "the count goes on from the stop");
    }

    [Test]
    public void StopInsideTheTimerEventSendsNoMoreEvents()
    {
        var (rt, p) = Movie();
        var t = NewTimer(rt, 10, 0);
        int fires = 0;
        t.addEventListener("timer", Fn(rt, () => { fires++; t.stop(); }));
        t.start();
        for (int i = 0; i < 3; i++) p.Tick();
        Assert.That((fires, t.running, t.currentCount), Is.EqualTo((1, false, 1)));
    }

    [Test]
    public void ANegativeRepeatCountRepeatsLikeAVeryLargeOne()
    {
        // the core keeps the repeat count unsigned (IntervalTimer(Timer*, unsigned delay, unsigned curCount, unsigned
        // repeatCount), sfr_as3_15.cpp), so new Timer(10, -1) keeps firing; ExecuteEvent compares the object's SInt32
        // repeatCount, so each fire also sends timerComplete
        var (rt, p) = Movie();
        var t = NewTimer(rt, 10, -1);
        var seen = new List<string>();
        t.addEventListener("timer", Fn(rt, () => seen.Add("timer " + t.currentCount)));
        t.addEventListener("timerComplete", Fn(rt, () => seen.Add("complete")));
        t.start();
        for (int i = 0; i < 2; i++) p.Tick();
        Assert.That(seen, Is.EqualTo(new[] { "timer 1", "complete", "timer 2", "complete" }));
        Assert.That(t.running, Is.True);
    }

    [Test]
    public void ACallbackThatAdvancesTheMovieAgainKeepsTheRunningPass()
    {
        // a callback that runs the movie's advance again (a nested timer pass) and stopped another timer first: the nested
        // pass must not take the stopped one off under the outer pass, which goes on over its own count of entries
        var (rt, p) = Movie();
        bool nested = false;
        int b = 0, c = 0, cId = 0;
        rt.AddTimeout(Fn(rt, () => { if (nested) return; nested = true; rt.ClearTimeout(cId); p.AdvanceTime(0); }), 10, Array.Empty<object>(), true);
        rt.AddTimeout(Fn(rt, () => b++), 10, Array.Empty<object>(), true);
        cId = rt.AddTimeout(Fn(rt, () => c++), 10, Array.Empty<object>(), true);
        Assert.DoesNotThrow(() => p.AdvanceTime(20));
        Assert.That((b, c), Is.EqualTo((1, 0)), "the second fired once (in the nested pass), the cleared one never");
        p.AdvanceTime(10);
        Assert.That((b, c), Is.EqualTo((2, 0)));
    }

    static object Global(Runtime rt, string name, params object[] args) => ((AsFunction)rt.GetGlobal("flash.utils." + name)).Call(null, args);

    [Test]
    public void AnIntervalThatClearsItselfInItsCallbackFiresOnce()
    {
        // clearInterval of the running timer only marks it (ClearIntervalTimer, sfr_gfx_01i.cpp:1127); Invoke then sets its
        // next time, but a stopped timer is not invoked again and the pass takes it off
        var (rt, p) = Movie();
        int fires = 0, id = 0;
        id = Rt.ToInt32(Global(rt, "setInterval", Fn(rt, () => { fires++; Global(rt, "clearInterval", id); }), 10));
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(fires, Is.EqualTo(1));
    }

    [Test]
    public void ResetAndStartInsideTheTimerListenerRunFromANewCore()
    {
        // reset() in the "timer" listener stops the core the advance is invoking and sets the count to 0; ExecuteEvent then
        // tests that count (sfr_as3_14.cpp:864), and start() puts a new core at the end of the list, first due one delay
        // after now: the old core sends nothing more and the new one counts from 1
        var (rt, p) = Movie();
        var t = NewTimer(rt, 10, 3);
        var seen = new List<string>();
        bool restarted = false;
        t.addEventListener("timer", Fn(rt, () =>
        {
            seen.Add("timer " + t.currentCount);
            if (!restarted) { restarted = true; t.reset(); t.start(); }
        }));
        t.addEventListener("timerComplete", Fn(rt, () => seen.Add("complete")));
        t.start();
        for (int i = 0; i < 6; i++) p.Tick();
        Assert.That(seen, Is.EqualTo(new[] { "timer 1", "timer 1", "timer 2", "timer 3", "complete" }));
        Assert.That((t.running, t.currentCount), Is.EqualTo((false, 3)));

        // a repeat-forever Timer reset in its listener: the count is 0 when ExecuteEvent tests it, so timerComplete still
        // comes, and the timer is stopped
        var u = NewTimer(rt, 10, 0);
        var seenU = new List<string>();
        u.addEventListener("timer", Fn(rt, () => { seenU.Add("timer"); u.reset(); }));
        u.addEventListener("timerComplete", Fn(rt, () => seenU.Add("complete " + u.running + " " + u.currentCount)));
        u.start();
        for (int i = 0; i < 3; i++) p.Tick();
        Assert.That(seenU, Is.EqualTo(new[] { "timer", "complete False 0" }));
    }

    [Test]
    public void ANaNDelayIsZeroAndANegativeOneIsAVeryLongOne()
    {
        // setTimeout's delay is Convert2UInt32 (NaN -> 0, -1 -> 4294967295 ms); Timer.start takes (unsigned)(SInt64)delay
        // (NaN -> 0, -5 -> 2^32 - 5 ms)
        var (rt, p) = Movie();
        int nanTimeout = 0, negTimeout = 0, nanTimer = 0, negTimer = 0;
        Global(rt, "setTimeout", Fn(rt, () => nanTimeout++), double.NaN);
        Global(rt, "setTimeout", Fn(rt, () => negTimeout++), -1.0);
        var a = NewTimer(rt, double.NaN, 1);
        a.addEventListener("timer", Fn(rt, () => nanTimer++));
        a.start();
        var b = NewTimer(rt, -5, 1);
        b.addEventListener("timer", Fn(rt, () => negTimer++));
        b.start();
        p.AdvanceTime(1);
        Assert.That((nanTimeout, nanTimer), Is.EqualTo((1, 1)), "due at once");
        p.Advance(1_000_000, 0);
        Assert.That((negTimeout, negTimer), Is.EqualTo((0, 0)), "about 49.7 days away");
        Assert.That(b.running, Is.True);
    }

    [Test]
    public void AdvancingTimersThatAreNotDueAllocatesNothing()
    {
        var (rt, p) = Movie();
        rt.AddTimeout(Fn(rt, () => { }), 100_000, Array.Empty<object>(), true);
        NewTimer(rt, 100_000, 0).start();
        p.AdvanceTime(1);
        long bytes = Alloc.Least(() => { for (int i = 0; i < 200; i++) p.AdvanceTime(16); });
        Assert.That(bytes, Is.EqualTo(0));
    }
}
