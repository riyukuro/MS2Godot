using System;
using System.Collections.Generic;
using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/frame_rate_scenes.tscn) of the process frame rate meter (<see cref="FrameRateMeter.Process"/>,
/// CMainSystem::OnTick, mainsystem.cpp:540-561):
/// - the <see cref="FrameRateClock"/> autoload ticks it every frame, outside the current scene;
/// - it has a rate after a little over a second, and still has it on the first frame of the next scene, so the frame rate
///   dialog opened at game entry does not start from 0.0 (a meter owned by the game scene starts again at each entry);
/// - a tick allocates nothing.
/// Quits with 0 when it holds, else 1.
/// </summary>
public partial class FrameRateScenesCheck : Node
{
    static int phase;
    static float rateBefore;
    readonly List<string> fails = new();
    ulong start;

    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Ready()
    {
        if (phase == 0) { start = Time.GetTicksMsec(); return; }
        // the next scene's first frame: the rate counted before the change is still there
        try
        {
            var clock = GetTree().Root.GetNodeOrNull("FrameRateClock");
            Check(clock is FrameRateClock, "the frame rate clock autoload is missing");
            Check(clock != null && clock.GetParent() == GetTree().Root && clock != GetTree().CurrentScene, "the clock is not beside the scenes");
            float now = FrameRateMeter.Process.Fps;
            Check(now > 0f, $"the next scene starts with rate {now} (before the change {rateBefore})");
            var m = FrameRateMeter.Process;
            double t = Time.GetTicksUsec() / 1_000_000.0;
            m.Tick(t);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i <= 1000; i++) m.Tick(t + i * 0.002);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0, $"1000 ticks allocated {bytes} B");
        }
        catch (Exception e) { fails.Add(e.ToString()); }
        foreach (var f in fails) GD.PrintErr("frame rate scenes: " + f);
        GD.Print(fails.Count == 0 ? "frame rate scenes: PASS" : $"frame rate scenes: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    public override void _Process(double delta)
    {
        if (phase != 0) return;
        if (Time.GetTicksMsec() - start < 1500) return;
        rateBefore = FrameRateMeter.Process.Fps;
        if (rateBefore <= 0f) { GD.PrintErr($"frame rate scenes: no rate after 1.5 s ({rateBefore})"); GD.Print("frame rate scenes: FAIL (1)"); GetTree().Quit(1); return; }
        phase = 1;
        GetTree().ChangeSceneToFile(SceneFilePath);
    }
}
