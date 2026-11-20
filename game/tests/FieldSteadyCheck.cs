using System;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Field check (res://tests/field_steady.tscn) that a loaded field standing still creates no Godot objects and no tracked
/// wrappers per frame. It needs the real renderer (the render callbacks are part of the frame; --headless runs the dummy
/// one), so run it on a display with --fixed-fps 60:
///   godot-mono --display-driver x11 --fixed-fps 60 --path . res://tests/field_steady.tscn -- --field 02020120_bf --no-npcs
/// It opens field.tscn, waits for the load and 60 frames more, then samples the engine's ObjectCount and the two tables of
/// GodotSharp's DisposablesTracker every 30 frames for 600 frames. The peak over the start must stay under Slack for each:
/// a GC can collect garbage mid-run, so the peak and not the end value is what shows a per-frame leak (the bloom pass's
/// undisposed uniform Variants were ~9 objects and ~9 Variant wrappers a frame, ~5400 in 600 frames).
/// Quits with 0 when all three stay flat, else 1; 2 when the field does not load in time.
/// </summary>
public partial class FieldSteadyCheck : Node
{
    const int Settle = 60, Run = 600, Every = 30, Slack = 60, LoadFrames = 60 * 300;
    int frames, loadedAt = -1;
    long[] start, peak;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // stay on the root across the scene change
        CallDeferred(MethodName.Move);
    }

    void Move()
    {
        var root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        GetTree().ChangeSceneToFile("res://field.tscn");
    }

    public override void _Process(double delta)
    {
        frames++;
        if (loadedAt < 0)
        {
            if (Field.LoadComplete && GetTree().CurrentScene is Field) loadedAt = frames;
            else if (frames > LoadFrames) { GD.Print("field steady: FAIL the field did not load"); GetTree().Quit(2); }
            return;
        }
        int n = frames - loadedAt - Settle;
        if (n < 0 || n % Every != 0) return;
        var now = Counts();
        if (start == null) { start = now; peak = (long[])now.Clone(); return; }
        for (int i = 0; i < now.Length; i++) peak[i] = Math.Max(peak[i], now[i]);
        if (n < Run) return;
        string[] names = { "objects", "object wrappers", "other wrappers" };
        bool ok = true;
        for (int i = 0; i < names.Length; i++)
        {
            long grew = peak[i] - start[i];
            GD.Print($"field steady: {names[i]} start {start[i]} peak {peak[i]} end {now[i]} (+{grew} in {Run} frames)");
            if (grew > Slack) ok = false;
        }
        GD.Print(ok ? "field steady: ok" : "field steady: FAIL");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>ObjectCount and the entry counts of DisposablesTracker's GodotObjectInstances and OtherInstances (internal, read by
    /// reflection: only the dictionaries' Count, nothing enumerated).</summary>
    static long[] Counts()
    {
        var r = new long[] { (long)Performance.GetMonitor(Performance.Monitor.ObjectCount), -1, -1 };
        var tracker = typeof(GodotObject).Assembly.GetType("Godot.DisposablesTracker");
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        if (tracker?.GetProperty("GodotObjectInstances", F)?.GetValue(null) is System.Collections.ICollection a) r[1] = a.Count;
        if (tracker?.GetProperty("OtherInstances", F)?.GetValue(null) is System.Collections.ICollection b) r[2] = b.Count;
        return r;
    }
}
