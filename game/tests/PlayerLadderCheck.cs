using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/player_ladder.tscn) of the own pc's StateLadder when a rope / ladder trigger event clears the
/// ladder's Climbable flag (0x4df0c0de) mid-climb. StateLadder::UpdateClimb (@0x1417d8c00, stateladder.cpp:370-386) reads the
/// flag on every update: with it off and a ground hit below the feet it sets sub 0x32, which FUN_1417d9240 (:480-484) turns
/// into the fall state; with no hit below, the climb moves nothing.
/// - a ladder over the floor, flag cleared in the middle of a climb: the pc falls;
/// - the same, cleared while the pc is still taking hold: the pc falls (the client reads it whatever the sub state);
/// - a ladder with nothing below, flag cleared while the up key is held: the pc stays on it and does not move; set again,
///   the climb goes on.
/// StateRope (StateRope::FUN_1417e1e60, statemanager.cpp:1438-1446) reads the rope's flag the same way, sub 0x39 on a hit:
/// - a rope over the floor, flag cleared in the middle of a climb: the pc falls;
/// - a rope with nothing below, flag cleared while the up key is held: the pc stays on it and does not move; set again,
///   the down key moves it.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlayerLadderCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    Player pc;
    MethodInfo step, take, takeRope;
    int frames;
    const float Dt = 1f / 60;
    // the floor spans x -2000..2000 with its top at 0; the first ladder stands 200 above it, the second past it
    static readonly Vector3 OverFloor = new(0, 200, 0), OverNothing = new(3000, 0, 0);

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var sb = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } });
        AddChild(sb);
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);
        pc = Player.Create(visual, anim, faceGrids);
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        pc.ScriptDriven = true;
        pc.BoundsMin = new Vector3(-5000, -5000, -5000);
        pc.BoundsMax = new Vector3(5000, 5000, 5000);
        pc.GlobalPosition = new Vector3(0, 5, 0);
        step = typeof(Player).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
        take = typeof(Player).GetMethod("TryLadder", BindingFlags.Instance | BindingFlags.NonPublic);
        takeRope = typeof(Player).GetMethod("TryRope", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    void Steps(int n)
    {
        for (int i = 0; i < n; i++) { step.Invoke(pc, new object[] { (double)Dt }); pc.Anim.Advance(Dt); }
    }

    void Expect(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Process(double delta)
    {
        if (++frames != 5) return;
        try { Run(); Rope(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        SetProcess(false);
        GD.Print($"player ladder: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>A ladder with its base at <paramref name="at"/>, takes hold of it from beside (side 1).</summary>
    Climbables.Climb Take(Vector3 at)
    {
        var l = new Climbables.Climb { Name = "ladder", Position = at, YawDeg = 0, Extrude = 0, Usable = true };
        pc.Climbs = new Climbables();
        pc.Climbs.All.Add(l);
        pc.Input2 = Vector2.Zero;
        pc.GlobalPosition = at;
        if (!(bool)take.Invoke(pc, new object[] { 1 })) fails.Add($"no hold of the ladder at {at}");
        return l;
    }

    /// <summary>A rope with its base at <paramref name="at"/>, takes hold of it from beside (side 1).</summary>
    Climbables.Climb TakeRope(Vector3 at)
    {
        var l = new Climbables.Climb { Name = "rope", IsRope = true, Position = at, YawDeg = 0, Extrude = 0, Usable = true };
        pc.Climbs = new Climbables();
        pc.Climbs.All.Add(l);
        pc.Input2 = Vector2.Zero;
        pc.GlobalPosition = at;
        if (!(bool)takeRope.Invoke(pc, new object[] { 1 })) fails.Add($"no hold of the rope at {at}");
        return l;
    }

    void Rope()
    {
        Steps(30);
        var l = TakeRope(OverFloor);
        Steps(30);
        pc.Input2 = new Vector2(0, 1);
        Steps(10);
        Expect(pc.State == Player.MoveState.Rope, $"rope climb: {pc.State}");
        float y0 = pc.GlobalPosition.Y;
        l.Usable = false;
        Steps(1);
        Expect(pc.State == Player.MoveState.Fall, $"rope flag cleared mid-climb over the floor: {pc.State}, want Fall");
        pc.Input2 = Vector2.Zero;
        Steps(60);
        GD.Print($"player ladder: rope over the floor, cleared at y {y0:0.#}, after it {pc.State} at y {pc.GlobalPosition.Y:0.#}");

        l = TakeRope(OverNothing);
        Steps(30);
        pc.Input2 = new Vector2(0, 1);
        Steps(10);
        l.Usable = false;
        float y1 = pc.GlobalPosition.Y;
        Steps(30);
        float y2 = pc.GlobalPosition.Y;
        Expect(pc.State == Player.MoveState.Rope, $"rope flag cleared with nothing below: {pc.State}, want Rope");
        Expect(Mathf.Abs(y2 - y1) < 0.01f, $"rope flag cleared with nothing below: moved {y2 - y1:0.##}, want 0");
        // a lone segment is held at its top (base + RopeGrip), so set again the pc climbs down it
        l.Usable = true;
        pc.Input2 = new Vector2(0, -1);
        Steps(10);
        float y3 = pc.GlobalPosition.Y;
        Expect(y3 < y2 - 1f, $"rope flag set again, down held: {pc.State}, moved {y3 - y2:0.##}");
        GD.Print($"player ladder: rope with nothing below, off {y2 - y1:0.##} moved, on again {y3 - y2:0.##} moved");
    }

    void Run()
    {
        Steps(30);
        // over the floor, in the middle of a climb
        var l = Take(OverFloor);
        Steps(15);
        pc.Input2 = new Vector2(0, 1);
        Steps(10);
        Expect(pc.State == Player.MoveState.Ladder, $"climb: {pc.State}");
        float y0 = pc.GlobalPosition.Y;
        l.Usable = false;
        Steps(1);
        Expect(pc.State == Player.MoveState.Fall, $"flag cleared mid-climb over the floor: {pc.State}, want Fall");
        pc.Input2 = Vector2.Zero;
        Steps(60);
        GD.Print($"player ladder: over the floor, cleared at y {y0:0.#}, after it {pc.State} at y {pc.GlobalPosition.Y:0.#}");

        // over the floor, still taking hold
        l = Take(OverFloor);
        Steps(1);
        l.Usable = false;
        Steps(1);
        Expect(pc.State == Player.MoveState.Fall, $"flag cleared while taking hold: {pc.State}, want Fall");
        Steps(60);

        // nothing below: the climb stops while the flag is off
        l = Take(OverNothing);
        Steps(15);
        pc.Input2 = new Vector2(0, 1);
        Steps(10);
        l.Usable = false;
        float y1 = pc.GlobalPosition.Y;
        Steps(30);
        float y2 = pc.GlobalPosition.Y;
        Expect(pc.State == Player.MoveState.Ladder, $"flag cleared with nothing below: {pc.State}, want Ladder");
        Expect(Mathf.Abs(y2 - y1) < 0.01f, $"flag cleared with nothing below: moved {y2 - y1:0.##}, want 0");
        l.Usable = true;
        Steps(30);
        float y3 = pc.GlobalPosition.Y;
        Expect(pc.State == Player.MoveState.Ladder && y3 > y2 + 1f, $"flag set again: {pc.State}, moved {y3 - y2:0.##}");
        GD.Print($"player ladder: nothing below, off {y2 - y1:0.##} moved, on again {y3 - y2:0.##} moved");
    }
}
