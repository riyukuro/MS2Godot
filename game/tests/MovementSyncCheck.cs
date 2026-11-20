using System.Collections.Generic;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/movement_sync.tscn) of MyPC's movement and its sync out.
/// - Sync direction: the record's m_vel3c is the move buffer's input (m_pos98 = Unk_PCMove::GetMoveDirection,
///   unit_141682f80.cpp:313-335: x +1 left, y +1 forward), packed by FUN_140836250 (statesyncobject.cpp:202-220). The
///   receiver turns it by the sender's camera (PCBase::CalcMoveDirection, pcbase.cpp:2828-2840). With the camera turned a
///   quarter, the right key must still send x -1, y 0 (flags 0 + 8) and the forward key x 0, y +1 (flags 1 + 12).
/// - The controller (NxController, MyPC's and the remotes' body): a walking body's sweeps allocate at most 8 B per move.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class MovementSyncCheck : Node3D
{
    int frames;
    Player runner;
    readonly List<Ms2.Net.Packets.StateSync> sent = new();

    public override void _Ready()
    {
        var sb = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } });
        AddChild(sb);
        var visual = new Node3D();
        var anim = new AnimationPlayer();
        visual.AddChild(anim);
        runner = Player.Create(visual, anim);
        AddChild(runner);
        runner.SetProcess(false);
        runner.SetPhysicsProcess(false);
        runner.ScriptDriven = true;
        runner.Live = true;
        runner.BoundsMin = new Vector3(-2000, -2000, -1000);   // the field's world box (field axes x, -z, y)
        runner.BoundsMax = new Vector3(2000, 2000, 1000);
        runner.StateSyncOut += list => { foreach (var (r, _) in list) sent.Add(r); };
    }

    // walks with the given input for half a second and returns the move flags of the last walk record sent
    int Walk(Vector2 input)
    {
        var step = typeof(Player).GetMethod("Step", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        sent.Clear();
        runner.Input2 = input;
        for (int i = 0; i < 30; i++) step.Invoke(runner, new object[] { 1.0 / 60 });
        runner.Input2 = Vector2.Zero;
        for (int i = 0; i < 30; i++) step.Invoke(runner, new object[] { 1.0 / 60 });
        int flags = -1;
        foreach (var r in sent) if (r.Animation != 9) flags = r.Animation;   // 9 = no input (x 0 = 1, y 0 = +8)
        return flags;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (++frames < 3) return;
        SetPhysicsProcess(false);
        try { Run(); }
        catch (System.Exception e) { GD.Print($"movement sync check: {e.InnerException ?? e}: FAIL"); GetTree().Quit(1); }
    }

    // bare controllers, as the players' and the remotes' bodies: the PC capsule, its group and its groups
    List<(CharacterBody3D b, Movement.NxController nx)> Bodies(int count, float x0)
    {
        var bodies = new List<(CharacterBody3D, Movement.NxController)>();
        for (int i = 0; i < count; i++)
        {
            var b = new CharacterBody3D { CollisionLayer = Movement.MoveConfig.PcLayer, CollisionMask = Movement.MoveConfig.PcGroups };
            var cs = new CollisionShape3D
            {
                Shape = new CapsuleShape3D { Radius = Movement.MoveConfig.Radius, Height = Movement.MoveConfig.Height },
                Position = new Vector3(0, Movement.MoveConfig.Height / 2, 0),
            };
            b.AddChild(cs);
            AddChild(b);
            b.GlobalPosition = new Vector3(x0 + i * 300, 25, 0);
            bodies.Add((b, new Movement.NxController(b, cs)));
        }
        return bodies;
    }

    // A body walks a fixed square over the floor, a step box and a wall box: managed bytes per move, after a warm-up lap.
    bool MoveAllocations()
    {
        var step = new StaticBody3D { Position = new Vector3(5000, 10, 0) };
        step.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 20, 200) } });
        AddChild(step);
        var wall = new StaticBody3D { Position = new Vector3(5000, 100, 400) };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(600, 200, 40) } });
        AddChild(wall);
        var (b, nx) = Bodies(1, 4800)[0];
        Vector3[] dirs = { new(6, -1, 0), new(0, -1, 6), new(-6, -1, 0), new(0, -1, -6) };
        long bytes = 0;
        var sw = new System.Diagnostics.Stopwatch();
        const int lap = 480;
        for (int f = 0; f < 2 * lap; f++)
        {
            long a0 = System.GC.GetAllocatedBytesForCurrentThread();
            if (f >= lap) sw.Start();
            nx.Move(dirs[f / 120 % 4]);
            sw.Stop();
            long used = System.GC.GetAllocatedBytesForCurrentThread() - a0;
            if (f >= lap) bytes += used;
        }
        float perMove = bytes / (float)lap;
        bool ok = perMove <= 8f;
        GD.Print($"movement sync check: a walking body allocates {perMove:0.0} B per move (cap 8), {sw.Elapsed.TotalMilliseconds * 1000 / lap:0.0} us per move, feet at {b.GlobalPosition.Y:0.00}: {(ok ? "OK" : "FAIL")}");
        b.QueueFree();
        return ok;
    }

    void Run()
    {
        runner.CameraYaw = Mathf.Pi / 2;
        int right = Walk(new Vector2(1, 0)), forward = Walk(new Vector2(0, 1));
        bool dir = right == 8 && forward == 13;
        GD.Print($"movement sync check: camera a quarter turned, right key flags {right}, forward key flags {forward}: {(dir ? "OK" : "FAIL")}");
        var floor = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20000, 100, 20000) } });
        AddChild(floor);
        bool alloc = MoveAllocations();
        bool ok = dir && alloc;
        GetTree().Quit(ok ? 0 : 1);
    }
}
