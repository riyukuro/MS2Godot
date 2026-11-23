using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/player_force.tscn) of the force zones on the own pc (WorldService::FUN_141bc8420,
/// worldservice_1.cpp:5503-5608, from PCBase::vfunc_68, pcbase.cpp:1771-1775): region skill objects with a push cylinder,
/// set as the field sets them (Player.ForceRegions = the region skills' objects).
/// - the data: skill 99999709 level 1 motion 0 pushes (pushCylinderVelH 300), 99999698 pulls (-280, inner 175);
/// - a push zone moves the idle pc 300 a second away from its centre, and the pushed frames force state-sync records
///   (MyPC::vfunc_110), more than the same second idle without a zone;
/// - a pull zone moves the pc toward its centre until it is inside the inner radius;
/// - a rising zone (pushCylinderVelV 200, height 300) lifts the pc to the cylinder's top and holds it there;
/// - a zone more than 105 above the feet, and any zone on a dead pc, do not move it;
/// - a pc at exactly the push zone's outer radius is not moved, but the zone acted (bPushed), so its frames force records too;
/// - a jump held by a rising zone keeps its jump clock still (bNoVelocity skips FUN_1412407f0), and runs on from there once out.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlayerForceCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    readonly List<Ms2.Net.Packets.StateSync> sent = new();
    Player pc;
    SkillTable skills;
    MethodInfo step;
    FieldInfo regionsField;
    int frames;
    const float Dt = 1f / 60;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        skills = new SkillTable(packs);
        var sb = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8000, 100, 8000) } });
        AddChild(sb);
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);
        pc = Player.Create(visual, anim, faceGrids);
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        pc.ScriptDriven = true;
        pc.Live = true;
        pc.BoundsMin = new Vector3(-5000, -5000, -5000);
        pc.BoundsMax = new Vector3(5000, 5000, 5000);
        pc.StateSyncOut += list => { foreach (var (r, _) in list) sent.Add(r); };
        step = typeof(Player).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
        regionsField = typeof(Player).GetField("ForceRegions");   // by name, so the check builds and fails without the port
    }

    void Steps(int n)
    {
        for (int i = 0; i < n; i++) { step.Invoke(pc, new object[] { (double)Dt }); pc.Anim.Advance(Dt); }
    }

    void Expect(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Process(double delta)
    {
        if (++frames != 5) return;
        try { Run(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        SetProcess(false);
        GD.Print($"player force: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>Field axes (x, y, z up) of the pc's feet.</summary>
    Vector3 Feet => new(pc.GlobalPosition.X, -pc.GlobalPosition.Z, pc.GlobalPosition.Y);

    static RegionSkillObjects Zone(Vector3? centre, PushCylinder c)
    {
        var regions = new RegionSkillObjects();
        if (centre is { } at)
            regions.Add(new RegionSkillObject { Id = 1, Points = new[] { new Vec3(at.X, at.Y, at.Z) }, Attr = new SkillMotion { PushCylinder = c } });
        return regions;
    }

    /// <summary>The pc stood idle at field (x, y) on the floor, with one zone at <paramref name="centre"/> (field axes).</summary>
    void Place(float x, float y, Vector3? centre, PushCylinder c)
    {
        pc.DeadFlag = false;
        var regions = Zone(centre, c);
        regionsField?.SetValue(pc, null);
        pc.GlobalPosition = new Vector3(x, 1, -y);
        pc.ResetMotion();
        Steps(30);                                   // settle on the floor
        regionsField?.SetValue(pc, regions);
    }

    void Run()
    {
        Expect(regionsField != null, "no force zones on the own pc (Player.ForceRegions)");
        var push = skills.RegionMotion(99999709, 1)?.PushCylinder ?? default;
        var pull = skills.RegionMotion(99999698, 1)?.PushCylinder ?? default;
        GD.Print($"player force: data push {push}, pull {pull}");
        Expect(push == new PushCylinder(300, 0, 3000, 300, 350), $"99999709's cylinder {push}");
        Expect(pull == new PushCylinder(-280, 0, 1200, 175, 450), $"99999698's cylinder {pull}");

        // the same second idle without a zone, for the sync count
        Place(500, 0, null, default);
        sent.Clear();
        Steps(60);
        int idleRecords = sent.Count;

        Place(500, 0, Vector3.Zero, push);
        sent.Clear();
        float x0 = Feet.X;
        Steps(60);
        float moved = Feet.X - x0;
        int pushedRecords = sent.Count;
        GD.Print($"player force: push moved {moved:0.#} in 1 s (y {Feet.Y:0.#}), {pushedRecords} sync records (idle {idleRecords}), state {pc.State}");
        Expect(Mathf.Abs(moved - 300f) < 10f && Mathf.Abs(Feet.Y) < 1f, $"push moved {moved:0.#} (y {Feet.Y:0.#}), not 300 out");
        Expect(pushedRecords > idleRecords, $"push sync records {pushedRecords}, idle {idleRecords}");

        Place(600, 0, Vector3.Zero, pull);
        Steps(60);
        float afterOne = Feet.X;
        Steps(180);
        float afterAll = Feet.X;
        GD.Print($"player force: pull at {afterOne:0.#} after 1 s, {afterAll:0.#} after 4 s (inner 175)");
        Expect(Mathf.Abs(afterOne - 320f) < 10f, $"pull after 1 s at {afterOne:0.#}, not 320");
        Expect(afterAll <= 175f && afterAll > 165f, $"pull ended at {afterAll:0.#}, not just inside 175");

        var rise = new PushCylinder(0, 200, 500, 0, 300);
        Place(100, 0, Vector3.Zero, rise);
        Steps(60);
        float upOne = Feet.Z;
        Steps(120);
        float upAll = Feet.Z;
        GD.Print($"player force: lift at {upOne:0.#} after 1 s, {upAll:0.#} after 3 s (top 300), state {pc.State}");
        Expect(Mathf.Abs(upOne - 200f) < 10f, $"lift after 1 s at {upOne:0.#}, not 200");
        Expect(Mathf.Abs(upAll - 300f) < 5f, $"lift after 3 s at {upAll:0.#}, not held at 300");

        Place(500, 0, new Vector3(0, 0, 110), push);   // the feet rest about 1 above the floor: 110 is past their 105
        x0 = Feet.X;
        Steps(60);
        float high = Feet.X - x0;
        Place(500, 0, Vector3.Zero, push);
        pc.DeadFlag = true;
        x0 = Feet.X;
        Steps(60);
        float dead = Feet.X - x0;
        pc.DeadFlag = false;
        GD.Print($"player force: zone 110 above moved {high:0.##}, dead pc moved {dead:0.##}");
        Expect(Mathf.Abs(high) < 0.01f, $"a zone 110 above the feet moved the pc {high:0.##}");
        Expect(Mathf.Abs(dead) < 0.01f, $"a dead pc was moved {dead:0.##}");

        // exactly at the outer radius: a zero push, but the routine acted
        Place(3000, 0, Vector3.Zero, push);
        sent.Clear();
        x0 = Feet.X;
        Steps(60);
        float edge = Feet.X - x0;
        int edgeRecords = sent.Count;
        GD.Print($"player force: at the outer radius moved {edge:0.##}, {edgeRecords} sync records (idle {idleRecords})");
        Expect(Mathf.Abs(edge) < 0.01f, $"a pc at the outer radius was moved {edge:0.##}");
        Expect(edgeRecords > idleRecords + 30, $"a pc at the outer radius sent {edgeRecords} records, idle {idleRecords}: the acting zone forces them");

        // a jump into a rising zone: the jump clock stands while held
        var jumpTime = typeof(Player).GetField("jumpTime", BindingFlags.Instance | BindingFlags.NonPublic);
        Place(100, 0, null, default);
        pc.JumpHeld = true;
        Steps(1);
        pc.JumpHeld = false;
        Steps(9);
        float t0 = (float)jumpTime.GetValue(pc);
        var state0 = pc.State;
        regionsField?.SetValue(pc, Zone(new Vector3(Feet.X, Feet.Y, 0), new PushCylinder(0, 200, 500, 0, 1000)));
        Steps(30);
        float t1 = (float)jumpTime.GetValue(pc);
        regionsField?.SetValue(pc, null);
        Steps(1);
        float t2 = (float)jumpTime.GetValue(pc);
        GD.Print($"player force: jump clock {t0:0.###} ({state0}), {t1:0.###} after 0.5 s held, {t2:0.###} a frame out");
        Expect(state0 == Player.MoveState.Jump && t0 > 0f, $"the jump did not start: state {state0}, clock {t0}");
        Expect(Mathf.Abs(t1 - t0) < 1e-5f, $"the jump clock ran from {t0:0.###} to {t1:0.###} while held by the rising zone");
        Expect(Mathf.Abs(t2 - t1 - Dt) < 1e-4f, $"out of the zone the jump clock went {t1:0.###} -> {t2:0.###}, not on by a frame");
        regionsField?.SetValue(pc, null);
    }
}
