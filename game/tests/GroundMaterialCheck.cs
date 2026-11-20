using System;
using Godot;
using Ms2.Audio;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/ground_material.tscn): a step or a landing takes its ground from the shape the feet touch
/// (PCBase::UpdateGroundMaterial on the hit report's entity, pcbase.cpp:2441), not from a cell lookup by position. A stone
/// cube (no MapAttribute) stands next to a cube of water one cell lower, the layout of a pool edge. A point on the stone
/// 50 units from the edge lies in the water cube's 150-unit column, so a lookup that floors the position and searches down
/// finds the water. The check casts the down ray the controller's hit report would see, feeds its shape to the footsteps
/// and lands: on the stone it must be "default", on the water "water", and with no touched entity nothing plays.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class GroundMaterialCheck : Node3D
{
    int frames;
    CharacterBody3D body;

    public override void _Ready()
    {
        // MS2 cubes: XY centred on the entity position, standing on its Z (GeneratePhysXBox)
        Cube(new Vector3(0, 0, 0), null);           // stone at the top
        Cube(new Vector3(-150, -150, 0), "water");  // the water cell beside it, one cell lower
        body = new CharacterBody3D();
        AddChild(body);
    }

    void Cube(Vector3 at, string material)
    {
        var sb = new StaticBody3D { Position = at + new Vector3(0, 75, 0) };
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(150, 150, 150) } };
        FieldBuilder.TagGround(cs, material);
        sb.AddChild(cs);
        AddChild(sb);
    }

    (GodotObject collider, int shape) Down(Vector3 from)
    {
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 1000));
        return hit.Count == 0 ? (null, -1) : ((GodotObject)hit["collider"], (int)hit["shape"]);
    }

    string Land(Vector3 at, bool touched)
    {
        var steps = new FootstepSounds(null, new GroundEffectTable(_ => null), body, null);
        AddChild(steps);
        int effects = 0;
        steps.EffectFile = _ => effects++;
        body.GlobalPosition = at;
        var (collider, shape) = Down(at);
        steps.Ground = () => touched ? FieldBuilder.GroundMaterial(collider, shape) : "";
        steps.Land();
        var g = steps.LastGround;
        if (!touched && effects != 0) g = "played";
        steps.QueueFree();
        return g;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (++frames < 3) return;
        SetPhysicsProcess(false);
        string edge = Land(new Vector3(-50, 150, 0), true), middle = Land(new Vector3(0, 150, 0), true);
        string water = Land(new Vector3(-150, 0, 0), true), none = Land(new Vector3(0, 150, 0), false);
        bool ok = edge == "default" && middle == "default" && water == "water" && none == "";
        GD.Print($"ground material check: stone edge '{edge}', stone middle '{middle}', water '{water}', no entity '{none}': {(ok ? "OK" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
