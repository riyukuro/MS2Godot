using System;
using System.Globalization;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Field fog check (res://tests/field_fog.tscn; args after `--`: --field XBLOCK --want NEAR,FAR,OFF): the depth fog the field
/// load sets (Field.FogShown's ms2_fog near, far and off) is the client's for the field's mapxblock: LoadXBlock's range, and
/// off (1) when the block has no fog element (IsFogEnabled false, so ApplyFieldFog's SetFogRange(false)). The telescope's skip
/// fog (Field.LoadedFog) is checked to carry the same switch. Quits with 0 when both agree, 1 when not, 2 when the field does
/// not load in time.
/// </summary>
public partial class FieldFogCheck : Node
{
    const int LoadFrames = 60 * 300;
    int frames;
    string want = "";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--want") want = args[i + 1];
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
        if (!(Field.LoadComplete && GetTree().CurrentScene is Field f))
        {
            if (frames > LoadFrames) { GD.Print("field fog: FAIL the field did not load"); GetTree().Quit(2); }
            return;
        }
        var shown = f.FogShown.Split(' ')[0];
        var w = Array.ConvertAll(want.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
        var g = Array.ConvertAll(shown.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
        var lf = f.LoadedFog;
        bool ok = g.Length == 3 && w.Length == 3 && g[0] == w[0] && g[1] == w[1] && g[2] == w[2] && lf.Fog == (w[2] == 0f);
        GD.Print($"field fog: shown {shown} want {want}, loaded fog {lf.Fog} {lf.FogNear} {lf.FogFar}: {(ok ? "ok" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
