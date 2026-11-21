using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Windowed check (res://tests/field_lights_swap.tscn, no game data): an effect light started between a field's exit and the
/// next field's FieldLights.Add is not put into the old field's set; the retry after the Add puts it into the new set.
/// Quits with 0 when it does, else 1.
/// </summary>
public partial class FieldLightsSwapCheck : Node
{
    public override void _Ready()
    {
        var fails = new List<string>();
        var p = new EffectLightParam { Dimmer = 1, Range = 500, Constant = 1 };
        var oldSet = FieldLightSet.Read(new List<FieldEntity>());
        var newSet = FieldLightSet.Read(new List<FieldEntity>());
        FieldLights.Add(oldSet, 1f);
        FieldLights.Clear(oldSet);   // the old field's _ExitTree
        if (FieldLights.Start(p, new Vec3()) != null) fails.Add("a light started before the new Add got a slot");
        if (System.Array.Exists(oldSet.Effects, e => e != null)) fails.Add("the light went into the old field's set");
        FieldLights.Add(newSet, 1f);
        var l = FieldLights.Start(p, new Vec3());   // the retry while Light == null
        if (l == null || !System.Array.Exists(newSet.Effects, e => e == l)) fails.Add("the retry after the Add is not in the new set");
        FieldLights.Clear(oldSet);   // a late exit of the old field keeps the new set
        if (FieldLights.Start(p, new Vec3()) == null) fails.Add("a late Clear of the old set dropped the new one");
        FieldLights.Clear();
        GD.Print($"field lights swap: {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
