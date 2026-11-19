using System;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/move_start.tscn): a field move starts its load when no window can draw. Headless (and
/// minimized) Godot draws no frame and emits no FramePostDraw, so the move's wait for its loading screen's first frame
/// must end at a process frame instead. It runs a move on a field with no loading screen (the move's ready check
/// records the load's start, then holds it) and the screenshot cover's capture step on its own, and fails when either
/// has not run within 30 frames. Quits with 0 when both ran, else 1.
/// </summary>
public partial class MoveStartCheck : Node
{
    int frames, started, captured;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var field = new FieldContents(packs, null, null);
        AddChild(field);
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FieldContents).GetMethod("BeginMove", Private).Invoke(field, new object[]
        {
            2000062, (Func<Travel.Move>)(() => null), (Func<bool>)(() => { started++; return false; }),
        });
        typeof(FieldContents).GetMethod("ShowLoadingOverField", Private).Invoke(field, new object[]
        {
            "", (Action)(() => captured++),
        });
    }

    public override void _Process(double delta)
    {
        if (++frames < 30 && (started == 0 || captured == 0)) return;
        SetProcess(false);
        bool ok = started > 0 && captured == 1;
        GD.Print($"move start: {(ok ? "ok" : "FAIL")}, load started {started > 0}, cover captured {captured} times, after {frames} frames");
        GetTree().Quit(ok ? 0 : 1);
    }
}
