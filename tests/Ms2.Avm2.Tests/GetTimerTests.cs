using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// getTimer is real time since the movie started: GlobalObjectCPP::getTimer (sfr_as3_04.cpp:2943) gives
/// MovieImpl::GetASTimerMs (sfr_gfx_01i.cpp:1169) = Timer::GetTicks()/1000 - StartTickMs, not frames * 1000/fps. When a
/// slow frame drops ticks, getTimer still reads the real elapsed time (the quick bar's cooldown text counts down on it).
/// </summary>
public class GetTimerTests
{
    static (Runtime, Player) Empty()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["fps"] = 30, ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        return (rt, new Player(rt, layout));
    }

    static int GetTimer(Runtime rt) => (int)Rt.ToNumber(((AsFunction)rt.GetGlobal("flash.utils.getTimer")).Call(null, Array.Empty<object>()));

    [Test]
    public void GetTimerIsRealTimeWhenTicksAreDropped()
    {
        var (rt, p) = Empty();
        double now = 50_000;                    // the clock's reading when the movie starts
        p.Clock = () => now;
        Assert.That(GetTimer(rt), Is.EqualTo(0), "ms since the movie start, not since the clock's own start");

        for (int i = 0; i < 3; i++) { now += 1000.0 / 30; p.Tick(); }
        Assert.That(GetTimer(rt), Is.EqualTo(100), "3 frames in 100 ms");

        now += 1000; p.Tick();                  // one slow frame: 1 s passes, one tick runs (the rest are dropped)
        Assert.That(GetTimer(rt), Is.EqualTo(1100), "the real 1.1 s, not 4 ticks = 133 ms");

        now += 250;                             // between ticks too: GetTicks is read at the call
        Assert.That(GetTimer(rt), Is.EqualTo(1350));
    }

    [Test]
    public void GetTimerCountsWholeMsAtStartAndRead()
    {
        // GetTicks()/1000 is an integer division of microseconds, at the start (ctor) and at the read, so both are whole ms:
        // started at 7.5 ms and read at 107.4 ms gives 107 - 7 = 100, not 99.9 cut to 99
        var (rt, p) = Empty();
        double now = 7.5;
        p.Clock = () => now;
        now = 107.4;
        Assert.That(GetTimer(rt), Is.EqualTo(100));
        now = 106.9;
        Assert.That(GetTimer(rt), Is.EqualTo(99));
    }

    [Test]
    public void GetTimerAllocatesNothing()
    {
        var (rt, p) = Empty();
        double now = 0;
        p.Clock = () => now;
        GetTimer(rt);
        long bytes = Alloc.Least(() => { for (int i = 0; i < 200; i++) { now += 16; _ = p.ASTimerMs; } });
        Assert.That(bytes, Is.EqualTo(0));
    }
}
