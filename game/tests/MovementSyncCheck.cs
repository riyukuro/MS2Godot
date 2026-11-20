using System.Collections.Generic;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/movement_sync.tscn) of MyPC's movement and its sync out.
/// - Sync direction: the record's m_vel3c is the move buffer's input (m_pos98 = Unk_PCMove::GetMoveDirection,
///   unit_141682f80.cpp:313-335: x +1 left, y +1 forward), packed by FUN_140836250 (statesyncobject.cpp:202-220). The
///   receiver turns it by the sender's camera (PCBase::CalcMoveDirection, pcbase.cpp:2828-2840). With the camera turned a
///   quarter, the right key must still send x -1, y 0 (flags 0 + 8) and the forward key x 0, y +1 (flags 1 + 12).
/// - The controller (NxController, MyPC's and the remotes' body): a walking body's sweeps allocate at most 8 B per move;
///   bodies jumping on a 200 m floor box and on a mesh terrain never fall through.
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

    // Eight bodies walk and jump on a 200 m floor box (field boxes come that large: GeneratePhysXBox, whitebox sizes): a new
    // heading every 60 frames, a jump every 150, gravity each frame. GodotPhysics3D's cast misses such a box now and then
    // (its GJK distance test reports the capsule's motion separate), and a landing went through the floor.
    bool Landings(StaticBody3D floor)
    {
        var rng = new System.Random(7);
        var bodies = Bodies(8, 0);
        int falls = 0, firstFrame = -1;
        for (int i = 0; i < bodies.Count; i++)
        {
            var (b, nx) = bodies[i];
            float vy = 0;
            var h = Vector3.Zero;
            for (int f = 0; f < 6000; f++)
            {
                if (f % 60 == 0)
                {
                    double a = rng.NextDouble() * Mathf.Tau;
                    float sp = (float)rng.NextDouble() * 12f;
                    h = new Vector3(Mathf.Cos((float)a) * sp, 0, Mathf.Sin((float)a) * sp);
                }
                if (f % 150 == 75) vy = 10f + (float)rng.NextDouble() * 10f;
                vy -= 0.6f + (float)rng.NextDouble() * 0.01f;
                if ((nx.Move(h + new Vector3(0, vy, 0)) & Movement.NxController.Hit.Down) != 0) vy = 0;
                if (b.GlobalPosition.Y < -1f)
                {
                    falls++;
                    if (firstFrame < 0) firstFrame = f;
                    break;
                }
            }
            b.QueueFree();
        }
        bool ok = falls == 0;
        GD.Print($"movement sync check: jump landings on a 200 m floor box, {falls} of {bodies.Count} bodies fell through (first at frame {firstFrame}): {(ok ? "OK" : "FAIL")}");
        return ok;
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

    // Eight bodies drop onto a bumpy mesh terrain rising to a slope (1 m cells, two-sided, no floor under it) and walk and
    // jump on it: the sweep's probes and its gap closing on mesh triangles.
    bool MeshLandings()
    {
        var rng = new System.Random(3);
        var ground = new StaticBody3D { Position = new Vector3(20000, 0, 0) };
        static float H(int i, int j) => (i * 7919 + j * 104729) % 97 * 0.4f + (i > 10 ? (i - 10) * 15f : 0);
        var faces = new Vector3[30 * 30 * 6];
        int n = 0;
        for (int i = 0; i < 30; i++)
            for (int j = 0; j < 30; j++)
            {
                Vector3 a = new(i * 100, H(i, j), j * 100), b = new((i + 1) * 100, H(i + 1, j), j * 100);
                Vector3 c = new(i * 100, H(i, j + 1), (j + 1) * 100), d = new((i + 1) * 100, H(i + 1, j + 1), (j + 1) * 100);
                faces[n++] = a; faces[n++] = c; faces[n++] = b; faces[n++] = b; faces[n++] = c; faces[n++] = d;
            }
        var mesh = new ConcavePolygonShape3D { BackfaceCollision = true };
        mesh.SetFaces(faces);
        ground.AddChild(new CollisionShape3D { Shape = mesh });
        AddChild(ground);
        var bodies = Bodies(8, 0);
        int falls = 0, firstFrame = -1;
        for (int i = 0; i < bodies.Count; i++)
        {
            var (b, nx) = bodies[i];
            b.GlobalPosition = new Vector3(21200 + i * 100, 400, 1200 + i * 80);   // above the terrain's top, 339
            float vy = 0;
            var h = Vector3.Zero;
            for (int f = 0; f < 600; f++)
            {
                if (f % 60 == 0)
                {
                    double a = rng.NextDouble() * Mathf.Tau;
                    float sp = (float)rng.NextDouble() * 2f;
                    h = new Vector3(Mathf.Cos((float)a) * sp, 0, Mathf.Sin((float)a) * sp);
                }
                if (f % 150 == 75) vy = 10f + (float)rng.NextDouble() * 10f;
                vy -= 0.6f;
                if ((nx.Move(h + new Vector3(0, vy, 0)) & Movement.NxController.Hit.Down) != 0) vy = 0;
                if (b.GlobalPosition.Y < -1f)
                {
                    falls++;
                    if (firstFrame < 0) firstFrame = f;
                    break;
                }
            }
            b.QueueFree();
        }
        ground.QueueFree();
        bool ok = falls == 0;
        GD.Print($"movement sync check: landings on a mesh terrain, {falls} of {bodies.Count} bodies fell through (first at frame {firstFrame}): {(ok ? "OK" : "FAIL")}");
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
        bool land = Landings(floor);
        bool alloc = MoveAllocations();
        bool meshLand = MeshLandings();
        bool ok = dir && land && alloc && meshLand;
        GetTree().Quit(ok ? 0 : 1);
    }
}
