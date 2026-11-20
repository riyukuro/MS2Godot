using System.Diagnostics;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Movement;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless cost probe (res://tests/remote_cost.tscn, MS2_REMOTES = the number of remote PCs, default 8): the remote PCs walk a
/// square on a stone floor from sync records (a new direction every 60 frames, a jump every 150), then stand (an idle record),
/// fixed 60 fps. After 300 warm-up
/// frames it measures 300 frames of the remote ticks only: main-thread bytes per frame, µs per remote tick (median and worst
/// frame) and the engine object count change. A PC that has dropped through the test floor (a jump landing the controller
/// sweep missed on the large test box, NxController's) keeps sweeping as it falls; its cost is left out and it is counted. Then the same frames for the previous model (a body gliding
/// to the record's position with an idle/walk/run pick by speed). Prints one line per model; quits 0 when the replay allocates
/// under 64 bytes per frame standing (a walking PC's controller sweeps every frame: NxController's cost).
/// </summary>
public partial class RemoteCostCheck : Node3D
{
    readonly List<RemotePc> pcs = new();
    readonly List<(Node3D root, Node3D visual, AnimationPlayer anim)> glides = new();
    int n, frame;
    const float Dt = 1f / 60f;
    readonly List<double> frameUs = new();
    long bytes; ulong objects0;

    public override void _Ready()
    {
        n = int.TryParse(System.Environment.GetEnvironmentVariable("MS2_REMOTES"), out var k) ? k : 8;
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var body = new StaticBody3D { CollisionLayer = 1 };
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20000, 100, 20000) }, Position = new Vector3(0, -50, 0) };
        FieldBuilder.TagGround(cs, "stone");
        body.AddChild(cs);
        AddChild(body);
        var md = builder.LoadModel("female/f_body.nif");
        ActionAnims anims = null; AnimationLibrary lib = null;
        for (int i = 0; i < n; i++)
        {
            var visual = builder.Build(md, "Female");
            var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            if (anims == null) { anims = ActionAnims.Load(packs, builder, md, anim, "female"); lib = anim.GetAnimationLibrary(""); }
            else { anim.RemoveAnimationLibrary(""); anim.AddAnimationLibrary("", lib); }
            var pc = RemotePc.Create($"Pc{i}", visual, anim, anims, MoveConfig.SceneGravity, new Vector3(i * 300, 25, 0), 0);
            AddChild(pc.Body);
            pcs.Add(pc);
        }
        // the previous model's bodies: a node gliding with its own animation player (shared library)
        for (int i = 0; i < n; i++)
        {
            var visual = builder.Build(md, "Female");
            var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            anim.RemoveAnimationLibrary(""); anim.AddAnimationLibrary("", lib);
            var root = new Node3D { Position = new Vector3(i * 300, 0, 3000) };
            root.AddChild(visual);
            AddChild(root);
            glides.Add((root, visual, anim));
        }
    }

    static readonly System.Numerics.Vector2[] Dirs = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    static SyncRecord Rec(int state, int sub, System.Numerics.Vector3 p, System.Numerics.Vector2 move)
    {
        int ax = move.X == 0 ? 1 : move.X > 0 ? 2 : 0, ay = move.Y == 0 ? 2 : move.Y > 0 ? 3 : 1;
        return SyncRecord.From(new StateSync
        {
            State = (byte)state, SubState = (byte)sub, Position = new Vector3S((short)p.X, (short)p.Y, (short)p.Z),
            Speed = new Vector3S(57, 90, 0), Unknown1 = 2, SyncNumber = 0x7fffffff, Animation = (byte)(ax + 4 * ay),
        });
    }

    // the previous model's state per body
    readonly Dictionary<int, (Vector3 target, Vector3 last, float speed, string cur)> glide = new();

    void Glide(int i, float dt)
    {
        var (root, visual, anim) = glides[i];
        glide.TryGetValue(i, out var g);
        var d = g.target - root.Position;
        if (d.Length() > 400) root.Position = g.target;
        else root.Position += d * FrameRate.Ease(12f / FrameRate.Reference, dt);
        visual.Rotation = new Vector3(0, Mathf.LerpAngle(visual.Rotation.Y, 0, FrameRate.Ease(14f / FrameRate.Reference, dt)), 0);
        float sp = (root.Position - g.last).Length() / Mathf.Max(dt, 1e-4f);
        g.speed = Mathf.Lerp(g.speed, sp, FrameRate.Ease(8f / FrameRate.Reference, dt));
        g.last = root.Position;
        string a = g.speed < 25 ? "idle_a" : g.speed < 220 ? "walk_a" : "run_a";
        if (a != g.cur && anim.HasAnimation(a)) { anim.Play(a, 0.15); g.cur = a; }
        glide[i] = g;
    }

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        if (frame == 3) foreach (var pc in pcs) pc.Spawn();
        if (frame < 3) return;
        int phase = (frame - 3) / 600;                 // 0 walking replay, 1 standing replay, 2 glide
        bool replayPhase = phase < 2;
        int local = (frame - 3) % 600;
        if (phase == 1 && local == 0) foreach (var pc in pcs) pc.States.Push(Rec(1, 1, pc.Position, default));
        if (phase != 1 && local % 60 == 0)
            for (int i = 0; i < n; i++)
            {
                if (replayPhase) pcs[i].States.Push(Rec(2, 0x18, pcs[i].Position, Dirs[(local / 60 + i) % 4]));
                else { glide.TryGetValue(i, out var g); g.target = glides[i].root.Position + 350f * new Vector3(Dirs[(local / 60 + i) % 4].X, 0, -Dirs[(local / 60 + i) % 4].Y); glide[i] = g; }
            }
        if (phase == 0 && local % 150 == 75) foreach (var pc in pcs) pc.States.Push(Rec(6, 0x21, pc.Position, default));
        if (!replayPhase && local % 60 != 0)
            for (int i = 0; i < n; i++) { glide.TryGetValue(i, out var g); g.target += 350f / 60f * (g.target - glides[i].root.Position).Normalized(); glide[i] = g; }
        if (local == 300) { GC.Collect(); GC.WaitForPendingFinalizers(); frameUs.Clear(); bytes = 0; objects0 = (ulong)Performance.GetMonitor(Performance.Monitor.ObjectCount); }
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        long t0 = Stopwatch.GetTimestamp();
        long fallenBytes = 0;
        for (int i = 0; i < n; i++)
        {
            if (!replayPhase) { Glide(i, Dt); continue; }
            bool below = pcs[i].Position.Z < -20;     // left the test floor (see the summary): its cost is counted apart
            long f0 = below ? GC.GetAllocatedBytesForCurrentThread() : 0;
            pcs[i].Tick(Dt);
            if (below) fallenBytes += GC.GetAllocatedBytesForCurrentThread() - f0;
        }
        double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
        long db = GC.GetAllocatedBytesForCurrentThread() - b0 - fallenBytes;
        if (local >= 300) { frameUs.Add(us); bytes += db; }
        if (local == 599)
        {
            frameUs.Sort();
            double perFrame = bytes / 300.0;
            long objs = (long)(ulong)Performance.GetMonitor(Performance.Monitor.ObjectCount) - (long)objects0;
            int fallen = replayPhase ? pcs.Count(p => p.Position.Z < -20) : 0;
            string model = phase == 0 ? "replay walking" : phase == 1 ? "replay standing" : "glide";
            GD.Print($"remote cost [{model}] {n} remotes: {perFrame:0.0} B/frame, {frameUs[150] / n:0.0} us per remote median, {frameUs[^1] / n:0.0} us worst frame per remote, objects {objs:+0;-0;0}, {fallen} below the floor (not counted)");
            if (phase == 1) replayBytes = perFrame;
            if (phase == 2) GetTree().Quit(replayBytes < 64 ? 0 : 1);
        }
    }
    double replayBytes;
}
