using System.Collections.Generic;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/movement_sync.tscn) of MyPC's movement and its sync out.
/// - Sync direction: the record's m_vel3c is the move buffer's input (m_pos98 = Unk_PCMove::GetMoveDirection,
///   unit_141682f80.cpp:313-335: x +1 left, y +1 forward), packed by FUN_140836250 (statesyncobject.cpp:202-220). The
///   receiver turns it by the sender's camera (PCBase::CalcMoveDirection, pcbase.cpp:2828-2840). With the camera turned a
///   quarter, the right key must still send x -1, y 0 (flags 0 + 8) and the forward key x 0, y +1 (flags 1 + 12).
/// - The controller (NxController, MyPC's and the remotes' body): a walking body's sweeps, and resting and landing
///   bodies' (whose sweeps end in the end test), allocate at most 8 B per move;
///   bodies jumping on a 200 m floor box and on a mesh terrain never fall through, nor bodies crossing one-sided walls from
///   behind on that box.
/// - Riding: the game tick makes the mount's records (scenestategame.cpp:1362-1368), which go out as RideSync
///   (StateRideeController::vfunc_0, state.cpp:537-562); the rider's own records wait for the ride off.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class MovementSyncCheck : Node3D
{
    readonly Movement.NxController.FaceGrids faceGrids = new();   // the scene is the field: one mesh face cache for its bodies (Field.FaceGrids)
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
        runner = Player.Create(visual, anim, faceGrids);
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

    // walks on a bare mount (no model: the seat is the visual) and off it again, counting the packets of each kind
    bool RideSync()
    {
        int ride = 0, foot = 0;
        runner.RideSyncOut += list => ride += list.Count;
        runner.StateSyncOut += list => foot += list.Count;
        runner.RideOn(new Mount { Data = new Movement.RideData { Id = 1, RunSpeed = 5 }, Visual = new Node3D() });
        Walk(new Vector2(0, 1));
        int rideOn = ride, footOn = foot;
        runner.RideOff();
        Walk(new Vector2(0, 1));
        bool ok = rideOn > 0 && footOn == 0 && ride == rideOn && foot > 0;
        GD.Print($"movement sync check: riding sent {rideOn} RideSync and {footOn} UserSync record(s); on foot again {ride - rideOn} and {foot}: {(ok ? "OK" : "FAIL")}");
        return ok;
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
            bodies.Add((b, new Movement.NxController(b, cs, faceGrids)));
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

    // Eight bodies resting on the 200 m floor box under gravity, and eight landing on it again and again (dropped from 1 to
    // 3 m, falling until they rest): managed bytes and time per move, after a warm-up. Each move's sweep ends overlapping the
    // box often, so the end test runs on most of them; it must decide from the probe's own result objects. A walking body's
    // free steps never reach it, so MoveAllocations cannot see this.
    bool RestingAllocations()
    {
        var bodies = Bodies(8, -6000);
        var vy = new float[bodies.Count];
        var rest = new int[bodies.Count];
        for (int i = 0; i < bodies.Count; i++) bodies[i].b.GlobalPosition = new Vector3(-6000 + i * 300, 1, -3000);
        long restBytes = 0, landBytes = 0;
        int restMoves = 0, landMoves = 0;
        var swRest = new System.Diagnostics.Stopwatch();
        var swLand = new System.Diagnostics.Stopwatch();
        const int warm = 120, frames = 720;
        for (int f = 0; f < warm + frames; f++)
            for (int i = 0; i < bodies.Count; i++)
            {
                var (_, nx) = bodies[i];
                vy[i] -= 0.6f;
                long a0 = System.GC.GetAllocatedBytesForCurrentThread();
                if (f >= warm) swRest.Start();
                if ((nx.Move(new Vector3(0, vy[i], 0)) & Movement.NxController.Hit.Down) != 0) vy[i] = 0;
                swRest.Stop();
                if (f >= warm) { restBytes += System.GC.GetAllocatedBytesForCurrentThread() - a0; restMoves++; }
            }
        float lowRest = float.MaxValue;
        foreach (var (b, _) in bodies) { lowRest = Mathf.Min(lowRest, b.GlobalPosition.Y); b.QueueFree(); }
        bodies = Bodies(8, -6000);
        for (int i = 0; i < bodies.Count; i++) { bodies[i].b.GlobalPosition = new Vector3(-6000 + i * 300, 100 + i * 25, -2000); vy[i] = 0; rest[i] = 0; }
        int landings = 0;
        float lowLand = float.MaxValue;
        for (int f = 0; f < warm + frames; f++)
            for (int i = 0; i < bodies.Count; i++)
            {
                var (b, nx) = bodies[i];
                vy[i] -= 0.6f;
                long a0 = System.GC.GetAllocatedBytesForCurrentThread();
                if (f >= warm) swLand.Start();
                var hit = nx.Move(new Vector3(0, vy[i], 0));
                swLand.Stop();
                if (f >= warm) { landBytes += System.GC.GetAllocatedBytesForCurrentThread() - a0; landMoves++; }
                lowLand = Mathf.Min(lowLand, b.GlobalPosition.Y);
                if ((hit & Movement.NxController.Hit.Down) != 0) { vy[i] = 0; rest[i]++; }
                if (rest[i] >= 10)
                {
                    landings++;
                    rest[i] = 0;
                    b.GlobalPosition = new Vector3(b.GlobalPosition.X, 100 + (f * 7 + i * 31) % 200, b.GlobalPosition.Z);
                }
            }
        foreach (var (b, _) in bodies) b.QueueFree();
        float perRest = restBytes / (float)restMoves, perLand = landBytes / (float)landMoves;
        bool ok = perRest <= 8f && perLand <= 8f && lowRest > -1f && lowLand > -1f;
        GD.Print($"movement sync check: resting bodies allocate {perRest:0.0} B per move (cap 8), {swRest.Elapsed.TotalMilliseconds * 1000 / restMoves:0.0} us per move; " +
                 $"landing bodies ({landings} landings) {perLand:0.0} B per move (cap 8), {swLand.Elapsed.TotalMilliseconds * 1000 / landMoves:0.0} us per move; " +
                 $"lowest feet {Mathf.Min(lowRest, lowLand):0.00}: {(ok ? "OK" : "FAIL")}");
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
        // the same lap with groups left out of the sweeps (after a controller hit)
        nx.IgnoreGroups = Movement.MoveConfig.ControllerHitIgnore;
        bytes = 0;
        sw.Reset();
        for (int f = 0; f < lap; f++)
        {
            long a0 = System.GC.GetAllocatedBytesForCurrentThread();
            sw.Start();
            nx.Move(dirs[f / 120 % 4]);
            sw.Stop();
            bytes += System.GC.GetAllocatedBytesForCurrentThread() - a0;
        }
        nx.IgnoreGroups = 0;
        float perIgnored = bytes / (float)lap;
        bool okIgnored = perIgnored <= 8f;
        GD.Print($"movement sync check: with ignored groups {perIgnored:0.0} B per move (cap 8), {sw.Elapsed.TotalMilliseconds * 1000 / lap:0.0} us per move: {(okIgnored ? "OK" : "FAIL")}");
        b.QueueFree();
        return ok && okIgnored;
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

    // A one-sided mesh (as the field's, FieldBuilder) of triangles facing n: a triangle (a, b, c) faces (c - a) x (b - a)
    // (the shape holds v0, v2, v1).
    static ConcavePolygonShape3D OneSided(params (Vector3 centre, Vector3 n, float half)[] quads)
    {
        var faces = new Vector3[quads.Length * 6];
        int k = 0;
        foreach (var (o, n, r) in quads)
        {
            var u = (Mathf.Abs(n.Y) > 0.5f ? Vector3.Right : Vector3.Up) * r;
            var v = n.Cross(u);
            Vector3 p0 = o - u - v, p1 = o + u - v, p2 = o + u + v, p3 = o - u + v;
            foreach (var (a, b0, c0) in new[] { (p0, p1, p2), (p0, p2, p3) })
            {
                var (b, c) = (c0 - a).Cross(b0 - a).Dot(n) > 0 ? (b0, c0) : (c0, b0);
                faces[k++] = a; faces[k++] = b; faces[k++] = c;
            }
        }
        return new ConcavePolygonShape3D { Data = faces, BackfaceCollision = false };
    }

    // One-sided meshes count from the front only, as the client's triangle sweeps cull back faces: a body falls through a
    // platform facing down, walks out of a closed prop of outward faces, and jumps up through a platform facing up onto it.
    bool OneSidedMeshes()
    {
        var at = new Vector3(-8000, 0, -8000);
        var props = new StaticBody3D { Position = at };
        props.AddChild(new CollisionShape3D { Shape = OneSided((new Vector3(0, 150, 0), Vector3.Down, 300)) });
        props.AddChild(new CollisionShape3D { Shape = OneSided((new Vector3(1000, 150, 0), Vector3.Up, 300)) });
        var box = new List<(Vector3, Vector3, float)>();
        foreach (var n in new[] { Vector3.Up, Vector3.Down, Vector3.Left, Vector3.Right, Vector3.Forward, Vector3.Back })
            box.Add((new Vector3(2000, 50, 0) + n * 50, n, 50));
        props.AddChild(new CollisionShape3D { Shape = OneSided(box.ToArray()) });
        AddChild(props);
        var bodies = Bodies(3, 0);

        // landing from above on the platform facing down: through it, onto the floor
        var (b0, nx0) = bodies[0];
        b0.GlobalPosition = at + new Vector3(0, 300, 0);
        float vy = 0;
        for (int f = 0; f < 120; f++) { vy -= 0.6f; if ((nx0.Move(new Vector3(0, vy, 0)) & Movement.NxController.Hit.Down) != 0) vy = 0; }
        float down = b0.GlobalPosition.Y;
        bool okDown = down > -1f && down < 5f;

        // standing inside the closed prop and walking out of it
        var (b1, nx1) = bodies[1];
        b1.GlobalPosition = at + new Vector3(2000, 1, 0);
        for (int f = 0; f < 60; f++) nx1.Move(new Vector3(4, -1, 0));
        float outX = b1.GlobalPosition.X - (at.X + 2000);
        bool okOut = outX > 200f;

        // jumping up through the platform facing up: onto its top
        var (b2, nx2) = bodies[2];
        b2.GlobalPosition = at + new Vector3(1000, 1, 0);
        vy = 20f;
        for (int f = 0; f < 120; f++) { vy -= 0.6f; if ((nx2.Move(new Vector3(0, vy, 0)) & Movement.NxController.Hit.Down) != 0) vy = 0; }
        float up = b2.GlobalPosition.Y;
        bool okUp = up > 145f && up < 160f;

        foreach (var (b, _) in bodies) b.QueueFree();
        props.QueueFree();
        bool ok = okDown && okOut && okUp;
        GD.Print($"movement sync check: one-sided meshes, feet {down:0.0} under a platform facing down (on the floor), {outX:0.0} walked out of a closed prop, feet {up:0.0} after jumping up through a platform facing up at 150: {(ok ? "OK" : "FAIL")}");
        return ok;
    }

    // One-sided walls crossed from behind on the 200 m floor box: 80 walls facing +X every 200 cm, eight bodies walking +X
    // at 4-8 per frame under gravity, started on a wall, between walls, and with jumps. The client's triangle sweeps cull back
    // faces (the body walks through, inside the wall for a few moves) and its box sweep does not miss the floor
    // (CharacterController.cpp:297-374), so no body falls through. Then a body standing 10 cm through a wall, gravity only.
    bool OneSidedWalls(StaticBody3D floor)
    {
        const float z0 = 6000, x0 = -8000;
        var walls = new StaticBody3D();
        var quads = new (Vector3, Vector3, float)[80];
        for (int w = 0; w < quads.Length; w++) quads[w] = (new Vector3(x0 + w * 200, 300, z0), Vector3.Right, 600);
        walls.AddChild(new CollisionShape3D { Shape = OneSided(quads) });
        AddChild(walls);
        var rng = new System.Random(11);
        var line = new System.Text.StringBuilder();
        bool ok = true;
        long wallBytes = 0;
        int wallMoves = 0;
        var swWall = new System.Diagnostics.Stopwatch();
        foreach (var (name, dx, jumps) in new[] { ("on a wall", 0f, false), ("between walls", 100f, false), ("on a wall with jumps", 0f, true) })
        {
            var bodies = Bodies(8, 0);
            int falls = 0, firstFrame = -1;
            float least = float.MaxValue;
            for (int i = 0; i < bodies.Count; i++)
            {
                var (b, nx) = bodies[i];
                b.GlobalPosition = new Vector3(x0 + 200 * (i % 4) + dx, 1, z0 - 400 + i * 100);
                float x = b.GlobalPosition.X, sp = 4f + (float)rng.NextDouble() * 4f, vy = 0;
                for (int f = 0; f < 1800; f++)
                {
                    if (jumps && f % 150 == 75) vy = 10f + (float)rng.NextDouble() * 10f;
                    vy -= 0.6f;
                    long a0 = System.GC.GetAllocatedBytesForCurrentThread();
                    swWall.Start();
                    var hit = nx.Move(new Vector3(sp, vy, 0));
                    swWall.Stop();
                    wallBytes += System.GC.GetAllocatedBytesForCurrentThread() - a0;
                    wallMoves++;
                    if ((hit & Movement.NxController.Hit.Down) != 0) vy = 0;
                    if (b.GlobalPosition.Y < -1f)
                    {
                        falls++;
                        if (firstFrame < 0 || f < firstFrame) firstFrame = f;
                        break;
                    }
                }
                least = Mathf.Min(least, b.GlobalPosition.X - x);
                b.QueueFree();
            }
            bool okCase = falls == 0 && least > 2000f;
            ok &= okCase;
            line.Append($"{name} {falls} of 8 fell (first at frame {firstFrame}), least walked {least:0}; ");
        }
        // a body standing in a wall, gravity only: 8 bodies, from 10 cm into its back to 10 cm past its centre and into its front
        var standing = Bodies(8, 0);
        float low = float.MaxValue;
        int sunk = 0;
        for (int i = 0; i < standing.Count; i++)
        {
            var (b4, nx4) = standing[i];
            float wall = x0 + 200 * (2 + i), r = Movement.MoveConfig.Radius;
            float[] into = { -r + 10, -r / 2, -10, 0, 10, r / 2, r - 10, r - 1 };
            b4.GlobalPosition = new Vector3(wall + into[i], 25, z0 + 100 * (i - 4));
            float vy4 = 0, lowI = float.MaxValue;
            for (int f = 0; f < 300; f++)
            {
                vy4 -= 0.6f;
                if ((nx4.Move(new Vector3(0, vy4, 0)) & Movement.NxController.Hit.Down) != 0) vy4 = 0;
                lowI = Mathf.Min(lowI, b4.GlobalPosition.Y);
            }
            if (lowI <= -1f) sunk++;
            low = Mathf.Min(low, lowI);
            b4.QueueFree();
        }
        walls.QueueFree();
        bool ok4 = sunk == 0;
        ok &= ok4;
        GD.Print($"movement sync check: one-sided walls crossed from behind on a 200 m floor box, {line}standing in a wall, gravity only, {sunk} of 8 fell, lowest feet {low:0.0}; walking {wallBytes / (float)wallMoves:0.0} B per move, {swWall.Elapsed.TotalMilliseconds * 1000 / wallMoves:0.0} us per move: {(ok ? "OK" : "FAIL")}");
        return ok;
    }

    // The shapes under the spawn of field 02020140_bf, as the field builds them (dumped from the running game, kept to the bit):
    // 150 cm cubes of one chunk body, three as one-sided 12-triangle meshes (the spawn stands on one's top face, its tip on
    // that face's diagonal) and the rest as boxes. Kind, basis columns X Y Z, origin.
    static readonly (char kind, float[] xf)[] SpawnCubes =
    {
        ('B', new[] { 1.1924881E-08f, 4.371139E-08f, 1f, 1f, -5.2125306E-16f, -1.1924881E-08f, 0f, 1f, -4.371139E-08f, -8250f, 8775f, 7949.9995f }),
        ('M', new[] { 1f, 0f, 0f, 0f, -4.371139E-08f, -1f, 0f, 1f, -4.371139E-08f, -8250f, 8700f, 8099.9995f }),
        ('B', new[] { -4.371139E-08f, -4.371139E-08f, -1f, -1f, 1.9106855E-15f, 4.371139E-08f, 0f, 1f, -4.371139E-08f, -8250f, 8775f, 8250f }),
        ('B', new[] { -4.371139E-08f, -4.371139E-08f, -1f, -1f, 1.9106855E-15f, 4.371139E-08f, 0f, 1f, -4.371139E-08f, -8400f, 8625f, 8250f }),
        ('M', new[] { 1f, 0f, 0f, 0f, -4.371139E-08f, -1f, 0f, 1f, -4.371139E-08f, -8400f, 8700f, 8099.9995f }),
        ('B', new[] { -1f, 3.821371E-15f, 8.742278E-08f, 8.742278E-08f, 4.371139E-08f, 1f, 0f, 1f, -4.371139E-08f, -8550f, 8625f, 8099.9995f }),
        ('B', new[] { -1f, 3.821371E-15f, 8.742278E-08f, 8.742278E-08f, 4.371139E-08f, 1f, 0f, 1f, -4.371139E-08f, -8550f, 8775f, 7949.9995f }),
        ('B', new[] { -4.371139E-08f, -4.371139E-08f, -1f, -1f, 1.9106855E-15f, 4.371139E-08f, 0f, 1f, -4.371139E-08f, -8550f, 8625f, 8250f }),
        ('B', new[] { -1f, 3.821371E-15f, 8.742278E-08f, 8.742278E-08f, 4.371139E-08f, 1f, 0f, 1f, -4.371139E-08f, -8550f, 8775f, 8250f }),
        ('B', new[] { 1f, 0f, 0f, 0f, -4.371139E-08f, -1f, 0f, 1f, -4.371139E-08f, -8400f, 8775f, 8250f }),
        ('B', new[] { -4.371139E-08f, -4.371139E-08f, -1f, -1f, 1.9106855E-15f, 4.371139E-08f, 0f, 1f, -4.371139E-08f, -8550f, 8175.0005f, 8250f }),
        ('B', new[] { -4.371139E-08f, -4.371139E-08f, -1f, -1f, 1.9106855E-15f, 4.371139E-08f, 0f, 1f, -4.371139E-08f, -8550f, 8025.0005f, 8250f }),
        ('M', new[] { -1f, -5.3531012E-24f, -1.2246469E-16f, -1.2246469E-16f, 4.371139E-08f, 1f, 0f, 1f, -4.371139E-08f, -8400f, 8700f, 7949.9995f }),
        ('B', new[] { -4.371139E-08f, -4.371139E-08f, -1f, -1f, 1.9106855E-15f, 4.371139E-08f, 0f, 1f, -4.371139E-08f, -8550f, 7875.0005f, 8250f }),
        ('B', new[] { 1f, 0f, 0f, 0f, -4.371139E-08f, -1f, 0f, 1f, -4.371139E-08f, -8550f, 8775f, 8099.9995f }),
        ('B', new[] { -4.371139E-08f, -4.371139E-08f, -1f, -1f, 1.9106855E-15f, 4.371139E-08f, 0f, 1f, -4.371139E-08f, -8550f, 8325f, 8250f }),
        ('B', new[] { -4.371139E-08f, -4.371139E-08f, -1f, -1f, 1.9106855E-15f, 4.371139E-08f, 0f, 1f, -4.371139E-08f, -8550f, 8475f, 8250f }),
    };
    static readonly Vector3[] CubeFaces =
    {
        new(75, 75, 150), new(75, -75, 0), new(75, -75, 150), new(75, -75, 0), new(75, 75, 150), new(75, 75, 0),
        new(-75, -75, 0), new(75, -75, 150), new(75, -75, 0), new(-75, -75, 150), new(75, 75, 150), new(75, -75, 150),
        new(75, 75, 0), new(-75, -75, 0), new(75, -75, 0), new(-75, 75, 150), new(75, 75, 0), new(75, 75, 150),
        new(-75, -75, 150), new(-75, 75, 0), new(-75, 75, 150), new(-75, 75, 0), new(-75, -75, 150), new(-75, -75, 0),
        new(75, -75, 150), new(-75, -75, 0), new(-75, -75, 150), new(-75, -75, 0), new(75, 75, 0), new(-75, 75, 0),
        new(75, 75, 0), new(-75, 75, 150), new(-75, 75, 0), new(75, 75, 150), new(-75, -75, 150), new(-75, 75, 150),
    };

    // A body standing on the 02020140_bf spawn, falling by every length from 0.5 to 30 in 1/1024 steps (gravity's moves).
    // GodotPhysics3D's cast misses the mesh cube's top face for some lengths (its GJK distance on the triangle edge under the
    // tip reports the motion separate; 2.6133 in the field), so the move ended inside the cube and the next one fell through
    // it, out of the world. The client's sweep does not miss a triangle under the capsule (CharacterController.cpp:297-374).
    bool SpawnCubeStands()
    {
        var chunk = new StaticBody3D { CollisionLayer = 512 };
        var face = new ConcavePolygonShape3D { Data = CubeFaces, BackfaceCollision = false };
        var box = new BoxShape3D { Size = new Vector3(150, 150, 150) };
        foreach (var (kind, f) in SpawnCubes)
            chunk.AddChild(new CollisionShape3D
            {
                Shape = kind == 'M' ? face : box,
                Transform = new Transform3D(new Basis(new Vector3(f[0], f[1], f[2]), new Vector3(f[3], f[4], f[5]), new Vector3(f[6], f[7], f[8])), new Vector3(f[9], f[10], f[11])),
            });
        AddChild(chunk);
        var (b, nx) = Bodies(1, 0)[0];
        var spawn = new Vector3(-8400, 8851, 8100);
        b.GlobalPosition = spawn;
        int moves = 0, sank = 0;
        float firstLen = -1, low = float.MaxValue;
        for (int k = 0; k <= 29 * 1024; k++)
        {
            float len = 0.5f + k / 1024f;
            nx.Move(new Vector3(0, -len, 0));
            moves++;
            float y = b.GlobalPosition.Y;
            low = Mathf.Min(low, y);
            if (y < 8849f)
            {
                sank++;
                if (firstLen < 0) firstLen = len;
                b.GlobalPosition = spawn;
            }
        }
        b.QueueFree();
        chunk.QueueFree();
        bool ok = sank == 0;
        GD.Print($"movement sync check: 02020140_bf spawn on its mesh cube, {moves} falls from 0.5 to 29.5, {sank} sank into the cube (first at length {firstLen:0.####}), lowest feet {low:0.##}: {(ok ? "OK" : "FAIL")}");
        return ok;
    }

    void Run()
    {
        runner.CameraYaw = Mathf.Pi / 2;
        int right = Walk(new Vector2(1, 0)), forward = Walk(new Vector2(0, 1));
        bool dir = right == 8 && forward == 13;
        GD.Print($"movement sync check: camera a quarter turned, right key flags {right}, forward key flags {forward}: {(dir ? "OK" : "FAIL")}");
        bool ride = RideSync();
        var floor = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20000, 100, 20000) } });
        AddChild(floor);
        bool spawn = SpawnCubeStands();
        bool land = Landings(floor);
        bool alloc = MoveAllocations();
        bool restAlloc = RestingAllocations();
        bool meshLand = MeshLandings();
        bool oneSided = OneSidedMeshes();
        bool walls = OneSidedWalls(floor);
        bool ok = dir && ride && spawn && land && alloc && restAlloc && meshLand && oneSided && walls;
        GetTree().Quit(ok ? 0 : 1);
    }
}
