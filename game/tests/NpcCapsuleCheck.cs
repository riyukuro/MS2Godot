using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_capsule.tscn) of a live npc's controller and the skill move's ignored groups:
/// - NpcLayer gives a live npc the controller capsule of its row (FUN_141670d50, unit_14108e1e0_npcactorandvibrate.cpp:2101)
///   in group 4 (CNpc::vfunc_126 at the spawn, :541), group 5 with the row's capsule ignore, and an npc added dead or
///   killed later goes to group 5 (StateDeadNPC::vfunc_4, statedead.cpp:248).
/// - The own player's controller move (PCBase::vfunc_68) stops at the live npc; a move that leaves out the skill motion's
///   ignoreCollisionGroups 20 (groups 2 and 4, heavy gunner JetBoots 10600081; StateSkill::FUN_1417edc20,
///   stateskill.cpp:2862-2874) passes it, and so does a plain move once the npc is dead.
/// - A live npc pressing the standing player (each move starts inside its capsule): the moves pass through it, as with no
///   npc there (SweepCapsuleBox d == 0, CharacterController.cpp:366-374), and allocate nothing.
/// - Click to move passes a live npc: the click ray picks the floor under it (FUN_140ad3740 picks block models only) and
///   the forward probe (groups 0x201, FUN_140ad2830) does not hit it.
/// Quits with 0 when all hold, else 1.
/// MS2_NPC_CAPSULE_PERF=N (a measurement, not a check): after the checks, N live npcs walk a circle each physics frame
/// with the runner stepping among them, 300 frames with their capsule bodies and 300 with the bodies freed; prints the
/// median of the npc move loop, the runner's controller step and the physics frame, and the main-thread bytes per frame.
/// </summary>
public partial class NpcCapsuleCheck : Node3D
{
    readonly Movement.NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    NpcLayer npcs;
    Player runner;
    int frames;
    int perfN, perfFrame, table0;
    List<Node3D> perfRoots;
    Vector3[] perfHome;
    readonly List<double> moveUs = new(), stepUs = new(), physMs = new();
    long perfBytes;
    System.Reflection.MethodInfo stepMethod;
    object[] stepArgs;

    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    static uint Layer(int id, Node parent) =>
        parent.GetNodeOrNull<StaticBody3D>($"Npc{id}/Capsule")?.CollisionLayer ?? 0;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new Ms2.Formats.World.NpcTable(packs);
        npcs = new NpcLayer(packs, builder, table, this);
        var ids = new List<int>();
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) != null) ids.Add(id);
        Check(ids.Count >= 2, $"{ids.Count} npc rows");
        if (ids.Count < 2) return;
        table0 = ids[0];
        Check(!table.Find(ids[0]).CapsuleIgnore, $"npc {ids[0]} has the capsule ignore");
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = ids[0] }));
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 2, NpcId = ids[0], Hidden = true }));
        // a row with the capsule ignore (the test's own copy of the flag, set back after the add)
        var ignoreRow = table.Find(ids[1]);
        bool keep = ignoreRow.CapsuleIgnore;
        ignoreRow.CapsuleIgnore = true;
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 3, NpcId = ids[1] }));
        ignoreRow.CapsuleIgnore = keep;
        Check(Layer(1, this) == 1u << 4, $"live npc capsule layer {Layer(1, this):x}, not group 4");
        Check(Layer(2, this) == 1u << 5, $"npc added dead: capsule layer {Layer(2, this):x}, not group 5");
        Check(Layer(3, this) == 1u << 5, $"capsule-ignore npc: capsule layer {Layer(3, this):x}, not group 5");
        var capsule = GetNodeOrNull<StaticBody3D>("Npc1/Capsule");
        Check(capsule != null && capsule.HasMeta(Movement.MoveConfig.ControllerMeta), "the capsule is no controller");

        var floor = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } });
        AddChild(floor);
        var visual = new Node3D();
        var anim = new AnimationPlayer();
        visual.AddChild(anim);
        runner = Player.Create(visual, anim, faceGrids);
        AddChild(runner);
        runner.SetProcess(false);
        runner.SetPhysicsProcess(false);
        runner.ScriptDriven = true;
        runner.BoundsMin = new Vector3(-2000, -2000, -1000);
        runner.BoundsMax = new Vector3(2000, 2000, 1000);
        // the other two npcs out of the way of the runner's line
        GetNode<Node3D>("Npc2").Position = new Vector3(0, 4, 1000);
        GetNode<Node3D>("Npc3").Position = new Vector3(0, 4, -1000);
    }

    // the runner from x -300 along +x by 10 a step for 60 steps, its moves leaving out exclude; the end x
    float Rush(uint exclude)
    {
        var step = typeof(Player).GetMethod("MoveStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        runner.GlobalPosition = new Vector3(-300, 0.5f, 0);
        for (int i = 0; i < 60; i++) step.Invoke(runner, new object[] { new Vector3(10, 0, 0), 1f / 60, exclude });
        return runner.GlobalPosition.X;
    }

    // The capsule body takes a moved root's transform at the next frame's flush; these steps run inside one frame.
    static void Place(Node3D npc, Vector3 at)
    {
        npc.GlobalPosition = at;
        npc.GetNode<Node3D>("Capsule").ForceUpdateTransform();
    }

    // A live npc pressing the runner, as a mob walking into a standing player in melee: npc 4's capsule 20 behind the
    // runner each step, so every step starts inside it, and the runner stepping 2 along +x. The sweeps pass through a
    // controller they start inside (SweepCapsuleBox d == 0, CharacterController.cpp:366-374), so the runner ends where it
    // does with no npc there, and the start inside the npc's capsule allocates nothing.
    void Pressed()
    {
        var step = (System.Func<Vector3, float, uint, Movement.NxController.Hit>)System.Delegate.CreateDelegate(
            typeof(System.Func<Vector3, float, uint, Movement.NxController.Hit>), runner,
            typeof(Player).GetMethod("MoveStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 4, NpcId = table0 }));
        var npc = GetNode<Node3D>("Npc4");
        Check(Layer(4, this) == 1u << 4, $"pressing npc capsule layer {Layer(4, this):x}, not group 4");
        float Walk(bool pressed, out long bytes)
        {
            runner.GlobalPosition = new Vector3(-300, 0.5f, 500);
            bytes = 0;
            for (int i = 0; i < 80; i++)
            {
                Place(npc, pressed ? runner.GlobalPosition + new Vector3(-20, 0, 0) : new Vector3(0, 4, -3000));
                long b0 = System.GC.GetAllocatedBytesForCurrentThread();
                step(new Vector3(2, 0, 0), 1f / 60, 0);
                if (i >= 20) bytes += System.GC.GetAllocatedBytesForCurrentThread() - b0;
            }
            return runner.GlobalPosition.X;
        }
        float free = Walk(false, out long freeBytes), pressedX = Walk(true, out long pressedBytes);
        Check(Mathf.Abs(pressedX - free) < 0.01f, $"the pressed runner ends at x {pressedX:0.000}, with no npc at {free:0.000}");
        Check(pressedBytes / 60 <= 8, $"a step inside the npc's capsule allocates {pressedBytes / 60} B (with no npc {freeBytes / 60} B)");
        GD.Print($"npc capsule check: pressed by a live npc the runner ends at x {pressedX:0.000} (no npc {free:0.000}), " +
                 $"{pressedBytes / 60} B per step (no npc {freeBytes / 60} B)");
        ClickPast(npc);
        npcs.Remove(4, false);
    }

    // The click to move ray and its forward probe pass a live npc (FUN_140ad3740 picks block models only; FUN_140ad2830's
    // probe is groups 0x201): a click down onto the npc picks the floor under it, the probe at the npc has no hit, and the
    // probe still hits a group 0 wall. An all-layers ray (what both cast before) hits the capsule.
    void ClickPast(Node3D npc)
    {
        var space = GetWorld3D().DirectSpaceState;
        Place(npc, new Vector3(600, 4, 500));
        var top = new Vector3(600, 1000, 500);
        float AllLayers(Vector3 a, Vector3 b)
        {
            using var h = space.IntersectRay(PhysicsRayQueryParameters3D.Create(a, b, uint.MaxValue, new Godot.Collections.Array<Rid> { runner.GetRid() }));
            return h.Count == 0 ? float.NaN : ((Vector3)h[PhysicsKeys.Position]).DistanceTo(a);
        }
        float allDown = AllLayers(top, top + Vector3.Down * 2000);
        var click = Movement.MouseMove.ClickPoint(space, new RayQuery(), top, top + Vector3.Down * 2000, runner.GetRid());
        Check(click is { } c && Mathf.Abs(c.Position.Y) < 0.5f, $"a click onto the live npc picks {click?.Position}, not the floor under it");
        Check(allDown < 990, $"the all-layers click ray passes the npc too ({allDown:0.0}): the setup does not test it");
        runner.GlobalPosition = new Vector3(560, 0.5f, 500);
        var probe = RayQuery.CastRay(Movement.MouseMove.ProbeGroups);
        var atNpc = Movement.MouseMove.ProbeAhead(space, probe, runner.GlobalPosition, Vector3.Right, runner.GetRid());
        var up = runner.GlobalPosition + new Vector3(0, Movement.MoveConfig.MouseProbeUp, 0);
        float allAhead = AllLayers(up, up + Vector3.Right * Movement.MoveConfig.MouseProbeLen);
        Check(atNpc == null, $"the forward probe hits the live npc at {atNpc?.Position}");
        Check(!float.IsNaN(allAhead), "the all-layers probe misses the npc too: the setup does not test it");
        Place(npc, new Vector3(0, 4, -3000));
        var wall = new StaticBody3D { Position = new Vector3(600, 100, 500) };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20, 200, 200) } });
        AddChild(wall);
        var atWall = Movement.MouseMove.ProbeAhead(space, probe, runner.GlobalPosition, Vector3.Right, runner.GetRid());
        Check(atWall != null, "the forward probe misses a group 0 wall");
        wall.Free();
        GD.Print($"npc capsule check: a click onto the live npc picks y {click?.Position.Y:0.00} (all layers: the capsule, " +
                 $"{allDown:0.0} down), the probe at it {(atNpc == null ? "no hit" : "a hit")} (all layers: {allAhead:0.0} ahead), " +
                 $"at a group 0 wall {(atWall == null ? "no hit" : "a hit")});
    }

    void Run()
    {
        Check(npcs != null, "the setup did not finish (no npc layer)");
        if (runner != null)
        {
            float plain = Rush(0), ignored = Rush(20);
            Check(plain < 0, $"a plain move went through the live npc (x {plain:0})");
            Check(ignored > 250, $"a move leaving out groups 2 and 4 stopped at the npc (x {ignored:0})");
            npcs.Remove(1, true);
            Check(Layer(1, this) == 1u << 5, $"killed npc capsule layer {Layer(1, this):x}, not group 5");
            float dead = Rush(0);
            Check(dead > 250, $"a plain move stopped at the dead npc (x {dead:0})");
            GD.Print($"npc capsule check: plain move ends at x {plain:0}, the ignore-20 move at {ignored:0}, past the dead npc at {dead:0}");
            Pressed();
        }
        foreach (var f in fails) GD.Print($"npc capsule check: FAIL {f}");
        GD.Print($"npc capsule check: {(fails.Count == 0 ? "OK" : "FAIL")}");
        if (fails.Count == 0 && runner != null && int.TryParse(OS.GetEnvironment("MS2_NPC_CAPSULE_PERF"), out perfN) && perfN > 0)
        {
            StartPerf();
            return;
        }
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void StartPerf()
    {
        int id = table0;
        perfRoots = new List<Node3D>(perfN);
        perfHome = new Vector3[perfN];
        int side = (int)Mathf.Ceil(Mathf.Sqrt(perfN));
        for (int i = 0; i < perfN; i++)
        {
            npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 100 + i, NpcId = id }));
            var root = GetNode<Node3D>($"Npc{100 + i}");
            perfHome[i] = new Vector3(-1200 + 2400f * (i % side) / side, 4, -1200 + 2400f * (i / side) / side);
            root.Position = perfHome[i];
            perfRoots.Add(root);
        }
        stepMethod = typeof(Player).GetMethod("MoveStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        stepArgs = new object[] { new Vector3(10, 0, 0), 1f / 60, 0u };
        runner.GlobalPosition = new Vector3(-1200, 0.5f, 0);
        perfFrame = 0;
        SetPhysicsProcess(true);
    }

    static double Median(List<double> v) { v.Sort(); return v.Count == 0 ? 0 : v[v.Count / 2]; }

    // one perf frame: frames 0-29 settle, 30-329 measured with bodies, then the bodies freed, 330-359 settle, 360-659 measured
    void PerfFrame()
    {
        int f = perfFrame++;
        bool measure = f is >= 30 and < 330 or >= 360 and < 660;
        if (measure) physMs.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000);
        long b0 = System.GC.GetAllocatedBytesForCurrentThread();
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        float a = f * 0.05f;
        for (int i = 0; i < perfRoots.Count; i++)
            perfRoots[i].Position = perfHome[i] + new Vector3(60 * Mathf.Cos(a + i), 0, 60 * Mathf.Sin(a + i));
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (runner.GlobalPosition.X > 1200) runner.GlobalPosition = new Vector3(-1200, 0.5f, runner.GlobalPosition.Z);
        stepMethod.Invoke(runner, stepArgs);
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        long b1 = System.GC.GetAllocatedBytesForCurrentThread();
        if (measure)
        {
            moveUs.Add((t1 - t0) * 1e6 / System.Diagnostics.Stopwatch.Frequency);
            stepUs.Add((t2 - t1) * 1e6 / System.Diagnostics.Stopwatch.Frequency);
            perfBytes += b1 - b0;
        }
        if (f == 329 || f == 659)
        {
            string tag = f == 329 ? "with bodies" : "bodies freed";
            int bodies = 0;
            foreach (var r in perfRoots) if (r.GetNodeOrNull("Capsule") != null) bodies++;
            GD.Print($"npc capsule perf: {perfN} live npcs, {tag} ({bodies} bodies): npc move loop median {Median(moveUs):0.0} us, " +
                     $"runner step median {Median(stepUs):0.0} us, physics frame median {Median(physMs):0.000} ms, " +
                     $"main-thread bytes/frame {perfBytes / 300} (incl. the step's reflection call)");
            moveUs.Clear(); stepUs.Clear(); physMs.Clear(); perfBytes = 0;
            if (f == 329) foreach (var r in perfRoots) r.GetNodeOrNull("Capsule")?.Free();
            else { SetPhysicsProcess(false); GetTree().Quit(0); }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (perfRoots != null) { PerfFrame(); return; }
        if (++frames < 3) return;
        SetPhysicsProcess(false);
        try { Run(); }
        catch (System.Exception e) { GD.Print($"npc capsule check: {e.InnerException ?? e}: FAIL"); GetTree().Quit(1); }
    }
}
