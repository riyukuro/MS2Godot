using System;
using System.Reflection;
using Godot;
using Ms2.Audio;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/ground_material.tscn) of the ground a step or a landing plays on.
/// - Shape: the material is the touched shape's entity's (PCBase::UpdateGroundMaterial, pcbase.cpp:2441), not a cell
///   lookup by position. A stone cube (no MapAttribute) stands next to a cube of water one cell lower; a point on the stone
///   50 units from the edge lies in the water cube's 150-unit column. On the stone it must be "default", on the water
///   "water", and on a shape with no entity "" (nothing plays).
/// - Snapshot: the stored material changes only on vfunc_93(true), every 200 ms (pc.cpp:4441-4445). Moving from stone onto
///   water keeps "default" until the refresh; stepping off a ledge keeps the ledge's material until the refresh.
/// - Landing: StateLand's ray (stateland.cpp:83-92) puts the entity under the feet in the hit report. A player whose report
///   still holds the stone wall lands on the water: the landing plays "water" and the report's entity is water.
/// - Dash skid: StateDash::vfunc_9 (state.cpp:1855-1861) refreshes the material and plays one "land" when the skid starts.
/// - Frame order: PC::vfunc_53 (pc.cpp:4436-4445) moves first and refreshes after, so on the refresh frame whose move
///   first touches water from stone the stored material is already "water". A player runs from a stone cube onto a water
///   cube of the same height; the 200 ms timer expires in the frame of that move.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class GroundMaterialCheck : Node3D
{
    int frames;
    CharacterBody3D body;
    Player player, runner;
    StaticBody3D stone, water, bare;

    public override void _Ready()
    {
        // MS2 cubes: XY centred on the entity position, standing on its Z (GeneratePhysXBox)
        stone = Cube(new Vector3(0, 0, 0), "");                 // stone at the top: an entity with no MapAttribute
        water = Cube(new Vector3(-150, -150, 0), "water");      // the water cell beside it, one cell lower
        bare = Cube(new Vector3(600, 0, 0), null);              // a shape with no entity
        ((Node)water.GetChild(0)).SetMeta(FieldBuilder.KindMeta, MoveConfig.WaterGroup);
        body = new CharacterBody3D();
        AddChild(body);
        player = Player.Create(new Node3D(), null);
        AddChild(player);
        player.SetProcess(false);
        player.SetPhysicsProcess(false);
        Cube(new Vector3(1200, 0, 0), "");                      // a stone floor and a water floor at the same height
        var pool = Cube(new Vector3(1350, 0, 0), "water");
        ((Node)pool.GetChild(0)).SetMeta(FieldBuilder.KindMeta, MoveConfig.WaterGroup);
        runner = Player.Create(new Node3D(), null);
        AddChild(runner);
        runner.SetProcess(false);
        runner.SetPhysicsProcess(false);
        runner.ScriptDriven = true;
        runner.GlobalPosition = new Vector3(1240, 150, 0);
        runner.BoundsMin = new Vector3(1000, -1000, -1000);       // the field's world box (field axes x, -z, y)
        runner.BoundsMax = new Vector3(1600, 1000, 1000);
    }

    StaticBody3D Cube(Vector3 at, string material)
    {
        var sb = new StaticBody3D { Position = at + new Vector3(0, 75, 0) };
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(150, 150, 150) } };
        if (material != null) FieldBuilder.TagGround(cs, material);
        sb.AddChild(cs);
        AddChild(sb);
        return sb;
    }

    Node Down(Vector3 from)
    {
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 1000));
        if (hit.Count == 0) return null;
        var co = (CollisionObject3D)(GodotObject)hit["collider"];
        return co.ShapeOwnerGetOwner(co.ShapeFindOwner((int)hit["shape"])) as Node;
    }

    string Land(Vector3 at)
    {
        var steps = new FootstepSounds(null, new GroundEffectTable(_ => null), body, null);
        AddChild(steps);
        int effects = 0;
        steps.EffectFile = _ => effects++;
        var snap = new GroundMaterialSnapshot { Entity = FieldBuilder.GroundMaterial(Down(at)) };
        snap.Refresh(true);
        steps.Ground = () => snap.Material;
        steps.Land();
        var g = steps.LastGround;
        if (g == "" && effects != 0) g = "played";
        steps.QueueFree();
        return g;
    }

    // 60 fps frames of PC::vfunc_53 until the first refresh after the change; returns the material each frame saw
    static (string before, string after, int frame) Change(GroundMaterialSnapshot s, Action change, bool onOrAbove)
    {
        const double dt = 1.0 / 60;
        s.Frame(dt, true);                          // the first frame refreshes (the timer is not running)
        s.Frame(dt, true);
        change();
        string before = null;
        for (int f = 2; f < 30; f++)
        {
            if (s.Frame(dt, onOrAbove)) return (before, s.Material, f);
            before ??= s.Material;
            if (s.Material != before) return (before, s.Material, -f);
        }
        return (before, s.Material, -1);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (++frames < 3) return;
        SetPhysicsProcess(false);
        string edge = Land(new Vector3(-50, 150, 0)), middle = Land(new Vector3(0, 150, 0));
        string wet = Land(new Vector3(-150, 0, 0)), none = Land(new Vector3(600, 150, 0));
        bool shapes = edge == "default" && middle == "default" && wet == "water" && none == "";

        var a = new GroundMaterialSnapshot { Entity = "" };
        var (aBefore, aAfter, aFrame) = Change(a, () => a.Entity = "water", true);
        var b = new GroundMaterialSnapshot { Entity = "rock" };
        var (bBefore, bAfter, bFrame) = Change(b, () => { }, false);
        // the refresh comes 200 ms after the timer start: the 12th 16.7 ms frame (13th with rounding), not earlier
        bool snapshot = aBefore == "default" && aAfter == "water" && aFrame is 12 or 13 && bBefore == "rock" && bAfter == "default" && bFrame is 12 or 13;

        // the report holds the stone wall; the player lands on the water 40 units below its feet
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Player).GetMethod("NoteHit", Private).Invoke(player, new object[] { stone, 0 });
        bool wallFirst = !player.OnWater;
        player.GlobalPosition = new Vector3(-150, 1, 0);
        typeof(Player).GetMethod("LandRay", Private).Invoke(player, null);
        var g = (GroundMaterialSnapshot)typeof(Player).GetField("ground", Private).GetValue(player);
        g.Refresh(true);
        bool landing = wallFirst && player.OnWater && player.GroundMaterial == "water";

        // StateDash's skid (state.cpp:1855-1861): one "land" and a refresh in the same frame. The report holds the stone
        // now; the stored material still says water until the skid refreshes it.
        int lands = 0;
        player.Landed += () => lands++;
        typeof(Player).GetMethod("NoteHit", Private).Invoke(player, new object[] { stone, 0 });
        string beforeSkid = player.GroundMaterial;
        typeof(Player).GetMethod("DashSkid", Private).Invoke(player, new object[] { 0f });
        bool dash = beforeSkid == "water" && lands == 1 && player.GroundMaterial == "default";

        // one real step: a frame on the stone (the first refresh), a frame that starts the walk, then a 200 ms frame whose
        // move runs 70 units from the stone onto the water and whose end expires the timer
        var step = typeof(Player).GetMethod("Step", Private);
        step.Invoke(runner, new object[] { 1.0 / 60 });
        string onStone = runner.GroundMaterial;
        runner.Input2 = new Vector2(1, 0);
        step.Invoke(runner, new object[] { 1.0 / 60 });
        step.Invoke(runner, new object[] { GroundMaterialSnapshot.RefreshMs / 1000 });
        string afterStep = runner.GroundMaterial;
        bool order = onStone == "default" && runner.GlobalPosition.X > 1300 && afterStep == "water";

        bool ok = shapes && snapshot && landing && dash && order;
        GD.Print($"ground material check: stone edge '{edge}', stone middle '{middle}', water '{wet}', no entity '{none}'; " +
                 $"stone->water '{aBefore}' then '{aAfter}' at frame {aFrame}, off a ledge '{bBefore}' then '{bAfter}' at frame {bFrame}; " +
                 $"landing from a wall report: water {landing}; dash skid '{beforeSkid}' then '{player.GroundMaterial}', {lands} land; " +
                 $"step onto water at x {runner.GlobalPosition.X:0}: '{onStone}' then '{afterStep}': {(ok ? "OK" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
